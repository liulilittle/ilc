#pragma once
//------------------------------------------------------------------------------
// il2cpp_allocator.h -- high-performance allocation layer for NON-GC memory.
//
// PURPOSE
//   A bypass allocation layer serving temporary buffers and native scratch
//   memory only. Managed objects (which need a klass header and a reference
//   bitmap) MUST go through il2cpp_gc_alloc -- the GC remains the single
//   owner of managed heap memory and never sees the pool. Pool memory is
//   reclaimed exclusively by il2cpp_free_fast; it is invisible to the
//   collector and to its bookkeeping.
//
// DESIGN
//   Fixed size classes: 16/32/64/128/256 bytes (kBucketSizes). Each bucket
//   keeps a singly linked free list; allocation pops the head and free
//   pushes the head -- both O(1). Every block is stamped with its size class
//   in an 8-byte header immediately before the payload, so free() recovers
//   the owning bucket from the pointer alone. Requests larger than 256 bytes
//   fall back to malloc (also stamped, with a distinct large marker) and are
//   counted separately in the statistics. Memory is fetched from the OS in
//   chunks (128 cells per bucket per malloc) and carved into cells; chunks
//   are never returned to the OS in this version (a pool, mirroring the GC's
//   non-moving heap).
//
//   Alignment: every returned pointer is 8-byte aligned (64-bit safe). Cell
//   sizes are multiples of 8 and chunks come from malloc, so cell bases are
//   always 8-aligned; the user pointer is the cell base plus the 8-byte
//   header.
//
// THREAD SAFETY (reserved lock slot, iron rule 2j)
//   The pool is SINGLE-THREADED by contract in the current milestone. A lock
//   slot is reserved: compiling with IL2CPP_ALLOCATOR_THREAD_SAFE wraps every
//   state mutation in a mutex without changing any call site -- the future
//   multi-threaded runtime (JIT / concurrent managed threads) flips one
//   define instead of touching the pool.
//
// JIT REPLACEMENT (future instrumentation / allocator swap)
//   Every allocation flows through a function-pointer table (AllocatorTable).
//   il2cpp_alloc_fast / il2cpp_free_fast / il2cpp_alloc_stats are thin
//   wrappers over the CURRENT table, so swapping the table
//   (il2cpp_allocator_set) re-routes every call site -- the seam a future
//   JIT uses to install an instrumented or entirely different allocator.
//   The default table points at the pool implementation. Table entries must
//   be non-null (il2cpp_allocator_set aborts otherwise); the table itself is
//   copied by value, so the function pointers must outlive the table.
//
// STATISTICS
//   il2cpp_alloc_stats() returns an immutable snapshot (counts, per-bucket
//   counts, total requested bytes, large-path counts, chunk carves) for
//   debugging and benchmarking; il2cpp_alloc_bucket_hit_rate(b) reports the
//   fraction of all fast allocations served by bucket b.
//------------------------------------------------------------------------------

#include <cstddef>
#include <cstdint>

namespace il2cpp {
namespace alloc {

// Number of size classes and their payload sizes (bytes).
constexpr int kNumBuckets = 5;
constexpr size_t kBucketSizes[kNumBuckets] = { 16, 32, 64, 128, 256 };

// Allocation statistics snapshot. Returned by value from
// il2cpp_alloc_stats(): an immutable point-in-time copy.
struct AllocStats
{
    uint64_t allocCount;                  // total fast allocations (pool + large)
    uint64_t freeCount;                   // total fast frees
    uint64_t totalBytes;                  // total bytes REQUESTED by fast allocations
    uint64_t bucketAllocs[kNumBuckets];   // per-bucket allocation counts
    uint64_t largeAllocs;                 // allocations > 256 bytes (malloc fallback)
    uint64_t largeBytes;                  // bytes requested on the large path
    uint64_t chunkCarves;                 // OS chunks carved (footprint proxy)
};

// Function-pointer types -- the JIT replacement seam (see the header comment).
typedef void* (*il2cpp_alloc_fn)(size_t size);
typedef void  (*il2cpp_free_fn)(void* ptr);
typedef AllocStats (*il2cpp_alloc_stats_fn)();

// The allocator table: allocation, free and statistics go through this
// indirection. The default table points at the pool implementation.
struct AllocatorTable
{
    il2cpp_alloc_fn       alloc;  // allocate 'size' bytes (8-byte aligned)
    il2cpp_free_fn        free;   // free a pointer previously returned by alloc
    il2cpp_alloc_stats_fn stats;  // snapshot of the allocator's statistics
};

// Query the current allocator table (default = the pool). The returned
// reference is the LIVE table: it is what il2cpp_alloc_fast goes through,
// and it tracks later swaps. To save/restore the table, capture it BY VALUE
// (a reference held across a swap aliases the swapped-in table, making a
// later "restore" a self-copying no-op).
const AllocatorTable& il2cpp_allocator();

// Swap the current table (JIT hook). All three entries must be non-null
// (violations abort -- fail loud, matching the runtime's style). The table
// is copied by value; the pointed-to functions must outlive the table.
void il2cpp_allocator_set(const AllocatorTable& table);

// Fast allocation: 8-byte aligned non-null pointer, O(1) for sizes <= 256
// (malloc fallback above). Zero-size requests land in the 16-byte bucket
// (non-null). Never returns null (out-of-memory aborts).
inline void* il2cpp_alloc_fast(size_t size)
{
    return il2cpp_allocator().alloc(size);
}

// Fast free: O(1) return to the owning bucket. Null is a no-op. The pointer
// MUST come from the same allocator table (the pool reads the block header
// to recover the bucket; a foreign pointer is a caller bug).
inline void il2cpp_free_fast(void* ptr)
{
    il2cpp_allocator().free(ptr);
}

// Statistics snapshot of the CURRENT table (see AllocStats).
inline AllocStats il2cpp_alloc_stats()
{
    return il2cpp_allocator().stats();
}

// Bucket hit rate for bucket 'bucketIndex' (0..kNumBuckets-1): the fraction
// of all fast allocations served by that bucket; 0 when nothing was
// allocated yet, and 0 for out-of-range indices.
double il2cpp_alloc_bucket_hit_rate(int bucketIndex);

} // namespace alloc
} // namespace il2cpp
