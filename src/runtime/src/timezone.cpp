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

#if defined(_WIN32)
    #include <winreg.h>
#endif

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <vector>

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

//------------------------------------------------------------------------------
//  Per-date evaluation family (net48 GetUtcOffset / IsDaylightSavingTime /
//  IsInvalidTime semantics -- see timezone.h). The machine zone's adjustment
//  rules are read from the SAME Windows registry the .NET Framework reads
//  (the static TZI + the optional Dynamic DST table), so a DST-zone machine
//  reproduces net48 byte-for-byte; a no-DST host (this machine) reduces
//  every query to a constant offset.
//------------------------------------------------------------------------------

namespace {

// Civil-calendar helpers on the DATETIME tick epoch (0001-01-01 = 0), the
// days counted by Hinnant's civil_from_days + the 719162-day epoch shift
// (the same shift as timezone_registry.cpp -- civil dates feed the DST
// transition resolution).
int64_t DaysFromCivilEpoch(int64_t y, int64_t m, int64_t d)
{
    y -= (m <= 2) ? 1 : 0;
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;
    const int64_t doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468 + 719162;
}

void CivilFromDaysEpoch(int64_t days, int32_t& y, int32_t& m, int32_t& d)
{
    const int64_t z = days + 306;
    const int64_t era = (z >= 0 ? z : z - 146096) / 146097;
    const int64_t doe = z - era * 146097;
    const int64_t yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    y = static_cast<int32_t>(yoe + era * 400);
    const int64_t doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    const int64_t mp = (5 * doy + 2) / 153;
    d = doy - (153 * mp + 2) / 5 + 1;
    m = (mp < 10) ? mp + 3 : mp - 9;
    if (m <= 2)
    {
        ++y;
    }
}

// Sunday = 0 (0001-01-01 is a Monday, so (days + 1) % 7 is Sunday-based).
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

// One DST transition rule from a SYSTEMTIME (the Windows TZI form): wYear==0
// -> FLOATING (the wDay occurrence of the wDayOfWeek weekday; wDay 1..5 where
// 5 is the LAST occurrence), else FIXED (wDay is the day of the month). The
// exact disjunction the .NET Framework uses
// (TransitionTimeFromTimeZoneInformation).
struct TzTransition
{
    int32_t month;      // wMonth
    int32_t day;        // wDay
    int32_t dayOfWeek;  // wDayOfWeek (floating)
    bool isFloating;    // wYear == 0
    int64_t timeTicks;  // wHour:wMinute:wSecond:wMillis -> ticks-of-day
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
        d -= 7; // wDay==5: the LAST occurrence when the weekday does not recur
    }
    return d;
}

// The machine zone's per-year rule (mirrors net48's AdjustmentRule): the
// year range covered, the base/daylight biasses and the two transitions.
struct MachineRule
{
    int32_t firstYear; // inclusive (DateTime year; 1..9999)
    int32_t lastYear;  // inclusive
    int32_t baseOffsetSeconds; // -(Bias + StandardBias) * 60
    int32_t dstOffsetSeconds;  // -(Bias + DaylightBias) * 60
    bool supportsDst;          // StandardDate.wMonth != 0 (net48's gate)
    TzTransition springTrans;  // DaylightDate (standard -> daylight)
    TzTransition fallTrans;    // StandardDate (daylight -> standard)
};

const int64_t kTicksPerMinute = 60LL * kTicksPerSecond;
const int64_t kTicksPerHour = 3600LL * kTicksPerSecond;
const int64_t kTicksPerDay = 86400LL * kTicksPerSecond;
const int64_t kUnixEpochTicks = 621355968000000000LL;

// The per-year DST window (UTC-side), mirroring timezone_registry.cpp's
// OffsetSecondsAt: spring walls are read on the STANDARD clock, fall walls
// on the DAYLIGHT clock.
struct YearWindow
{
    int64_t springWall; // wall ticks (standard clock)
    int64_t fallWall;   // wall ticks (daylight clock)
    int64_t startUtc;   // springWall - baseOffset
    int64_t endUtc;     // fallWall - dstOffset
    int64_t dstDeltaTicks;
};

bool ResolveYearWindow(const MachineRule& r, int32_t year, YearWindow& w)
{
    if (!r.supportsDst)
    {
        return false;
    }
    const int64_t springDay = DaysFromCivilEpoch(year, r.springTrans.month,
        TransitionDayOfMonth(r.springTrans, year, r.springTrans.month));
    const int64_t fallDay = DaysFromCivilEpoch(year, r.fallTrans.month,
        TransitionDayOfMonth(r.fallTrans, year, r.fallTrans.month));
    w.springWall = springDay * kTicksPerDay + r.springTrans.timeTicks;
    w.fallWall = fallDay * kTicksPerDay + r.fallTrans.timeTicks;
    w.dstDeltaTicks = (static_cast<int64_t>(r.dstOffsetSeconds) - r.baseOffsetSeconds) * kTicksPerSecond;
    w.startUtc = w.springWall - static_cast<int64_t>(r.baseOffsetSeconds) * kTicksPerSecond;
    w.endUtc = w.fallWall - static_cast<int64_t>(r.dstOffsetSeconds) * kTicksPerSecond;
    return true;
}

#if defined(_WIN32)
// Reads a REG_TZI_FORMAT (44-byte REG_BINARY) into the rule fields
// (byte-wise -- no packing assumptions about the SYSTEMTIME layout).
void ParseRegTzi(const uint8_t* b, MachineRule& r)
{
    const int32_t bias = static_cast<int32_t>(b[0]) | (static_cast<int32_t>(b[1]) << 8)
                       | (static_cast<int32_t>(b[2]) << 16) | (static_cast<int32_t>(b[3]) << 24);
    const int32_t standardBias = static_cast<int32_t>(b[4]) | (static_cast<int32_t>(b[5]) << 8)
                               | (static_cast<int32_t>(b[6]) << 16) | (static_cast<int32_t>(b[7]) << 24);
    const int32_t daylightBias = static_cast<int32_t>(b[8]) | (static_cast<int32_t>(b[9]) << 8)
                               | (static_cast<int32_t>(b[10]) << 16) | (static_cast<int32_t>(b[11]) << 24);
    auto st = [](const uint8_t* p, TzTransition& t) {
        const int year = p[0] | (p[1] << 8);
        t.month = p[2] | (p[3] << 8);
        t.dayOfWeek = p[4] | (p[5] << 8);
        t.day = p[6] | (p[7] << 8);
        t.isFloating = (year == 0);
        t.timeTicks = (static_cast<int64_t>(p[8] | (p[9] << 8))) * kTicksPerHour
                    + (static_cast<int64_t>(p[10] | (p[11] << 8))) * kTicksPerMinute
                    + (static_cast<int64_t>(p[12] | (p[13] << 8))) * kTicksPerSecond
                    + (static_cast<int64_t>(p[14] | (p[15] << 8))) * (kTicksPerSecond / 1000);
    };
    st(b + 12, r.fallTrans);   // StandardDate (fall)
    st(b + 28, r.springTrans); // DaylightDate (spring)
    r.baseOffsetSeconds = -(bias + standardBias) * 60;
    r.dstOffsetSeconds = -(bias + daylightBias) * 60;
    // net48's supportsDst gate: StandardDate.wMonth != 0 (DaylightDate is
    // validated by the OS to accompany it).
    r.supportsDst = r.fallTrans.month != 0;
}

bool ReadRegBinary(HKEY key, const char* name, uint8_t* out, DWORD capacity)
{
    DWORD size = capacity;
    DWORD type = 0;
    const LONG rc = RegGetValueA(key, nullptr, name, RRF_RT_REG_BINARY, &type, out, &size);
    return rc == ERROR_SUCCESS && type == REG_BINARY && size == 44;
}

bool ReadRegDword(HKEY key, const char* name, int32_t& out)
{
    DWORD value = 0;
    DWORD size = sizeof(value);
    DWORD type = 0;
    const LONG rc = RegGetValueA(key, nullptr, name, RRF_RT_REG_DWORD, &type, &value, &size);
    if (rc != ERROR_SUCCESS || type != REG_DWORD)
    {
        return false;
    }
    out = static_cast<int32_t>(value);
    return true;
}

bool ReadRegString(HKEY base, const char* sub, const char* name, char* out, DWORD capacity)
{
    DWORD size = capacity;
    DWORD type = 0;
    const LONG rc = RegGetValueA(base, sub, name, RRF_RT_REG_SZ, &type, out, &size);
    if (rc != ERROR_SUCCESS || type != REG_SZ)
    {
        return false;
    }
    out[capacity - 1] = '\0'; // defensive NUL
    return true;
}

// Builds the machine zone's rule table from the registry -- the EXACT data
// the .NET Framework reads (the static TZI + the "Dynamic DST" SUBKEY with
// FirstEntry/LastEntry + per-year entries), replicating net48's
// TryCreateAdjustmentRules ranges:
//   [1..first]  <- the first dynamic TZI      (pre-dynamic years)
//   [yr..yr]    <- per-year dynamic TZI       (in-range years)
//   [last,9999] <- the last dynamic TZI       (post-dynamic years)
// No Dynamic DST subkey -> one all-year rule from the static TZI.
bool BuildMachineRulesFromRegistry(std::vector<MachineRule>& out)
{
    char zoneId[128];
    if (!ReadRegString(HKEY_LOCAL_MACHINE,
        "SYSTEM\\CurrentControlSet\\Control\\TimeZoneInformation",
        "TimeZoneKeyName", zoneId, sizeof(zoneId)))
    {
        return false;
    }
    char keyPath[256];
    const int n = std::snprintf(keyPath, sizeof(keyPath),
        "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Time Zones\\%s", zoneId);
    if (n <= 0 || static_cast<size_t>(n) >= sizeof(keyPath))
    {
        return false;
    }
    HKEY key = nullptr;
    if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, keyPath, 0, KEY_READ, &key) != ERROR_SUCCESS)
    {
        return false;
    }

    uint8_t baseBlob[44];
    const bool haveBase = ReadRegBinary(key, "TZI", baseBlob, sizeof(baseBlob));

    // The Dynamic DST data (FirstEntry/LastEntry + per-year REG_TZI_FORMAT
    // values) lives in the "<zone id>\Dynamic DST" SUBKEY (the registry
    // layout the .NET Framework iterates).
    HKEY dynKey = nullptr;
    const LONG dynOpen = RegOpenKeyExA(key, "Dynamic DST", 0, KEY_READ, &dynKey);
    int32_t first = 0;
    int32_t last = 0;
    bool haveDynamic = dynOpen == ERROR_SUCCESS && haveBase
                       && ReadRegDword(dynKey, "FirstEntry", first)
                       && ReadRegDword(dynKey, "LastEntry", last)
                       && first >= 1 && last <= 9999 && first <= last;

    if (haveDynamic)
    {
        char yearName[16];
        uint8_t yearBlob[44];
        // first entry -> [1..first]; last entry -> [last..9999]
        int32_t bounds[2] = { first, last };
        for (int i = 0; i < 2; ++i)
        {
            std::snprintf(yearName, sizeof(yearName), "%d", bounds[i]);
            if (!ReadRegBinary(dynKey, yearName, yearBlob, sizeof(yearBlob)))
            {
                RegCloseKey(dynKey);
                RegCloseKey(key);
                return false;
            }
            MachineRule r;
            ParseRegTzi(yearBlob, r);
            if (i == 0)
            {
                r.firstYear = 1;
                r.lastYear = (bounds[0] > 1) ? bounds[0] : 1;
                if (bounds[1] <= bounds[0])
                {
                    r.lastYear = 9999; // single dynamic entry -- covers all years
                }
            }
            else
            {
                if (bounds[1] <= bounds[0])
                {
                    RegCloseKey(dynKey);
                    RegCloseKey(key);
                    return true; // single dynamic rule only -- already pushed
                }
                r.firstYear = bounds[1];
                r.lastYear = 9999;
            }
            out.push_back(r);
        }
        // per-year middle entries (first+1 .. last-1); each covers exactly
        // its own year (net48's middle-rule DateStart/DateEnd).
        for (int32_t yr = first + 1; yr < last; ++yr)
        {
            std::snprintf(yearName, sizeof(yearName), "%d", yr);
            if (!ReadRegBinary(dynKey, yearName, yearBlob, sizeof(yearBlob)))
            {
                RegCloseKey(dynKey);
                RegCloseKey(key);
                return false;
            }
            MachineRule r;
            ParseRegTzi(yearBlob, r);
            r.firstYear = yr;
            r.lastYear = yr;
            out.push_back(r);
        }
        RegCloseKey(dynKey);
        RegCloseKey(key);
        return true;
    }
    if (dynOpen == ERROR_SUCCESS)
    {
        RegCloseKey(dynKey);
    }

    if (haveBase)
    {
        MachineRule r;
        ParseRegTzi(baseBlob, r);
        r.firstYear = 1;
        r.lastYear = 9999;
        out.push_back(r);
        RegCloseKey(key);
        return true;
    }
    RegCloseKey(key);
    return false;
}

// Fallback: the STATIC GetTimeZoneInformation structure (no dynamic table) --
// the registry reader above is authoritative on the OS; this covers a
// registry-query failure so the per-date surface never crashes.
bool BuildMachineRulesFromStatic(std::vector<MachineRule>& out)
{
    TIME_ZONE_INFORMATION tz = {};
    if (GetTimeZoneInformation(&tz) == TIME_ZONE_ID_INVALID)
    {
        return false;
    }
    MachineRule r;
    r.baseOffsetSeconds = -(static_cast<int64_t>(tz.Bias) + tz.StandardBias) * 60;
    r.dstOffsetSeconds = -(static_cast<int64_t>(tz.Bias) + tz.DaylightBias) * 60;
    auto st = [](const SYSTEMTIME& s, TzTransition& t) {
        t.month = s.wMonth;
        t.day = s.wDay;
        t.dayOfWeek = s.wDayOfWeek;
        t.isFloating = (s.wYear == 0);
        t.timeTicks = static_cast<int64_t>(s.wHour) * kTicksPerHour
                    + static_cast<int64_t>(s.wMinute) * kTicksPerMinute
                    + static_cast<int64_t>(s.wSecond) * kTicksPerSecond
                    + static_cast<int64_t>(s.wMilliseconds) * (kTicksPerSecond / 1000);
    };
    st(tz.StandardDate, r.fallTrans);
    st(tz.DaylightDate, r.springTrans);
    r.supportsDst = tz.StandardDate.wMonth != 0;
    r.firstYear = 1;
    r.lastYear = 9999;
    out.push_back(r);
    return true;
}
#endif // _WIN32

// The fallback offset whenever the per-date machinery cannot supply year
// rules (registry failure): the current-instant source (same contract).
int64_t CurrentOffsetFallback()
{
    return il2cpp_timezone_get_utc_offset();
}

#if !defined(_WIN32)
#include <ctime>
// POSIX per-date offset: localtime_r/gmtime_r at the UTC instant that
// corresponds to the DateTime's civil face approximated by the CURRENT
// offset -- the system tzdata applies the real per-date DST rules, so
// converting the civil wall through the current bias and reading the field
// difference reproduces the zone's offset that day. Out-of-time_t-range
// dates fall back to the current offset.
int64_t PerDateOffsetPosix(Il2CppDateTime dt)
{
    if (dt.ticks < kUnixEpochTicks)
    {
        return CurrentOffsetFallback();
    }
    const int64_t current = CurrentOffsetFallback();
    int64_t utcTicks = dt.ticks - current * kTicksPerSecond;
    if (utcTicks < 0)
    {
        utcTicks = 0;
    }
    const std::time_t t = static_cast<std::time_t>(utcTicks / kTicksPerSecond);
    std::tm localTm = {};
    std::tm utcTm = {};
    if (localtime_r(&t, &localTm) == nullptr || gmtime_r(&t, &utcTm) == nullptr)
    {
        return current;
    }
    const int64_t localDays = DaysFromCivil(localTm.tm_year + 1900, localTm.tm_mon + 1, localTm.tm_mday);
    const int64_t utcDays = DaysFromCivil(utcTm.tm_year + 1900, utcTm.tm_mon + 1, utcTm.tm_mday);
    const int64_t localSeconds = localDays * 86400 + localTm.tm_hour * 3600
                               + localTm.tm_min * 60 + localTm.tm_sec;
    const int64_t utcSeconds = utcDays * 86400 + utcTm.tm_hour * 3600
                             + utcTm.tm_min * 60 + utcTm.tm_sec;
    return localSeconds - utcSeconds;
}
#endif // !defined(_WIN32)

// The matching rule for a civil date + the per-date offset evaluation.
int64_t EvaluatePerDate(const std::vector<MachineRule>& rules, Il2CppDateTime dt, bool& isDst)
{
    isDst = false;
    int32_t y = 0;
    int32_t m = 0;
    int32_t d = 0;
    CivilFromDaysEpoch(dt.ticks / kTicksPerDay, y, m, d);
    const MachineRule* match = nullptr;
    for (size_t i = 0; i < rules.size(); ++i)
    {
        if (y >= rules[i].firstYear && y <= rules[i].lastYear)
        {
            match = &rules[i];
            break;
        }
    }
    const int32_t baseOff = match != nullptr ? match->baseOffsetSeconds : 0;
    if (match == nullptr || !match->supportsDst)
    {
        return baseOff; // no DST rule for the year -> the base offset only
    }
    YearWindow w = {};
    if (!ResolveYearWindow(*match, y, w))
    {
        return baseOff;
    }
    if (w.dstDeltaTicks <= 0)
    {
        return baseOff; // a zero/negative save is not a DST period
    }
    if (dt.kind == 1 /* DateTimeKind.Utc */)
    {
        isDst = (w.startUtc <= dt.ticks && dt.ticks < w.endUtc);
    }
    else
    {
        const int64_t candidate = dt.ticks - static_cast<int64_t>(baseOff) * kTicksPerSecond;
        isDst = (w.startUtc + w.dstDeltaTicks <= candidate && candidate < w.endUtc);
    }
    return isDst ? match->dstOffsetSeconds : baseOff;
}

bool LoadMachineRules(std::vector<MachineRule>& out)
{
#if defined(_WIN32)
    if (BuildMachineRulesFromRegistry(out) || BuildMachineRulesFromStatic(out))
    {
        return true;
    }
#else
    (void)out;
#endif
    return false;
}

} // namespace

int64_t il2cpp_timezone_get_utc_offset_at(Il2CppDateTime dt)
{
    std::vector<MachineRule> rules;
    if (LoadMachineRules(rules))
    {
        bool isDst = false;
        return EvaluatePerDate(rules, dt, isDst);
    }
#if defined(_WIN32)
    return CurrentOffsetFallback();
#else
    return PerDateOffsetPosix(dt);
#endif
}

int64_t il2cpp_timezone_get_utc_offset_legacy(Il2CppDateTime dt)
{
    // net48 TimeZoneInfoWrapper::GetUtcOffset -- the obsolete TimeZone API:
    // a Kind==Utc input returns TimeSpan.Zero, otherwise the per-date offset.
    if (dt.kind == 1 /* DateTimeKind.Utc */)
    {
        return 0;
    }
    std::vector<MachineRule> rules;
    if (LoadMachineRules(rules))
    {
        bool isDst = false;
        return EvaluatePerDate(rules, dt, isDst);
    }
#if defined(_WIN32)
    return CurrentOffsetFallback();
#else
    return PerDateOffsetPosix(dt);
#endif
}

int32_t il2cpp_timezone_get_is_daylight_saving_at(Il2CppDateTime dt)
{
#if defined(_WIN32)
    std::vector<MachineRule> rules;
    if (LoadMachineRules(rules))
    {
        bool isDst = false;
        EvaluatePerDate(rules, dt, isDst);
        return isDst ? 1 : 0;
    }
    return 0;
#else
    // POSIX: the CRT's tm_isdst at the instant (the same data the offset
    // path uses).
    if (dt.ticks >= kUnixEpochTicks)
    {
        const std::time_t t = static_cast<std::time_t>((dt.ticks - kUnixEpochTicks) / kTicksPerSecond);
        std::tm localTm = {};
        if (localtime_r(&t, &localTm) != nullptr)
        {
            return localTm.tm_isdst > 0 ? 1 : 0;
        }
    }
    return 0;
#endif
}

int32_t il2cpp_timezone_get_is_invalid_time(Il2CppDateTime dt)
{
#if defined(_WIN32)
    std::vector<MachineRule> rules;
    if (!LoadMachineRules(rules))
    {
        return 0;
    }
    int32_t y = 0;
    int32_t m = 0;
    int32_t d = 0;
    CivilFromDaysEpoch(dt.ticks / kTicksPerDay, y, m, d);
    for (size_t i = 0; i < rules.size(); ++i)
    {
        if (y >= rules[i].firstYear && y <= rules[i].lastYear)
        {
            if (!rules[i].supportsDst)
            {
                return 0;
            }
            YearWindow w = {};
            if (!ResolveYearWindow(rules[i], y, w) || w.dstDeltaTicks <= 0)
            {
                return 0;
            }
            // The non-existent spring-forward hour [springWall,
            // springWall + dstDelta) read on the wall clock.
            return (w.springWall <= dt.ticks && dt.ticks < w.springWall + w.dstDeltaTicks) ? 1 : 0;
        }
    }
    return 0;
#else
    return 0; // POSIX: no registry-rule model -- the invalid-hour surface is unmodelled
#endif
}

} // namespace runtime
} // namespace il2cpp
