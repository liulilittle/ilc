//------------------------------------------------------------------------------
// test_timezone_posix.cpp -- POSIX per-date TimeZone offsets vs the net48
// oracle (nb-tz: the current-offset back-projection defect).
//
// The defect: the old POSIX per-date path built the UTC instant as
// "wall - CURRENT offset" and read the offset there. Crossing a DST
// boundary therefore used the WRONG instant and reported the neighbouring
// period's offset (measured: Pacific/Auckland 2000-04-01 wall +12:00 ->
// +13:00; America/New_York 2000-04-01 -> -4 h instead of -5 h).
//
// The oracle table below is net48's TimeZoneInfo.GetUtcOffset /
// IsDaylightSavingTime measured on a Windows machine whose zone registry is
// the same data the .NET Framework reads (2000-01/04/07/10 12:00, both
// Unspecified (wall face) and Utc kinds). POSIX resolves the SAME zones
// through the system tzdata, so the values must agree.
//
// Known data-source divergence (NOT a defect of this path): glibc tzdata
// carries the one-off 2000-08-27 early DST start for the Sydney Olympics in
// Australia/Sydney and Australia/Lord_Howe; the Windows registry does not.
// Those two cells therefore differ from net48 on POSIX by one hour and are
// pinned separately (kOlympicCells) so the divergence is explicit rather
// than hidden.
//------------------------------------------------------------------------------

#include "timezone.h"

#include "il2cpp_time.h"

#if !defined(_WIN32)
#include <cstdlib>
#include <cstring>
#include <ctime>
#endif

#include <cstdint>
#include <cstdio>

namespace {

int g_failures = 0;
int g_checks = 0;
int g_skips = 0;

void Expect(bool condition, const char* what)
{
    ++g_checks;
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// A skipped zone is NOT a pass. Every skip is counted so main() can tell
// "everything was skipped" (a broken/absent tzdata) apart from "everything
// ran and passed", and report the former as a non-zero exit (77, the CTest
// SKIP_RETURN_CODE). Without this an empty TZDIR produced 12 SKIP lines, 0
// FAILs, exit 0 and "all checks passed" -- a false green in CI.
void SkipZone(const char* tzid)
{
    std::printf("SKIP: zone '%s' is not available in this host's tzdata "
                "(install tzdata / zoneinfo to run this check)\n", tzid);
    ++g_skips;
}

// TZ switching for the matrix. setenv can "succeed" on a host without the
// zoneinfo database (or with a stripped one) while tzset silently leaves the
// process on UTC; that would turn the whole table red for an environment
// reason instead of a code reason. SelectZone therefore VERIFIES the zone took
// effect and the caller reports a counted SKIP.
#if !defined(_WIN32)
char g_originalTz[256];
bool g_haveOriginalTz = false;

void SaveTzOnce()
{
    if (g_haveOriginalTz)
    {
        return;
    }
    const char* tz = std::getenv("TZ");
    if (tz != nullptr)
    {
        std::snprintf(g_originalTz, sizeof(g_originalTz), "%s", tz);
    }
    g_haveOriginalTz = true;
}

void RestoreTz()
{
    SaveTzOnce();
    const char* tz = std::getenv("TZ");
    if (tz == nullptr && g_originalTz[0] == '\0')
    {
        unsetenv("TZ");
    }
    else
    {
        setenv("TZ", g_originalTz, 1);
    }
    tzset();
}

// Returns false when the zone could not be selected (missing zoneinfo entry).
bool SelectZone(const char* tzid)
{
    SaveTzOnce();
    setenv("TZ", tzid, 1);
    tzset();
    // Verification: a missing zoneinfo entry makes glibc fall back to UTC
    // SILENTLY. Sample two instants (winter and summer) and require that at
    // least one produces localtime fields different from gmtime; a zone with a
    // genuine zero offset all year (only "UTC" in this table) is special-cased.
    // Two samples are needed because a +0-in-winter zone (Europe/Dublin) is
    // indistinguishable from UTC at the January sample alone.
    const std::time_t probes[2] = {
        static_cast<std::time_t>(946684800),  // 2000-01-01T00:00Z
        static_cast<std::time_t>(962409600),  // 2000-07-01T00:00Z
    };
    bool anyDifference = false;
    for (int i = 0; i < 2; ++i)
    {
        std::tm localTm = {};
        std::tm utcTm = {};
        if (localtime_r(&probes[i], &localTm) == nullptr
            || gmtime_r(&probes[i], &utcTm) == nullptr)
        {
            return false;
        }
        if (localTm.tm_hour != utcTm.tm_hour || localTm.tm_mday != utcTm.tm_mday
            || localTm.tm_min != utcTm.tm_min)
        {
            anyDifference = true;
        }
    }
    if (std::strcmp(tzid, "UTC") == 0)
    {
        return !anyDifference;
    }
    return anyDifference;
}
#endif // !defined(_WIN32)

constexpr int64_t kTicksPerSecond = 10000000LL;
constexpr int64_t kTicksPerHour = 3600LL * kTicksPerSecond;

int64_t DaysFromCivil(int64_t y, int64_t m, int64_t d)
{
    y -= (m <= 2) ? 1 : 0;
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;
    const int64_t doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468 + 719162;
}

int64_t Ticks(int y, int mo, int d, int h)
{
    return DaysFromCivil(y, mo, d) * 86400LL * kTicksPerSecond
         + static_cast<int64_t>(h) * kTicksPerHour;
}

// zone, posix id, per-month (Jan/Apr/Jul/Oct) wall offset seconds, wall
// daylight flag, utc offset seconds, utc daylight flag -- the net48 oracle.
struct Row
{
    const char* label;
    const char* posixId;
    int32_t wallOffset[4];
    int32_t wallDst[4];
    int32_t utcOffset[4];
    int32_t utcDst[4];
};

const Row kOracle[] = {
    { "UTC", "UTC",
      { 0, 0, 0, 0 }, { 0, 0, 0, 0 },
      { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
    { "Asia/Shanghai", "Asia/Shanghai",
      { 28800, 28800, 28800, 28800 }, { 0, 0, 0, 0 },
      { 28800, 28800, 28800, 28800 }, { 0, 0, 0, 0 } },
    { "America/New_York", "America/New_York",
      { -18000, -18000, -14400, -14400 }, { 0, 0, 1, 1 },
      { -18000, -18000, -14400, -14400 }, { 0, 0, 1, 1 } },
    { "Australia/Lord_Howe", "Australia/Lord_Howe",
      { 39600, 37800, 37800, 37800 }, { 1, 0, 0, 0 },
      { 39600, 37800, 37800, 37800 }, { 1, 0, 0, 0 } },
    { "Europe/Dublin", "Europe/Dublin",
      { 0, 3600, 3600, 3600 }, { 0, 1, 1, 1 },
      { 0, 3600, 3600, 3600 }, { 0, 1, 1, 1 } },
    { "Pacific/Auckland", "Pacific/Auckland",
      { 46800, 43200, 43200, 46800 }, { 1, 0, 0, 1 },
      { 46800, 43200, 43200, 46800 }, { 1, 0, 0, 1 } },
    { "Australia/Sydney", "Australia/Sydney",
      { 39600, 36000, 36000, 36000 }, { 1, 0, 0, 0 },
      { 39600, 36000, 36000, 36000 }, { 1, 0, 0, 0 } },
    { "Asia/Kolkata", "Asia/Kolkata",
      { 19800, 19800, 19800, 19800 }, { 0, 0, 0, 0 },
      { 19800, 19800, 19800, 19800 }, { 0, 0, 0, 0 } },
};

const int kMonths[4] = { 1, 4, 7, 10 };

#if !defined(_WIN32)
// The tzdata-only Olympic cells: Australia/Sydney and Australia/Lord_Howe
// on 2000-10-01 (glibc: DST already in effect since 2000-08-27; the Windows
// registry starts DST on the regular rule, 2000-10-29).
bool IsTzdataOnlyCell(int rowIndex, int monthIndex)
{
    return monthIndex == 3
        && (rowIndex == 3 /* Lord_Howe */ || rowIndex == 6 /* Sydney */);
}

void TestOracleMatrix()
{
    for (int r = 0; r < static_cast<int>(sizeof(kOracle) / sizeof(kOracle[0])); ++r)
    {
        const Row& row = kOracle[r];
        if (!SelectZone(row.posixId))
        {
            SkipZone(row.posixId);
            continue;
        }
        for (int m = 0; m < 4; ++m)
        {
            const int64_t wall = Ticks(2000, kMonths[m], 1, 12);
            const il2cpp::runtime::Il2CppDateTime wallDt(wall, 0 /* Unspecified */);
            const il2cpp::runtime::Il2CppDateTime utcDt(wall, 1 /* Utc */);

            char what[160];
            const int32_t gotWall = static_cast<int32_t>(
                il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(wallDt));
            const int32_t gotWallDst =
                il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(wallDt);
            const int32_t gotUtc = static_cast<int32_t>(
                il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(utcDt));
            const int32_t gotUtcDst =
                il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(utcDt);

            if (IsTzdataOnlyCell(r, m))
            {
                // Explicit, pinned divergence: tzdata carries the one-off
                // early DST start for the Sydney Olympics (2000-08-27), the
                // Windows registry does not. The POSIX path must report the
                // tzdata values (+11:00, daylight) on BOTH faces -- these are
                // the measured `zoneinfo` values, not the net48 ones.
                std::snprintf(what, sizeof(what),
                              "%s 2000-%02d-01 tzdata-only cell: wall == +11:00 daylight",
                              row.label, kMonths[m]);
                Expect(gotWall == 39600, what);
                Expect(gotWallDst == 1, what);
                std::snprintf(what, sizeof(what),
                              "%s 2000-%02d-01 tzdata-only cell: utc == +11:00 daylight",
                              row.label, kMonths[m]);
                Expect(gotUtc == 39600, what);
                Expect(gotUtcDst == 1, what);
                continue;
            }

            std::snprintf(what, sizeof(what), "%s 2000-%02d-01 wall offset == %d",
                          row.label, kMonths[m], row.wallOffset[m]);
            Expect(gotWall == row.wallOffset[m], what);
            std::snprintf(what, sizeof(what), "%s 2000-%02d-01 wall dst == %d",
                          row.label, kMonths[m], row.wallDst[m]);
            Expect(gotWallDst == row.wallDst[m], what);
            std::snprintf(what, sizeof(what), "%s 2000-%02d-01 utc offset == %d",
                          row.label, kMonths[m], row.utcOffset[m]);
            Expect(gotUtc == row.utcOffset[m], what);
            std::snprintf(what, sizeof(what), "%s 2000-%02d-01 utc dst == %d",
                          row.label, kMonths[m], row.utcDst[m]);
            Expect(gotUtcDst == row.utcDst[m], what);
        }
    }
}

// net48's wall-clock resolution at the DST edges: the non-existent
// spring-forward hour and the repeated fall-back hour both resolve to the
// STANDARD offset with daylight = false (measured, New Zealand Standard Time).
void TestWallClockEdges()
{
    if (!SelectZone("Pacific/Auckland"))
    {
        SkipZone("Pacific/Auckland");
        return;
    }

    struct Case
    {
        int month;
        int day;
        int hour;
        int minute;
        int32_t offset;
        int32_t dst;
        const char* what;
    };
    const Case cases[] = {
        { 10, 1, 1, 59, 43200, 0, "AKL spring 01:59 -> +12 standard" },
        { 10, 1, 2, 0, 43200, 0, "AKL spring 02:00 (invalid hour) -> +12 standard" },
        { 10, 1, 2, 30, 43200, 0, "AKL spring 02:30 (invalid hour) -> +12 standard" },
        { 10, 1, 3, 0, 46800, 1, "AKL spring 03:00 -> +13 daylight" },
        { 3, 19, 1, 59, 46800, 1, "AKL fall 01:59 -> +13 daylight" },
        { 3, 19, 2, 0, 43200, 0, "AKL fall 02:00 (repeated hour) -> +12 standard" },
        { 3, 19, 2, 30, 43200, 0, "AKL fall 02:30 (repeated hour) -> +12 standard" },
        { 3, 19, 3, 0, 43200, 0, "AKL fall 03:00 -> +12 standard" },
    };
    for (const Case& c : cases)
    {
        const int64_t ticks = DaysFromCivil(2000, c.month, c.day) * 86400LL * kTicksPerSecond
                            + (static_cast<int64_t>(c.hour) * 3600 + c.minute * 60) * kTicksPerSecond;
        const il2cpp::runtime::Il2CppDateTime dt(ticks, 0);
        const int32_t off = static_cast<int32_t>(
            il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(dt));
        const int32_t dst =
            il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(dt);
        Expect(off == c.offset, c.what);
        Expect(dst == c.dst, c.what);
    }

    // The legacy wrapper: a Kind=Utc input is TimeSpan.Zero, a wall input is
    // the per-date offset (net48 TimeZoneInfoWrapper).
    const il2cpp::runtime::Il2CppDateTime wallDt(Ticks(2000, 7, 1, 12), 0);
    const il2cpp::runtime::Il2CppDateTime utcDt(Ticks(2000, 7, 1, 12), 1);
    Expect(il2cpp::runtime::il2cpp_timezone_get_utc_offset_legacy(utcDt) == 0,
           "legacy GetUtcOffset(Kind=Utc) == 0");
    Expect(il2cpp::runtime::il2cpp_timezone_get_utc_offset_legacy(wallDt) == 43200,
           "legacy GetUtcOffset(AKL 2000-07 wall) == +12");
}

// The historic regression: the defect used the CURRENT offset to place the
// instant, so a wall clock in a DIFFERENT period of the year (or a different
// season than "now") read the neighbouring offset. Pin the two faces of the
// same civil date across all four seasons for a southern-hemisphere zone.
void TestSameCivilDateBothFaces()
{
    if (!SelectZone("Pacific/Auckland"))
    {
        SkipZone("Pacific/Auckland");
        return;
    }
    for (int m = 0; m < 4; ++m)
    {
        const int64_t wall = Ticks(2000, kMonths[m], 1, 12);
        const int32_t wallOff = static_cast<int32_t>(
            il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(
                il2cpp::runtime::Il2CppDateTime(wall, 0)));
        const int32_t utcOff = static_cast<int32_t>(
            il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(
                il2cpp::runtime::Il2CppDateTime(wall, 1)));
        // 12:00 wall and 12:00Z are both mid-day, never inside the 1 h gap,
        // so the two faces agree on the same date.
        char what[96];
        std::snprintf(what, sizeof(what), "AKL 2000-%02d-01 wall face == utc face", kMonths[m]);
        Expect(wallOff == utcOff, what);
    }
}

// DateTimeKind.Local (kind == 2) coverage.
//
// net48 does NOT read the receiver zone as a wall clock for Kind=Local. It
// converts dt through the MACHINE zone (TimeZoneInfo.Local) to a UTC instant
// and then reads the RECEIVER zone at that instant. Measured on this UTC+8
// host with a custom receiver (base +12:00, DST +13:00 over
// [2000-06-01T00:00Z, 04:00Z)), Kind=Local gave:
//   local wall 08:00 -> +13h, 10:00 -> +13h, 12:00 -> +12h, 14:00 -> +12h
// which matches the machine-conversion model; the receiver-wall model would
// have given +12h, +12h, +13h, +13h instead.
//
// In this runtime every reachable receiver is the machine zone itself
// (TimeZoneInfo.Local / TimeZone.CurrentTimeZone; TimeZoneInfo.Utc is folded
// to a literal zero offset by MethodMap.TimeZone.cs and never reaches the
// icall, and FindSystemTimeZoneById only feeds ConvertTimeFromUtc UTC
// instants). So the composition is: resolve the wall against the machine
// zone, subtract that offset to obtain the instant, then read the offset AT
// that instant. It therefore equals the Unspecified answer everywhere EXCEPT
// inside the skipped spring-forward hour, where the wall resolution reports
// standard but the instant it maps to is already daylight.
//
// That exception is the discriminating assertion below: aliasing kind 2 to
// kind 0 (the pre-fix shape) or routing it down the UTC face both break it.
void TestKindLocalUsesMachineInstant()
{
    const struct { const char* tz; int month; int day; int hour; bool expectGap; } zones[] = {
        { "Pacific/Auckland", 1, 1, 12, false },
        { "Pacific/Auckland", 4, 1, 12, false },
        { "Pacific/Auckland", 10, 1, 0, false },    // before the spring-forward
        { "Pacific/Auckland", 10, 1, 2, true },     // inside the skipped hour
        { "Pacific/Auckland", 3, 19, 2, false },    // repeated hour: no gap
        { "America/New_York", 4, 2, 2, true },      // inside the skipped hour
        { "America/New_York", 4, 2, 3, false },
        { "America/New_York", 10, 29, 1, false },   // repeated hour
        { "Australia/Lord_Howe", 10, 29, 2, false }, // 30-minute DST zone
    };
    for (const auto& z : zones)
    {
        if (!SelectZone(z.tz))
        {
            SkipZone(z.tz);
            continue;
        }
        const int64_t ticks = DaysFromCivil(2000, z.month, z.day) * 86400LL * kTicksPerSecond
                            + static_cast<int64_t>(z.hour) * 3600 * kTicksPerSecond;
        const il2cpp::runtime::Il2CppDateTime wallDt(ticks, 0 /* Unspecified */);
        const il2cpp::runtime::Il2CppDateTime localDt(ticks, 2 /* Local */);
        const int32_t wallOff = static_cast<int32_t>(
            il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(wallDt));
        const int32_t localOff = static_cast<int32_t>(
            il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(localDt));
        const int32_t wallDst =
            il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(wallDt);
        const int32_t localDst =
            il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(localDt);

        // The model: wall offset -> instant -> offset at that instant.
        const int64_t instant = ticks - static_cast<int64_t>(wallOff) * kTicksPerSecond;
        const int32_t expected = static_cast<int32_t>(
            il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(
                il2cpp::runtime::Il2CppDateTime(instant, 1 /* Utc */)));
        const int32_t expectedDst =
            il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(
                il2cpp::runtime::Il2CppDateTime(instant, 1 /* Utc */));

        char what[176];
        std::snprintf(what, sizeof(what),
                      "%s 2000-%02d-%02d %02d:00 Kind=Local == machine-instant model",
                      z.tz, z.month, z.day, z.hour);
        Expect(localOff == expected, what);
        std::snprintf(what, sizeof(what),
                      "%s 2000-%02d-%02d %02d:00 Kind=Local dst == machine-instant model",
                      z.tz, z.month, z.day, z.hour);
        Expect(localDst == expectedDst, what);

        if (z.expectGap)
        {
            // Inside the skipped hour the machine-instant model MUST diverge
            // from the wall answer: this is what makes the test discriminating
            // rather than a tautology.
            std::snprintf(what, sizeof(what),
                          "%s 2000-%02d-%02d %02d:00 skipped hour: Kind=Local != Kind=Unspecified",
                          z.tz, z.month, z.day, z.hour);
            Expect(localOff != wallOff, what);
        }
        else
        {
            std::snprintf(what, sizeof(what),
                          "%s 2000-%02d-%02d %02d:00 outside a gap: Kind=Local == Kind=Unspecified",
                          z.tz, z.month, z.day, z.hour);
            Expect(localOff == wallOff, what);
        }
    }
    // The legacy wrapper is Kind-aware only for Utc; Local is NOT zeroed.
    if (!SelectZone("Pacific/Auckland"))
    {
        return;
    }
    const il2cpp::runtime::Il2CppDateTime localJul(Ticks(2000, 7, 1, 12), 2);
    Expect(il2cpp::runtime::il2cpp_timezone_get_utc_offset_legacy(localJul) == 43200,
           "legacy GetUtcOffset(Kind=Local, AKL 2000-07) == +12 (not zeroed)");
}
#endif // !defined(_WIN32)

} // namespace

// Exit codes:
//   0  -- every check ran and passed (normal)
//   1  -- at least one assertion failed
//   77 -- nothing could run: every zone was skipped (missing/stripped tzdata).
//         This is the CTest SKIP_RETURN_CODE, so ctest reports "Skipped"
//         instead of a pass. Without it an empty TZDIR yielded 12 SKIP lines,
//         0 FAILs and exit 0 -- a false green.
int main()
{
#if defined(_WIN32)
    // POSIX-only. Windows takes the registry branch and never reaches the
    // function under test, so there is nothing to check here. Not coverage.
    std::printf("test_timezone_posix: POSIX-only, not applicable on Windows\n");
    return 0;
#else
    TestOracleMatrix();
    TestWallClockEdges();
    TestSameCivilDateBothFaces();
    TestKindLocalUsesMachineInstant();
    RestoreTz();

    if (g_failures != 0)
    {
        std::printf("test_timezone_posix: %d failure(s) (%d checks, %d skipped zone(s))\n",
                    g_failures, g_checks, g_skips);
        return 1;
    }
    if (g_skips != 0)
    {
        // The matrix needs all eight zones. A missing zone (e.g. TZDIR
        // pointing at an empty directory -- "UTC" still resolves because the
        // C library has it built in, so a plain checks==0 test would not
        // catch this) means the table was NOT verified. Refuse to report a
        // pass; 77 is the CTest SKIP_RETURN_CODE, so ctest shows "Skipped".
        std::printf("test_timezone_posix: INCOMPLETE -- %d zone(s) skipped, "
                    "matrix not verified (missing tzdata/zoneinfo?)\n", g_skips);
        return 77;
    }
    std::printf("test_timezone_posix: all checks passed (%d checks, %d zone(s) skipped)\n",
                g_checks, g_skips);
    return 0;
#endif
}
