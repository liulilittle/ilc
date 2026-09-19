#pragma once
//------------------------------------------------------------------------------
// il2cpp_dtoffset.h -- System.DateTimeOffset runtime support (dtoffset.cpp).
//
// DateTimeOffset is a .NET VALUE type: a UTC instant plus a fixed offset from
// UTC. This runtime represents it as a 16-byte value:
//
//   struct Il2CppDateTimeOffset {
//       int64_t utcTicks;      // the UTC instant (100 ns ticks since
//                              // 0001-01-01T00:00:00 -- the DateTime layout)
//       int32_t offsetMinutes; // the offset from UTC in whole minutes
//   };
//
// The LOCAL time is utcTicks + offsetMinutes * TicksPerMinute; the UTC time
// is utcTicks. This mirrors the .NET Framework layout (m_dateTime = UTC time,
// m_offsetMinutes = offset) with the offset widened to int32 for simplicity.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), the project's reference
// baseline:
//   * The offset must be a whole number of minutes in [-14:00, +14:00]
//     ([-840, +840] minutes); anything else throws ArgumentOutOfRangeException
//     (net48: the ctor validates the offset range and whole-minutes rule).
//   * DateTimeOffset.DateTime is the LOCAL time (UTC + offset); UtcDateTime is
//     the UTC instant; Offset is the TimeSpan of the offset.
//   * The component accessors (Year/Month/Day/Hour/Minute/Second) read the
//     LOCAL time.
//   * CompareTo/Equals compare the UTC instants (two offsets of the same
//     instant are equal).
//
// The icalls take Il2CppDateTimeOffset BY VALUE (16 bytes = two registers on
// x64 -- the managed value-type ABI, like Il2CppDateTime/Il2CppTimeSpan in
// il2cpp_time.h). DateTime/TimeSpan arguments pass by value too.
//------------------------------------------------------------------------------

#include "il2cpp_time.h" // Il2CppDateTime / Il2CppTimeSpan

#include <cstdint>

namespace il2cpp { namespace runtime {

// System.DateTimeOffset's raw 16-byte image (see header comment).
struct Il2CppDateTimeOffset
{
    int64_t utcTicks;      // the UTC instant (100 ns ticks since 0001-01-01)
    int32_t offsetMinutes; // the offset from UTC in whole minutes
};

// DateTimeOffset.GetHashCode (B4 dtoprobe [hc]): net48 returns
// UtcDateTime.GetHashCode() (referencesource mscorlib/system/
// datetimeoffset.cs, mirrored by mono) -- the ticks-fold of the UTC
// instant. Oracle: 2020-01-01 UTC -> -2085908915.
int32_t icall_dtoffset_get_hash_code(Il2CppDateTimeOffset dto);

// --- construction -----------------------------------------------------------

// DateTimeOffset(DateTime localTime, TimeSpan offset): the UTC instant is
// localTime - offset. The offset must be a whole number of minutes in
// [-840, +840] ([-14:00, +14:00]); otherwise ArgumentOutOfRangeException.
// Result-slot-first shape (the decimal ctor precedent): 'out' receives the
// value.
void icall_dtoffset_ctor(Il2CppDateTimeOffset* out, Il2CppDateTime localTime,
                         Il2CppTimeSpan offset);

// DateTimeOffset(int year, int month, int day, int hour, int minute,
//                int second, TimeSpan offset): builds the LOCAL time from the
// components (validated like the DateTime ctor) then applies the offset
// (same whole-minutes/range rule). Result-slot-first shape.
void icall_dtoffset_ctor_ymd(Il2CppDateTimeOffset* out, int32_t year,
                             int32_t month, int32_t day, int32_t hour,
                             int32_t minute, int32_t second,
                             Il2CppTimeSpan offset);

// DateTimeOffset.UtcNow (offset zero; the UTC instant).
Il2CppDateTimeOffset icall_dtoffset_utc_now();

// --- accessors --------------------------------------------------------------

// DateTimeOffset.DateTime: the LOCAL time (UTC + offset) as a DateTime value.
Il2CppDateTime icall_dtoffset_get_datetime(Il2CppDateTimeOffset dto);

// DateTimeOffset.UtcDateTime: the UTC instant as a DateTime value.
Il2CppDateTime icall_dtoffset_get_utc_datetime(Il2CppDateTimeOffset dto);

// DateTimeOffset.Offset: the offset as a TimeSpan value.
Il2CppTimeSpan icall_dtoffset_get_offset(Il2CppDateTimeOffset dto);

// Component accessors of the LOCAL time (net48 semantics).
int32_t icall_dtoffset_get_year(Il2CppDateTimeOffset dto);
int32_t icall_dtoffset_get_month(Il2CppDateTimeOffset dto);
int32_t icall_dtoffset_get_day(Il2CppDateTimeOffset dto);
int32_t icall_dtoffset_get_hour(Il2CppDateTimeOffset dto);
int32_t icall_dtoffset_get_minute(Il2CppDateTimeOffset dto);
int32_t icall_dtoffset_get_second(Il2CppDateTimeOffset dto);

// --- comparison / equality --------------------------------------------------

// CompareTo(DateTimeOffset): -1 / 0 / 1 comparing the UTC instants.
int32_t icall_dtoffset_compare(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b);

// Equals(DateTimeOffset): true iff the UTC instants are equal (offsets may
// differ).
bool icall_dtoffset_equals(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b);

// --- formatting -------------------------------------------------------------

// ToString(): the net48 parameterless default -- the SELECTED culture's
// General "G" pattern plus " zzz" (the offset), e.g. host-default zh-CN
// "2020/6/15 13:45:30 +08:00" (the dtoffsetfmt oracle). Delegates to the
// format icall with a null format and provider.
Il2CppObject* icall_dtoffset_to_string(Il2CppDateTimeOffset dto);

// ToString(string format[, IFormatProvider]): the net48 DateTimeOffset
// contract (pinned by the dtoffsetfmt oracle, tests/cases/dtoffsetfmt):
//   * null / empty format -> the provider-selected culture's General "G"
//     pattern plus " zzz" (the parameterless ToString path);
//   * standard specifiers d/D/t/T/f/F/g/G/m/M/y/Y -> the culture pattern
//     WITHOUT the offset; s -> local sortable; u/r/R -> the UTC instant;
//     o/O -> local round-trip "yyyy-MM-ddTHH:mm:ss.fffffff" plus the
//     always-numeric offset ("+00:00" at zero);
//   * "U" throws FormatException (net48 DateTimeOffset does not support it),
//     as does any unknown single-letter specifier;
//   * custom formats expand the offset tokens z -> "+8", zz -> "+08",
//     zzz -> "+08:00" and K -> "+08:00" (plus the net48 '%' single-character
//     escape); every other character is a literal.
// The provider argument follows the DateTime routing (nullptr -> host
// default zh-CN, empty culture name -> invariant, "zh-CN" -> zh-CN, other ->
// invariant fallback). Throws FormatException for invalid formats.
Il2CppObject* icall_dtoffset_to_string_format(Il2CppDateTimeOffset dto, Il2CppObject* format,
                                              Il2CppObject* provider);

}} // namespace il2cpp::runtime
