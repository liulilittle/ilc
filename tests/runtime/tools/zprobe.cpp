//------------------------------------------------------------------------------
// zprobe.cpp -- POSIX per-date TimeZone offset probe (nb-tz evidence tool).
//
// Drives the PUBLIC runtime API (il2cpp_timezone_get_utc_offset_at /
// il2cpp_timezone_get_is_daylight_saving_at) under a sequence of TZ zones and
// fixed 2000 dates, printing the same CSV shape as oracle.cs:
//
//   zone,date,kind,offset_seconds,isdst
//
// Build (POSIX / WSL), from the repo root:
//   g++ -std=c++17 -I src/runtime/include/internal tests/runtime/tools/zprobe.cpp src/runtime/src/timezone.cpp -o zprobe
// The TZ environment variable is switched per zone; the process must be able
// to read the system tzdata (/usr/share/zoneinfo). See
// tests/runtime/README-timezone-posix.md for the full evidence flow.
//------------------------------------------------------------------------------
#include "timezone.h"

#include <cstdio>
#include <cstdlib>
#include <ctime>

using il2cpp::runtime::Il2CppDateTime;

namespace {

constexpr int64_t kTicksPerSecond = 10000000LL;

// Civil date -> DateTime ticks (Hinnant days_from_civil + the 719162-day
// .NET-epoch shift).
int64_t DaysFromCivilEpoch(int64_t y, int64_t m, int64_t d)
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
    return DaysFromCivilEpoch(y, mo, d) * 86400LL * kTicksPerSecond
         + static_cast<int64_t>(h) * 3600 * kTicksPerSecond;
}

struct Zone
{
    const char* posixId;
    const char* label;
};

const Zone kZones[] = {
    { "UTC",                  "UTC" },
    { "Asia/Shanghai",        "Asia/Shanghai" },
    { "America/New_York",     "America/New_York" },
    { "Australia/Lord_Howe",  "Australia/Lord_Howe" },
    { "Europe/Dublin",        "Europe/Dublin" },
    { "Pacific/Auckland",     "Pacific/Auckland" },
    { "Australia/Sydney",     "Australia/Sydney" },
    { "Asia/Kolkata",         "Asia/Kolkata" },
};

const int kMonths[] = { 1, 4, 7, 10 };

} // namespace

int main()
{
    std::printf("zone,date,kind,offset_seconds,isdst\n");
    for (const Zone& z : kZones)
    {
        setenv("TZ", z.posixId, 1);
        tzset();
        for (int m : kMonths)
        {
            for (int kind = 0; kind <= 1; ++kind)
            {
                const int64_t wall = Ticks(2000, m, 1, 12);
                const Il2CppDateTime dt(wall, kind == 1 ? 1 : 0);
                const int64_t off = il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(dt);
                const int32_t dst =
                    il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at(dt);
                std::printf("%s,2000-%02d-01,%s,%lld,%d\n",
                            z.label, m, kind == 1 ? "utc" : "wall",
                            static_cast<long long>(off), dst);
            }
        }
    }
    return 0;
}
