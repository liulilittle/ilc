#pragma once
//------------------------------------------------------------------------------
// il2cpp_time_fmt.h -- date/time formatting engine shared by the DateTime and
// DateTimeOffset ToString(format[, provider]) icalls (implementation
// timeext.cpp; DateTimeOffset entry icall_dtoffset_to_string_format in
// dtoffset.cpp).
//
// SCOPE: the provider -> culture mapping, the civil-component extraction and
// the custom-pattern engine used by icall_datetime_to_string_format
// (timeext.cpp) and icall_dtoffset_to_string_format (dtoffset.cpp).
//
// The custom engine expands the FULL net48 DateTime specifier set for BOTH
// value types (pinned by the dtz1 oracle):
//   y/M/d/H/h/m/s/f/F/t/g  -- civil components, AM/PM designator, era name
//   z/zz/zzzz+             -- numeric UTC offset of the LOCAL zone
//   K                      -- DateTime: "" (Unspecified) / "Z" (Utc) /
//                             "+HH:mm" (Local); DateTimeOffset: "+HH:mm"
// A '%' forces the NEXT character to be read as a single custom specifier.
// Every other character is a literal; a SINGLE-character format string is a
// standard-specifier lookup instead, so an unknown one throws FormatException
// (net48) -- see the call sites.
//
// Provider routing (cultureprobe regression fix; see timeext.cpp):
//   * nullptr -> host default zh-CN (the saudit5 oracle);
//   * empty culture name (the InvariantCulture carrier) -> invariant patterns;
//   * "zh-CN" -> zh-CN patterns;
//   * any other name -> invariant fallback (optionally logged).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_time.h" // Il2CppDateTime

#include <cstdint>
#include <string>

namespace il2cpp {
namespace runtime {

// The two standard-pattern families this runtime implements. zh-CN is the
// host default (saudit5 oracle); invariant is the pre-saudit5 contract
// (culturedeep/timeformat/datetime2 pass CultureInfo.InvariantCulture -- its
// carrier is the EMPTY culture name).
enum class DateCulture
{
    Invariant,
    ZhCn,
};

// Resolves the provider carrier to a pattern family (see the header banner).
DateCulture ResolveProviderCulture(Il2CppObject* provider);

// Civil components of a DateTime (valid dates have non-negative ticks).
struct CivilTime
{
    int32_t year;
    int32_t month;
    int32_t day;
    int32_t hour;
    int32_t minute;
    int32_t second;
    int32_t fraction; // sub-second ticks, 0..9999999 (7 fraction digits)
};

// The zone context of a format call:
//   offsetMinutes -- the numeric UTC offset used by the z / zz / zzz tokens;
//   kind          -- DateTimeKind (0 Unspecified, 1 Utc, 2 Local) for the
//                    DateTime K token, or -1 for DateTimeOffset (K is always
//                    the numeric "+HH:mm").
// nullptr means "no zone context": z / K are plain literals (only the
// standard culture patterns take that path -- they never contain them).
struct DateTimeZoneInfo
{
    int32_t offsetMinutes;
    int32_t kind;
};

// Civil components / .NET DayOfWeek (Sunday = 0) of a raw DateTime value.
CivilTime CivilTimeFromTicks(Il2CppDateTime dt);
int32_t WeekdayFromTicks(Il2CppDateTime dt);

// net48 standard single-letter pattern table (custom-engine token strings).
// Returns nullptr for specifiers that are not a plain culture pattern:
// 'U' is context specific (DateTime converts to UTC first; DateTimeOffset
// throws) and unknown letters throw FormatException at the call site.
const char16_t* LookupStandardPattern(char16_t spec, DateCulture culture);

// Custom-format engine. See the banner above for the token set. 'zone'
// enables the z / K offset tokens (nullptr keeps them literal). 'culture'
// selects the era name (g/gg) and the AM/PM designator (t/tt).
std::u16string FormatPattern(const CivilTime& t, int32_t dow,
                             const char16_t* fmt, int32_t len,
                             const DateTimeZoneInfo* zone, DateCulture culture);

// Appends the net48 "+HH:mm" / "-HH:mm" offset text (the zzz token body; also
// the trailing piece of the DateTimeOffset default "G zzz" and "o"/"O").
void AppendOffsetZzz(std::u16string& out, int32_t offsetMinutes);

} // namespace runtime
} // namespace il2cpp
