//------------------------------------------------------------------------------
// timezone_registry.cpp -- minimal TimeZoneInfo zone registry (tzimpl).
//
// The simplification domain in timezone.cpp evaluates ONE current-instant
// offset (no zone objects, no dates). This translation unit adds the minimal
// usable subset for System.TimeZoneInfo.FindSystemTimeZoneById +
// ConvertTimeFromUtc (MethodMap.TimeZone.cs):
//
//   * CLOSED zone set (the only ids the registry resolves):
//       "Eastern Standard Time"  base UTC-5  + US DST
//       "Pacific Standard Time"  base UTC-8  + US DST
//       "Central Standard Time"  base UTC-6  + US DST
//       "UTC"                    base UTC+0  (no DST)
//   * US 2007+ DST rules for the three US zones: DST starts the SECOND
//     Sunday of March at 02:00 LOCAL STANDARD time and ends the FIRST Sunday
//     of November at 02:00 LOCAL DAYLIGHT time (the post-2007 rule set).
//   * INSTANTS BEFORE 2007 throw NotSupportedException carrying the zone id:
//     the pre-2007 US rules (and every historical rule globally) are out of
//     the modelled subset -- a silent wrong offset is never produced (the
//     LOUD boundary).
//   * Any OTHER zone id throws TimeZoneNotFoundException carrying the id
//     (net48 alignment, TZEXC: no fallback to the machine zone).
//
// Handle model: FindSystemTimeZoneById returns the ADDRESS of the static
// ZoneDef entry reinterpreted as Il2CppObject* -- a STABLE handle (static
// storage duration, one address per supported id, no allocation, no GC
// involvement). ConvertTimeFromUtc accepts that handle; the runtime never
// dereferences managed memory.
//
// Exception messages use a thread-local composed-message scratch (the
// exceptionext.cpp contract: DotNetException stores the char* WITHOUT
// copying, so the storage must outlive unwinding; the scratch is private per
// thread and needs no heap).
//------------------------------------------------------------------------------

#include "timezone.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_not_supported_exception etc.
#include "il2cpp_string.h"        // Il2CppString (id compare)

#include <cstddef>
#include <cstdint>
#include <cstdlib>

namespace il2cpp {
namespace runtime {

namespace {

constexpr int64_t kTicksPerSecond = 10000000LL;
constexpr int64_t kTicksPerHour = 3600LL * kTicksPerSecond;
constexpr int64_t kTicksPerDay = 24LL * kTicksPerHour;

// The DST zones model the post-2007 US rule set only (see the file header).
constexpr int32_t kFirstModelledYear = 2007;
constexpr int32_t kDstSaveSeconds = 3600;

struct ZoneDef
{
    const char* id;
    int32_t standardOffsetSeconds; // local - UTC, +east
    bool usDst;
};

const ZoneDef kZones[] = {
    { "Eastern Standard Time", -5 * 3600, true },
    { "Pacific Standard Time", -8 * 3600, true },
    { "Central Standard Time", -6 * 3600, true },
    { "UTC", 0, false },
};
constexpr int32_t kZoneCount = static_cast<int32_t>(sizeof(kZones) / sizeof(kZones[0]));

// Thread-local composed-message scratch (the exceptionext.cpp lifetime
// contract): DotNetException stores the char* WITHOUT copying, so the text
// must outlive unwinding. Clamped + always NUL-terminated.
thread_local char g_tzMessageScratch[256];

const char* ComposeTzMessage(const char* prefix, const char* middle, const char* suffix)
{
    char* p = g_tzMessageScratch;
    char* const end = g_tzMessageScratch + sizeof(g_tzMessageScratch) - 1;
    const char* parts[3] = { prefix, middle, suffix };
    for (int i = 0; i < 3; ++i)
    {
        const char* s = parts[i] != nullptr ? parts[i] : "";
        while (*s != '\0' && p < end)
        {
            *p++ = *s++;
        }
    }
    *p = '\0';
    return g_tzMessageScratch;
}

[[noreturn]] void ThrowZoneNotFound(const char* id)
{
    // net48 message shape (TZEXC): "The time zone ID '<id>' was not found on
    // the local computer." Composed into the thread-local scratch.
    il2cpp_throw_time_zone_not_found_exception(ComposeTzMessage(
        "The time zone ID '", id, "' was not found on the local computer."));
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort(); // defensive backstop (same pattern as xml_query.cpp)
#endif
}

[[noreturn]] void ThrowYearNotModelled(const char* id)
{
    il2cpp_throw_not_supported_exception(ComposeTzMessage(
        "TimeZoneInfo: zone '", id,
        "' requires the pre-2007 adjustment rules; the minimal registry models "
        "the US 2007+ DST rules only"));
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort(); // defensive backstop
#endif
}

bool EqualsAscii(const Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    int32_t i = 0;
    for (; ascii[i] != '\0'; ++i)
    {
        if (i >= s->length ||
            s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return s->length == i;
}

// Copies the managed id into 'out' (ASCII; non-ASCII -> '?'), bounded and
// NUL-terminated. Only used for the LOUD messages.
void CopyIdToBuffer(const Il2CppString* id, char* out, size_t capacity)
{
    size_t n = 0;
    if (id != nullptr)
    {
        for (int32_t i = 0; i < id->length && n + 1 < capacity; ++i)
        {
            const char16_t ch = id->chars[i];
            out[n++] = (ch >= 0x20 && ch < 0x7F) ? static_cast<char>(ch) : '?';
        }
    }
    out[n] = '\0';
}

//------------------------------------------------------------------------------
// Proleptic-Gregorian civil-calendar helpers (Howard Hinnant's algorithms).
// days counts from 0001-01-01 (the DateTime tick epoch).
//------------------------------------------------------------------------------
int64_t DaysFromCivil(int64_t y, int64_t m, int64_t d)
{
    y -= (m <= 2) ? 1 : 0;
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;
    const int64_t doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    return era * 146097 + doe - 719468 + 719162;
}

void CivilFromDays(int64_t days, int64_t& y, int64_t& m, int64_t& d)
{
    const int64_t z = days + 306; // days + (719468 - 719162): shift to the 0000-03-01 base
    const int64_t era = (z >= 0 ? z : z - 146096) / 146097;
    const int64_t doe = z - era * 146097;
    const int64_t yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    y = yoe + era * 400;
    const int64_t doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    const int64_t mp = (5 * doy + 2) / 153;
    d = doy - (153 * mp + 2) / 5 + 1;
    m = mp < 10 ? mp + 3 : mp - 9;
    if (m <= 2)
    {
        ++y;
    }
}

// Sunday = 0 (0001-01-01 is a Monday, so (days + 1) % 7 is Sunday-based).
int WeekdayOf(int64_t days)
{
    return static_cast<int>((days + 1) % 7);
}

// Day-of-month of the n-th (1-based) Sunday of (y, m).
int64_t NthSundayOfMonth(int64_t y, int64_t m, int n)
{
    const int wd = WeekdayOf(DaysFromCivil(y, m, 1));
    const int64_t firstSunday = 1 + ((7 - wd) % 7);
    return firstSunday + 7 * (n - 1);
}

// The zone's offset at the instant (local - UTC, seconds). Throws the LOUD
// pre-2007 boundary for the DST zones.
int32_t OffsetSecondsAt(const ZoneDef& z, int64_t utcTicks)
{
    if (!z.usDst)
    {
        return z.standardOffsetSeconds;
    }
    const int64_t days = utcTicks / kTicksPerDay; // DateTime ticks are >= 0
    int64_t y = 0;
    int64_t m = 0;
    int64_t d = 0;
    CivilFromDays(days, y, m, d);
    if (y < kFirstModelledYear)
    {
        ThrowYearNotModelled(z.id);
    }
    // DST window in UTC: start 02:00 local STANDARD on the 2nd Sunday of
    // March; end 02:00 local DAYLIGHT on the 1st Sunday of November.
    const int64_t startUtc = DaysFromCivil(y, 3, NthSundayOfMonth(y, 3, 2)) * kTicksPerDay
                           + 2 * kTicksPerHour
                           - static_cast<int64_t>(z.standardOffsetSeconds) * kTicksPerSecond;
    const int64_t endUtc = DaysFromCivil(y, 11, NthSundayOfMonth(y, 11, 1)) * kTicksPerDay
                         + 2 * kTicksPerHour
                         - static_cast<int64_t>(z.standardOffsetSeconds + kDstSaveSeconds) * kTicksPerSecond;
    return (utcTicks >= startUtc && utcTicks < endUtc)
        ? z.standardOffsetSeconds + kDstSaveSeconds
        : z.standardOffsetSeconds;
}

} // namespace

Il2CppObject* il2cpp_timezone_find_by_id(Il2CppString* id)
{
    if (id == nullptr)
    {
        il2cpp_throw_argument_null_exception("id");
    }
    for (int32_t i = 0; i < kZoneCount; ++i)
    {
        if (EqualsAscii(id, kZones[i].id))
        {
            return reinterpret_cast<Il2CppObject*>(const_cast<ZoneDef*>(&kZones[i]));
        }
    }
    char idBuffer[96];
    CopyIdToBuffer(id, idBuffer, sizeof(idBuffer));
    ThrowZoneNotFound(idBuffer);
}

Il2CppDateTime il2cpp_timezone_convert_from_utc(Il2CppDateTime utc, Il2CppObject* tz)
{
    if (tz == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    const ZoneDef* zone = reinterpret_cast<const ZoneDef*>(tz);
    const int32_t offset = OffsetSecondsAt(*zone, utc.ticks);
    return Il2CppDateTime(utc.ticks + static_cast<int64_t>(offset) * kTicksPerSecond);
}

} // namespace runtime
} // namespace il2cpp
