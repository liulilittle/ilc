//------------------------------------------------------------------------------
// test_interlocked.cpp -- System.Threading.Interlocked icall runtime unit
// tests. See il2cpp_interlocked.h for the contract under test.
//
// Coverage (normal + edge + error paths):
//   1. int32 round trip     : increment/decrement/exchange/add return the
//      .NET-documented values and update the location exactly.
//   2. int32 compare_exchange: match -> returns old value and replaces;
//      mismatch -> returns old value and leaves the location untouched.
//   3. int64 round trip     : same set for the 64-bit family.
//   4. pointer round trip   : exchange/compare_exchange over raw pointers
//      (the object-reference forms); match and mismatch paths.
//   5. Multi-threaded increment consistency: 8 threads x 100000
//      il2cpp_interlocked_increment on a shared counter -> exactly 800000
//      (an atomic counter cannot lose updates).
//   6. Multi-threaded add consistency: 8 threads x 100000
//      il2cpp_interlocked_add(counter, 2) -> exactly 1600000.
//   7. Spin lock from compare_exchange: 8 threads x 100000 lock-guarded
//      increments (the lock is built ONLY from
//      il2cpp_interlocked_compare_exchange + il2cpp_interlocked_exchange,
//      demonstrating the compare_exchange primitive) -> exactly 800000.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_interlocked.h"

#include <cstdint>
#include <cstdio>
#include <thread>
#include <vector>

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

// Spin lock built from the compare_exchange primitive (the documented
// purpose of the icall): acquire = CAS(0 -> 1), release = exchange(1 -> 0).
class SpinLock
{
public:
    SpinLock() : m_flag(0) {}

    void Acquire()
    {
        while (il2cpp::runtime::il2cpp_interlocked_compare_exchange(&m_flag, 1, 0) != 0)
        {
            // spin: another thread holds the lock
        }
    }

    void Release()
    {
        il2cpp::runtime::il2cpp_interlocked_exchange(&m_flag, 0);
    }

private:
    int32_t m_flag;
};

// 8 threads x iterations atomic increments on 'counter' (no lock: the
// atomicity of the icall is what makes the total exact).
void AddIncrements(int32_t* counter, int iterations)
{
    for (int i = 0; i < iterations; ++i)
    {
        il2cpp::runtime::il2cpp_interlocked_increment(counter);
    }
}

void AddIncrements64(int64_t* counter, int iterations)
{
    for (int i = 0; i < iterations; ++i)
    {
        il2cpp::runtime::il2cpp_interlocked_increment64(counter);
    }
}

void AddValues(int32_t* counter, int iterations)
{
    for (int i = 0; i < iterations; ++i)
    {
        il2cpp::runtime::il2cpp_interlocked_add(counter, 2);
    }
}

void AddIncrementsLocked(SpinLock* lock, int32_t* counter, int iterations)
{
    for (int i = 0; i < iterations; ++i)
    {
        lock->Acquire();
        ++(*counter); // guarded by the compare_exchange spin lock
        lock->Release();
    }
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 1. int32 round trip ---
    int32_t i32 = 0;
    Expect(il2cpp_interlocked_increment(&i32) == 1 && i32 == 1, "increment returns new value (1)");
    Expect(il2cpp_interlocked_increment(&i32) == 2 && i32 == 2, "increment returns new value (2)");
    Expect(il2cpp_interlocked_decrement(&i32) == 1 && i32 == 1, "decrement returns new value");
    Expect(il2cpp_interlocked_exchange(&i32, 10) == 1 && i32 == 10, "exchange returns old value");
    Expect(il2cpp_interlocked_add(&i32, 3) == 13 && i32 == 13, "add returns the sum");

    // --- 2. int32 compare_exchange: match replaces, mismatch leaves ---
    i32 = 7;
    Expect(il2cpp_interlocked_compare_exchange(&i32, 9, 7) == 7 && i32 == 9,
           "compare_exchange match: returns old, replaces");
    Expect(il2cpp_interlocked_compare_exchange(&i32, 42, 7) == 9 && i32 == 9,
           "compare_exchange mismatch: returns old, leaves untouched");

    // --- 2b. wrap-around (net48 unchecked arithmetic: no overflow trap) ---
    i32 = INT32_MAX;
    Expect(il2cpp_interlocked_add(&i32, 1) == INT32_MIN && i32 == INT32_MIN,
           "add wraps INT32_MAX + 1 -> INT32_MIN");
    i32 = INT32_MIN;
    Expect(il2cpp_interlocked_add(&i32, -1) == INT32_MAX && i32 == INT32_MAX,
           "add wraps INT32_MIN - 1 -> INT32_MAX");
    i32 = INT32_MAX;
    Expect(il2cpp_interlocked_increment(&i32) == INT32_MIN && i32 == INT32_MIN,
           "increment wraps INT32_MAX -> INT32_MIN");

    // --- 3. int64 round trip ---
    int64_t i64 = 0;
    Expect(il2cpp_interlocked_increment64(&i64) == 1 && i64 == 1, "increment64 returns new value");
    Expect(il2cpp_interlocked_decrement64(&i64) == 0 && i64 == 0, "decrement64 returns new value");
    Expect(il2cpp_interlocked_exchange64(&i64, 100) == 0 && i64 == 100, "exchange64 returns old value");
    Expect(il2cpp_interlocked_add64(&i64, 5) == 105 && i64 == 105, "add64 returns the sum");
    i64 = 7;
    Expect(il2cpp_interlocked_compare_exchange64(&i64, 9, 7) == 7 && i64 == 9,
           "compare_exchange64 match: returns old, replaces");
    Expect(il2cpp_interlocked_compare_exchange64(&i64, 42, 7) == 9 && i64 == 9,
           "compare_exchange64 mismatch: returns old, leaves untouched");
    i64 = INT64_MAX;
    Expect(il2cpp_interlocked_add64(&i64, 1) == INT64_MIN && i64 == INT64_MIN,
           "add64 wraps INT64_MAX + 1 -> INT64_MIN");

    // --- 4. pointer (object reference) round trip ---
    void* slot = nullptr;
    int a = 1, b = 2;
    Expect(il2cpp_interlocked_exchange_pointer(&slot, &a) == nullptr && slot == &a,
           "exchange_pointer returns old pointer");
    Expect(il2cpp_interlocked_compare_exchange_pointer(&slot, &b, &a) == &a && slot == &b,
           "compare_exchange_pointer match: returns old, replaces");
    Expect(il2cpp_interlocked_compare_exchange_pointer(&slot, &a, &a) == &b && slot == &b,
           "compare_exchange_pointer mismatch: returns old, leaves untouched");

    // --- 5. multi-threaded increment consistency (8 x 100000) ---
    {
        int32_t counter = 0;
        std::vector<std::thread> threads;
        for (int t = 0; t < 8; ++t)
        {
            threads.emplace_back(AddIncrements, &counter, 100000);
        }
        for (auto& th : threads)
        {
            th.join();
        }
        Expect(counter == 800000, "8 threads x 100000 increments == exactly 800000");
    }

    // --- 6. multi-threaded add consistency (8 x 100000 x 2) ---
    {
        int32_t counter = 0;
        std::vector<std::thread> threads;
        for (int t = 0; t < 8; ++t)
        {
            threads.emplace_back(AddValues, &counter, 100000);
        }
        for (auto& th : threads)
        {
            th.join();
        }
        Expect(counter == 1600000, "8 threads x 100000 adds of 2 == exactly 1600000");
    }

    // --- 7. int64 multi-threaded increment consistency ---
    {
        int64_t counter = 0;
        std::vector<std::thread> threads;
        for (int t = 0; t < 8; ++t)
        {
            threads.emplace_back(AddIncrements64, &counter, 100000);
        }
        for (auto& th : threads)
        {
            th.join();
        }
        Expect(counter == 800000, "8 threads x 100000 increments64 == exactly 800000");
    }

    // --- 8. spin lock built from compare_exchange (8 x 100000) ---
    {
        SpinLock lock;
        int32_t counter = 0;
        std::vector<std::thread> threads;
        for (int t = 0; t < 8; ++t)
        {
            threads.emplace_back(AddIncrementsLocked, &lock, &counter, 100000);
        }
        for (auto& th : threads)
        {
            th.join();
        }
        Expect(counter == 800000, "spin-lock-guarded 8 x 100000 increments == exactly 800000");
    }

    if (g_failures == 0)
    {
        std::printf("all runtime interlocked tests passed\n");
        return 0;
    }
    std::printf("%d interlocked test(s) FAILED\n", g_failures);
    return 1;
}
