#pragma once
//------------------------------------------------------------------------------
// il2cpp_time.h -- System.DateTime / System.TimeSpan runtime support.
//
// DateTime is a .NET VALUE type. This runtime keeps the tick count (100 ns
// units since 0001-01-01T00:00:00) in a dedicated field plus the
// DateTimeKind, so the struct is NOT the raw managed image -- the managed
// 8-byte array image packs the 2-bit kind into the TOP bits of the same
// 64-bit word (see the DateTime[] element helpers below):
//
//   struct Il2CppDateTime { int64_t ticks; int32_t kind; };
//   struct Il2CppTimeSpan { int64_t ticks; };   // signed 100 ns units
//
// Both are passed BY VALUE (the managed value-type ABI: one register/stack
// slot for TimeSpan; DateTime rides two eightbyte slots because of the extra
// kind member; no heap, no GC involvement).
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

// DateTimeKind: Unspecified=0, Utc=1, Local=2 (System.DateTimeKind).
struct Il2CppDateTime
{
    int64_t ticks;
    int32_t kind; // default 0 = Unspecified (net48 part-ctors / zone-less Parse)

    constexpr Il2CppDateTime() : ticks(0), kind(0) {}
    explicit constexpr Il2CppDateTime(int64_t value) : ticks(value), kind(0) {}
    constexpr Il2CppDateTime(int64_t value, int32_t k) : ticks(value), kind(k) {}
};

// TimeSpan value: signed 100 ns tick count (a DateTime difference).
struct Il2CppTimeSpan
{
    int64_t ticks;

    constexpr Il2CppTimeSpan() : ticks(0) {}
    explicit constexpr Il2CppTimeSpan(int64_t value) : ticks(value) {}
};

//------------------------------------------------------------------------------
// DateTime[] element image (LOUD P0-02).
//
// A DateTime ARRAY element is the CLR 8-byte value: 62 bits of ticks in the
// low bits plus the 2-bit DateTimeKind in the top bits (the .NET _dateData
// packing). The runtime value struct keeps kind in its own member, so the
// transpiler's array accessor arms pack/unpack here. The mask keeps the
// kind bits from leaking back into the tick count and the kind never gets
// silently dropped on an array round-trip.
//------------------------------------------------------------------------------
constexpr int64_t kDateTimeArrayTickMask = 0x3FFFFFFFFFFFFFFFLL;

inline int64_t il2cpp_datetime_to_array_bits(const Il2CppDateTime& value)
{
    return static_cast<int64_t>(
        (static_cast<uint64_t>(value.ticks) & static_cast<uint64_t>(kDateTimeArrayTickMask))
        | ((static_cast<uint64_t>(value.kind) & 0x3ULL) << 62));
}

inline Il2CppDateTime il2cpp_datetime_from_array_bits(int64_t bits)
{
    const uint64_t raw = static_cast<uint64_t>(bits);
    return Il2CppDateTime(
        static_cast<int64_t>(raw & static_cast<uint64_t>(kDateTimeArrayTickMask)),
        static_cast<int32_t>((raw >> 62) & 0x3ULL));
}

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
// bcldeep: DateTime(y,m,d,h,mi,s,DateTimeKind) -- the kind argument is STORED
// (net48: ticks stay raw; kind must be 0..2, else ArgumentException "kind").
Il2CppDateTime icall_datetime_from_ymdhms_kind(int32_t year, int32_t month, int32_t day,
                                               int32_t hour, int32_t minute, int32_t second,
                                               int32_t kind);
// DateTime(y,m,d,h,mi,s,ms): the 7-arg millisecond ctor (D5 cov3); the ms
// component (0..999) composes onto the second boundary.
Il2CppDateTime icall_datetime_from_ymdhms_ms(int32_t year, int32_t month, int32_t day,
                                             int32_t hour, int32_t minute, int32_t second,
                                             int32_t millisecond);
// dtprov1: DateTime(y,m,d,h,mi,s,ms,DateTimeKind) -- the 8-arg millisecond+
// kind ctor (ms 0..999 and kind 0..2 validated; the kind is STORED).
Il2CppDateTime icall_datetime_from_ymdhms_ms_kind(int32_t year, int32_t month, int32_t day,
                                                 int32_t hour, int32_t minute, int32_t second,
                                                 int32_t millisecond, int32_t kind);

//------------------------------------------------------------------------------
// DateTime(long ticks) -- batch 151: the raw 100ns-since-0001-01-01 ticks
// (validated against the net48 range [0, kMaxDateTimeTicks]).
//------------------------------------------------------------------------------
Il2CppDateTime icall_datetime_from_ticks(int64_t ticks);
// bcldeep: DateTime(long ticks, DateTimeKind) -- raw ticks + the stored kind
// (kind validation as the ymdhms kind ctor above).
Il2CppDateTime icall_datetime_from_ticks_kind(int64_t ticks, int32_t kind);

//------------------------------------------------------------------------------
// The FILETIME bridge (100 ns ticks since 1601-01-01 UTC). The Utc pair is
// the plain 1601-epoch shift; the Local pair converts through the machine
// zone's PER-DATE (DST-aware) offset, matching net48's measured contract:
//   FromFileTime      : UTC instant -> local wall clock, Kind = Local;
//                       the shifted result CLAMPS to [MinValue, MaxValue].
//   ToFileTime        : net48 is ToUniversalTime().ToFileTimeUtc(), and
//                       ToUniversalTime treats BOTH Local and Unspecified as
//                       local wall clock, so both are converted to UTC first;
//                       only Kind=Utc keeps its ticks.
//   ToFileTimeUtc     : Kind=Local is converted to UTC first (net48's
//                       ToFileTimeUtc is NOT the identity on Local values).
// A negative FILETIME input or a pre-1601 result throws
// ArgumentOutOfRangeException (paramName "fileTime" for the From forms).
//------------------------------------------------------------------------------
// ToFileTimeUtc: Kind-aware (see above); a result below zero throws
// ArgumentOutOfRangeException.
int64_t icall_datetime_to_file_time_utc(Il2CppDateTime dt);
// FromFileTimeUtc: the inverse conversion (Kind = Utc); out-of-range input
// throws ArgumentOutOfRangeException.
Il2CppDateTime icall_datetime_from_file_time_utc(int64_t fileTime);
// ToFileTime: the LOCAL-flavoured form (Kind=Local -> UTC -> epoch shift).
int64_t icall_datetime_to_file_time(Il2CppDateTime dt);
// FromFileTime: the LOCAL-flavoured inverse (Kind = Local).
Il2CppDateTime icall_datetime_from_file_time(int64_t fileTime);

// DateTime.GetHashCode (D1 diffdt001 dt.hash): net48 ticks-fold --
// unchecked((int)ticks) ^ (int)(ticks >> 32) (referencesource
// mscorlib/system/datetime.cs). Oracle: D6 ticks 637185764960000000 ->
// -1061690093 (exact-integer verified, powershell float-division
// misled once: hi=148356371, not 148356372).
int32_t icall_datetime_get_hash_code(Il2CppDateTime dt);

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
// impl-tz2: DateTime.Today -- Now's date part with the Kind=Local flavor
// preserved (net48 Today == Now.Date, referencesource datetime.cs:1262).
Il2CppDateTime icall_datetime_today();
// Equals: pure ticks equality.
bool icall_datetime_equals(Il2CppDateTime left, Il2CppDateTime right);

//------------------------------------------------------------------------------
// impl-tz2 -- the DateTimeExtra analyzable surface (MethodMap.DateTimeExtra).
//------------------------------------------------------------------------------
// DateTime.ToOADate() -> double: the net48 TicksToOADate algorithm
// (referencesource datetime.cs:1346-1361) -- the 1899-12-30 epoch shift,
// the VB 1/1/0001 fixup, and the OverflowException below OADateMinAsTicks.
double icall_datetime_to_oa_date(Il2CppDateTime dt);
// DateTime.FromOADate(double) -> DateTime (Kind=Unspecified): the net48
// DoubleDateToTicks algorithm (datetime.cs:1300-1329, referenced from
// FromOADate at datetime.cs:779) with its ArgumentException range walls.
Il2CppDateTime icall_datetime_from_oa_date(double value);
// DateTime.ToBinary() -> int64 (net48 datetime.cs:808-831): a Kind=Local
// value stores ticks - machineOffset under the Local flag bit (negative
// ticks wrap above TicksCeiling); the other kinds return the raw
// ticks|kind-bits dateData image.
int64_t icall_datetime_to_binary(Il2CppDateTime dt);
// DateTime.FromBinary(int64) -> DateTime (net48 datetime.cs:700-742): the
// Local flag reverses the ToBinary transform (per-date machine offset at
// the stored UTC instant, the day wrap for negative ticks, the
// ArgumentException range wall); the other flavors restore ticks + kind.
Il2CppDateTime icall_datetime_from_binary(int64_t dateData);
// DateTime.IsDaylightSavingTime() (net48 datetime.cs:797-802): a Kind=Utc
// input is 0; anything else evaluates the per-date machine DST window.
int32_t icall_datetime_is_daylight_saving(Il2CppDateTime dt);
// DateTime.GetTypeCode() -> TypeCode.DateTime == 16 (referencesource
// mscorlib/system/typecode.cs:44).
int32_t icall_datetime_get_type_code(Il2CppDateTime dt);

// TimeSpan.ToString(): the constant "c" format (see time.cpp).
Il2CppObject* icall_timespan_to_string(Il2CppTimeSpan ts);

// TimeSpan.ToString(format) / ToString(format, IFormatProvider): the net48
// standard "c"/"g"/"G"/"t"/"T" layouts and the custom d/h/m/s/f/F token set
// (see the time.cpp banner). A null/empty format selects "c"; anything
// outside the documented subset throws FormatException. 'format' may be null
// (the provider is accepted for ABI symmetry and ignored -- every TimeSpan
// layout is culture-invariant).
Il2CppObject* icall_timespan_to_string_format(Il2CppTimeSpan ts,
                                              Il2CppObject* format);

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
