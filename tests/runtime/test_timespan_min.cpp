//------------------------------------------------------------------------------
// test_timespan_min.cpp -- System.TimeSpan.FromMinutes(double) unit tests
// (icall_timespan_from_minutes, il2cpp_time.h).
//
// Implementation contract (src/runtime/src/time.cpp), net48 Interval():
// millis = minutes * 60000.0 + (minutes >= 0 ? +0.5 : -0.5); |millis| >
// 922337203685477.0 throws OverflowException; otherwise the result is
// (int64)millis * TicksPerMillisecond -- rounding half away from zero at
// MILLISECOND granularity (every result is a whole number of milliseconds).
// NaN throws ArgumentException. Every expectation below is net48-oracle
// verified (tspanprobe/probe2, 2026-09-13).
//
// Coverage:
//   1. Exactness via ticks math: FromMinutes(90) == 90 * 600000000 ==
//      54000000000 ticks and TotalHours == 1.5 bit-exact (54e9/36e9 is an
//      exactly representable IEEE quotient); TotalMinutes/Seconds/
//      Milliseconds/Days pins; agreement with TimeSpan(long ticks).
//   2. Whole- and half-minute grid: 0, 1, 60, 1440, 0.5, 1.5, 90.5, 2.5,
//      1.25 -- every x.5-minute value lands on an integral tick count.
//   3. Sub-millisecond inputs (< 0.0005 ms in magnitude) all round to 0 ms
//      == 0 ticks: 1e-9/2e-9/3e-9/4e-10 and their negatives; negative zero
//      stays +0; minute fractions that are exact seconds/milliseconds land
//      on their tick values.
//   4. Negative values: -90/-1440 exact ticks, TotalHours == -1.5 bit-exact,
//      accessor decomposition of -90 min (Days=0, Hours=-1, Minutes=-30 --
//      .NET's sign-on-components convention, truncation toward zero).
//   5. Range boundary: +/-15372286728 min == +/-9223372036800000000 ticks
//      accepted; one more minute overflows -> OverflowException.
//   6. Error paths: NaN -> ArgumentException; +/-infinity and huge finite
//      magnitudes -> OverflowException (the net48 Interval contract).
//
// Like the other runtime tests: plain executable, failed Expect() prints
// FAIL and bumps the counter; the process exits non-zero on any failure.
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

// Runs fn; returns true iff it throws a DotNetException whose type chain
// contains 'typeFragment' (e.g. "System.ArgumentOutOfRangeException").
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

// TicksPerMinute: the icall's scale factor (time.cpp kTicksPerMinute).
constexpr int64_t kTicksPerMinute = 600000000;

} // namespace

int main()
{
    // Unbuffered stdout: an aborted process (uncaught managed exception)
    // must still show the FAIL lines that led to it.
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    using il2cpp::runtime::Il2CppTimeSpan;
    using il2cpp::runtime::icall_timespan_from_minutes;
    using il2cpp::runtime::icall_timespan_from_ticks;
    using il2cpp::runtime::icall_timespan_get_days;
    using il2cpp::runtime::icall_timespan_get_hours;
    using il2cpp::runtime::icall_timespan_get_minutes;
    using il2cpp::runtime::icall_timespan_get_seconds;
    using il2cpp::runtime::icall_timespan_get_total_days;
    using il2cpp::runtime::icall_timespan_get_total_hours;
    using il2cpp::runtime::icall_timespan_get_total_milliseconds;
    using il2cpp::runtime::icall_timespan_get_total_minutes;
    using il2cpp::runtime::icall_timespan_get_total_seconds;

    // 1. Exactness via ticks math: FromMinutes(90) == 54000000000 ticks and
    //    TotalHours == 1.5 bit-exact (both operands and the quotient are
    //    exactly representable doubles, so == is the correct comparison).
    const Il2CppTimeSpan m90 = icall_timespan_from_minutes(90.0);
    Expect(m90.ticks == 90 * kTicksPerMinute && m90.ticks == 54000000000LL,
           "FromMinutes(90) == 54000000000 ticks");
    Expect(icall_timespan_get_total_hours(m90) == 1.5,
           "FromMinutes(90).TotalHours == 1.5 exact");
    Expect(icall_timespan_get_total_minutes(m90) == 90.0,
           "FromMinutes(90).TotalMinutes == 90 exact");
    Expect(icall_timespan_get_total_seconds(m90) == 5400.0,
           "FromMinutes(90).TotalSeconds == 5400 exact");
    Expect(icall_timespan_get_total_milliseconds(m90) == 5400000.0,
           "FromMinutes(90).TotalMilliseconds == 5400000 exact");
    Expect(icall_timespan_get_total_days(m90) == 0.0625,
           "FromMinutes(90).TotalDays == 0.0625 exact (54e9/864e9)");
    Expect(icall_timespan_from_ticks(54000000000LL).ticks == m90.ticks,
           "FromMinutes(90) agrees with TimeSpan(ticks)");

    // 2. Whole- and half-minute grid: x.5-minute inputs always produce an
    //    integral tick product (0.5 min == 300000000 ticks), no rounding.
    Expect(icall_timespan_from_minutes(0.0).ticks == 0, "FromMinutes(0) == 0");
    Expect(icall_timespan_from_minutes(1.0).ticks == kTicksPerMinute,
           "FromMinutes(1) == TicksPerMinute");
    Expect(icall_timespan_from_minutes(60.0).ticks == 36000000000LL,
           "FromMinutes(60) == TicksPerHour");
    Expect(icall_timespan_from_minutes(1440.0).ticks == 864000000000LL,
           "FromMinutes(1440) == TicksPerDay");
    Expect(icall_timespan_from_minutes(0.5).ticks == 300000000LL,
           "FromMinutes(0.5) == 300000000 ticks");
    Expect(icall_timespan_from_minutes(1.5).ticks == 900000000LL,
           "FromMinutes(1.5) == 900000000 ticks");
    Expect(icall_timespan_from_minutes(90.5).ticks == 54300000000LL,
           "FromMinutes(90.5) == 54300000000 ticks");
    Expect(icall_timespan_from_minutes(2.5).ticks == 1500000000LL,
           "FromMinutes(2.5) == 1500000000 ticks");
    Expect(icall_timespan_from_minutes(1.25).ticks == 750000000LL,
           "FromMinutes(1.25) == 750000000 ticks");

    // 3. Sub-millisecond rounding: net48 Interval() rounds to the nearest
    //    MILLISECOND (half away from zero); every input below is smaller
    //    than 0.0005 ms in magnitude, so all of them truncate to 0 ms ==
    //    0 ticks. net48-oracle verified (probe2, 2026-09-13):
    //    FromMinutes(1e-9/2e-9/3e-9/4e-10/-1e-9/-3e-9/-4e-10) == 0.
    Expect(icall_timespan_from_minutes(1e-9).ticks == 0,
           "FromMinutes(1e-9): 0.00006 ms rounds to 0");
    Expect(icall_timespan_from_minutes(2e-9).ticks == 0,
           "FromMinutes(2e-9): 0.00012 ms rounds to 0");
    Expect(icall_timespan_from_minutes(3e-9).ticks == 0,
           "FromMinutes(3e-9): 0.00018 ms rounds to 0");
    Expect(icall_timespan_from_minutes(4e-10).ticks == 0,
           "FromMinutes(4e-10): 0.000024 ms rounds to 0");
    Expect(icall_timespan_from_minutes(-1e-9).ticks == 0,
           "FromMinutes(-1e-9): -0.00006 ms rounds to 0");
    Expect(icall_timespan_from_minutes(-3e-9).ticks == 0,
           "FromMinutes(-3e-9): -0.00018 ms rounds to 0");
    Expect(icall_timespan_from_minutes(-4e-10).ticks == 0,
           "FromMinutes(-4e-10): -0.000024 ms rounds to 0");
    Expect(icall_timespan_from_minutes(-0.0).ticks == 0,
           "FromMinutes(-0.0) == 0 (negative zero, not -1)");
    // Minute fractions that ARE exact seconds/milliseconds land on them.
    Expect(icall_timespan_from_minutes(1.0 / 60.0).ticks == 10000000LL,
           "FromMinutes(1/60) == 1 second == 10000000 ticks");
    Expect(icall_timespan_from_minutes(1.0 / 600.0).ticks == 1000000LL,
           "FromMinutes(1/600) == 0.1 second == 1000000 ticks");
    Expect(icall_timespan_from_minutes(1.0 / 60000.0).ticks == 10000LL,
           "FromMinutes(1/60000) == 1 millisecond == 10000 ticks");

    // 4. Negative values: symmetric ticks, exact TotalHours, and .NET's
    //    sign-on-components decomposition (integer division truncates toward
    //    zero, so -90 min is Days=0, Hours=-1, Minutes=-30, Seconds=0).
    const Il2CppTimeSpan nm90 = icall_timespan_from_minutes(-90.0);
    Expect(nm90.ticks == -54000000000LL, "FromMinutes(-90) == -54000000000 ticks");
    Expect(icall_timespan_get_total_hours(nm90) == -1.5,
           "FromMinutes(-90).TotalHours == -1.5 exact");
    Expect(nm90.ticks == -m90.ticks, "FromMinutes(-90) mirrors FromMinutes(90)");
    Expect(icall_timespan_from_minutes(-1440.0).ticks == -864000000000LL,
           "FromMinutes(-1440) == -TicksPerDay");
    Expect(icall_timespan_get_days(nm90) == 0 && icall_timespan_get_hours(nm90) == -1
               && icall_timespan_get_minutes(nm90) == -30
               && icall_timespan_get_seconds(nm90) == 0,
           "-90 min decomposes as 0d -1h -30m 0s (truncation toward zero)");

    // 5. Range boundary: the validated bound is +/-9223372036850000000 ticks
    //    (Int64 limits / TicksPerSecond, rounded into double); the largest
    //    minute-grid double inside it is +/-15372286728 min, one more minute
    //    overflows.
    Expect(icall_timespan_from_minutes(15372286728.0).ticks == 9223372036800000000LL,
           "FromMinutes(15372286728) == 9223372036800000000 ticks (near max)");
    Expect(icall_timespan_from_minutes(-15372286728.0).ticks == -9223372036800000000LL,
           "FromMinutes(-15372286728) == -9223372036800000000 ticks (near min)");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(15372286729.0); },
                          "System.OverflowException"),
           "one minute past max throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(-15372286729.0); },
                          "System.OverflowException"),
           "one minute past min throws Overflow");

    // 6. Error paths (net48 Interval contract, oracle-verified): NaN
    //    throws ArgumentException; +/-infinity and every huge finite
    //    magnitude throw OverflowException.
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(
                                    std::numeric_limits<double>::quiet_NaN()); },
                          "System.ArgumentException"),
           "NaN throws Argument");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(
                                    std::numeric_limits<double>::infinity()); },
                          "System.OverflowException"),
           "+infinity throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(
                                    -std::numeric_limits<double>::infinity()); },
                          "System.OverflowException"),
           "-infinity throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(1e12); },
                          "System.OverflowException"),
           "1e12 minutes overflows");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(-1e12); },
                          "System.OverflowException"),
           "-1e12 minutes overflows");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(1e300); },
                          "System.OverflowException"),
           "1e300 minutes overflows");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_minutes(
                                    std::numeric_limits<double>::max()); },
                          "System.OverflowException"),
           "DBL_MAX minutes overflows");

    if (g_failures == 0)
    {
        std::printf("all runtime timespan_min tests passed\n");
    }
    return g_failures == 0 ? 0 : 1;
}
