//------------------------------------------------------------------------------
// test_timezone_registry.cpp -- tzimpl unit tests: the minimal TimeZoneInfo
// zone registry + per-date conversion (timezone.h, timezone_registry.cpp).
//
// Pins (net48-oracle-matched values, tzprobe, 2026-09-13):
//   * closed set: Eastern/Pacific/Central Standard Time + UTC; stable
//     handles (same pointer per id); every other id throws
//     TimeZoneNotFoundException carrying the id (net48 alignment, TZEXC);
//   * US 2007+ DST rules: DST starts the 2nd Sunday of March 02:00 local
//     standard, ends the 1st Sunday of November 02:00 local daylight;
//     winter/summer instants and all four 2020 boundary instants;
//   * DST-zone instants before 2007 throw NotSupportedException carrying
//     the id (the LOUD boundary -- historical tables not modelled);
//   * UTC converts identity for every year (no DST, no year gate).
//------------------------------------------------------------------------------

#include "timezone.h"

#include "il2cpp_exception.h"
#include "il2cpp_string.h"

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
constexpr int64_t kTicksPerHour = 60LL * kTicksPerMinute;
constexpr int64_t kTicksPerDay = 24LL * kTicksPerHour;

int64_t DaysFromCivil(int64_t y, int64_t m, int64_t d)
{
    y -= (m <= 2) ? 1 : 0;
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;
    const int64_t doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468 + 719162;
}

int64_t Utc(int64_t y, int64_t mo, int64_t d, int64_t h, int64_t mi, int64_t s)
{
    return DaysFromCivil(y, mo, d) * kTicksPerDay + h * kTicksPerHour + mi * kTicksPerMinute + s * kTicksPerSecond;
}

template <typename Fn>
bool ThrowsWithMessage(Fn fn, const char* typeFragment, const char* messageFragment)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const bool typeOk = std::strstr(e.type_name(), typeFragment) != nullptr;
        const bool msgOk = e.message_text() != nullptr
            && std::strstr(e.message_text(), messageFragment) != nullptr;
        return typeOk && msgOk;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

il2cpp::Il2CppString* Cs(const char* s)
{
    return il2cpp::il2cpp_string_new_from_cs(s);
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using il2cpp::Il2CppObject;
    using il2cpp::Il2CppString;

    // 1. The closed set resolves; handles are stable (same pointer).
    Il2CppObject* eastern = il2cpp_timezone_find_by_id(Cs("Eastern Standard Time"));
    Il2CppObject* eastern2 = il2cpp_timezone_find_by_id(Cs("Eastern Standard Time"));
    Il2CppObject* pacific = il2cpp_timezone_find_by_id(Cs("Pacific Standard Time"));
    Il2CppObject* central = il2cpp_timezone_find_by_id(Cs("Central Standard Time"));
    Il2CppObject* utc = il2cpp_timezone_find_by_id(Cs("UTC"));
    Expect(eastern != nullptr && pacific != nullptr && central != nullptr && utc != nullptr,
           "all four supported ids resolve");
    Expect(eastern == eastern2, "the handle is stable across lookups");
    Expect(eastern != pacific && pacific != central && central != utc,
           "distinct zones have distinct handles");

    // 2. Eastern winter/summer instants (net48 oracle).
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 1, 15, 12, 0, 0)), eastern).ticks
               == Utc(2020, 1, 15, 7, 0, 0),
           "Eastern winter 12:00Z -> 07:00 (UTC-5)");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 7, 15, 12, 0, 0)), eastern).ticks
               == Utc(2020, 7, 15, 8, 0, 0),
           "Eastern summer 12:00Z -> 08:00 (UTC-4)");

    // 3. Eastern 2020 DST boundaries: start 03-08 07:00Z, end 11-01 06:00Z.
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 3, 8, 6, 59, 59)), eastern).ticks
               == Utc(2020, 3, 8, 1, 59, 59),
           "Eastern DST start -1s: still EST");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 3, 8, 7, 0, 0)), eastern).ticks
               == Utc(2020, 3, 8, 3, 0, 0),
           "Eastern DST start: 07:00Z -> 03:00 EDT");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 11, 1, 5, 59, 59)), eastern).ticks
               == Utc(2020, 11, 1, 1, 59, 59),
           "Eastern DST end -1s: still EDT");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 11, 1, 6, 0, 0)), eastern).ticks
               == Utc(2020, 11, 1, 1, 0, 0),
           "Eastern DST end: 06:00Z -> 01:00 EST");

    // 4. Pacific / Central winter+summer offsets (net48 oracle).
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 1, 15, 12, 0, 0)), pacific).ticks
               == Utc(2020, 1, 15, 4, 0, 0),
           "Pacific winter 12:00Z -> 04:00 (UTC-8)");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 7, 15, 12, 0, 0)), pacific).ticks
               == Utc(2020, 7, 15, 5, 0, 0),
           "Pacific summer 12:00Z -> 05:00 (UTC-7)");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 1, 15, 12, 0, 0)), central).ticks
               == Utc(2020, 1, 15, 6, 0, 0),
           "Central winter 12:00Z -> 06:00 (UTC-6)");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 7, 15, 12, 0, 0)), central).ticks
               == Utc(2020, 7, 15, 7, 0, 0),
           "Central summer 12:00Z -> 07:00 (UTC-5)");

    // 5. UTC: identity for every year (no DST, no year gate).
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 7, 15, 12, 0, 0)), utc).ticks
               == Utc(2020, 7, 15, 12, 0, 0),
           "UTC identity (2020)");
    Expect(il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(1995, 7, 15, 12, 0, 0)), utc).ticks
               == Utc(1995, 7, 15, 12, 0, 0),
           "UTC identity (1995 -- no pre-2007 gate for UTC)");

    // 6. LOUD boundaries: unknown id (net48 TimeZoneNotFoundException, TZEXC)
    // and pre-2007 DST instants (NotSupported) carry the id.
    Expect(ThrowsWithMessage([&] { (void)il2cpp_timezone_find_by_id(Cs("W. Europe Standard Time")); },
                             "System.TimeZoneNotFoundException", "W. Europe Standard Time"),
           "unknown id throws TimeZoneNotFound carrying the id");
    Expect(ThrowsWithMessage([&] { (void)il2cpp_timezone_find_by_id(Cs("Custom Zone")); },
                             "System.TimeZoneNotFoundException", "Custom Zone"),
           "custom id throws TimeZoneNotFound carrying the id");
    Expect(ThrowsWithMessage([&] {
               (void)il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2000, 1, 15, 12, 0, 0)), eastern);
           },
                             "System.NotSupportedException", "Eastern Standard Time"),
           "pre-2007 DST instant throws NotSupported carrying the id");

    // 7. Null paths.
    Expect(ThrowsWithMessage([&] { (void)il2cpp_timezone_find_by_id(nullptr); },
                             "System.ArgumentNullException", "id"),
           "null id throws ArgumentNullException");
    Expect(ThrowsWithMessage([&] {
               (void)il2cpp_timezone_convert_from_utc(Il2CppDateTime(Utc(2020, 1, 15, 12, 0, 0)), nullptr);
           },
                             "System.NullReferenceException", ""),
           "null zone handle throws NullReferenceException");

    if (g_failures == 0)
    {
        std::printf("all runtime timezone_registry tests passed\n");
    }
    return g_failures == 0 ? 0 : 1;
}
