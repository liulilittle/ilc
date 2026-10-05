//------------------------------------------------------------------------------
// test_gcapi.cpp -- System.GC icall module unit tests (il2cpp_gc_api.h).
//
// Coverage (normal + edge + error paths):
//   1. icall_gc_collect: callable; the heap stays usable afterwards.
//   2. icall_gc_get_generation: a fresh allocation reports 0; a rooted
//      survivor of one collection reports 1, of two collections 2 (and
//      stays 2); a LOH-sized allocation reports 3 (and stays 3 through
//      collections); null raises the ArgumentNullException carrier
//      (net48 contract: GC.GetGeneration(null) throws).
//   3. icall_gc_keep_alive: a no-op -- callable with an object and with
//      null (net48: GC.KeepAlive(null) is a legal no-op); the object stays
//      intact and is promoted normally by a later collection.
//   4. icall_gc_suppress_finalize / icall_gc_re_register_for_finalize:
//      net48 null contract -- a null argument raises the ArgumentNullException
//      carrier; a non-null object is left intact (the runtime has no
//      finalizer-suppress primitive yet -- a reported collector gap).
//   5. icall_gc_get_total_memory: live-block snapshot -- non-negative on a
//      stable heap, grows by at least the allocated bytes after allocations,
//      falls or stays flat after a collection, and keeps counting rooted
//      survivors (the icall delegates to il2cpp_gc_get_total_memory, which
//      sums the live-block sizes in gc.cpp's block table).
//   6. icall_gc_get_allocated_bytes: monotonic cumulative allocation
//      counter (incremented in gc.cpp's AllocCore) -- callable, non-negative,
//      and it strictly grows with each allocation (delegates to the
//      collector's per-thread counter; the icall is the fix for the old
//      always-0 stub).
//
// GC-safety: an object that must survive a collection is registered as a
// static root FIRST and unregistered after the last use. The icalls do NOT
// root their arguments (native-stack arguments are not scanned), so an
// unrooted local pointer can be reclaimed by a collection -- exactly like
// test_gc.cpp. Roots are rewritten by the compacting collection, so the
// local variable holding a rooted object always points at the CURRENT
// location of its object.
//------------------------------------------------------------------------------

#include "il2cpp_gc_api.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

// The fixtures use pointer-sized slots; verify the platform assumption once.
static_assert(sizeof(void*) == 8, "gcapi test fixture assumes 8-byte pointers");

// Number of failed assertions accumulated by Expect(). main() returns
// non-zero when this counter is not zero, so the test runner and CI observe
// the result via the exit code.
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

// Fixture class A: a 32-byte leaf object (no reference fields).
il2cpp::Il2CppClass g_classA = {
    "Test.GcApiA",
    32,          // instanceSize: header + 3 payload slots (32B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,     // baseClass
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// Fixture LOH class: a 90000-byte instance (>= the 85000-byte LOH
// threshold) so the allocation is routed straight to the LOH (generation 3).
il2cpp::Il2CppClass g_lohClass = {
    "Test.GcApiLoh",
    90000,       // instanceSize: LOH-sized (> 85000)
    0u,
    0,
    nullptr,
    nullptr,
    0,
    nullptr,
    0,
};

} // namespace

//------------------------------------------------------------------------------
// main -- run every System.GC icall invariant and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    // --- 1. icall_gc_collect: callable, heap stays usable ---
    il2cpp::runtime::icall_gc_collect();
    il2cpp::Il2CppObject* o = il2cpp::il2cpp_gc_alloc(&g_classA);
    Expect(o != nullptr && o->klass == &g_classA,
           "heap allocates normally after icall_gc_collect");
    // Root 'o' before the next collection: the icall does not root its
    // argument, and an unrooted object would be reclaimed by the collect.
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&o));
    il2cpp::runtime::icall_gc_collect();
    Expect(o != nullptr && o->klass == &g_classA,
           "a rooted object survives icall_gc_collect intact (root rewritten by compaction)");
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&o));

    // --- 2. icall_gc_get_generation: 0 / 1 / 2 (stays 2) / 3 (LOH) ---
    {
        // A fresh allocation is in generation 0 (no collection in between).
        il2cpp::Il2CppObject* fresh = il2cpp::il2cpp_gc_alloc(&g_classA);
        Expect(il2cpp::runtime::icall_gc_get_generation(fresh) == il2cpp::kGcGeneration0,
               "fresh allocation reports generation 0");
        // Root it, then promote through explicit collections (the root is
        // rewritten by each compacting collection, so the local always
        // points at the current location of the object).
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fresh));
        il2cpp::runtime::icall_gc_collect();
        Expect(il2cpp::runtime::icall_gc_get_generation(fresh) == il2cpp::kGcGeneration1,
               "rooted survivor of one collection reports generation 1");
        il2cpp::runtime::icall_gc_collect();
        Expect(il2cpp::runtime::icall_gc_get_generation(fresh) == il2cpp::kGcGeneration2,
               "rooted survivor of two collections reports generation 2");
        il2cpp::runtime::icall_gc_collect();
        Expect(il2cpp::runtime::icall_gc_get_generation(fresh) == il2cpp::kGcGeneration2,
               "generation 2 object stays at generation 2 (oldest small-object gen)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fresh));
    }
    {
        // LOH-sized allocation: generation 3, never promoted.
        il2cpp::Il2CppObject* loh = il2cpp::il2cpp_gc_alloc(&g_lohClass);
        Expect(loh != nullptr, "LOH-sized allocation succeeds");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&loh));
        Expect(il2cpp::runtime::icall_gc_get_generation(loh) == il2cpp::kGcGenerationLoh,
               "LOH-sized allocation reports generation 3");
        il2cpp::runtime::icall_gc_collect();
        Expect(il2cpp::runtime::icall_gc_get_generation(loh) == il2cpp::kGcGenerationLoh,
               "LOH object stays on the LOH after a collection (no promotion)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&loh));
    }
    {
        // null -> ArgumentNullException (net48: GC.GetGeneration(null) throws).
        bool threwArgumentNull = false;
        try
        {
            il2cpp::runtime::icall_gc_get_generation(nullptr);
        }
        catch (const il2cpp::DotNetException& e)
        {
            threwArgumentNull = std::strcmp(e.type_name(), "System.ArgumentNullException") == 0;
        }
        Expect(threwArgumentNull, "icall_gc_get_generation(null) raises ArgumentNullException");
    }

    // --- 3. icall_gc_keep_alive: no-op, object stays intact ---
    {
        il2cpp::Il2CppObject* keep = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keep));
        il2cpp::runtime::icall_gc_keep_alive(keep);
        il2cpp::runtime::icall_gc_keep_alive(nullptr); // net48: KeepAlive(null) is legal
        Expect(il2cpp::runtime::icall_gc_get_generation(keep) == il2cpp::kGcGeneration0,
               "KeepAlive leaves the object intact (still generation 0)");
        il2cpp::runtime::icall_gc_collect();
        Expect(il2cpp::runtime::icall_gc_get_generation(keep) == il2cpp::kGcGeneration1,
               "KeepAlive does not disturb the collector (survivor promoted normally)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keep));
    }

    // --- 4. icall_gc_suppress_finalize / re_register_for_finalize ---
    // net48 null contract: both icalls throw ArgumentNullException for null.
    // Non-null objects stay intact (true suppression is a reported collector
    // gap -- the icalls are safe no-ops for non-null for now).
    {
        il2cpp::Il2CppObject* fin = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fin));

        bool supNullThrew = false;
        try
        {
            il2cpp::runtime::icall_gc_suppress_finalize(nullptr);
        }
        catch (const il2cpp::DotNetException& e)
        {
            supNullThrew = std::strcmp(e.type_name(), "System.ArgumentNullException") == 0;
        }
        Expect(supNullThrew, "icall_gc_suppress_finalize(null) raises ArgumentNullException (net48)");

        bool regNullThrew = false;
        try
        {
            il2cpp::runtime::icall_gc_re_register_for_finalize(nullptr);
        }
        catch (const il2cpp::DotNetException& e)
        {
            regNullThrew = std::strcmp(e.type_name(), "System.ArgumentNullException") == 0;
        }
        Expect(regNullThrew, "icall_gc_re_register_for_finalize(null) raises ArgumentNullException (net48)");

        il2cpp::runtime::icall_gc_suppress_finalize(fin);
        il2cpp::runtime::icall_gc_re_register_for_finalize(fin);
        Expect(il2cpp::runtime::icall_gc_get_generation(fin) == il2cpp::kGcGeneration0,
               "finalize null-throw icalls leave a non-null object intact");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fin));
    }

    // --- 5. icall_gc_get_total_memory: live-block bytes, snapshot semantics ---
    {
        // Baseline: a stable heap reports a non-negative live-block total.
        const int64_t memBefore = il2cpp::runtime::icall_gc_get_total_memory();
        Expect(memBefore >= 0, "GetTotalMemory returns a non-negative value");
        // Allocate three 32-byte objects (no roots): the live-block total
        // must grow by at least the allocated bytes.
        il2cpp::Il2CppObject* u1 = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::Il2CppObject* u2 = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::Il2CppObject* u3 = il2cpp::il2cpp_gc_alloc(&g_classA);
        const int64_t memAfterAlloc = il2cpp::runtime::icall_gc_get_total_memory();
        Expect(memAfterAlloc >= memBefore + 3 * static_cast<int64_t>(g_classA.instanceSize),
               "GetTotalMemory grows by at least the allocated bytes");
        // The unrooted allocations are reclaimed by the collection: the
        // total falls or stays flat (it never grows by collecting garbage).
        il2cpp::runtime::icall_gc_collect();
        const int64_t memAfterCollect = il2cpp::runtime::icall_gc_get_total_memory();
        Expect(memAfterCollect <= memAfterAlloc,
               "GetTotalMemory falls or stays flat after a collection");
        // A rooted survivor keeps its bytes live across the collection.
        il2cpp::Il2CppObject* keep = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keep));
        il2cpp::runtime::icall_gc_collect();
        const int64_t memWithSurvivor = il2cpp::runtime::icall_gc_get_total_memory();
        Expect(memWithSurvivor >= memAfterCollect + static_cast<int64_t>(g_classA.instanceSize),
               "GetTotalMemory counts a rooted survivor across a collection");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keep));
        (void)u1;
        (void)u2;
        (void)u3;
    }

    // --- 6. icall_gc_get_allocated_bytes: real cumulative counter ---
    {
        // Non-negative on entry (the process may have allocated via the
        // fixtures above; the counter never goes negative).
        Expect(il2cpp::runtime::icall_gc_get_allocated_bytes() >= 0,
               "GetAllocatedBytesForCurrentThread returns a non-negative value");
        // Monotonic growth: the icall delegates to the collector's per-thread
        // counter (gc.cpp AllocCore g_allocatedBytes). Each allocation of the
        // 32-byte fixture must be reflected: the value strictly grows.
        const int64_t before = il2cpp::runtime::icall_gc_get_allocated_bytes();
        const int64_t collectorBefore = il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread();
        Expect(before == collectorBefore,
               "GetAllocatedBytesForCurrentThread matches the collector's per-thread counter");
        il2cpp::Il2CppObject* u = il2cpp::il2cpp_gc_alloc(&g_classA);
        const int64_t after = il2cpp::runtime::icall_gc_get_allocated_bytes();
        Expect(after > before,
               "GetAllocatedBytesForCurrentThread grows after an allocation");
        Expect(after - before >= static_cast<int64_t>(g_classA.instanceSize),
               "GetAllocatedBytesForCurrentThread grows by at least the allocated bytes");
        // A collection does NOT reset the counter (cumulative, monotonic).
        il2cpp::runtime::icall_gc_collect();
        Expect(il2cpp::runtime::icall_gc_get_allocated_bytes() >= after,
               "GetAllocatedBytesForCurrentThread is not reset by a collection");
        (void)u;
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d gcapi assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime gcapi tests passed\n");
    return 0;
}
