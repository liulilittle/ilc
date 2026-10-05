//------------------------------------------------------------------------------
// test_gc_getters.cpp -- GC getter unit tests (GC statistics accessors).
//
// Coverage (normal + edge paths):
//   1. il2cpp_gc_get_max_generation(): the maximum small-object generation is
//      non-negative and equals the collector's oldest small-object generation
//      (Gen2 -- LOH objects are 3 but MaxGeneration reports the small-object
//      maximum), the GC.MaxGeneration contract.
//   2. il2cpp_gc_get_allocated_bytes_for_current_thread(): after an allocation
//      the cumulative per-thread counter is positive; a fresh allocation grows
//      it strictly; and (single-threaded collector) it coincides with the
//      process-wide cumulative counter il2cpp_gc_get_allocated_bytes().
//   3. GC.GetTotalMemory path: the exposed icall icall_gc_get_total_memory()
//      delegates to il2cpp_gc_get_total_memory() (values identical); the live
//      heap is positive after an allocation; and a rooted survivor of a
//      collection keeps at least its own block size in the live-heap total.
//
// Like test_gc.cpp this is a plain executable with a minimal assertion harness
// (no third-party framework); any failed Expect() prints FAIL, bumps a counter,
// and the process exits non-zero so CI sees a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_gc.h"
#include "il2cpp_gc_api.h"
#include "il2cpp_metadata.h"

#include <cstdint>
#include <cstdio>

namespace {

// Number of failed assertions accumulated by Expect(). main() returns non-zero
// when this counter is not zero, so the test runner and CI observe the result
// via the exit code.
int g_failures = 0;

// Expect -- evaluate a single invariant. On failure, name the violated check
// so a regression is diagnosable from the printed line alone.
void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Fixture class: a 16-byte object with no reference fields (the same shape as
// test_gc.cpp's g_classC) -- header + 1 payload slot, a leaf during marking.
il2cpp::Il2CppClass g_fixtureClass = {
    "Test.GcGetters",
    16,          // instanceSize: header + 1 payload slot (16B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

} // namespace

//------------------------------------------------------------------------------
// main -- run every GC-getter invariant and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    // --- 1. Allocation first: every counter below observes a non-empty heap ---
    il2cpp::Il2CppObject* obj = il2cpp::il2cpp_gc_alloc(&g_fixtureClass);
    Expect(obj != nullptr, "gc allocation returns a non-null object");
    Expect(obj->klass == &g_fixtureClass, "gc object klass header matches the requested class");

    // --- 2. GC.MaxGeneration: il2cpp_gc_get_max_generation() ---
    {
        const int32_t maxGen = il2cpp::il2cpp_gc_get_max_generation();
        Expect(maxGen >= 0, "max generation is non-negative");
        Expect(maxGen == il2cpp::kGcGeneration2,
               "max generation reports the oldest small-object generation (2)");
        // Stable across calls: the generation count never changes at runtime.
        Expect(il2cpp::il2cpp_gc_get_max_generation() == maxGen,
               "max generation is stable across repeated queries");
    }

    // --- 3. GC.GetAllocatedBytesForCurrentThread:
    //        il2cpp_gc_get_allocated_bytes_for_current_thread() ---
    {
        const int64_t threadBytes = il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread();
        Expect(threadBytes > 0, "per-thread allocated bytes are positive after an allocation");
        // Single-threaded collector: "current thread" == the process-wide
        // cumulative counter (identical to il2cpp_gc_get_allocated_bytes).
        Expect(threadBytes == il2cpp::il2cpp_gc_get_allocated_bytes(),
               "per-thread allocation counter equals the cumulative counter");
        // Monotonic: a fresh allocation strictly grows the counter.
        const int64_t before = il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread();
        il2cpp::Il2CppObject* extra = il2cpp::il2cpp_gc_alloc(&g_fixtureClass);
        Expect(extra != nullptr, "second gc allocation returns a non-null object");
        Expect(il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread() > before,
               "per-thread allocation counter grows after a fresh allocation");
    }

    // --- 4. GC.GetTotalMemory path: icall_gc_get_total_memory() ---
    {
        // The exposed icall delegates to the collector accessor: identical values.
        Expect(il2cpp::runtime::icall_gc_get_total_memory() ==
                   il2cpp::il2cpp_gc_get_total_memory(),
               "GetTotalMemory icall matches the collector's live-heap total");
        Expect(il2cpp::runtime::icall_gc_get_total_memory() > 0,
               "live-heap total is positive after an allocation");

        // A rooted survivor keeps at least its own block size live across a
        // collection (alloc and add_static_root must be adjacent -- an implicit
        // alloc-triggered collect would reclaim an unrooted fresh object).
        il2cpp::Il2CppObject* rooted = il2cpp::il2cpp_gc_alloc(&g_fixtureClass);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rooted));
        il2cpp::il2cpp_gc_collect();
        Expect(rooted != nullptr && rooted->klass == &g_fixtureClass,
               "rooted object survives collection");
        Expect(il2cpp::runtime::icall_gc_get_total_memory() >= g_fixtureClass.instanceSize,
               "live-heap total covers the rooted survivor's block after collection");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&rooted));
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d gc getter assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime gc getter tests passed\n");
    return 0;
}
