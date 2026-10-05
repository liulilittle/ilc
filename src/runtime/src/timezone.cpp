//------------------------------------------------------------------------------
// timezone.cpp -- TimeZone simplification domain (see timezone.h).
//
// Two offset sources, one per platform (see the header for the strategy):
//
//   - Windows: GetTimeZoneInformation. The structure's Bias is the base
//     bias; StandardBias / DaylightBias are ADDITIONAL biases applied per
//     period (MSDN: "This value is added to the value of the Bias member").
//     The function's return code (TIME_ZONE_ID_STANDARD / _DAYLIGHT /
//     _UNKNOWN) identifies the ACTIVE period, so the effective bias is
//     Bias + (DaylightBias when DAYLIGHT else StandardBias) and the
//     daylight-saving state comes from the same call. The sign follows the
//     documented formula UTC = local + bias, i.e. offset = -bias minutes.
//     Empirically verified on a UTC+8 machine: Bias = -480 -> +28800 s,
//     identical to the independent localtime-derived offset.
//
//   - POSIX: localtime_r / gmtime_r on the SAME time_t -- the field
//     difference is the offset at that instant (no mktime / DST ambiguity),
//     tm_isdst is the daylight-saving state. Same pattern as time.cpp's
//     LocalUtcOffsetSeconds (duplicated deliberately; see timezone.h).
//
// Conversions clamp to [DateTime.MinValue, DateTime.MaxValue]: valid
// offsets (<= 15 h) shift ticks by at most ~5.4e11, far inside the int64
// range, so the arithmetic cannot overflow and the clamp is exact.
//------------------------------------------------------------------------------

#include "timezone.h"

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#endif

#include <cstdint>
#include <ctime>

namespace il2cpp {
namespace runtime {

namespace {

// 1 tick = 100 ns; DateTime.MaxValue = 9999-12-31 23:59:59 (pinned by
// test_time.cpp). DateTime.MinValue = 0.
constexpr int64_t kTicksPerSecond = 10000000;
constexpr int64_t kMaxValueTicks = 3155378975999999999;

// Days from the Unix epoch to a civil date (Hinnant's days_from_civil --
// same algorithm as time.cpp's DaysFromNetEpoch without the .NET epoch
// shift). Both local and UTC representations use the same day count, so
// only the field difference survives.
int64_t DaysFromCivil(int32_t year, int32_t month, int32_t day)
{
    const int64_t y = static_cast<int64_t>(year) - (month <= 2 ? 1 : 0);
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;
    const int64_t doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + (day - 1);
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468;
}

// POSIX (and any non-Windows) offset: localtime/gmtime field difference at
// the current instant. Falls back to 0 (UTC) on failure.
int64_t GetOffsetSecondsCt()
{
    const std::time_t now = std::time(nullptr);
    if (now == static_cast<std::time_t>(-1))
    {
        return 0;
    }
    std::tm localTm = {};
    std::tm utcTm = {};
#if defined(_WIN32)
    // Windows CRT fallback path (only reached when GetTimeZoneInformation
    // is unavailable -- see il2cpp_timezone_get_utc_offset below).
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
    const int64_t localDays = DaysFromCivil(localTm.tm_year + 1900, localTm.tm_mon + 1, localTm.tm_mday);
    const int64_t utcDays = DaysFromCivil(utcTm.tm_year + 1900, utcTm.tm_mon + 1, utcTm.tm_mday);
    const int64_t localSeconds = localDays * 86400 + localTm.tm_hour * 3600
                               + localTm.tm_min * 60 + localTm.tm_sec;
    const int64_t utcSeconds = utcDays * 86400 + utcTm.tm_hour * 3600
                             + utcTm.tm_min * 60 + utcTm.tm_sec;
    return localSeconds - utcSeconds;
}

} // namespace

//------------------------------------------------------------------------------
// Public API.
//------------------------------------------------------------------------------
int64_t il2cpp_timezone_get_utc_offset()
{
#if defined(_WIN32)
    TIME_ZONE_INFORMATION tz = {};
    const DWORD state = GetTimeZoneInformation(&tz);
    if (state == TIME_ZONE_ID_INVALID)
    {
        return GetOffsetSecondsCt(); // registry corruption etc. -- CRT fallback, 0 on failure
    }
    const int64_t periodBias = (state == TIME_ZONE_ID_DAYLIGHT) ? tz.DaylightBias : tz.StandardBias;
    return -(static_cast<int64_t>(tz.Bias) + periodBias) * 60;
#else
    return GetOffsetSecondsCt();
#endif
}

Il2CppDateTime il2cpp_datetime_to_local(Il2CppDateTime utc)
{
    const int64_t offsetTicks = il2cpp_timezone_get_utc_offset() * kTicksPerSecond;
    const int64_t ticks = utc.ticks + offsetTicks;
    if (ticks < 0)
    {
        return Il2CppDateTime(0);
    }
    if (ticks > kMaxValueTicks)
    {
        return Il2CppDateTime(kMaxValueTicks);
    }
    return Il2CppDateTime(ticks);
}

Il2CppDateTime il2cpp_datetime_from_local(Il2CppDateTime local)
{
    const int64_t offsetTicks = il2cpp_timezone_get_utc_offset() * kTicksPerSecond;
    const int64_t ticks = local.ticks - offsetTicks;
    if (ticks < 0)
    {
        return Il2CppDateTime(0);
    }
    if (ticks > kMaxValueTicks)
    {
        return Il2CppDateTime(kMaxValueTicks);
    }
    return Il2CppDateTime(ticks);
}

int32_t il2cpp_timezone_get_is_daylight_saving()
{
#if defined(_WIN32)
    TIME_ZONE_INFORMATION tz = {};
    const DWORD state = GetTimeZoneInformation(&tz);
    if (state == TIME_ZONE_ID_INVALID)
    {
        // Symmetric with il2cpp_timezone_get_utc_offset's CRT fallback: a
        // registry failure must not crash, and the CRT's tm_isdst (same
        // Windows zone data) still answers the DST question. 0 on failure.
        const std::time_t now = std::time(nullptr);
        if (now == static_cast<std::time_t>(-1))
        {
            return 0;
        }
        std::tm localTm = {};
        if (_localtime64_s(&localTm, &now) != 0)
        {
            return 0;
        }
        return localTm.tm_isdst > 0 ? 1 : 0;
    }
    return (state == TIME_ZONE_ID_DAYLIGHT) ? 1 : 0;
#else
    const std::time_t now = std::time(nullptr);
    if (now == static_cast<std::time_t>(-1))
    {
        return 0;
    }
    std::tm localTm = {};
    if (localtime_r(&now, &localTm) == nullptr)
    {
        return 0;
    }
    return localTm.tm_isdst > 0 ? 1 : 0;
#endif
}

} // namespace runtime
} // namespace il2cpp
