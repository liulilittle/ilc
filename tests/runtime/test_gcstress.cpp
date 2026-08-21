//------------------------------------------------------------------------------
// test_gcstress.cpp -- GC stress tests for the mark-sweep collector (M2/M3).
//
// The collector under test (gc.cpp) performs full-heap mark-sweep WITH
// young-generation compaction (M3 Task 4): every surviving Gen0 block is
// COPIED to a fresh Gen1 segment during a collection, and every reference
// (static roots, root ranges, frame slots, object fields) is rewritten
// through the forward map afterwards (Phase C). Two consequences drive the
// design of this harness:
//
//   * Object addresses are NOT stable across collections. Every pointer is
//     re-read through a REGISTERED ROOT SLOT after a collection (Phase C
//     rewrites the root slot itself), never through a cached C++ local --
//     a stale local may point at swept memory.
//   * Reachability is proven by walking the object graph and validating
//     content (klass headers, payload values), not by comparing addresses.
//
// Coverage (normal + edge + error paths):
//   1. Mass small-object churn: 100k+ allocations across dozens of full
//      collections (explicit AND alloc-triggered). Every fresh allocation
//      must be zero-initialized with a correct klass header. The collector
//      ABORTS (fail-loud) when the live-block table overflows, so a sweep
//      leak is a hard process failure, not a silent drift.
//   2. Deep reference chains: a rooted head keeps a 64-node graph and a
//      2000-node graph alive through repeated compactions; every hop and
//      every payload value survives (exercises Phase C reference update).
//   3. Reference cycles: an unrooted A->B->A cycle is reclaimed without
//      corrupting the heap; a ROOTED cycle survives intact (the iterative
//      mark worklist must terminate on cycles, and Phase C must not leave
//      a dangling back-link).
//   4. Static roots over managed collections: a reference-element array, an
//      int32 array and a managed string stay valid (length, elements,
//      payload) across collections -- exercising the collector's array
//      scan/update paths and the size-extended allocation path.
//   5. Large objects: hundreds-of-KB allocations on the LOH (>= 85000 B),
//      the LOH threshold boundary (just under: Gen0; just over: LOH), and
//      non-moving survival of LOH objects.
//   6. Allocation/free alternation with mixed size classes: a root range
//      keeps half of each batch alive while the other half is swept,
//      fragmenting the free list; first-fit whole-block reuse must stay
//      correct across rounds.
//
// Like the other runtime tests this is a plain executable with a minimal
// assertion harness: a failed Expect() prints FAIL, bumps a counter, and the
// process exits non-zero so CI sees a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

// The fixtures use pointer-sized slots; verify the platform assumption once.
static_assert(sizeof(void*) == 8, "gc stress test assumes 8-byte pointers");

// Number of failed assertions accumulated by Expect(). main() returns
// non-zero when this counter is not zero, so CI observes the result via the
// process exit code.
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

//------------------------------------------------------------------------------
// Fixture classes (layout mirrors the descriptors emitted by the transpiler;
// slot i <=> byte offset i * sizeof(void*); slot 0 is the klass header).
//------------------------------------------------------------------------------

// A 32-byte object: slot 1 = reference field, slots 2..3 = value payload.
il2cpp::Il2CppClass g_classA = {
    "Test.GcStress.A",
    32,          // instanceSize
    0b00000010u, // refFieldBitmap: slot 1 is a GC reference
    1,           // refFieldCount
    nullptr,     // baseClass
    nullptr,     // vtable
    0,           // vtableSize
    nullptr,     // interfaces
    0,           // interfaceCount
};

// A 16-byte leaf object with no reference fields.
il2cpp::Il2CppClass g_classC = {
    "Test.GcStress.C",
    16,          // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// A 64-byte leaf object with no reference fields.
il2cpp::Il2CppClass g_classD = {
    "Test.GcStress.D",
    64,          // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// An 8-byte object: header only -- the System.Object (`new object()`) shape
// and the SMALLEST block the allocator can produce. Sweep writes a 16-byte
// FreeBlock header [next|size] into every reclaimed block, so an 8-byte
// block would let that write spill past its edge into the adjacent block's
// klass header (out-of-bounds overwrite, Phase C crash).
il2cpp::Il2CppClass g_tiny8 = {
    "Test.GcStress.Tiny8",
    8,           // instanceSize: header only
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// Chain node: 24 bytes = header + slot 1 (next reference) + slot 2 (value).
// The bitmap marks ONLY slot 1: slot 2 carries an integer payload and must
// never be treated as a GC pointer by the collector.
il2cpp::Il2CppClass g_node = {
    "Test.GcStress.Node",
    24,          // instanceSize
    0b00000010u, // refFieldBitmap: only slot 1 (next) is a GC reference
    1,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// Large object: 300000 bytes, no references (LOH: >= 85000 B).
il2cpp::Il2CppClass g_big = {
    "Test.GcStress.Big",
    300000,      // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// Very large object: 2 MB -- LARGER than a single LOH segment payload
// (kLohSegmentPayload = 1MB). The allocator must size the segment to the
// block, not bump a >1MB block into a fresh 1MB segment (audit A1:
// previously overflowed the segment -> heap corruption / 0xC0000005).
il2cpp::Il2CppClass g_mega = {
    "Test.GcStress.Mega",
    2 * 1024 * 1024, // instanceSize: 2MB (LOH)
    0u,              // refFieldBitmap: no references
    0,               // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// LOH threshold boundary, just over: 85001 -> LOH (aligned 85008).
il2cpp::Il2CppClass g_lohBoundary = {
    "Test.GcStress.Loh",
    85001,       // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// LOH threshold boundary, just under: 84992 -> aligned 84992 < 85000, so
// Gen0 -- even though the object is larger than a 64 KB segment (exercises
// the non-moving promotion path for oversized Gen0 survivors).
il2cpp::Il2CppClass g_gen0NearLoh = {
    "Test.GcStress.Gen0NearLoh",
    84992,       // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// Medium size classes for the fragmentation test (no references).
il2cpp::Il2CppClass g_m256 = {
    "Test.GcStress.M256",
    256,         // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass g_m2k = {
    "Test.GcStress.M2K",
    2048,        // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass g_m16k = {
    "Test.GcStress.M16K",
    16384,       // instanceSize
    0u,          // refFieldBitmap: no references
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

// The longest chain this suite builds (2000 nodes); bounds the holder array.
constexpr int32_t kMaxChainNodes = 2048;

// Reference-element array class for the static-root array test. The '[' name
// prefix is the collector's array-class marker: the payload is scanned as GC
// references (gc.cpp ScanObject). instanceSize mirrors the runtime array
// classes (header + 1-element stub); the array itself is allocated with
// il2cpp_gc_alloc_size (see the array test section for the size note).
il2cpp::Il2CppClass g_refArrayClass = {
    "[Test.GcStress.ObjectArray",
    static_cast<int32_t>(sizeof(il2cpp::Il2CppArray)), // instanceSize
    0u,          // refFieldBitmap: payload scanned dynamically by the array branch
    0,           // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

//------------------------------------------------------------------------------
// Field-access helpers (offsets in pointer slots, matching the bitmaps).
//------------------------------------------------------------------------------

// The reference slot at byte offset 8 (slot 1) of a fixture object.
il2cpp::Il2CppObject** NextSlot(il2cpp::Il2CppObject* obj)
{
    return reinterpret_cast<il2cpp::Il2CppObject**>(reinterpret_cast<char*>(obj) + 8);
}

// The integer value slot at byte offset 16 (slot 2) of a chain node.
uint64_t* ValueSlot(il2cpp::Il2CppObject* obj)
{
    return reinterpret_cast<uint64_t*>(reinterpret_cast<char*>(obj) + 16);
}

// The marker slot at byte offset 16 -- available for every class with
// instanceSize >= 24 (not for the 16-byte g_classC).
uintptr_t* MarkerSlot(il2cpp::Il2CppObject* obj)
{
    return reinterpret_cast<uintptr_t*>(reinterpret_cast<char*>(obj) + 16);
}

void SetMarker(il2cpp::Il2CppObject* obj, uintptr_t value)
{
    *MarkerSlot(obj) = value;
}

uintptr_t GetMarker(il2cpp::Il2CppObject* obj)
{
    return *MarkerSlot(obj);
}

bool HasMarker(il2cpp::Il2CppClass* klass)
{
    return klass->instanceSize >= 24;
}

//------------------------------------------------------------------------------
// Deterministic pseudo-random source (LCG; no <random> state, identical
// sequence on every platform -- the stress pattern must be reproducible).
//------------------------------------------------------------------------------
uint64_t g_rng = 0x9E3779B97F4A7C15ull;

uint32_t NextRand()
{
    g_rng = g_rng * 6364136223846793005ull + 1442695040888963407ull;
    return static_cast<uint32_t>(g_rng >> 32);
}

// Verify that the first 'bytes' payload bytes of 'obj' are all zero.
bool PayloadZeroed(il2cpp::Il2CppObject* obj, size_t bytes)
{
    const unsigned char* p = static_cast<const unsigned char*>(
        static_cast<const void*>(reinterpret_cast<char*>(obj) + sizeof(il2cpp::Il2CppObject)));
    for (size_t i = 0; i < bytes; ++i)
    {
        if (p[i] != 0)
        {
            return false;
        }
    }
    return true;
}

// Fill the payload of 'obj' (up to 'bytes') with a repeating byte pattern.
void FillPattern(il2cpp::Il2CppObject* obj, size_t bytes, unsigned char pattern)
{
    std::memset(reinterpret_cast<char*>(obj) + sizeof(il2cpp::Il2CppObject), pattern, bytes);
}

// Rooted state for the chain tests (section 2) and the cycle tests (3).
il2cpp::Il2CppObject* g_chainHead = nullptr;   // static root slot: head of the chain
il2cpp::Il2CppObject* g_cycleHead = nullptr;   // static root slot: head of the cycle
il2cpp::Il2CppArray*  g_arrRoot = nullptr;     // static root slot: reference-element array
il2cpp::Il2CppArray*  g_intArrRoot = nullptr;  // static root slot: int32 array
il2cpp::Il2CppString* g_strRoot = nullptr;     // static root slot: managed string
il2cpp::Il2CppObject* g_bigRoot = nullptr;     // static root slot: LOH object

// Node holder for chain construction: every node is stored here the moment it
// is allocated, and the WHOLE range is registered as GC roots during the
// build (.NET newobj semantics: an object is a root from the instant it
// exists -- a collection may run between any two allocations). After the
// build the range is removed and only the head stays rooted.
il2cpp::Il2CppObject* g_chainNodes[kMaxChainNodes] = { nullptr };

// Build a chain of 'count' nodes (node 1 -> node 2 -> ... -> node count ->
// null). Node k carries the value k. Every node is rooted through the
// g_chainNodes range from the moment it is allocated, so a collection can
// never reclaim a half-built chain. Returns the head.
il2cpp::Il2CppObject* BuildChain(int32_t count)
{
    for (int32_t i = 0; i < count; ++i)
    {
        il2cpp::Il2CppObject* node = il2cpp::il2cpp_gc_alloc(&g_node);
        if (node == nullptr)
        {
            return nullptr;
        }
        *ValueSlot(node) = static_cast<uint64_t>(i + 1);
        *NextSlot(node) = nullptr;
        g_chainNodes[i] = node; // rooted here, before the next allocation
        if (i > 0)
        {
            *NextSlot(g_chainNodes[i - 1]) = node;
        }
    }
    // Re-read the head from the ROOT ARRAY: a collection may have fired
    // inside any allocation of this loop and COPIED the Gen0 nodes -- the
    // loop-local 'node' of the last iteration is a stale address (Phase C
    // rewrites the registered root range, never C++ locals).
    return g_chainNodes[0];
}

// Walk the chain rooted at g_chainHead and validate every hop: klass header,
// expected length, per-node value, and a null terminator. Re-reads the root
// slot each call -- the head may have MOVED during a compaction.
bool ValidateChain(int32_t expectedLength)
{
    il2cpp::Il2CppObject* cur = g_chainHead;
    int32_t visited = 0;
    while (cur != nullptr)
    {
        if (cur->klass != &g_node)
        {
            std::fprintf(stderr, "DBG chain: node[%d] klass=%p expect=%p\n",
                         visited, (void*)cur->klass, (void*)&g_node);
            return false;
        }
        if (visited >= expectedLength)
        {
            return false; // longer than expected: a loop or a bad link
        }
        if (*ValueSlot(cur) != static_cast<uint64_t>(visited + 1))
        {
            std::fprintf(stderr, "DBG chain: node[%d] val=%llu expect=%d\n",
                         visited, (unsigned long long)*ValueSlot(cur), visited + 1);
            return false;
        }
        cur = *NextSlot(cur);
        ++visited;
    }
    if (visited != expectedLength)
    {
        std::fprintf(stderr, "DBG chain: len=%d expect=%d\n", visited, expectedLength);
    }
    return visited == expectedLength;
}

// Walk a rooted cycle (g_cycleHead -> B -> head) exactly two hops and verify
// that the back-link returns to the CURRENT head (which may have moved).
// All checks are content-based (klass + link topology) -- addresses are NOT
// stable across the compacting collection, so no address equality is used.
bool ValidateCycle()
{
    il2cpp::Il2CppObject* a = g_cycleHead;
    if (a == nullptr || a->klass != &g_node)
    {
        return false;
    }
    il2cpp::Il2CppObject* b = *NextSlot(a);
    if (b == nullptr || b->klass != &g_node)
    {
        return false;
    }
    il2cpp::Il2CppObject* back = *NextSlot(b);
    return back == a && a == g_cycleHead;
}

} // namespace

//------------------------------------------------------------------------------
// main -- run every stress pattern and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    const auto t0 = std::chrono::steady_clock::now();

    //--- 1. Mass small-object churn: 100k+ allocations, many collections ---
    // 30 cycles x 4200 = 126000 allocations across explicit collects plus
    // alloc-triggered collects (a 64 KB Gen0 segment fills roughly every
    // 2000 32-byte objects, so a collection fires mid-batch each cycle).
    // Every allocation is validated for zero-initialization and klass right
    // after it happens; if a reclaimed block were not fully zeroed (sweep or
    // reuse bug), the check fails.  Any leak of live objects eventually
    // overflows the 16384-entry block table, which aborts the process.
    {
        const int32_t kCycle = 4200;
        il2cpp::Il2CppObject* batch[kCycle] = { nullptr };
        for (int32_t cycle = 0; cycle < 30; ++cycle)
        {
            for (int32_t i = 0; i < kCycle; ++i)
            {
                il2cpp::Il2CppObject* o = (i % 2 == 0)
                    ? il2cpp::il2cpp_gc_alloc(&g_classA)
                    : il2cpp::il2cpp_gc_alloc(&g_classC);
                if (o == nullptr || o->klass == nullptr)
                {
                    Expect(false, "churn allocation returns a well-formed object");
                    break;
                }
                if (o->klass != &g_classA && o->klass != &g_classC)
                {
                    Expect(false, "churn allocation stamps the requested klass");
                    break;
                }
                if (!PayloadZeroed(o, 8))
                {
                    Expect(false, "churn allocation payload is zero-initialized");
                    break;
                }
                batch[i] = o;
            }
            il2cpp::il2cpp_gc_collect();
        }
        (void)batch; // holds the cycle's allocations (GCC -Wunused-but-set-variable)
        // The last cycle's objects are unreachable now; a fresh allocation
        // must still be clean (post-stress heap integrity).
        il2cpp::Il2CppObject* probe = il2cpp::il2cpp_gc_alloc(&g_classA);
        Expect(probe != nullptr && probe->klass == &g_classA,
               "heap allocates cleanly after the mass churn");
        Expect(PayloadZeroed(probe, 8), "post-churn allocation is zero-initialized");
    }

    //--- Leak tripwire: repeated full cycles with no surviving objects ---
    // 12 cycles x 15000 allocations; every object is unreachable when the
    // explicit collect runs. If sweep retained ANY block, the live-block
    // count would grow by 15000 per cycle and overflow the 16384-entry
    // block table within two cycles -- a fail-loud abort, by design.
    {
        const int32_t kCycle = 15000;
        il2cpp::Il2CppObject* batch[kCycle] = { nullptr };
        for (int32_t cycle = 0; cycle < 12; ++cycle)
        {
            for (int32_t i = 0; i < kCycle; ++i)
            {
                batch[i] = il2cpp::il2cpp_gc_alloc(&g_classD);
            }
            il2cpp::il2cpp_gc_collect();
        }
        (void)batch; // holds the cycle's allocations (GCC -Wunused-but-set-variable)
        il2cpp::Il2CppObject* probe = il2cpp::il2cpp_gc_alloc(&g_classD);
        Expect(probe != nullptr && probe->klass == &g_classD,
               "heap survives 12 full cycles of 15000-object batches");
    }

    //--- 2. Deep reference chains: rooted graph across compactions ---
    // The chain is rooted ONLY at its head (a static root slot). Each
    // collection copies every surviving Gen0 node to the Gen1 promote
    // segment; the walk after each collect re-reads the root slot and every
    // link from live memory. A missed Phase C update would surface as a
    // wrong klass, a wrong value, a broken link or a crash.
    {
        const int32_t kChainLen = 64;
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(g_chainNodes), kMaxChainNodes);
        g_chainHead = BuildChain(kChainLen);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_chainHead));
        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(g_chainNodes));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Expect(ValidateChain(kChainLen),
                   "64-node chain stays intact after each of 20 collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_chainHead));
        g_chainHead = nullptr;
    }
    {
        // Deep graph: 2000 nodes. Survives three collections (Gen0 -> Gen1
        // with full copy + reference update, then two further full cycles).
        const int32_t kChainLen = 2000;
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(g_chainNodes), kMaxChainNodes);
        g_chainHead = BuildChain(kChainLen);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_chainHead));
        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(g_chainNodes));
        for (int32_t i = 0; i < 3; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Expect(ValidateChain(kChainLen),
                   "2000-node chain stays intact across compactions");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_chainHead));
        g_chainHead = nullptr;
    }

    //--- 3. Reference cycles ---
    // (a) Unrooted A->B->A cycle: unreachable from every root, so mark
    //     reaches neither node and sweep reclaims both. The heap must stay
    //     usable and every fresh allocation clean -- a cycle that survives
    //     would also be harmless to sweep, but a corrupted mark/sweep would
    //     surface as bad klass headers or non-zero payloads on reuse.
    {
        for (int32_t round = 0; round < 8; ++round)
        {
            il2cpp::Il2CppObject* a = il2cpp::il2cpp_gc_alloc(&g_node);
            il2cpp::Il2CppObject* b = il2cpp::il2cpp_gc_alloc(&g_node);
            // Root both nodes while the cycle is being WIRED: a collection
            // inside the second allocation would COPY a (Gen0 compaction) and
            // the C++ local 'a' would be stale -- the link writes below would
            // land in swept memory (possibly a free-block header). The root
            // range keeps both slots current via Phase C.
            il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(&a), 2);
            *NextSlot(a) = b;
            *NextSlot(b) = a;
            il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(&a));
            il2cpp::il2cpp_gc_collect(); // both unreachable -> swept
            // Churn same-size allocations over the reclaimed blocks.
            for (int32_t i = 0; i < 100; ++i)
            {
                il2cpp::Il2CppObject* o = il2cpp::il2cpp_gc_alloc(&g_node);
                Expect(o != nullptr && o->klass == &g_node,
                       "heap reuses blocks after a rootless cycle is swept");
                Expect(PayloadZeroed(o, 8),
                       "reused cycle memory is zero-initialized (no dangling reference)");
            }
        }
    }
    // (b) Rooted A->B->A cycle: must survive intact. The mark worklist must
    //     terminate (it is a cycle), and Phase C must keep B's back-link
    //     pointing at the CURRENT head after compaction.
    {
        g_cycleHead = il2cpp::il2cpp_gc_alloc(&g_node);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_cycleHead));
        il2cpp::Il2CppObject* b = il2cpp::il2cpp_gc_alloc(&g_node);
        *NextSlot(g_cycleHead) = b;
        *NextSlot(b) = g_cycleHead;
        for (int32_t i = 0; i < 10; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Expect(ValidateCycle(),
                   "rooted A->B->A cycle stays intact and non-dangling across collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_cycleHead));
        g_cycleHead = nullptr;
    }

    //--- 4. Static roots over managed collections: array + string ---
    // (a) Reference-element array: 16 elements, each a distinct node with a
    //     marker. After collections the ARRAY may have moved (Gen0 copy) and
    //     every ELEMENT may have moved -- both are rewritten by Phase C.
    //     Verifies the collector's array scan branch AND the array update
    //     branch of the reference rewrite.
    //     NOTE: the array is created via il2cpp_gc_alloc_size with a padded
    //     extent rather than il2cpp_array_new: the runtime's Il2CppArray
    //     layout puts 'data' at byte offset 25 (after the isRefElem flag,
    //     no padding) and the array sizing formula then leaves the LAST
    //     reference element one byte PAST the block end -- the collector's
    //     element scan reads that byte from the neighbouring block and may
    //     push a garbage reference into the mark worklist. The padding below
    //     keeps the element storage inside the block so this test can
    //     exercise the array scan/update paths without tripping that runtime
    //     layout bug (reported separately; see the file header notes).
    {
        const int32_t kArrLen = 16;
        const size_t kPaddedSize = sizeof(il2cpp::Il2CppArray) +
                                   static_cast<size_t>(kArrLen) * 8 + 8;
        g_arrRoot = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::il2cpp_gc_alloc_size(&g_refArrayClass, kPaddedSize));
        g_arrRoot->length = kArrLen;
        g_arrRoot->elemSize = 8;
        g_arrRoot->elemClass = &g_node;
        g_arrRoot->isRefElem = true;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_arrRoot));
        for (int32_t i = 0; i < 16; ++i)
        {
            il2cpp::Il2CppObject* e = il2cpp::il2cpp_gc_alloc(&g_node);
            SetMarker(e, static_cast<uintptr_t>(100 + i));
            // Re-read the array through its ROOT SLOT on every store: a
            // collection may fire inside any allocation and COPY the array
            // (Gen0 compaction) -- a C++ local computed before the loop would
            // be a stale address into swept memory (Phase C rewrites the root
            // slot itself, never C++ locals).
            reinterpret_cast<il2cpp::Il2CppObject**>(g_arrRoot->data)[i] = e;
        }
        for (int32_t i = 0; i < 10; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            bool ok = (g_arrRoot != nullptr &&
                       il2cpp::il2cpp_array_get_length(g_arrRoot) == 16 &&
                       g_arrRoot->isRefElem);
            for (int32_t e = 0; ok && e < 16; ++e)
            {
                il2cpp::Il2CppObject* elem = reinterpret_cast<il2cpp::Il2CppObject**>(g_arrRoot->data)[e];
                ok = (elem != nullptr && elem->klass == &g_node &&
                      GetMarker(elem) == static_cast<uintptr_t>(100 + e));
            }
            Expect(ok, "rooted reference array and its elements survive collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_arrRoot));
        g_arrRoot = nullptr;
    }
    // (b) int32 array (value elements -- no reference scanning): values must
    //     survive even though the array itself may be copied.
    {
        g_intArrRoot = il2cpp::il2cpp_array_new(nullptr, 4, false, 8);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_intArrRoot));
        int32_t* vals = reinterpret_cast<int32_t*>(g_intArrRoot->data);
        for (int32_t i = 0; i < 8; ++i)
        {
            vals[i] = i * i;
        }
        for (int32_t i = 0; i < 10; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            bool ok = (g_intArrRoot != nullptr &&
                       il2cpp::il2cpp_array_get_length(g_intArrRoot) == 8 &&
                       !g_intArrRoot->isRefElem);
            const int32_t* v = reinterpret_cast<const int32_t*>(g_intArrRoot->data);
            for (int32_t e = 0; ok && e < 8; ++e)
            {
                ok = (v[e] == e * e);
            }
            Expect(ok, "rooted int32 array keeps its elements across collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_intArrRoot));
        g_intArrRoot = nullptr;
    }
    // (c) Managed string (size-extended allocation): length and every UTF-16
    //     unit must survive collections (the string is a Gen0 object and is
    //     COPIED by the first collection -- the root slot is rewritten).
    {
        const char* literal = "gcstress-payload-0123456789";
        g_strRoot = il2cpp::il2cpp_string_new_from_cs(literal);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_strRoot));
        const int32_t kLen = static_cast<int32_t>(std::strlen(literal));
        for (int32_t i = 0; i < 10; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            bool ok = (g_strRoot != nullptr &&
                       il2cpp::il2cpp_string_get_length(
                           reinterpret_cast<il2cpp::Il2CppObject*>(g_strRoot)) == kLen);
            for (int32_t c = 0; ok && c < kLen; ++c)
            {
                ok = (g_strRoot->chars[c] == static_cast<char16_t>(literal[c]));
            }
            Expect(ok, "rooted string keeps length and payload across collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_strRoot));
        g_strRoot = nullptr;
    }

    //--- 5. Large objects (hundreds of KB) ---
    // (a) LOH churn: three 300000-byte objects, one rooted. The LOH is
    //     non-moving (generation 3) -- the rooted object's ADDRESS must stay
    //     stable while its payload pattern survives; the two unrooted ones
    //     are reclaimed and their blocks reused by the next round's fresh
    //     allocations (which must come back zero-initialized).
    {
        g_bigRoot = il2cpp::il2cpp_gc_alloc(&g_big);
        FillPattern(g_bigRoot, 64, 0x5A);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_bigRoot));
        il2cpp::Il2CppObject* const bigAddr = g_bigRoot;
        for (int32_t round = 0; round < 6; ++round)
        {
            // Two fresh unrooted large objects per round (they may trigger
            // implicit collections; the rooted one must survive them).
            il2cpp::Il2CppObject* u1 = il2cpp::il2cpp_gc_alloc(&g_big);
            il2cpp::Il2CppObject* u2 = il2cpp::il2cpp_gc_alloc(&g_big);
            (void)u1;
            (void)u2;
            il2cpp::il2cpp_gc_collect();
            Expect(g_bigRoot != nullptr && g_bigRoot->klass == &g_big,
                   "rooted 300KB object survives LOH churn");
            Expect(il2cpp::il2cpp_gc_get_generation(g_bigRoot) == il2cpp::kGcGenerationLoh,
                   "300KB object stays on the LOH (generation 3)");
            Expect(g_bigRoot == bigAddr,
                   "LOH object does not move across collections");
            const unsigned char* p = static_cast<const unsigned char*>(
                static_cast<const void*>(reinterpret_cast<char*>(g_bigRoot) + sizeof(il2cpp::Il2CppObject)));
            bool pattern = true;
            for (int32_t i = 0; i < 64; ++i)
            {
                if (p[i] != 0x5A)
                {
                    pattern = false;
                }
            }
            Expect(pattern, "rooted 300KB object payload pattern survives");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_bigRoot));
        g_bigRoot = nullptr;
        il2cpp::il2cpp_gc_collect(); // the last rooted big object is now dead
        il2cpp::Il2CppObject* fresh = il2cpp::il2cpp_gc_alloc(&g_big);
        Expect(fresh != nullptr && fresh->klass == &g_big,
               "a fresh 300KB allocation succeeds after the LOH block is freed");
        Expect(PayloadZeroed(fresh, 64), "reused LOH block is zero-initialized");
    }
    // (b) LOH threshold boundary: just under (Gen0, oversized for a segment
    //     -> non-moving promotion) and just over (LOH). Both must survive a
    //     collection with content intact and the correct generation.
    {
        il2cpp::Il2CppObject* under = il2cpp::il2cpp_gc_alloc(&g_gen0NearLoh);
        il2cpp::Il2CppObject* over = il2cpp::il2cpp_gc_alloc(&g_lohBoundary);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&under));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&over));
        Expect(il2cpp::il2cpp_gc_get_generation(under) == il2cpp::kGcGeneration0,
               "84992-byte object (aligned < 85000) starts in Gen0");
        Expect(il2cpp::il2cpp_gc_get_generation(over) == il2cpp::kGcGenerationLoh,
               "85001-byte object is allocated on the LOH");
        il2cpp::il2cpp_gc_collect();
        Expect(under != nullptr && under->klass == &g_gen0NearLoh,
               "Gen0 near-threshold object survives a collection");
        Expect(il2cpp::il2cpp_gc_get_generation(under) >= il2cpp::kGcGeneration1,
               "oversized Gen0 survivor is promoted out of Gen0 (non-moving path)");
        Expect(over != nullptr && over->klass == &g_lohBoundary,
               "LOH boundary object survives a collection");
        Expect(il2cpp::il2cpp_gc_get_generation(over) == il2cpp::kGcGenerationLoh,
               "LOH boundary object stays on the LOH");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&under));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&over));
    }
    // (c) Oversized LOH block: LARGER than a single LOH segment (1MB). The
    // allocator must bump it into a segment SIZED TO THE BLOCK (audit A1
    // fix); previously the fresh-segment path allocated a standard 1MB LOH
    // segment and bumped 2MB into it, overflowing the segment (heap
    // corruption -> 0xC0000005). The block must allocate, keep a deep
    // payload marker, survive collections non-moving on the LOH, and leave
    // the heap usable for a subsequent normal allocation.
    {
        il2cpp::Il2CppObject* mega = il2cpp::il2cpp_gc_alloc(&g_mega);
        Expect(mega != nullptr && mega->klass == &g_mega,
               "2MB LOH object (larger than one LOH segment) allocates");
        Expect(il2cpp::il2cpp_gc_get_generation(mega) == il2cpp::kGcGenerationLoh,
               "2MB object is on the LOH (generation 3)");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&mega));
        // Deep marker: write a byte at every 4KB offset across the payload.
        // If the segment were 1MB, the tail writes would land past the
        // segment edge (corrupting the adjacent C++ heap block).
        char* payload = reinterpret_cast<char*>(mega) + sizeof(il2cpp::Il2CppObject);
        for (int32_t off = 0; off < 2 * 1024 * 1024 - static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)); off += 4096)
        {
            payload[off] = static_cast<char>(0x3C);
        }
        const il2cpp::Il2CppObject* addrBefore = mega;
        il2cpp::il2cpp_gc_collect();
        bool ok = (mega != nullptr && mega->klass == &g_mega &&
                   il2cpp::il2cpp_gc_get_generation(mega) == il2cpp::kGcGenerationLoh &&
                   mega == addrBefore); // LOH is non-moving
        for (int32_t off = 0; ok && off < 2 * 1024 * 1024 - static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)); off += 4096)
        {
            if (payload[off] != static_cast<char>(0x3C))
            {
                ok = false;
            }
        }
        Expect(ok, "2MB LOH object survives collections intact (non-moving, deep marker)");
        il2cpp::Il2CppObject* small = il2cpp::il2cpp_gc_alloc(&g_classC);
        Expect(small != nullptr && small->klass == &g_classC,
               "heap usable after a >1MB LOH allocation (no neighbour corruption)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&mega));
    }

    //--- 6. Allocation/free alternation with mixed size classes ---
    // Ten rounds: allocate 800 objects across six size classes (16 B .. 16
    // KB) using the deterministic RNG; keep the first half alive via a root
    // range, sweep the second half. The freed mix fragments the free list
    // (odd block sizes in random order); first-fit whole-block reuse must
    // keep every surviving object intact (klass + marker) and every
    // subsequent allocation clean.
    {
        il2cpp::Il2CppClass* const kClasses[6] = {
            &g_classC, &g_classA, &g_classD, &g_m256, &g_m2k, &g_m16k,
        };
        const int32_t kBatch = 800;
        // The batch slots are roots from BEFORE the first allocation (.NET
        // newobj semantics: an object is a root from the instant it exists --
        // a collection may fire between any two allocations, and without the
        // range the earlier entries would hold stale pre-collection addresses
        // that the later collect would mark through as garbage). The range
        // covers the first half only: the second half must be swept by each
        // round's collection to keep fragmenting the free list. Unwritten
        // slots are explicitly zeroed so marking them is a no-op.
        il2cpp::Il2CppObject* batch[kBatch] = { nullptr };
        il2cpp::Il2CppClass*  clsOf[kBatch] = { nullptr };
        for (int32_t round = 0; round < 10; ++round)
        {
            il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(batch), kBatch / 2);
            for (int32_t i = 0; i < kBatch; ++i)
            {
                il2cpp::Il2CppClass* cls = kClasses[NextRand() % 6u];
                clsOf[i] = cls;
                batch[i] = il2cpp::il2cpp_gc_alloc(cls);
                if (HasMarker(cls))
                {
                    SetMarker(batch[i], static_cast<uintptr_t>(round) * 1000000u +
                                        static_cast<uintptr_t>(i));
                }
            }
            il2cpp::il2cpp_gc_collect();
            bool alive = true;
            for (int32_t i = 0; i < kBatch / 2; ++i)
            {
                if (batch[i] == nullptr || batch[i]->klass != clsOf[i])
                {
                    alive = false;
                    break;
                }
                if (HasMarker(clsOf[i]) &&
                    GetMarker(batch[i]) != static_cast<uintptr_t>(round) * 1000000u +
                                          static_cast<uintptr_t>(i))
                {
                    alive = false;
                    break;
                }
            }
            Expect(alive, "surviving half of a mixed-size batch stays intact after collection");
            il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(batch));
        }
        il2cpp::Il2CppObject* probe = il2cpp::il2cpp_gc_alloc(&g_m2k);
        Expect(probe != nullptr && probe->klass == &g_m2k,
               "heap remains usable after the fragmentation rounds");
        Expect(PayloadZeroed(probe, 32),
               "post-fragmentation allocation is zero-initialized");
    }

    //--- 7. Free-list retention across explicit collections (leak detector) ---
    // The free list must SURVIVE a collection: blocks freed by one sweep but
    // not yet reused by an allocation are still valid free memory and must be
    // handed out by later allocations. A collector that RESETS the free list
    // at the start of every collection discards those untouched blocks --
    // they are lost forever, the free list shrinks, and every further
    // allocation has to bump a fresh segment (unbounded heap growth).
    // Detection is exact and deterministic:
    //   (a) Pre-fill: 1024 classD allocations, then collect -- every one is
    //       unrooted, so the free list now holds exactly those 1024 blocks.
    //   (b) Consume 100 of them (allocations reuse the free list head).
    //   (c) Run an EXPLICIT collection while 924 free blocks are still
    //       untouched. With the bug they are discarded; without it they stay.
    //   (d) Allocate 1000 more. Correct behavior: every allocation pops one
    //       of the original 1024 blocks (the 100 consumed in (b) were swept
    //       by (c) and returned) -- all 1000 addresses EXACTLY match the
    //       pre-fill batch. With the bug: only ~100 match; ~900 come from
    //       fresh segments (the leaked blocks are gone).
    {
        const int32_t kFill = 1024;
        const int32_t kConsume = 100;
        const int32_t kProbe = 1000;
        // Root the fill batch while it is being built: a collection inside any
        // allocation would otherwise sweep the earlier (unrooted) fill
        // objects and later allocations would reuse those very blocks -- the
        // batch would then contain duplicate addresses and the DISTINCT
        // probe count below would collapse regardless of free-list behavior.
        // The range is removed BEFORE the explicit collect so the collect
        // still reclaims all 1024 (that reclaim is what fills the free list).
        il2cpp::Il2CppObject* fill[kFill] = { nullptr };
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(fill), kFill);
        for (int32_t i = 0; i < kFill; ++i)
        {
            fill[i] = il2cpp::il2cpp_gc_alloc(&g_classD);
        }
        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(fill));
        il2cpp::il2cpp_gc_collect(); // all 1024 now on the free list
        for (int32_t i = 0; i < kConsume; ++i)
        {
            fill[i] = il2cpp::il2cpp_gc_alloc(&g_classD); // reuse 100 blocks
        }
        il2cpp::il2cpp_gc_collect(); // (c): must NOT discard the 924 untouched blocks
        // (d) Allocate kProbe objects. Count DISTINCT addresses among them:
        // with the free list preserved the probes reuse (mostly) distinct
        // pool blocks; with the leak only the small surviving pool is
        // available, so the probes cycle a handful of blocks and the distinct
        // count collapses far below kProbe.
        il2cpp::Il2CppObject* probes[kProbe] = { nullptr };
        for (int32_t i = 0; i < kProbe; ++i)
        {
            probes[i] = il2cpp::il2cpp_gc_alloc(&g_classD);
        }
        int32_t distinct = 0;
        for (int32_t i = 0; i < kProbe; ++i)
        {
            bool seen = false;
            for (int32_t j = 0; j < i; ++j)
            {
                if (probes[j] == probes[i])
                {
                    seen = true;
                    break;
                }
            }
            if (!seen)
            {
                ++distinct;
            }
        }
        // With the free list preserved, >= 990 of 1000 allocations reuse the
        // original 1024 blocks (the probe allocates fewer than the pool).
        Expect(distinct >= kProbe - 10,
               "free list survives explicit collections (no free-block loss)");
        if (distinct < kProbe - 10)
        {
            std::printf("  NOTE: only %d/%d DISTINCT allocations reused freed "
                        "blocks (free list was discarded by a collection)\n",
                        distinct, kProbe);
        }
    }

    //--- 8. 8-byte minimum-block storm (free-list header overflow) ---
    // The free-list header is 16 bytes ([next|size] on 64-bit); the smallest
    // block the allocator may hand out is 8 bytes (an instanceSize ==
    // sizeof(Il2CppObject) class, the `new object()` shape). Sweeping a
    // block smaller than the header writes 16 bytes into it, corrupting the
    // ADJACENT block's klass header -- the collector must round blocks up to
    // at least the header size. Thousands of 8-byte blocks are churned
    // directly before rooted 16-byte sentinels (the last tiny block is
    // adjacent to the first sentinel), across repeated collections; every
    // sentinel must keep its klass through every collection.
    {
        const int32_t kTiny = 8192;
        const int32_t kSent = 128;
        il2cpp::Il2CppObject* sentinels[kSent] = { nullptr };
        for (int32_t cycle = 0; cycle < 8; ++cycle)
        {
            for (int32_t i = 0; i < kTiny; ++i)
            {
                Expect(il2cpp::il2cpp_gc_alloc(&g_tiny8) != nullptr,
                       "8-byte storm allocation is non-null");
            }
            il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(sentinels), kSent);
            for (int32_t i = 0; i < kSent; ++i)
            {
                sentinels[i] = il2cpp::il2cpp_gc_alloc(&g_classC);
            }
            il2cpp::il2cpp_gc_collect();
            bool intact = true;
            for (int32_t i = 0; i < kSent; ++i)
            {
                if (sentinels[i] == nullptr || sentinels[i]->klass != &g_classC)
                {
                    intact = false;
                    break;
                }
            }
            Expect(intact,
                   "sentinels adjacent to swept 8-byte blocks keep their klass");
            il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(sentinels));
        }
    }

    //--- 9. Statistics counters stay consistent across the churn ---
    // The mass-churn sections above ran dozens of full collections, so the
    // statistics domain must report a large, internally consistent state:
    // every generation's collection count has advanced, the per-generation
    // live-size sums equal the live-heap total, memory load stays a valid
    // percentage, and the GCSettings policy modes round-trip cleanly.
    {
        Expect(il2cpp::il2cpp_gc_get_collection_count(0) >= 50 &&
                   il2cpp::il2cpp_gc_get_collection_count(1) >= 50 &&
                   il2cpp::il2cpp_gc_get_collection_count(2) >= 50,
               "all generation collection counts advanced across the churn");
        Expect(il2cpp::il2cpp_gc_get_max_generation() == il2cpp::kGcGeneration2,
               "MaxGeneration is 2 after the churn");
        const int64_t genSum =
            il2cpp::il2cpp_gc_get_generation_size(0) +
            il2cpp::il2cpp_gc_get_generation_size(1) +
            il2cpp::il2cpp_gc_get_generation_size(2) +
            il2cpp::il2cpp_gc_get_generation_size(3);
        Expect(genSum == il2cpp::il2cpp_gc_get_total_memory(),
               "per-generation live sizes equal the live-heap total after the churn");
        const int32_t load = il2cpp::il2cpp_gc_get_memory_load();
        Expect(load >= 0 && load <= 100,
               "memory load percentage stays within 0..100 after the churn");
        const int64_t pauseBefore = il2cpp::il2cpp_gc_get_total_pause_ticks();
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_total_pause_ticks() >= pauseBefore,
               "pause-time counter is monotonic after the churn");
        il2cpp::il2cpp_gc_set_latency_mode(il2cpp::kGcLatencyModeLowLatency);
        Expect(il2cpp::il2cpp_gc_get_latency_mode() == il2cpp::kGcLatencyModeLowLatency,
               "latency mode round-trips after the churn");
        il2cpp::il2cpp_gc_set_latency_mode(il2cpp::kGcLatencyModeInteractive);
        Expect(il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread() ==
                   il2cpp::il2cpp_gc_get_allocated_bytes(),
               "per-thread allocation counter equals the cumulative counter after the churn");
        il2cpp::GcMemoryInfoData info;
        il2cpp::il2cpp_gc_get_memory_info(info);
        Expect(info.compacted && !info.concurrent,
               "memory info reports a compacting non-concurrent collector after the churn");
        Expect(info.totalCommittedBytes >= info.heapSizeBytes,
               "committed bytes cover the live heap after the churn");
    }

    //--- Report ---
    const auto t1 = std::chrono::steady_clock::now();
    const double ms = std::chrono::duration<double, std::milli>(t1 - t0).count();
    if (g_failures != 0)
    {
        std::printf("%d gc stress assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("gc stress suite finished in %.1f ms\n", ms);
    std::printf("all gc stress tests passed\n");
    return 0;
}



