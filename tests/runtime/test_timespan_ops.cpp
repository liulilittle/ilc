//------------------------------------------------------------------------------
// test_timespan_ops.cpp -- System.TimeSpan creation/arithmetic/parse unit
// tests (icall_timespan_from_hours/days/seconds/milliseconds/ticks_exact,
// from_hms/dhms/dhmsm, add/subtract/negate/compare_to/duration,
// il2cpp_timespan_parse/try_parse).
//
// Companion to test_timespan_min.cpp (which pins FromMinutes exhaustively):
// this file covers the REST of the TimeSpan family with the same
// ticks-exact discipline. Implementation contracts (src/runtime/src/
// time.cpp, timeext2.cpp):
//   * FromMinutes/Hours/Days/Seconds/Milliseconds share the net48
//     Interval() contract: millis = units * scale + (units >= 0 ? +0.5 :
//     -0.5); |millis| > 922337203685477.0 -> OverflowException; NaN ->
//     ArgumentException; result = (int64)millis * TicksPerMillisecond.
//     net48-oracle verified (probe2 / tspanprobe, 2026-09-13).
//   * FromTicks/FromTicksExact are the identity (no validation).
//   * from_hms/dhms/dhmsm compose int64 totalSeconds (TimeToTicks) with a
//     checked total range; out-of-range totals throw
//     ArgumentOutOfRangeException.
//   * Add/Subtract use AddTicksChecked (same-sign-operands-flip-sign test);
//     Subtract reuses it via negation (Int64.MinValue right operand throws
//     OverflowException); Negate/Duration throw OverflowException at
//     Int64.MinValue; CompareTo is -1/0/+1 on ticks.
//   * Parse accepts net48 layouts ("d", "h:mm", "h:mm:ss", "d.h:mm:ss",
//     optional .fffffff fraction, sign, surrounding whitespace); null ->
//     ArgumentNullException, unparseable -> FormatException, numeric
//     overflow -> OverflowException. TryParse mirrors with true/false and
//     never throws (null -> false).
//
// Like the other runtime tests: plain executable, failed Expect() prints
// FAIL and bumps the counter; the process exits non-zero on any failure.
//------------------------------------------------------------------------------

#include "il2cpp_time.h"
#include "timeext2.h"

#include "il2cpp_exception.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

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
// contains 'typeFragment'.
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

il2cpp::Il2CppString* Cs(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

} // namespace

int main()
{
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    using il2cpp::runtime::Il2CppTimeSpan;
    using il2cpp::runtime::icall_timespan_from_ticks;
    using il2cpp::runtime::icall_timespan_from_ticks_exact;
    using il2cpp::runtime::icall_timespan_from_hours;
    using il2cpp::runtime::icall_timespan_from_days;
    using il2cpp::runtime::icall_timespan_from_seconds;
    using il2cpp::runtime::icall_timespan_from_milliseconds;
    using il2cpp::runtime::icall_timespan_from_hms;
    using il2cpp::runtime::icall_timespan_from_dhms;
    using il2cpp::runtime::icall_timespan_from_dhmsm;
    using il2cpp::runtime::icall_timespan_get_ticks;
    using il2cpp::runtime::icall_timespan_get_total_hours;
    using il2cpp::runtime::icall_timespan_get_total_days;
    using il2cpp::runtime::icall_timespan_add;
    using il2cpp::runtime::icall_timespan_subtract;
    using il2cpp::runtime::icall_timespan_negate;
    using il2cpp::runtime::icall_timespan_compare_to;
    using il2cpp::runtime::icall_timespan_duration;
    using il2cpp::runtime::il2cpp_timespan_parse;
    using il2cpp::runtime::il2cpp_timespan_try_parse;

    // --- 1. FromHours/FromDays/FromSeconds/FromMilliseconds exactness ---
    // All products below are exactly representable doubles, so the tick
    // results are exact (no rounding grid involved).
    Expect(icall_timespan_from_hours(1.5).ticks == 54000000000LL,
           "FromHours(1.5) == 54000000000 ticks");
    Expect(icall_timespan_from_hours(0.5).ticks == 18000000000LL,
           "FromHours(0.5) == 18000000000 ticks");
    Expect(icall_timespan_from_hours(-1.5).ticks == -54000000000LL,
           "FromHours(-1.5) mirrors positive");
    Expect(icall_timespan_from_days(1.0).ticks == 864000000000LL,
           "FromDays(1) == 864000000000 ticks");
    Expect(icall_timespan_from_days(0.5).ticks == 432000000000LL,
           "FromDays(0.5) == 432000000000 ticks");
    Expect(icall_timespan_from_days(-1.0).ticks == -864000000000LL,
           "FromDays(-1) mirrors positive");
    Expect(icall_timespan_from_seconds(90.0).ticks == 900000000LL,
           "FromSeconds(90) == 900000000 ticks");
    Expect(icall_timespan_from_seconds(1.5).ticks == 15000000LL,
           "FromSeconds(1.5) == 15000000 ticks");
    Expect(icall_timespan_from_seconds(-90.0).ticks == -900000000LL,
           "FromSeconds(-90) mirrors positive");
    Expect(icall_timespan_from_milliseconds(1500.0).ticks == 15000000LL,
           "FromMilliseconds(1500) == 15000000 ticks");
    Expect(icall_timespan_from_milliseconds(0.5).ticks == 10000LL,
           "FromMilliseconds(0.5) == 10000 ticks (0.5 ms rounds half away "
           "from zero to 1 ms; net48 oracle)");
    Expect(icall_timespan_from_milliseconds(-1500.0).ticks == -15000000LL,
           "FromMilliseconds(-1500) mirrors positive");
    // Cross-family agreement: 1.5h == 90min == 5400s.
    Expect(icall_timespan_from_hours(1.5).ticks == icall_timespan_from_seconds(5400.0).ticks,
           "FromHours(1.5) agrees with FromSeconds(5400)");
    Expect(icall_timespan_get_total_hours(icall_timespan_from_days(1.0)) == 24.0,
           "FromDays(1).TotalHours == 24 exact");
    Expect(icall_timespan_get_total_days(icall_timespan_from_hours(36.0)) == 1.5,
           "FromHours(36).TotalDays == 1.5 exact");
    Expect(icall_timespan_get_ticks(icall_timespan_from_hours(1.5)) == 54000000000LL,
           "get_Ticks round-trips FromHours(1.5)");

    // --- 2. FromHours/FromDays/FromSeconds range gates (net48 Interval) ---
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_hours(1e300); },
                          "System.OverflowException"),
           "FromHours(1e300) throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_days(-1e300); },
                          "System.OverflowException"),
           "FromDays(-1e300) throws Overflow");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_seconds(1e300); },
                          "System.OverflowException"),
           "FromSeconds(1e300) throws Overflow");

    // --- 3. FromTicks / FromTicksExact identity (no validation) ---
    Expect(icall_timespan_from_ticks(0).ticks == 0, "FromTicks(0) == 0");
    Expect(icall_timespan_from_ticks(INT64_MAX).ticks == INT64_MAX,
           "FromTicks(Max) identity");
    Expect(icall_timespan_from_ticks(INT64_MIN).ticks == INT64_MIN,
           "FromTicks(Min) identity (no range gate)");
    Expect(icall_timespan_from_ticks_exact(123456789012345LL).ticks == 123456789012345LL,
           "FromTicksExact passes the value through");
    Expect(icall_timespan_from_ticks_exact(INT64_MIN).ticks == INT64_MIN,
           "FromTicksExact(Min) identity");

    // --- 4. hms / dhms / dhmsm constructors (TimeToTicks composition) ---
    Expect(icall_timespan_from_hms(1, 30, 0).ticks == 54000000000LL,
           "TimeSpan(1,30,0) == 54000000000 ticks");
    Expect(icall_timespan_from_hms(0, 0, 90).ticks == 900000000LL,
           "TimeSpan(0,0,90) == 900000000 ticks");
    Expect(icall_timespan_from_hms(-1, -30, 0).ticks == -54000000000LL,
           "TimeSpan(-1,-30,0) mirrors positive");
    Expect(icall_timespan_from_dhms(1, 2, 30, 15).ticks == 954150000000LL,
           "TimeSpan(1,2,30,15) == 954150000000 ticks");
    Expect(icall_timespan_from_dhms(-1, -2, -30, -15).ticks == -954150000000LL,
           "TimeSpan(-1,-2,-30,-15) mirrors positive");
    Expect(icall_timespan_from_dhmsm(0, 0, 0, 1, 500).ticks == 15000000LL,
           "TimeSpan(0,0,0,1,500) == 15000000 ticks");
    Expect(icall_timespan_from_dhmsm(1, 2, 30, 15, 250).ticks == 954152500000LL,
           "TimeSpan(1,2,30,15,250) == 954152500000 ticks");
    Expect(icall_timespan_from_dhmsm(0, 0, 0, 0, 0).ticks == 0,
           "TimeSpan(0,0,0,0,0) == 0");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_hms(INT32_MAX, 0, 0); },
                          "System.ArgumentOutOfRangeException"),
           "TimeSpan(Int32.Max,0,0) overflows the total-seconds range");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_dhms(INT32_MAX, 0, 0, 0); },
                          "System.ArgumentOutOfRangeException"),
           "TimeSpan(Int32.Max,0,0,0) overflows the total-seconds range");
    Expect(ThrowsWithType([&] { (void)icall_timespan_from_dhmsm(INT32_MAX, 0, 0, 0, 0); },
                          "System.ArgumentOutOfRangeException"),
           "TimeSpan(Int32.Max,0,0,0,0) overflows the total-seconds range");

    // --- 5. Add / Subtract / Negate / CompareTo / Duration ---
    const Il2CppTimeSpan h90 = icall_timespan_from_ticks(54000000000LL);
    const Il2CppTimeSpan h60 = icall_timespan_from_ticks(36000000000LL);
    Expect(icall_timespan_add(h90, h60).ticks == 90000000000LL,
           "Add(90h-ticks, 60h-ticks) == 150h-ticks");
    Expect(icall_timespan_add(h90, icall_timespan_negate(h90)).ticks == 0,
           "x + (-x) == 0");
    Expect(ThrowsWithType([&] {
                (void)icall_timespan_add(icall_timespan_from_ticks(INT64_MAX),
                                         icall_timespan_from_ticks(1));
            }, "System.OverflowException"),
           "Add(Max, 1) throws OverflowException");
    Expect(ThrowsWithType([&] {
                (void)icall_timespan_add(icall_timespan_from_ticks(INT64_MIN),
                                         icall_timespan_from_ticks(-1));
            }, "System.OverflowException"),
           "Add(Min, -1) throws OverflowException");
    Expect(icall_timespan_subtract(h90, h60).ticks == 18000000000LL,
           "Subtract(90h, 60h) == 30h-ticks");
    Expect(ThrowsWithType([&] {
                (void)icall_timespan_subtract(icall_timespan_from_ticks(0),
                                              icall_timespan_from_ticks(INT64_MIN));
            }, "System.OverflowException"),
           "Subtract(0, Min) throws OverflowException (unrepresentable negation)");
    Expect(ThrowsWithType([&] {
                (void)icall_timespan_subtract(icall_timespan_from_ticks(INT64_MAX),
                                              icall_timespan_from_ticks(-1));
            }, "System.OverflowException"),
           "Subtract(Max, -1) throws OverflowException");
    Expect(icall_timespan_negate(h90).ticks == -54000000000LL,
           "Negate(90h) == -90h-ticks");
    Expect(icall_timespan_negate(icall_timespan_negate(h90)).ticks == h90.ticks,
           "double negation round-trips");
    Expect(ThrowsWithType([&] {
                (void)icall_timespan_negate(icall_timespan_from_ticks(INT64_MIN));
            }, "System.OverflowException"),
           "Negate(Min) throws OverflowException");
    Expect(icall_timespan_compare_to(h60, h90) == -1, "Compare(60h, 90h) == -1");
    Expect(icall_timespan_compare_to(h90, h60) == 1, "Compare(90h, 60h) == 1");
    Expect(icall_timespan_compare_to(h90, h90) == 0, "Compare(90h, 90h) == 0");
    Expect(icall_timespan_duration(icall_timespan_negate(h90)).ticks == h90.ticks,
           "Duration(-90h) == 90h-ticks");
    Expect(icall_timespan_duration(h90).ticks == h90.ticks,
           "Duration(90h) is identity");
    Expect(ThrowsWithType([&] {
                (void)icall_timespan_duration(icall_timespan_from_ticks(INT64_MIN));
            }, "System.OverflowException"),
           "Duration(Min) throws OverflowException");

    // --- 6. Parse / TryParse (net48 layouts) ---
    Expect(il2cpp_timespan_parse(Cs("1.02:03:04")).ticks == 937840000000LL,
           "Parse 1.02:03:04 == 937840000000 ticks");
    Expect(il2cpp_timespan_parse(Cs("02:03:04")).ticks == 73840000000LL,
           "Parse 02:03:04 == 73840000000 ticks");
    Expect(il2cpp_timespan_parse(Cs("02:03")).ticks == 73800000000LL,
           "Parse 02:03 (hh:mm) == 73800000000 ticks");
    Expect(il2cpp_timespan_parse(Cs("6")).ticks == 5184000000000LL,
           "Parse 6 (days-only) == 5184000000000 ticks");
    Expect(il2cpp_timespan_parse(Cs("-01:00:00")).ticks == -36000000000LL,
           "Parse -01:00:00 negates the total");
    Expect(il2cpp_timespan_parse(Cs("00:00:01.5")).ticks == 15000000LL,
           "Parse 00:00:01.5 fraction == 15000000 ticks");
    Expect(il2cpp_timespan_parse(Cs("00:00:01.1234567")).ticks == 11234567LL,
           "Parse 7-digit fraction is tick-exact");
    Expect(il2cpp_timespan_parse(Cs(" 01:00:00 ")).ticks == 36000000000LL,
           "Parse tolerates surrounding whitespace");
    {
        Il2CppTimeSpan parsed = icall_timespan_from_ticks(0);
        Expect(il2cpp_timespan_try_parse(Cs("1.02:03:04"), &parsed)
                   && parsed.ticks == 937840000000LL,
               "TryParse 1.02:03:04 succeeds with exact ticks");
        Expect(!il2cpp_timespan_try_parse(Cs("garbage"), &parsed),
               "TryParse garbage returns false");
        Expect(!il2cpp_timespan_try_parse(nullptr, &parsed),
               "TryParse null returns false (no throw)");
        Expect(!il2cpp_timespan_try_parse(Cs("00:00:01.12345678"), &parsed),
               "TryParse 8-digit fraction returns false");
        Expect(!il2cpp_timespan_try_parse(Cs("99999999999:00:00"), &parsed),
               "TryParse overflowing hours returns false");
        Expect(il2cpp_timespan_try_parse(Cs("00:00:01"), nullptr),
               "TryParse with null out-param still returns true");
    }
    Expect(ThrowsWithType([&] { (void)il2cpp_timespan_parse(nullptr); },
                          "System.ArgumentNullException"),
           "Parse null throws ArgumentNullException");
    Expect(ThrowsWithType([&] { (void)il2cpp_timespan_parse(Cs("garbage")); },
                          "System.FormatException"),
           "Parse garbage throws FormatException");
    Expect(ThrowsWithType([&] { (void)il2cpp_timespan_parse(Cs("00:00:01.12345678")); },
                          "System.FormatException"),
           "Parse 8-digit fraction throws FormatException");
    Expect(ThrowsWithType([&] { (void)il2cpp_timespan_parse(Cs("99999999999:00:00")); },
                          "System.OverflowException"),
           "Parse overflowing hours throws OverflowException");

    if (g_failures == 0)
    {
        std::printf("all runtime timespan_ops tests passed\n");
    }
    return g_failures == 0 ? 0 : 1;
}
