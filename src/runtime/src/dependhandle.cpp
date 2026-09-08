//------------------------------------------------------------------------------
// dependhandle.cpp -- System.Runtime.DependentHandle degraded dependent GC
// handle (see il2cpp_dependhandle.h for the full contract).
//
// The ILC collector has no dependent-handle table, so the semantics are
// simulated with the collector's public primitives:
//
//   * a per-handle PRIMARY weak slot  (il2cpp_gc_add_weak_ref on
//     &g_primaries[i]): the collector clears it when the primary is no longer
//     reachable, giving .NET's "the handle does not keep the target alive".
//   * a SHARED SECONDARY strong root range over the whole secondary array
//     (il2cpp_gc_add_root_range on g_secondaries): every live entry's
//     secondary is a strong root, i.e. the "strong reference table" the
//     degradation calls for.
//   * lazy dependent release: an entry whose primary slot was cleared by a
//     collection drops its secondary (clears the slot) on the next handle
//     operation, so the secondary becomes collectible at the following
//     collection.
//
// The secondary slots MUST be a contiguous array (il2cpp_gc_add_root_range
// contract): the storage is therefore two PARALLEL arrays (primaries +
// secondaries) plus a parallel allocated-flag array, not an array of structs
// (the strided secondary field would break the contiguous-range requirement).
//
// Storage is flat bounded static tables (constraint #12 -- no global-
// constructor allocation), exactly like the safehandle/weakref tables:
// overflow aborts loudly. Single-threaded, matching the rest of the runtime
// (safehandle.h thread-model note).
//------------------------------------------------------------------------------

#include "il2cpp_dependhandle.h"

#include "il2cpp_gc.h"

#include <cstdio>
#include <cstdlib>

namespace il2cpp { namespace runtime {

namespace {

// Flat side table: bounded static storage. Overflow aborts loudly (a
// programming error -- the transpiler emits a bounded set of handles).
constexpr int32_t kMaxDependentHandles = 256;

// Parallel arrays (see the contiguous-range requirement above). All three are
// fixed static storage; entries below g_handleCount are the live region.
Il2CppObject* g_primaries[kMaxDependentHandles];   // per-entry weak slot
Il2CppObject* g_secondaries[kMaxDependentHandles]; // whole array = one root range
bool          g_allocated[kMaxDependentHandles];
int32_t       g_handleCount = 0; // high-water mark (compaction-free append)
bool          g_rootRangeArmed = false;

// The shared secondary root range covers the WHOLE secondary array, including
// never-allocated trailing slots -- their null values are harmless no-ops for
// the mark phase (il2cpp_gc.h root-range contract). Registered once, lazily.
void ArmRootRange()
{
    if (g_rootRangeArmed)
    {
        return;
    }
    il2cpp::il2cpp_gc_add_root_range(
        reinterpret_cast<void**>(&g_secondaries[0]), kMaxDependentHandles);
    g_rootRangeArmed = true;
}

// Decode the opaque handle (table index + 1). Returns -1 for invalid/zero
// handles (defensive; the managed wrapper validates before calling). The
// FULL intptr_t value is range-checked BEFORE truncation: handles are
// encoded as index + 1, so any value outside [1, kMaxDependentHandles] is
// bogus no matter how its low bits decode (a huge 64-bit value would
// otherwise wrap to a plausible in-range index -- handle-spoofing).
int32_t DecodeHandle(intptr_t handle)
{
    if (handle <= 0 || handle > static_cast<intptr_t>(kMaxDependentHandles))
    {
        return -1;
    }
    const int32_t idx = static_cast<int32_t>(handle) - 1;
    if (!g_allocated[idx])
    {
        return -1;
    }
    return idx;
}

// Lazy dependent release: once the primary slot has been cleared (either by a
// collection -- the primary died -- or by set_target_to_null), the secondary
// strong root is dropped (the slot is cleared) so the dependent becomes
// collectible at the next collection. .NET releases the dependent at the SAME
// collection; the degradation releases it on the next handle operation (see
// the documented deviation in il2cpp_dependhandle.h).
void ReleaseDependentIfPrimaryDead(int32_t idx)
{
    if (g_primaries[idx] == nullptr && g_secondaries[idx] != nullptr)
    {
        g_secondaries[idx] = nullptr;
    }
}

} // namespace

intptr_t icall_dependenthandle_alloc(Il2CppObject* primary, Il2CppObject* secondary)
{
    ArmRootRange();
    for (int32_t i = 0; i < kMaxDependentHandles; ++i)
    {
        if (g_allocated[i])
        {
            continue;
        }
        g_primaries[i] = primary;
        // A null primary means the dependent is not tracked (.NET: strong
        // only while the target is non-null) -- the secondary is NOT stored,
        // so it is never retained by the strong root range and stays
        // collectible immediately (new DependentHandle(null, s) does not keep
        // s alive in .NET). Store is atomic in the single-threaded runtime
        // model (the slot is not yet observable).
        g_secondaries[i] = (primary == nullptr) ? nullptr : secondary;
        g_allocated[i] = true;
        if (i + 1 > g_handleCount)
        {
            g_handleCount = i + 1; // extend the high-water mark
        }
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&g_primaries[i]));
        return static_cast<intptr_t>(i) + 1;
    }
    std::fprintf(stderr, "icall_dependenthandle_alloc: handle table overflow\n");
    std::abort();
}

Il2CppObject* icall_dependenthandle_get_target(intptr_t handle)
{
    const int32_t idx = DecodeHandle(handle);
    if (idx < 0)
    {
        return nullptr;
    }
    ReleaseDependentIfPrimaryDead(idx);
    return g_primaries[idx];
}

Il2CppObject* icall_dependenthandle_get_dependent(intptr_t handle)
{
    const int32_t idx = DecodeHandle(handle);
    if (idx < 0)
    {
        return nullptr;
    }
    ReleaseDependentIfPrimaryDead(idx);
    // .NET: the dependent is tracked only while the target is non-null.
    if (g_primaries[idx] == nullptr)
    {
        return nullptr;
    }
    return g_secondaries[idx];
}

Il2CppObject* icall_dependenthandle_get_target_and_dependent(intptr_t handle,
    Il2CppObject** outDependent)
{
    if (outDependent == nullptr)
    {
        std::fprintf(stderr, "icall_dependenthandle_get_target_and_dependent: null out slot\n");
        std::abort();
    }
    const int32_t idx = DecodeHandle(handle);
    if (idx < 0)
    {
        *outDependent = nullptr;
        return nullptr;
    }
    ReleaseDependentIfPrimaryDead(idx);
    *outDependent = g_primaries[idx] == nullptr ? nullptr : g_secondaries[idx];
    return g_primaries[idx];
}

void icall_dependenthandle_set_target_to_null(intptr_t handle)
{
    const int32_t idx = DecodeHandle(handle);
    if (idx < 0)
    {
        return;
    }
    // .NET: setting the target to null makes the dependent start returning
    // null and become eligible for collection -- drop both references now.
    g_primaries[idx] = nullptr;
    g_secondaries[idx] = nullptr;
}

void icall_dependenthandle_set_dependent(intptr_t handle, Il2CppObject* dependent)
{
    const int32_t idx = DecodeHandle(handle);
    if (idx < 0)
    {
        return;
    }
    // .NET: the dependent is tracked only while the target is non-null. A
    // null primary drops the dependent immediately (it stays collectible).
    if (g_primaries[idx] == nullptr)
    {
        g_secondaries[idx] = nullptr;
        return;
    }
    g_secondaries[idx] = dependent;
}

void icall_dependenthandle_set_primary_and_secondary(intptr_t handle,
    Il2CppObject* primary, Il2CppObject* secondary)
{
    const int32_t idx = DecodeHandle(handle);
    if (idx < 0)
    {
        return;
    }
    g_primaries[idx] = primary;
    // The weak slot already holds the new primary value; the collector reads
    // the slot by address each collection, so no re-registration is needed.
    g_secondaries[idx] = secondary;
    // A null primary means the dependent is not tracked (.NET): drop it.
    if (primary == nullptr)
    {
        g_secondaries[idx] = nullptr;
    }
}

bool icall_dependenthandle_free(intptr_t handle)
{
    const int32_t idx = DecodeHandle(handle);
    if (idx < 0)
    {
        return false;
    }
    il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&g_primaries[idx]));
    g_primaries[idx] = nullptr;
    g_secondaries[idx] = nullptr;
    g_allocated[idx] = false;
    return true;
}

//------------------------------------------------------------------------------
// net48 static-extern n* adapters (see the header contract): bridge the
// BCL's void-plus-out-slot shape to the returning core icalls above.
//------------------------------------------------------------------------------
void icall_dependenthandle_n_init(Il2CppObject* primary, Il2CppObject* secondary, int32_t* outHandle)
{
    if (outHandle == nullptr) return;
    *outHandle = static_cast<int32_t>(icall_dependenthandle_alloc(primary, secondary));
}

void icall_dependenthandle_n_get_primary(intptr_t handle, Il2CppObject** outPrimary)
{
    if (outPrimary == nullptr) return;
    *outPrimary = icall_dependenthandle_get_target(handle);
}

void icall_dependenthandle_n_get_primary_and_secondary(intptr_t handle,
    Il2CppObject** outPrimary, Il2CppObject** outSecondary)
{
    if (outPrimary == nullptr || outSecondary == nullptr) return;
    *outPrimary = icall_dependenthandle_get_target_and_dependent(handle, outSecondary);
}

}} // namespace il2cpp::runtime
