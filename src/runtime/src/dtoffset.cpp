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
#include "il2cpp_string.h"        // il2cpp_string_new_from_cs

#include <cstdint>
#include <cstdio>

namespace il2cpp { namespace runtime {

namespace {

constexpr int64_t kTicksPerMinute = 600000000LL; // 60 s * 10^7 ticks/s
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

} // namespace

//------------------------------------------------------------------------------
// Construction.
//------------------------------------------------------------------------------
void icall_dtoffset_ctor(Il2CppDateTimeOffset* out, Il2CppDateTime localTime,
                         Il2CppTimeSpan offset)
{
    const int32_t minutes = ValidateOffset(offset);
    out->utcTicks = localTime.ticks - offset.ticks;
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
// Accessors.
//------------------------------------------------------------------------------
Il2CppDateTime icall_dtoffset_get_datetime(Il2CppDateTimeOffset dto)
{
    return Il2CppDateTime(dto.utcTicks
                          + static_cast<int64_t>(dto.offsetMinutes) * kTicksPerMinute);
}

Il2CppDateTime icall_dtoffset_get_utc_datetime(Il2CppDateTimeOffset dto)
{
    return Il2CppDateTime(dto.utcTicks);
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
// ToString(): "MM/dd/yyyy HH:mm:ss zzz" -- the local time in the fixed
// culture-neutral DateTime shape plus the offset as "+HH:mm" / "-HH:mm"
// (net48 DateTimeOffset default format).
//------------------------------------------------------------------------------
Il2CppObject* icall_dtoffset_to_string(Il2CppDateTimeOffset dto)
{
    const Il2CppDateTime local = icall_dtoffset_get_datetime(dto);
    const int32_t total = dto.offsetMinutes;
    const char sign = total < 0 ? '-' : '+';
    const int32_t abs = total < 0 ? -total : total;
    char buffer[48];
    std::snprintf(buffer, sizeof(buffer), "%02d/%02d/%04d %02d:%02d:%02d %c%02d:%02d",
                  icall_datetime_get_month(local),
                  icall_datetime_get_day(local),
                  icall_datetime_get_year(local),
                  icall_datetime_get_hour(local),
                  icall_datetime_get_minute(local),
                  icall_datetime_get_second(local),
                  sign, abs / 60, abs % 60);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(buffer));
}

}} // namespace il2cpp::runtime
