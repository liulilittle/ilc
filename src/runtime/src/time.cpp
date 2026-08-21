//------------------------------------------------------------------------------
// time.cpp -- System.DateTime / System.TimeSpan runtime implementation.
//
//   Representation : int64 tick counts (100 ns units). DateTime ticks count
//                    from 0001-01-01T00:00:00 (the .NET epoch); TimeSpan
//                    ticks are a signed difference of the same unit.
//   UTC clock      : std::chrono::system_clock::now() is UTC on both Windows
//                    (FILETIME-backed) and POSIX (epoch-based); its tick
//                    count is shifted by the 719162-day constant between the
//                    Unix and .NET epochs (621355968000000000 ticks).
//   Local clock    : DateTime.Now = UtcNow + local timezone offset. The
//                    offset is derived from <ctime> localtime/gmtime on the
//                    SAME instant (no Windows-only headers, no mktime / DST
//                    ambiguity -- both representations come from one time_t;
//                    the field difference IS the offset at that instant).
//   Civil calendar : the proleptic Gregorian day count (Howard Hinnant's
//                    days_from_civil / civil_from_days) shifted from the
//                    1970 epoch to the .NET epoch (719162 days). Pinned by
//                    constants: 1970-01-01 = 621355968000000000 ticks,
//                    2000-01-01 = 630822816000000000 ticks.
//
//   Validation mirrors .NET Framework's checked constructors/arithmetic:
//   DateTime components are range-checked (ArgumentOutOfRangeException),
//   AddDays/Hours/Minutes round to the nearest millisecond (half away from
//   zero -- the reference's (long)(value * scale +- 0.5) cast), and TimeSpan
//   Add/Subtract/Negate report overflow (OverflowException). All exceptions
//   are carried by il2cpp_throw_new (see il2cpp_exception.h).
//------------------------------------------------------------------------------

#include "il2cpp_time.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_string.h"

#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <ctime>

namespace il2cpp {
namespace runtime {

namespace {

// .NET time constants: 1 tick = 100 ns (TicksPer* are exact multiples).
constexpr int64_t kTicksPerSecond = 10000000;
constexpr int64_t kTicksPerMinute = 600000000;
constexpr int64_t kTicksPerHour = 36000000000;
constexpr int64_t kTicksPerDay = 864000000000;
constexpr int64_t kTicksPerMillisecond = 10000;
constexpr int64_t kMillisPerMinute = 60000;
constexpr int64_t kMillisPerHour = 3600000;
constexpr int64_t kMillisPerDay = 86400000;
// 1970-01-01 expressed in .NET ticks (719162 days * 864000000000).
constexpr int64_t kUnixEpochTicks = 621355968000000000;
// DateTime.MaxValue ticks: 9999-12-31 23:59:59.9999999 (MinValue == 0).
constexpr int64_t kMaxDateTimeTicks = 3155378975999999999;
// .NET Framework's Add(value, scale) bound: Int64.MaxValue / TicksPerMillisecond.
constexpr int64_t kMaxMillis = INT64_MAX / kTicksPerMillisecond;
// .NET Framework's TimeToTicks bounds: Int64 limits / TicksPerSecond.
constexpr int64_t kMaxTotalSeconds = INT64_MAX / kTicksPerSecond;
constexpr int64_t kMinTotalSeconds = INT64_MIN / kTicksPerSecond;

// Throws the managed ArgumentOutOfRangeException (canonical factory, see
// il2cpp_exception_ext.h). Never returns.
void ThrowArgOutOfRange()
{
    il2cpp_throw_argument_out_of_range_exception(
        nullptr, "Specified argument was out of the range of valid values.");
}

// Throws the managed OverflowException. Never returns.
void ThrowOverflow()
{
    il2cpp_throw_overflow_exception("The operation resulted in an overflow.");
}

// Days from the .NET epoch (0001-01-01) to the civil date (year, month, day).
// Hinnant's days_from_civil (1970 epoch) shifted by the 719162-day offset
// between the Unix and .NET epochs. Inputs are pre-validated (1..9999).
int64_t DaysFromNetEpoch(int32_t year, int32_t month, int32_t day)
{
    int64_t y = static_cast<int64_t>(year) - (month <= 2 ? 1 : 0);
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;                                    // [0, 399]
    const int64_t doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + (day - 1); // [0, 365]
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;            // [0, 146096]
    return era * 146097 + doe - 719468 + 719162;                          // days since 0001-01-01
}

// The civil date (year, month, day) for a day count since the .NET epoch.
// Hinnant's civil_from_days applied to (days - 719162); see DaysFromNetEpoch.
void CivilFromNetDays(int64_t days, int32_t* year, int32_t* month, int32_t* day)
{
    const int64_t z = days - 719162 + 719468; // 1970-based day count for the algorithm
    const int64_t era = (z >= 0 ? z : z - 146096) / 146097;
    const int64_t doe = z - era * 146097;                                 // [0, 146096]
    const int64_t yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365; // [0, 399]
    const int64_t y = yoe + era * 400;
    const int64_t doy = doe - (365 * yoe + yoe / 4 - yoe / 100);          // [0, 365]
    const int64_t mp = (5 * doy + 2) / 153;                               // [0, 11]
    const int64_t d = doy - (153 * mp + 2) / 5 + 1;                       // [1, 31]
    const int64_t m = mp < 10 ? mp + 3 : mp - 9;                          // [1, 12]
    *year = static_cast<int32_t>(y + (m <= 2 ? 1 : 0));
    *month = static_cast<int32_t>(m);
    *day = static_cast<int32_t>(d);
}

// Days in month for the proleptic Gregorian calendar (.NET leap rule:
// divisible by 4, except centuries, except every 400 years).
int32_t DaysInMonth(int32_t year, int32_t month)
{
    static const int32_t kDaysInMonth[12] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
    if (month == 2 && year % 4 == 0 && (year % 100 != 0 || year % 400 == 0))
    {
        return 29;
    }
    return kDaysInMonth[month - 1];
}

// DateTime civil components (see GetCivilTime).
struct CivilTime
{
    int32_t year;
    int32_t month;
    int32_t day;
    int32_t hour;
    int32_t minute;
    int32_t second;
};

// Decompose a DateTime into civil components. Valid DateTimes are
// non-negative; negative tick values are caller errors (defensive truncation
// only -- no crash).
CivilTime GetCivilTime(Il2CppDateTime dt)
{
    const int64_t days = dt.ticks / kTicksPerDay;
    const int64_t timeTicks = dt.ticks % kTicksPerDay; // 0..TicksPerDay-1 for valid dates
    CivilTime result;
    CivilFromNetDays(days, &result.year, &result.month, &result.day);
    result.hour = static_cast<int32_t>(timeTicks / kTicksPerHour);
    result.minute = static_cast<int32_t>((timeTicks % kTicksPerHour) / kTicksPerMinute);
    result.second = static_cast<int32_t>((timeTicks % kTicksPerMinute) / kTicksPerSecond);
    return result;
}

// .NET constructor validation: every component must be in range, otherwise
// ArgumentOutOfRangeException. 0001-01-01 is day 1 of the epoch, so year is
// [1, 9999] and the day must exist in the month.
void ValidateDateParts(int32_t year, int32_t month, int32_t day)
{
    if (year < 1 || year > 9999) ThrowArgOutOfRange();
    if (month < 1 || month > 12) ThrowArgOutOfRange();
    if (day < 1 || day > DaysInMonth(year, month)) ThrowArgOutOfRange();
}

void ValidateTimeParts(int32_t hour, int32_t minute, int32_t second)
{
    if (hour < 0 || hour > 23) ThrowArgOutOfRange();
    if (minute < 0 || minute > 59) ThrowArgOutOfRange();
    if (second < 0 || second > 59) ThrowArgOutOfRange();
}

// Build the DateTime ticks for validated civil components: whole days from
// the .NET epoch plus the time-of-day ticks (max = DateTime.MaxValue ticks,
// no int64 overflow for valid inputs).
int64_t ComposeTicks(int32_t year, int32_t month, int32_t day,
                     int32_t hour, int32_t minute, int32_t second)
{
    const int64_t days = DaysFromNetEpoch(year, month, day);
    const int64_t timeTicks = static_cast<int64_t>(hour) * kTicksPerHour
                            + static_cast<int64_t>(minute) * kTicksPerMinute
                            + static_cast<int64_t>(second) * kTicksPerSecond;
    return days * kTicksPerDay + timeTicks;
}

// .NET Framework's Add(value, scale): value * scale (milliseconds) is
// shifted by +/-0.5 and truncated toward zero (round half away from zero at
// the millisecond), bounds-checked against Int64/ms, then added to the ticks
// (the reference's (long)(value * scale + (value >= 0 ? 0.5 : -0.5))).
Il2CppDateTime AddMillis(Il2CppDateTime dt, double value, int64_t millisScale)
{
    const double shifted = value * static_cast<double>(millisScale) + (value >= 0.0 ? 0.5 : -0.5);
    // The double->int64 cast is UB outside the int64 range; guard first
    // (|millis| must fit int64 -- the range check below is much tighter).
    if (!(shifted > -9223372036854775808.0 && shifted < 9223372036854775808.0))
    {
        ThrowArgOutOfRange();
    }
    const int64_t millis = static_cast<int64_t>(shifted);
    if (millis <= -kMaxMillis || millis >= kMaxMillis)
    {
        ThrowArgOutOfRange();
    }
    const int64_t delta = millis * kTicksPerMillisecond;
    // Defensive: signed overflow would be UB in C++ (.NET Framework wraps
    // unchecked at this far edge); refuse instead of corrupting.
    if ((delta > 0 && dt.ticks > INT64_MAX - delta) || (delta < 0 && dt.ticks < INT64_MIN - delta))
    {
        ThrowArgOutOfRange();
    }
    // net48 DateTime.AddTicks bounds the RESULT to [MinValue=0, MaxValue]
    // (ticks < MinTicks || ticks > MaxTicks -> ArgumentOutOfRangeException);
    // the int64 guard above only prevents UB, it does not enforce the
    // DateTime range (MaxValue.AddDays(1) must throw).
    const int64_t result = dt.ticks + delta;
    if (result < 0 || result > kMaxDateTimeTicks)
    {
        ThrowArgOutOfRange();
    }
    return Il2CppDateTime(result);
}

// Checked tick addition for TimeSpan.Add/Subtract: unsigned addition (defined
// wrap) + .NET Framework's sign-of-result test -- overflow iff both operands
// share a sign and the sum's sign differs.
Il2CppTimeSpan AddTicksChecked(int64_t left, int64_t right)
{
    const uint64_t sum = static_cast<uint64_t>(left) + static_cast<uint64_t>(right);
    const int64_t result = static_cast<int64_t>(sum);
    if ((left < 0) == (right < 0) && (left < 0) != (result < 0))
    {
        ThrowOverflow();
    }
    return Il2CppTimeSpan(result);
}

// Current local UTC offset in seconds at the instant of the call. Both the
// local and UTC representations derive from the SAME time_t, so the field
// difference is exact (no mktime / DST ambiguity). Falls back to 0 (UTC) if
// the platform conversion fails -- a clock call must never crash.
int64_t LocalUtcOffsetSeconds()
{
    const std::time_t now = std::time(nullptr);
    if (now == static_cast<std::time_t>(-1))
    {
        return 0;
    }
    std::tm localTm = {};
    std::tm utcTm = {};
#if defined(_WIN32)
    // Windows (MSVC and MinGW-w64): the _s variants are declared without any
    // feature-test guard (localtime/gmtime are deprecated -> C4996 under
    // /W4; the POSIX localtime_r/gmtime_r exist in MinGW-w64 but are guarded
    // by _POSIX_THREAD_SAFE_FUNCTIONS, which clang/MinGW does not define).
    // time_t is 64-bit on Win64, so __time64_t == time_t.
    if (_localtime64_s(&localTm, &now) != 0 || _gmtime64_s(&utcTm, &now) != 0)
    {
        return 0;
    }
#else
    // POSIX: the _r variants (thread-safe).
    if (localtime_r(&now, &localTm) == nullptr || gmtime_r(&now, &utcTm) == nullptr)
    {
        return 0;
    }
#endif
    const int64_t localDays = DaysFromNetEpoch(localTm.tm_year + 1900, localTm.tm_mon + 1, localTm.tm_mday);
    const int64_t utcDays = DaysFromNetEpoch(utcTm.tm_year + 1900, utcTm.tm_mon + 1, utcTm.tm_mday);
    const int64_t localSeconds = localDays * 86400 + localTm.tm_hour * 3600 + localTm.tm_min * 60 + localTm.tm_sec;
    const int64_t utcSeconds = utcDays * 86400 + utcTm.tm_hour * 3600 + utcTm.tm_min * 60 + utcTm.tm_sec;
    return localSeconds - utcSeconds;
}

} // namespace

//------------------------------------------------------------------------------
// System.DateTime -- clock accessors.
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_utc_now()
{
    // Exact 100 ns units (Ticks100Ns) cast from system_clock's duration; the
    // cast is exact on Windows (FILETIME is 100 ns) and on POSIX (10^7
    // nanoseconds divide evenly).
    using Ticks100Ns = std::chrono::duration<int64_t, std::ratio<1, 10000000>>;
    const auto now = std::chrono::system_clock::now();
    const int64_t unixTicks = std::chrono::duration_cast<Ticks100Ns>(now.time_since_epoch()).count();
    return Il2CppDateTime(unixTicks + kUnixEpochTicks);
}

Il2CppDateTime icall_datetime_now()
{
    const int64_t offset = LocalUtcOffsetSeconds();
    const Il2CppDateTime utc = icall_datetime_utc_now();
    return Il2CppDateTime(utc.ticks + offset * kTicksPerSecond);
}

//------------------------------------------------------------------------------
// System.DateTime -- construction.
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_from_ymd(int32_t year, int32_t month, int32_t day)
{
    ValidateDateParts(year, month, day);
    return Il2CppDateTime(ComposeTicks(year, month, day, 0, 0, 0));
}

Il2CppDateTime icall_datetime_from_ymdhms(int32_t year, int32_t month, int32_t day,
                                          int32_t hour, int32_t minute, int32_t second)
{
    ValidateDateParts(year, month, day);
    ValidateTimeParts(hour, minute, second);
    return Il2CppDateTime(ComposeTicks(year, month, day, hour, minute, second));
}

//------------------------------------------------------------------------------
// icall_datetime_from_ticks -- DateTime(long ticks) (see the header).
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_from_ticks(int64_t ticks)
{
    if (ticks < 0 || ticks > kMaxDateTimeTicks)
    {
        il2cpp_throw_argument_out_of_range_exception("ticks", nullptr);
    }
    return Il2CppDateTime(ticks);
}

//------------------------------------------------------------------------------
// System.DateTime -- component accessors.
//------------------------------------------------------------------------------
int32_t icall_datetime_get_year(Il2CppDateTime dt) { return GetCivilTime(dt).year; }
int32_t icall_datetime_get_month(Il2CppDateTime dt) { return GetCivilTime(dt).month; }
int32_t icall_datetime_get_day(Il2CppDateTime dt) { return GetCivilTime(dt).day; }
int32_t icall_datetime_get_hour(Il2CppDateTime dt) { return GetCivilTime(dt).hour; }
int32_t icall_datetime_get_minute(Il2CppDateTime dt) { return GetCivilTime(dt).minute; }
int32_t icall_datetime_get_second(Il2CppDateTime dt) { return GetCivilTime(dt).second; }

int32_t icall_datetime_get_dayofweek(Il2CppDateTime dt)
{
    // .NET: (days since 0001-01-01 + 1) % 7 -- 0001-01-01 was a Monday (= 1),
    // Sunday = 0. Verified: 2000-01-01 (Saturday = 6), 2020-06-15 (Monday).
    const int64_t days = dt.ticks / kTicksPerDay;
    return static_cast<int32_t>((days + 1) % 7);
}

//------------------------------------------------------------------------------
// System.DateTime -- extended component accessors.
//------------------------------------------------------------------------------
int64_t icall_datetime_get_ticks(Il2CppDateTime dt)
{
    // .NET: Ticks => (long)(_dateData & TicksMask) -- the raw 100 ns count.
    // This runtime has no kind bits: the raw count IS the value.
    return dt.ticks;
}

int32_t icall_datetime_get_millisecond(Il2CppDateTime dt)
{
    // .NET: Millisecond => (int)((UTicks / TicksPerMillisecond) % 1000).
    return static_cast<int32_t>((dt.ticks / kTicksPerMillisecond) % 1000);
}

int32_t icall_datetime_get_dayofyear(Il2CppDateTime dt)
{
    // .NET: the day number within the year, 1..366. Days since Jan 1 of the
    // SAME year (via the civil calendar) plus one -- leap-aware because
    // DaysFromNetEpoch counts real calendar days.
    const CivilTime t = GetCivilTime(dt);
    const int64_t dayOfYear = DaysFromNetEpoch(t.year, t.month, t.day)
                            - DaysFromNetEpoch(t.year, 1, 1) + 1;
    return static_cast<int32_t>(dayOfYear);
}

//------------------------------------------------------------------------------
// System.DateTime -- static date helpers.
//------------------------------------------------------------------------------
int32_t icall_datetime_days_in_month(int32_t year, int32_t month)
{
    // .NET: DaysInMonth checks the month range (1..12), then delegates the
    // year validation to IsLeapYear (1..9999) before indexing the month table.
    if (month < 1 || month > 12) ThrowArgOutOfRange();
    if (year < 1 || year > 9999) ThrowArgOutOfRange();
    return DaysInMonth(year, month);
}

int32_t icall_datetime_is_leap_year(int32_t year)
{
    // .NET: IsLeapYear validates the year (1..9999), then the Gregorian rule
    // (divisible by 4, except centuries, except every 400 years).
    if (year < 1 || year > 9999) ThrowArgOutOfRange();
    return (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) ? 1 : 0;
}

//------------------------------------------------------------------------------
// System.DateTime -- arithmetic.
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_add_days(Il2CppDateTime dt, double days)
{
    return AddMillis(dt, days, kMillisPerDay);
}

Il2CppDateTime icall_datetime_add_hours(Il2CppDateTime dt, double hours)
{
    return AddMillis(dt, hours, kMillisPerHour);
}

Il2CppDateTime icall_datetime_add_minutes(Il2CppDateTime dt, double minutes)
{
    return AddMillis(dt, minutes, kMillisPerMinute);
}

Il2CppTimeSpan icall_datetime_subtract(Il2CppDateTime left, Il2CppDateTime right)
{
    // Signed difference; both operands are within [0, MaxValue ticks] for
    // valid DateTimes, so no int64 overflow is possible.
    return Il2CppTimeSpan(left.ticks - right.ticks);
}

int32_t icall_datetime_compare_to(Il2CppDateTime left, Il2CppDateTime right)
{
    if (left.ticks < right.ticks) return -1;
    if (left.ticks > right.ticks) return 1;
    return 0;
}

Il2CppObject* icall_datetime_to_string(Il2CppDateTime dt)
{
    // Fixed culture-neutral format "MM/dd/yyyy HH:mm:ss" (zero-padded).
    const CivilTime t = GetCivilTime(dt);
    char buffer[40];
    std::snprintf(buffer, sizeof(buffer), "%02d/%02d/%04d %02d:%02d:%02d",
                  t.month, t.day, t.year, t.hour, t.minute, t.second);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(buffer));
}

//------------------------------------------------------------------------------
// System.TimeSpan.
//------------------------------------------------------------------------------
Il2CppTimeSpan icall_timespan_from_ticks(int64_t ticks)
{
    return Il2CppTimeSpan(ticks);
}

Il2CppTimeSpan icall_timespan_from_hms(int32_t hours, int32_t minutes, int32_t seconds)
{
    // .NET Framework's TimeToTicks: totalSeconds fits int64 for int32 inputs,
    // then bounded to Int64 / TicksPerSecond before scaling to ticks.
    const int64_t totalSeconds = static_cast<int64_t>(hours) * 3600
                               + static_cast<int64_t>(minutes) * 60
                               + static_cast<int64_t>(seconds);
    if (totalSeconds < kMinTotalSeconds || totalSeconds > kMaxTotalSeconds)
    {
        ThrowArgOutOfRange();
    }
    return Il2CppTimeSpan(totalSeconds * kTicksPerSecond);
}

//------------------------------------------------------------------------------
// icall_timespan_from_dhms -- TimeSpan(days, hours, minutes, seconds)
// (batch 152): the net48 TimeToTicks composition.
//------------------------------------------------------------------------------
Il2CppTimeSpan icall_timespan_from_dhms(int32_t days, int32_t hours, int32_t minutes, int32_t seconds)
{
    // net48 TimeToTicks: ((days * 3600 * 24 + hours * 3600 + minutes * 60 +
    // seconds) * 10000000) -- the day term may overflow int32, so the whole
    // sum is int64; the .NET Framework checked-range applies to the total.
    const int64_t totalSeconds = static_cast<int64_t>(days) * 86400
                               + static_cast<int64_t>(hours) * 3600
                               + static_cast<int64_t>(minutes) * 60
                               + static_cast<int64_t>(seconds);
    if (totalSeconds < kMinTotalSeconds || totalSeconds > kMaxTotalSeconds)
    {
        ThrowArgOutOfRange();
    }
    return Il2CppTimeSpan(totalSeconds * kTicksPerSecond);
}

int64_t icall_timespan_get_ticks(Il2CppTimeSpan ts)
{
    return ts.ticks;
}

int32_t icall_timespan_get_days(Il2CppTimeSpan ts)
{
    return static_cast<int32_t>(ts.ticks / kTicksPerDay);
}

int32_t icall_timespan_get_hours(Il2CppTimeSpan ts)
{
    return static_cast<int32_t>((ts.ticks % kTicksPerDay) / kTicksPerHour);
}

int32_t icall_timespan_get_minutes(Il2CppTimeSpan ts)
{
    return static_cast<int32_t>((ts.ticks % kTicksPerHour) / kTicksPerMinute);
}

int32_t icall_timespan_get_seconds(Il2CppTimeSpan ts)
{
    return static_cast<int32_t>((ts.ticks % kTicksPerMinute) / kTicksPerSecond);
}

double icall_timespan_get_total_seconds(Il2CppTimeSpan ts)
{
    return static_cast<double>(ts.ticks) / static_cast<double>(kTicksPerSecond);
}

double icall_timespan_get_total_minutes(Il2CppTimeSpan ts)
{
    return static_cast<double>(ts.ticks) / static_cast<double>(kTicksPerMinute);
}

double icall_timespan_get_total_hours(Il2CppTimeSpan ts)
{
    return static_cast<double>(ts.ticks) / static_cast<double>(kTicksPerHour);
}

double icall_timespan_get_total_days(Il2CppTimeSpan ts)
{
    return static_cast<double>(ts.ticks) / static_cast<double>(kTicksPerDay);
}

double icall_timespan_get_total_milliseconds(Il2CppTimeSpan ts)
{
    return static_cast<double>(ts.ticks) / static_cast<double>(kTicksPerMillisecond);
}

Il2CppTimeSpan icall_timespan_add(Il2CppTimeSpan left, Il2CppTimeSpan right)
{
    return AddTicksChecked(left.ticks, right.ticks);
}

Il2CppTimeSpan icall_timespan_subtract(Il2CppTimeSpan left, Il2CppTimeSpan right)
{
    // left - right == left + (-right); negation of Int64.MinValue is not
    // representable (.NET Framework's unary minus throws OverflowException).
    if (right.ticks == INT64_MIN)
    {
        ThrowOverflow();
    }
    return AddTicksChecked(left.ticks, -right.ticks);
}

Il2CppTimeSpan icall_timespan_negate(Il2CppTimeSpan value)
{
    if (value.ticks == INT64_MIN)
    {
        ThrowOverflow();
    }
    return Il2CppTimeSpan(-value.ticks);
}

int32_t icall_timespan_compare_to(Il2CppTimeSpan left, Il2CppTimeSpan right)
{
    if (left.ticks < right.ticks) return -1;
    if (left.ticks > right.ticks) return 1;
    return 0;
}

} // namespace runtime
} // namespace il2cpp
