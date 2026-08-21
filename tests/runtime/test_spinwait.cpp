//------------------------------------------------------------------------------
// test_spinwait.cpp -- icall_threadobj_spin_wait(int32_t) timing contract
// (stubs.cpp -- System.Threading.Thread.SpinWait icall; net48 contract:
// never throws, returns void, O(iterations) busy loop, no side effects).
//
// Coverage (normal + edge + error paths):
//   1. Small N (1000): completes quickly -- wall time < 1 s (a regression
//      to a sleeping/yielding implementation would blow this bound).
//   2. Zero iterations: the loop condition fails on entry -- immediate.
//   3. Negative N (-1, INT32_MIN): terminates immediately -- `i` starts at
//      0 so `i < iterations` is false at once; an unsigned-promotion or
//      signed-wrap bug would turn this into a huge busy loop and hang.
//
// Elapsed times are measured with std::chrono::steady_clock (immune to
// system-time jumps) and printed so regressions show up as numbers.
//
// Plain executable + minimal assertion harness (see test_threadobj.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_object.h" // Il2CppObject (stubs.h signatures)
#include "il2cpp_string.h" // Il2CppString
#include "stubs.h"         // il2cpp::runtime::icall_threadobj_spin_wait

#include <chrono>
#include <cstdint>
#include <cstdio>

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

// Run 'fn' once and report its wall-clock duration in milliseconds.
template <typename Fn> double ElapsedMs(Fn &&fn)
{
    const auto start = std::chrono::steady_clock::now();
    fn();
    const auto stop = std::chrono::steady_clock::now();
    return std::chrono::duration<double, std::milli>(stop - start).count();
}

} // namespace

int main()
{
    using il2cpp::runtime::icall_threadobj_spin_wait;

    // 1. Small N: 1000 pause iterations must stay far below 1 s wall time.
    const double ms1000 = ElapsedMs([] { icall_threadobj_spin_wait(1000); });
    std::printf("spin_wait(1000)     : %9.4f ms\n", ms1000);
    Expect(ms1000 < 1000.0, "spin_wait(1000) completes in < 1 s wall");

    // 2. Zero iterations: loop never entered -- must be effectively instant.
    const double msZero = ElapsedMs([] { icall_threadobj_spin_wait(0); });
    std::printf("spin_wait(0)        : %9.4f ms\n", msZero);
    Expect(msZero < 100.0, "spin_wait(0) returns immediately (< 100 ms)");

    // 3. Negative N: loop condition false on entry -- terminates at once.
    const double msNeg1 = ElapsedMs([] { icall_threadobj_spin_wait(-1); });
    std::printf("spin_wait(-1)       : %9.4f ms\n", msNeg1);
    Expect(msNeg1 < 100.0,
           "spin_wait(-1) terminates immediately (< 100 ms)");

    const double msMin =
        ElapsedMs([] { icall_threadobj_spin_wait(INT32_MIN); });
    std::printf("spin_wait(INT32_MIN): %9.4f ms\n", msMin);
    Expect(msMin < 100.0,
           "spin_wait(INT32_MIN) terminates immediately (< 100 ms)");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
    }
    return g_failures == 0 ? 0 : 1;
}
