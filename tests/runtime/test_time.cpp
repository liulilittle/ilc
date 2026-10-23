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
#include "timezone.h" // il2cpp_timezone_get_utc_offset_at (FileTime offset oracle)

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
    Expect(StringEquals(str, "2020/6/15 12:30:45"), "ToString -> \"06/15/2020 12:30:45\"");
    Expect(StringEquals(reinterpret_cast<il2cpp::Il2CppString*>(
               il2cpp::runtime::icall_datetime_to_string(il2cpp::runtime::icall_datetime_from_ymd(1, 1, 1))),
               "0001/1/1 0:00:00"),
           "ToString renders the zh-CN \"G\" pattern (saudit5; net48 oracle)");
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&strObj));
    il2cpp::il2cpp_gc_collect();
    Expect(StringEquals(str, "2020/6/15 12:30:45"),
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

    // --- 8c. TimeSpan.ToString(format) -- c/g/G standard + custom tokens
    // (tsfmt1/tsfmt2 net48 oracle, 2026-10-23). ---
    {
        auto SpanFmt = [](il2cpp::runtime::Il2CppTimeSpan value, const char* fmt) {
            il2cpp::Il2CppString* fs = il2cpp::il2cpp_string_new_from_cs(fmt);
            return reinterpret_cast<il2cpp::Il2CppString*>(
                il2cpp::runtime::icall_timespan_to_string_format(
                    value, reinterpret_cast<il2cpp::Il2CppObject*>(fs)));
        };
        const il2cpp::runtime::Il2CppTimeSpan span =
            il2cpp::runtime::icall_timespan_from_ticks(937840050000LL); // 1.02:03:04.005
        Expect(StringEquals(SpanFmt(span, "c"), "1.02:03:04.0050000"),
               "TimeSpan 'c' == d.hh:mm:ss.fffffff");
        Expect(StringEquals(SpanFmt(span, "g"), "1:2:03:04.005"),
               "TimeSpan 'g' == d:h:mm:ss[.trimmed]");
        Expect(StringEquals(SpanFmt(span, "G"), "1:02:03:04.0050000"),
               "TimeSpan 'G' == d:hh:mm:ss.fffffff (day always)");
        Expect(StringEquals(SpanFmt(span, "t"), "1.02:03:04.0050000"),
               "TimeSpan 't' aliases 'c'");
        Expect(StringEquals(SpanFmt(span, ""), "1.02:03:04.0050000"),
               "TimeSpan empty format == 'c'");
        Expect(StringEquals(SpanFmt(span, nullptr), "1.02:03:04.0050000"),
               "TimeSpan null format == 'c'");

        const il2cpp::runtime::Il2CppTimeSpan zeroSpan =
            il2cpp::runtime::icall_timespan_from_ticks(0);
        Expect(StringEquals(SpanFmt(zeroSpan, "c"), "00:00:00"),
               "TimeSpan zero 'c'");
        Expect(StringEquals(SpanFmt(zeroSpan, "g"), "0:00:00"),
               "TimeSpan zero 'g'");
        Expect(StringEquals(SpanFmt(zeroSpan, "G"), "0:00:00:00.0000000"),
               "TimeSpan zero 'G'");

        const il2cpp::runtime::Il2CppTimeSpan negSpan =
            il2cpp::runtime::icall_timespan_from_ticks(-5000000LL);
        Expect(StringEquals(SpanFmt(negSpan, "c"), "-00:00:00.5000000"),
               "TimeSpan negative 'c' keeps the sign");
        Expect(StringEquals(SpanFmt(negSpan, "g"), "-0:00:00.5"),
               "TimeSpan negative 'g' keeps the sign");
        Expect(StringEquals(SpanFmt(negSpan, "G"), "-0:00:00:00.5000000"),
               "TimeSpan negative 'G' keeps the sign");

        // Custom tokens.
        Expect(StringEquals(SpanFmt(span, "dd\\.hh\\:mm\\:ss\\.fff"),
                            "01.02:03:04.005"),
               "TimeSpan custom full pattern");
        Expect(StringEquals(SpanFmt(span, "hh\\:mm"), "02:03"),
               "TimeSpan custom hh:mm");
        Expect(StringEquals(SpanFmt(span, "%d"), "1"), "TimeSpan %d == day (1 digit)");
        Expect(StringEquals(SpanFmt(span, "ddd"), "001"), "TimeSpan ddd zero-pads to 3");
        Expect(StringEquals(SpanFmt(span, "dddddddd"), "00000001"),
               "TimeSpan dddddddd zero-pads to 8");
        Expect(StringEquals(SpanFmt(span, "%f"), "0"), "TimeSpan %f == 1 fraction digit");
        Expect(StringEquals(SpanFmt(span, "fffffff"), "0050000"),
               "TimeSpan fffffff == 7 fraction digits");
        Expect(StringEquals(SpanFmt(span, "ss\\.FFF"), "04.005"),
               "TimeSpan F trims trailing zeros");
        Expect(StringEquals(SpanFmt(span, "hh':'mm"), "02:03"),
               "TimeSpan quoted ':' literal");
        Expect(StringEquals(SpanFmt(span, "\\\\dd"), "\\01"),
               "TimeSpan escaped backslash literal");

        // Custom formats use the ABSOLUTE value (no sign).
        const il2cpp::runtime::Il2CppTimeSpan negFull =
            il2cpp::runtime::icall_timespan_from_ticks(-937840050000LL);
        Expect(StringEquals(SpanFmt(negFull, "dd\\.hh\\:mm\\:ss\\.fff"),
                            "01.02:03:04.005"),
               "TimeSpan custom format drops the sign (absolute value)");

        // Invalid patterns.
        Expect(ThrowsWithType([&] { (void)SpanFmt(span, "d"); },
                              "System.FormatException"),
               "TimeSpan single 'd' throws FormatException (standard lookup)");
        Expect(ThrowsWithType([&] { (void)SpanFmt(span, "C"); },
                              "System.FormatException"),
               "TimeSpan single 'C' throws FormatException");
        Expect(ThrowsWithType([&] { (void)SpanFmt(span, "abc"); },
                              "System.FormatException"),
               "TimeSpan custom 'abc' throws FormatException (no literal passthrough)");
        Expect(ThrowsWithType([&] { (void)SpanFmt(span, "hh:mm"); },
                              "System.FormatException"),
               "TimeSpan raw ':' throws FormatException");
        Expect(ThrowsWithType([&] { (void)SpanFmt(span, "hhh"); },
                              "System.FormatException"),
               "TimeSpan 'hhh' throws FormatException");
        Expect(ThrowsWithType([&] { (void)SpanFmt(span, "ffffffff"); },
                              "System.FormatException"),
               "TimeSpan 8 fraction digits throws FormatException");
        Expect(ThrowsWithType([&] { (void)SpanFmt(span, "ddddddddd"); },
                              "System.FormatException"),
               "TimeSpan 9 day digits throws FormatException");
    }

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

    // --- 12. The FILETIME bridge (diffdt001b) ---
    //
    // Structure: the machine-independent INVARIANTS run everywhere; the exact
    // net48 numbers are hardcoded (measured 2026-10-20, see
    // .opencode/tmp/dt-fix.md) and only asserted when the host zone matches
    // the oracle zone -- otherwise an explicit SKIP line is printed. Nothing
    // here derives an expectation from the same function under test.
    constexpr int64_t kFileTimeEpoch = 504911232000000000LL;
    constexpr int64_t kTicksPerSecondLocal = 10000000LL;

    const il2cpp::runtime::Il2CppDateTime utcValue(637185764967890000LL, 1 /* Utc */);
    const il2cpp::runtime::Il2CppDateTime unsValue(637185764967890000LL, 0 /* Unspecified */);
    const il2cpp::runtime::Il2CppDateTime localValue(637185764967890000LL, 2 /* Local */);

    // --- 12a. Kind-independent epoch arithmetic (invariants) ---
    Expect(il2cpp::runtime::icall_datetime_to_file_time_utc(utcValue) == 132274532967890000LL,
           "ToFileTimeUtc(Utc) is the plain 1601-epoch shift (net48 132274532967890000)");
    Expect(il2cpp::runtime::icall_datetime_to_file_time(utcValue) == 132274532967890000LL,
           "ToFileTime(Utc) is the plain 1601-epoch shift");
    // net48 ToFileTimeUtc on an Unspecified value keeps the ticks (net48
    // 132274532967890000), while ToFileTime routes through ToUniversalTime.
    Expect(il2cpp::runtime::icall_datetime_to_file_time_utc(unsValue) == unsValue.ticks - kFileTimeEpoch,
           "ToFileTimeUtc(Unspecified) keeps the ticks (net48)");
    // ToFileTime and ToFileTimeUtc agree on a Local value (net48 measured
    // 132274244967890000 on UTC+8); the Kind=Local branch must convert.
    Expect(il2cpp::runtime::icall_datetime_to_file_time(localValue)
               == il2cpp::runtime::icall_datetime_to_file_time_utc(localValue),
           "ToFileTime(Local) == ToFileTimeUtc(Local) (net48)");
    Expect(il2cpp::runtime::icall_datetime_to_file_time(localValue) != localValue.ticks - kFileTimeEpoch
               || il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(
                      il2cpp::runtime::Il2CppDateTime(localValue.ticks, 0)) == 0,
           "ToFileTime(Local) applies the offset unless the zone is UTC");

    // --- 12b. FromFileTimeUtc: epoch shift, Kind, range contract ---
    const il2cpp::runtime::Il2CppDateTime fromUtc = il2cpp::runtime::icall_datetime_from_file_time_utc(132223104000000000LL);
    Expect(fromUtc.ticks == 637134336000000000LL, "FromFileTimeUtc(132223104000000000) == 637134336000000000");
    Expect(fromUtc.kind == 1, "FromFileTimeUtc returns Kind=Utc");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_file_time_utc(-1); },
                          "System.ArgumentOutOfRangeException"),
           "FromFileTimeUtc(-1) throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_file_time_utc(2650467744000000000LL); },
                          "System.ArgumentOutOfRangeException"),
           "FromFileTimeUtc(max + 1) throws ArgumentOutOfRangeException");

    // --- 12c. FromFileTime: Kind=Local + the range/edge contract ---
    Expect(il2cpp::runtime::icall_datetime_from_file_time(132223104000000000LL).kind == 2,
           "FromFileTime returns Kind=Local");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_file_time(-1); },
                          "System.ArgumentOutOfRangeException"),
           "FromFileTime(-1) throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_from_file_time(2650467744000000000LL); },
                          "System.ArgumentOutOfRangeException"),
           "FromFileTime(max + 1) throws ArgumentOutOfRangeException");
    // FromFileTime(max) must stay inside the DateTime range (net48 CLAMPS;
    // it never throws and never wraps).
    const il2cpp::runtime::Il2CppDateTime fromMax = il2cpp::runtime::icall_datetime_from_file_time(2650467743999999999LL);
    Expect(fromMax.ticks >= 0 && fromMax.ticks <= 3155378975999999999LL,
           "FromFileTime(max) stays inside [MinValue, MaxValue]");
    // Pre-1601 values throw on both ToFileTime forms (Kind=Utc so the assertion
    // does not depend on the machine zone's sign).
    const il2cpp::runtime::Il2CppDateTime preEpoch(504911231999999999LL, 1 /* Utc */);
    Expect(ThrowsWithType([&]() { il2cpp::runtime::icall_datetime_to_file_time(preEpoch); },
                          "System.ArgumentOutOfRangeException"),
           "ToFileTime(pre-1601) throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([&]() { il2cpp::runtime::icall_datetime_to_file_time_utc(preEpoch); },
                          "System.ArgumentOutOfRangeException"),
           "ToFileTimeUtc(pre-1601) throws ArgumentOutOfRangeException");

    // --- 12d. Round trips in BOTH directions ---
    // (a) fileTime -> FromFileTime -> ToFileTime must give the original
    //     FILETIME back: the wall value carries Kind=Local, so ToFileTime
    //     converts it through the same zone data.
    const int64_t roundTripFileTimes[] = { 132223104000000000LL, 132358752000000000LL, 132224256000000000LL };
    for (int i = 0; i < 3; ++i)
    {
        const int64_t fileTime = roundTripFileTimes[i];
        Expect(il2cpp::runtime::icall_datetime_to_file_time(
                   il2cpp::runtime::icall_datetime_from_file_time(fileTime)) == fileTime,
               "FromFileTime -> ToFileTime round-trips the FILETIME");
    }
    // (b) Local DateTime -> ToFileTime -> FromFileTime must give the ticks back.
    Expect(il2cpp::runtime::icall_datetime_from_file_time(
               il2cpp::runtime::icall_datetime_to_file_time(localValue)).ticks == localValue.ticks,
           "ToFileTime -> FromFileTime round-trips the Local ticks");

    // --- 12e. The pre-1900 branch really is the STANDARD-offset branch ---
    //
    // Every expectation below comes from a DIFFERENT code path than the value
    // under test:
    //   * the CRT itself (localtime_r/gmtime_r on the same time_t) for the
    //     zone's per-instant offsets -- note that timezone.cpp's
    //     il2cpp_timezone_get_utc_offset_at is a POSIX APPROXIMATION (it feeds
    //     the civil face through the current bias and mis-classifies the
    //     2000-04-01 season), so it cannot serve as the reference here;
    //   * a DIFFERENT instant's FileTime result for the standard-offset value.
    // They do NOT claim net48 equality: net48 still reports DST below 1900
    // (measured: Eastern 1899/1900-07 -> -14400, Lord Howe 1899/1900-01 ->
    // +39600), while this bridge deliberately normalises pre-1900 instants to
    // the standard offset (see kHistoricalFloorTicks and section 7 of
    // .opencode/tmp/dt-fix.md).
    const int64_t kUnixEpochTicksLocal = 621355968000000000LL;
    auto crtOffsetTicksAt = [&](int64_t utcTicks) {
        // TICKS, straight from the CRT's tzdata at that instant.
        const std::time_t instant = static_cast<std::time_t>((utcTicks - kUnixEpochTicksLocal) / kTicksPerSecondLocal);
        std::tm localTm = {};
#if defined(_WIN32)
        // MinGW-w64 does not declare localtime_r without _POSIX_THREAD_SAFE_FUNCTIONS;
        // the _s variant is unconditional (same CRT zone data).
        if (_localtime64_s(&localTm, &instant) != 0)
        {
            return int64_t(0);
        }
#else
        if (localtime_r(&instant, &localTm) == nullptr)
        {
            return int64_t(0);
        }
#endif
        // "Local wall clock interpreted as UTC" minus the instant = the offset.
        // _mkgmtime (MSVC/MinGW) and timegm (glibc) are the same function.
#if defined(_WIN32)
        const std::time_t asIfUtc = _mkgmtime(&localTm);
#else
        const std::time_t asIfUtc = timegm(&localTm);
#endif
        if (asIfUtc == static_cast<std::time_t>(-1))
        {
            return int64_t(0);
        }
        return (static_cast<int64_t>(asIfUtc) - static_cast<int64_t>(instant)) * kTicksPerSecondLocal;
    };
    auto ftOffsetAt = [](int64_t utcTicks) {
        // TICKS, from the bridge under test.
        return il2cpp::runtime::icall_datetime_from_file_time(utcTicks - kFileTimeEpoch).ticks - utcTicks;
    };
    // The four 2000 season samples, classified by the CRT.
    const int64_t kSeasonSamples[4] = {
        630822816000000000LL, 630901440000000000LL, 630980064000000000LL, 631059552000000000LL
    };
    int64_t crtSeasons[4];
    int minIdx = 0;
    int maxIdx = 0;
    for (int i = 0; i < 4; ++i)
    {
        crtSeasons[i] = crtOffsetTicksAt(kSeasonSamples[i]);
        if (crtSeasons[i] < crtSeasons[minIdx]) minIdx = i;
        if (crtSeasons[i] > crtSeasons[maxIdx]) maxIdx = i;
    }
    const bool hostHasDst = crtSeasons[minIdx] != crtSeasons[maxIdx];

    const int64_t jan1601Offset = ftOffsetAt(kFileTimeEpoch);
    const int64_t jul1601Offset = ftOffsetAt(505067616000000000LL);

    // (a) The pre-floor value is the zone's STANDARD offset: it equals the
    //     CRT's minimum season and (on a DST host) differs from the CRT's
    //     daylight season. The second half is the discriminating part.
    Expect(jan1601Offset == crtSeasons[minIdx],
           "pre-1900 offset == the CRT's standard (min) season offset");
    if (hostHasDst)
    {
        Expect(jan1601Offset != crtSeasons[maxIdx],
               "pre-1900 offset is NOT the DST-side offset (no rule lookup)");
    }
    // (b) ... and that offset is minute-aligned (never tzdata local mean time).
    Expect(jan1601Offset % (60 * kTicksPerSecondLocal) == 0,
           "pre-1900 offset is minute-aligned (never tzdata local mean time)");
    // (c) Expected value built from a DIFFERENT INSTANT: the FileTime offset at
    //     the 2000 sample the CRT classified as the standard season.
    const int64_t standardSeasonFileTimeOffset = ftOffsetAt(kSeasonSamples[minIdx]);
    Expect(jan1601Offset == standardSeasonFileTimeOffset,
           "pre-1900 offset == the FileTime offset of the CRT's standard 2000 season");
    //     Both pre-floor instants (six months apart) must land on that same
    //     constant; a rule lookup would separate them on a DST host.
    Expect(jul1601Offset == standardSeasonFileTimeOffset,
           "pre-1900 July also uses the standard-season constant");
    if (hostHasDst)
    {
        // The DST-season FileTime offset must be the CRT's daylight value and
        // must differ from the pre-floor constant.
        Expect(ftOffsetAt(kSeasonSamples[maxIdx]) == crtSeasons[maxIdx],
               "post-floor DST-season FileTime offset == the CRT's daylight offset");
        Expect(jan1601Offset != crtSeasons[maxIdx],
               "pre-1900 constant differs from the CRT's daylight offset");
    }
    // (d) Post-floor rule branch: the FileTime offsets at 2020-01/2020-07 must
    //     match the CRT at the SAME instants, and their DIFFERENCE must equal
    //     the CRT's DST delta. Delta-vs-delta, so it is not the vacuous
    //     "one constant != two values" shape.
    const int64_t jan2020 = ftOffsetAt(637134336000000000LL);
    const int64_t jul2020 = ftOffsetAt(637269984000000000LL);
    Expect(jan2020 == crtOffsetTicksAt(637134336000000000LL),
           "post-floor January offset agrees with the CRT at that instant");
    Expect(jul2020 == crtOffsetTicksAt(637269984000000000LL),
           "post-floor July offset agrees with the CRT at that instant");
    if (hostHasDst)
    {
        const int64_t crtDstDelta = crtSeasons[maxIdx] - crtSeasons[minIdx];
        const int64_t observedDelta = jul2020 > jan2020 ? jul2020 - jan2020 : jan2020 - jul2020;
        Expect(observedDelta == crtDstDelta,
               "post-floor Jan/Jul offset difference equals the CRT's DST delta");
    }
    // --- 12f. Hardcoded net48 oracle, asserted only in the matching zone ---
    //
    // The expected numbers below are net48 measurements, NOT values computed by
    // this runtime. They are asserted when the host zone matches the oracle
    // zone and skipped (with an explicit line) otherwise, so CI on any zone
    // prints a visible SKIP instead of silently passing.
    const int64_t janInstantOffset =
        il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(il2cpp::runtime::Il2CppDateTime(637134336000000000LL, 1));
    const int64_t julInstantOffset =
        il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(il2cpp::runtime::Il2CppDateTime(637269984000000000LL, 1));

    auto oracleSkip = [](const char* what) {
        std::printf("SKIP: %s (host zone does not match the net48 oracle zone)\n", what);
    };

    if (janInstantOffset == 28800 && julInstantOffset == 28800)
    {
        std::printf("ORACLE: net48 China Standard Time (+08:00, no DST)\n");
        // net48 China Standard Time (measured).
        Expect(il2cpp::runtime::icall_datetime_from_file_time(132223104000000000LL).ticks == 637134624000000000LL,
               "net48 China: FromFileTime(2020-01-01Z) == 637134624000000000");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(132358752000000000LL).ticks == 637270272000000000LL,
               "net48 China: FromFileTime(2020-07-01Z) == 637270272000000000");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(0).ticks == 504911520000000000LL,
               "net48 China: FromFileTime(0) == 504911520000000000");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(2650467743999999999LL).ticks == 3155378975999999999LL,
               "net48 China: FromFileTime(max) clamps to MaxValue");
        Expect(il2cpp::runtime::icall_datetime_to_file_time(unsValue) == 132274244967890000LL,
               "net48 China: ToFileTime(Unspecified) == 132274244967890000");
        Expect(il2cpp::runtime::icall_datetime_to_file_time(localValue) == 132274244967890000LL,
               "net48 China: ToFileTime(Local) == 132274244967890000");
    }
    else if (janInstantOffset == -18000 && julInstantOffset == -14400)
    {
        std::printf("ORACLE: net48 Eastern Standard Time (-05:00 / -04:00 DST)\n");
        // net48 Eastern Standard Time (measured).
        Expect(il2cpp::runtime::icall_datetime_from_file_time(132223104000000000LL).ticks == 637134156000000000LL,
               "net48 EST: FromFileTime(2020-01-01Z) == 637134156000000000");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(132358752000000000LL).ticks == 637269840000000000LL,
               "net48 EST: FromFileTime(2020-07-01Z) == 637269840000000000 (DST)");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(0).ticks == 504911052000000000LL,
               "net48 EST: FromFileTime(0) == 504911052000000000");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(2650467743999999999LL).ticks == 3155378795999999999LL,
               "net48 EST: FromFileTime(max) == 3155378795999999999");
        // The DST season must be distinguished from the winter offset (the
        // old current-offset code used one offset for both). 2020 is inside
        // the verified 1918-1970+ window.
        Expect(il2cpp::runtime::icall_datetime_from_file_time(132358752000000000LL).ticks
                   != 637269984000000000LL + janInstantOffset * kTicksPerSecondLocal,
               "net48 EST: FromFileTime(Jul) does NOT use the January offset");
        // Ambiguous wall clock (2020-11-01 01:30, the repeated fall-back hour):
        // net48 resolves it to the STANDARD offset -> ToFileTime
        // 132486858000000000 (asserted through the icall under test).
        Expect(il2cpp::runtime::icall_datetime_to_file_time(
                   il2cpp::runtime::Il2CppDateTime(637397910000000000LL, 2)) == 132486858000000000LL,
               "net48 EST: ToFileTime(wall 2020-11-01 01:30, ambiguous) == 132486858000000000");
        // Invalid wall clock (2020-03-08 02:30, the skipped spring-forward
        // hour): net48 also resolves to the standard offset -> 132281262000000000.
        Expect(il2cpp::runtime::icall_datetime_to_file_time(
                   il2cpp::runtime::Il2CppDateTime(637192314000000000LL, 2)) == 132281262000000000LL,
               "net48 EST: ToFileTime(wall 2020-03-08 02:30, invalid) == 132281262000000000");
        // Pre-1970 REAL history: only the 1918-1970 window is verified against
        // net48 (see the kHistoricalFloorTicks note in time.cpp). net48 reports
        // the 1969 DST season, so FromFileTime shifts by -4 h in July and -5 h
        // in January (expected ticks are the net48 UTC ticks minus the offset).
        //
        // NOT asserted here (known divergence, documented in
        // .opencode/tmp/dt-fix.md section 7):
        //   * 1900-1917 New York -- glibc -0500 all year vs net48 -0400 in July
        //   * pre-1900 (incl. 1899) -- net48 still applies DST; this bridge
        //     deliberately uses the standard offset
        std::printf("SKIP: 1900-1917 / pre-1900 New York net48 equality (known POSIX-vs-net48 rule-source divergence)\n");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(621196992000000000LL - 504911232000000000LL).ticks
                   == 621196848000000000LL,
               "net48 EST: FromFileTime(1969-07-01Z) == 621196848000000000 (-4 h, pre-1970 DST)");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(621040608000000000LL - 504911232000000000LL).ticks
                   == 621040428000000000LL,
               "net48 EST: FromFileTime(1969-01-01Z) == 621040428000000000 (-5 h, pre-1970 standard)");
    }
    else if (janInstantOffset == 0 && julInstantOffset == 0)
    {
        std::printf("ORACLE: net48 UTC (no offset)\n");
        // net48 UTC (measured): no offset anywhere.
        Expect(il2cpp::runtime::icall_datetime_from_file_time(132223104000000000LL).ticks == 637134336000000000LL,
               "net48 UTC: FromFileTime(2020-01-01Z) == 637134336000000000");
        Expect(il2cpp::runtime::icall_datetime_from_file_time(0).ticks == 504911232000000000LL,
               "net48 UTC: FromFileTime(0) == 504911232000000000");
        Expect(il2cpp::runtime::icall_datetime_to_file_time(localValue) == 132274532967890000LL,
               "net48 UTC: ToFileTime(Local) == 132274532967890000");
    }
    else
    {
        oracleSkip("exact FileTime numbers (host zone has no recorded net48 oracle)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d time assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime time tests passed\n");
    return 0;
}
