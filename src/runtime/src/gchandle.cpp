//------------------------------------------------------------------------------
// gchandle.cpp -- System.Runtime.InteropServices.GCHandle icall domain (see
// il2cpp_gchandle.h). The net48 GCHandle handle semantics on the runtime's
// existing handle-table model.
//
// Handle table: a static C++ array of entries (non-moving memory, single-
// threaded by contract -- the same contract as the gcext weak-target table).
// Each entry holds the referent (Il2CppObject*) and its GCHandleType. The
// collector is handed the entry's referent SLOT ADDRESS:
//   * Normal / Pinned  -> il2cpp_gc_add_static_root -- the referent stays
//     alive and the slot follows the Gen0 compaction (roots are rewritten
//     through the forward map, gc.cpp Phase C).
//   * Weak / WeakTrack -> il2cpp_gc_add_weak_ref -- the slot is cleared when
//     the referent dies and rewritten when a live referent moves
//     (gc.cpp UpdateExternalReferences).
// The slot address MUST NOT live inside a managed object (the compacting
// collection copies every surviving Gen0 object) -- the static table entry
// is the authoritative referent location, exactly like gcext.
//
// Capacity: kMaxGcHandleSlots = 64. This is the shared table for all four
// handle types, AND every Normal/Pinned handle consumes one slot of the
// collector's kMaxStaticRoots (128) -- the bound is documented in the
// header so callers know a strong-handle burst is bounded by the root table,
// not by this table.
//------------------------------------------------------------------------------

#include "il2cpp_gchandle.h"

#include "il2cpp_array.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// Shared handle table bound. 64 slots is generous for ILC's scope and keeps
// the worst-case strong-handle burst (64 static roots) well inside the
// collector's kMaxStaticRoots = 128.
constexpr int32_t kMaxGcHandleSlots = 64;

struct GcHandleEntry
{
    Il2CppObject* target; // referent (null when cleared / freed)
    int32_t       type;   // one of kGCHandleType*
    bool          inUse;  // slot currently allocated
};

GcHandleEntry g_handles[kMaxGcHandleSlots];
int32_t       g_handleFree[kMaxGcHandleSlots];
int32_t       g_handleFreeCount = 0;
bool          g_handleTableArmed = false;

void GcHandleFailLoud(const char* what)
{
    std::fprintf(stderr, "il2cpp_gchandle: %s\n", what);
    std::abort();
}

void GcHandleEnsureReady()
{
    if (g_handleTableArmed)
    {
        return;
    }
    g_handleTableArmed = true;
    // First use: push every index onto the free stack (descending, so the
    // first allocation pops index 0).
    for (int32_t i = kMaxGcHandleSlots - 1; i >= 0; --i)
    {
        g_handleFree[g_handleFreeCount++] = i;
    }
}

// Handle value -> table index. Handles are (index+1) << 1: positive, bit 0
// clear, 0 never valid (the netcore InternalGCHandle pin-flag convention).
bool IsValidHandle(int32_t handle)
{
    if (handle <= 0 || (handle & 1) != 0)
    {
        return false;
    }
    const int32_t idx = (handle >> 1) - 1;
    return idx >= 0 && idx < kMaxGcHandleSlots && g_handles[idx].inUse;
}

GcHandleEntry* EntryFromHandle(int32_t handle)
{
    if (!IsValidHandle(handle))
    {
        return nullptr;
    }
    return &g_handles[(handle >> 1) - 1];
}

int32_t AllocSlot()
{
    GcHandleEnsureReady();
    if (g_handleFreeCount == 0)
    {
        GcHandleFailLoud("GCHandle table overflow");
    }
    const int32_t idx = g_handleFree[--g_handleFreeCount];
    g_handles[idx] = GcHandleEntry{};
    return idx;
}

// Unregister the collector slot and return the table slot to the free list.
// Idempotent teardown is the CALLER's job (free validates first); this is
// the single release point for the alloc's registration (1:1 pairing).
void ReleaseSlot(int32_t idx)
{
    GcHandleEntry& e = g_handles[idx];
    if (e.type == kGCHandleTypeNormal || e.type == kGCHandleTypePinned)
    {
        if (e.type == kGCHandleTypePinned)
        {
            // Unregister the immovable slot (1:1 pairing with the alloc's
            // registration). The referent may be collected/moved again once
            // the pin is released.
            il2cpp::il2cpp_gc_remove_pinned(reinterpret_cast<void**>(&e.target));
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&e.target));
    }
    else
    {
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&e.target));
    }
    e.target = nullptr;
    e.type = 0;
    e.inUse = false;
    g_handleFree[g_handleFreeCount++] = idx;
}

} // namespace

//------------------------------------------------------------------------------
// icall_gchandle_alloc -- GCHandle.Alloc(object, GCHandleType).
//------------------------------------------------------------------------------
int32_t icall_gchandle_alloc(Il2CppObject* value, int32_t type)
{
    if (type < kGCHandleTypeWeak || type > kGCHandleTypePinned)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "type", "GCHandleType was not a valid enum value.");
    }
    const int32_t idx = AllocSlot();
    GcHandleEntry& e = g_handles[idx];
    e.target = value;
    e.type = type;
    e.inUse = true;
    if (type == kGCHandleTypeNormal || type == kGCHandleTypePinned)
    {
        // Strong: keep the referent alive; the slot follows the Gen0
        // compaction (roots are rewritten through the forward map).
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&e.target));
        if (type == kGCHandleTypePinned)
        {
            // Immovable: the collector must NOT move the referent during the
            // Gen0 compaction, so the address AddrOfPinnedObject returns stays
            // valid across collections (the GCHandle.Pinned contract).
            il2cpp::il2cpp_gc_add_pinned(reinterpret_cast<void**>(&e.target));
        }
    }
    else
    {
        // Weak (incl. WeakTrackResurrection simplification): cleared when
        // the referent dies, rewritten when a live referent moves.
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&e.target));
    }
    return (idx + 1) << 1;
}

//------------------------------------------------------------------------------
// icall_gchandle_alloc_normal -- GCHandle.Alloc(object): alloc with Normal.
//------------------------------------------------------------------------------
int32_t icall_gchandle_alloc_normal(Il2CppObject* value)
{
    return icall_gchandle_alloc(value, kGCHandleTypeNormal);
}

//------------------------------------------------------------------------------
// icall_gchandle_free -- GCHandle.Free. Invalid / already-freed handles
// throw InvalidOperationException (net48 double-free semantics).
//------------------------------------------------------------------------------
void icall_gchandle_free(int32_t handle)
{
    GcHandleEntry* entry = EntryFromHandle(handle);
    if (entry == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The handle is not initialized.");
    }
    ReleaseSlot(static_cast<int32_t>(entry - g_handles));
}

//------------------------------------------------------------------------------
// icall_gchandle_get_target -- GCHandle.get_Target.
//------------------------------------------------------------------------------
Il2CppObject* icall_gchandle_get_target(int32_t handle)
{
    const GcHandleEntry* entry = EntryFromHandle(handle);
    if (entry == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The handle is not initialized.");
    }
    return entry->target;
}

//------------------------------------------------------------------------------
// icall_gchandle_set_target -- GCHandle.set_Target. The handle's type stays
// fixed (net48 semantics); the collector registration already covers the
// slot, so storing the new referent is the whole update (no re-register).
//------------------------------------------------------------------------------
void icall_gchandle_set_target(int32_t handle, Il2CppObject* value)
{
    GcHandleEntry* entry = EntryFromHandle(handle);
    if (entry == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The handle is not initialized.");
    }
    entry->target = value;
}

//------------------------------------------------------------------------------
// icall_gchandle_get_handle_type -- net48 InternalGetHandleType (the IsPinned
// semantics live on top: type == kGCHandleTypePinned).
//------------------------------------------------------------------------------
int32_t icall_gchandle_get_handle_type(int32_t handle)
{
    const GcHandleEntry* entry = EntryFromHandle(handle);
    if (entry == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The handle is not initialized.");
    }
    return entry->type;
}

//------------------------------------------------------------------------------
// icall_gchandle_is_allocated -- GCHandle.get_IsAllocated. Garbage handle
// values (0, negative, freed slots) are simply not allocated -- no throw
// (net48 IsAllocated never throws).
//------------------------------------------------------------------------------
bool icall_gchandle_is_allocated(int32_t handle)
{
    return IsValidHandle(handle);
}

//------------------------------------------------------------------------------
// icall_gchandle_addr_of_pinned -- GCHandle.AddrOfPinnedObject. The pinned
// object's data address (see the header for the per-kind offset rule). PIN
// LIMITATION: ILC has no real pinning, so the address is only valid until
// the next collection (same documented limitation as
// icall_marshal_unsafe_addr_of_pinned_array_element).
//------------------------------------------------------------------------------
intptr_t icall_gchandle_addr_of_pinned(int32_t handle)
{
    const GcHandleEntry* entry = EntryFromHandle(handle);
    if (entry == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The handle is not initialized.");
    }
    if (entry->type != kGCHandleTypePinned)
    {
        il2cpp_throw_invalid_operation_exception("The handle is not pinned.");
    }
    Il2CppObject* target = entry->target;
    if (target == nullptr)
    {
        return 0;
    }
    // Strings expose their payload at &chars[0]; arrays at &data[0] (the
    // array-class marker prefix '[' identifies an array descriptor); any
    // other object's raw data starts right after the klass header.
    if (target->klass == il2cpp::il2cpp_string_class())
    {
        Il2CppString* str = reinterpret_cast<Il2CppString*>(target);
        return static_cast<intptr_t>(
            reinterpret_cast<uintptr_t>(str->chars));
    }
    const char* name = (target->klass != nullptr) ? target->klass->name : nullptr;
    if (name != nullptr && name[0] == '[')
    {
        Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(target);
        return static_cast<intptr_t>(
            reinterpret_cast<uintptr_t>(arr->data));
    }
    return static_cast<intptr_t>(
        reinterpret_cast<uintptr_t>(target) + sizeof(Il2CppObject));
}

} // namespace runtime
} // namespace il2cpp
 
