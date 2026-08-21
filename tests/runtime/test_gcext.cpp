//------------------------------------------------------------------------------
// test_gcext.cpp -- GC extension unit tests: heap statistics accessors,
// weak references and finalization (see il2cpp_gc.h extension + il2cpp_gc_ext.h).
//
// Coverage (normal + edge + error paths):
//   1. Heap statistics: il2cpp_gc_get_total_memory reports live-block bytes
//      (> 0 with a rooted survivor, after allocations); the cumulative
//      il2cpp_gc_get_allocated_bytes counter strictly increases with each
//      allocation.
//   2. Weak references, live target: the slot keeps pointing at its target
//      AND follows it through the Gen0 compaction (the collector rewrites
//      the slot to the object's new address via the forward map -- this is
//      the only thing that makes the address comparison with the rooted
//      value meaningful).
//   3. Weak references, dead target: the slot is cleared to null at the
//      collection where the target dies, and stays null afterwards.
//   4. Weak references, edge paths: null registrations are no-ops; removing
//      a never-added slot is a no-op; double-add is idempotent.
//   5. Finalization, two-collection semantics: the finalizer does NOT run at
//      the collection where the object dies (the block is RETAINED); it runs
//      at the START of the next collection, observing valid object memory
//      (klass header intact -- fn runs BEFORE the memory is reclaimed); the
//      block is actually freed by that same collection (total memory drops).
//   6. Finalization, live object: the callback stays armed while the object
//      is rooted and runs only after the object dies.
//   7. Finalization + Gen0 compaction: the registration entry's address is
//      fixed up through the forward map when the object moves; the callback
//      later runs with the CURRENT (post-compaction) address.
//   8. Finalization, re-registration: a second registration for the same
//      object REPLACES the callback (old one never runs).
//   9. Finalization, direct trigger (icall_gc_finalize_trigger): a pending
//      finalizer runs immediately when triggered, before the next collection.
//  10. Weak reference + finalization interplay: a dead object retained for
//      finalization still has its weak slot cleared (weakness is judged
//      against liveness, not retention).
//  11. Finalization edge paths: null object / null callback registrations are
//      safe no-ops.
//  12. WeakReference OBJECT model (il2cpp_gc_ext.h -- the managed
//      System.WeakReference icalls): ctor arms the handle (live target ->
//      IsAlive true, Target identity, survives the Gen0 compaction); set_Target
//      re-arms a new target and a null clears it.
//  13. WeakReference OBJECT model edge paths: a null ctor target is legal
//      (net48: IsAlive false, Target null); a target that dies is reclaimed and
//      the weak entry clears at the next full collection.
//  14. TYPE-level finalizer registry (il2cpp_gc_register_type_finalizer):
//      registering a type arms every instance at allocation; an unreferenced
//      instance is finalized (two-collection: retained at death, finalize runs
//      at the start of the next collection, memory then reclaimed); live
//      instances are NOT finalized; re-registration replaces the callback;
//      null registrations are safe no-ops; unregistered types stay unaffected.
//
// GC-safety: objects that must survive a collection are registered as static
// roots FIRST and unregistered after the last use (the compacting collection
// rewrites the root slot, so the local always holds the CURRENT address).
// Objects under test for weak-reference/finalizer death are deliberately NOT
// rooted -- that is the whole point.
//------------------------------------------------------------------------------

#include "il2cpp_gc_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h" // il2cpp_object_new (WeakReference object allocation)

#include <cstdint>
#include <cstdio>

namespace {

// The fixtures use pointer-sized slots; verify the platform assumption once.
static_assert(sizeof(void*) == 8, "gcext test fixture assumes 8-byte pointers");

// Number of failed assertions accumulated by Expect(). main() returns
// non-zero when this counter is not zero, so CI sees a red signal.
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

// Fixture class: a 32-byte leaf object (no reference fields).
il2cpp::Il2CppClass g_leaf = {
    "Test.GcExt.Leaf",
    32,          // instanceSize: header + 3 payload slots (32B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,     // baseClass
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// Dedicated fixture type for the TYPE-level finalizer tests (section 14). A
// separate descriptor is REQUIRED: il2cpp_gc_register_type_finalizer arms
// EVERY instance of a registered type at allocation, so using g_leaf here
// would make every later g_leaf allocation finalizable and break the other
// sections' reclaim/timing assertions.
il2cpp::Il2CppClass g_finType = {
    "Test.GcExt.FinType",
    32,          // instanceSize: header + 3 payload slots (32B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,     // baseClass
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// Finalizer callback observation state: invocation count and the klass header
// of the object the callback was invoked with. A valid klass proves the
// object's memory was intact AT CALL TIME -- the whole point of running the
// finalizer before the memory is reclaimed.
int g_finCalls = 0;
il2cpp::Il2CppClass* g_finKlassSeen = nullptr;

void OnFinalized(il2cpp::Il2CppObject* obj)
{
    ++g_finCalls;
    if (obj != nullptr)
    {
        g_finKlassSeen = obj->klass;
    }
}

// Second callback for the re-registration replacement test.
int g_finBCalls = 0;

void OnFinalizedB(il2cpp::Il2CppObject* obj)
{
    (void)obj;
    ++g_finBCalls;
}

// Type-level finalizer callback for g_finType (observes klass identity).
int g_finTypeCalls = 0;

void OnTypeFinalized(il2cpp::Il2CppObject* obj)
{
    ++g_finTypeCalls;
    if (obj != nullptr)
    {
        g_finKlassSeen = obj->klass;
    }
}

} // namespace

//------------------------------------------------------------------------------
// main -- run every GC-extension invariant and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    //--- 1. Heap statistics: total memory + cumulative allocation counter ---
    {
        const int64_t alloc0 = il2cpp::il2cpp_gc_get_allocated_bytes();
        il2cpp::Il2CppObject* o1 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* o2 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        const int64_t alloc1 = il2cpp::il2cpp_gc_get_allocated_bytes();
        Expect(alloc1 > alloc0,
               "allocated-bytes counter increases with each allocation");
        Expect(il2cpp::il2cpp_gc_get_total_memory() > 0,
               "total memory reports live blocks after allocations");
        // Root one survivor: total memory must stay positive across the
        // collection (the other object is unrooted and reclaimed).
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&o1));
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_total_memory() > 0,
               "total memory stays positive while a rooted object survives");
        Expect(o1 != nullptr && o1->klass == &g_leaf,
               "rooted survivor stays intact (root rewritten by compaction)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&o1));
        il2cpp::il2cpp_gc_collect(); // heap is clean for the next sections
        (void)o2;
    }

    //--- 2. Weak references: live target follows Gen0 compaction ---
    {
        il2cpp::Il2CppObject* target = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* weakSlot = target; // client-owned weak slot
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&target));
        il2cpp::runtime::icall_gc_weak_ref_add(reinterpret_cast<void**>(&weakSlot));
        il2cpp::il2cpp_gc_collect(); // Gen0 compaction MOVES the target
        Expect(weakSlot != nullptr,
               "weak slot still points at its live target after collection");
        Expect(weakSlot == target,
               "weak slot follows its live target through compaction (matches rooted address)");
        il2cpp::il2cpp_gc_collect(); // target is Gen1 now: not moved again
        Expect(weakSlot == target,
               "weak slot stays current across multiple collections");
        il2cpp::runtime::icall_gc_weak_ref_remove(reinterpret_cast<void**>(&weakSlot));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&target));
    }

    //--- 3. Weak references: dead target is cleared to null ---
    {
        il2cpp::Il2CppObject* dead = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* deadSlot = dead;
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&deadSlot));
        il2cpp::il2cpp_gc_collect(); // dead is unrooted -> swept; slot must clear
        Expect(deadSlot == nullptr,
               "weak slot is cleared to null when its target dies");
        il2cpp::il2cpp_gc_collect();
        Expect(deadSlot == nullptr,
               "cleared weak slot stays null across further collections");
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&deadSlot));
    }

    //--- 4. Weak references: null / unknown / duplicate registrations ---
    {
        il2cpp::il2cpp_gc_add_weak_ref(nullptr);
        il2cpp::il2cpp_gc_remove_weak_ref(nullptr);
        il2cpp::Il2CppObject* slotHolder = nullptr;
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&slotHolder)); // never added
        il2cpp::il2cpp_gc_collect();
        Expect(true, "null / unknown weak-ref registrations are safe no-ops");
        // Double-add is idempotent: the slot is still tracked exactly once.
        il2cpp::Il2CppObject* d = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* dSlot = d;
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&dSlot));
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&dSlot));
        il2cpp::il2cpp_gc_collect();
        Expect(dSlot == nullptr, "idempotent double-add still tracks the slot");
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&dSlot));
    }

    //--- 5. Finalization: two-collection semantics ---
    {
        g_finCalls = 0;
        g_finKlassSeen = nullptr;
        il2cpp::Il2CppObject* fin = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::il2cpp_gc_register_finalizer(fin, &OnFinalized);
        il2cpp::il2cpp_gc_collect(); // fin dies: RETAINED for finalization, not reclaimed
        Expect(g_finCalls == 0,
               "finalizer does NOT run at the collection where the object dies");
        const int64_t memBefore = il2cpp::il2cpp_gc_get_total_memory();
        il2cpp::il2cpp_gc_collect(); // fn runs at the START, block freed by this sweep
        Expect(g_finCalls == 1,
               "finalizer runs exactly once, at the next collection");
        Expect(g_finKlassSeen == &g_leaf,
               "finalizer observes valid object memory (klass header intact)");
        Expect(il2cpp::il2cpp_gc_get_total_memory() < memBefore,
               "finalized object's memory is reclaimed after its finalizer ran");
    }

    //--- 6. Finalization: a live object's finalizer stays armed ---
    {
        g_finCalls = 0;
        g_finKlassSeen = nullptr;
        il2cpp::Il2CppObject* keep = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::il2cpp_gc_register_finalizer(keep, &OnFinalized);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keep));
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(g_finCalls == 0,
               "live object's finalizer does not run while the object is rooted");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keep));
        il2cpp::il2cpp_gc_collect(); // dies: pending finalization
        Expect(g_finCalls == 0,
               "finalizer still waits for the next collection after death");
        il2cpp::il2cpp_gc_collect(); // fn runs
        Expect(g_finCalls == 1, "finalizer runs after the object dies");
        Expect(g_finKlassSeen == &g_leaf,
               "finalizer sees valid memory in the delayed path");
    }

    //--- 7. Finalization: registration address survives Gen0 compaction ---
    {
        g_finCalls = 0;
        g_finKlassSeen = nullptr;
        il2cpp::Il2CppObject* mover = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::il2cpp_gc_register_finalizer(mover, &OnFinalized);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&mover));
        il2cpp::il2cpp_gc_collect(); // MOVES mover (Gen0 -> Gen1); the entry's
                                     // address is fixed up through the forward map
        Expect(g_finCalls == 0,
               "finalizer not run while the object is alive (even after moving)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&mover));
        il2cpp::il2cpp_gc_collect(); // dies at the NEW address: the entry must
                                     // still match -> pending finalization
        il2cpp::il2cpp_gc_collect(); // fn runs with the CURRENT address
        Expect(g_finCalls == 1,
               "finalizer entry tracks its object through compaction");
        Expect(g_finKlassSeen == &g_leaf,
               "finalizer sees valid memory at the post-compaction address");
    }

    //--- 8. Finalization: re-registration replaces the callback ---
    {
        g_finCalls = 0;
        g_finBCalls = 0;
        il2cpp::Il2CppObject* rep = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::il2cpp_gc_register_finalizer(rep, &OnFinalized);
        il2cpp::il2cpp_gc_register_finalizer(rep, &OnFinalizedB); // replaces
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(g_finCalls == 0 && g_finBCalls == 1,
               "re-registration replaces the finalizer callback (old one does not run)");
    }

    //--- 9. Finalization: direct trigger (icall_gc_finalize_trigger) ---
    {
        g_finCalls = 0;
        g_finKlassSeen = nullptr;
        il2cpp::Il2CppObject* trig = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::runtime::icall_gc_register_finalizer(trig, &OnFinalized);
        il2cpp::il2cpp_gc_collect(); // dead: pending
        Expect(g_finCalls == 0,
               "pending finalizer does not run before the trigger");
        il2cpp::runtime::icall_gc_finalize_trigger();
        Expect(g_finCalls == 1, "finalize trigger runs the due finalizer immediately");
        Expect(g_finKlassSeen == &g_leaf,
               "triggered finalizer sees valid memory");
        il2cpp::il2cpp_gc_collect(); // frees the finalized block
    }

    //--- 10. Weak reference + finalization interplay ---
    // A dead object with a finalizer is RETAINED (pending finalization), but
    // its weak slot is still cleared at the collection where it died --
    // weakness is judged against the object's liveness, not its retention.
    {
        g_finCalls = 0;
        il2cpp::Il2CppObject* fin = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::Il2CppObject* finSlot = fin;
        il2cpp::il2cpp_gc_register_finalizer(fin, &OnFinalized);
        il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&finSlot));
        il2cpp::il2cpp_gc_collect(); // dead: weak cleared, finalizer pending
        Expect(finSlot == nullptr,
               "weak slot clears even for a retained finalizable object");
        Expect(g_finCalls == 0,
               "finalizable object is retained (finalizer not yet run)");
        il2cpp::il2cpp_gc_collect();
        Expect(g_finCalls == 1,
               "retained finalizable object is finalized at the next collection");
        il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&finSlot));
    }

    //--- 11. Null registrations are safe no-ops ---
    {
        g_finCalls = 0;
        il2cpp::il2cpp_gc_register_finalizer(nullptr, &OnFinalized);
        il2cpp::Il2CppObject* o = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::il2cpp_gc_register_finalizer(o, nullptr);
        il2cpp::il2cpp_gc_add_weak_ref(nullptr);
        il2cpp::il2cpp_gc_remove_weak_ref(nullptr);
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(g_finCalls == 0,
               "null object / null callback registrations are safe no-ops");
    }

    //--- 12. WeakReference OBJECT model: ctor / IsAlive / Target / set_Target ---
    // The managed System.WeakReference object lives in the gcext handle-table
    // model (il2cpp_gc_ext.h): an Il2CppWeakRefObject whose int32 handle
    // indexes the non-moving g_weakTargets table. The weak slot (the ADDRESS
    // of the table entry) is registered with the collector, so a live target
    // survives collections and FOLLOWS the Gen0 compaction.
    {
        // 12a. live target: ctor arms the handle; IsAlive true; Target
        // identity; the target survives a full collection and the weak entry
        // is rewritten to the target's post-compaction address (identical to
        // the rooted address -- the compaction rewrites both the root slot
        // and the weak slot through the same forward map).
        il2cpp::Il2CppObject* target = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&target));
        il2cpp::Il2CppObject* wr = il2cpp::il2cpp_object_new(il2cpp::il2cpp_weakref_class());
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&wr));
        il2cpp::runtime::icall_weakref_ctor(wr, target);
        Expect(il2cpp::runtime::icall_weakref_get_is_alive(wr),
               "WeakReference.IsAlive true for a live target");
        Expect(il2cpp::runtime::icall_weakref_get_target(wr) == target,
               "WeakReference.Target returns the live target");
        il2cpp::il2cpp_gc_collect(); // target Gen0 -> Gen1: both the root and the weak slot follow
        Expect(il2cpp::runtime::icall_weakref_get_is_alive(wr),
               "WeakReference.IsAlive stays true after a collection of its live target");
        Expect(il2cpp::runtime::icall_weakref_get_target(wr) == target,
               "WeakReference.Target follows the compacted target (matches rooted address)");

        // 12b. set_Target: re-arms the handle with a new target; null clears it.
        il2cpp::Il2CppObject* target2 = il2cpp::il2cpp_gc_alloc(&g_leaf);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&target2));
        il2cpp::runtime::icall_weakref_set_target(wr, target2);
        Expect(il2cpp::runtime::icall_weakref_get_is_alive(wr) &&
                   il2cpp::runtime::icall_weakref_get_target(wr) == target2,
               "WeakReference.set_Target re-arms the handle with a new target");
        il2cpp::runtime::icall_weakref_set_target(wr, nullptr);
        Expect(!il2cpp::runtime::icall_weakref_get_is_alive(wr),
               "WeakReference.set_Target(null) clears IsAlive");
        Expect(il2cpp::runtime::icall_weakref_get_target(wr) == nullptr,
               "WeakReference.set_Target(null) yields a null Target");

        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&target2));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&target));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&wr));
        il2cpp::il2cpp_gc_collect(); // wr dies: retained for finalization
        il2cpp::il2cpp_gc_collect(); // handle-release finalizer frees the handle
    }

    //--- 13. WeakReference OBJECT model: null target + dead target ---
    {
        // 13a. null ctor target is LEGAL (net48): IsAlive false, Target null.
        il2cpp::Il2CppObject* wr = il2cpp::il2cpp_object_new(il2cpp::il2cpp_weakref_class());
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&wr));
        il2cpp::runtime::icall_weakref_ctor(wr, nullptr);
        Expect(!il2cpp::runtime::icall_weakref_get_is_alive(wr),
               "WeakReference(null) has IsAlive false (net48)");
        Expect(il2cpp::runtime::icall_weakref_get_target(wr) == nullptr,
               "WeakReference(null) has a null Target (net48)");

        // 13b. dead target: an unrooted target is reclaimed at the next full
        // collection and the weak entry is cleared to null (IsAlive false).
        il2cpp::Il2CppObject* dead = il2cpp::il2cpp_gc_alloc(&g_leaf); // unrooted by design
        il2cpp::runtime::icall_weakref_set_target(wr, dead);
        Expect(il2cpp::runtime::icall_weakref_get_is_alive(wr),
               "WeakReference.IsAlive true while the target is reachable");
        il2cpp::il2cpp_gc_collect(); // dead swept; weak slot clears
        Expect(!il2cpp::runtime::icall_weakref_get_is_alive(wr),
               "WeakReference.IsAlive false after its target dies");
        Expect(il2cpp::runtime::icall_weakref_get_target(wr) == nullptr,
               "WeakReference.Target null after its target dies");

        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&wr));
        il2cpp::il2cpp_gc_collect(); // wr dies: retained for finalization
        il2cpp::il2cpp_gc_collect(); // handle-release finalizer frees the handle
    }

    //--- 14. TYPE-level finalizer registry (il2cpp_gc_register_type_finalizer) ---
    // Registering a TYPE arms every instance at allocation, so the collector
    // runs the type's Finalize method (two-collection semantics) when an
    // instance becomes unreferenced. Uses the DEDICATED g_finType descriptor
    // so arming never leaks into the other sections' g_leaf objects.
    {
        g_finTypeCalls = 0;
        g_finKlassSeen = nullptr;
        il2cpp::il2cpp_gc_register_type_finalizer(&g_finType, &OnTypeFinalized);

        // 14a. unreferenced instance -> two-collection -> finalize count.
        // (net48: object unreferenced then GC.Collect; the observable effect
        // is the Finalize method running once -- count == 1.)
        il2cpp::Il2CppObject* fin = il2cpp::il2cpp_gc_alloc(&g_finType); // unrooted
        (void)fin;
        il2cpp::il2cpp_gc_collect(); // fin dies: retained, NOT reclaimed
        Expect(g_finTypeCalls == 0,
               "type-finalized object is retained at its death collection (pending)");
        il2cpp::il2cpp_gc_collect(); // fn runs at the START of the next collection
        Expect(g_finTypeCalls == 1,
               "type-level finalizer runs exactly once after the object dies");
        Expect(g_finKlassSeen == &g_finType,
               "type-level finalizer observes valid memory (klass intact)");

        // 14b. a LIVE instance of a registered type is NOT finalized.
        g_finTypeCalls = 0;
        g_finKlassSeen = nullptr;
        il2cpp::Il2CppObject* keep = il2cpp::il2cpp_gc_alloc(&g_finType);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keep));
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(g_finTypeCalls == 0,
               "live instance of a registered type is not finalized");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keep));
        il2cpp::il2cpp_gc_collect(); // dies: pending
        il2cpp::il2cpp_gc_collect(); // fn runs
        Expect(g_finTypeCalls == 1,
               "type-level finalizer runs only after its object dies");
        Expect(g_finKlassSeen == &g_finType,
               "type-level finalizer sees valid memory after unrooting");

        // 14c. re-registration REPLACES the type callback.
        g_finTypeCalls = 0;
        g_finBCalls = 0;
        il2cpp::il2cpp_gc_register_type_finalizer(&g_finType, &OnFinalizedB);
        il2cpp::Il2CppObject* rep = il2cpp::il2cpp_gc_alloc(&g_finType);
        (void)rep;
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(g_finTypeCalls == 0 && g_finBCalls == 1,
               "re-registering a type finalizer replaces the callback");

        // 14d. null klass / null callback registrations are safe no-ops:
        // they neither crash nor REPLACE the still-registered callback
        // (OnFinalizedB from 14c) -- the next instance is still armed with it.
        g_finTypeCalls = 0;
        g_finBCalls = 0;
        il2cpp::il2cpp_gc_register_type_finalizer(nullptr, &OnTypeFinalized);
        il2cpp::il2cpp_gc_register_type_finalizer(&g_finType, nullptr);
        il2cpp::Il2CppObject* safe = il2cpp::il2cpp_gc_alloc(&g_finType);
        (void)safe;
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(g_finTypeCalls == 0 && g_finBCalls == 1,
               "null type-finalizer registrations are safe no-ops (existing callback kept)");

        // 14e. an unregistered type is NOT finalized (no stray finalization).
        g_finTypeCalls = 0;
        il2cpp::Il2CppObject* plain = il2cpp::il2cpp_gc_alloc(&g_leaf); // never registered
        (void)plain;
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(g_finTypeCalls == 0,
               "unregistered type's instances are not finalized");
    }

    //--- Report ---
    if (g_failures != 0)
    {
        std::printf("%d gcext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime gcext tests passed\n");
    return 0;
}
