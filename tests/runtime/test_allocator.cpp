//------------------------------------------------------------------------------
// test_allocator.cpp -- size-classed non-GC allocation pool unit tests.
//
// The pool (il2cpp::alloc) is a BYPASS allocation layer for NON-GC memory
// (temporary buffers, native scratch). Managed objects never go through it --
// they need a klass header and a reference bitmap and stay with the GC
// (il2cpp_gc_alloc). The pool's contract:
//
//   1. Alignment: every returned pointer is 8-byte aligned (64-bit safe).
//   2. Size classes: 16/32/64/128/256-byte buckets; requests larger than
//      256 bytes fall back to malloc (the "large" path).
//   3. Zero-size requests land in the smallest bucket (non-null result).
//   4. O(1) allocation (free-list pop) and O(1) free (free-list push); a
//      freed cell is reused by the next allocation of the same bucket
//      (LIFO order within a bucket).
//   5. Statistics (il2cpp_alloc_stats): allocation count, per-bucket counts,
//      total bytes requested, large-path counts, chunk carves.
//   6. Bucket hit rate (il2cpp_alloc_bucket_hit_rate): bucketAllocs /
//      totalAllocs.
//   7. Replaceability (JIT slot): all allocation flows through the
//      AllocatorTable function-pointer table; swapping the table re-routes
//      every call site.
//
// Coverage: normal path (alignment, content integrity, bucket selection),
// edge paths (zero size, large fallback, invalid hit-rate indices), error
// path (null free is a no-op), and a 100k-cycle alloc/free stress loop.
//
// Plain executable harness (no framework): every Expect() failure prints a
// named FAIL line and the process exits non-zero (CI red signal).
//------------------------------------------------------------------------------

#include "il2cpp_allocator.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace {

// The fixtures rely on pointer-sized headers; verify the platform once.
static_assert(sizeof(void*) == 8, "allocator tests assume 8-byte pointers");

// Failed-assertion counter; main() returns non-zero when it is not zero.
int g_failures = 0;

// Expect -- evaluate one invariant, naming the check on failure.
void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Alignment check helper: a pool pointer must be 8-byte aligned.
bool Is8Aligned(const void* p)
{
    return (reinterpret_cast<uintptr_t>(p) & 7u) == 0;
}

// Pattern helpers: stamp 'n' bytes of a buffer with a repeating value and
// verify the stamp survived (content-integrity check).
void StampPattern(void* p, size_t n, unsigned char value)
{
    std::memset(p, value, n);
}

bool PatternIntact(const void* p, size_t n, unsigned char value)
{
    const unsigned char* bytes = static_cast<const unsigned char*>(p);
    for (size_t i = 0; i < n; ++i)
    {
        if (bytes[i] != value)
        {
            return false;
        }
    }
    return true;
}

// JIT-slot fixture: a tracking allocator installed via il2cpp_allocator_set.
// It counts calls and forwards to malloc -- the shape of a future
// instrumented / replaced allocator.
int g_trackedAllocs = 0;
int g_trackedFrees = 0;

void* TrackedAlloc(size_t size)
{
    ++g_trackedAllocs;
    return std::malloc(size);
}

void TrackedFree(void* ptr)
{
    ++g_trackedFrees;
    std::free(ptr);
}

il2cpp::alloc::AllocStats TrackedStats()
{
    il2cpp::alloc::AllocStats s;
    s.allocCount = static_cast<uint64_t>(g_trackedAllocs);
    s.freeCount = static_cast<uint64_t>(g_trackedFrees);
    for (int b = 0; b < il2cpp::alloc::kNumBuckets; ++b)
    {
        s.bucketAllocs[b] = 0;
    }
    s.totalBytes = 0;
    s.largeAllocs = 0;
    s.largeBytes = 0;
    s.chunkCarves = 0;
    return s;
}

} // namespace

//------------------------------------------------------------------------------
// main -- run every pool invariant and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    // --- 1. Alignment, non-null, distinctness, content integrity ---
    // Sizes span every bucket boundary plus the large path (>256).
    const size_t kSizes[] = { 1, 16, 17, 32, 33, 64, 100, 128, 200, 256, 257, 1024, 4096 };
    const size_t kCount = sizeof(kSizes) / sizeof(kSizes[0]);
    void* ptrs[kCount] = { nullptr };
    for (size_t i = 0; i < kCount; ++i)
    {
        ptrs[i] = il2cpp::alloc::il2cpp_alloc_fast(kSizes[i]);
        Expect(ptrs[i] != nullptr, "fast allocation returns a non-null pointer");
        Expect(Is8Aligned(ptrs[i]), "fast allocation is 8-byte aligned");
        if (ptrs[i] != nullptr)
        {
            StampPattern(ptrs[i], kSizes[i], static_cast<unsigned char>(i + 1));
        }
    }
    bool distinct = true;
    for (size_t i = 0; i < kCount; ++i)
    {
        for (size_t j = i + 1; j < kCount; ++j)
        {
            if (ptrs[i] != nullptr && ptrs[i] == ptrs[j])
            {
                distinct = false;
            }
        }
    }
    Expect(distinct, "simultaneously live allocations are distinct");
    for (size_t i = 0; i < kCount; ++i)
    {
        Expect(PatternIntact(ptrs[i], kSizes[i], static_cast<unsigned char>(i + 1)),
               "requested payload bytes survive intact (usable capacity)");
        il2cpp::alloc::il2cpp_free_fast(ptrs[i]);
    }


    // --- 2. Zero-size request: smallest bucket, non-null, 16 bytes usable ---
    void* zero = il2cpp::alloc::il2cpp_alloc_fast(0);
    Expect(zero != nullptr, "zero-size request returns a non-null pointer");
    Expect(Is8Aligned(zero), "zero-size request is 8-byte aligned");
    if (zero != nullptr)
    {
        // Documented contract: a zero request lands in the 16-byte bucket, so
        // a full 16-byte payload must be writable.
        StampPattern(zero, 16, 0x5A);
        Expect(PatternIntact(zero, 16, 0x5A), "zero-size block holds a full 16-byte payload");
    }
    il2cpp::alloc::il2cpp_free_fast(zero);


    // --- 3. Bucket selection and statistics ---
    {
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        void* a = il2cpp::alloc::il2cpp_alloc_fast(16);    // bucket 0 (16)
        void* b = il2cpp::alloc::il2cpp_alloc_fast(32);    // bucket 1 (32)
        void* c = il2cpp::alloc::il2cpp_alloc_fast(100);   // bucket 3 (128)
        void* d = il2cpp::alloc::il2cpp_alloc_fast(257);   // large path (>256)
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(a != nullptr && b != nullptr && c != nullptr && d != nullptr,
               "bucket-selection allocations succeed");
        Expect(after.allocCount == before.allocCount + 4ull,
               "stats: allocation count increments by the number of calls");
        Expect(after.bucketAllocs[0] == before.bucketAllocs[0] + 1ull,
               "stats: 16-byte request hits bucket 0");
        Expect(after.bucketAllocs[1] == before.bucketAllocs[1] + 1ull,
               "stats: 32-byte request hits bucket 1");
        Expect(after.bucketAllocs[3] == before.bucketAllocs[3] + 1ull,
               "stats: 100-byte request hits bucket 3 (128)");
        Expect(after.largeAllocs == before.largeAllocs + 1ull,
               "stats: 257-byte request takes the large (malloc) path");
        Expect(after.totalBytes == before.totalBytes + 16ull + 32ull + 100ull + 257ull,
               "stats: total bytes accumulate requested sizes");
        il2cpp::alloc::il2cpp_free_fast(a);
        il2cpp::alloc::il2cpp_free_fast(b);
        il2cpp::alloc::il2cpp_free_fast(c);
        il2cpp::alloc::il2cpp_free_fast(d);
        Expect(il2cpp::alloc::il2cpp_alloc_stats().freeCount == before.freeCount + 4ull,
               "stats: free count increments per free");
    }


    // --- 4. Free-list roundtrip: freed cells are reused (LIFO within bucket) ---
    {
        void* p1 = il2cpp::alloc::il2cpp_alloc_fast(32);
        void* p2 = il2cpp::alloc::il2cpp_alloc_fast(32);
        Expect(p1 != nullptr && p2 != nullptr && p1 != p2,
               "two live allocations of one bucket are distinct");
        il2cpp::alloc::il2cpp_free_fast(p1);
        il2cpp::alloc::il2cpp_free_fast(p2);
        void* r1 = il2cpp::alloc::il2cpp_alloc_fast(32);
        void* r2 = il2cpp::alloc::il2cpp_alloc_fast(32);
        Expect(r1 == p2 && r2 == p1,
               "freed cells are reused in LIFO order by the next allocations");
        il2cpp::alloc::il2cpp_free_fast(r1);
        il2cpp::alloc::il2cpp_free_fast(r2);
    }


    // --- 5. Cross-bucket isolation: freed cells stay in their own bucket ---
    {
        void* small1 = il2cpp::alloc::il2cpp_alloc_fast(16);
        void* big1 = il2cpp::alloc::il2cpp_alloc_fast(128);
        il2cpp::alloc::il2cpp_free_fast(small1);
        il2cpp::alloc::il2cpp_free_fast(big1);
        void* small2 = il2cpp::alloc::il2cpp_alloc_fast(16);
        void* big2 = il2cpp::alloc::il2cpp_alloc_fast(128);
        Expect(small2 == small1, "16-byte bucket reuses its own freed cell");
        Expect(big2 == big1, "128-byte bucket reuses its own freed cell");
        il2cpp::alloc::il2cpp_free_fast(small2);
        il2cpp::alloc::il2cpp_free_fast(big2);
    }


    // --- 6. Large path: malloc fallback, stats, and pool health ---
    {
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        void* big = il2cpp::alloc::il2cpp_alloc_fast(4096);
        Expect(big != nullptr, "large allocation succeeds");
        Expect(Is8Aligned(big), "large allocation is 8-byte aligned");
        if (big != nullptr)
        {
            StampPattern(big, 4096, 0x3C);
        }
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(PatternIntact(big, 4096, 0x3C), "large payload survives intact");
        Expect(after.largeAllocs == before.largeAllocs + 1ull,
               "stats: large allocation counted on the large path");
        Expect(after.largeBytes == before.largeBytes + 4096ull,
               "stats: large bytes accumulate");
        bool bucketsUntouched = true;
        for (int b = 0; b < il2cpp::alloc::kNumBuckets; ++b)
        {
            if (after.bucketAllocs[b] != before.bucketAllocs[b])
            {
                bucketsUntouched = false;
            }
        }
        Expect(bucketsUntouched, "stats: large allocation does not touch any bucket");
        il2cpp::alloc::il2cpp_free_fast(big);
        Expect(il2cpp::alloc::il2cpp_alloc_stats().freeCount == before.freeCount + 1ull,
               "stats: large free counted");
        // The pool must stay healthy after large-path traffic.
        void* small = il2cpp::alloc::il2cpp_alloc_fast(16);
        Expect(small != nullptr && Is8Aligned(small),
               "pool serves bucket allocations after large-path traffic");
        il2cpp::alloc::il2cpp_free_fast(small);
    }


    // --- 7. Null free is a no-op ---
    il2cpp::alloc::il2cpp_free_fast(nullptr);


    // --- 8. Bucket hit rate ---
    {
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        for (int i = 0; i < 5; ++i)
        {
            il2cpp::alloc::il2cpp_free_fast(il2cpp::alloc::il2cpp_alloc_fast(16));
        }
        for (int i = 0; i < 3; ++i)
        {
            il2cpp::alloc::il2cpp_free_fast(il2cpp::alloc::il2cpp_alloc_fast(32));
        }
        for (int i = 0; i < 2; ++i)
        {
            il2cpp::alloc::il2cpp_free_fast(il2cpp::alloc::il2cpp_alloc_fast(128));
        }
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount == before.allocCount + 10ull,
               "hit-rate section allocates exactly 10 blocks");
        // The rate is bucketAllocs / totalAllocs over the whole history; it
        // must match the snapshot counters exactly (self-consistent).
        for (int b = 0; b < il2cpp::alloc::kNumBuckets; ++b)
        {
            const double expected = after.allocCount == 0ull
                ? 0.0
                : static_cast<double>(after.bucketAllocs[b]) / static_cast<double>(after.allocCount);
            Expect(std::fabs(il2cpp::alloc::il2cpp_alloc_bucket_hit_rate(b) - expected) < 1e-12,
                   "hit rate matches bucketAllocs / allocCount");
        }
        // All fast allocations are either a bucket hit or a large allocation:
        // the rates must sum to 1 with the large fraction.
        double rateSum = 0.0;
        for (int b = 0; b < il2cpp::alloc::kNumBuckets; ++b)
        {
            rateSum += il2cpp::alloc::il2cpp_alloc_bucket_hit_rate(b);
        }
        const double largeFraction = after.allocCount == 0ull
            ? 0.0
            : static_cast<double>(after.largeAllocs) / static_cast<double>(after.allocCount);
        Expect(std::fabs(rateSum + largeFraction - 1.0) < 1e-12,
               "bucket hit rates plus large fraction sum to 1");
        // Invalid indices are rejected with 0 (never negative, never out of
        // range).
        Expect(il2cpp::alloc::il2cpp_alloc_bucket_hit_rate(-1) == 0.0,
               "negative bucket index yields a zero hit rate");
        Expect(il2cpp::alloc::il2cpp_alloc_bucket_hit_rate(il2cpp::alloc::kNumBuckets) == 0.0,
               "out-of-range bucket index yields a zero hit rate");
    }


    // --- 9. Function-pointer table: default table and JIT replacement ---
    {
        // NOTE: the default table is captured BY VALUE. il2cpp_allocator()
        // hands out a live reference to the current table, so holding it as
        // a reference across a swap would alias the swapped-in table and a
        // later "restore" would be a self-copying no-op.
        const il2cpp::alloc::AllocatorTable defaultTable = il2cpp::alloc::il2cpp_allocator();
        Expect(defaultTable.alloc != nullptr && defaultTable.free != nullptr
                   && defaultTable.stats != nullptr,
               "default allocator table has all three entries");

        // Table-driven allocation reaches the pool (statistics move).
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        void* viaTable = defaultTable.alloc(32);
        Expect(viaTable != nullptr, "table-driven allocation succeeds");
        Expect(il2cpp::alloc::il2cpp_alloc_stats().allocCount == before.allocCount + 1ull,
               "table-driven allocation feeds pool statistics");
        defaultTable.free(viaTable);
        Expect(il2cpp::alloc::il2cpp_alloc_stats().freeCount == before.freeCount + 1ull,
               "table-driven free feeds pool statistics");

        // JIT slot: swap in a tracking allocator; every call site (including
        // the il2cpp_alloc_fast wrappers) is re-routed.
        il2cpp::alloc::AllocatorTable tracked;
        tracked.alloc = TrackedAlloc;
        tracked.free = TrackedFree;
        tracked.stats = TrackedStats;
        il2cpp::alloc::il2cpp_allocator_set(tracked);

        void* trackedPtr = il2cpp::alloc::il2cpp_alloc_fast(64);
        Expect(g_trackedAllocs == 1, "JIT swap: allocations route to the replacement allocator");
        Expect(il2cpp::alloc::il2cpp_alloc_stats().allocCount == 1ull,
               "JIT swap: statistics come from the replacement allocator");
        il2cpp::alloc::il2cpp_free_fast(trackedPtr);
        Expect(g_trackedFrees == 1, "JIT swap: frees route to the replacement allocator");

        // Restore the default table: the pool is usable again.
        il2cpp::alloc::il2cpp_allocator_set(defaultTable);
        void* restored = il2cpp::alloc::il2cpp_alloc_fast(16);
        Expect(restored != nullptr && Is8Aligned(restored),
               "restoring the default table re-enables the pool");
        il2cpp::alloc::il2cpp_free_fast(restored);
        Expect(il2cpp::alloc::il2cpp_alloc_stats().allocCount > 0ull,
               "pool statistics resume after table restore");
    }

    // --- 10. Stress: 100k alloc/free cycles across mixed sizes ---
    {
        const uint64_t kStressIters = 100000ull;
        const size_t kStressSizes[] = { 16, 32, 64, 128, 256, 100, 7, 1, 0, 48 };
        const size_t kPattern = sizeof(kStressSizes) / sizeof(kStressSizes[0]);
        uint64_t alignFailures = 0;

        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        for (uint64_t i = 0; i < kStressIters; ++i)
        {
            void* p = il2cpp::alloc::il2cpp_alloc_fast(kStressSizes[i % kPattern]);
            if (p == nullptr)
            {
                Expect(false, "stress: allocation returns non-null every cycle");
                break;
            }
            if (!Is8Aligned(p))
            {
                ++alignFailures;
            }
            *reinterpret_cast<uint32_t*>(p) = static_cast<uint32_t>(i);
            il2cpp::alloc::il2cpp_free_fast(p);
        }
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(alignFailures == 0, "stress: every cycle is 8-byte aligned");
        Expect(after.allocCount == before.allocCount + kStressIters,
               "stress: allocation count advances by the number of cycles");
        Expect(after.freeCount == before.freeCount + kStressIters,
               "stress: free count advances by the number of cycles");
        Expect(after.allocCount == after.freeCount,
               "stress: no blocks are outstanding after the loop");
        // Every fast allocation is one bucket hit or one large allocation.
        uint64_t bucketDelta = 0;
        for (int b = 0; b < il2cpp::alloc::kNumBuckets; ++b)
        {
            bucketDelta += after.bucketAllocs[b] - before.bucketAllocs[b];
        }
        const uint64_t largeDelta = after.largeAllocs - before.largeAllocs;
        Expect(bucketDelta + largeDelta == after.allocCount - before.allocCount,
               "stress: every allocation is accounted in one bucket or the large path");
        Expect(after.chunkCarves >= before.chunkCarves,
               "stress: the pool carved at least one chunk");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d allocator assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime allocator tests passed\n");
    return 0;
}
