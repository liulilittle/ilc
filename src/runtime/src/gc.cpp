//------------------------------------------------------------------------------
// gc.cpp -- mark-sweep garbage collector (AOT runtime style).
//
// Design (M2 conservative correctness, non-moving, no write barrier -- see
// the M2 plan for the rationale and the M3 upgrade path):
//
//   Heap layout
//     The heap is a singly linked list of 64KB segments. Within a segment,
//     objects are bump-allocated upward from the start of the payload area;
//     every allocation is zero-initialized before its klass header is stamped.
//     A segment that cannot satisfy a request triggers collection; if a fresh
//     segment is still needed it is appended to the list (segments are never
//     returned to the OS in M2 -- non-moving heap).
//
//   Block bookkeeping
//     Every live allocation is recorded in a side table
//     (g_blockTable: fixed-capacity static array of {object, size}). This
//     table is the collector's authoritative view of the heap: it tells sweep
//     exactly which bytes each block occupies WITHOUT having to read memory
//     that a free-list header may have overwritten (the classic
//     inline-free-header ambiguity: a reclaimed block's klass pointer is
//     destroyed by the FreeBlock [next|size] header, so sweep must never infer
//     block extents from object memory -- it walks the block table instead).
//
//   Zero heap-allocation rule (project constraint #12)
//     The collector manages the managed heap, so its OWN bookkeeping must not
//     live on the C++ heap (a chicken-and-egg dependency, and a crash source
//     under memory pressure). Every structure below is a fixed-capacity
//     STATIC array with explicit bounds checks that fail loud (abort) instead
//     of corrupting state -- the same pattern the type registry uses. There is
//     no std::vector, no std::unordered_map, no operator new anywhere in the
//     runtime's hot paths. Generated code registers a compile-time bounded
//     root set, and the block table's capacity bounds the maximum LIVE object
//     count (sweep compacts the table in place, so garbage never occupies a
//     slot).
//
//   Free list
//     Unreachable blocks recovered by sweep are chained into a singly linked
//     free list. Each free block carries its own size inline (the first two
//     words of the block: [next | size]). Allocation is first-fit over the
//     free list; an oversized block is used whole (no splitting in M2 --
//     internal fragmentation is accepted for simplicity; compaction arrives
//     with the generational collector). A re-allocated free block is
//     re-registered in the block table with its true physical extent.
//
//   Mark
//     Mark state is a side table (a flat open-addressing hash of pointer ->
//     0/1, fixed static storage) -- the object header carries no GC bits
//     (header layout is frozen to a single klass pointer; a generational
//     header redesign is M3).
//     The root set is:
//       * registered static reference fields (il2cpp_gc_add_static_root)
//       * registered reference-slot ranges (il2cpp_gc_add_root_range)
//     Marking is iterative with a fixed-capacity static worklist (stack).
//     Each object is scanned via klass->refFieldBitmap: bit i set <=> the
//     pointer slot at byte offset i*sizeof(void*) inside the object is a GC
//     reference. Slot 0 (byte offset 0) is the klass header itself -- a
//     pointer to a static descriptor, never a heap reference -- so scanning
//     starts at slot 1. Scanning also stops at the object's physical
//     slot limit (instanceSize / sizeof(void*)) so a bitmap bit beyond the
//     object can never read into the neighbouring block.
//
//   Sweep
//     Iterate the block table with std::remove_if semantics IN PLACE: marked
//     blocks are kept and packed to the front; unmarked blocks are linked into
//     the free list. Sweep is O(#live+#freed) and needs no allocation.
//
//   Trigger
//     Allocation consults the free list first, then the current segment. If
//     neither can satisfy the request, a collection runs once and the request
//     is retried (freshly reclaimed blocks almost always fit); a second
//     failure allocates a fresh segment. The collector is also exposed
//     directly (il2cpp_gc_collect) for explicit/forced collection.
//
//   Constraints honored
//     * No per-object metadata in the object header (side tables only).
//     * No reference counting -- bare GC pointers only (project rule).
//     * No C++ heap in the collector: fixed static tables + fail-loud bounds.
//------------------------------------------------------------------------------

#include "il2cpp_gc.h"

#include "il2cpp_array.h"
#include "il2cpp_frame.h"
#include "il2cpp_metadata.h"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <algorithm>
#include <mutex>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Segment -- one contiguous region of managed heap.
//------------------------------------------------------------------------------
// 2026-09-23 (perf): raised 64KB -> 256KB -> 1MB -- the small Gen0 segment
// forced a full collection roughly every ~600 small allocations (sbappend/
// strjoin allocate 10^4-10^5 short-lived strings), and each collection
// compacts + runs the linear Phase-C forward map; 1MB cuts collection
// frequency ~16x vs the original. The oversized-block path (>capacity,
// below LOH) and test_gcstress's "84992-byte object starts in Gen0" contract
// are unaffected (84992 < 1MB bumps in a standard Gen0 segment; >1MB
// survivors still promote non-moving via the sweep's oversized path).
constexpr size_t kSegmentPayload  = 1024 * 1024;  // bytes of object area per segment (Gen0/1/2)
// 2026-09-23 (perf): bumped-bytes collection threshold -- after this many
// bytes are bumped without a collection, the next allocation runs a
// collection before bumping (bounds heap growth; implicit collection under
// realistic allocation pressure). Sized below the frame-deep pressure test
// (~30k allocations, >=256KB) so that test's implicit-collection contract
// holds; the bucketed free list keeps the resulting collections cheap.
constexpr size_t kCollectAfterBumpedBytes = 256 * 1024;
constexpr size_t kLohSegmentPayload = 1024 * 1024; // bytes per LOH segment (large objects)

struct Segment
{
    Segment* next;      // next segment in the heap list (null for the last)
    int32_t  generation; // 0=Gen0 bump, 3=LOH (M3: segments are either Gen0 or LOH;
                         // promoted objects stay in their original segment -- the
                         // generation lives on the BLOCK record, see BlockEntry)
    size_t   used;      // bytes of payload consumed so far (bump high-water mark)
    size_t   capacity;  // payload capacity in bytes (kSegmentPayload or kLohSegmentPayload)
    char     data[1];   // object payload; allocated as capacity bytes (offsetof pattern)
};

//------------------------------------------------------------------------------
// FreeBlock -- a recovered block linked into the free list. The [next, size]
// words live IN the block's payload (the block is not a live object), so no
// extra header is needed to chain free blocks.
//------------------------------------------------------------------------------
struct FreeBlock
{
    FreeBlock* next; // next free block (null for the last)
    size_t     size; // usable payload bytes of this block
    // block payload follows inline
};

// Minimum physical extent of any block. Sweep writes the FreeBlock header
// [next|size] INTO every reclaimed block, so a block smaller than the header
// would make that write spill past the block edge into the adjacent block's
// klass header (out-of-bounds overwrite -> corrupted klass -> Phase C crash).
constexpr size_t kMinBlockSize = sizeof(FreeBlock);

// Block bookkeeping entry: one per live allocation.
struct BlockEntry
{
    Il2CppObject* obj;        // block address (also the object address)
    size_t        size;       // TRUE physical extent of the block in bytes
    int32_t       generation; // current generation (0/1/2/3); promoted by each collection
};

//------------------------------------------------------------------------------
// ForwardMap -- old->new address table for the compacting collection
// (M3 Task 4). During a young-generation collection, surviving Gen0 blocks
// are COPIED to a fresh Gen1 segment; the old address -> new address mapping
// is recorded here so every reference (roots, object fields) can be updated
// after the copy. The map is valid only during one collection cycle (built
// in the copy phase, consumed by the reference-update phase, then cleared).
// Fixed static storage (constraint #12), fail-loud on overflow.
//------------------------------------------------------------------------------
// 2026-08-13 (stackqueue2 e2e): raised 16384 -> 65536 -- the S6 probe keeps
// 20000 boxed ints live through a full collection (all gen0 survivors are
// copied), which overflowed the old bound at the collection after GC.Collect().
constexpr int32_t kMaxForwardEntries = 65536;

struct ForwardEntry
{
    Il2CppObject* oldAddr;
    Il2CppObject* newAddr;
};

ForwardEntry g_forwardMap[kMaxForwardEntries];
int32_t      g_forwardCount = 0;
// 2026-09-23 (perf): the forward map is sorted by oldAddr before Phase C so
// ForwardLookup runs in O(log n); the map is rebuilt (and the flag cleared)
// at the start of every collection.
bool g_forwardMapSorted = false;

// Segment receiving promoted (copied) Gen0 survivors during compaction.
Segment*     g_promoteSegment = nullptr;

//------------------------------------------------------------------------------
// Fixed-capacity static tables (zero heap allocation, constraint #12).
//------------------------------------------------------------------------------
// 2026-08-13 (stackqueue2 e2e): raised 16384 -> 65536 (4x) -- the block
// table bounds the maximum simultaneously LIVE objects, and the e2e sweep's
// S6 probe keeps 20000 boxed ints alive, which overflowed the old bound.
// The worklist is the same order as the live set (a breadth-first mark can
// hold the whole live set at once), so it is raised in lockstep.
// 2026-09-23 (perf): raised 65536 -> 524288 (8x) -- the fresh-segment GC
// change (collect switches to a new Gen0 segment) lengthens the interval
// between collections to roughly one segment's worth of allocations, so the
// block table must hold all allocations of one interval (dead blocks are
// only removed at the next collection). 512K live slots is far above any
// measured workload (largest probes stay under ~100K).
constexpr int32_t kMaxBlocks     = 524288; // max simultaneously LIVE objects
constexpr int32_t kMaxWorklist   = 65536; // max objects marked in one cycle

BlockEntry g_blockTable[kMaxBlocks];
int32_t    g_blockCount = 0;

// 2026-09-23 (perf): the block table is kept address-sorted for O(log n)
// containment/generation lookups on the store-barrier hot path. RegisterBlock
// appends (unsorted) and collection compacts in place; the flag is cleared on
// both, and the next lookup re-sorts once (amortized O(n log n) per
// allocation/collection boundary).
bool g_blockTableSorted = false;

void EnsureBlockTableSorted()
{
    if (g_blockTableSorted)
    {
        return;
    }
    std::sort(g_blockTable, g_blockTable + g_blockCount,
              [](const BlockEntry& a, const BlockEntry& b) { return a.obj < b.obj; });
    g_blockTableSorted = true;
}

Il2CppObject* g_worklist[kMaxWorklist];
int32_t       g_worklistCount = 0;

// Mark side table: object pointer -> 0/1. Flat open-addressing hash table in
// fixed static storage; cleared at the start of every collection. A per-block
// bitmap is the M3 optimization; this table keeps M2 correct and inspectable.
struct MarkTable
{
    static constexpr size_t kCapacity = 1 << 17; // must be a power of two
                                                 // (raised 1<<16 -> 1<<17 on
                                                 // 2026-08-13 with kMaxBlocks:
                                                 // worst-case mark set == the
                                                 // live set, <= kMaxBlocks, so
                                                 // max load stays <= 50%)
    Il2CppObject* keys[kCapacity];
    uint8_t       values[kCapacity];

    void Reset()
    {
        std::memset(keys, 0, sizeof(keys));
        std::memset(values, 0, sizeof(values));
    }

    static size_t Hash(Il2CppObject* p)
    {
        // SplitMix-style finalizer on the pointer value: mixes low bits so the
        // linear table spreads heap pointers (which tend to be low-bit-sparse)
        // across the capacity without pathological clustering.
        uint64_t z = reinterpret_cast<uintptr_t>(p);
        z ^= z >> 30;
        z *= 0xbf58476d1ce4e5b9ull;
        z ^= z >> 27;
        z *= 0x94d049bb133111ebull;
        z ^= z >> 31;
        return static_cast<size_t>(z) & (kCapacity - 1);
    }

    bool Get(Il2CppObject* p) const
    {
        size_t i = Hash(p);
        while (keys[i] != nullptr)
        {
            if (keys[i] == p)
            {
                return values[i] != 0;
            }
            i = (i + 1) & (kCapacity - 1);
        }
        return false;
    }

    void Set(Il2CppObject* p)
    {
        size_t i = Hash(p);
        while (keys[i] != nullptr)
        {
            if (keys[i] == p)
            {
                values[i] = 1;
                return;
            }
            i = (i + 1) & (kCapacity - 1);
        }
        keys[i] = p;
        values[i] = 1;
    }
};

//------------------------------------------------------------------------------
// Heap state (all zero-initialized static storage; nothing here runs before
// main). il2cpp_gc_init initializes the first segment lazily.
//------------------------------------------------------------------------------
Segment*     g_segments = nullptr; // heap list head (null until first init)
Segment*     g_current  = nullptr; // Gen0 segment accepting bump allocations
Segment*     g_lohCurrent = nullptr; // LOH segment accepting bump allocations
MarkTable    g_mark;               // mark side table (cleared each collect)
// 2026-09-23 (perf): the free list is segmented into size buckets so the
// first-fit allocation walk stays short: each bucket holds blocks of a
// similar size class (powers of two from 64B), so the first qualifying
// block is typically the head and a bucket miss walks at most a few
// entries. This keeps the dead-space reuse semantics (recycled addresses
// are handed out again) while removing the O(#dead) walk that dominated
// allocation in sbappend-style workloads.
constexpr int32_t kFreeBuckets = 14; // 64B .. 8MB, then one overflow bucket
FreeBlock*   g_freeBuckets[kFreeBuckets] = {};
size_t       g_freeBytes = 0;      // total bytes recoverable on the free list
// 2026-09-23 (perf): bytes bumped since the last collection. When a
// segment is exhausted and no free block fits, a bumped-bytes threshold
// triggers a collection instead of always opening another segment (bounds
// heap growth while keeping the fast bump path dominant).
size_t       g_bumpedSinceCollect = 0;

// Size class of a block: bucket 0 = <=64B, then powers of two, overflow
// bucket holds everything above 8MB.
int32_t SizeBucket(size_t size)
{
    int32_t b = 0;
    size_t upper = 64;
    while (size > upper && b < kFreeBuckets - 1)
    {
        upper <<= 1;
        ++b;
    }
    return b;
}

// Root registrations: fixed-size arrays with a running count (constraint #12:
// STATIC storage, never malloc / never a dynamic container). The capacity is a
// compile-time bound on how many roots a program may register; it is NOT a
// .NET semantic (net48 has no such table).
//
// The earlier 128/64 were an artificial ceiling: every CACHED lambda
// (the Roslyn `<>c` display-class singleton) is one static reference field and
// therefore one static root, so a real program that caches 129 lambdas aborted
// the process with "il2cpp_gc: static root table overflow". Raised to 4096 /
// 1024 (32 KB + 16 KB of .bss) so ordinary programs never hit it; the overflow
// branch below still FAILS LOUD rather than silently dropping a root.
//
// kMaxRootRanges = 16384 (2026-10-31, gc-roots hardening). The 1024 entries
// were reached by real programs: one range is registered per live XmlWriter,
// per String.Concat(IEnumerable) call and per HandleRef P/Invoke argument
// (il2cpp_handleref.h roots its wrapper slot for the call), and a recursive
// call chain with a HandleRef argument holds one range per native frame. The
// probe (tests/runtime/test_gcrootrange_race.cpp) measures the native cost of
// one nested range per level; scaling that to the 1 MiB default thread stack
// puts the deepest reachable nesting well above 1024, so the old bound aborted
// an ordinary deep call chain. 16384 costs 256 KiB of .bss (16 bytes/entry,
// against 8 MiB for g_blockTable) and leaves >=2x margin over the measured
// stack-imposed nesting limit. The overflow branch below still FAILS LOUD:
// silently dropping a root would leave dangling pointers, which is strictly
// worse than aborting, and an unbounded dynamic table would make OOM behavior
// undefined (constraint #12: static storage only).
constexpr int32_t kMaxStaticRoots = 4096;
constexpr int32_t kMaxRootRanges  = 16384;

void**      g_staticRoots[kMaxStaticRoots];
int32_t     g_staticRootCount = 0;

struct RootRange
{
    void**  begin;
    int32_t count;
};
RootRange   g_rootRanges[kMaxRootRanges];
int32_t     g_rootRangeCount = 0;

// Serializes every access to the root-range table above (2026-10-31, gc-roots).
//
// WHY: the table is process-global mutable state. Managed worker threads are
// real OS threads (threadobj.cpp ThreadTrampoline -> plat_thread_spawn) and
// register/unregister ranges from ordinary runtime calls:
//   * il2cpp_handleref.h:110  -- one slot per HandleRef P/Invoke argument
//   * stringext2.cpp:1141     -- String.Concat(IEnumerable) (per call)
//   * xml_write.cpp:241/517   -- one slot per live XmlWriter
//   * dependhandle.cpp:63, appdomain.cpp:383 -- one-shot registrations
// while the collector enumerates the SAME table during mark and during the
// compaction reference update. Without a lock, concurrent add/add overwrites an
// entry (a silently lost root -> premature reclamation) and remove/remove drives
// the count negative (out-of-bounds static writes) or inflated.
//
// LOCK DISCIPLINE (D11 -- no deadlock):
//   * Acquired in: il2cpp_gc_add_root_range, il2cpp_gc_remove_root_range,
//     the diagnostic getters, and Collect() from the root-range mark loop
//     through the end of the cycle (so a range can neither appear between the
//     mark scan and the sweep nor vanish while Phase C rewrites its slots; a
//     second concurrent collection also serializes here).
//   * While held, the collector executes only pure table/heap bookkeeping:
//     no managed code, no finalizer callback (RunPendingFinalizers runs BEFORE
//     the lock is taken), no I/O. It never acquires another runtime lock; the
//     only lock that can be taken underneath is the CRT malloc lock in
//     AllocSegment, which is a leaf (malloc never calls back into the runtime).
//     The only I/O reachable while the lock is held is FailLoud's stderr write
//     on a fatal invariant violation (immediately followed by abort(), so it
//     cannot participate in a lock cycle); il2cpp_gc_add_root_range reports its
//     own overflow AFTER releasing the lock (see below).
//   * Callers of add/remove hold no runtime lock at the call site (verified at
//     every caller listed above), so no thread acquires this mutex while
//     holding another runtime lock.
//   Therefore the lock-order graph is {this mutex} -> {CRT malloc lock} only:
//   it has a cycle-free topology.
//
// SCOPE (honest limitation): this makes the root-range TABLE internally
// consistent and keeps a registered range's target marked for the whole cycle.
// It does NOT make the collector a stop-the-world collector: the M3 allocator
// and the static-root table are still single-threaded by design, and the
// collector scans only the CALLING thread's frame stack (framestack.cpp:13-20).
// A full concurrent collector is a separate architecture task.
std::mutex  g_rootRangeMutex;

bool g_initialized = false;

// Monotonic interval anchor for the "percent time in GC" statistic:
// g_intervalStart is the wall-clock instant the previous interval began. It
// is anchored in EnsureInitialized (before the first collection) and reset by
// RecordCollectionEnd after every collection (single writer, D1).
std::chrono::steady_clock::time_point g_intervalStart;

//------------------------------------------------------------------------------
// Card table (M3 Task 3 -- write barrier support).
//
// The card table tracks old->young references so a young-only collection can
// find every reference from the old generation into the young generation
// WITHOUT scanning the whole old heap. One card per 4KB of address space
// (a byte per card; 1 GB of addressable heap = 1 MB of cards -- static bss).
//
//   card index = address >> 12
//   card value 0/1: 1 means "this 4KB region contains an old object that was
//   written to since the last collection" (a potential old->young reference).
//
// The write barrier (il2cpp_write_barrier) is called by generated code before
// every reference-field store; it marks the card of the STORING object when
// that object is in an old generation (>= 1). Gen0 (young) objects need no
// card mark -- they are scanned wholesale by every collection anyway.
//
// M3 note: the collector currently performs full-heap collection (mark from
// ALL roots), so the card table does not change correctness yet -- it is the
// data surface for the young-only collection optimization (Task 4) and for
// the compacting collection's reference update. The barrier + card marking
// are exercised by tests in this task; the scan integration arrives with
// Task 4.
//------------------------------------------------------------------------------
constexpr uintptr_t kCardShift   = 12;            // 4 KB per card
// The card table is a FIXED-SIZE hash-indexed table: the card index is
// (address >> 12) MASKED to the table size. Aliasing (two regions sharing a
// card) is SAFE: a marked card causes the collector to scan the whole 4KB
// region containing the written slot (conservative over-scan); it can never
// MISS a written region. A 4 MB table covers the address space at 4 KB
// granularity with bounded aliasing.
constexpr size_t    kCardTableBytes = (4u << 20); // 4 MB of cards (hash-indexed)
uint8_t g_cardTable[kCardTableBytes];

//------------------------------------------------------------------------------
// MarkCardForAddress -- set the card covering 'addr' (no-op on null).
//------------------------------------------------------------------------------
void MarkCardForAddress(const void* addr)
{
    if (addr == nullptr)
    {
        return;
    }
    const uintptr_t index = (reinterpret_cast<uintptr_t>(addr) >> kCardShift) & (kCardTableBytes - 1);
    g_cardTable[index] = 1;
}

//------------------------------------------------------------------------------
// ClearCardTable -- reset every card to 0 (called at the start of each
// collection: cards are "dirty since last collection" markers).
//------------------------------------------------------------------------------
void ClearCardTable()
{
    std::memset(g_cardTable, 0, sizeof(g_cardTable));
}

//------------------------------------------------------------------------------
// FailLoud -- report a collector invariant violation and abort. Every call
// site names the violated bound so the failure is diagnosable from the log.
//------------------------------------------------------------------------------
void FailLoud(const char* what)
{
    std::fprintf(stderr, "il2cpp_gc: %s\n", what);
    std::abort();
}

//------------------------------------------------------------------------------
// AllocSegmentWithCapacity -- allocate and append one fresh segment with an
// explicit payload capacity.
//   generation: 0 (Gen0) or 3 (LOH) -- selects the standard capacity when the
//   caller does not override it (see AllocSegment).
//   capacity  : the payload capacity in bytes (must be pointer-aligned).
//------------------------------------------------------------------------------
Segment* AllocSegmentWithCapacity(int32_t generation, size_t capacity)
{
    Segment* seg = static_cast<Segment*>(
        std::malloc(offsetof(Segment, data) + capacity));
    if (seg == nullptr)
    {
        FailLoud("out of memory allocating segment");
    }
    seg->next = nullptr;
    seg->generation = generation;
    seg->used = 0;
    seg->capacity = capacity;
    if (g_segments == nullptr)
    {
        g_segments = seg;
        g_current  = seg;
    }
    else
    {
        Segment* tail = g_segments;
        while (tail->next != nullptr)
        {
            tail = tail->next;
        }
        tail->next = seg;
    }
    if (generation == kGcGenerationLoh)
    {
        g_lohCurrent = seg;
    }
    return seg;
}

//------------------------------------------------------------------------------
// AllocSegment -- allocate and append one fresh segment using the STANDARD
// payload capacity for the generation (64KB for Gen0/1/2, 1MB for LOH).
//------------------------------------------------------------------------------
Segment* AllocSegment(int32_t generation)
{
    const size_t capacity = (generation == kGcGenerationLoh) ? kLohSegmentPayload : kSegmentPayload;
    return AllocSegmentWithCapacity(generation, capacity);
}

//------------------------------------------------------------------------------
// EnsureInitialized -- create the first segment on first use.
//------------------------------------------------------------------------------
void EnsureInitialized()
{
    if (g_initialized)
    {
        return;
    }
    g_mark.Reset();
    AllocSegment(kGcGeneration0); // Gen0 segment; LOH segment created on demand
    // Anchor the "percent time in GC" interval at the collector's first use
    // so the first collection's wall-clock baseline is not the process epoch.
    g_intervalStart = std::chrono::steady_clock::now();
    g_initialized = true;
}

//------------------------------------------------------------------------------
// AlignUp -- round a byte count up to the pointer alignment the allocator
// guarantees for every block (keeps refFieldBitmap slot reads aligned).
//------------------------------------------------------------------------------
size_t AlignUp(size_t n)
{
    return (n + sizeof(void*) - 1) & ~(sizeof(void*) - 1);
}

//------------------------------------------------------------------------------
// RegisterBlock -- append a live block to the block table; fails loud on
// capacity overflow (a table bound exceeded means more live objects than the
// runtime's compile-time bound -- a build-time invariant violation).
//------------------------------------------------------------------------------
void RegisterBlock(Il2CppObject* obj, size_t size, int32_t generation)
{
    if (g_blockCount >= kMaxBlocks)
    {
        FailLoud("block table overflow (too many live objects)");
    }
    g_blockTable[g_blockCount].obj = obj;
    g_blockTable[g_blockCount].size = size;
    g_blockTable[g_blockCount].generation = generation;
    // Keep the table address-sorted without re-sorting on every append: the
    // common bump allocation is monotonic (new addresses grow), so appending
    // above the current tail preserves order. Only a non-monotonic append
    // (free-list reuse of an old address) marks the table unsorted; the next
    // lookup re-sorts once.
    if (g_blockCount > 0 && obj < g_blockTable[g_blockCount - 1].obj)
    {
        g_blockTableSorted = false;
    }
    ++g_blockCount;
}

//------------------------------------------------------------------------------
// PopFreeBlock -- first-fit search over the free list. Oversized blocks are
// used whole (no splitting in M2). The caller receives the block AND its true
// physical extent ('got') so the block table always records the real boundary
// -- never a rounded-down request size, which would make sweep free a block
// whose edge overlaps its neighbour. Returns null when no block fits.
//------------------------------------------------------------------------------
Il2CppObject* PopFreeBlock(size_t size, size_t& got)
{
    // Walk buckets from the matching size class upward; the first
    // qualifying block in the first non-empty bucket is used whole.
    for (int32_t b = SizeBucket(size); b < kFreeBuckets; ++b)
    {
        FreeBlock** link = &g_freeBuckets[b];
        while (*link != nullptr)
        {
            FreeBlock* blk = *link;
            if (blk->size >= size)
            {
                *link = blk->next;
                g_freeBytes -= blk->size;
                got = blk->size;
                return reinterpret_cast<Il2CppObject*>(blk);
            }
            link = &blk->next;
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// WorklistPush / WorklistPop -- fixed-capacity mark stack.
//------------------------------------------------------------------------------
void WorklistPush(Il2CppObject* obj)
{
    if (g_worklistCount >= kMaxWorklist)
    {
        FailLoud("mark worklist overflow");
    }
    g_worklist[g_worklistCount] = obj;
    ++g_worklistCount;
}

Il2CppObject* WorklistPop()
{
    if (g_worklistCount <= 0)
    {
        return nullptr;
    }
    --g_worklistCount;
    return g_worklist[g_worklistCount];
}

//------------------------------------------------------------------------------
// MarkSlot -- trace one reference slot; a non-null, not-yet-marked target is
// pushed onto the worklist.
//------------------------------------------------------------------------------
void MarkSlot(Il2CppObject** slot)
{
    if (slot == nullptr)
    {
        return;
    }
    Il2CppObject* target = *slot;
    if (target == nullptr)
    {
        return;
    }
    if (g_mark.Get(target))
    {
        return;
    }
    g_mark.Set(target);
    WorklistPush(target);
}

//------------------------------------------------------------------------------
// ScanObject -- push every referenced field of 'obj' onto the worklist.
//
// The reference-field bitmap is indexed by pointer slot: slot i is the pointer
// at byte offset i*sizeof(void*) inside the object. Slot 0 is the klass header
// (a pointer to a static descriptor, never a heap reference) and is therefore
// skipped; scanning starts at slot 1 and stops at the object's physical slot
// limit (instanceSize / sizeof(void*)) so a bitmap bit beyond the object can
// never read into the neighbouring block.
//------------------------------------------------------------------------------
void ScanObject(Il2CppObject* obj)
{
    Il2CppClass* klass = obj->klass;
    if (klass == nullptr)
    {
        return;
    }
    // Array scan (M3 Task 5): an array class is identified by the leading '['
    // in its name (.NET convention). Reference-element arrays carry the
    // isRefElem flag on the instance; their payload is a run of GC references
    // scanned element-wise. Value-element arrays (int32[]) have no references.
    if (klass->name != nullptr && klass->name[0] == '[')
    {
        Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(obj);
        if (arr->isRefElem)
        {
            Il2CppObject** elems = reinterpret_cast<Il2CppObject**>(arr->data);
            for (int32_t i = 0; i < arr->length; ++i)
            {
                MarkSlot(&elems[i]);
            }
        }
        return;
    }
    const uint32_t bitmap = klass->refFieldBitmap;
    const int32_t slotLimit = static_cast<int32_t>(
        klass->instanceSize / static_cast<int32_t>(sizeof(void*)));
    // Start at slot 1: slot 0 is the klass header, not a managed reference.
    for (int32_t slot = 1; slot < 32 && slot < slotLimit; ++slot)
    {
        if ((bitmap & (1u << slot)) == 0)
        {
            continue;
        }
        Il2CppObject** ref = reinterpret_cast<Il2CppObject**>(obj) + slot;
        MarkSlot(ref);
    }
}

//------------------------------------------------------------------------------
// ForwardLookup -- old address -> new address after Gen0 compaction.
// Returns nullptr when 'addr' was not moved (not in the forward map).
//------------------------------------------------------------------------------
Il2CppObject* ForwardLookup(Il2CppObject* addr)
{
    if (addr == nullptr || g_forwardCount == 0)
    {
        return nullptr;
    }
    if (!g_forwardMapSorted)
    {
        std::sort(g_forwardMap, g_forwardMap + g_forwardCount,
                  [](const ForwardEntry& a, const ForwardEntry& b) { return a.oldAddr < b.oldAddr; });
        g_forwardMapSorted = true;
    }
    // Binary search for the exact old address (map is address-sorted).
    int32_t lo = 0;
    int32_t hi = g_forwardCount - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (g_forwardMap[mid].oldAddr < addr)
        {
            lo = mid + 1;
        }
        else if (g_forwardMap[mid].oldAddr > addr)
        {
            hi = mid - 1;
        }
        else
        {
            return g_forwardMap[mid].newAddr;
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// UpdateReferencesAfterCompaction -- Phase C of the compacting collection.
//
//   Every root slot and every reference field of every surviving object is
//   rewritten through the forward map: an old (pre-copy) Gen0 address is
//   replaced with its new address. This is the ONLY place that guarantees no
//   dangling pointer survives a compaction -- it must be exhaustive:
//     (1) static roots, (2) root ranges (frames), (3) every surviving
//     object's reference fields (all generations -- a Gen2 object may hold a
//     reference to a moved Gen0 object).
//------------------------------------------------------------------------------
void UpdateReferencesAfterCompaction()
{
    if (g_forwardCount == 0)
    {
        return;
    }
    // (1) Static roots.
    for (int32_t i = 0; i < g_staticRootCount; ++i)
    {
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(g_staticRoots[i]);
        Il2CppObject* oldRef = *slot;
        Il2CppObject* newRef = ForwardLookup(oldRef);
        if (newRef != nullptr)
        {
            *slot = newRef;
        }
    }
    // (2) Root ranges (managed frames).
    for (int32_t r = 0; r < g_rootRangeCount; ++r)
    {
        const RootRange& range = g_rootRanges[r];
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(range.begin);
        for (int32_t i = 0; i < range.count; ++i)
        {
            Il2CppObject* newRef = ForwardLookup(slots[i]);
            if (newRef != nullptr)
            {
                slots[i] = newRef;
            }
        }
    }
    // (2b) The managed frame stack (M3 Task 9): every frame's refs slots.
    // Each refs[i] is the ADDRESS of a reference slot; rewrite *refs[i].
    for (Il2CppFrame* frame = il2cpp_frame_top(); frame != nullptr; frame = frame->prev)
    {
        if (frame->refs == nullptr)
        {
            continue;
        }
        for (int32_t i = 0; i < frame->refCount; ++i)
        {
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(frame->refs[i]);
            Il2CppObject* newRef = ForwardLookup(*slot);
            if (newRef != nullptr)
            {
                *slot = newRef;
            }
        }
    }
    // (3) Every surviving object's reference fields (bitmap-driven scan).
    for (int32_t b = 0; b < g_blockCount; ++b)
    {
        Il2CppObject* obj = g_blockTable[b].obj;
        Il2CppClass* klass = obj->klass;
        if (klass == nullptr)
        {
            continue;
        }
        // Arrays: update reference elements (M3 Task 5).
        if (klass->name != nullptr && klass->name[0] == '[')
        {
            Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(obj);
            if (arr->isRefElem)
            {
                Il2CppObject** elems = reinterpret_cast<Il2CppObject**>(arr->data);
                for (int32_t i = 0; i < arr->length; ++i)
                {
                    Il2CppObject* newRef = ForwardLookup(elems[i]);
                    if (newRef != nullptr)
                    {
                        elems[i] = newRef;
                    }
                }
            }
            continue;
        }
        const uint32_t bitmap = klass->refFieldBitmap;
        const int32_t slotLimit = static_cast<int32_t>(
            klass->instanceSize / static_cast<int32_t>(sizeof(void*)));
        for (int32_t slot = 1; slot < 32 && slot < slotLimit; ++slot)
        {
            if ((bitmap & (1u << slot)) == 0)
            {
                continue;
            }
            Il2CppObject** ref = reinterpret_cast<Il2CppObject**>(obj) + slot;
            Il2CppObject* newRef = ForwardLookup(*ref);
            if (newRef != nullptr)
            {
                *ref = newRef;
            }
        }
    }
    g_forwardCount = 0; // map consumed; cleared for the next cycle
}

//------------------------------------------------------------------------------
// Weak references (M-phase extension; see il2cpp_gc.h).
//
// A weak reference is a CLIENT-OWNED pointer slot (void** -- the address of
// a memory location holding an Il2CppObject*). The collector never marks
// through a weak slot (weakness is the whole point), but at every collection
// it re-examines each registered slot: a dead target clears the slot, a live
// target moved by the Gen0 compaction rewrites the slot (via the forward
// map). The pass runs AFTER the sweep (Phase B: the block table now holds
// only survivors, g_mark still distinguishes live/dead) and BEFORE Phase C
// (which consumes and clears the forward map).
//------------------------------------------------------------------------------
constexpr int32_t kMaxWeakRefs = 256; // fixed static table (constraint #12)

void**  g_weakRefs[kMaxWeakRefs];
int32_t g_weakRefCount = 0;

//------------------------------------------------------------------------------
// Pinned references (GCHandle.Alloc(obj, Pinned) support; see il2cpp_gc.h).
//
// A pinned slot is a strong root (marking is unaffected) whose target is
// IMMOVABLE: the Gen0 compaction promotes a pinned survivor NON-MOVING, so
// the address handed out by AddrOfPinnedObject stays valid across
// collections. Registered/removed by gchandle.cpp (1:1 pairing with the
// handle's lifetime).
//------------------------------------------------------------------------------
constexpr int32_t kMaxPinnedRefs = 64; // fixed static table (constraint #12)

void**  g_pinnedRefs[kMaxPinnedRefs];
int32_t g_pinnedRefCount = 0;

bool IsPinned(Il2CppObject* obj)
{
    if (obj == nullptr || g_pinnedRefCount == 0)
    {
        return false;
    }
    for (int32_t i = 0; i < g_pinnedRefCount; ++i)
    {
        if (g_pinnedRefs[i] != nullptr &&
            *reinterpret_cast<Il2CppObject**>(g_pinnedRefs[i]) == obj)
        {
            return true;
        }
    }
    return false;
}

//------------------------------------------------------------------------------
// Finalization (simplified coreclr CFinalize; see il2cpp_gc.h).
//
// A registered finalizer is an (object, callback) pair. The callback runs
// exactly once per registration, BEFORE the object's memory is reclaimed,
// with the .NET two-collection semantic:
//
//   collect N:   object dies -> entry Registered -> Pending; the block is
//                RETAINED (stays in the block table, memory not freed).
//   collect N+1: pending finalizers run at the START of the collection
//                (Pending -> Running -> fn(obj) -> Done); the sweep of the
//                SAME collection then sees the dead block with state Done
//                and finally reclaims it.
//
// A finalizer that makes its object reachable again (resurrection, e.g. by
// storing it into a root) is honored: the object is marked by the same
// collection, survives, and the entry is dropped -- the finalizer does not
// run a second time. A finalizer that ALLOCATES may trigger a nested
// collection; the reentrancy guard in RunPendingFinalizers skips re-entry
// and the Running state protects the in-flight object from the nested sweep.
//------------------------------------------------------------------------------
constexpr int32_t kMaxFinalizers = 256; // fixed static table (constraint #12)

enum class FinalizerState : uint8_t
{
    Registered, // finalizer armed; the object may be alive or dead
    Pending,    // object was dead at the last collection; fn not yet run
    Running,    // fn is currently executing (nested-collection protection)
    Done,       // fn has run; the next dead sighting reclaims the block
};

struct FinalizerEntry
{
    Il2CppObject* obj;
    void (*fn)(Il2CppObject*);
    FinalizerState state;
};

FinalizerEntry g_finalizers[kMaxFinalizers];
int32_t        g_finalizerCount = 0;
bool           g_finalizerRunning = false; // reentrancy guard

//------------------------------------------------------------------------------
// Type-level finalizer registry: maps a type to its Finalize method pointer
// (the analogue of IL2CPP's klass->finalize dispatch, and the runtime half of
// the transpiler's user-finalizer emission). Registering a type arms EVERY
// subsequent instance of it with the type's finalize method at allocation, so
// the collector runs that method (with the standard two-collection semantics
// above) whenever an instance becomes unreferenced. Fixed static table
// (constraint #12); overflow aborts.
//------------------------------------------------------------------------------
constexpr int32_t kMaxFinalizerTypes = 64; // fixed static table (constraint #12)

struct FinalizerTypeEntry
{
    Il2CppClass* klass;        // the type whose instances are finalizable
    void (*fn)(Il2CppObject*); // the type's Finalize method pointer
};

FinalizerTypeEntry g_finalizerTypes[kMaxFinalizerTypes];
int32_t           g_finalizerTypeCount = 0;

// TypeFinalizerFind -- linear lookup of a type's finalizer entry by klass.
FinalizerTypeEntry* TypeFinalizerFind(Il2CppClass* klass)
{
    for (int32_t i = 0; i < g_finalizerTypeCount; ++i)
    {
        if (g_finalizerTypes[i].klass == klass)
        {
            return &g_finalizerTypes[i];
        }
    }
    return nullptr;
}

// Cumulative managed-heap allocation counter (true physical bytes handed to
// objects since process start; incremented in AllocCore -- this backs
// il2cpp_gc_get_allocated_bytes).
int64_t g_allocatedBytes = 0;

//------------------------------------------------------------------------------
// Collection statistics + GCSettings state (fixed static storage, constraint
// #12 -- the GC statistics domain; see il2cpp_gc.h). Every value is written
// ONLY from RecordCollectionEnd (single authoritative writer, D1) or from the
// explicit setter icalls below, and read by the public accessors.
//------------------------------------------------------------------------------

// Per-generation collection counts (0/1/2 -- the M3 collector runs full-heap
// cycles, so one cycle increments all three). g_totalCollectionCount is the
// sequence number exposed as GCMemoryInfo.Index (1-based).
int64_t g_collectionCounts[3] = { 0, 0, 0 };
int64_t g_totalCollectionCount = 0;

// Cumulative GC pause time in 100ns TimeSpan ticks (monotonic).
int64_t g_totalPauseTicks = 0;

// g_lastGcPercentTimeInGc is the derived percentage of wall-clock time spent
// in the collector since the previous collection. It is reset at every
// collection end (RecordCollectionEnd) so GetLastGCPercentTimeInGC always
// describes the interval since the LAST collection.
int32_t g_lastGcPercentTimeInGc = 0;

// Bytes promoted by the last collection (surviving Gen0 blocks, computed
// from the mark phase before the sweep -- a read-only snapshot).
int64_t g_lastPromotedBytes = 0;

// GCSettings policy hints (stored; the M3 collector always runs full
// blocking cycles and never moves the LOH, so these are recorded but only
// the latency/compaction modes are validated on set).
int32_t g_latencyMode = kGcLatencyModeInteractive;
int32_t g_lohCompactionMode = kGcLohCompactionModeDefault;

// Full-GC notification registration state (blocking collector: always
// succeeds). The thresholds are stored so a future notification mechanism
// can honour them.
[[maybe_unused]] bool    g_fullGcNotificationRegistered = false;
[[maybe_unused]] int32_t g_fullGcMaxGenThreshold = 0;
[[maybe_unused]] int32_t g_fullGcLohThreshold = 0;

// The collector's fixed memory budget for the cross-platform memory-load
// statistic (1 GB of addressable managed heap; no OS-specific calls).
constexpr int64_t kMemoryLoadBudgetBytes = 1024LL * 1024 * 1024;

//------------------------------------------------------------------------------
// FinalizerFind -- linear lookup of a finalizer entry by object address.
//------------------------------------------------------------------------------
FinalizerEntry* FinalizerFind(Il2CppObject* obj)
{
    for (int32_t i = 0; i < g_finalizerCount; ++i)
    {
        if (g_finalizers[i].obj == obj)
        {
            return &g_finalizers[i];
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// FinalizerRemove -- remove an entry (swap the last entry into the hole).
//------------------------------------------------------------------------------
void FinalizerRemove(FinalizerEntry* fe)
{
    const int32_t idx = static_cast<int32_t>(fe - g_finalizers);
    g_finalizers[idx] = g_finalizers[g_finalizerCount - 1];
    --g_finalizerCount;
}

//------------------------------------------------------------------------------
// RunPendingFinalizers -- invoke fn for every finalizer that is due (state
// Pending). Called at the START of every collection (BEFORE marking, so a
// resurrecting finalizer's effect is visible to the mark phase) and directly
// by il2cpp_gc_run_finalizers. Reentrancy: a finalizer that allocates may
// trigger a nested collection; the guard skips this step in the nested run
// and the Running state keeps the in-flight object from being reclaimed by
// the nested sweep.
//------------------------------------------------------------------------------
void RunPendingFinalizers()
{
    if (g_finalizerRunning)
    {
        return;
    }
    g_finalizerRunning = true;
    for (int32_t i = 0; i < g_finalizerCount; ++i)
    {
        FinalizerEntry& fe = g_finalizers[i];
        if (fe.state != FinalizerState::Pending)
        {
            continue;
        }
        fe.state = FinalizerState::Running; // set BEFORE fn: a nested sweep never frees Running
        void (*fn)(Il2CppObject*) = fe.fn;
        Il2CppObject* obj = fe.obj;
        if (fn != nullptr)
        {
            fn(obj);
        }
        fe.state = FinalizerState::Done;
    }
    g_finalizerRunning = false;
}

//------------------------------------------------------------------------------
// UpdateExternalReferences -- post-sweep, pre-Phase-C bookkeeping for the
// external reference tables (weak slots + finalizer entries).
//
// Runs after the sweep (Phase B: g_mark still distinguishes live/dead, the
// block table holds only survivors) and before Phase C (the forward map is
// still valid; Phase C clears it). Two jobs:
//   (1) Weak slots: dead targets are cleared to null; live targets moved by
//       the Gen0 compaction are rewritten to their new address.
//   (2) Finalizer entries: object addresses are kept current through the
//       forward map (g_mark is keyed by PRE-copy addresses, so liveness is
//       checked BEFORE the address is updated); an entry whose object is
//       alive again after the finalizer already ran (resurrection) is
//       dropped; an entry whose object came back alive before the finalizer
//       ran is re-armed.
//------------------------------------------------------------------------------
void UpdateExternalReferences()
{
    // (1) Weak reference slots.
    for (int32_t i = 0; i < g_weakRefCount; ++i)
    {
        void** slot = g_weakRefs[i];
        if (slot == nullptr)
        {
            continue;
        }
        Il2CppObject* target = *reinterpret_cast<Il2CppObject**>(slot);
        if (target == nullptr)
        {
            continue;
        }
        if (!g_mark.Get(target))
        {
            *reinterpret_cast<Il2CppObject**>(slot) = nullptr; // dead: clear
            continue;
        }
        Il2CppObject* moved = ForwardLookup(target);
        if (moved != nullptr)
        {
            *reinterpret_cast<Il2CppObject**>(slot) = moved;
        }
    }
    // (2) Finalizer entries.
    for (int32_t i = 0; i < g_finalizerCount; ++i)
    {
        FinalizerEntry& fe = g_finalizers[i];
        const bool alive = g_mark.Get(fe.obj); // mark table keys are pre-copy addresses
        Il2CppObject* moved = ForwardLookup(fe.obj);
        if (moved != nullptr)
        {
            fe.obj = moved;
        }
        if (alive)
        {
            if (fe.state == FinalizerState::Done)
            {
                FinalizerRemove(&fe);
                --i; // re-examine the entry swapped into this hole
            }
            else if (fe.state == FinalizerState::Pending)
            {
                fe.state = FinalizerState::Registered; // alive again, fn never ran
            }
        }
    }
}

//------------------------------------------------------------------------------
// RecordCollectionEnd -- statistics bookkeeping at the end of a collection.
// Single authoritative writer for every collection counter (D1): the pause
// duration, the per-generation collection counts, the interval reset for the
// "percent time in GC" statistic. The collection body itself is untouched --
// this runs AFTER Phase C, when the collection has fully converged.
//------------------------------------------------------------------------------
void RecordCollectionEnd(const std::chrono::steady_clock::time_point& gcStart)
{
    const auto gcEnd = std::chrono::steady_clock::now();
    const int64_t pauseNs = std::chrono::duration_cast<std::chrono::nanoseconds>(gcEnd - gcStart).count();
    const int64_t wallNs = std::chrono::duration_cast<std::chrono::nanoseconds>(gcEnd - g_intervalStart).count();

    // Cumulative pause time in 100ns TimeSpan ticks (TimeSpan tick unit).
    g_totalPauseTicks += pauseNs / 100;

    // Interval reset: the next "percent time in GC" describes the interval
    // that just ended (this collection's pause over the wall time since the
    // previous collection).
    g_intervalStart = gcEnd;
    if (wallNs > 0)
    {
        g_lastGcPercentTimeInGc = static_cast<int32_t>((pauseNs * 100) / wallNs);
        if (g_lastGcPercentTimeInGc > 100)
        {
            g_lastGcPercentTimeInGc = 100;
        }
    }

    // Full-heap collector: one cycle counts once for every small-object
    // generation (0/1/2); the sequence number backs GCMemoryInfo.Index.
    ++g_collectionCounts[0];
    ++g_collectionCounts[1];
    ++g_collectionCounts[2];
    ++g_totalCollectionCount;
}

//------------------------------------------------------------------------------
// Collect -- full mark-sweep cycle.
//------------------------------------------------------------------------------
void Collect()
{
    EnsureInitialized();

    g_bumpedSinceCollect = 0; // allocation-rate accounting resets each cycle

    const auto gcStart = std::chrono::steady_clock::now();

    // Finalizers for objects that died at an earlier collection run BEFORE
    // this collection's mark: a finalizer that resurrects its object (by
    // storing it into a root) must do so before marking starts, so the
    // resurrected object is marked and survives. Nested collections inside
    // a finalizer (a finalizer that allocates) are handled by the
    // reentrancy guard in RunPendingFinalizers.
    RunPendingFinalizers();

    // Cards are "dirty since last collection" markers: reset at the start of
    // each collection (write barriers during the next interval re-mark).
    ClearCardTable();

    // --- Mark ---
    g_mark.Reset();
    g_worklistCount = 0;

    // Roots: static reference fields.
    for (int32_t i = 0; i < g_staticRootCount; ++i)
    {
        MarkSlot(reinterpret_cast<Il2CppObject**>(g_staticRoots[i]));
    }
    // Roots: contiguous reference-slot ranges (managed frames, etc.).
    // Take the root-range lock here and keep it until Collect returns: the
    // table must be a stable snapshot from this mark scan through the Phase C
    // reference rewrite below (UpdateReferencesAfterCompaction walks the same
    // table). A registration that arrives meanwhile waits for the cycle to
    // finish, so a live range can never be swapped out from under the collector
    // and a range cannot appear between the scan and the sweep unmarked.
    std::unique_lock<std::mutex> rootRangeLock(g_rootRangeMutex);
    for (int32_t r = 0; r < g_rootRangeCount; ++r)
    {
        const RootRange& range = g_rootRanges[r];
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(range.begin);
        for (int32_t i = 0; i < range.count; ++i)
        {
            MarkSlot(&slots[i]);
        }
    }
    // Roots: the managed frame stack (M3 Task 9) -- every frame's refs array.
    // Frames are the GC safe points: a method's reference locals are roots
    // while the method is on the call stack. Each refs[i] is the ADDRESS of a
    // reference slot (e.g. &loc0); MarkSlot reads the value at that address.
    for (Il2CppFrame* frame = il2cpp_frame_top(); frame != nullptr; frame = frame->prev)
    {
        if (frame->refs == nullptr)
        {
            continue;
        }
        for (int32_t i = 0; i < frame->refCount; ++i)
        {
            MarkSlot(reinterpret_cast<Il2CppObject**>(frame->refs[i]));
        }
    }
    // Iterative worklist trace.
    for (;;)
    {
        Il2CppObject* obj = WorklistPop();
        if (obj == nullptr)
        {
            break;
        }
        ScanObject(obj);
    }

    // Promoted-bytes snapshot (read-only, from the completed mark phase):
    // every SURVIVING Gen0 block is promoted by this collection (copied to
    // the Gen1 promote segment or promoted non-moving if oversized). This is
    // the bytes figure GCMemoryInfo.PromotedBytes reports for this GC.
    g_lastPromotedBytes = 0;
    for (int32_t i = 0; i < g_blockCount; ++i)
    {
        if (g_blockTable[i].generation == kGcGeneration0 && g_mark.Get(g_blockTable[i].obj))
        {
            g_lastPromotedBytes += static_cast<int64_t>(g_blockTable[i].size);
        }
    }

    // --- Sweep with Gen0 compaction (M3 Task 4) ---
    // Phase A (copy): every SURVIVING Gen0 block is copied to a fresh Gen1
    // segment (sequential copy = compaction within the young generation).
    // The old address is recorded in the forward map (old -> new). Gen1/2/LOH
    // survivors are NOT moved (mark-sweep semantics; .NET's Gen2/LOH are
    // non-compacting by default too).
    // Phase B (reclaim): unmarked blocks (any generation) go to the free
    // list; the block table is compacted in place.
    // Phase C (reference update): every root and every surviving object's
    // reference fields are rewritten from old addresses to new addresses via
    // the forward map. A MISSED update is a dangling pointer -- Phase C must
    // cover roots AND the full object graph.
    // NOTE: the free list is NOT reset here. Blocks freed by an EARLIER
    // sweep but not yet reused are still valid free memory: they must survive
    // this collection and stay available to later allocations. Resetting the
    // list at the start of every collection discards those untouched blocks
    // forever -- the heap then grows unboundedly as every later allocation
    // has to bump a fresh segment (a real memory leak; the gc stress suite
    // detects it deterministically). Sweep below PREPENDS this cycle's newly
    // freed blocks to the surviving list.
    g_forwardCount = 0;
    g_forwardMapSorted = false; // map rebuilt for this cycle

    // Ensure a promote segment exists (Gen1 target for copied survivors).
    if (g_promoteSegment == nullptr || g_promoteSegment->used > 0)
    {
        g_promoteSegment = AllocSegment(kGcGeneration1);
    }

    int32_t write = 0;
    for (int32_t read = 0; read < g_blockCount; ++read)
    {
        Il2CppObject* obj = g_blockTable[read].obj;
        const size_t size = g_blockTable[read].size;
        BlockEntry& entry = g_blockTable[read];
        if (!g_mark.Get(obj))
        {
            // Finalization hook: a dead block with a registered finalizer is
            // NOT reclaimed here. It enters the "pending finalization" state
            // and stays in the block table (its memory stays allocated) until
            // its finalizer has run (RunPendingFinalizers, at the START of
            // the NEXT collection) and this branch sees it dead again with
            // state Done -- only then is it actually reclaimed. This gives
            // the .NET two-collection finalization semantic; the callback
            // always observes memory that has not been freed yet.
            FinalizerEntry* fe = FinalizerFind(obj);
            if (fe != nullptr)
            {
                if (fe->state == FinalizerState::Done)
                {
                    FinalizerRemove(fe); // finalizer already ran: reclaim now
                }
                else
                {
                    fe->state = FinalizerState::Pending; // retained for finalization
                    if (write != read)
                    {
                        g_blockTable[write] = entry;
                    }
                    ++write;
                    continue;
                }
            }
            FreeBlock* blk = reinterpret_cast<FreeBlock*>(obj);
            const int32_t bucket = SizeBucket(size);
            blk->next = g_freeBuckets[bucket];
            blk->size = size;
            g_freeBuckets[bucket] = blk;
            g_freeBytes += size;
            continue; // reclaimed: not copied, not kept
        }
        // Surviving block: promote generation and possibly COPY.
        if (entry.generation == kGcGeneration0)
        {
            // A PINNED object is immovable (GCHandle.Alloc(obj, Pinned): the
            // address AddrOfPinnedObject returned must stay valid across
            // collections -- e.g. a COM/Win32 call marshaling a pinned buffer
            // while the dialog pumps messages and allocates). It is promoted
            // NON-MOVING like an oversized block: generation advances in
            // place, no forward-map entry, no copy.
            if (IsPinned(obj))
            {
                entry.generation = kGcGeneration1;
                if (write != read)
                {
                    g_blockTable[write] = entry;
                }
                ++write;
                continue;
            }
            // Gen0 survivor -> compacting copy to the Gen1 promote segment.
            // A block LARGER than a segment cannot be copied (it would not
            // fit); such blocks (e.g. a reclaimed LOH block reused by a small
            // object) are promoted NON-MOVING: generation advances in place.
            if (size > g_promoteSegment->capacity)
            {
                entry.generation = kGcGeneration1;
                if (write != read)
                {
                    g_blockTable[write] = entry;
                }
                ++write;
                continue;
            }
            if (g_promoteSegment->used + size > g_promoteSegment->capacity)
            {
                g_promoteSegment = AllocSegment(kGcGeneration1);
            }
            Il2CppObject* newObj = reinterpret_cast<Il2CppObject*>(
                g_promoteSegment->data + g_promoteSegment->used);
            g_promoteSegment->used += size;
            std::memcpy(newObj, obj, size); // copy header + payload verbatim
            if (g_forwardCount >= kMaxForwardEntries)
            {
                FailLoud("forward map overflow during Gen0 compaction");
            }
            g_forwardMap[g_forwardCount].oldAddr = obj;
            g_forwardMap[g_forwardCount].newAddr = newObj;
            ++g_forwardCount;
            entry.obj = newObj;
            entry.generation = kGcGeneration1;
        }
        else if (entry.generation == kGcGeneration1)
        {
            entry.generation = kGcGeneration2;
        }
        // LOH (3) and Gen2 (2) stay put.
        if (write != read)
        {
            g_blockTable[write] = entry;
        }
        ++write;
    }
    g_blockCount = write;
    g_blockTableSorted = false; // in-place compaction scrambles address order

    // External-reference bookkeeping (weak slots + finalizer entries): runs
    // AFTER the sweep (Phase B -- the block table now holds only survivors,
    // g_mark still distinguishes live/dead) and BEFORE Phase C (which
    // consumes and clears the forward map). See UpdateExternalReferences.
    UpdateExternalReferences();

    // Phase C: rewrite every reference (roots + surviving object graph).
    UpdateReferencesAfterCompaction();

    // Statistics bookkeeping (pause duration, collection counts, interval
    // reset). Runs after Phase C so the recorded state is fully converged.
    RecordCollectionEnd(gcStart);

    // 2026-09-23 (perf): after a collection the current Gen0 bump segment is
    // PHYSICALLY empty (survivors were copied to the promote segment, dead
    // blocks went to the free list), but its high-water mark ('used') was
    // never reset -- later allocations then depended on free-list scraps,
    // forcing a full collection every handful of allocations (observed: 1
    // collection per ~20 appends in sbappend-style loops). Switch to a fresh
    // Gen0 segment instead of reusing the old one: the fresh segment bumps
    // from offset 0 with zero overlap against the free list (the old, now
    // empty segment stays linked but is never bumped again). The old segment
    // could be recycled into an idle pool later; virtual memory cost is one
    // segment per collection until then.
    if (g_current != nullptr && g_current->generation == kGcGeneration0)
    {
        g_current = AllocSegment(kGcGeneration0);
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_write_barrier -- write barrier for reference-field stores (M3 Task 3).
//
//   slot : address OF the reference field being written (e.g. &obj->field)
//   value: the reference being stored (may be null)
//
//   Generated code calls this before every reference-field assignment:
//     il2cpp::il2cpp_write_barrier(&obj->field, value);
//     obj->field = value;
//
//   Semantics: if the STORING object (the object containing 'slot') lives in
//   an old generation (>= 1), mark the card covering it -- a later
//   young-only collection must scan that region for young references. The
//   storing object's generation is looked up in the block table (linear
//   scan; the table is small and barrier sites are not the hottest path in
//   M3 -- a per-object generation field is the M3.5 optimization).
//
//   Conservative simplification: marking is always safe (a spurious card
//   mark only costs a little extra scanning); missing a mark would be fatal
//   (a young object reachable only from an unscanned old region would be
//   collected). The lookup only SKIPS the mark when the storing object is
//   provably young (generation 0) -- young objects are scanned wholesale.
//------------------------------------------------------------------------------

// Forward declaration (defined below): generation of the block containing obj.
int32_t LookupGeneration(Il2CppObject* obj);

void il2cpp_write_barrier(void** slot, Il2CppObject* value)
{
    (void)value; // the barrier marks based on the STORING object, not the value
    if (slot == nullptr)
    {
        return;
    }
    // Segment-based generation check (2026-09-23 perf): the storing object's
    // generation is derived from the segment that PHYSICALLY contains the
    // slot address. Segments are few (heap extent count), so this is O(#seg)
    // with a small constant -- no block-table walk, no re-sort. Over-marking
    // is safe (an oversized logical-Gen0 block placed in a LOH-sized segment
    // marks its card; collection just scans a little more). Under-marking is
    // impossible: every block with logical generation >= 1 lives in a segment
    // with generation >= 1 (Gen1 promote segment / LOH / Gen2 stay-put), and
    // logical-Gen0 blocks only ever sit in Gen0 or LOH-sized segments.
    const uintptr_t slotAddr = reinterpret_cast<uintptr_t>(slot);
    for (Segment* seg = g_segments; seg != nullptr; seg = seg->next)
    {
        const uintptr_t begin = reinterpret_cast<uintptr_t>(seg->data);
        if (slotAddr >= begin && slotAddr < begin + seg->capacity)
        {
            if (seg->generation >= kGcGeneration1)
            {
                MarkCardForAddress(slot);
            }
            return;
        }
    }
    // Not a managed slot (defensive) -- nothing to track.
}

//------------------------------------------------------------------------------
// LookupGeneration -- generation of the block containing 'obj' (-1 unknown).
//------------------------------------------------------------------------------
int32_t LookupGeneration(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        return -1;
    }
    EnsureBlockTableSorted();
    // Binary search for the exact block address (table is address-sorted).
    int32_t lo = 0;
    int32_t hi = g_blockCount - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (g_blockTable[mid].obj < obj)
        {
            lo = mid + 1;
        }
        else if (g_blockTable[mid].obj > obj)
        {
            hi = mid - 1;
        }
        else
        {
            return g_blockTable[mid].generation;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// il2cpp_gc_is_card_marked -- test/diagnostic: is the card covering 'addr'
// dirty? Used by unit tests to verify write-barrier behavior.
//------------------------------------------------------------------------------
bool il2cpp_gc_is_card_marked(const void* addr)
{
    if (addr == nullptr)
    {
        return false;
    }
    const uintptr_t index = (reinterpret_cast<uintptr_t>(addr) >> kCardShift) & (kCardTableBytes - 1);
    return g_cardTable[index] != 0;
}

//------------------------------------------------------------------------------
// il2cpp_gc_init -- initialize the heap. Idempotent.
//------------------------------------------------------------------------------
void il2cpp_gc_init()
{
    EnsureInitialized();
}

//------------------------------------------------------------------------------
// AllocCore -- shared allocation core (used by il2cpp_gc_alloc and
// il2cpp_gc_alloc_size). 'size' is the TRUE physical extent (>= the aligned
// instanceSize for fixed-size types; the full header+payload extent for
// variable-size types like string/array). Path: free list first-fit ->
// generation segment bump -> (collect + retry) -> fresh segment. The klass
// header is stamped last, after the payload is zeroed. Single-threaded in M3.
//
// Generational routing (M3 Task 2):
//   * size < kGcLohThreshold (85000) -> Gen0 segment (g_current bump)
//   * size >= kGcLohThreshold        -> LOH segment (g_lohCurrent bump)
// A fresh LOH segment is created on demand; LOH objects are never promoted.
//
// PHYSICAL CONSTRAINT (bump path): a Gen0 segment holds only kSegmentPayload
// (64KB) of object area, so a Gen0-classified block LARGER than 64KB (e.g. a
// StringBuilder char buffer at 32768 units, a large string[] or a join result
// -- all below the 85000 LOH threshold) cannot be bumped into a fresh 64KB
// segment: the bump would run past the segment boundary and corrupt the heap.
// Such blocks are bumped from a LOH-sized segment while their LOGICAL
// generation stays 0; the sweep's oversized-block path promotes them
// NON-MOVING (test_gcstress pins this "oversized Gen0" contract).
//------------------------------------------------------------------------------
static Il2CppObject* AllocCore(Il2CppClass* klass, size_t size)
{
    if (klass == nullptr)
    {
        return nullptr;
    }
    EnsureInitialized();

    // Clamp to the minimum block size: every registered block must be able to
    // hold a FreeBlock header, or sweep's 16-byte free-list write would run
    // past the block edge into the neighbour block (klass corruption).
    if (size < kMinBlockSize)
    {
        size = kMinBlockSize;
    }

    const bool isLoh = size >= static_cast<size_t>(kGcLohThreshold);
    // LOH current segment is created lazily on the first large allocation.
    if (isLoh && g_lohCurrent == nullptr)
    {
        AllocSegment(kGcGenerationLoh);
    }
    Segment* target = isLoh ? g_lohCurrent : g_current;

    size_t got = 0;
    Il2CppObject* mem = nullptr;
    // Allocation strategy (2026-09-23 perf, iterated):
    //   1. A bumped-bytes threshold periodically runs a full collection
    //      (bounds heap growth; the bucketed free list keeps collections
    //      cheap). This is what makes implicit collection happen under
    //      realistic allocation pressure (frame-deep pins ~1MB pressure).
    //   2. Bump-first fast path: when the segment has room, bump. The old
    //      free-list-first order made every allocation walk the free list
    //      (O(#dead) before bucketing; still slow inside a huge bucket), so
    //      bump-first is the performance-critical order.
    //   3. Segment exhausted: reuse a free block (bucketed first-fit).
    //   4. Still nothing: collect + retry, then a fresh segment.
    // Address-reuse of reclaimed blocks is NOT guaranteed (bump-first may
    // take fresh space; CLR does not guarantee it either) -- see test_gc.
    if (g_bumpedSinceCollect > kCollectAfterBumpedBytes)
    {
        Collect();
        target = isLoh ? g_lohCurrent : g_current;
    }
    if (target->used + size <= target->capacity)
    {
        mem = reinterpret_cast<Il2CppObject*>(target->data + target->used);
        target->used += size;
        got = size;
        g_bumpedSinceCollect += size;
    }
    else
    {
        mem = PopFreeBlock(size, got);
        if (mem == nullptr)
        {
            // Neither a free block nor the current segment can satisfy the
            // request: collect and retry, then fall through to a fresh segment.
            Collect();
            mem = PopFreeBlock(size, got);
            target = isLoh ? g_lohCurrent : g_current; // collect may re-target
        }
        if (mem == nullptr)
        {
            if (target->used + size > target->capacity)
            {
                // Physical constraint: a segment holds only its payload capacity
                // (64KB for Gen0/1/2, 1MB for LOH). A block LARGER than the
                // standard capacity for its logical generation cannot be bumped
                // into a standard segment -- the bump would run past the segment
                // boundary (heap corruption). Such a block is bumped from a
                // segment sized by its PHYSICAL extent rather than its logical
                // generation:
                //   * a >64KB object below the .NET LOH threshold stays logically
                //     Gen0 but is placed in a LOH-sized (1MB) segment -- the
                //     sweep's oversized-block path promotes it NON-MOVING
                //     (test_gcstress pins: "84992-byte object starts in Gen0").
                //   * a >1MB LOH object gets a dedicated segment sized to the
                //     block, so the bump (and the zero-fill) never runs past the
                //     segment edge (audit A1 fix: a 2MB LOH block was previously
                //     bumped into a fresh 1MB segment, overflowing it ->
                //     0xC0000005 heap corruption).
                if (size > kSegmentPayload && !isLoh)
                {
                    target = AllocSegment(kGcGenerationLoh);
                }
                else if (size > kLohSegmentPayload && isLoh)
                {
                    target = AllocSegmentWithCapacity(kGcGenerationLoh, AlignUp(size));
                }
                else
                {
                    target = AllocSegment(isLoh ? kGcGenerationLoh : kGcGeneration0);
                    if (!isLoh)
                    {
                        g_current = target; // fresh Gen0 segment becomes the bump segment
                    }
                }
            }
            mem = reinterpret_cast<Il2CppObject*>(target->data + target->used);
            target->used += size;
            got = size;
        }
    }

    // Register the block before it is observable. The recorded extent is the
    // block's TRUE physical size (a reused free block may exceed the request).
    RegisterBlock(mem, got, isLoh ? kGcGenerationLoh : kGcGeneration0);
    // Cumulative allocation counter (backs il2cpp_gc_get_allocated_bytes).
    g_allocatedBytes += static_cast<int64_t>(got);

    // Zero the payload, then stamp the klass header.
    std::memset(mem, 0, got);
    mem->klass = klass;

    // Type-finalizer auto-arm: an object whose TYPE has a registered finalize
    // method (il2cpp_gc_register_type_finalizer) is armed with that method at
    // allocation, so the collector runs it (with two-collection semantics)
    // when the instance becomes unreferenced. This reuses the per-object
    // machinery unchanged (resurrection, compaction address fixup, reentrancy
    // all apply). The lookup is a linear scan over the small type registry.
    FinalizerTypeEntry* typeFe = TypeFinalizerFind(klass);
    if (typeFe != nullptr)
    {
        il2cpp_gc_register_finalizer(mem, typeFe->fn);
    }
    return mem;
}

//------------------------------------------------------------------------------
// il2cpp_gc_alloc -- fixed-size allocation (see il2cpp_gc.h).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_gc_alloc(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        return nullptr;
    }
    return AllocCore(klass, AlignUp(static_cast<size_t>(klass->instanceSize)));
}

//------------------------------------------------------------------------------
// il2cpp_gc_alloc_size -- size-extended allocation (string/array payloads).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_gc_alloc_size(Il2CppClass* klass, size_t totalSize)
{
    if (klass == nullptr)
    {
        return nullptr;
    }
    // The physical extent must at least cover the fixed descriptor size.
    const size_t minSize = AlignUp(static_cast<size_t>(klass->instanceSize));
    const size_t size = totalSize > minSize ? AlignUp(totalSize) : minSize;
    return AllocCore(klass, size);
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_generation -- query a block's current generation (0/1/2/3).
// Diagnostic/test helper: linear scan over the block table (the table is
// small and the query is rare). Returns -1 for unknown addresses.
//------------------------------------------------------------------------------
int32_t il2cpp_gc_get_generation(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        return -1;
    }
    for (int32_t i = 0; i < g_blockCount; ++i)
    {
        if (g_blockTable[i].obj == obj)
        {
            return g_blockTable[i].generation;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// il2cpp_gc_collect -- run a full mark-sweep cycle on demand.
//------------------------------------------------------------------------------
void il2cpp_gc_collect()
{
    Collect();
}

//------------------------------------------------------------------------------
// il2cpp_gc_add_static_root -- register a static reference field as a root.
// Idempotent per address; overflow aborts (generated code registers a
// compile-time bounded set).
//------------------------------------------------------------------------------
void il2cpp_gc_add_static_root(void** addr)
{
    if (addr == nullptr)
    {
        return;
    }
    for (int32_t i = 0; i < g_staticRootCount; ++i)
    {
        if (g_staticRoots[i] == addr)
        {
            return;
        }
    }
    if (g_staticRootCount >= kMaxStaticRoots)
    {
        FailLoud("static root table overflow");
    }
    g_staticRoots[g_staticRootCount] = addr;
    ++g_staticRootCount;
}

//------------------------------------------------------------------------------
// il2cpp_gc_remove_static_root -- unregister a static root by its address.
// Paired with add; the entry is compacted by swapping the last entry into
// the hole. Missing entries are no-ops (idempotent teardown).
//------------------------------------------------------------------------------
void il2cpp_gc_remove_static_root(void** addr)
{
    if (addr == nullptr)
    {
        return;
    }
    for (int32_t i = 0; i < g_staticRootCount; ++i)
    {
        if (g_staticRoots[i] == addr)
        {
            g_staticRoots[i] = g_staticRoots[g_staticRootCount - 1];
            --g_staticRootCount;
            return;
        }
    }
}

//------------------------------------------------------------------------------
// il2cpp_gc_add_root_range -- register a contiguous run of reference slots.
//------------------------------------------------------------------------------
void il2cpp_gc_add_root_range(void** begin, int32_t count)
{
    if (begin == nullptr || count <= 0)
    {
        return;
    }
    bool overflow = false;
    {
        std::lock_guard<std::mutex> lock(g_rootRangeMutex);
        if (g_rootRangeCount >= kMaxRootRanges)
        {
            overflow = true;
        }
        else
        {
            g_rootRanges[g_rootRangeCount].begin = begin;
            g_rootRanges[g_rootRangeCount].count = count;
            ++g_rootRangeCount;
        }
    }
    // Report OUTSIDE the lock: FailLoud writes to stderr, and no I/O may run
    // while the root-range mutex is held (D11). The overflow decision was made
    // atomically above, so this cannot race another registration.
    if (overflow)
    {
        FailLoud("root range table overflow");
    }
}

//------------------------------------------------------------------------------
// il2cpp_gc_remove_root_range -- unregister a root range by its begin address.
//
//   The managed frame protocol (Task 10) pairs each prologue add with an
//   epilogue remove: a method's reference locals are roots ONLY while the
//   method is on the call stack. Removal is linear over the (small) table;
//   the entry is compacted by swapping the last entry into the hole. A
//   missing entry is a no-op (idempotent teardown).
//------------------------------------------------------------------------------
void il2cpp_gc_remove_root_range(void** begin)
{
    if (begin == nullptr)
    {
        return;
    }
    std::lock_guard<std::mutex> lock(g_rootRangeMutex);
    for (int32_t i = 0; i < g_rootRangeCount; ++i)
    {
        if (g_rootRanges[i].begin == begin)
        {
            g_rootRanges[i] = g_rootRanges[g_rootRangeCount - 1];
            --g_rootRangeCount;
            return;
        }
    }
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_root_range_count / il2cpp_gc_get_root_range_capacity --
// test/diagnostic accessors for the root-range table (same role as
// il2cpp_gc_is_card_marked): the unit tests assert that add/remove pairing
// leaves the table balanced and that a realistic nesting fits the capacity.
//------------------------------------------------------------------------------
int32_t il2cpp_gc_get_root_range_count()
{
    std::lock_guard<std::mutex> lock(g_rootRangeMutex);
    return g_rootRangeCount;
}

int32_t il2cpp_gc_get_root_range_capacity()
{
    return kMaxRootRanges;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_total_memory -- sum of the sizes of all LIVE blocks in the
// block table (bytes of managed heap currently occupied by live objects,
// INCLUDING blocks retained for pending finalization -- their memory is
// still allocated). 0 on an empty heap. Snapshot semantics: changes with
// every allocation and collection.
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_total_memory()
{
    int64_t total = 0;
    for (int32_t i = 0; i < g_blockCount; ++i)
    {
        total += static_cast<int64_t>(g_blockTable[i].size);
    }
    return total;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_allocated_bytes -- cumulative managed-heap allocation
// counter (true physical bytes handed to objects since process start;
// incremented in AllocCore). Monotonic; unaffected by collections.
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_allocated_bytes()
{
    return g_allocatedBytes;
}

//------------------------------------------------------------------------------
// il2cpp_gc_add_weak_ref -- register a weak-reference slot (see the header
// contract in il2cpp_gc.h). Idempotent per address; overflow aborts.
//------------------------------------------------------------------------------
void il2cpp_gc_add_weak_ref(void** slot)
{
    if (slot == nullptr)
    {
        return;
    }
    for (int32_t i = 0; i < g_weakRefCount; ++i)
    {
        if (g_weakRefs[i] == slot)
        {
            return; // idempotent per address (like static roots)
        }
    }
    if (g_weakRefCount >= kMaxWeakRefs)
    {
        FailLoud("weak reference table overflow");
    }
    g_weakRefs[g_weakRefCount] = slot;
    ++g_weakRefCount;
}

//------------------------------------------------------------------------------
// il2cpp_gc_remove_weak_ref -- unregister a weak slot by its address.
// Paired with add; the entry is compacted by swapping the last entry into
// the hole. Missing entries are no-ops (idempotent teardown).
//------------------------------------------------------------------------------
void il2cpp_gc_remove_weak_ref(void** slot)
{
    if (slot == nullptr)
    {
        return;
    }
    for (int32_t i = 0; i < g_weakRefCount; ++i)
    {
        if (g_weakRefs[i] == slot)
        {
            g_weakRefs[i] = g_weakRefs[g_weakRefCount - 1];
            --g_weakRefCount;
            return;
        }
    }
}

//------------------------------------------------------------------------------
// il2cpp_gc_add_pinned -- register a pinned slot (GCHandle.Alloc(obj, Pinned)
// support). Idempotent per address; overflow aborts.
//------------------------------------------------------------------------------
void il2cpp_gc_add_pinned(void** slot)
{
    if (slot == nullptr)
    {
        return;
    }
    for (int32_t i = 0; i < g_pinnedRefCount; ++i)
    {
        if (g_pinnedRefs[i] == slot)
        {
            return; // idempotent per address (like static roots)
        }
    }
    if (g_pinnedRefCount >= kMaxPinnedRefs)
    {
        FailLoud("pinned reference table overflow");
    }
    g_pinnedRefs[g_pinnedRefCount] = slot;
    ++g_pinnedRefCount;
}

//------------------------------------------------------------------------------
// il2cpp_gc_remove_pinned -- unregister a pinned slot by its address.
// Paired with add; the entry is compacted by swapping the last entry into
// the hole. Missing entries are no-ops (idempotent teardown).
//------------------------------------------------------------------------------
void il2cpp_gc_remove_pinned(void** slot)
{
    if (slot == nullptr)
    {
        return;
    }
    for (int32_t i = 0; i < g_pinnedRefCount; ++i)
    {
        if (g_pinnedRefs[i] == slot)
        {
            g_pinnedRefs[i] = g_pinnedRefs[g_pinnedRefCount - 1];
            --g_pinnedRefCount;
            return;
        }
    }
}

//------------------------------------------------------------------------------
// il2cpp_gc_register_finalizer -- register a finalizer callback for 'obj'
// (see the header contract in il2cpp_gc.h). Re-registering an object
// REPLACES the callback and re-arms it (the analogue of
// GC.ReRegisterForFinalize). Null object/callback are ignored; overflow
// aborts.
//------------------------------------------------------------------------------
void il2cpp_gc_register_finalizer(Il2CppObject* obj, void (*fn)(Il2CppObject*))
{
    if (obj == nullptr || fn == nullptr)
    {
        return;
    }
    FinalizerEntry* fe = FinalizerFind(obj);
    if (fe != nullptr)
    {
        fe->fn = fn;
        fe->state = FinalizerState::Registered;
        return;
    }
    if (g_finalizerCount >= kMaxFinalizers)
    {
        FailLoud("finalizer table overflow");
    }
    g_finalizers[g_finalizerCount].obj = obj;
    g_finalizers[g_finalizerCount].fn = fn;
    g_finalizers[g_finalizerCount].state = FinalizerState::Registered;
    ++g_finalizerCount;
}

//------------------------------------------------------------------------------
// il2cpp_gc_register_type_finalizer -- register a TYPE-level finalizer: the
// type's Finalize method pointer, armed on EVERY instance of 'klass' at
// allocation (see AllocCore). This is the runtime half of the transpiler's
// user-finalizer emission: generated code registers each finalizable type
// once, and every subsequent newobj of that type is automatically
// finalizable. Re-registering a type REPLACES the callback. Null klass or
// null callback are ignored; overflow aborts.
//------------------------------------------------------------------------------
void il2cpp_gc_register_type_finalizer(Il2CppClass* klass, void (*fn)(Il2CppObject*))
{
    if (klass == nullptr || fn == nullptr)
    {
        return;
    }
    FinalizerTypeEntry* te = TypeFinalizerFind(klass);
    if (te != nullptr)
    {
        te->fn = fn; // replace
        return;
    }
    if (g_finalizerTypeCount >= kMaxFinalizerTypes)
    {
        FailLoud("finalizer type table overflow");
    }
    g_finalizerTypes[g_finalizerTypeCount].klass = klass;
    g_finalizerTypes[g_finalizerTypeCount].fn = fn;
    ++g_finalizerTypeCount;
}

//------------------------------------------------------------------------------
// il2cpp_gc_run_finalizers -- run all due (pending) finalizers now.
// Normally they run automatically at the start of the next collection; this
// is the direct trigger (tests, future finalizer-thread emulation).
//------------------------------------------------------------------------------
void il2cpp_gc_run_finalizers()
{
    RunPendingFinalizers();
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_collection_count -- per-generation collection counts.
// The M3 collector runs full-heap cycles, so one collection increments every
// small-object generation's count exactly once. LOH (3) and out-of-range
// generations report 0 (the LOH has no collection count of its own in this
// model; .NET's CollectionCount is defined for generations 0..MaxGeneration).
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_collection_count(int32_t generation)
{
    if (generation < 0 || generation > kGcGeneration2)
    {
        return 0;
    }
    return g_collectionCounts[generation];
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_total_pause_ticks -- total wall-clock time inside collections
// since process start, in 100ns TimeSpan ticks. Monotonic (accumulated in
// RecordCollectionEnd).
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_total_pause_ticks()
{
    return g_totalPauseTicks;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_last_gc_percent_time_in_gc -- percentage (0..100) of
// wall-clock time spent in the collector since the last collection. Written
// by RecordCollectionEnd at every collection end.
//------------------------------------------------------------------------------
int32_t il2cpp_gc_get_last_gc_percent_time_in_gc()
{
    return g_lastGcPercentTimeInGc;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_memory_load -- cross-platform memory load percentage. The
// collector makes no OS-specific calls (project rule #8), so the load is
// computed from the managed heap itself: committed segment bytes as a
// percentage of the fixed runtime budget (kMemoryLoadBudgetBytes), clamped
// to 0..100.
//------------------------------------------------------------------------------
int32_t il2cpp_gc_get_memory_load()
{
    const int64_t committed = il2cpp_gc_get_total_committed_bytes();
    const int64_t percent = (committed * 100) / kMemoryLoadBudgetBytes;
    if (percent > 100)
    {
        return 100;
    }
    return static_cast<int32_t>(percent);
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_total_committed_bytes -- sum of every segment's payload
// capacity (the collector's reserved managed-heap bytes). Monotonic in M2/M3
// (segments are never returned to the OS).
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_total_committed_bytes()
{
    EnsureInitialized();
    int64_t total = 0;
    for (Segment* s = g_segments; s != nullptr; s = s->next)
    {
        total += static_cast<int64_t>(s->capacity);
    }
    return total;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_segment_size -- the collector's segment payload size in bytes
// (the kSegmentPayload constant; the analogue of GC.GetSegmentSize()).
//------------------------------------------------------------------------------
uint64_t il2cpp_gc_get_segment_size()
{
    return static_cast<uint64_t>(kSegmentPayload);
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_fragmented_bytes -- reclaimable free-list bytes (swept blocks
// not yet reused). The collector's fragmentation proxy.
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_fragmented_bytes()
{
    return static_cast<int64_t>(g_freeBytes);
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_generation_size -- sum of the sizes of all LIVE blocks in one
// generation (0/1/2/3). Out-of-range generations report 0.
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_generation_size(int32_t generation)
{
    int64_t total = 0;
    for (int32_t i = 0; i < g_blockCount; ++i)
    {
        if (g_blockTable[i].generation == generation)
        {
            total += static_cast<int64_t>(g_blockTable[i].size);
        }
    }
    return total;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_max_generation -- the maximum small-object generation number.
// .NET's GC.MaxGeneration is 2 (Gen2 is the oldest small-object generation;
// the LOH is separate and not counted).
//------------------------------------------------------------------------------
int32_t il2cpp_gc_get_max_generation()
{
    return kGcGeneration2;
}

//------------------------------------------------------------------------------
// il2cpp_gc_register_for_full_gc_notification -- register full-GC
// notification thresholds. The M3 collector is a BLOCKING (non-concurrent)
// collector, so registration always succeeds and returns true (in .NET,
// registration returns false when concurrent GC is enabled -- this collector
// never is). The thresholds are stored for a future notification mechanism.
//------------------------------------------------------------------------------
bool il2cpp_gc_register_for_full_gc_notification(int32_t maxGenerationThreshold,
                                                 int32_t largeObjectHeapThreshold)
{
    g_fullGcNotificationRegistered = true;
    g_fullGcMaxGenThreshold = maxGenerationThreshold;
    g_fullGcLohThreshold = largeObjectHeapThreshold;
    return true;
}

//------------------------------------------------------------------------------
// il2cpp_gc_cancel_full_gc_notification -- cancel a prior registration.
// Always succeeds (mirrors the registration path).
//------------------------------------------------------------------------------
bool il2cpp_gc_cancel_full_gc_notification()
{
    g_fullGcNotificationRegistered = false;
    g_fullGcMaxGenThreshold = 0;
    g_fullGcLohThreshold = 0;
    return true;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_allocated_bytes_for_current_thread -- cumulative allocation
// counter for the current thread. The M3 collector is single-threaded, so the
// per-thread and process-wide counters coincide (same cumulative counter
// incremented in AllocCore).
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_allocated_bytes_for_current_thread()
{
    return g_allocatedBytes;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_total_allocated_bytes_approximate -- approximate total bytes
// ever allocated (the cumulative counter; the exact per-thread breakdown is
// not tracked). The analogue of GCInterface::GetTotalAllocatedBytesApproximate.
//------------------------------------------------------------------------------
int64_t il2cpp_gc_get_total_allocated_bytes_approximate()
{
    return g_allocatedBytes;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_is_server_gc -- GCSettings.IsServerGC. Always false: the M3
// collector is a workstation (non-concurrent, single-heap) collector.
//------------------------------------------------------------------------------
bool il2cpp_gc_get_is_server_gc()
{
    return false;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_latency_mode / il2cpp_gc_set_latency_mode -- GCSettings.
// LatencyMode. The mode is a stored policy hint (the collector always runs
// full blocking cycles), validated on set; setting always succeeds (there is
// no NoGCRegion to refuse a change), so the setter returns
// kGcSetLatencyStatusSucceeded.
//------------------------------------------------------------------------------
int32_t il2cpp_gc_get_latency_mode()
{
    return g_latencyMode;
}

int32_t il2cpp_gc_set_latency_mode(int32_t mode)
{
    if (mode < kGcLatencyModeBatch || mode > kGcLatencyModeSustainedLowLatency)
    {
        return kGcSetLatencyStatusSucceeded; // out-of-range: keep the current mode
    }
    g_latencyMode = mode;
    return kGcSetLatencyStatusSucceeded;
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_loh_compaction_mode / il2cpp_gc_set_loh_compaction_mode --
// GCSettings.LargeObjectHeapCompactionMode. Stored policy hint: the LOH is
// non-moving in M3, so the mode is recorded but not acted on until a
// compacting LOH path exists.
//------------------------------------------------------------------------------
int32_t il2cpp_gc_get_loh_compaction_mode()
{
    return g_lohCompactionMode;
}

void il2cpp_gc_set_loh_compaction_mode(int32_t mode)
{
    if (mode == kGcLohCompactionModeDefault || mode == kGcLohCompactionModeCompactOnce)
    {
        g_lohCompactionMode = mode;
    }
}

//------------------------------------------------------------------------------
// il2cpp_gc_get_memory_info -- fill the GcMemoryInfoData snapshot (the data
// surface for GC.GetGCMemoryInfo). All fields are derived from the collector's
// static bookkeeping; none require OS-specific calls.
//------------------------------------------------------------------------------
void il2cpp_gc_get_memory_info(GcMemoryInfoData& out)
{
    EnsureInitialized();
    out.highMemoryLoadThresholdBytes = kMemoryLoadBudgetBytes;
    out.totalAvailableMemoryBytes = kMemoryLoadBudgetBytes;
    out.heapSizeBytes = il2cpp_gc_get_total_memory();
    out.fragmentedBytes = il2cpp_gc_get_fragmented_bytes();
    out.totalCommittedBytes = il2cpp_gc_get_total_committed_bytes();
    out.promotedBytes = g_lastPromotedBytes;
    out.pinnedObjectsCount = 0; // no pinning in M3
    out.finalizationPendingCount = 0;
    for (int32_t i = 0; i < g_finalizerCount; ++i)
    {
        if (g_finalizers[i].state == FinalizerState::Pending)
        {
            ++out.finalizationPendingCount;
        }
    }
    out.index = g_totalCollectionCount;
    out.generation = kGcGeneration2; // the last (and every) collection is full
    out.pauseTimePercentage = g_lastGcPercentTimeInGc;
    const int64_t loadPercent = il2cpp_gc_get_memory_load();
    out.memoryLoadBytes = (loadPercent * kMemoryLoadBudgetBytes) / 100;
    out.compacted = true;   // Gen0 survivors are compacted every collection
    out.concurrent = false; // no background collection
}

} // namespace il2cpp






