//------------------------------------------------------------------------------
// test_bench_allocator.cpp -- benchmark for the non-GC allocation pool.
//
// Measures alloc+free cycles per second for the fast pool (il2cpp::alloc)
// against the same pattern on plain malloc, then prints the pool statistics
// and per-bucket hit rates.
//
// Naming note: the tests/runtime CMakeLists auto-discovers executables by the
// "test_*.cpp" glob, so this file is prefixed "test_" (the executable is
// named test_bench_allocator) -- bench_allocator.cpp would never be picked up
// by the glob and could not be built without touching CMakeLists.
//
// A benchmark, not a unit test: it prints numbers and exits 0. CI runs it for
// regression visibility (a catastrophic allocation slowdown shows up as a
// orders-of-magnitude drop in the ops/s column).
//------------------------------------------------------------------------------

#include "il2cpp_allocator.h"

#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace {

// A mixed size pattern mimicking temporary-buffer workloads: everything is a
// pool size except nothing exceeds the largest bucket (all <= 256).
const size_t kSizes[] = { 16, 32, 64, 128, 256, 100, 7, 48, 200, 1 };
const size_t kPatternLen = sizeof(kSizes) / sizeof(kSizes[0]);

// Alloc+free cycles per measured path.
const uint64_t kIters = 2000000ull;

// Pointer checksum sink. High optimization levels legally elide a
// malloc/memset/free cycle whose memory never escapes; feeding the returned
// pointer value into this accumulator keeps every cycle observable (a
// dangling pointer VALUE, never dereferenced, is safe to read after free).
uintptr_t g_checksum = 0;

// Seconds elapsed between two steady-clock time points.
double SecondsSince(const std::chrono::steady_clock::time_point& start)
{
    const auto now = std::chrono::steady_clock::now();
    return std::chrono::duration<double>(now - start).count();
}

} // namespace

int main()
{
    // Warm-up: one pass over the pattern so lazy bucket chunks and cold
    // caches do not pollute the timed runs.
    for (size_t i = 0; i < kPatternLen; ++i)
    {
        void* p = il2cpp::alloc::il2cpp_alloc_fast(kSizes[i]);
        if (p == nullptr)
        {
            std::printf("bench aborted: warm-up allocation failed\n");
            return 1;
        }
        il2cpp::alloc::il2cpp_free_fast(p);
    }

    // --- Fast pool path: O(1) alloc + free per cycle ---
    const auto poolStart = std::chrono::steady_clock::now();
    for (uint64_t i = 0; i < kIters; ++i)
    {
        const size_t size = kSizes[i % kPatternLen];
        void* p = il2cpp::alloc::il2cpp_alloc_fast(size);
        if (p == nullptr)
        {
            std::printf("bench aborted: pool allocation failed at cycle %llu\n",
                        static_cast<unsigned long long>(i));
            return 1;
        }
        std::memset(p, 0xAB, size);
        g_checksum ^= reinterpret_cast<uintptr_t>(p);
        il2cpp::alloc::il2cpp_free_fast(p);
    }
    const double poolSeconds = SecondsSince(poolStart);

    // --- malloc comparison: identical pattern ---
    const auto mallocStart = std::chrono::steady_clock::now();
    for (uint64_t i = 0; i < kIters; ++i)
    {
        const size_t size = kSizes[i % kPatternLen];
        void* p = std::malloc(size);
        if (p == nullptr)
        {
            std::printf("bench aborted: malloc failed at cycle %llu\n",
                        static_cast<unsigned long long>(i));
            return 1;
        }
        std::memset(p, 0xAB, size);
        g_checksum ^= reinterpret_cast<uintptr_t>(p);
        std::free(p);
    }
    const double mallocSeconds = SecondsSince(mallocStart);

    const double poolMops = static_cast<double>(kIters) / poolSeconds / 1e6;
    const double mallocMops = static_cast<double>(kIters) / mallocSeconds / 1e6;

    std::printf("fast pool : %8.2f M alloc+free/s (%.3f s for %llu cycles)\n",
                poolMops, poolSeconds, static_cast<unsigned long long>(kIters));
    std::printf("malloc    : %8.2f M alloc+free/s (%.3f s for %llu cycles)\n",
                mallocMops, mallocSeconds, static_cast<unsigned long long>(kIters));
    std::printf("speedup   : %8.2fx\n", poolMops / mallocMops);

    const il2cpp::alloc::AllocStats s = il2cpp::alloc::il2cpp_alloc_stats();
    std::printf("stats     : allocs=%llu frees=%llu bytes=%llu chunks=%llu large=%llu\n",
                static_cast<unsigned long long>(s.allocCount),
                static_cast<unsigned long long>(s.freeCount),
                static_cast<unsigned long long>(s.totalBytes),
                static_cast<unsigned long long>(s.chunkCarves),
                static_cast<unsigned long long>(s.largeAllocs));
    std::printf("checksum  : %llu\n", static_cast<unsigned long long>(g_checksum));
    for (int b = 0; b < il2cpp::alloc::kNumBuckets; ++b)
    {
        std::printf("  bucket %3zu B: %llu allocs, hit rate %5.1f%%\n",
                    il2cpp::alloc::kBucketSizes[b],
                    static_cast<unsigned long long>(s.bucketAllocs[b]),
                    il2cpp::alloc::il2cpp_alloc_bucket_hit_rate(b) * 100.0);
    }
    return 0;
}
