//------------------------------------------------------------------------------
// dtoffset.cpp -- System.DateTimeOffset runtime support (see il2cpp_dtoffset.h).
//
// A DateTimeOffset is a UTC instant plus a fixed offset from UTC. The
// component accessors and ToString reuse the existing System.DateTime domain
// (il2cpp_time.h icalls) on the LOCAL time (UTC + offset); the offset
// validation follows the net48 ctor rule (whole minutes in [-14:00, +14:00]).
//------------------------------------------------------------------------------

#include "il2cpp_dtoffset.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_string.h"        // il2cpp_string_new
#include "il2cpp_time_fmt.h"      // shared date/time formatting engine
#include "timezone.h"             // il2cpp_timezone_get_utc_offset (Now)

#include <cstdint>
#include <string>

namespace il2cpp { namespace runtime {

namespace {

constexpr int64_t kTicksPerMinute = 600000000LL; // 60 s * 10^7 ticks/s
constexpr int64_t kTicksPerSecond = 10000000LL;
constexpr int64_t kTicksPerMillisecond = 10000LL;
constexpr int64_t kMaxTicks = 3155378975999999999LL; // 9999-12-31T23:59:59.9999999
constexpr int32_t kMinOffsetMinutes = -840;      // -14:00
constexpr int32_t kMaxOffsetMinutes = 840;       // +14:00

// Validate the offset (net48 ctor rule): a whole number of minutes in
// [-14:00, +14:00]. Throws ArgumentOutOfRangeException on violation.
int32_t ValidateOffset(Il2CppTimeSpan offset)
{
    if (offset.ticks % kTicksPerMinute != 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "offset", "The offset must be a whole number of minutes.");
    }
    const int64_t minutes = offset.ticks / kTicksPerMinute;
    if (minutes < kMinOffsetMinutes || minutes > kMaxOffsetMinutes)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "offset", "The offset must be within plus or minus 14 hours.");
    }
    return static_cast<int32_t>(minutes);
}

// Validate the UTC instant (net48 DateTimeOffset range [0001-01-01,
// 9999-12-31]); throws ArgumentOutOfRangeException on violation.
void ValidateUtcTicks(int64_t utcTicks)
{
    if (utcTicks < 0 || utcTicks > kMaxTicks)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "value",
            "The UTC time represented when the offset is applied must be between year 0 and 10,000.");
    }
}

Il2CppDateTimeOffset MakeDto(int64_t utcTicks, int32_t offsetMinutes)
{
    ValidateUtcTicks(utcTicks);
    // nb9 (M11): net48's arithmetic computes through ClockDateTime, so the
    // LOCAL clock must also stay inside [0001-01-01, 9999-12-31]; otherwise
    // ArgumentOutOfRangeException. (The CTOR keeps its own UTC-only rule --
    // net48 validates the ctor differently, so this check is deliberately
    // NOT applied there.)
    const int64_t local = utcTicks + static_cast<int64_t>(offsetMinutes) * kTicksPerMinute;
    if (local < 0 || local > kMaxTicks)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "value",
            "The UTC time represented when the offset is applied must be between year 0 and 10,000.");
    }
    Il2CppDateTimeOffset result;
    result.utcTicks = utcTicks;
    result.offsetMinutes = offsetMinutes;
    return result;
}

int64_t OffsetTicks(const Il2CppDateTimeOffset& dto)
{
    return static_cast<int64_t>(dto.offsetMinutes) * kTicksPerMinute;
}

} // namespace

//------------------------------------------------------------------------------
// Construction.
//------------------------------------------------------------------------------
void icall_dtoffset_ctor(Il2CppDateTimeOffset* out, Il2CppDateTime localTime,
                         Il2CppTimeSpan offset)
{
    const int32_t minutes = ValidateOffset(offset);
    const int64_t utcTicks = localTime.ticks - offset.ticks;
    // net48 ctor rule: the UTC instant must be in [0001-01-01, 9999-12-31]
    // (DateTime.MinValue/MaxValue.Ticks). DateTime.MinValue + a positive
    // offset underflows -> ArgumentOutOfRangeException("offset") (dtaudit4).
    if (utcTicks < 0 || utcTicks > kMaxTicks)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "offset",
            "The UTC time represented when the offset is applied must be between year 0 and 10,000.");
    }
    out->utcTicks = utcTicks;
    out->offsetMinutes = minutes;
}

void icall_dtoffset_ctor_ymd(Il2CppDateTimeOffset* out, int32_t year,
                             int32_t month, int32_t day, int32_t hour,
                             int32_t minute, int32_t second,
                             Il2CppTimeSpan offset)
{
    // Build the LOCAL time from the components (validated like the DateTime
    // ctor -- ArgumentOutOfRangeException on out-of-range parts), then apply
    // the offset.
    const Il2CppDateTime local = icall_datetime_from_ymdhms(
        year, month, day, hour, minute, second);
    icall_dtoffset_ctor(out, local, offset);
}

//------------------------------------------------------------------------------
// DateTimeOffset.UtcNow -- DateTime.UtcNow with offset ZERO (net48; the
// oracle prints "00:00:00"). Returns the 16-byte value directly (the
// DateTime.get_UtcNow "dt" precedent, result kind "dto").
//------------------------------------------------------------------------------
Il2CppDateTimeOffset icall_dtoffset_utc_now()
{
    const Il2CppDateTime utc = icall_datetime_utc_now();
    Il2CppDateTimeOffset dto;
    dto.utcTicks = utc.ticks;
    dto.offsetMinutes = 0;
    return dto;
}

// DateTimeOffset.Now: the current UTC instant plus the host timezone offset.
// The offset is truncated to whole minutes (net48 stores whole minutes).
Il2CppDateTimeOffset icall_dtoffset_now()
{
    const Il2CppDateTime utc = icall_datetime_utc_now();
    const int64_t offsetSeconds = il2cpp_timezone_get_utc_offset();
    Il2CppDateTimeOffset dto;
    dto.utcTicks = utc.ticks;
    dto.offsetMinutes = static_cast<int32_t>(offsetSeconds / 60);
    return dto;
}

//------------------------------------------------------------------------------
// Accessors.
//------------------------------------------------------------------------------
Il2CppDateTime icall_dtoffset_get_datetime(Il2CppDateTimeOffset dto)
{
    return Il2CppDateTime(dto.utcTicks
                          + static_cast<int64_t>(dto.offsetMinutes) * kTicksPerMinute);
}

Il2CppDateTime icall_dtoffset_get_utc_datetime(Il2CppDateTimeOffset dto)
{
    return Il2CppDateTime(dto.utcTicks, 1 /* Utc */);
}

Il2CppTimeSpan icall_dtoffset_get_offset(Il2CppDateTimeOffset dto)
{
    return Il2CppTimeSpan(static_cast<int64_t>(dto.offsetMinutes) * kTicksPerMinute);
}

int32_t icall_dtoffset_get_year(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_year(icall_dtoffset_get_datetime(dto));
}

int32_t icall_dtoffset_get_month(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_month(icall_dtoffset_get_datetime(dto));
}

int32_t icall_dtoffset_get_day(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_day(icall_dtoffset_get_datetime(dto));
}

int32_t icall_dtoffset_get_hour(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_hour(icall_dtoffset_get_datetime(dto));
}

int32_t icall_dtoffset_get_minute(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_minute(icall_dtoffset_get_datetime(dto));
}

int32_t icall_dtoffset_get_second(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_second(icall_dtoffset_get_datetime(dto));
}

int64_t icall_dtoffset_get_ticks(Il2CppDateTimeOffset dto)
{
    return dto.utcTicks + OffsetTicks(dto);
}

int64_t icall_dtoffset_get_utc_ticks(Il2CppDateTimeOffset dto)
{
    return dto.utcTicks;
}

int32_t icall_dtoffset_get_dayofweek(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_dayofweek(icall_dtoffset_get_datetime(dto));
}

int32_t icall_dtoffset_get_dayofyear(Il2CppDateTimeOffset dto)
{
    return icall_datetime_get_day_of_year(icall_dtoffset_get_datetime(dto));
}

//------------------------------------------------------------------------------
// Comparison / equality (UTC instants).
//------------------------------------------------------------------------------
int32_t icall_dtoffset_compare(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    if (a.utcTicks < b.utcTicks)
    {
        return -1;
    }
    if (a.utcTicks > b.utcTicks)
    {
        return 1;
    }
    return 0;
}

bool icall_dtoffset_equals(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return a.utcTicks == b.utcTicks;
}

//------------------------------------------------------------------------------
// Arithmetic. The DateTime.Add* kernels run on the LOCAL clock (net48
// DateTimeOffset.AddDays = new DateTimeOffset(ClockDateTime.AddDays(days),
// Offset)); the result is re-based to UTC ticks and range-checked.
//------------------------------------------------------------------------------
namespace {

Il2CppDateTimeOffset AddLocal(const Il2CppDateTimeOffset& dto, Il2CppDateTime local)
{
    return MakeDto(local.ticks - OffsetTicks(dto), dto.offsetMinutes);
}

} // namespace

Il2CppDateTimeOffset icall_dtoffset_add_days(Il2CppDateTimeOffset dto, double days)
{
    return AddLocal(dto, icall_datetime_add_days(icall_dtoffset_get_datetime(dto), days));
}

Il2CppDateTimeOffset icall_dtoffset_add_hours(Il2CppDateTimeOffset dto, double hours)
{
    return AddLocal(dto, icall_datetime_add_hours(icall_dtoffset_get_datetime(dto), hours));
}

Il2CppDateTimeOffset icall_dtoffset_add_minutes(Il2CppDateTimeOffset dto, double minutes)
{
    return AddLocal(dto, icall_datetime_add_minutes(icall_dtoffset_get_datetime(dto), minutes));
}

Il2CppDateTimeOffset icall_dtoffset_add_seconds(Il2CppDateTimeOffset dto, double seconds)
{
    return AddLocal(dto, icall_datetime_add_seconds(icall_dtoffset_get_datetime(dto), seconds));
}

Il2CppDateTimeOffset icall_dtoffset_add_milliseconds(Il2CppDateTimeOffset dto, double milliseconds)
{
    return AddLocal(dto, icall_datetime_add_milliseconds(icall_dtoffset_get_datetime(dto), milliseconds));
}

Il2CppDateTimeOffset icall_dtoffset_add_ticks(Il2CppDateTimeOffset dto, int64_t ticks)
{
    return MakeDto(dto.utcTicks + ticks, dto.offsetMinutes);
}

//------------------------------------------------------------------------------
// nb9 (M10): AddYears / AddMonths. net48 computes on the LOCAL clock
// (ClockDateTime.AddYears/AddMonths -- month-end truncation and leap-year
// rules live in the DateTime kernels), then re-applies the same offset. A
// result whose local or UTC clock leaves the representable range throws
// ArgumentOutOfRangeException (the MakeDto contract).
//------------------------------------------------------------------------------
Il2CppDateTimeOffset icall_dtoffset_add_years(Il2CppDateTimeOffset dto, int32_t years)
{
    return AddLocal(dto, icall_datetime_add_years(icall_dtoffset_get_datetime(dto), years));
}

Il2CppDateTimeOffset icall_dtoffset_add_months(Il2CppDateTimeOffset dto, int32_t months)
{
    return AddLocal(dto, icall_datetime_add_months(icall_dtoffset_get_datetime(dto), months));
}

Il2CppDateTimeOffset icall_dtoffset_add(Il2CppDateTimeOffset dto, Il2CppTimeSpan span)
{
    return MakeDto(dto.utcTicks + span.ticks, dto.offsetMinutes);
}

Il2CppDateTimeOffset icall_dtoffset_subtract_ts(Il2CppDateTimeOffset dto, Il2CppTimeSpan span)
{
    return MakeDto(dto.utcTicks - span.ticks, dto.offsetMinutes);
}

Il2CppTimeSpan icall_dtoffset_subtract_dto(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return Il2CppTimeSpan(a.utcTicks - b.utcTicks);
}

// net48 UnixTimeSeconds/Milliseconds are relative to 1970-01-01T00:00:00Z.
// net48 divides the ABSOLUTE tick count first and then subtracts the epoch
// constant (DateTimeOffset.ToUnixTimeSeconds: UtcTicks / TicksPerSecond -
// UnixEpoch.Ticks / TicksPerSecond), so the same order is used here for
// byte-exact pre-1970 truncation.
constexpr int64_t kUnixEpochTicks = 621355968000000000LL;

int64_t icall_dtoffset_to_unix_seconds(Il2CppDateTimeOffset dto)
{
    return dto.utcTicks / kTicksPerSecond - kUnixEpochTicks / kTicksPerSecond;
}

int64_t icall_dtoffset_to_unix_milliseconds(Il2CppDateTimeOffset dto)
{
    return dto.utcTicks / kTicksPerMillisecond - kUnixEpochTicks / kTicksPerMillisecond;
}

Il2CppDateTimeOffset icall_dtoffset_to_offset(Il2CppDateTimeOffset dto, Il2CppTimeSpan offset)
{
    return MakeDto(dto.utcTicks, ValidateOffset(offset));
}

bool icall_dtoffset_op_equality(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return a.utcTicks == b.utcTicks;
}

bool icall_dtoffset_op_inequality(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return a.utcTicks != b.utcTicks;
}

bool icall_dtoffset_op_less_than(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return a.utcTicks < b.utcTicks;
}

bool icall_dtoffset_op_greater_than(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return a.utcTicks > b.utcTicks;
}

bool icall_dtoffset_op_less_or_equal(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return a.utcTicks <= b.utcTicks;
}

bool icall_dtoffset_op_greater_or_equal(Il2CppDateTimeOffset a, Il2CppDateTimeOffset b)
{
    return a.utcTicks >= b.utcTicks;
}

//------------------------------------------------------------------------------
// ToString([format[, provider]]) -- the shared date/time engine with the
// DateTimeOffset offset semantics (net48, pinned by the dtoffsetfmt oracle):
//   * null / empty format -> the provider-selected culture General "G"
//     pattern + " zzz" (the parameterless ToString path);
//   * standard specifiers -> the culture pattern WITHOUT the offset, except
//     o/O (local + always-numeric offset), u/r/R (the UTC instant) and 'U'
//     (FormatException -- net48 DateTimeOffset does not support it);
//   * custom formats -> the offset tokens z/zz/zzz/K and the '%' escape
//     (see il2cpp_time_fmt.h).
//------------------------------------------------------------------------------
Il2CppObject* icall_dtoffset_to_string_format(Il2CppDateTimeOffset dto, Il2CppObject* format,
                                              Il2CppObject* provider)
{
    const Il2CppString* fs = reinterpret_cast<const Il2CppString*>(format);
    const DateCulture culture = ResolveProviderCulture(provider);
    const Il2CppDateTime local = icall_dtoffset_get_datetime(dto);
    const int32_t offsetMinutes = dto.offsetMinutes;
    // DateTimeOffset K is always the numeric offset (kind -1).
    const DateTimeZoneInfo zone = {offsetMinutes, -1};
    std::u16string out;

    if (fs == nullptr || fs->obj.klass != il2cpp_string_class() || fs->length == 0)
    {
        // Default: the culture General "G" pattern plus " zzz".
        const char16_t* pattern = LookupStandardPattern(u'G', culture);
        out = FormatPattern(CivilTimeFromTicks(local), WeekdayFromTicks(local), pattern,
                            static_cast<int32_t>(std::char_traits<char16_t>::length(pattern)),
                            nullptr, culture);
        out.push_back(u' ');
        AppendOffsetZzz(out, offsetMinutes);
    }
    else if (fs->length == 1)
    {
        const char16_t c = fs->chars[0];
        const bool isLetter = (c >= u'a' && c <= u'z') || (c >= u'A' && c <= u'Z');
        if (!isLetter)
        {
            // net48: a single-character format string is a standard
            // specifier lookup -- a non-letter throws FormatException.
            il2cpp_throw_format_exception("Format specifier was invalid.");
        }
        else if (c == u'U')
        {
            // net48: DateTimeOffset.ToString("U") throws -- the universal
            // full pattern expects a DateTime, not a DateTimeOffset.
            il2cpp_throw_format_exception("Format specifier was invalid.");
        }
        else if (c == u'o' || c == u'O')
        {
            // Round trip: local clock + 7 fraction digits + the offset
            // (always numeric, "+00:00" at zero).
            const char16_t* pattern = LookupStandardPattern(u'O', culture);
            out = FormatPattern(CivilTimeFromTicks(local), WeekdayFromTicks(local), pattern,
                                static_cast<int32_t>(std::char_traits<char16_t>::length(pattern)),
                                nullptr, culture);
            AppendOffsetZzz(out, offsetMinutes);
        }
        else if (c == u'u' || c == u'r' || c == u'R')
        {
            // The UTC instant (net48 converts before formatting).
            const Il2CppDateTime utc = icall_dtoffset_get_utc_datetime(dto);
            const char16_t* pattern = LookupStandardPattern(c, culture);
            out = FormatPattern(CivilTimeFromTicks(utc), WeekdayFromTicks(utc), pattern,
                                static_cast<int32_t>(std::char_traits<char16_t>::length(pattern)),
                                nullptr, culture);
        }
        else
        {
            const char16_t* pattern = LookupStandardPattern(c, culture);
            if (pattern == nullptr)
            {
                // net48: unknown single-letter standard specifier.
                il2cpp_throw_format_exception("Format specifier was invalid.");
            }
            out = FormatPattern(CivilTimeFromTicks(local), WeekdayFromTicks(local), pattern,
                                static_cast<int32_t>(std::char_traits<char16_t>::length(pattern)),
                                nullptr, culture);
        }
    }
    else
    {
        out = FormatPattern(CivilTimeFromTicks(local), WeekdayFromTicks(local),
                            fs->chars, fs->length, &zone, culture);
    }

    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
}

// ToString(): the parameterless overload is the null-format path of the
// format icall (host-default culture): culture "G" pattern + " zzz".
Il2CppObject* icall_dtoffset_to_string(Il2CppDateTimeOffset dto)
{
    return icall_dtoffset_to_string_format(dto, nullptr, nullptr);
}

}} // namespace il2cpp::runtime
