//------------------------------------------------------------------------------
// test_timezone_perdate.cpp -- per-date TimeZone evaluation unit tests
// (GN-20 MSCORLIB TimeZone DST audit fix).
//
// Pins (committed net48-oracle measurements, East 2026/2020 + historical):
// the per-date family (il2cpp_timezone_get_utc_offset_at / _legacy /
// _is_daylight_saving_at / _is_invalid_time) must reproduce net48's measured
// contract. Since the production icalls read the MACHINE zone (a no-DST UTC+8
// host reduces them to a constant), the DST-zone MATH is verified here by
// feeding the same model the Eastern Standard Time registry data -- the exact
// TZI + Dynamic DST the .NET Framework reads -- through a LOCAL mirror of the
// timezone.cpp rule evaluation (the mirror is the algorithm under test).
//
// Part A (machine-agnostic, any host):
//   * il2cpp_timezone_get_utc_offset_legacy(Kind=Utc) == 0 (the net48
//     TimeZoneInfoWrapper Kind-aware zero -- the audit's red case).
//   * il2cpp_timezone_get_utc_offset_legacy(non-Utc) ==
//     il2cpp_timezone_get_utc_offset_at(same).
//   * is_invalid_time on fixed mid-season noons == 0 (no real zone's 1-2 h
//     spring gap contains Jan-15 or Jul-15 noon).
//   * |utc_offset_at(any)| <= 15 h.
//
// Part B (Windows only): Eastern Standard Time from the registry -- assert the
// production MODEL (the mirror) against the net48-measured oracle values:
// winter/summer offsets, all four 2026 March/November boundary wall-clock and
// Kind=Utc instants, IsDaylightSavingTime, IsInvalidTime, and historical
// (2005/1988) instants resolved through the Dynamic-DST 2006 pre-2007 rule.
//------------------------------------------------------------------------------

#include "il2cpp_time.h"
#include "timezone.h"

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
    #include <winreg.h>
#endif

#include <cstdint>
#include <cstdio>
#include <cstring>

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

constexpr int64_t kTicksPerSecond = 10000000LL;
constexpr int64_t kTicksPerMinute = 60LL * kTicksPerSecond;
constexpr int64_t kTicksPerHour = 3600LL * kTicksPerSecond;
constexpr int64_t kTicksPerDay = 86400LL * kTicksPerSecond;

// The civil helpers + rule model BELOW MIRROR timezone.cpp's per-date
// evaluation (the algorithm under test -- the production icalls read the
// machine zone; Part B drives the Eastern registry data through the same
// math). Keep in sync with src/timezone.cpp.
int64_t DaysFromCivilEpoch(int64_t y, int64_t m, int64_t d)
{
    y -= (m <= 2) ? 1 : 0;
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;
    const int64_t doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468 + 719162;
}

int WeekdayOfEpoch(int64_t days)
{
    return static_cast<int>((days + 1) % 7);
}

int DaysInMonth(int32_t y, int32_t m)
{
    static const int kDays[13] = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    if (m == 2 && (y % 4 == 0 && (y % 100 != 0 || y % 400 == 0)))
    {
        return 29;
    }
    return kDays[m];
}

struct TzTransition
{
    int32_t month;
    int32_t day;
    int32_t dayOfWeek;
    bool isFloating;
    int64_t timeTicks;
};

int64_t TransitionDayOfMonth(const TzTransition& t, int32_t y, int32_t m)
{
    if (!t.isFloating)
    {
        return t.day;
    }
    const int wd = WeekdayOfEpoch(DaysFromCivilEpoch(y, m, 1));
    const int first = 1 + ((t.dayOfWeek - wd + 7) % 7);
    int64_t d = first + 7 * (t.day - 1);
    const int dim = DaysInMonth(y, m);
    if (d > dim)
    {
        d -= 7;
    }
    return d;
}

struct Rule
{
    int32_t firstYear;
    int32_t lastYear;
    int32_t baseOffsetSeconds;
    int32_t dstOffsetSeconds;
    bool supportsDst;
    TzTransition spring;
    TzTransition fall;
};

int64_t UtcTicks(int y, int mo, int d, int h, int mi, int s)
{
    return DaysFromCivilEpoch(y, mo, d) * kTicksPerDay + h * kTicksPerHour
         + mi * kTicksPerMinute + s * kTicksPerSecond;
}

// Evaluates the per-date offset for a WALL-face (civil) DateTime.
int32_t EvaluateWall(const Rule& r, int32_t year, int64_t wallTicks)
{
    const int32_t base = r.baseOffsetSeconds;
    if (!r.supportsDst)
    {
        return base;
    }
    const int64_t springDay = DaysFromCivilEpoch(year, r.spring.month, TransitionDayOfMonth(r.spring, year, r.spring.month));
    const int64_t fallDay = DaysFromCivilEpoch(year, r.fall.month, TransitionDayOfMonth(r.fall, year, r.fall.month));
    const int64_t springWall = springDay * kTicksPerDay + r.spring.timeTicks;
    const int64_t fallWall = fallDay * kTicksPerDay + r.fall.timeTicks;
    const int64_t dstDelta = (static_cast<int64_t>(r.dstOffsetSeconds) - base) * kTicksPerSecond;
    const int64_t startUtc = springWall - static_cast<int64_t>(base) * kTicksPerSecond;
    const int64_t endUtc = fallWall - static_cast<int64_t>(r.dstOffsetSeconds) * kTicksPerSecond;
    if (dstDelta <= 0)
    {
        return base;
    }
    const int64_t candidate = wallTicks - static_cast<int64_t>(base) * kTicksPerSecond;
    const bool dst = (startUtc + dstDelta <= candidate && candidate < endUtc);
    return dst ? r.dstOffsetSeconds : base;
}

// Evaluates the per-date offset for a UTC-INSTANT input (DateTimeKind.Utc --
// net48's GetUtcOffsetFromUtc window [startUtc, endUtc)).
int32_t EvaluateInstant(const Rule& r, int32_t year, int64_t utcTicks)
{
    if (!r.supportsDst)
    {
        return r.baseOffsetSeconds;
    }
    const int64_t springDay = DaysFromCivilEpoch(year, r.spring.month, TransitionDayOfMonth(r.spring, year, r.spring.month));
    const int64_t fallDay = DaysFromCivilEpoch(year, r.fall.month, TransitionDayOfMonth(r.fall, year, r.fall.month));
    const int64_t springWall = springDay * kTicksPerDay + r.spring.timeTicks;
    const int64_t fallWall = fallDay * kTicksPerDay + r.fall.timeTicks;
    const int64_t dstDelta = (static_cast<int64_t>(r.dstOffsetSeconds) - r.baseOffsetSeconds) * kTicksPerSecond;
    const int64_t startUtc = springWall - static_cast<int64_t>(r.baseOffsetSeconds) * kTicksPerSecond;
    const int64_t endUtc = fallWall - static_cast<int64_t>(r.dstOffsetSeconds) * kTicksPerSecond;
    if (dstDelta <= 0)
    {
        return r.baseOffsetSeconds;
    }
    const bool dst = (startUtc <= utcTicks && utcTicks < endUtc);
    return dst ? r.dstOffsetSeconds : r.baseOffsetSeconds;
}

bool InInvalidHour(const Rule& r, int32_t year, int64_t wallTicks)
{
    if (!r.supportsDst)
    {
        return false;
    }
    const int64_t springDay = DaysFromCivilEpoch(year, r.spring.month, TransitionDayOfMonth(r.spring, year, r.spring.month));
    const int64_t springWall = springDay * kTicksPerDay + r.spring.timeTicks;
    const int64_t dstDelta = (static_cast<int64_t>(r.dstOffsetSeconds) - r.baseOffsetSeconds) * kTicksPerSecond;
    return dstDelta > 0 && springWall <= wallTicks && wallTicks < springWall + dstDelta;
}

#if defined(_WIN32)
// Reads the Eastern Standard Time registry TZI + Dynamic DST entries and
// asserts the net48-measured oracle (the same data .NET Framework reads).
void TestEasternRegistryPerDate()
{
    const char* keyPath = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Time Zones\\Eastern Standard Time";
    HKEY key = nullptr;
    if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, keyPath, 0, KEY_READ, &key) != ERROR_SUCCESS)
    {
        return; // an exotic Windows without the key -- skip (never failed here)
    }
    auto readBinary = [&](HKEY h, const char* name, uint8_t* out) {
        DWORD size = 44;
        DWORD type = 0;
        return RegGetValueA(h, nullptr, name, RRF_RT_REG_BINARY, &type, out, &size)
            == ERROR_SUCCESS && type == REG_BINARY && size == 44;
    };
    auto parseSt = [](const uint8_t* p, TzTransition& t) {
        const int year = p[0] | (p[1] << 8);
        t.month = p[2] | (p[3] << 8);
        t.dayOfWeek = p[4] | (p[5] << 8);
        t.day = p[6] | (p[7] << 8);
        t.isFloating = (year == 0);
        t.timeTicks = (static_cast<int64_t>(p[8] | (p[9] << 8))) * kTicksPerHour
                    + (static_cast<int64_t>(p[10] | (p[11] << 8))) * kTicksPerMinute
                    + (static_cast<int64_t>(p[12] | (p[13] << 8))) * kTicksPerSecond;
    };
    auto parseRule = [&](const uint8_t* b, Rule& r) {
        const int32_t bias = static_cast<int32_t>(b[0]) | (static_cast<int32_t>(b[1]) << 8)
                           | (static_cast<int32_t>(b[2]) << 16) | (static_cast<int32_t>(b[3]) << 24);
        const int32_t standardBias = static_cast<int32_t>(b[4]) | (static_cast<int32_t>(b[5]) << 8)
                                   | (static_cast<int32_t>(b[6]) << 16) | (static_cast<int32_t>(b[7]) << 24);
        const int32_t daylightBias = static_cast<int32_t>(b[8]) | (static_cast<int32_t>(b[9]) << 8)
                                   | (static_cast<int32_t>(b[10]) << 16) | (static_cast<int32_t>(b[11]) << 24);
        parseSt(b + 12, r.fall);
        parseSt(b + 28, r.spring);
        r.baseOffsetSeconds = -(bias + standardBias) * 60;
        r.dstOffsetSeconds = -(bias + daylightBias) * 60;
        r.supportsDst = r.fall.month != 0;
    };
    auto readDword = [&](HKEY h, const char* name, int32_t& out) {
        DWORD t;
        DWORD size = sizeof(t);
        DWORD type = 0;
        const LONG rc = RegGetValueA(h, nullptr, name, RRF_RT_REG_DWORD, &type, &t, &size);
        if (rc == ERROR_SUCCESS && type == REG_DWORD)
        {
            out = static_cast<int32_t>(t);
            return true;
        }
        return false;
    };

    uint8_t blob[44];
    // The Dynamic DST data lives in the "<zone id>\Dynamic DST" subkey.
    HKEY dyn = nullptr;
    int32_t first = 0;
    int32_t last = 0;
    const bool ok = readBinary(key, "TZI", blob)
                    && RegOpenKeyExA(key, "Dynamic DST", 0, KEY_READ, &dyn) == ERROR_SUCCESS
                    && readDword(dyn, "FirstEntry", first)
                    && readDword(dyn, "LastEntry", last);
    if (!ok || first > last)
    {
        if (dyn) RegCloseKey(dyn);
        RegCloseKey(key);
        std::printf("(Eastern registry incomplete -- skipping Part B)\n");
        return;
    }
    char name[16];
    Rule firstRule = {};
    Rule lastRule = {};
    std::snprintf(name, sizeof(name), "%d", first);
    if (!readBinary(dyn, name, blob)) { RegCloseKey(dyn); RegCloseKey(key); return; }
    parseRule(blob, firstRule);
    firstRule.firstYear = 1;
    firstRule.lastYear = (first > 1) ? first : 1;
    std::snprintf(name, sizeof(name), "%d", last);
    if (!readBinary(dyn, name, blob)) { RegCloseKey(dyn); RegCloseKey(key); return; }
    parseRule(blob, lastRule);
    if (last > first)
    {
        lastRule.firstYear = last;
        lastRule.lastYear = 9999;
    }
    else
    {
        lastRule = firstRule; // single dynamic rule only
    }
    RegCloseKey(dyn);
    RegCloseKey(key);

    // net48-measured values are MINUTES; the model returns SECONDS.
    const int32_t kMin = 60;

    auto evalYear = [&](int32_t year, const int64_t wallTicks) -> int32_t {
        const Rule& r = (year <= firstRule.lastYear) ? firstRule : lastRule;
        return EvaluateWall(r, year, wallTicks);
    };
    auto invalidYear = [&](int32_t year, const int64_t wallTicks) -> bool {
        const Rule& r = (year <= firstRule.lastYear) ? firstRule : lastRule;
        return InInvalidHour(r, year, wallTicks);
    };

    auto evalInstant = [&](int32_t year, const int64_t utcTicks) -> int32_t {
        const Rule& r = (year <= firstRule.lastYear) ? firstRule : lastRule;
        return EvaluateInstant(r, year, utcTicks);
    };
    auto isDstOf = [&](int32_t year, const int64_t wallTicks) -> bool {
        const Rule& r = (year <= firstRule.lastYear) ? firstRule : lastRule;
        return EvaluateWall(r, year, wallTicks) == r.dstOffsetSeconds;
    };

    // --- Kind=Utc instants (net48-measured: the UTC-window model) ---
    Expect(evalInstant(2026, UtcTicks(2026, 3, 8, 6, 30, 0)) == -300 * kMin, "u.0630z = -300");
    Expect(evalInstant(2026, UtcTicks(2026, 3, 8, 7, 0, 0)) == -240 * kMin, "u.0700z = -240");
    Expect(evalInstant(2026, UtcTicks(2026, 3, 8, 7, 30, 0)) == -240 * kMin, "u.0730z = -240");
    Expect(evalInstant(2026, UtcTicks(2026, 11, 1, 5, 30, 0)) == -240 * kMin, "u.0530z_nov = -240");
    Expect(evalInstant(2026, UtcTicks(2026, 11, 1, 6, 0, 0)) == -300 * kMin, "u.0600z_nov = -300");

    // --- IsDaylightSavingTime derived from the offset (net48-measured) ---
    Expect(!isDstOf(2026, UtcTicks(2026, 3, 8, 2, 0, 0)), "w.0200 not DST");
    Expect(isDstOf(2026, UtcTicks(2026, 3, 8, 3, 0, 0)), "w.0300 DST");
    Expect(!isDstOf(2026, UtcTicks(2026, 11, 1, 1, 0, 0)), "f.0100 not DST");

    // --- winter/summer (net48-measured) ---
    Expect(evalYear(2026, UtcTicks(2026, 1, 15, 12, 0, 0)) == -300 * kMin, "e.2026.jan wall = -300");
    Expect(evalYear(2026, UtcTicks(2026, 7, 15, 12, 0, 0)) == -240 * kMin, "e.2026.jul wall = -240");
    Expect(evalYear(2020, UtcTicks(2020, 1, 15, 12, 0, 0)) == -300 * kMin, "e.2020.jan wall = -300");
    Expect(evalYear(2020, UtcTicks(2020, 7, 15, 12, 0, 0)) == -240 * kMin, "e.2020.jul wall = -240");

    // --- 2026-03-08 spring-forward wall-clock boundary (net48-measured) ---
    Expect(evalYear(2026, UtcTicks(2026, 3, 8, 1, 0, 0)) == -300 * kMin, "w.0100 = -300");
    Expect(evalYear(2026, UtcTicks(2026, 3, 8, 2, 0, 0)) == -300 * kMin, "w.0200 = -300 (invalid hour)");
    Expect(evalYear(2026, UtcTicks(2026, 3, 8, 2, 30, 0)) == -300 * kMin, "w.0230 = -300 (invalid hour)");
    Expect(evalYear(2026, UtcTicks(2026, 3, 8, 3, 0, 0)) == -240 * kMin, "w.0300 = -240");
    Expect(evalYear(2026, UtcTicks(2026, 3, 8, 4, 0, 0)) == -240 * kMin, "w.0400 = -240");

    // --- 2026-11-01 fall-back wall-clock boundary (net48-measured) ---
    Expect(evalYear(2026, UtcTicks(2026, 11, 1, 0, 59, 59)) == -240 * kMin, "f.0059 = -240");
    Expect(evalYear(2026, UtcTicks(2026, 11, 1, 1, 0, 0)) == -300 * kMin, "f.0100 = -300 (repeated hour)");
    Expect(evalYear(2026, UtcTicks(2026, 11, 1, 2, 0, 0)) == -300 * kMin, "f.0200 = -300");

    // --- IsDaylightSavingTime / IsInvalidTime (net48-measured) ---
    Expect(!invalidYear(2026, UtcTicks(2026, 3, 8, 1, 0, 0)), "w.0100 valid");
    Expect(invalidYear(2026, UtcTicks(2026, 3, 8, 2, 0, 0)), "w.0200 invalid");
    Expect(invalidYear(2026, UtcTicks(2026, 3, 8, 2, 30, 0)), "w.0230 invalid");
    Expect(!invalidYear(2026, UtcTicks(2026, 3, 8, 3, 0, 0)), "w.0300 valid");
    Expect(!invalidYear(2026, UtcTicks(2026, 11, 1, 1, 0, 0)), "f.0100 valid (repeated hour)");
    Expect(evalYear(2026, UtcTicks(2026, 3, 8, 2, 30, 0)) != evalYear(2026, UtcTicks(2026, 7, 15, 12, 0, 0)),
           "dst state differs across the year (DST math active)");

    // --- historical via the Dynamic-DST 2006 rule (net48-measured: the
    //     Apr 1st-Sunday / Oct 5th-Sunday pre-2007 rule applies) ---
    Expect(evalYear(2005, UtcTicks(2005, 1, 15, 12, 0, 0)) == -300 * kMin, "e.2005.jan = -300 (pre-2007 rule)");
    Expect(evalYear(2005, UtcTicks(2005, 7, 15, 12, 0, 0)) == -240 * kMin, "e.2005.jul = -240 (pre-2007 DST)");
    Expect(evalYear(1988, UtcTicks(1988, 7, 15, 12, 0, 0)) == -240 * kMin, "e.1988.jul = -240 (pre-2007 DST)");
}
#endif // _WIN32

void TestMachineAgnostic()
{
    const il2cpp::runtime::Il2CppDateTime janWall(UtcTicks(2026, 1, 15, 12, 0, 0), 0 /* Unspecified */);
    const il2cpp::runtime::Il2CppDateTime janUtc(UtcTicks(2026, 1, 15, 12, 0, 0), 1 /* Utc */);
    const il2cpp::runtime::Il2CppDateTime julWall(UtcTicks(2026, 7, 15, 12, 0, 0), 0);

    // net48 TimeZoneInfoWrapper::GetUtcOffset zeroes a Kind=Utc input (the
    // audit's legacy-kind red case).
    Expect(il2cpp::runtime::il2cpp_timezone_get_utc_offset_legacy(janUtc) == 0,
           "legacy GetUtcOffset(Kind=Utc) == 0");
    Expect(il2cpp::runtime::il2cpp_timezone_get_utc_offset_legacy(janWall)
               == il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(janWall),
           "legacy GetUtcOffset(non-Utc) == per-date offset");
    // offset sanity (any real zone is within +/-15 h).
    const int64_t janOff = il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(janWall);
    const int64_t julOff = il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(julWall);
    Expect(janOff >= -15 * 3600 && janOff <= 15 * 3600, "offset_at within +/-15h");
    Expect(julOff >= -15 * 3600 && julOff <= 15 * 3600, "offset_at within +/-15h (jul)");
    // fixed mid-season noons are never inside a 1-2 h spring gap on any zone.
    Expect(il2cpp::runtime::il2cpp_timezone_get_is_invalid_time(janWall) == 0,
           "is_invalid_time(jan noon) == 0");
    Expect(il2cpp::runtime::il2cpp_timezone_get_is_invalid_time(julWall) == 0,
           "is_invalid_time(jul noon) == 0");
    // the daylight flag is 0 or 1, never crashes.
    const int32_t dst = il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(janWall);
    Expect(dst == 0 || dst == 1, "is_daylight_saving_at returns 0/1");
}

} // namespace

int main()
{
    TestMachineAgnostic();
#if defined(_WIN32)
    TestEasternRegistryPerDate();
#endif
    if (g_failures != 0)
    {
        std::printf("test_timezone_perdate: %d failure(s)\n", g_failures);
        return 1;
    }
    std::printf("test_timezone_perdate: all checks passed\n");
    return 0;
}