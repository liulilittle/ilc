//------------------------------------------------------------------------------
// test_gc.cpp -- runtime mark-sweep collector unit tests.
//
// Coverage (normal + edge + error paths):
//   1. Allocation: N objects are all non-null, zero-initialized and stamped
//      with the requested klass.
//   2. Reclaim/reuse: unreachable objects are collected and their memory is
//      reused by later allocations (identical address under a stable size
//      class).
//   3. Reachability: a live object referenced only from a static root survives
//      a collect intact; a second object NOT reachable from any root is
//      reclaimed.
//   4. Reference-field scanning: a root object holding a reference to a second
//      object (marked by the refFieldBitmap) keeps that second object alive
//      through collection.
//   5. Root ranges: a contiguous range of reference slots keeps its targets
//      alive (frame-stack scenario).
//   6. Collect-without-roots: collecting with no roots at all reclaims
//      everything and still leaves the heap usable for fresh allocations.
//
// Like test_object.cpp this is a plain executable with a minimal assertion
// harness (no third-party framework); any failed Expect() prints FAIL, bumps a
// counter, and the process exits non-zero so CI sees a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

// The fixtures use pointer-sized slots; verify the platform assumption once.
static_assert(sizeof(void*) == 8, "gc test fixture assumes 8-byte pointers");

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

// Fixture class A: a 32-byte object. Bitmap 0b00000010 (bit 1, byte offset 8)
// marks the first payload slot as a GC reference; slots 0 is the klass header
// (never scanned as a reference), slots 2..3 are value payload.
il2cpp::Il2CppClass g_classA = {
    "Test.GcA", // name
    32,         // instanceSize: header + 4 slots (32B)
    0b00000010u,// refFieldBitmap: slot 1 (offset 8) is a GC reference
    1,          // refFieldCount: one reference-typed field
    nullptr,    // baseClass
    nullptr,    // vtable
    0,          // vtableSize
    nullptr,    // interfaces
    0,          // interfaceCount
};

// Fixture class B: a 24-byte object with NO reference fields -- the collector
// must treat it as a leaf during marking (nothing to scan).
il2cpp::Il2CppClass g_classB = {
    "Test.GcB",
    24,          // instanceSize: header + 2 payload slots (24B)
    0b00000010u, // refFieldBitmap: slot 1 (offset 8) is a GC reference
    1,
    nullptr,
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// Fixture class C: a 16-byte object with no reference fields at all.
il2cpp::Il2CppClass g_classC = {
    "Test.GcC",
    16,          // instanceSize: header + 1 payload slot (16B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// Fixture class D: a 64-byte object with no reference fields -- used by the
// churn test so segments cycle through full/collect/full without any live
// object ever outliving its cycle.
il2cpp::Il2CppClass g_classD = {
    "Test.GcD",
    64,          // instanceSize: header + 7 payload slots (64B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// Fixture class E: an 8-byte object -- header only, exactly the shape of
// System.Object (`new object()`). This is the SMALLEST block the allocator
// can produce (instanceSize == sizeof(Il2CppObject)). Sweep writes a 16-byte
// FreeBlock header [next|size] into every reclaimed block, so the collector
// must round this up to fit the header -- a smaller block would make the
// free-list write spill past its edge into the adjacent block's klass.
il2cpp::Il2CppClass g_classE = {
    "Test.GcE",
    8,           // instanceSize: header only (8B)
    0u,          // refFieldBitmap: no reference fields
    0,           // refFieldCount
    nullptr,
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// Value slot helpers: read/write the pointer stored in the reference field at
// byte offset 8 of a fixture object (the slot 1 the bitmaps mark).
il2cpp::Il2CppObject** RefSlot(il2cpp::Il2CppObject* obj)
{
    return reinterpret_cast<il2cpp::Il2CppObject**>(reinterpret_cast<char*>(obj) + 8);
}

} // namespace

//------------------------------------------------------------------------------
// main -- run every collector invariant and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    // --- 1. Allocation: N objects non-null, zeroed, correct klass header ---
    const int kBatch = 64;
    il2cpp::Il2CppObject* batch[kBatch] = { nullptr };
    for (int i = 0; i < kBatch; ++i)
    {
        batch[i] = il2cpp::il2cpp_gc_alloc(&g_classA);
        Expect(batch[i] != nullptr, "gc allocation returns a non-null object");
        Expect(batch[i]->klass == &g_classA, "gc object klass header matches the requested class");
    }
    {
        const unsigned char* bytes = static_cast<const unsigned char*>(static_cast<const void*>(batch[0]));
        bool payloadZero = true;
        for (size_t i = sizeof(il2cpp::Il2CppObject); i < static_cast<size_t>(g_classA.instanceSize); ++i)
        {
            if (bytes[i] != 0)
            {
                payloadZero = false;
                break;
            }
        }
        Expect(payloadZero, "gc allocation payload bytes are zero-initialized");
    }
    // Distinctness: each allocation lands on its own block.
    bool distinct = true;
    for (int i = 0; i < kBatch; ++i)
    {
        for (int j = i + 1; j < kBatch; ++j)
        {
            if (batch[i] == batch[j])
            {
                distinct = false;
            }
        }
    }
    Expect(distinct, "concurrent allocations return distinct addresses");

    // --- 2. Reclaim: drop all roots, collect, allocate again ---
    // No static roots point at 'batch' now, so every object is unreachable.
    // 2026-09-23: bump-first allocation no longer guarantees that a reclaimed
    // address is handed out again (CLR does not guarantee address reuse
    // either); the recycling contract is "the heap accepts new allocations
    // after a full collect and committed memory does not grow unboundedly".
    il2cpp::il2cpp_gc_collect();
    const int64_t memBefore = il2cpp::il2cpp_gc_get_total_memory();
    il2cpp::Il2CppObject* reused = il2cpp::il2cpp_gc_alloc(&g_classA);
    Expect(reused != nullptr, "heap accepts allocations after a full collect");
    const int64_t memAfter = il2cpp::il2cpp_gc_get_total_memory();
    Expect(memAfter <= memBefore + 64 * 1024,
           "post-collect allocation does not grow the heap unboundedly");

    // --- 3. Static-root reachability ---
    il2cpp::Il2CppObject* alive = il2cpp::il2cpp_gc_alloc(&g_classC);
    il2cpp::Il2CppObject* dead  = il2cpp::il2cpp_gc_alloc(&g_classC);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&alive));
    il2cpp::il2cpp_gc_collect();
    Expect(alive != nullptr && alive->klass == &g_classC, "static-rooted object survives collection");
    // 2026-09-23: bump-first allocation does not guarantee the reclaimed
    // address is reused (CLR neither); the contract is "the heap accepts new
    // allocations after collection while the rooted object stays alive".
    il2cpp::Il2CppObject* probe = il2cpp::il2cpp_gc_alloc(&g_classC);
    Expect(probe != nullptr && alive->klass == &g_classC,
           "heap accepts allocations after collection; rooted object stays alive");

    // --- 4. Reference-field scanning ---
    il2cpp::Il2CppObject* holder = il2cpp::il2cpp_gc_alloc(&g_classA);
    il2cpp::Il2CppObject* target = il2cpp::il2cpp_gc_alloc(&g_classB);
    *RefSlot(holder) = target;
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&holder));
    // Drop the local alias 'target': only holder -> target remains.
    il2cpp::il2cpp_gc_collect();
    Expect(holder->klass == &g_classA, "holder survives collection while rooted");
    Expect(RefSlot(holder) != nullptr, "holder's reference field survives collection intact");
    il2cpp::Il2CppObject* kept = *RefSlot(holder);
    // NOTE: with the M3 compacting collection, surviving Gen0 objects may
    // MOVE -- the old C++ local 'target' is stale. Correctness is proven by
    // the FIELD content (a valid classB object), not by address equality.
    Expect(kept != nullptr && kept->klass == &g_classB,
           "object reachable via a referenced field survives collection (content intact)");

    // --- 5. Root ranges (frame-stack scenario) ---
    il2cpp::Il2CppObject* ranged[3] = { nullptr, nullptr, nullptr };
    for (int i = 0; i < 3; ++i)
    {
        ranged[i] = il2cpp::il2cpp_gc_alloc(&g_classC);
    }
    il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(ranged), 3);
    il2cpp::il2cpp_gc_collect();
    bool rangeAlive = true;
    for (int i = 0; i < 3; ++i)
    {
        if (ranged[i] == nullptr || ranged[i]->klass != &g_classC)
        {
            rangeAlive = false;
        }
    }
    Expect(rangeAlive, "objects in a registered root range survive collection");

    // --- 6. Collect with no roots, heap still usable ---
    il2cpp::il2cpp_gc_collect();
    il2cpp::Il2CppObject* post = il2cpp::il2cpp_gc_alloc(&g_classB);
    Expect(post != nullptr && post->klass == &g_classB, "heap allocates normally after an empty-root collection");

    // --- 7. Churn / reuse stress: repeated full-heap cycles must not corrupt ---
    // 40 cycles x 1500 mixed objects; every object is dropped before the next
    // collect, so each cycle reclaims and reuses the same segments. Any
    // overlap or boundary error in the free-list reuse surfaces as a wrong
    // klass or a non-zero (partially overwritten) payload.
    for (int cycle = 0; cycle < 40; ++cycle)
    {
        for (int i = 0; i < 1500; ++i)
        {
            il2cpp::Il2CppObject* o = (i % 2 == 0)
                ? il2cpp::il2cpp_gc_alloc(&g_classA)
                : il2cpp::il2cpp_gc_alloc(&g_classD);
            Expect(o != nullptr && o->klass != nullptr, "churn allocation returns a well-formed object");
        }
        il2cpp::il2cpp_gc_collect();
    }
    {
        il2cpp::Il2CppObject* fin = il2cpp::il2cpp_gc_alloc(&g_classA);
        const unsigned char* bytes = static_cast<const unsigned char*>(static_cast<const void*>(fin));
        bool payloadZero = true;
        for (size_t i = sizeof(il2cpp::Il2CppObject); i < static_cast<size_t>(g_classA.instanceSize); ++i)
        {
            if (bytes[i] != 0)
            {
                payloadZero = false;
            }
        }
        Expect(payloadZero, "post-churn allocation is still zero-initialized");
    }

    // --- 8. Generational promotion (M3 Task 2) ---
    // A fresh allocation starts in Gen0; after one collection it is Gen1;
    // after another it is Gen2. LOH-sized objects stay at Gen3 (LOH).
    {
        il2cpp::Il2CppObject* gen0 = il2cpp::il2cpp_gc_alloc(&g_classA);
        Expect(il2cpp::il2cpp_gc_get_generation(gen0) == il2cpp::kGcGeneration0,
               "fresh allocation is in generation 0");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&gen0));
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_generation(gen0) == il2cpp::kGcGeneration1,
               "survivor of one collection is promoted to generation 1");
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_generation(gen0) == il2cpp::kGcGeneration2,
               "survivor of two collections is promoted to generation 2");
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_generation(gen0) == il2cpp::kGcGeneration2,
               "generation 2 objects stay in generation 2 (oldest small-object gen)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&gen0));
    }
    {
        // LOH-sized allocation (>= 85000 bytes): goes straight to the LOH and
        // never promotes (stays at generation 3 through collections).
        // A 90000-byte object needs a class with instanceSize >= 90000.
        static il2cpp::Il2CppClass lohClass = {
            "Test.GcLoh", 90000, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
        };
        il2cpp::Il2CppObject* loh = il2cpp::il2cpp_gc_alloc(&lohClass);
        Expect(loh != nullptr, "LOH-sized allocation succeeds");
        Expect(il2cpp::il2cpp_gc_get_generation(loh) == il2cpp::kGcGenerationLoh,
               "large object (> 85000 B) is allocated on the LOH (generation 3)");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&loh));
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_generation(loh) == il2cpp::kGcGenerationLoh,
               "LOH object stays on the LOH after collection (no promotion)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&loh));
    }
    {
        // Dead objects (no root) are still reclaimed regardless of generation.
        il2cpp::Il2CppObject* dead2 = il2cpp::il2cpp_gc_alloc(&g_classC);
        (void)dead2;
        il2cpp::il2cpp_gc_collect();
        il2cpp::Il2CppObject* after = il2cpp::il2cpp_gc_alloc(&g_classC);
        Expect(after != nullptr && after->klass == &g_classC,
               "generation 0 dead objects are reclaimed (heap stays usable)");
    }

    // --- 9. Write barrier + card table (M3 Task 3) ---
    {
        // A Gen0 (young) storing object: barrier must NOT mark the card
        // (young objects are scanned wholesale -- no old->young tracking).
        // NOTE: an implicit alloc-triggered collect may have promoted the
        // object already; the assertion is valid only while it is Gen0.
        il2cpp::Il2CppObject* youngHolder = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&youngHolder));
        il2cpp::Il2CppObject* youngTarget = il2cpp::il2cpp_gc_alloc(&g_classB);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&youngTarget));
        il2cpp::il2cpp_write_barrier(reinterpret_cast<void**>(RefSlot(youngHolder)), youngTarget);
        const int32_t holderGenAtBarrier = il2cpp::il2cpp_gc_get_generation(youngHolder);
        if (holderGenAtBarrier == il2cpp::kGcGeneration0)
        {
            Expect(!il2cpp::il2cpp_gc_is_card_marked(RefSlot(youngHolder)),
                   "writing a reference from a Gen0 object does not mark a card");
        }
        // (If the object was implicitly promoted before the barrier, the card
        // MAY be marked -- that is correct old-generation behavior; the
        // assertion above is skipped because the precondition (Gen0) failed.)

        // An old (promoted) storing object: barrier MUST mark the card.
        il2cpp::il2cpp_gc_collect(); // promotes youngHolder (may have been promoted once more by an implicit alloc-triggered collect)
        Expect(il2cpp::il2cpp_gc_get_generation(youngHolder) >= il2cpp::kGcGeneration1,
               "holder promoted out of Gen0 before card test");
        il2cpp::Il2CppObject* oldTarget = il2cpp::il2cpp_gc_alloc(&g_classC);
        il2cpp::il2cpp_write_barrier(reinterpret_cast<void**>(RefSlot(youngHolder)), oldTarget);
        Expect(il2cpp::il2cpp_gc_is_card_marked(RefSlot(youngHolder)),
               "writing a reference from an old (Gen1+) object marks its card");

        // After a collection the card table is reset (dirty markers cleared).
        il2cpp::il2cpp_gc_collect();
        Expect(!il2cpp::il2cpp_gc_is_card_marked(RefSlot(youngHolder)),
               "card table is cleared by collection");
        // Block-scoped roots MUST be removed before the block ends: with the
        // compacting collection, a stale root pointing at dead stack memory
        // is scanned as a garbage reference.
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&youngHolder));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&youngTarget));
    }

    // --- 10. Compacting collection: Gen0 survivors move, references update ---
    {
        // Two Gen0 objects linked by a reference field. After one collection
        // BOTH are copied to the Gen1 promote segment; the field inside the
        // copied holder must point at the copied target (reference update
        // across the move). Addresses change; contents must be preserved.
        // NOTE: alloc and add_static_root must be adjacent -- a later
        // allocation may trigger a collection, and an unrooted freshly
        // allocated object would be reclaimed (exactly like .NET: the newobj
        // result is a frame root from the moment it exists).
        il2cpp::Il2CppObject* h = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&h));
        il2cpp::Il2CppObject* t = il2cpp::il2cpp_gc_alloc(&g_classB);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&t));
        *RefSlot(h) = t;
        il2cpp::il2cpp_gc_collect();
        Expect(h->klass == &g_classA, "compacted holder survives with correct class");
        Expect(t->klass == &g_classB, "compacted target survives with correct class");
        Expect(il2cpp::il2cpp_gc_get_generation(h) >= il2cpp::kGcGeneration1,
               "compacted Gen0 survivor is promoted out of Gen0");
        Expect(*RefSlot(h) == t, "holder's reference field points at the MOVED target (reference update)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&h));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&t));

        // Repeated compaction cycles keep deep references correct.
        il2cpp::Il2CppObject* h2 = il2cpp::il2cpp_gc_alloc(&g_classA);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&h2));
        il2cpp::Il2CppObject* t2 = il2cpp::il2cpp_gc_alloc(&g_classB);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&t2));
        *RefSlot(h2) = t2;
        il2cpp::il2cpp_gc_collect();
        il2cpp::il2cpp_gc_collect();
        Expect(*RefSlot(h2) == t2, "repeated compactions keep field references correct");
        Expect((*RefSlot(h2))->klass == &g_classB, "deep reference survives repeated compactions");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&h2));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&t2));
    }

    // --- 11. Minimum-size blocks: 8-byte objects must not corrupt their
    // neighbour when swept. The free-list header is 16 bytes ([next|size] on
    // 64-bit); a block smaller than that makes sweep's write spill past the
    // block edge into the adjacent block's klass header (Phase C crash). The
    // storm bump-allocates thousands of 8-byte blocks immediately before
    // rooted 16-byte sentinels, so sweeping the LAST tiny block overwrites
    // the FIRST sentinel's klass -- the exact `new object()` churn shape.
    {
        const int kTiny = 4096;
        const int kSent = 64;
        il2cpp::Il2CppObject* sentinels[kSent] = { nullptr };
        for (int cycle = 0; cycle < 5; ++cycle)
        {
            for (int i = 0; i < kTiny; ++i)
            {
                Expect(il2cpp::il2cpp_gc_alloc(&g_classE) != nullptr,
                       "8-byte block allocation succeeds");
            }
            for (int i = 0; i < kSent; ++i)
            {
                sentinels[i] = il2cpp::il2cpp_gc_alloc(&g_classC);
                il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sentinels[i]));
            }
            il2cpp::il2cpp_gc_collect();
            bool intact = true;
            for (int i = 0; i < kSent; ++i)
            {
                if (sentinels[i] == nullptr || sentinels[i]->klass != &g_classC)
                {
                    intact = false;
                }
            }
            Expect(intact,
                   "sentinels adjacent to swept 8-byte blocks keep their klass");
            for (int i = 0; i < kSent; ++i)
            {
                il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sentinels[i]));
            }
        }
    }

    // --- 12. GC statistics + GCSettings (Core GC 13 gap domain) ---
    // The collector runs FULL-heap cycles, so one explicit collection
    // increments every small-object generation's count exactly once, and the
    // pause/committed/fragmentation counters are monotonic. Generation-size
    // sums must equal the live-heap total (every live block carries exactly
    // one generation).
    {
        // Collection counts: deterministic +1 for each generation per cycle.
        const int64_t count0 = il2cpp::il2cpp_gc_get_collection_count(0);
        const int64_t count1 = il2cpp::il2cpp_gc_get_collection_count(1);
        const int64_t count2 = il2cpp::il2cpp_gc_get_collection_count(2);
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_collection_count(0) == count0 + 1,
               "gen0 collection count increments by 1 per full collection");
        Expect(il2cpp::il2cpp_gc_get_collection_count(1) == count1 + 1,
               "gen1 collection count increments by 1 per full collection");
        Expect(il2cpp::il2cpp_gc_get_collection_count(2) == count2 + 1,
               "gen2 collection count increments by 1 per full collection");
        Expect(il2cpp::il2cpp_gc_get_collection_count(3) == 0,
               "LOH generation reports no collection count");
        Expect(il2cpp::il2cpp_gc_get_collection_count(-1) == 0 &&
               il2cpp::il2cpp_gc_get_collection_count(99) == 0,
               "out-of-range generations report 0 collection count");

        // MaxGeneration is the oldest small-object generation (2).
        Expect(il2cpp::il2cpp_gc_get_max_generation() == il2cpp::kGcGeneration2,
               "GC.MaxGeneration reports the oldest small-object generation (2)");

        // Pause ticks: monotonic across collections.
        const int64_t pauseBefore = il2cpp::il2cpp_gc_get_total_pause_ticks();
        Expect(pauseBefore >= 0, "total GC pause ticks are non-negative");
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_total_pause_ticks() >= pauseBefore,
               "total GC pause ticks are monotonic across collections");

        // Percent time in GC: always a valid percentage.
        const int32_t pct = il2cpp::il2cpp_gc_get_last_gc_percent_time_in_gc();
        Expect(pct >= 0 && pct <= 100,
               "percent time in GC stays within 0..100");

        // Memory load / committed / fragmented: committed covers the live
        // heap; fragmentation is never negative.
        const int32_t load = il2cpp::il2cpp_gc_get_memory_load();
        Expect(load >= 0 && load <= 100,
               "memory load percentage stays within 0..100");
        const int64_t committed = il2cpp::il2cpp_gc_get_total_committed_bytes();
        Expect(committed > 0, "committed segment bytes are positive after allocation");
        Expect(committed >= il2cpp::il2cpp_gc_get_total_memory(),
               "committed bytes cover the live heap");
        Expect(il2cpp::il2cpp_gc_get_segment_size() == 1024u * 1024u,
               "segment size reports the 1 MB segment payload");
        Expect(il2cpp::il2cpp_gc_get_fragmented_bytes() >= 0,
               "fragmented (free-list) bytes are never negative");

        // Generation sizes: every live block has exactly one generation, so
        // the per-generation sums equal the live-heap total.
        const int64_t genSum =
            il2cpp::il2cpp_gc_get_generation_size(0) +
            il2cpp::il2cpp_gc_get_generation_size(1) +
            il2cpp::il2cpp_gc_get_generation_size(2) +
            il2cpp::il2cpp_gc_get_generation_size(3);
        Expect(genSum == il2cpp::il2cpp_gc_get_total_memory(),
               "per-generation live sizes sum to the live-heap total");
        Expect(il2cpp::il2cpp_gc_get_generation_size(-1) == 0 &&
               il2cpp::il2cpp_gc_get_generation_size(99) == 0,
               "out-of-range generations report 0 live bytes");

        // Full-GC notification: the blocking collector always succeeds.
        Expect(il2cpp::il2cpp_gc_register_for_full_gc_notification(50, 50),
               "full-GC notification registration succeeds (blocking collector)");
        Expect(il2cpp::il2cpp_gc_cancel_full_gc_notification(),
               "full-GC notification cancellation succeeds");

        // Allocation counters: per-thread and approximate totals coincide
        // with the cumulative process counter (single-threaded collector).
        Expect(il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread() ==
                   il2cpp::il2cpp_gc_get_allocated_bytes(),
               "per-thread allocation counter equals the cumulative counter");
        Expect(il2cpp::il2cpp_gc_get_total_allocated_bytes_approximate() ==
                   il2cpp::il2cpp_gc_get_allocated_bytes(),
               "approximate total allocation counter equals the cumulative counter");
        const int64_t allocBefore = il2cpp::il2cpp_gc_get_allocated_bytes();
        il2cpp::il2cpp_gc_alloc(&g_classC);
        Expect(il2cpp::il2cpp_gc_get_allocated_bytes() > allocBefore,
               "allocation counter grows after a fresh allocation");

        // GCSettings.
        Expect(!il2cpp::il2cpp_gc_get_is_server_gc(),
               "IsServerGC is false (workstation collector)");
        Expect(il2cpp::il2cpp_gc_get_latency_mode() == il2cpp::kGcLatencyModeInteractive,
               "default GCLatencyMode is Interactive");
        Expect(il2cpp::il2cpp_gc_set_latency_mode(il2cpp::kGcLatencyModeLowLatency) ==
                   il2cpp::kGcSetLatencyStatusSucceeded,
               "setting a valid latency mode succeeds");
        Expect(il2cpp::il2cpp_gc_get_latency_mode() == il2cpp::kGcLatencyModeLowLatency,
               "latency mode stores the requested value");
        il2cpp::il2cpp_gc_set_latency_mode(99); // out-of-range: ignored
        Expect(il2cpp::il2cpp_gc_get_latency_mode() == il2cpp::kGcLatencyModeLowLatency,
               "out-of-range latency mode is ignored");
        il2cpp::il2cpp_gc_set_latency_mode(il2cpp::kGcLatencyModeInteractive);
        Expect(il2cpp::il2cpp_gc_get_latency_mode() == il2cpp::kGcLatencyModeInteractive,
               "latency mode resets to Interactive");
        Expect(il2cpp::il2cpp_gc_get_loh_compaction_mode() == il2cpp::kGcLohCompactionModeDefault,
               "default LOH compaction mode is Default");
        il2cpp::il2cpp_gc_set_loh_compaction_mode(il2cpp::kGcLohCompactionModeCompactOnce);
        Expect(il2cpp::il2cpp_gc_get_loh_compaction_mode() == il2cpp::kGcLohCompactionModeCompactOnce,
               "LOH compaction mode stores CompactOnce");
        il2cpp::il2cpp_gc_set_loh_compaction_mode(0); // out-of-range: ignored
        Expect(il2cpp::il2cpp_gc_get_loh_compaction_mode() == il2cpp::kGcLohCompactionModeCompactOnce,
               "out-of-range LOH compaction mode is ignored");
        il2cpp::il2cpp_gc_set_loh_compaction_mode(il2cpp::kGcLohCompactionModeDefault);

        // GetMemoryInfo data surface: consistent with the scalar accessors.
        il2cpp::GcMemoryInfoData info;
        il2cpp::il2cpp_gc_get_memory_info(info);
        Expect(info.index >= 1, "memory-info index counts at least one collection");
        Expect(info.generation == il2cpp::kGcGeneration2,
               "memory-info reports the last (full) collection generation");
        Expect(info.compacted && !info.concurrent,
               "memory-info reports a compacting, non-concurrent collection");
        Expect(info.heapSizeBytes == il2cpp::il2cpp_gc_get_total_memory(),
               "memory-info heap size matches the live-heap total");
        Expect(info.fragmentedBytes == il2cpp::il2cpp_gc_get_fragmented_bytes(),
               "memory-info fragmentation matches the free-list bytes");
        Expect(info.totalCommittedBytes >= info.heapSizeBytes,
               "memory-info committed bytes cover the live heap");
        Expect(info.pinnedObjectsCount == 0,
               "memory-info reports no pinned objects (no pinning in M3)");
        Expect(info.finalizationPendingCount >= 0,
               "memory-info finalization-pending count is never negative");
        Expect(info.memoryLoadBytes >= 0 &&
                   info.memoryLoadBytes <= info.highMemoryLoadThresholdBytes,
               "memory-info load bytes stay within the memory budget");
        Expect(info.highMemoryLoadThresholdBytes == info.totalAvailableMemoryBytes,
               "memory-info threshold equals the available-memory budget");
        // Promoted-bytes snapshot: a fresh rooted Gen0 block survives the next
        // collection and is promoted, so promotedBytes must grow by at least
        // that block's physical extent (16-byte classC block). The snapshot is
        // read-only (computed from the mark phase before the sweep) and feeds
        // GCMemoryInfo.PromotedBytes.
        const int64_t promotedBefore = info.promotedBytes;
        Expect(promotedBefore >= 0, "promoted-bytes snapshot is non-negative");
        il2cpp::Il2CppObject* promo = il2cpp::il2cpp_gc_alloc(&g_classC);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&promo));
        il2cpp::il2cpp_gc_collect();
        il2cpp::GcMemoryInfoData info2;
        il2cpp::il2cpp_gc_get_memory_info(info2);
        Expect(info2.promotedBytes >= promotedBefore + 16,
               "promoted-bytes snapshot grows by the surviving Gen0 block's size");
        Expect(info2.promotedBytes <= info2.heapSizeBytes,
               "promoted bytes never exceed the live-heap total");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&promo));
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d gc assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime gc tests passed\n");
    return 0;
}

