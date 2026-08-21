//------------------------------------------------------------------------------
// test_dependhandle.cpp -- System.Runtime.DependentHandle degraded semantics
// unit tests (see il2cpp_dependhandle.h for the contract).
//
// Coverage (normal + edge + error + GC semantics):
//   1. alloc returns a non-zero handle; get_target / get_dependent /
//      get_target_and_dependent return the stored primary/secondary.
//   2. set_dependent replaces the secondary; the primary is unchanged.
//   3. set_target_to_null makes the primary AND dependent return null and
//      drops both references (eligible for collection).
//   4. free returns true and the handle becomes invalid (reads null, double
//      free returns false); the slot is reusable by a later alloc.
//   5. A null primary at alloc: the handle is still allocated but the
//      dependent is not tracked (get_dependent returns null, .NET semantics).
//   6. set_dependent on a null-primary handle does NOT retain the dependent.
//   7. GC, primary rooted: a collection keeps the primary alive (the root is
//      rewritten through compaction) AND keeps the secondary alive -- the
//      strong root range is the whole degradation point. The handle's
//      internal slots follow the compaction (compare against rooted/weak
//      probe addresses).
//   8. GC, primary NOT rooted: the primary dies (the handle does not keep the
//      target alive -- .NET semantics). The secondary is still kept alive by
//      the root range until the next handle operation, which observes the
//      dead primary and releases the dependent; the FOLLOWING collection then
//      reclaims the secondary (documented degradation).
//   9. GC, freed handle: both the primary and the secondary become
//      collectible after free (the weak slot is unregistered, both slots are
//      cleared).
//  10. Invalid-handle edge paths: zero handle and out-of-range handles are
//      defensive (reads null, writes no-op, free false).
//
// GC-safety: objects that must survive a collection are registered as static
// roots FIRST and unregistered after the last use (the compacting collection
// rewrites the root slot, so the local always holds the CURRENT address).
// Objects under test for death are probed with registered WEAK slots (cleared
// on death, rewritten on movement) -- never compared by stale local address.
//------------------------------------------------------------------------------

#include "il2cpp_dependhandle.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h" // Il2CppClass (complete definition for the fixture)
#include "il2cpp_object.h"

#include <cstdint>
#include <cstdio>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Fixture class: a 32-byte leaf object (no reference fields) -- the same
// pattern as test_gcext.cpp. Strings would work too, but a leaf object keeps
// the GC semantics (death/liveness) fully deterministic.
il2cpp::Il2CppClass g_leaf = {
    "Test.DependentHandle.Leaf",
    32,          // instanceSize: header + 3 payload slots (32B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,     // baseClass
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

} // namespace

int main()
{
    using il2cpp::runtime::icall_dependenthandle_alloc;
    using il2cpp::runtime::icall_dependenthandle_get_target;
    using il2cpp::runtime::icall_dependenthandle_get_dependent;
    using il2cpp::runtime::icall_dependenthandle_get_target_and_dependent;
    using il2cpp::runtime::icall_dependenthandle_set_target_to_null;
    using il2cpp::runtime::icall_dependenthandle_set_dependent;
    using il2cpp::runtime::icall_dependenthandle_set_primary_and_secondary;
    using il2cpp::runtime::icall_dependenthandle_free;

    // --- 1. alloc + readback (no collection: addresses are stable) ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const intptr_t h = icall_dependenthandle_alloc(p, s);
        Expect(h != 0, "alloc returns a non-zero handle");
        Expect(icall_dependenthandle_get_target(h) == p,
               "get_target returns the primary");
        Expect(icall_dependenthandle_get_dependent(h) == s,
               "get_dependent returns the secondary while the primary is alive");
        il2cpp::Il2CppObject* out = nullptr;
        il2cpp::Il2CppObject* prim = icall_dependenthandle_get_target_and_dependent(h, &out);
        Expect(prim == p && out == s,
               "get_target_and_dependent returns both (primary as result)");
        icall_dependenthandle_free(h);
    }

    // --- 2. set_dependent replaces the secondary ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s1 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s2 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const intptr_t h = icall_dependenthandle_alloc(p, s1);
        icall_dependenthandle_set_dependent(h, s2);
        Expect(icall_dependenthandle_get_dependent(h) == s2,
               "set_dependent replaces the secondary");
        Expect(icall_dependenthandle_get_target(h) == p,
               "set_dependent leaves the primary unchanged");
        icall_dependenthandle_free(h);
    }

    // --- 3. set_target_to_null drops both references ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const intptr_t h = icall_dependenthandle_alloc(p, s);
        icall_dependenthandle_set_target_to_null(h);
        Expect(icall_dependenthandle_get_target(h) == nullptr,
               "set_target_to_null makes get_target return null");
        Expect(icall_dependenthandle_get_dependent(h) == nullptr,
               "set_target_to_null makes get_dependent return null (.NET semantics)");
        il2cpp::Il2CppObject* out = p;
        Expect(icall_dependenthandle_get_target_and_dependent(h, &out) == nullptr && out == nullptr,
               "set_target_to_null makes get_target_and_dependent return (null, null)");
        icall_dependenthandle_free(h);
    }

    // --- 4. free + slot reuse ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const intptr_t h = icall_dependenthandle_alloc(p, s);
        Expect(icall_dependenthandle_free(h) == true, "free returns true on a live handle");
        Expect(icall_dependenthandle_get_target(h) == nullptr,
               "a freed handle reads null (defensive)");
        Expect(icall_dependenthandle_free(h) == false,
               "double free returns false (idempotent teardown)");
        const intptr_t h2 = icall_dependenthandle_alloc(p, s);
        Expect(h2 != 0, "the freed slot is reusable by a later alloc");
        Expect(icall_dependenthandle_get_target(h2) == p,
               "the reused slot reads its new primary correctly");
        icall_dependenthandle_free(h2);
    }

    // --- 5. null primary: handle allocated, dependent not tracked ---
    {
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const intptr_t h = icall_dependenthandle_alloc(nullptr, s);
        Expect(h != 0, "alloc with a null primary still allocates a handle");
        Expect(icall_dependenthandle_get_target(h) == nullptr,
               "get_target returns null for a null primary");
        Expect(icall_dependenthandle_get_dependent(h) == nullptr,
               "the dependent is not tracked while the primary is null (.NET)");
        il2cpp::Il2CppObject* out = s;
        Expect(icall_dependenthandle_get_target_and_dependent(h, &out) == nullptr && out == nullptr,
               "get_target_and_dependent returns (null, null) for a null primary");
        icall_dependenthandle_free(h);
    }

    // --- 6. set_dependent on a null-primary handle does NOT retain ---
    {
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const intptr_t h = icall_dependenthandle_alloc(nullptr, s);
        icall_dependenthandle_set_dependent(h, s);
        Expect(icall_dependenthandle_get_dependent(h) == nullptr,
               "set_dependent on a null-primary handle does not retain the dependent");
        icall_dependenthandle_free(h);
    }

    // --- 6.5 combined setter: replaces primary AND secondary ---
    {
        il2cpp::Il2CppObject* p1 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s1 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* p2 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s2 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const intptr_t h = icall_dependenthandle_alloc(p1, s1);
        icall_dependenthandle_set_primary_and_secondary(h, p2, s2);
        Expect(icall_dependenthandle_get_target(h) == p2,
               "set_primary_and_secondary replaces the primary");
        Expect(icall_dependenthandle_get_dependent(h) == s2,
               "set_primary_and_secondary replaces the secondary");
        // A null primary in the combined setter drops the dependent.
        icall_dependenthandle_set_primary_and_secondary(h, nullptr, s2);
        Expect(icall_dependenthandle_get_target(h) == nullptr &&
               icall_dependenthandle_get_dependent(h) == nullptr,
               "a null primary in set_primary_and_secondary drops both references");
        icall_dependenthandle_free(h);
    }

    // --- 7. GC, primary rooted: both survive; handle slots follow compaction ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        // p: strong static root (rewritten through compaction -- the local
        // holds the CURRENT address after every collection).
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&p));
        // s: weak probe (rewritten on movement, cleared on death) -- the
        // ONLY way to compare the handle's internal slot against a live
        // address after a compaction (local addresses go stale).
        il2cpp::Il2CppObject* sProbe = s;
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&sProbe));
        const intptr_t h = icall_dependenthandle_alloc(p, s);
        il2cpp::il2cpp_gc_collect(); // Gen0 compaction moves both objects
        Expect(icall_dependenthandle_get_target(h) == p,
               "a rooted primary survives and its handle slot follows the compaction");
        Expect(sProbe != nullptr,
               "the secondary is kept alive while the primary lives (root range)");
        Expect(icall_dependenthandle_get_dependent(h) == sProbe,
               "the handle's secondary slot follows the compaction (matches the weak probe)");
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&sProbe));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&p));
        icall_dependenthandle_free(h);
    }

    // --- 8. GC, primary NOT rooted: primary dies; dependent released lazily ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* pProbe = p; // weak: cleared when p dies
        il2cpp::Il2CppObject* sProbe = s; // weak: follows s, cleared when s dies
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&pProbe));
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&sProbe));
        const intptr_t h = icall_dependenthandle_alloc(p, s);
        // Neither object is rooted outside the handle. The dependent handle
        // keeps p weak (dies) and s strong (root range) -- .NET semantics.
        il2cpp::il2cpp_gc_collect();
        Expect(pProbe == nullptr,
               "an unrooted primary dies: the handle does not keep the target alive (.NET)");
        Expect(sProbe != nullptr,
               "the secondary is STILL alive after the primary dies (strong root range)");
        Expect(icall_dependenthandle_get_target(h) == nullptr,
               "get_target returns null after the primary died");
        // The get_target above observed the dead primary and released the
        // dependent slot. The FOLLOWING collection reclaims the secondary.
        il2cpp::il2cpp_gc_collect();
        Expect(sProbe == nullptr,
               "the secondary is reclaimed by the collection after the release (degradation)");
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&pProbe));
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&sProbe));
        icall_dependenthandle_free(h);
    }

    // --- 9. GC, freed handle: both objects become collectible ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* pProbe = p;
        il2cpp::Il2CppObject* sProbe = s;
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&pProbe));
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&sProbe));
        const intptr_t h = icall_dependenthandle_alloc(p, s);
        icall_dependenthandle_free(h); // both slots cleared, weak slot unregistered
        il2cpp::il2cpp_gc_collect();
        Expect(pProbe == nullptr && sProbe == nullptr,
               "after free, both the primary and the secondary are collectible");
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&pProbe));
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&sProbe));
    }

    // --- 10. invalid-handle edge paths (defensive) ---
    {
        Expect(icall_dependenthandle_get_target(0) == nullptr,
               "get_target on a zero handle reads null");
        Expect(icall_dependenthandle_get_dependent(0) == nullptr,
               "get_dependent on a zero handle reads null");
        il2cpp::Il2CppObject* out = nullptr;
        Expect(icall_dependenthandle_get_target_and_dependent(0, &out) == nullptr && out == nullptr,
               "get_target_and_dependent on a zero handle reads (null, null)");
        icall_dependenthandle_set_target_to_null(0); // must not crash
        icall_dependenthandle_set_dependent(0, reinterpret_cast<il2cpp::Il2CppObject*>(&g_leaf));
        Expect(icall_dependenthandle_free(0) == false,
               "free on a zero handle returns false");
        // Out-of-range handle (index beyond the table).
        Expect(icall_dependenthandle_get_target(1LL << 40) == nullptr,
               "get_target on an out-of-range handle reads null (defensive)");
        Expect(icall_dependenthandle_free(1LL << 40) == false,
               "free on an out-of-range handle returns false");
    }

    // --- 11. GC, null primary at alloc: the secondary is NOT retained ---
    {
        il2cpp::Il2CppObject* s = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* sProbe = s; // weak: follows s, cleared when s dies
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&sProbe));
        const intptr_t h = icall_dependenthandle_alloc(nullptr, s);
        Expect(h != 0, "null-primary alloc still allocates a handle");
        // .NET: new DependentHandle(null, s) does NOT keep s alive -- the
        // dependent is strong only while the target is non-null, so s must be
        // collectible immediately (the secondary is not stored in the strong
        // root range).
        il2cpp::il2cpp_gc_collect();
        Expect(sProbe == nullptr,
               "a null-primary alloc does not retain the secondary (collectible immediately)");
        Expect(icall_dependenthandle_get_target(h) == nullptr &&
                   icall_dependenthandle_get_dependent(h) == nullptr,
               "null-primary handle reads (null, null) after the collection");
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&sProbe));
        icall_dependenthandle_free(h);
    }

    // --- 12. handle-spoofing: a huge 64-bit handle wrapping to an in-range
    // index is rejected (the full intptr_t is range-checked before the int32
    // truncation) ---
    {
        il2cpp::Il2CppObject* p = il2cpp::il2cpp_gc_alloc(&g_leaf);
        // Four live handles occupy indices 0..3 (handle values 1..4); slot 3
        // (handle value 4) is occupied so a wrap to index 3 would be plausible.
        const intptr_t h1 = icall_dependenthandle_alloc(p, p);
        const intptr_t h2 = icall_dependenthandle_alloc(p, p);
        const intptr_t h3 = icall_dependenthandle_alloc(p, p);
        const intptr_t h4 = icall_dependenthandle_alloc(p, p);
        Expect(h4 == 4, "the fourth dependent handle sits at index 3 (handle value 4)");
        // 2^32 + 4 truncates to int32 4 -> decodes to index 3 under the OLD
        // bounds check (truncate-then-check); the full-value check rejects it.
        Expect(icall_dependenthandle_get_target((1LL << 32) + 4) == nullptr,
               "a huge handle wrapping to an occupied index is rejected (defensive)");
        Expect(icall_dependenthandle_free((1LL << 32) + 4) == false,
               "free on a wrapping huge handle returns false");
        Expect(icall_dependenthandle_get_dependent((1LL << 32) + 4) == nullptr,
               "get_dependent on a wrapping huge handle reads null");
        icall_dependenthandle_free(h1);
        icall_dependenthandle_free(h2);
        icall_dependenthandle_free(h3);
        icall_dependenthandle_free(h4);
    }

    if (g_failures != 0)
    {
        std::printf("%d dependenthandle assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime dependenthandle tests passed\n");
    return 0;
}
