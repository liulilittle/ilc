//------------------------------------------------------------------------------
// allocator.cpp -- size-classed pool for NON-GC memory.
//
// See il2cpp_allocator.h for the full contract. Summary of this file:
//
//   * Five buckets (16/32/64/128/256 payload bytes), each a singly linked
//     free list of fixed-size cells. Allocation pops the head (O(1)); free
//     pushes the head (O(1)).
//   * Every cell is stamped with its bucket index in an 8-byte header at the
//     cell base; the user pointer is base + 8. Free recovers the bucket from
//     the header -- no side tables, no linear search, no per-free lookup.
//   * Free-list linkage lives in the cell PAYLOAD (free cells carry no user
//     data), so cells need no extra link word beyond the header.
//   * Requests > 256 bytes go to malloc with a distinct large marker in the
//     header; free distinguishes the paths by the marker value.
//   * OS memory is fetched in chunks of kCellsPerChunk cells per bucket and
//     carved into cells on first use of the bucket; chunks are never
//     returned to the OS (pool semantics, mirroring the GC's non-moving
//     heap).
//   * All state is zero-initialized static storage -- nothing runs before
//     main, no C++ heap bookkeeping (project constraint #12 style).
//   * A reserved lock slot: defining IL2CPP_ALLOCATOR_THREAD_SAFE wraps
//     every state mutation in a mutex (single-threaded by contract today;
//     the future multi-threaded runtime flips one define).
//
// Alignment proof: chunk bases come from malloc (16-aligned), every cell
// size is a multiple of 8 (static_assert), so cell bases are 8-aligned and
// user pointers (base + 8) are 8-aligned. Large-path pointers are
// malloc-base + 8, also 8-aligned.
//------------------------------------------------------------------------------

#include "il2cpp_allocator.h"

#include <cstddef>
#include <cstdio>
#include <cstdlib>

namespace il2cpp {
namespace alloc {

namespace {

//------------------------------------------------------------------------------
// Constants.
//------------------------------------------------------------------------------

// Header word stamped in front of every block's payload. The header lives at
// the cell/malloc base; the payload starts kHeaderBytes later, so every
// returned pointer is 8-byte aligned whenever bases are 8-aligned.
constexpr size_t kHeaderBytes = sizeof(size_t);

// Marker stored in the header of large (>256-byte) blocks. All-ones can
// never collide with a bucket index (0..kNumBuckets-1), so free() reliably
// tells the malloc fallback from the pool.
constexpr size_t kLargeHeader = ~size_t{0};

// Cells carved per OS chunk (one malloc call per bucket per fill).
constexpr size_t kCellsPerChunk = 128;

//------------------------------------------------------------------------------
// Bucket -- one size class: a singly linked free list of cells.
//------------------------------------------------------------------------------
struct Bucket
{
    void*    head;   // free-list head; linkage lives in the cell payload
    uint64_t allocs; // allocation counter for this bucket (statistics)
};

//------------------------------------------------------------------------------
// Pool state -- zero-initialized static storage (nothing runs before main).
//------------------------------------------------------------------------------
Bucket   g_buckets[kNumBuckets];
uint64_t g_allocCount = 0; // total fast allocations (pool + large)
uint64_t g_freeCount = 0;  // total fast frees
uint64_t g_totalBytes = 0; // total bytes REQUESTED by fast allocations
uint64_t g_largeAllocs = 0; // allocations served by the malloc fallback
uint64_t g_largeBytes = 0;  // bytes requested on the large path
uint64_t g_chunkCarves = 0; // OS chunks carved so far

//------------------------------------------------------------------------------
// Reserved lock slot (iron rule 2j -- future JIT / multi-threaded runtime).
// The pool is single-threaded by contract in the current milestone; defining
// IL2CPP_ALLOCATOR_THREAD_SAFE compiles a mutex around every state mutation
// without changing any call site.
//------------------------------------------------------------------------------
#ifdef IL2CPP_ALLOCATOR_THREAD_SAFE
#include <mutex>
std::mutex g_poolMutex;
#define IL2CPP_ALLOC_POOL_LOCK() std::lock_guard<std::mutex> g_poolLock(g_poolMutex)
#else
#define IL2CPP_ALLOC_POOL_LOCK() ((void)0)
#endif

//------------------------------------------------------------------------------
// FailLoud -- report a pool invariant violation and abort (fail loud, the
// runtime's established style for fixed-capacity / precondition failures).
//------------------------------------------------------------------------------
void FailLoud(const char* what)
{
    std::fprintf(stderr, "il2cpp_allocator: %s\n", what);
    std::abort();
}

//------------------------------------------------------------------------------
// BucketFor -- index of the smallest bucket whose payload fits 'size'.
// Precondition: 1 <= size <= kBucketSizes[kNumBuckets-1].
//------------------------------------------------------------------------------
int BucketFor(size_t size)
{
    for (int b = 0; b < kNumBuckets; ++b)
    {
        if (size <= kBucketSizes[b])
        {
            return b;
        }
    }
    return kNumBuckets; // unreachable for in-range sizes (defensive)
}

//------------------------------------------------------------------------------
// CellSize -- total bytes a cell of bucket 'b' occupies (payload + header).
//------------------------------------------------------------------------------
constexpr size_t CellSize(int b)
{
    return kBucketSizes[b] + kHeaderBytes;
}

//------------------------------------------------------------------------------
// Alignment proof: every cell size is a multiple of the header, so carving a
// 16-aligned malloc chunk into cells keeps every cell base 8-aligned and the
// user pointer (base + header) 8-aligned on 64-bit platforms.
//------------------------------------------------------------------------------
static_assert(sizeof(size_t) == sizeof(void*), "size_t must be pointer-width for a stable header stamp");
static_assert(kHeaderBytes == sizeof(void*), "header word must be a full pointer slot");

constexpr bool AllCellSizesAligned()
{
    for (int b = 0; b < kNumBuckets; ++b)
    {
        if (CellSize(b) % kHeaderBytes != 0)
        {
            return false;
        }
    }
    return true;
}
static_assert(AllCellSizesAligned(), "every cell size must keep 8-byte alignment");

//------------------------------------------------------------------------------
// CarveChunk -- fetch one OS chunk for bucket 'b' and thread every cell onto
// its free list. Each cell is stamped with its bucket index in the header
// (written once, never touched again); the free-list linkage is stored in
// the cell payload, which is unowned while the cell is free.
//------------------------------------------------------------------------------
void CarveChunk(int b)
{
    const size_t cellSize = CellSize(b);
    char* chunk = static_cast<char*>(std::malloc(kCellsPerChunk * cellSize));
    if (chunk == nullptr)
    {
        FailLoud("out of memory carving bucket chunk");
    }
    for (size_t i = 0; i < kCellsPerChunk; ++i)
    {
        char* cell = chunk + i * cellSize;
        *reinterpret_cast<size_t*>(cell) = static_cast<size_t>(b); // size-class header
        void** link = reinterpret_cast<void**>(cell + kHeaderBytes); // payload linkage
        *link = g_buckets[b].head;
        g_buckets[b].head = cell;
    }
    ++g_chunkCarves;
}

//------------------------------------------------------------------------------
// SnapshotStats -- copy the pool counters into an AllocStats value.
//------------------------------------------------------------------------------
AllocStats SnapshotStats()
{
    AllocStats s;
    s.allocCount = g_allocCount;
    s.freeCount = g_freeCount;
    s.totalBytes = g_totalBytes;
    for (int b = 0; b < kNumBuckets; ++b)
    {
        s.bucketAllocs[b] = g_buckets[b].allocs;
    }
    s.largeAllocs = g_largeAllocs;
    s.largeBytes = g_largeBytes;
    s.chunkCarves = g_chunkCarves;
    return s;
}

//------------------------------------------------------------------------------
// PoolAlloc -- O(1) allocation from the size-class pool (or the malloc
// fallback for requests above the largest bucket). Never returns null:
// out-of-memory aborts (fail loud).
//------------------------------------------------------------------------------
void* PoolAlloc(size_t size)
{
    if (size == 0)
    {
        size = 1; // zero requests land in the smallest bucket (non-null)
    }
    if (size > kBucketSizes[kNumBuckets - 1])
    {
        // Large path: malloc fallback with the large marker in the header.
        size_t* base = static_cast<size_t*>(std::malloc(size + kHeaderBytes));
        if (base == nullptr)
        {
            FailLoud("out of memory on large allocation");
        }
        base[0] = kLargeHeader;
        IL2CPP_ALLOC_POOL_LOCK();
        ++g_allocCount;
        ++g_largeAllocs;
        g_totalBytes += size;
        g_largeBytes += size;
        return base + 1;
    }
    const int b = BucketFor(size);
    IL2CPP_ALLOC_POOL_LOCK();
    if (g_buckets[b].head == nullptr)
    {
        CarveChunk(b); // lazy chunk fetch: first use of the bucket
    }
    void* cell = g_buckets[b].head;
    void** link = reinterpret_cast<void**>(static_cast<char*>(cell) + kHeaderBytes);
    g_buckets[b].head = *link; // pop: next free cell is the stored linkage
    ++g_allocCount;
    ++g_buckets[b].allocs;
    g_totalBytes += size;
    return static_cast<char*>(cell) + kHeaderBytes;
}

//------------------------------------------------------------------------------
// PoolFree -- O(1) free: recover the owning bucket from the block header and
// push the cell back onto that bucket's free list. Null is a no-op. Large
// blocks (marker == kLargeHeader) return to malloc.
//------------------------------------------------------------------------------
void PoolFree(void* ptr)
{
    if (ptr == nullptr)
    {
        return; // null free is a no-op (pool contract)
    }
    char* payload = static_cast<char*>(ptr);
    size_t* header = reinterpret_cast<size_t*>(payload - kHeaderBytes);
    IL2CPP_ALLOC_POOL_LOCK();
    if (*header == kLargeHeader)
    {
        std::free(header); // the malloc base is the header word itself
        ++g_freeCount;
        return;
    }
    const int b = static_cast<int>(*header);
    if (b < 0 || b >= kNumBuckets)
    {
        FailLoud("free of a pointer that is not a pool block (bad size-class header)");
    }
    // Push: the free-list node is the CELL BASE (linkage lives in the
    // payload, which is unowned while the cell is free) -- the same
    // convention CarveChunk and PoolAlloc use. Pushing the user pointer
    // instead would make the next pop treat the linkage word as the
    // size-class header and return an interior pointer.
    void* cell = payload - kHeaderBytes;
    *reinterpret_cast<void**>(payload) = g_buckets[b].head;
    g_buckets[b].head = cell;
    ++g_freeCount;
}

//------------------------------------------------------------------------------
// Default table entries: the pool implementation itself.
//------------------------------------------------------------------------------
void* DefaultAlloc(size_t size)
{
    return PoolAlloc(size);
}

void DefaultFree(void* ptr)
{
    PoolFree(ptr);
}

AllocStats DefaultStats()
{
    IL2CPP_ALLOC_POOL_LOCK();
    return SnapshotStats();
}

// The current allocator table, zero-initialized then filled at static init:
// default = the pool. il2cpp_allocator_set replaces it (JIT hook).
AllocatorTable g_table = { DefaultAlloc, DefaultFree, DefaultStats };

} // namespace

//------------------------------------------------------------------------------
// il2cpp_allocator -- the live table reference (see il2cpp_allocator.h).
//------------------------------------------------------------------------------
const AllocatorTable& il2cpp_allocator()
{
    return g_table;
}

//------------------------------------------------------------------------------
// il2cpp_allocator_set -- swap the current table (JIT hook). All entries
// must be non-null (fail loud). The table is copied by value; the pointed-to
// functions must outlive the table.
//------------------------------------------------------------------------------
void il2cpp_allocator_set(const AllocatorTable& table)
{
    if (table.alloc == nullptr || table.free == nullptr || table.stats == nullptr)
    {
        FailLoud("allocator table with a null entry");
    }
    g_table = table;
}

//------------------------------------------------------------------------------
// il2cpp_alloc_bucket_hit_rate -- fraction of all fast allocations served by
// one bucket; 0 for out-of-range indices and before any allocation.
//------------------------------------------------------------------------------
double il2cpp_alloc_bucket_hit_rate(int bucketIndex)
{
    if (bucketIndex < 0 || bucketIndex >= kNumBuckets)
    {
        return 0.0;
    }
    // Snapshot under the same pool lock as DefaultStats: in the reserved
    // IL2CPP_ALLOCATOR_THREAD_SAFE build the counters are mutated under the
    // mutex, so a lock-free read would race with a concurrent alloc/free.
    IL2CPP_ALLOC_POOL_LOCK();
    const AllocStats s = SnapshotStats();
    if (s.allocCount == 0)
    {
        return 0.0;
    }
    return static_cast<double>(s.bucketAllocs[bucketIndex])
        / static_cast<double>(s.allocCount);
}

} // namespace alloc
} // namespace il2cpp
