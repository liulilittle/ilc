#pragma once
//------------------------------------------------------------------------------
// il2cpp_time_fmt.h -- date/time formatting engine shared by the DateTime and
// DateTimeOffset ToString(format[, provider]) icalls (implementation
// timeext.cpp; DateTimeOffset entry icall_dtoffset_to_string_format in
// dtoffset.cpp).
//
// SCOPE: the provider -> culture mapping, the civil-component extraction and
// the custom-pattern engine used by icall_datetime_to_string_format
// (timeext.cpp) and icall_dtoffset_to_string_format (dtoffset.cpp). The
// DateTimeOffset path passes the offset minutes into FormatPattern so the
// z / zz / zzz / K tokens expand (net48); DateTime passes nullptr and keeps
// the documented "timezone specifiers are literals" divergence.
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

// Civil components / .NET DayOfWeek (Sunday = 0) of a raw DateTime value.
CivilTime CivilTimeFromTicks(Il2CppDateTime dt);
int32_t WeekdayFromTicks(Il2CppDateTime dt);

// net48 standard single-letter pattern table (custom-engine token strings).
// Returns nullptr for specifiers that are not a plain culture pattern:
// 'U' is context specific (DateTime converts to UTC first; DateTimeOffset
// throws) and unknown letters throw FormatException at the call site.
const char16_t* LookupStandardPattern(char16_t spec, DateCulture culture);

// Custom-format engine. offsetMinutes != nullptr enables the DateTimeOffset
// z / zz / zzz / K numeric offset tokens; nullptr is the DateTime behaviour
// (those characters are emitted literally). '%' makes the next character a
// single custom specifier (net48: "%d" -> day digits, "%z" -> "+8").
std::u16string FormatPattern(const CivilTime& t, int32_t dow,
                             const char16_t* fmt, int32_t len,
                             const int32_t* offsetMinutes);

// Appends the net48 "+HH:mm" / "-HH:mm" offset text (the zzz token body; also
// the trailing piece of the DateTimeOffset default "G zzz" and "o"/"O").
void AppendOffsetZzz(std::u16string& out, int32_t offsetMinutes);

} // namespace runtime
} // namespace il2cpp
