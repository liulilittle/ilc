//------------------------------------------------------------------------------
// test_bench_inline.cpp -- benchmark for the hot-path runtime helpers
// (generated-code audit Top 1: il2cpp_array_get_elem_ptr / get_length /
// il2cpp_string_get_length / il2cpp_null_check).
//
// Measures nanoseconds per operation for 10,000,000 calls of each helper.
// Run it BEFORE and AFTER the header-inline change (same machine, Release
// build) and compare; the post-inline disassembly must show no call to the
// inlined helpers inside the hot loops (only the cold abort paths remain
// as call targets).
//
// Anti-elimination design (mirrors test_bench_allocator.cpp):
//   * every measured value feeds a checksum printed at the end
//     (externally observable, so -O3 cannot elide the loops)
//   * the element index is data-dependent on the loop counter
//     (idx = (i * K) >> 22), so no index/base is loop-invariant
//   * the array/string are runtime-allocated, so their contents are
//     unknown to the compiler and sums cannot be constant-folded
//   * the null_check receiver is read from heap slots (runtime value the
//     compiler cannot prove non-null), so the null test survives inlining
//
// A benchmark, not a unit test: it also performs sanity assertions on the
// helper contracts and exits 0 (regression visibility in CI).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <chrono>
#include <cstdint>
#include <cstdio>

namespace {

const uint64_t kIters = 10000000ull; // 10M ops per benchmark
const int32_t kArrayLen = 4096;
const uint32_t kIndexMask = 1023u;   // varying index in [0, 1024)

// Externally observable checksum sink (see header comment). NOTE: with an
// even number of identical rounds (1 warm-up + 3 timed), each bench's sum
// xors with itself an even number of times and cancels to 0 -- expected;
// the anti-elimination guarantee comes from the disassembly, not from the
// printed value.
uint64_t g_checksum = 0;

double SecondsSince(const std::chrono::steady_clock::time_point& start)
{
    const auto now = std::chrono::steady_clock::now();
    return std::chrono::duration<double>(now - start).count();
}

// Runs one benchmark round; returns nanoseconds per operation.
double BenchNsPerOp(const char* name, void (*fn)())
{
    const auto start = std::chrono::steady_clock::now();
    fn();
    const double secs = SecondsSince(start);
    const double nsPerOp = secs * 1e9 / static_cast<double>(kIters);
    std::printf("bench %-22s: %8.2f ns/op (%.3f s for %llu ops)\n",
                name, nsPerOp, secs, static_cast<unsigned long long>(kIters));
    return nsPerOp;
}

il2cpp::Il2CppArray* g_arr = nullptr;
il2cpp::Il2CppArray* g_slotArr = nullptr;
il2cpp::Il2CppString* g_str = nullptr;
il2cpp::Il2CppObject** g_slots = nullptr;

// --- 1. Element pointer: per-element bounds check + pointer arithmetic ---
void BenchElemPtr()
{
    int32_t sum = 0;
    for (uint64_t i = 0; i < kIters; ++i)
    {
        const uint32_t idx = (static_cast<uint32_t>(i) * 2654435761u) >> 22;
        sum += *reinterpret_cast<const int32_t*>(
            il2cpp::il2cpp_array_get_elem_ptr(g_arr, static_cast<int32_t>(idx & kIndexMask)));
    }
    g_checksum ^= static_cast<uint64_t>(sum);
}

// --- 2. Array length: the audit's loop-hoisting case ---
void BenchArrayLength()
{
    int32_t sum = 0;
    for (uint64_t i = 0; i < kIters; ++i)
    {
        sum += il2cpp::il2cpp_array_get_length(g_arr);
    }
    g_checksum ^= static_cast<uint64_t>(sum);
}

// --- 3. String length: plain field read ---
void BenchStringLength()
{
    int32_t sum = 0;
    for (uint64_t i = 0; i < kIters; ++i)
    {
        sum += il2cpp::il2cpp_string_get_length(reinterpret_cast<il2cpp::Il2CppObject*>(g_str));
    }
    g_checksum ^= static_cast<uint64_t>(sum);
}

// --- 4. Null check: receiver read from heap (not provably non-null) ---
void BenchNullCheck()
{
    uintptr_t sum = 0;
    for (uint64_t i = 0; i < kIters; ++i)
    {
        il2cpp::Il2CppObject* o = g_slots[(i & kIndexMask)];
        sum += reinterpret_cast<uintptr_t>(il2cpp::il2cpp_null_check(o));
    }
    g_checksum ^= static_cast<uint64_t>(sum);
}

} // namespace

int main()
{
    il2cpp::Il2CppClass* mock = nullptr; // primitive array: element class unused
    g_arr = il2cpp::il2cpp_array_new(mock, 4, false, kArrayLen);
    if (g_arr == nullptr)
    {
        std::printf("bench aborted: array allocation failed\n");
        return 1;
    }
    g_str = il2cpp::il2cpp_string_new_from_cs("benchmark-string");
    if (g_str == nullptr)
    {
        std::printf("bench aborted: string allocation failed\n");
        return 1;
    }

    // Sanity assertions on the helper contracts (regression checks).
    int failures = 0;
    if (il2cpp::il2cpp_array_get_length(g_arr) != kArrayLen) ++failures;
    if (il2cpp::il2cpp_string_get_length(reinterpret_cast<il2cpp::Il2CppObject*>(g_str)) != 16) ++failures;
    if (il2cpp::il2cpp_array_get_elem_ptr(g_arr, 0) != g_arr->data) ++failures;
    if (il2cpp::il2cpp_array_get_elem_ptr(g_arr, kArrayLen - 1) !=
        g_arr->data + static_cast<size_t>(kArrayLen - 1) * 4u) ++failures;
    if (failures != 0)
    {
        std::printf("%d bench-inline assertion(s) failed\n", failures);
        return 1;
    }

    // Reference-slot array for the null_check benchmark: every slot holds a
    // non-null pointer (the array's own data address -- never dereferenced
    // by null_check, which only tests and returns the receiver). Uses a
    // SEPARATE array so the elem_ptr bench keeps reading its zeroed int32
    // payload (sum == 0 is deterministic).
    g_slotArr = il2cpp::il2cpp_array_new(mock, 8, true, kArrayLen);
    if (g_slotArr == nullptr)
    {
        std::printf("bench aborted: slot array allocation failed\n");
        return 1;
    }
    g_slots = reinterpret_cast<il2cpp::Il2CppObject**>(g_slotArr->data);
    for (int32_t i = 0; i < kArrayLen; ++i)
    {
        g_slots[i] = reinterpret_cast<il2cpp::Il2CppObject*>(&g_slotArr->data[i]);
    }

    // Warm-up: one pass so cold caches and lazy registration do not pollute
    // the timed runs (array class registration happens on first new).
    BenchElemPtr();
    BenchArrayLength();
    BenchStringLength();
    BenchNullCheck();

    // Timed rounds (3 each for stability evidence).
    for (int round = 1; round <= 3; ++round)
    {
        std::printf("--- round %d ---\n", round);
        BenchNsPerOp("get_elem_ptr", BenchElemPtr);
        BenchNsPerOp("get_length", BenchArrayLength);
        BenchNsPerOp("string_get_length", BenchStringLength);
        BenchNsPerOp("null_check", BenchNullCheck);
    }

    std::printf("checksum  : %llu\n", static_cast<unsigned long long>(g_checksum));
    std::printf("all runtime bench-inline tests passed\n");
    return 0;
}
