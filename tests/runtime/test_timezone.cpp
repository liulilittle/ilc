//------------------------------------------------------------------------------
// test_timezone.cpp -- TimeZone simplification domain unit tests.
//
// Domain: DateTime is stored as UTC ticks (see il2cpp_time.h); the TimeZone
// domain adds the local-timezone offset at the CURRENT instant:
//   il2cpp_timezone_get_utc_offset()       offset seconds (local - UTC, +east)
//   il2cpp_datetime_to_local(utc)          utc ticks + offset (local wall time)
//   il2cpp_datetime_from_local(local)      local ticks - offset (UTC ticks)
//   il2cpp_timezone_get_is_daylight_saving() 0/1 at the current instant
//
// The domain is a simplification: ONE current offset applies to every
// instant (no historical/zone-table lookup), so every assertion compares
// against the OS clock or checks round-trips -- NEVER against a hard-coded
// offset value (the machine may run any timezone, with or without DST).
//
// Coverage (normal + edge + error paths):
//   1. Offset bounds: |offset| <= 15 h (any real timezone).
//   2. Offset matches the OS clock: an independent localtime/gmtime field
//      difference at the same instant agrees within 120 s (the tolerance
//      absorbs a DST transition landing between the two calls and sub-minute
//      rounding differences between Windows bias (minutes) and CRT seconds).
//   3. Round-trip inverse at fixed instants: from_local then to_local and
//      to_local then from_local both restore the input within 2 s -- this
//      catches double-application and sign errors (offset is applied EXACTLY
//      once in each direction). When the machine's offset is non-zero these
//      are strong assertions.
//   4. to_local(UtcNow) equals the OS local wall clock within 3 s.
//   5. from_local(Now) equals UtcNow within 5 s -- ties the new domain to
//      the existing System.DateTime clock icalls (Now == UtcNow + offset).
//   6. Exact single application: to_local(utc_now) - utc_now == offset
//      ticks exactly (skipped if a DST transition flips the offset between
//      the two readings -- the 1 s/year race).
//   7. is_daylight_saving returns 0 or 1, never crashes.
//   8. is_daylight_saving agrees with the OS clock: the CRT's own tm_isdst at
//      the same instant (localtime) must match -- the independent net48
//      IsDaylightSavingTime(Now) semantic. A DST transition landing between
//      the two reads is skipped via a double-read, exactly like the offset
//      race in check 6.
//   9. Edge clamps: conversions of DateTime.MinValue / DateTime.MaxValue
//      never produce out-of-range ticks (negative or > MaxValue).
//  10. (Windows only) The offset/state formulas match GetTimeZoneInformation
//      itself: offset == -(Bias + periodBias) * 60 with periodBias from the
//      returned TIME_ZONE_ID, and is_daylight_saving == (state ==
//      TIME_ZONE_ID_DAYLIGHT). This pins the MSDN "UTC = local + bias"
//      contract (the header's primary Windows strategy) against the OS's own
//      registry data at the current instant.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "timezone.h"

#include "il2cpp_time.h"

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <windows.h>
#endif

#include <cstdint>
#include <cstdio>
#include <ctime>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Days from the Unix epoch to a civil date (Hinnant's days_from_civil --
// exact, no platform calls). The test helper computes both sides of the
// offset with the same day count, so only the FIELD DIFFERENCE survives.
int64_t DaysFromCivil(int32_t year, int32_t month, int32_t day)
{
    const int64_t y = static_cast<int64_t>(year) - (month <= 2 ? 1 : 0);
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;
    const int64_t doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + (day - 1);
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468;
}

// The OS's own opinion of the local offset at 'now', in seconds, computed
// independently from the production function (CRT localtime/gmtime field
// difference -- the pattern pinned by test_time.cpp's clock tests).
int64_t OsOffsetSeconds(std::time_t now)
{
    std::tm localTm = {};
    std::tm utcTm = {};
#if defined(_WIN32)
    if (_localtime64_s(&localTm, &now) != 0 || _gmtime64_s(&utcTm, &now) != 0)
#else
    if (localtime_r(&now, &localTm) == nullptr || gmtime_r(&now, &utcTm) == nullptr)
#endif
    {
        return 0;
    }
    const int64_t localDays = DaysFromCivil(localTm.tm_year + 1900, localTm.tm_mon + 1, localTm.tm_mday);
    const int64_t utcDays = DaysFromCivil(utcTm.tm_year + 1900, utcTm.tm_mon + 1, utcTm.tm_mday);
    const int64_t localSeconds = localDays * 86400 + localTm.tm_hour * 3600
                               + localTm.tm_min * 60 + localTm.tm_sec;
    const int64_t utcSeconds = utcDays * 86400 + utcTm.tm_hour * 3600
                             + utcTm.tm_min * 60 + utcTm.tm_sec;
    return localSeconds - utcSeconds;
}

constexpr int64_t kTicksPerSecond = 10000000;
constexpr int64_t kMaxValueTicks = 3155378975999999999; // 9999-12-31 23:59:59
// 2020-06-15 00:00:00 UTC (pinned by test_time.cpp) plus half a day.
constexpr int64_t kFixedInstant = 637277760000000000 + 432000000000;
constexpr int64_t kTwoSecondsTicks = 2 * kTicksPerSecond;

void TestOffsetMatchesOsClock()
{
    const int64_t offset = il2cpp::runtime::il2cpp_timezone_get_utc_offset();
    const std::time_t now = std::time(nullptr);
    const int64_t osOffset = OsOffsetSeconds(now);
    Expect(offset >= -15 * 3600 && offset <= 15 * 3600, "offset within +/-15h");
    // 120 s absorbs a DST transition between the two reads and Windows'
    // whole-minute bias rounding.
    const int64_t diff = offset > osOffset ? offset - osOffset : osOffset - offset;
    Expect(diff <= 120, "offset matches independent OS localtime/gmtime");
}

void TestRoundTripFromLocalThenToLocal()
{
    // X is a LOCAL wall instant; from_local(X) must undo the offset and
    // to_local must re-apply it. Two mismatched offsets (double-add,
    // double-subtract, or wrong sign) break this by 2*|offset| -- far beyond
    // the 2 s tolerance whenever the machine is not on UTC.
    const il2cpp::runtime::Il2CppDateTime local(kFixedInstant);
    const il2cpp::runtime::Il2CppDateTime utc = il2cpp::runtime::il2cpp_datetime_from_local(local);
    const il2cpp::runtime::Il2CppDateTime back = il2cpp::runtime::il2cpp_datetime_to_local(utc);
    const int64_t drift = back.ticks > local.ticks ? back.ticks - local.ticks : local.ticks - back.ticks;
    Expect(drift <= kTwoSecondsTicks, "round trip local->utc->local within 2s");
}

void TestRoundTripToLocalThenFromLocal()
{
    // Same round trip in the other direction from a UTC instant.
    const il2cpp::runtime::Il2CppDateTime utc(kFixedInstant);
    const il2cpp::runtime::Il2CppDateTime local = il2cpp::runtime::il2cpp_datetime_to_local(utc);
    const il2cpp::runtime::Il2CppDateTime back = il2cpp::runtime::il2cpp_datetime_from_local(local);
    const int64_t drift = back.ticks > utc.ticks ? back.ticks - utc.ticks : utc.ticks - back.ticks;
    Expect(drift <= kTwoSecondsTicks, "round trip utc->local->utc within 2s");
}

void TestToLocalOfUtcNowMatchesWallClock()
{
    // Independent check: the converted value must equal what the OS reports
    // as the local wall clock at the same instant (whole-second agreement).
    const std::time_t before = std::time(nullptr);
    const il2cpp::runtime::Il2CppDateTime utcNow = il2cpp::runtime::icall_datetime_utc_now();
    const std::time_t after = std::time(nullptr);
    const il2cpp::runtime::Il2CppDateTime local = il2cpp::runtime::il2cpp_datetime_to_local(utcNow);
    std::tm localTm = {};
    const std::time_t mid = before + (after - before) / 2;
#if defined(_WIN32)
    _localtime64_s(&localTm, &mid);
#else
    localtime_r(&mid, &localTm);
#endif
    // Compare only the seconds-of-day within a 3 s window (the date math is
    // pinned by test_time.cpp's clock tests; here the offset is under test).
    const int64_t day = 86400 * kTicksPerSecond;
    const int64_t localTod = local.ticks % day;
    const int64_t osTod = (static_cast<int64_t>(localTm.tm_hour) * 3600
                           + localTm.tm_min * 60 + localTm.tm_sec) * kTicksPerSecond;
    int64_t diff = localTod > osTod ? localTod - osTod : osTod - localTod;
    if (diff > day / 2)
    {
        diff = day - diff; // local midnight wrap-around
    }
    Expect(diff <= 3 * kTicksPerSecond, "to_local(UtcNow) matches OS wall clock");
}

void TestFromLocalOfNowMatchesUtcNow()
{
    // Domain tie-in: DateTime.Now is defined as UtcNow + offset; converting
    // it back with the new domain must recover UtcNow.
    const il2cpp::runtime::Il2CppDateTime now = il2cpp::runtime::icall_datetime_now();
    const il2cpp::runtime::Il2CppDateTime utc = il2cpp::runtime::il2cpp_datetime_from_local(now);
    const il2cpp::runtime::Il2CppDateTime utcNow = il2cpp::runtime::icall_datetime_utc_now();
    const int64_t drift = utc.ticks > utcNow.ticks ? utc.ticks - utcNow.ticks : utcNow.ticks - utc.ticks;
    Expect(drift <= 5 * kTicksPerSecond, "from_local(Now) within 5s of UtcNow");
}

void TestExactSingleApplication()
{
    // The offset must be applied exactly once (and in the right direction):
    // to_local(utc_now) - utc_now == offset ticks. The assertion is skipped
    // only if a DST transition flips the offset between the two reads (the
    // ~1 s per year race -- physics, not logic).
    const int64_t offsetBefore = il2cpp::runtime::il2cpp_timezone_get_utc_offset();
    const il2cpp::runtime::Il2CppDateTime utcNow = il2cpp::runtime::icall_datetime_utc_now();
    const il2cpp::runtime::Il2CppDateTime local = il2cpp::runtime::il2cpp_datetime_to_local(utcNow);
    const int64_t offsetAfter = il2cpp::runtime::il2cpp_timezone_get_utc_offset();
    if (offsetBefore == offsetAfter)
    {
        Expect(local.ticks - utcNow.ticks == offsetBefore * kTicksPerSecond,
               "to_local applies offset exactly once");
    }
}

void TestIsDaylightSaving()
{
    const int32_t dst = il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving();
    Expect(dst == 0 || dst == 1, "is_daylight_saving returns 0 or 1");
}

void TestIsDaylightSavingMatchesOsState()
{
    // net48 semantic: TimeZoneInfo.Local.IsDaylightSavingTime(Now) must agree
    // with the OS clock's own daylight state. The CRT's tm_isdst at the same
    // instant is the independent oracle (the same CRT that time.cpp's clock
    // path pins). A DST transition flipping the flag between the two reads
    // (~1 s per year) is skipped via a double-read -- the TestExactSingleApplication
    // race contract.
    const int32_t before = il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving();
    std::tm localTm = {};
    const std::time_t now = std::time(nullptr);
#if defined(_WIN32)
    if (_localtime64_s(&localTm, &now) != 0)
    {
        return; // CRT unavailable -- skip (the production function's own fallback)
    }
#else
    if (localtime_r(&now, &localTm) == nullptr)
    {
        return;
    }
#endif
    const int32_t after = il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving();
    if (before != after)
    {
        return; // DST transition landed between the reads -- skip the race
    }
    const int32_t osDst = localTm.tm_isdst > 0 ? 1 : 0;
    Expect(after == osDst, "is_daylight_saving matches OS tm_isdst");
}

#if defined(_WIN32)
void TestWindowsOffsetMatchesRegistry()
{
    // The primary Windows strategy (timezone.h): GetTimeZoneInformation gives
    // UTC = local + bias, so offset = -(Bias + periodBias) * 60 seconds, with
    // periodBias = DaylightBias when the active period is DAYLIGHT else
    // StandardBias -- and is_daylight_saving == (state == TIME_ZONE_ID_DAYLIGHT).
    // Re-deriving both from the OS's own structure pins the net48 contract.
    TIME_ZONE_INFORMATION tz = {};
    const DWORD state = GetTimeZoneInformation(&tz);
    if (state == TIME_ZONE_ID_INVALID)
    {
        return; // registry failure -- the production function has a CRT fallback
    }
    const int32_t periodBias = (state == TIME_ZONE_ID_DAYLIGHT) ? tz.DaylightBias : tz.StandardBias;
    const int64_t expectedOffset = -(static_cast<int64_t>(tz.Bias) + periodBias) * 60;
    const int64_t actualOffset = il2cpp::runtime::il2cpp_timezone_get_utc_offset();
    Expect(actualOffset == expectedOffset, "utc_offset == -(Bias + periodBias) * 60");
    const int32_t expectedDst = (state == TIME_ZONE_ID_DAYLIGHT) ? 1 : 0;
    const int32_t actualDst = il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving();
    Expect(actualDst == expectedDst, "is_daylight_saving == (state == TIME_ZONE_ID_DAYLIGHT)");
}
#endif

void TestEdgeClamps()
{
    const il2cpp::runtime::Il2CppDateTime minValue(0);
    const il2cpp::runtime::Il2CppDateTime maxValue(kMaxValueTicks);
    Expect(il2cpp::runtime::il2cpp_datetime_to_local(minValue).ticks >= 0,
           "to_local(MinValue) clamped >= MinValue");
    Expect(il2cpp::runtime::il2cpp_datetime_from_local(minValue).ticks >= 0,
           "from_local(MinValue) clamped >= MinValue");
    Expect(il2cpp::runtime::il2cpp_datetime_to_local(maxValue).ticks <= kMaxValueTicks,
           "to_local(MaxValue) clamped <= MaxValue");
    Expect(il2cpp::runtime::il2cpp_datetime_from_local(maxValue).ticks <= kMaxValueTicks,
           "from_local(MaxValue) clamped <= MaxValue");
}

} // namespace

int main()
{
    TestOffsetMatchesOsClock();
    TestRoundTripFromLocalThenToLocal();
    TestRoundTripToLocalThenFromLocal();
    TestToLocalOfUtcNowMatchesWallClock();
    TestFromLocalOfNowMatchesUtcNow();
    TestExactSingleApplication();
    TestIsDaylightSaving();
    TestIsDaylightSavingMatchesOsState();
#if defined(_WIN32)
    TestWindowsOffsetMatchesRegistry();
#endif
    TestEdgeClamps();

    if (g_failures != 0)
    {
        std::printf("test_timezone: %d failure(s)\n", g_failures);
        return 1;
    }
    std::printf("test_timezone: all checks passed\n");
    return 0;
}
