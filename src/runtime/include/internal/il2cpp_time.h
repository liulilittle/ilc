#pragma once
//------------------------------------------------------------------------------
// il2cpp_time.h -- System.DateTime / System.TimeSpan runtime support.
//
// DateTime is a .NET VALUE type. This runtime represents it as a plain 64-bit
// tick count (100 ns units since 0001-01-01T00:00:00), exactly matching the
// managed layout (the managed struct is { long ticks } -- blittable):
//
//   struct Il2CppDateTime { int64_t ticks; };   // ticks since 0001-01-01
//   struct Il2CppTimeSpan { int64_t ticks; };   // signed 100 ns units
//
// Both are 8-byte values passed BY VALUE (one register/stack slot on x64,
// identical to the managed value-type ABI; no heap, no GC involvement).
//
// The icall set mirrors the transpiler's BCL mapping for System.DateTime /
// System.TimeSpan: clock accessors (Now / UtcNow), construction, component
// accessors (Year..Second / DayOfWeek), arithmetic (AddDays/Hours/Minutes /
// Subtract / Add / Subtract / Negate), comparison (CompareTo), and
// DateTime.ToString(). Every function is a plain C++ function in namespace
// il2cpp::runtime; generated code resolves the BCL method to these names by
// convention, exactly like the Console / Marshal / Math icalls.
//
// Error contract: out-of-range arguments throw the managed exception via
// il2cpp_throw_new (ArgumentOutOfRangeException / OverflowException) -- see
// il2cpp_exception.h. The implementation (src/time.cpp) is cross-platform:
// std::chrono::system_clock for UTC, <ctime> localtime/gmtime for the local
// offset, no Windows-only headers.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// DateTime value: 100 ns ticks since 0001-01-01T00:00:00 (UTC semantics).
struct Il2CppDateTime
{
    int64_t ticks;

    constexpr Il2CppDateTime() : ticks(0) {}
    explicit constexpr Il2CppDateTime(int64_t value) : ticks(value) {}
};

// TimeSpan value: signed 100 ns tick count (a DateTime difference).
struct Il2CppTimeSpan
{
    int64_t ticks;

    constexpr Il2CppTimeSpan() : ticks(0) {}
    explicit constexpr Il2CppTimeSpan(int64_t value) : ticks(value) {}
};

//------------------------------------------------------------------------------
// System.DateTime -- clock accessors.
//------------------------------------------------------------------------------
// DateTime.UtcNow: the current UTC instant (std::chrono::system_clock).
Il2CppDateTime icall_datetime_utc_now();
// DateTime.Now: the current LOCAL instant (UtcNow + local timezone offset).
Il2CppDateTime icall_datetime_now();

//------------------------------------------------------------------------------
// System.DateTime -- construction (validated like .NET Framework; any
// out-of-range component throws ArgumentOutOfRangeException).
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_from_ymd(int32_t year, int32_t month, int32_t day);
Il2CppDateTime icall_datetime_from_ymdhms(int32_t year, int32_t month, int32_t day,
                                          int32_t hour, int32_t minute, int32_t second);

//------------------------------------------------------------------------------
// DateTime(long ticks) -- batch 151: the raw 100ns-since-0001-01-01 ticks
// (validated against the net48 range [0, kMaxDateTimeTicks]).
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_from_ticks(int64_t ticks);

// ToFileTimeUtc: ticks + the 1601-epoch offset (see time.cpp); a result
// below zero throws ArgumentOutOfRangeException.
int64_t icall_datetime_to_file_time_utc(Il2CppDateTime dt);
// FromFileTimeUtc: the inverse conversion; out-of-range input throws
// ArgumentOutOfRangeException.
Il2CppDateTime icall_datetime_from_file_time_utc(int64_t fileTime);

//------------------------------------------------------------------------------
// System.DateTime -- component accessors (Year/Month/Day/Hour/Minute/Second).
//------------------------------------------------------------------------------
int32_t icall_datetime_get_year(Il2CppDateTime dt);
int32_t icall_datetime_get_month(Il2CppDateTime dt);
int32_t icall_datetime_get_day(Il2CppDateTime dt);
int32_t icall_datetime_get_day_of_year(Il2CppDateTime dt);
int32_t icall_datetime_get_hour(Il2CppDateTime dt);
int32_t icall_datetime_get_minute(Il2CppDateTime dt);
int32_t icall_datetime_get_second(Il2CppDateTime dt);

//------------------------------------------------------------------------------
// DateTime.Ticks -- batch 151: the raw 100ns-since-0001-01-01 ticks.
//------------------------------------------------------------------------------
int64_t icall_datetime_get_ticks(Il2CppDateTime dt);
// DayOfWeek: int32 encoding, Sunday = 0 .. Saturday = 6 (the .NET enum).
int32_t icall_datetime_get_dayofweek(Il2CppDateTime dt);

//------------------------------------------------------------------------------
// System.DateTime -- extended component accessors (net48 audit medium-priority
// batch: get_Ticks / get_Millisecond / get_DayOfYear).
//------------------------------------------------------------------------------
// DateTime.Ticks: the raw 64-bit tick count (100 ns units since
// 0001-01-01T00:00:00) -- the net48 property masks the kind bits and exposes
// the raw count, which is exactly this runtime's Il2CppDateTime.ticks.
int64_t icall_datetime_get_ticks(Il2CppDateTime dt);

// DateTime.Millisecond: 0..999 -- (ticks / TicksPerMillisecond) % 1000
// (net48: UTicks / TicksPerMillisecond % 1000).
int32_t icall_datetime_get_millisecond(Il2CppDateTime dt);

// DateTime.DayOfYear: 1..366 -- the day number within the year (days since
// Jan 1 of the same year plus one; leap-aware through the civil calendar).
int32_t icall_datetime_get_dayofyear(Il2CppDateTime dt);

//------------------------------------------------------------------------------
// System.DateTime -- static date helpers (net48: DaysInMonth / IsLeapYear).
//------------------------------------------------------------------------------
// DateTime.DaysInMonth(year, month): 28..31. month must be 1..12 and year
// must be 1..9999, otherwise ArgumentOutOfRangeException (net48: the month
// range is checked first, then IsLeapYear validates the year).
int32_t icall_datetime_days_in_month(int32_t year, int32_t month);

// DateTime.IsLeapYear(year): 1 if year is a Gregorian leap year (divisible by
// 4, except centuries, except every 400 years), else 0. year must be 1..9999,
// otherwise ArgumentOutOfRangeException. int32 ABI (1/0 -- the debuggerext.h
// bool convention).
int32_t icall_datetime_is_leap_year(int32_t year);

//------------------------------------------------------------------------------
// System.DateTime -- arithmetic. Each returns a NEW DateTime. The double
// argument is rounded to the nearest millisecond exactly like .NET
// Framework's Add(value, scale); an out-of-range result throws
// ArgumentOutOfRangeException.
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_add_days(Il2CppDateTime dt, double days);
Il2CppDateTime icall_datetime_add_hours(Il2CppDateTime dt, double hours);
// Civil-calendar arithmetic: the day clamps to the target month's length;
// the time-of-day is preserved exactly (see time.cpp).
Il2CppDateTime icall_datetime_add_months(Il2CppDateTime dt, int32_t months);
Il2CppDateTime icall_datetime_add_years(Il2CppDateTime dt, int32_t years);
Il2CppDateTime icall_datetime_add_minutes(Il2CppDateTime dt, double minutes);
Il2CppDateTime icall_datetime_add_seconds(Il2CppDateTime dt, double seconds);
Il2CppDateTime icall_datetime_add_milliseconds(Il2CppDateTime dt, double milliseconds);
// Kind support (single-zone doctrine): every DateTime is Utc; SpecifyKind
// is the identity on ticks.
int32_t icall_datetime_get_kind(Il2CppDateTime dt);
Il2CppDateTime icall_datetime_specify_kind(Il2CppDateTime dt, int32_t kind);
// get_Date: the date part at midnight; get_TimeOfDay: the time since
// midnight as a TimeSpan.
Il2CppDateTime icall_datetime_get_date(Il2CppDateTime dt);
Il2CppTimeSpan icall_datetime_get_time_of_day(Il2CppDateTime dt);
// Equals: pure ticks equality.
bool icall_datetime_equals(Il2CppDateTime left, Il2CppDateTime right);

// TimeSpan.ToString(): the constant "c" format (see time.cpp).
Il2CppObject* icall_timespan_to_string(Il2CppTimeSpan ts);

// DateTime.Subtract(DateTime) -> TimeSpan (signed tick difference).
Il2CppTimeSpan icall_datetime_subtract(Il2CppDateTime left, Il2CppDateTime right);
// DateTime.CompareTo(DateTime): -1 / 0 / +1.
int32_t icall_datetime_compare_to(Il2CppDateTime left, Il2CppDateTime right);

// The relational operators: pure ticks comparison (see time.cpp).
bool icall_datetime_op_equality(Il2CppDateTime left, Il2CppDateTime right);
bool icall_datetime_op_inequality(Il2CppDateTime left, Il2CppDateTime right);
bool icall_datetime_op_less_than(Il2CppDateTime left, Il2CppDateTime right);
bool icall_datetime_op_greater_than(Il2CppDateTime left, Il2CppDateTime right);
bool icall_datetime_op_less_or_equal(Il2CppDateTime left, Il2CppDateTime right);
bool icall_datetime_op_greater_or_equal(Il2CppDateTime left, Il2CppDateTime right);
// DateTime.ToString(): fixed culture-neutral "MM/dd/yyyy HH:mm:ss" string.
Il2CppObject* icall_datetime_to_string(Il2CppDateTime dt);

//------------------------------------------------------------------------------
// System.TimeSpan.
//------------------------------------------------------------------------------
// TimeSpan(long ticks): raw construction (no validation, like .NET).
Il2CppTimeSpan icall_timespan_from_ticks(int64_t ticks);
// TimeSpan(int hours, int minutes, int seconds): validated against the long
// range (ArgumentOutOfRangeException on overflow, .NET TimeToTicks rules).
Il2CppTimeSpan icall_timespan_from_hms(int32_t hours, int32_t minutes, int32_t seconds);
Il2CppTimeSpan icall_timespan_from_minutes(double minutes);
Il2CppTimeSpan icall_timespan_from_hours(double hours);
Il2CppTimeSpan icall_timespan_from_days(double days);
Il2CppTimeSpan icall_timespan_from_seconds(double seconds);
// FromMilliseconds / FromTicks (see time.cpp).
Il2CppTimeSpan icall_timespan_from_milliseconds(double milliseconds);
Il2CppTimeSpan icall_timespan_from_ticks_exact(int64_t ticks);

//------------------------------------------------------------------------------
// TimeSpan(int days, int hours, int minutes, int seconds) -- batch 152: the
// net48 TimeToTicks composition (bounded like from_hms).
//------------------------------------------------------------------------------
Il2CppTimeSpan icall_timespan_from_dhms(int32_t days, int32_t hours, int32_t minutes, int32_t seconds);
// TimeSpan(d, h, m, s, ms): the 5-arg ctor (see time.cpp).
Il2CppTimeSpan icall_timespan_from_dhmsm(int32_t days, int32_t hours, int32_t minutes,
                                         int32_t seconds, int32_t millis);

int64_t icall_timespan_get_ticks(Il2CppTimeSpan ts);
int32_t icall_timespan_get_days(Il2CppTimeSpan ts);
int32_t icall_timespan_get_hours(Il2CppTimeSpan ts);
int32_t icall_timespan_get_minutes(Il2CppTimeSpan ts);
int32_t icall_timespan_get_seconds(Il2CppTimeSpan ts);
double icall_timespan_get_total_seconds(Il2CppTimeSpan ts);
// Total* family: pure arithmetic on ticks (.NET: ticks / TicksPer* as double).
double icall_timespan_get_total_minutes(Il2CppTimeSpan ts);
double icall_timespan_get_total_hours(Il2CppTimeSpan ts);
double icall_timespan_get_total_days(Il2CppTimeSpan ts);
double icall_timespan_get_total_milliseconds(Il2CppTimeSpan ts);

// Milliseconds: the 0..999 sub-second component.
int32_t icall_timespan_get_milliseconds(Il2CppTimeSpan ts);

// The TimeSpan relational operators: pure ticks comparison.
bool icall_timespan_op_equality(Il2CppTimeSpan left, Il2CppTimeSpan right);
bool icall_timespan_op_inequality(Il2CppTimeSpan left, Il2CppTimeSpan right);
bool icall_timespan_op_less_than(Il2CppTimeSpan left, Il2CppTimeSpan right);
bool icall_timespan_op_greater_than(Il2CppTimeSpan left, Il2CppTimeSpan right);
bool icall_timespan_op_less_or_equal(Il2CppTimeSpan left, Il2CppTimeSpan right);
bool icall_timespan_op_greater_or_equal(Il2CppTimeSpan left, Il2CppTimeSpan right);

// Duration: the absolute value (OverflowException at Int64.MinValue).
Il2CppTimeSpan icall_timespan_duration(Il2CppTimeSpan value);

// GetHashCode: the int64 hash of the raw ticks; Equals: pure ticks equality.
int32_t icall_timespan_get_hash_code(Il2CppTimeSpan ts);
bool icall_timespan_equals(Il2CppTimeSpan left, Il2CppTimeSpan right);

// TimeSpan.Add / Subtract / Negate: OverflowException when the result is not
// representable (sign-of-result test for Add, Int64.MinValue for Negate --
// both mirroring .NET Framework).
Il2CppTimeSpan icall_timespan_add(Il2CppTimeSpan left, Il2CppTimeSpan right);
Il2CppTimeSpan icall_timespan_subtract(Il2CppTimeSpan left, Il2CppTimeSpan right);
Il2CppTimeSpan icall_timespan_negate(Il2CppTimeSpan value);
// TimeSpan.CompareTo: -1 / 0 / +1.
int32_t icall_timespan_compare_to(Il2CppTimeSpan left, Il2CppTimeSpan right);

} // namespace runtime
} // namespace il2cpp
