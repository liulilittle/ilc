//------------------------------------------------------------------------------
// test_time.cpp -- System.DateTime / System.TimeSpan runtime unit tests.
//
// Coverage (normal + edge + error paths):
//   1. Known date -> tick constants (pins the civil-calendar math):
//      0001-01-01 = 0, 1970-01-01 = 621355968000000000,
//      2000-01-01 = 630822816000000000, 2020-06-15 = 637277760000000000,
//      9999-12-31 23:59:59 = 3155378975999999999 (DateTime.MaxValue).
//   2. Component round-trip: from_ymdhms -> Year..Second accessors.
//   3. DayOfWeek known values (Sunday = 0): 0001-01-01 Monday (=1),
//      2000-01-01 Saturday (=6), 2020-06-15 Monday (=1), 2020-06-14 Sunday
//      (=0), 9999-12-31 Friday (=5).
//   4. Clocks: consecutive UtcNow calls differ < 5 s; Now is a whole-minute
//      multiple of UtcNow within +/-15 h (any real timezone); Now matches the
//      OS local wall clock within 5 s (independent loop-based day count);
//      Now's year is in [2020, 2100].
//   5. AddDays/AddHours/AddMinutes carry: 2020-02-28 +1 -> 02/29 (leap),
//      +2 -> 03/01, -1 from 03/01 -> 02/29, 2019-12-31 +1 -> 2020-01-01
//      (year carry), 23:30 +1.5 h -> next day 01:00, +45 min, +1.5 days.
//   6. Subtract(DateTime) -> TimeSpan (signed), CompareTo -1/0/+1.
//   7. ToString: fixed "MM/dd/yyyy HH:mm:ss" (zero-padded), GC survival.
//   8. TimeSpan: from_ticks/from_hms, Ticks/Days/Hours/Minutes/Seconds/
//      TotalSeconds (incl. negative truncation toward zero), hours > 23
//      allowed, Add/Subtract/Negate/CompareTo, overflow error paths.
//   9. Validation: every out-of-range DateTime component throws
//      ArgumentOutOfRangeException; TimeSpan overflow throws
//      OverflowException.
//  10. Extended accessors: get_Ticks (raw 64-bit count round-trip),
//      get_Millisecond (0..999 incl. a fractional construction via direct
//      tick addition), get_DayOfYear (1..366 leap-aware).
//  11. Static date helpers: DaysInMonth (leap/century/400-year rule +
//      month/year range validation) and IsLeapYear (Gregorian rule + year
//      range validation).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_time.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <limits>

namespace {

static_assert(sizeof(void*) == 8, "time test fixture assumes 8-byte pointers");

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

// Civil components of a DateTime (the value struct's payload is opaque).
struct Parts
{
    int32_t year;
    int32_t month;
    int32_t day;
    int32_t hour;
    int32_t minute;
    int32_t second;
};

bool operator==(const Parts& a, const Parts& b)
{
    return a.year == b.year && a.month == b.month && a.day == b.day
        && a.hour == b.hour && a.minute == b.minute && a.second == b.second;
}

Parts GetParts(il2cpp::runtime::Il2CppDateTime dt)
{
    Parts p;
    p.year = il2cpp::runtime::icall_datetime_get_year(dt);
    p.month = il2cpp::runtime::icall_datetime_get_month(dt);
    p.day = il2cpp::runtime::icall_datetime_get_day(dt);
    p.hour = il2cpp::runtime::icall_datetime_get_hour(dt);
    p.minute = il2cpp::runtime::icall_datetime_get_minute(dt);
    p.second = il2cpp::runtime::icall_datetime_get_second(dt);
    return p;
}

// Independent civil-day count from 0001-01-01 (loop style -- a distinct code
// path from the runtime's Hinnant arithmetic, so agreement catches
// transcription errors in the implementation).
int64_t DaysSinceYear1(int32_t year, int32_t month, int32_t day)
{
    int64_t days = 0;
    for (int32_t y = 1; y < year; ++y)
    {
        days += 365;
        if (y % 4 == 0 && (y % 100 != 0 || y % 400 == 0)) ++days;
    }
    static const int32_t kDaysInMonth[12] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
    for (int32_t m = 1; m < month; ++m)
    {
        days += kDaysInMonth[m - 1];
        if (m == 2 && year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) ++days;
    }
    return days + day - 1;
}

// True iff the managed string's payload equals the ASCII literal.
bool StringEquals(const il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr) return false;
    const int32_t len = static_cast<int32_t>(std::strlen(ascii));
    if (s->length != len) return false;
    for (int32_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i]))) return false;
    }
    return true;
}

} // namespace

int main()
{
    // --- 1. Known date -> tick constants ---
    Expect(il2cpp::runtime::icall_datetime_from_ymd(1, 1, 1).ticks == 0,
           "0001-01-01 is the tick epoch (ticks == 0)");
    Expect(il2cpp::runtime::icall_datetime_from_ymd(1970, 1, 1).ticks == 621355968000000000LL,
           "1970-01-01 ticks == 621355968000000000 (Unix epoch offset)");
    Expect(il2cpp::runtime::icall_datetime_from_ymd(2000, 1, 1).ticks == 630822816000000000LL,
           "2000-01-01 ticks == 630822816000000000");
    Expect(il2cpp::runtime::icall_datetime_from_ymd(2020, 6, 15).ticks == 637277760000000000LL,
           "2020-06-15 ticks == 637277760000000000");
    Expect(il2cpp::runtime::icall_datetime_from_ymdhms(9999, 12, 31, 23, 59, 59).ticks == 3155378975990000000LL,
           "9999-12-31 23:59:59 ticks == 3155378975990000000 (MaxValue minus the last 0.9999999 s)");

    // --- 2. Component round-trip ---
    const il2cpp::runtime::Il2CppDateTime dt = il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 12, 30, 45);
    Expect(GetParts(dt) == Parts{2020, 6, 15, 12, 30, 45},
           "from_ymdhms -> Year/Month/Day/Hour/Minute/Second round-trip");

    // --- 3. DayOfWeek known values (Sunday = 0) ---
    Expect(il2cpp::runtime::icall_datetime_get_dayofweek(il2cpp::runtime::icall_datetime_from_ymd(1, 1, 1)) == 1,
           "0001-01-01 is a Monday (DayOfWeek == 1)");
    Expect(il2cpp::runtime::icall_datetime_get_dayofweek(il2cpp::runtime::icall_datetime_from_ymd(2000, 1, 1)) == 6,
           "2000-01-01 is a Saturday (DayOfWeek == 6)");
    Expect(il2cpp::runtime::icall_datetime_get_dayofweek(il2cpp::runtime::icall_datetime_from_ymd(2020, 6, 15)) == 1,
           "2020-06-15 is a Monday (DayOfWeek == 1)");
    Expect(il2cpp::runtime::icall_datetime_get_dayofweek(il2cpp::runtime::icall_datetime_from_ymd(2020, 6, 14)) == 0,
           "2020-06-14 is a Sunday (DayOfWeek == 0)");
    Expect(il2cpp::runtime::icall_datetime_get_dayofweek(il2cpp::runtime::icall_datetime_from_ymd(9999, 12, 31)) == 5,
           "9999-12-31 is a Friday (DayOfWeek == 5)");

    // --- 4. Clocks ---
    const il2cpp::runtime::Il2CppDateTime a = il2cpp::runtime::icall_datetime_utc_now();
    const il2cpp::runtime::Il2CppDateTime b = il2cpp::runtime::icall_datetime_utc_now();
    Expect(std::llabs(a.ticks - b.ticks) < 50000000LL,
           "consecutive UtcNow calls differ by less than 5 s");
    const il2cpp::runtime::Il2CppDateTime now = il2cpp::runtime::icall_datetime_now();
    // Now = UtcNow + whole-minute offset; the two clock calls are NOT atomic
    // together, so the observed difference is offset + (sub-2 s elapsed) --
    // assert the remainder is within 2 s of a whole minute.
    const int64_t offset = now.ticks - a.ticks;
    const int64_t minuteRemainder = offset % 600000000LL;
    Expect(minuteRemainder <= 20000000LL || minuteRemainder >= 580000000LL,
           "Now - UtcNow is a whole number of minutes (within 2 s call gap)");
    Expect(offset <= 54000000000000LL && offset >= -54000000000000LL,
           "Now - UtcNow is within +/-15 h (real-world timezone bounds)");
    const int32_t nowYear = il2cpp::runtime::icall_datetime_get_year(now);
    Expect(nowYear >= 2020 && nowYear <= 2100, "Now's year is in the plausible range [2020, 2100]");
    // Now must match the OS local wall clock within 5 s (independent path:
    // <ctime> localtime + the loop-based day count above).
    const std::time_t nowT = std::time(nullptr);
    std::tm localTm = {};
#if defined(_WIN32)
    const bool gotLocal = _localtime64_s(&localTm, &nowT) == 0;
#else
    const bool gotLocal = localtime_r(&nowT, &localTm) != nullptr;
#endif
    if (gotLocal)
    {
        const int64_t wallTicks = DaysSinceYear1(localTm.tm_year + 1900, localTm.tm_mon + 1, localTm.tm_mday) * 864000000000LL
                                + localTm.tm_hour * 36000000000LL
                                + localTm.tm_min * 600000000LL
                                + localTm.tm_sec * 10000000LL;
        Expect(std::llabs(now.ticks - wallTicks) < 50000000LL,
               "Now matches the OS local wall clock within 5 s");
    }
    else
    {
        Expect(false, "localtime conversion failed in the test harness");
    }

    // --- 5. AddDays/AddHours/AddMinutes carry ---
    const il2cpp::runtime::Il2CppDateTime feb28 = il2cpp::runtime::icall_datetime_from_ymd(2020, 2, 28);
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_days(feb28, 1.0)) == Parts{2020, 2, 29, 0, 0, 0},
           "2020-02-28 + 1 day -> 2020-02-29 (leap year carry)");
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_days(feb28, 2.0)) == Parts{2020, 3, 1, 0, 0, 0},
           "2020-02-28 + 2 days -> 2020-03-01 (month carry)");
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_days(il2cpp::runtime::icall_datetime_from_ymd(2020, 3, 1), -1.0)) == Parts{2020, 2, 29, 0, 0, 0},
           "2020-03-01 - 1 day -> 2020-02-29 (negative carry)");
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_days(il2cpp::runtime::icall_datetime_from_ymd(2019, 12, 31), 1.0)) == Parts{2020, 1, 1, 0, 0, 0},
           "2019-12-31 + 1 day -> 2020-01-01 (year carry)");
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_hours(
               il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 23, 30, 0), 1.5)) == Parts{2020, 6, 16, 1, 0, 0},
           "23:30 + 1.5 h -> next day 01:00 (day carry through hours)");
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_minutes(
               il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 12, 30, 45), 45.0)) == Parts{2020, 6, 15, 13, 15, 45},
           "12:30:45 + 45 min -> 13:15:45 (hour carry through minutes)");
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_days(
               il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 0, 0, 0), 1.5)) == Parts{2020, 6, 16, 12, 0, 0},
           "midnight + 1.5 days -> next day noon (fractional days)");

    // net48 oracle AddDays cross-year / leap-boundary cases (datetime-
    // conformance.md AD6/AD7): +365 days from a Feb-28 crosses the year
    // boundary differently for leap vs common start years.
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_days(
               il2cpp::runtime::icall_datetime_from_ymd(2024, 2, 28), 365.0)) == Parts{2025, 2, 27, 0, 0, 0},
           "2024-02-28 + 365 days -> 2025-02-27 (net48 AD6)");
    Expect(GetParts(il2cpp::runtime::icall_datetime_add_days(
               il2cpp::runtime::icall_datetime_from_ymd(2023, 2, 28), 365.0)) == Parts{2024, 2, 28, 0, 0, 0},
           "2023-02-28 + 365 days -> 2024-02-28 (net48 AD7)");

    // net48 DateTime.AddTicks bounds the RESULT to [MinValue, MaxValue]
    // (ArgumentOutOfRangeException): the int64-overflow guard alone is not
    // enough -- MaxValue.AddDays(1) and MinValue.AddDays(-1) must throw.
    const il2cpp::runtime::Il2CppDateTime maxDt(3155378975999999999LL);
    const il2cpp::runtime::Il2CppDateTime minDt(0);
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_days(maxDt, 1.0); },
                          "System.ArgumentOutOfRangeException"),
           "MaxValue + 1 day throws ArgumentOutOfRangeException (net48)");
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_days(minDt, -1.0); },
                          "System.ArgumentOutOfRangeException"),
           "MinValue - 1 day throws ArgumentOutOfRangeException (net48)");
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_days(maxDt, 0.0); },
                          "System.ArgumentOutOfRangeException") == false,
           "MaxValue + 0 days keeps MaxValue (no throw)");
    // NaN / +/-Infinity: net48 DateTime.Add(value, scale) -> millis bound
    // check rejects them (ArgumentOutOfRangeException).
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_days(dt, std::nan("")); },
                          "System.ArgumentOutOfRangeException"),
           "AddDays(NaN) throws ArgumentOutOfRangeException (net48)");
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_days(dt, std::numeric_limits<double>::infinity()); },
                          "System.ArgumentOutOfRangeException"),
           "AddDays(+Inf) throws ArgumentOutOfRangeException (net48)");
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_days(dt, -std::numeric_limits<double>::infinity()); },
                          "System.ArgumentOutOfRangeException"),
           "AddDays(-Inf) throws ArgumentOutOfRangeException (net48)");
    // The AddHours/AddMinutes result bound (same AddTicks path).
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_hours(maxDt, 24.0); },
                          "System.ArgumentOutOfRangeException"),
           "MaxValue + 24 h throws ArgumentOutOfRangeException (net48)");
    Expect(ThrowsWithType([&] { il2cpp::runtime::icall_datetime_add_minutes(minDt, -1.0); },
                          "System.ArgumentOutOfRangeException"),
           "MinValue - 1 min throws ArgumentOutOfRangeException (net48)");
    // net48 AddDays fractional-rounding probes (probe3): 8.64 ms -> 9 ms
    // (half-away-from-zero), 3.6 ms -> 4 ms (AddHours), 0.06 ms -> 0 ms
    // (AddMinutes, truncation of a sub-0.5 value).
    Expect(il2cpp::runtime::icall_datetime_get_millisecond(
               il2cpp::runtime::icall_datetime_add_days(dt, 0.0000001)) == 9,
           "AddDays(0.0000001 d = 8.64 ms) rounds to 9 ms (net48 probe3 FR1)");
    Expect(il2cpp::runtime::icall_datetime_get_millisecond(
               il2cpp::runtime::icall_datetime_add_hours(dt, 0.000001)) == 4,
           "AddHours(0.000001 h = 3.6 ms) rounds to 4 ms (net48 probe3 FR4)");
    Expect(il2cpp::runtime::icall_datetime_get_millisecond(
               il2cpp::runtime::icall_datetime_add_minutes(dt, 0.000001)) == 0,
           "AddMinutes(0.000001 min = 0.06 ms) truncates to 0 ms (net48 probe3 FR5)");

    // --- 6. Subtract(DateTime) -> TimeSpan, CompareTo ---
    const il2cpp::runtime::Il2CppDateTime noon = il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 12, 0, 0);
    const il2cpp::runtime::Il2CppDateTime halfPast = il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 11, 30, 0);
    Expect(il2cpp::runtime::icall_datetime_subtract(noon, halfPast).ticks == 18000000000LL,
           "12:00 - 11:30 == 30 min (18000000000 ticks)");
    Expect(il2cpp::runtime::icall_datetime_subtract(halfPast, noon).ticks == -18000000000LL,
           "11:30 - 12:00 == -30 min (signed TimeSpan)");
    Expect(il2cpp::runtime::icall_datetime_compare_to(noon, noon) == 0,
           "CompareTo equal dates -> 0");
    Expect(il2cpp::runtime::icall_datetime_compare_to(halfPast, noon) == -1,
           "CompareTo earlier date -> -1");
    Expect(il2cpp::runtime::icall_datetime_compare_to(noon, halfPast) == 1,
           "CompareTo later date -> +1");

    // --- 7. ToString: fixed "MM/dd/yyyy HH:mm:ss", zero-padded, GC-safe ---
    il2cpp::Il2CppObject* strObj = il2cpp::runtime::icall_datetime_to_string(dt);
    il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(strObj);
    Expect(StringEquals(str, "06/15/2020 12:30:45"), "ToString -> \"06/15/2020 12:30:45\"");
    Expect(StringEquals(reinterpret_cast<il2cpp::Il2CppString*>(
               il2cpp::runtime::icall_datetime_to_string(il2cpp::runtime::icall_datetime_from_ymd(1, 1, 1))),
               "01/01/0001 00:00:00"),
           "ToString zero-pads year/month/day/hour/minute/second");
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&strObj));
    il2cpp::il2cpp_gc_collect();
    Expect(StringEquals(str, "06/15/2020 12:30:45"),
           "ToString string survives collection (rooted, payload intact)");

    // --- 8. TimeSpan basics ---
    const il2cpp::runtime::Il2CppTimeSpan ts = il2cpp::runtime::icall_timespan_from_ticks(1838450000000LL);
    Expect(il2cpp::runtime::icall_timespan_get_ticks(ts) == 1838450000000LL,
           "TimeSpan(long) ticks round-trip");
    Expect(il2cpp::runtime::icall_timespan_get_days(ts) == 2, "TimeSpan days == 2");
    Expect(il2cpp::runtime::icall_timespan_get_hours(ts) == 3, "TimeSpan hours == 3");
    Expect(il2cpp::runtime::icall_timespan_get_minutes(ts) == 4, "TimeSpan minutes == 4");
    Expect(il2cpp::runtime::icall_timespan_get_seconds(ts) == 5, "TimeSpan seconds == 5");
    Expect(il2cpp::runtime::icall_timespan_get_total_seconds(ts) == 183845.0,
           "TimeSpan TotalSeconds == 183845.0");
    const il2cpp::runtime::Il2CppTimeSpan hms = il2cpp::runtime::icall_timespan_from_hms(2, 3, 4);
    Expect(il2cpp::runtime::icall_timespan_get_ticks(hms) == 73840000000LL,
           "TimeSpan(2, 3, 4) == 73840000000 ticks");
    Expect(il2cpp::runtime::icall_timespan_get_total_seconds(hms) == 7384.0,
           "TimeSpan(2, 3, 4) TotalSeconds == 7384.0");
    const il2cpp::runtime::Il2CppTimeSpan totalProbe = il2cpp::runtime::icall_timespan_from_ticks(864000000000LL);
    Expect(il2cpp::runtime::icall_timespan_get_total_days(totalProbe) == 1.0
           && il2cpp::runtime::icall_timespan_get_total_hours(totalProbe) == 24.0
           && il2cpp::runtime::icall_timespan_get_total_minutes(totalProbe) == 1440.0
           && il2cpp::runtime::icall_timespan_get_total_seconds(totalProbe) == 86400.0
           && il2cpp::runtime::icall_timespan_get_total_milliseconds(totalProbe) == 86400000.0,
           "TimeSpan(1 day) TotalDays/Hours/Minutes/Seconds/Milliseconds (pure tick arithmetic)");
    Expect(il2cpp::runtime::icall_timespan_get_total_minutes(
               il2cpp::runtime::icall_timespan_from_hms(1, 30, 0)) == 90.0,
           "TimeSpan(1:30) TotalMinutes == 90.0");
    Expect(il2cpp::runtime::icall_timespan_get_total_milliseconds(
               il2cpp::runtime::icall_timespan_from_ticks(10000)) == 1.0,
           "TimeSpan(10000 ticks) TotalMilliseconds == 1.0");
    Expect(il2cpp::runtime::icall_timespan_get_ticks(il2cpp::runtime::icall_timespan_from_hms(0, 0, 0)) == 0,
           "TimeSpan(0, 0, 0) == 0 ticks");
    Expect(il2cpp::runtime::icall_timespan_get_ticks(il2cpp::runtime::icall_timespan_from_hms(25, 0, 0)) == 900000000000LL,
           "TimeSpan hours > 23 allowed (25 h == 900000000000 ticks)");
    Expect(il2cpp::runtime::icall_timespan_get_ticks(il2cpp::runtime::icall_timespan_from_hms(0, 0, -1)) == -10000000LL,
           "TimeSpan(0, 0, -1) == -10000000 ticks (negative allowed)");
    const il2cpp::runtime::Il2CppTimeSpan neg = il2cpp::runtime::icall_timespan_from_ticks(-126000000000LL);
    Expect(il2cpp::runtime::icall_timespan_get_days(neg) == 0 && il2cpp::runtime::icall_timespan_get_hours(neg) == -3
           && il2cpp::runtime::icall_timespan_get_minutes(neg) == -30,
           "negative TimeSpan components truncate toward zero (-3.5 h -> -3 h -30 m)");

    // --- 8b. TimeSpan arithmetic ---
    const il2cpp::runtime::Il2CppTimeSpan oneHour = il2cpp::runtime::icall_timespan_from_ticks(36000000000LL);
    const il2cpp::runtime::Il2CppTimeSpan tenMin = il2cpp::runtime::icall_timespan_from_ticks(600000000LL);
    Expect(il2cpp::runtime::icall_timespan_get_ticks(
               il2cpp::runtime::icall_timespan_add(oneHour, tenMin)) == 36600000000LL,
           "1 h + 10 min == 36600000000 ticks");
    Expect(il2cpp::runtime::icall_timespan_get_total_seconds(
               il2cpp::runtime::icall_timespan_subtract(oneHour, tenMin)) == 3540.0,
           "1 h - 10 min == 3540 s");
    Expect(il2cpp::runtime::icall_timespan_get_ticks(
               il2cpp::runtime::icall_timespan_negate(tenMin)) == -600000000LL,
           "Negate(10 min) == -600000000 ticks");
    Expect(il2cpp::runtime::icall_timespan_get_ticks(
               il2cpp::runtime::icall_timespan_negate(il2cpp::runtime::icall_timespan_from_ticks(-600000000LL))) == 600000000LL,
           "Negate(-10 min) == +600000000 ticks");
    Expect(il2cpp::runtime::icall_timespan_compare_to(tenMin, tenMin) == 0
           && il2cpp::runtime::icall_timespan_compare_to(tenMin, oneHour) == -1
           && il2cpp::runtime::icall_timespan_compare_to(oneHour, tenMin) == 1,
           "TimeSpan CompareTo -1/0/+1");
    Expect(ThrowsWithType([]() {
               il2cpp::runtime::icall_timespan_add(
                   il2cpp::runtime::icall_timespan_from_ticks(4611686018427387904LL),
                   il2cpp::runtime::icall_timespan_from_ticks(4611686018427387904LL));
           }, "System.OverflowException"),
           "TimeSpan.Add overflow throws OverflowException");
    Expect(ThrowsWithType([]() {
               il2cpp::runtime::icall_timespan_negate(il2cpp::runtime::icall_timespan_from_ticks(INT64_MIN));
           }, "System.OverflowException"),
           "TimeSpan.Negate(Int64.MinValue) throws OverflowException");
    Expect(ThrowsWithType([]() {
               il2cpp::runtime::icall_timespan_subtract(
                   il2cpp::runtime::icall_timespan_from_ticks(0),
                   il2cpp::runtime::icall_timespan_from_ticks(INT64_MIN));
           }, "System.OverflowException"),
           "TimeSpan.Subtract with Int64.MinValue throws OverflowException");

    // --- 9. Validation error paths ---
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(0, 1, 1); },
                          "System.ArgumentOutOfRangeException"),
           "year 0 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(10000, 1, 1); },
                          "System.ArgumentOutOfRangeException"),
           "year 10000 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(2020, 0, 1); },
                          "System.ArgumentOutOfRangeException"),
           "month 0 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(2020, 13, 1); },
                          "System.ArgumentOutOfRangeException"),
           "month 13 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(2020, 2, 30); },
                          "System.ArgumentOutOfRangeException"),
           "2020-02-30 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(2021, 2, 29); },
                          "System.ArgumentOutOfRangeException"),
           "2021-02-29 throws ArgumentOutOfRangeException (2021 not a leap year)");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(1900, 2, 29); },
                          "System.ArgumentOutOfRangeException"),
           "1900-02-29 throws ArgumentOutOfRangeException (century non-leap)");
    Expect(!ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(2020, 2, 29); },
                           "System.ArgumentOutOfRangeException"),
           "2020-02-29 is valid (divisible by 400 rule: 2000-02-29 also valid)");
    Expect(!ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymd(2000, 2, 29); },
                           "System.ArgumentOutOfRangeException"),
           "2000-02-29 is valid (2000 divisible by 400)");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymdhms(2020, 1, 1, 24, 0, 0); },
                          "System.ArgumentOutOfRangeException"),
           "hour 24 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymdhms(2020, 1, 1, 0, 60, 0); },
                          "System.ArgumentOutOfRangeException"),
           "minute 60 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymdhms(2020, 1, 1, 0, 0, 60); },
                          "System.ArgumentOutOfRangeException"),
           "second 60 throws ArgumentOutOfRangeException");
    Expect(!ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_ymdhms(2020, 1, 1, 23, 59, 59); },
                           "System.ArgumentOutOfRangeException"),
           "2020-01-01 23:59:59 is valid");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_timespan_from_hms(INT32_MAX, 0, 0); },
                          "System.ArgumentOutOfRangeException"),
           "TimeSpan(hours = int.MaxValue) throws ArgumentOutOfRangeException");

    // --- 10. Extended DateTime component accessors (get_Ticks / get_Millisecond / get_DayOfYear) ---
    const il2cpp::runtime::Il2CppDateTime known = il2cpp::runtime::icall_datetime_from_ymd(2020, 6, 15);
    Expect(il2cpp::runtime::icall_datetime_get_ticks(known) == 637277760000000000LL,
           "get_Ticks(2020-06-15) == the raw tick count");
    Expect(il2cpp::runtime::icall_datetime_get_ticks(il2cpp::runtime::icall_datetime_from_ymd(1, 1, 1)) == 0,
           "get_Ticks(0001-01-01) == 0");
    // Ticks round-trips with the construction value even with a time-of-day.
    const il2cpp::runtime::Il2CppDateTime stamped =
        il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 12, 30, 45);
    Expect(il2cpp::runtime::icall_datetime_get_ticks(stamped) == stamped.ticks,
           "get_Ticks returns the raw ticks for a stamped instant");
    // Millisecond: midnight has 0; 12:30:45.123 has 123 (no carry into the
    // second from the raw tick arithmetic -- the constructor stores whole
    // seconds, so build the fraction by adding ticks directly).
    Expect(il2cpp::runtime::icall_datetime_get_millisecond(
               il2cpp::runtime::icall_datetime_from_ymd(2020, 6, 15)) == 0,
           "get_Millisecond(midnight) == 0");
    Expect(il2cpp::runtime::icall_datetime_get_millisecond(
               il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 12, 30, 45)) == 0,
           "get_Millisecond(12:30:45) == 0 (whole seconds)");
    const il2cpp::runtime::Il2CppDateTime withMs(
        il2cpp::runtime::icall_datetime_from_ymdhms(2020, 6, 15, 12, 30, 45).ticks + 123 * 10000);
    Expect(il2cpp::runtime::icall_datetime_get_millisecond(withMs) == 123,
           "get_Millisecond(12:30:45.123) == 123");
    // DayOfYear: 2020-06-15 is day 167 of a leap year (Jan 31 + Feb 29 + Mar
    // 31 + Apr 30 + May 31 = 152, +15 = 167). 0001-01-01 is day 1; Dec 31 of
    // a leap year is 366; Dec 31 of a common year is 365.
    Expect(il2cpp::runtime::icall_datetime_get_dayofyear(known) == 167,
           "get_DayOfYear(2020-06-15) == 167 (leap-year accounting)");
    Expect(il2cpp::runtime::icall_datetime_get_dayofyear(
               il2cpp::runtime::icall_datetime_from_ymd(1, 1, 1)) == 1,
           "get_DayOfYear(0001-01-01) == 1");
    Expect(il2cpp::runtime::icall_datetime_get_dayofyear(
               il2cpp::runtime::icall_datetime_from_ymd(2020, 12, 31)) == 366,
           "get_DayOfYear(2020-12-31) == 366 (leap year)");
    Expect(il2cpp::runtime::icall_datetime_get_dayofyear(
               il2cpp::runtime::icall_datetime_from_ymd(2021, 12, 31)) == 365,
           "get_DayOfYear(2021-12-31) == 365 (common year)");
    Expect(il2cpp::runtime::icall_datetime_get_dayofyear(
               il2cpp::runtime::icall_datetime_from_ymd(2020, 3, 1)) == 61,
           "get_DayOfYear(2020-03-01) == 61 (Jan 31 + Feb 29 + 1)");

    // --- 11. Static date helpers (DaysInMonth / IsLeapYear) ---
    Expect(il2cpp::runtime::icall_datetime_days_in_month(2020, 2) == 29,
           "DaysInMonth(2020, 2) == 29 (leap February)");
    Expect(il2cpp::runtime::icall_datetime_days_in_month(2021, 2) == 28,
           "DaysInMonth(2021, 2) == 28 (common February)");
    Expect(il2cpp::runtime::icall_datetime_days_in_month(2000, 2) == 29,
           "DaysInMonth(2000, 2) == 29 (400-year rule)");
    Expect(il2cpp::runtime::icall_datetime_days_in_month(1900, 2) == 28,
           "DaysInMonth(1900, 2) == 28 (century non-leap)");
    Expect(il2cpp::runtime::icall_datetime_days_in_month(2020, 1) == 31
           && il2cpp::runtime::icall_datetime_days_in_month(2020, 4) == 30
           && il2cpp::runtime::icall_datetime_days_in_month(2020, 12) == 31,
           "DaysInMonth 31/30/31 anchor months");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_days_in_month(2020, 0); },
                          "System.ArgumentOutOfRangeException"),
           "DaysInMonth month 0 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_days_in_month(2020, 13); },
                          "System.ArgumentOutOfRangeException"),
           "DaysInMonth month 13 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_days_in_month(0, 1); },
                          "System.ArgumentOutOfRangeException"),
           "DaysInMonth year 0 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_days_in_month(10000, 1); },
                          "System.ArgumentOutOfRangeException"),
           "DaysInMonth year 10000 throws ArgumentOutOfRangeException");
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(2020) == 1,
           "IsLeapYear(2020) == 1");
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(2021) == 0,
           "IsLeapYear(2021) == 0");
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(2000) == 1,
           "IsLeapYear(2000) == 1 (400-year rule)");
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(1900) == 0,
           "IsLeapYear(1900) == 0 (century non-leap)");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_is_leap_year(0); },
                          "System.ArgumentOutOfRangeException"),
           "IsLeapYear(0) throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_is_leap_year(10000); },
                          "System.ArgumentOutOfRangeException"),
           "IsLeapYear(10000) throws ArgumentOutOfRangeException");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d time assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime time tests passed\n");
    return 0;
}
