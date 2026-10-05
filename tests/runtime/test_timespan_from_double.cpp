//------------------------------------------------------------------------------
// test_timespan_from_double.cpp -- net48 Interval() oracle pins for the
// TimeSpan.FromMinutes/Hours/Days/Seconds/Milliseconds(double) family
// (runtime/src/time.cpp TimeSpanFromDoubleUnits).
//
// Contract (net48, reference source Interval + empirical oracle probes
// tspanprobe/probe2, 2026-09-13 -- 33/33 candidate matches):
//   millis = units * millisPerUnit + (units >= 0 ? +0.5 : -0.5)
//   |millis| > 922337203685477.0            -> OverflowException
//   NaN                                     -> ArgumentException
//   result = (int64)millis * TicksPerMillisecond
// i.e. rounding is half away from zero at MILLISECOND granularity (every
// result is a whole number of milliseconds); this is what makes
// FromSeconds(1.5e-7).Ticks == 0 (0.00015 ms rounds to 0 ms) while the old
// tick-grid truncation produced 1 (dtaudit3 sub_tick mismatch).
//
// Like the other runtime tests: plain executable; a failed Expect() prints
// FAIL and the process exits non-zero.
//------------------------------------------------------------------------------

#include "il2cpp_time.h"

#include "il2cpp_exception.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>

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

template <typename Fn>
bool ThrowsWithType(Fn fn, const char* typeFragment)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeFragment) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // 1. FromSeconds: the dtaudit3 sub-millisecond probe and its neighbours.
    Expect(icall_timespan_from_seconds(0.00000015).ticks == 0,
           "FromSeconds(1.5e-7): 0.00015 ms rounds to 0 (dtaudit3)");
    Expect(icall_timespan_from_seconds(1e-7).ticks == 0,
           "FromSeconds(1e-7): 0.0001 ms rounds to 0");
    Expect(icall_timespan_from_seconds(5e-7).ticks == 0,
           "FromSeconds(5e-7): 0.0005 ms rounds to 0");
    Expect(icall_timespan_from_seconds(4e-7).ticks == 0,
           "FromSeconds(4e-7): 0.0004 ms rounds to 0");
    Expect(icall_timespan_from_seconds(6e-7).ticks == 0,
           "FromSeconds(6e-7): 0.0006 ms rounds to 0");
    Expect(icall_timespan_from_seconds(1.5e-6).ticks == 0,
           "FromSeconds(1.5e-6): 0.0015 ms rounds to 0");
    Expect(icall_timespan_from_seconds(0.0004).ticks == 0,
           "FromSeconds(0.0004): 0.4 ms rounds to 0");
    Expect(icall_timespan_from_seconds(0.0005).ticks == 10000,
           "FromSeconds(0.0005): 0.5 ms rounds to 1 ms == 10000 ticks");
    Expect(icall_timespan_from_seconds(0.0006).ticks == 10000,
           "FromSeconds(0.0006): 0.6 ms rounds to 1 ms");
    Expect(icall_timespan_from_seconds(1234.5678).ticks == 12345680000LL,
           "FromSeconds(1234.5678) == 12345680000 ticks");
    Expect(icall_timespan_from_seconds(-0.00000015).ticks == 0,
           "FromSeconds(-1.5e-7) rounds to 0 (toward zero at 0)");
    Expect(icall_timespan_from_seconds(-0.0006).ticks == -10000,
           "FromSeconds(-0.0006): -0.6 ms rounds to -1 ms (away from zero)");
    Expect(icall_timespan_from_seconds(-1234.5678).ticks == -12345680000LL,
           "FromSeconds(-1234.5678) mirrors positive");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_seconds(1e30); },
                          "System.OverflowException"),
           "FromSeconds(1e30) throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_seconds(922337203685.477); },
                          "System.OverflowException"),
           "FromSeconds(922337203685.477) throws Overflow (millis bound)");

    // 2. FromMilliseconds: millisecond-granularity rounding and bounds.
    Expect(icall_timespan_from_milliseconds(0.5).ticks == 10000,
           "FromMilliseconds(0.5) rounds to 1 ms");
    Expect(icall_timespan_from_milliseconds(1.5).ticks == 20000,
           "FromMilliseconds(1.5) rounds to 2 ms");
    Expect(icall_timespan_from_milliseconds(-0.5).ticks == -10000,
           "FromMilliseconds(-0.5) rounds to -1 ms");
    Expect(icall_timespan_from_milliseconds(0.4999999).ticks == 0,
           "FromMilliseconds(0.4999999) rounds to 0 ms");
    Expect(icall_timespan_from_milliseconds(0.9999999).ticks == 10000,
           "FromMilliseconds(0.9999999) rounds to 1 ms");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_milliseconds(1e30); },
                          "System.OverflowException"),
           "FromMilliseconds(1e30) throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_milliseconds(
                                    std::numeric_limits<double>::quiet_NaN()); },
                          "System.ArgumentException"),
           "FromMilliseconds(NaN) throws Argument");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_milliseconds(922337203685477.0); },
                          "System.OverflowException"),
           "FromMilliseconds(922337203685477) throws Overflow (+0.5 ms shift)");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_milliseconds(
                                    std::numeric_limits<double>::infinity()); },
                          "System.OverflowException"),
           "FromMilliseconds(+inf) throws Overflow");

    // 3. Sub-millisecond rounding across the other units (probe2 pins).
    Expect(icall_timespan_from_minutes(1e-9).ticks == 0,
           "FromMinutes(1e-9): 0.00006 ms rounds to 0");
    Expect(icall_timespan_from_minutes(0.5).ticks == 300000000LL,
           "FromMinutes(0.5) == 300000000 ticks");
    Expect(icall_timespan_from_hours(1e-7).ticks == 0,
           "FromHours(1e-7): 0.36 ms rounds to 0");
    Expect(icall_timespan_from_hours(0.5).ticks == 18000000000LL,
           "FromHours(0.5) == 18000000000 ticks");
    Expect(icall_timespan_from_days(1e-12).ticks == 0,
           "FromDays(1e-12): 0.0000864 ms rounds to 0");
    Expect(icall_timespan_from_days(1.5).ticks == 1296000000000LL,
           "FromDays(1.5) == 1296000000000 ticks");

    // 4. Range edges for the companion units (probe2 pins).
    Expect(icall_timespan_from_minutes(15372286728.0).ticks == 9223372036800000000LL,
           "FromMinutes(15372286728) == 9223372036800000000 ticks (integral ms)");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(15372286729.0); },
                          "System.OverflowException"),
           "FromMinutes(15372286729) throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_hours(1e300); },
                          "System.OverflowException"),
           "FromHours(1e300) throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_days(-1e300); },
                          "System.OverflowException"),
           "FromDays(-1e300) throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(
                                    std::numeric_limits<double>::quiet_NaN()); },
                          "System.ArgumentException"),
           "FromMinutes(NaN) throws Argument");

    if (g_failures == 0)
    {
        std::printf("all runtime timespan_from_double tests passed\n");
    }
    return g_failures == 0 ? 0 : 1;
}
