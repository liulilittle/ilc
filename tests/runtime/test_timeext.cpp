//------------------------------------------------------------------------------
// test_timeext.cpp -- DateTime.ToString(format) unit tests.
//
// The expected outputs are pinned by the net48 e2e oracle (run on
// .NET Framework 4.8 with CultureInfo.InvariantCulture -- see
// tests/cases/timeext/timeext.cs); every assertion below mirrors an oracle
// line. The fixed-tick DateTime 2020-06-15 13:45:30 =
// 637278255300000000 ticks (midnight 637277760000000000 + 49530 s) -- the
// same value test_time.cpp pins via the construction icalls.
//
// Coverage:
//   1. Standard single-letter formats (d/D/t/T/f/F) -- PROVIDER-AWARE: the
//      invariant family via the empty-name carrier (the default in this
//      file), the zh-CN host-default family via nullptr/"zh-CN" (section 10).
//   2. null / empty / non-string format -> General "G" default.
//   3. Custom tokens: y (1/2/3/4/5 lengths incl. last-two-digit "yy"),
//      M/d numeric + MMM/MMMM/ddd/dddd names, H/h/m/s padding rules
//      (HHH/hhh/mmm/sss are 2-digit), 12-hour clock boundaries.
//   4. Month / weekday name tables (net48 en-US) for all 12 / 7 entries.
//   5. Year-1 and MaxValue edges; error path: unknown single LETTER
//      formats throw FormatException (net48 contract).
//
// Like the other runtime tests: plain executable, failed Expect() prints
// FAIL and bumps the counter; process exits non-zero on any failure.
//------------------------------------------------------------------------------

#include "il2cpp_time_ext.h"
#include "timezone.h" // il2cpp_datetime_from_local (standard "U" check)

#include "il2cpp_exception.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

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

// Runs fn; returns true iff it throws a DotNetException whose type chain
// contains 'typeFragment' (e.g. "System.FormatException").
template <typename Fn>
bool ThrowsWithType(Fn fn, const char* typeFragment)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeFragment) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// True iff the managed string's payload equals the ASCII literal.
bool StrEquals(const il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const int32_t len = static_cast<int32_t>(std::strlen(ascii));
    if (s->length != len)
    {
        return false;
    }
    for (int32_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

// True iff two managed strings have equal payloads.
bool StrEquals(const il2cpp::Il2CppString* a, const il2cpp::Il2CppString* b)
{
    if (a == nullptr || b == nullptr)
    {
        return a == b;
    }
    if (a->length != b->length)
    {
        return false;
    }
    for (int32_t i = 0; i < a->length; ++i)
    {
        if (a->chars[i] != b->chars[i])
        {
            return false;
        }
    }
    return true;
}

// True iff the managed string's payload equals the UTF-16 literal.
bool StrEqualsUtf16(const il2cpp::Il2CppString* s, const char16_t* text)
{
    if (s == nullptr)
    {
        return false;
    }
    int32_t len = 0;
    while (text[len] != 0)
    {
        ++len;
    }
    if (s->length != len)
    {
        return false;
    }
    for (int32_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != text[i])
        {
            return false;
        }
    }
    return true;
}

// Formats dt with an ASCII format string and an optional culture carrier:
// "" (default) = the INVARIANT carrier (this file's pinned net48 oracle),
// nullptr = host default (zh-CN), "zh-CN" = the zh-CN family, any other
// name = unknown -> invariant fallback (+ optional log).
il2cpp::Il2CppString* Fmt(il2cpp::runtime::Il2CppDateTime dt, const char* asciiFormat,
                          const char* providerName = "")
{
    il2cpp::Il2CppString* fs = il2cpp::il2cpp_string_new_from_cs(asciiFormat);
    il2cpp::Il2CppObject* provider = providerName == nullptr
        ? nullptr
        : reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(providerName));
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::runtime::icall_datetime_to_string_format(
            dt, reinterpret_cast<il2cpp::Il2CppObject*>(fs), provider));
}

// 2020-06-15 13:45:30 as raw ticks (fixed value; see the header comment).
constexpr int64_t kTicks2020Midnight = 637277760000000000;
constexpr int64_t kTicks2020 = kTicks2020Midnight + 495300000000;
// 13:45:30 with fractional seconds (used by O / f / F assertions).
constexpr int64_t kTicks2020Frac123 = 637278255301230000LL; // 13:45:30.123
constexpr int64_t kTicks2020Frac7 = 637278255301234567LL;   // 13:45:30.1234567

} // namespace

int main()
{
    using il2cpp::runtime::Il2CppDateTime;
    using il2cpp::runtime::icall_datetime_from_ymdhms;
    using il2cpp::runtime::icall_datetime_to_string_format;

    const Il2CppDateTime dt2020(kTicks2020); // 2020-06-15 13:45:30 (Monday)

    // 1. Standard single-letter formats (invariant patterns, net48 oracle).
    Expect(StrEquals(Fmt(dt2020, "d"), "06/15/2020"),
           "standard 'd' == MM/dd/yyyy");
    Expect(StrEquals(Fmt(dt2020, "D"), "Monday, 15 June 2020"),
           "standard 'D' == dddd, dd MMMM yyyy");
    Expect(StrEquals(Fmt(dt2020, "t"), "13:45"), "standard 't' == HH:mm");
    Expect(StrEquals(Fmt(dt2020, "T"), "13:45:30"), "standard 'T' == HH:mm:ss");
    Expect(StrEquals(Fmt(dt2020, "f"), "Monday, 15 June 2020 13:45"),
           "standard 'f' == dddd, dd MMMM yyyy HH:mm");
    Expect(StrEquals(Fmt(dt2020, "F"), "Monday, 15 June 2020 13:45:30"),
           "standard 'F' == dddd, dd MMMM yyyy HH:mm:ss");

    // 2. null / empty / non-string format -> General "G" (existing default).
    Expect(StrEquals(reinterpret_cast<il2cpp::Il2CppString*>(
                         icall_datetime_to_string_format(dt2020, nullptr,
                             reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("")))),
                     "06/15/2020 13:45:30"),
           "null format + invariant carrier == invariant General 'G'");
    Expect(StrEquals(Fmt(dt2020, ""), "06/15/2020 13:45:30"),
           "empty format == General 'G' (net48 rule)");
    il2cpp::Il2CppObject fakeObj; // non-string object: defensive path
    fakeObj.klass = nullptr;      // never dereferenced (klass mismatch short-circuits)
    Expect(StrEquals(reinterpret_cast<il2cpp::Il2CppString*>(
                         icall_datetime_to_string_format(dt2020, &fakeObj,
                             reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("")))),
                     "06/15/2020 13:45:30"),
           "non-string format object -> General 'G' default");

    // 3. Custom tokens on the fixed date.
    Expect(StrEquals(Fmt(dt2020, "yyyy-MM-dd HH:mm:ss"), "2020-06-15 13:45:30"),
           "custom 'yyyy-MM-dd HH:mm:ss'");
    Expect(StrEquals(Fmt(dt2020, "yy/MM/dd hh:mm:ss"), "20/06/15 01:45:30"),
           "custom 'yy/MM/dd hh:mm:ss' (yy=last two, hh=12-hour)");
    Expect(StrEquals(Fmt(dt2020, "MMM ddd"), "Jun Mon"),
           "MMM + ddd abbreviated names");
    Expect(StrEquals(Fmt(dt2020, "MMMM dddd"), "June Monday"),
           "MMMM + dddd full names");
    Expect(StrEquals(Fmt(dt2020, "H h m s"), "13 1 45 30"),
           "single H/h/m/s tokens are unpadded");
    Expect(StrEquals(Fmt(dt2020, "yyyy"), "2020"), "yyyy == 2020");
    Expect(StrEquals(Fmt(dt2020, "yy"), "20"), "yy == last two digits");
    Expect(StrEquals(Fmt(dt2020, "yyy"), "2020"), "yyy == min 3 digits");
    Expect(StrEquals(Fmt(dt2020, "yyyyy"), "02020"), "yyyyy == min 5 digits");
    Expect(StrEquals(Fmt(dt2020, "HH"), "13"), "HH == 2-digit hour");
    Expect(StrEquals(Fmt(dt2020, "HHH"), "13"), "HHH is 2-digit padded (net48)");
    Expect(StrEquals(Fmt(dt2020, "hh"), "01"), "hh == 2-digit 12-hour");
    Expect(StrEquals(Fmt(dt2020, "hhh"), "01"), "hhh is 2-digit padded (net48)");
    Expect(StrEquals(Fmt(dt2020, "mm"), "45"), "mm == 2-digit minute");
    Expect(StrEquals(Fmt(dt2020, "mmm"), "45"), "mmm is 2-digit padded (net48)");
    Expect(StrEquals(Fmt(dt2020, "ss"), "30"), "ss == 2-digit second");
    Expect(StrEquals(Fmt(dt2020, "sss"), "30"), "sss is 2-digit padded (net48)");
    Expect(StrEquals(Fmt(dt2020, "d/M"), "15/6"),
           "custom single d/M == day/month unpadded");
    Expect(StrEquals(Fmt(dt2020, "dd"), "15"), "dd == 2-digit day");
    Expect(StrEquals(Fmt(dt2020, "dddd, MMMM d, yyyy"), "Monday, June 15, 2020"),
           "custom en-US style with literals");
    // A SINGLE-character format string is a standard-specifier lookup, so a
    // non-letter ("/") throws FormatException -- it is not a one-literal
    // custom format (net48 dtz2 oracle). Multi-character formats keep
    // unknown characters as literals (the next assertion).
    Expect(ThrowsWithType([&] { (void)Fmt(dt2020, "/"); },
                          "System.FormatException"),
           "single non-letter '/' throws FormatException (standard lookup)");
    Expect(StrEquals(Fmt(dt2020, "MM:dd"), "06:15"), "literal ':' between tokens");

    // 4. 12-hour clock boundaries (0 -> 12, noon -> 12).
    const Il2CppDateTime midnight = icall_datetime_from_ymdhms(2020, 6, 15, 0, 5, 6);
    Expect(StrEquals(Fmt(midnight, "h:m"), "12:5"),
           "midnight 12-hour: 0 -> 12, single m unpadded");
    Expect(StrEquals(Fmt(midnight, "h:mm"), "12:05"),
           "midnight 12-hour: mm is 2-digit padded (net48)");
    const Il2CppDateTime noon = icall_datetime_from_ymdhms(2020, 6, 15, 12, 0, 0);
    Expect(StrEquals(Fmt(noon, "hh"), "12"), "noon 12-hour == 12");
    Expect(StrEquals(Fmt(dt2020, "h:mm"), "1:45"), "13:45 -> 1:45 in 12-hour");

    // 5. Year-1 / year-5 / MaxValue edges.
    const Il2CppDateTime year5 = icall_datetime_from_ymdhms(5, 6, 15, 0, 0, 0);
    Expect(StrEquals(Fmt(year5, "y-MM-dd"), "5-06-15"), "single y unpadded");
    Expect(StrEquals(Fmt(year5, "yy-MM-dd"), "05-06-15"), "yy zero-pads");
    Expect(StrEquals(Fmt(year5, "yyy-MM-dd"), "005-06-15"), "yyy == min 3");
    Expect(StrEquals(Fmt(year5, "yyyy-MM-dd"), "0005-06-15"), "yyyy == min 4");
    Expect(StrEquals(Fmt(year5, "yyyyy-MM-dd"), "00005-06-15"), "yyyyy == min 5");
    const Il2CppDateTime year1(0); // 0001-01-01 (Monday)
    Expect(StrEquals(Fmt(year1, "yyyy"), "0001"), "year 1 formats 4-digit");
    Expect(StrEquals(Fmt(year1, "dddd"), "Monday"), "0001-01-01 is a Monday");
    const Il2CppDateTime maxDt(3155378975999999999); // 9999-12-31 23:59:59
    Expect(StrEquals(Fmt(maxDt, "yyyy-MM-dd HH:mm:ss"), "9999-12-31 23:59:59"),
           "MaxValue round trip");
    Expect(StrEquals(Fmt(maxDt, "ddd"), "Fri"), "9999-12-31 is a Friday");

    // 6. Name tables (net48 en-US).
    static const char* kMonthAbbr[12] = {"Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                         "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"};
    static const char* kMonthFull[12] = {"January",  "February", "March",   "April",
                                         "May",      "June",     "July",    "August",
                                         "September", "October", "November", "December"};
    for (int32_t m = 1; m <= 12; ++m)
    {
        const Il2CppDateTime first = icall_datetime_from_ymdhms(2020, m, 1, 0, 0, 0);
        Expect(StrEquals(Fmt(first, "MMM"), kMonthAbbr[m - 1]), "month abbreviation table");
        Expect(StrEquals(Fmt(first, "MMMM"), kMonthFull[m - 1]), "month full name table");
    }
    static const char* kDayAbbr[7] = {"Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"};
    static const char* kDayFull[7] = {"Sunday",   "Monday", "Tuesday", "Wednesday",
                                      "Thursday", "Friday", "Saturday"};
    for (int32_t d = 0; d <= 6; ++d) // 2020-06-14 is a Sunday (= 0)
    {
        const Il2CppDateTime day = icall_datetime_from_ymdhms(2020, 6, 14 + d, 0, 0, 0);
        Expect(StrEquals(Fmt(day, "ddd"), kDayAbbr[d]), "weekday abbreviation table");
        Expect(StrEquals(Fmt(day, "dddd"), kDayFull[d]), "weekday full name table");
    }

    // 7. Error path: unknown single-LETTER formats -> FormatException
    // (net48 contract; single 'h' is not a standard specifier either).
    Expect(ThrowsWithType([&] { (void)icall_datetime_to_string_format(dt2020, reinterpret_cast<il2cpp::Il2CppObject*>(Fmt(dt2020, "H")), nullptr); },
                          "System.FormatException"),
           "single 'H' throws FormatException");
    Expect(ThrowsWithType([&] { (void)icall_datetime_to_string_format(dt2020, reinterpret_cast<il2cpp::Il2CppObject*>(Fmt(dt2020, "q")), nullptr); },
                          "System.FormatException"),
           "single 'q' throws FormatException");
    Expect(ThrowsWithType([&] { (void)icall_datetime_to_string_format(dt2020, reinterpret_cast<il2cpp::Il2CppObject*>(Fmt(dt2020, "h")), nullptr); },
                          "System.FormatException"),
           "single 'h' throws FormatException (not a standard specifier)");

    // 8. Standard "G" == the plain ToString() pattern (net48 oracle:
    // invariant G is "MM/dd/yyyy HH:mm:ss").
    Expect(StrEquals(Fmt(dt2020, "G"), "06/15/2020 13:45:30"),
           "standard 'G' == MM/dd/yyyy HH:mm:ss");

    // 8a. Standard O/R/r/u/U (D-FMT-1 fix: these were FormatException --
    // net48 invariant patterns pinned by the fresh 2026-08-12 probe).
    Expect(StrEquals(Fmt(dt2020, "O"), "2020-06-15T13:45:30.0000000"),
           "standard 'O' == ISO8601 round-trip yyyy-MM-ddTHH:mm:ss.fffffff");
    Expect(StrEquals(Fmt(Il2CppDateTime(kTicks2020Frac123), "O"), "2020-06-15T13:45:30.1230000"),
           "standard 'O' keeps 7 fraction digits");
    Expect(StrEquals(Fmt(Il2CppDateTime(kTicks2020Frac7), "O"), "2020-06-15T13:45:30.1234567"),
           "standard 'O' full 7 fraction digits");
    Expect(StrEquals(Fmt(dt2020, "R"), "Mon, 15 Jun 2020 13:45:30 GMT"),
           "standard 'R' == RFC1123 ddd, dd MMM yyyy HH:mm:ss GMT");
    Expect(StrEquals(Fmt(dt2020, "r"), "Mon, 15 Jun 2020 13:45:30 GMT"),
           "standard 'r' == RFC1123 (same as R)");
    Expect(StrEquals(Fmt(dt2020, "u"), "2020-06-15 13:45:30Z"),
           "standard 'u' == universal sortable yyyy-MM-dd HH:mm:ss Z");
    // U converts to UTC first (net48: ToUniversalTime of an Unspecified
    // DateTime treats the wall-clock value as local). The runtime's
    // il2cpp_datetime_from_local does the same on this machine, so U(dt)
    // must equal the full date/time pattern of the UTC-shifted value (the
    // e2e timeformat probe pins the byte-exact string on this UTC+8
    // machine; the unit test pins the conversion contract machine-free).
    Expect(StrEquals(Fmt(dt2020, "U"),
                     Fmt(il2cpp::runtime::il2cpp_datetime_from_local(dt2020),
                         "dddd, dd MMMM yyyy HH:mm:ss")),
           "standard 'U' == full pattern of the UTC-converted value");

    // 8c. Quoted literals and backslash escapes in custom formats (D-FMT-1
    // fix: quotes were emitted literally -- net48 strips them).
    Expect(StrEquals(Fmt(dt2020, "yyyy-MM-dd'T'HH:mm:ss"), "2020-06-15T13:45:30"),
           "quoted literal 'T' is emitted without quotes");
    Expect(StrEquals(Fmt(dt2020, "yyyy-MM-dd\"T\"HH:mm:ss"), "2020-06-15T13:45:30"),
           "double-quoted literal \"T\" is emitted without quotes");
    Expect(StrEquals(Fmt(dt2020, "dddd, dd MMMM yyyy 'at' HH:mm"), "Monday, 15 June 2020 at 13:45"),
           "quoted literal 'at' is emitted verbatim");
    Expect(StrEquals(Fmt(dt2020, "yyyy'-'MM'-'dd"), "2020-06-15"),
           "quoted '-' literals split tokens");
    Expect(StrEquals(Fmt(dt2020, "d'dd'd"), "15dd15"),
           "quoted group 'dd' is literal, d tokens expand");
    Expect(StrEquals(Fmt(dt2020, "yyyy'yy'"), "2020yy"),
           "quoted group 'yy' is literal");
    Expect(StrEquals(Fmt(dt2020, "yyyy\\-MM\\-dd"), "2020-06-15"),
           "backslash escape \\- == literal '-'");
    Expect(StrEquals(Fmt(dt2020, "a\\b"), "ab"),
           "backslash escape \\b == literal 'b'");
    Expect(ThrowsWithType([&] { (void)icall_datetime_to_string_format(dt2020, reinterpret_cast<il2cpp::Il2CppObject*>(Fmt(dt2020, "'unterminated")), nullptr); },
                          "System.FormatException"),
           "unterminated quote throws FormatException");
    Expect(ThrowsWithType([&] { (void)icall_datetime_to_string_format(dt2020, reinterpret_cast<il2cpp::Il2CppObject*>(Fmt(dt2020, "\"unterminated")), nullptr); },
                          "System.FormatException"),
           "unterminated double quote throws FormatException");
    Expect(ThrowsWithType([&] { (void)icall_datetime_to_string_format(dt2020, reinterpret_cast<il2cpp::Il2CppObject*>(Fmt(dt2020, "abc\\")), nullptr); },
                          "System.FormatException"),
           "trailing backslash throws FormatException");

    // 8b. Standard single-letter specifiers y/m/s/g (net48 invariant patterns,
    // pinned by the timeext e2e oracle) and the custom tt AM/PM designator.
    Expect(StrEquals(Fmt(dt2020, "g"), "06/15/2020 13:45"),
           "standard 'g' == short general MM/dd/yyyy HH:mm");
    Expect(StrEquals(Fmt(dt2020, "m"), "June 15"),
           "standard 'm' == month-day MMMM dd");
    Expect(StrEquals(Fmt(dt2020, "y"), "2020 June"),
           "standard 'y' == year-month yyyy MMMM");
    Expect(StrEquals(Fmt(dt2020, "s"), "2020-06-15T13:45:30"),
           "standard 's' == sortable yyyy-MM-ddTHH:mm:ss");
    Expect(StrEquals(Fmt(dt2020, "tt"), "PM"),
           "custom 'tt' == PM designator (afternoon)");
    Expect(StrEquals(Fmt(midnight, "tt"), "AM"),
           "custom 'tt' == AM designator (midnight)");
    Expect(StrEquals(Fmt(dt2020, "Ht"), "13P"),
           "custom single 't' == first designator char (afternoon)");
    Expect(StrEquals(Fmt(midnight, "Ht"), "0A"),
           "custom single 't' == first designator char (midnight)");
    Expect(StrEquals(Fmt(year5, "y"), "0005 June"),
           "standard 'y' on year 5 == \"0005 June\" (yyyy MMMM)");

    // 8d. Timezone tokens z / zz / zzz / K and the era 'g' (dtz1 net48
    // oracle, 2026-10-23): z always prints the machine zone's offset
    // regardless of Kind; K is "" for Unspecified, "Z" for Utc and the
    // numeric offset for Local. The assertions compare against the runtime's
    // OWN offset query so they are machine-independent.
    {
        const int32_t offMin = static_cast<int32_t>(
            il2cpp::runtime::il2cpp_timezone_get_utc_offset_at(dt2020) / 60);
        char expectZ[16];
        char expectZZ[16];
        char expectZZZ[16];
        const int32_t absOff = offMin < 0 ? -offMin : offMin;
        std::snprintf(expectZ, sizeof expectZ, "%c%d", offMin < 0 ? '-' : '+',
                      absOff / 60);
        std::snprintf(expectZZ, sizeof expectZZ, "%c%02d", offMin < 0 ? '-' : '+',
                      absOff / 60);
        std::snprintf(expectZZZ, sizeof expectZZZ, "%c%02d:%02d",
                      offMin < 0 ? '-' : '+', absOff / 60, absOff % 60);

        // Single 'z' / 'K' are standard lookups -> FormatException; the
        // custom engine only sees them via a 2+ character format or '%'.
        Expect(StrEquals(Fmt(dt2020, "zz"), expectZZ), "custom 'zz' == 2-digit offset hours");
        Expect(StrEquals(Fmt(dt2020, "zzz"), expectZZZ), "custom 'zzz' == +HH:mm");
        Expect(StrEquals(Fmt(dt2020, "zzzz"), expectZZZ), "custom 'zzzz' == +HH:mm (same)");
        Expect(StrEquals(Fmt(dt2020, "%z"), expectZ), "custom '%z' == offset hours");
        Expect(StrEquals(Fmt(dt2020, "%K"), ""), "custom '%K' Unspecified == empty");
        Expect(StrEquals(Fmt(dt2020, "Kx"), "x"), "custom 'K' Unspecified == empty");
        Expect(StrEquals(Fmt(dt2020, "KKK"), ""), "custom 'KKK' Unspecified == empty");

        const Il2CppDateTime utc = il2cpp::runtime::icall_datetime_specify_kind(dt2020, 1);
        const Il2CppDateTime local = il2cpp::runtime::icall_datetime_specify_kind(dt2020, 2);
        Expect(StrEquals(Fmt(utc, "%K"), "Z"), "custom '%K' Utc == \"Z\"");
        Expect(StrEquals(Fmt(utc, "KKK"), "ZZZ"), "custom 'KKK' Utc == \"ZZZ\"");
        Expect(StrEquals(Fmt(local, "%K"), expectZZZ), "custom '%K' Local == +HH:mm");
        Expect(StrEquals(Fmt(local, "KK"),
                         std::string(expectZZZ).append(expectZZZ).c_str()),
               "custom 'KK' Local repeats the offset once per K");
        Expect(StrEquals(Fmt(utc, "zzz"), expectZZZ), "zzz ignores Kind (Utc still local offset)");

        // Standard "O" carries the Kind suffix.
        Expect(StrEquals(Fmt(dt2020, "O"), "2020-06-15T13:45:30.0000000"),
               "standard 'O' Unspecified has no suffix");
        Expect(StrEquals(Fmt(utc, "O"), "2020-06-15T13:45:30.0000000Z"),
               "standard 'O' Utc appends \"Z\"");
        Expect(StrEquals(Fmt(local, "O"),
                         std::string("2020-06-15T13:45:30.0000000").append(expectZZZ).c_str()),
               "standard 'O' Local appends +HH:mm");

        // Single 'z' / 'K' are standard lookups -> FormatException.
        Expect(ThrowsWithType([&] { (void)Fmt(dt2020, "z"); },
                              "System.FormatException"),
               "single 'z' throws FormatException (standard lookup)");
        Expect(ThrowsWithType([&] { (void)Fmt(dt2020, "K"); },
                              "System.FormatException"),
               "single 'K' throws FormatException (standard lookup)");

        // Era names.
        Expect(StrEquals(Fmt(dt2020, "gg"), "A.D."), "custom 'gg' invariant == A.D.");
        Expect(StrEquals(Fmt(dt2020, "ggg"), "A.D."), "custom 'ggg' invariant == A.D.");
        Expect(StrEqualsUtf16(Fmt(dt2020, "gg", nullptr), u"\u516C\u5143"),
               "custom 'gg' zh-CN == \u516C\u5143");
        Expect(StrEqualsUtf16(Fmt(dt2020, "tt", nullptr), u"\u4E0B\u5348"),
               "custom 'tt' zh-CN PM == \u4E0B\u5348");
        Expect(StrEqualsUtf16(Fmt(midnight, "tt", nullptr), u"\u4E0A\u5348"),
               "custom 'tt' zh-CN AM == \u4E0A\u5348");
        Expect(StrEqualsUtf16(Fmt(dt2020, "%t", nullptr), u"\u4E0B"),
               "custom '%t' zh-CN == \u4E0B (first char of the PM designator)");
    }

    // 9. Fractional seconds f/F (net48 oracle: .123 s -> fff "123",
    // ffffff "123000"; .000 s -> fff "000", FFF ""; 7 digits max).
    const Il2CppDateTime frac123(kTicks2020Frac123); // 13:45:30.123
    const Il2CppDateTime frac7(kTicks2020Frac7);     // 13:45:30.1234567
    Expect(StrEquals(Fmt(frac123, "yyyy-MM-dd HH:mm:ss.fff"), "2020-06-15 13:45:30.123"),
           "fff == 3 fraction digits");
    Expect(StrEquals(Fmt(frac123, "fff"), "123"), "fff on .123 s == \"123\"");
    Expect(StrEquals(Fmt(frac123, "ffffff"), "123000"), "ffffff zero-pads to 6 digits");
    Expect(StrEquals(Fmt(frac123, "FFF"), "123"), "FFF keeps nonzero digits");
    Expect(StrEquals(Fmt(frac123, "FFFFFF"), "123"), "FFFFFF trims trailing zeros");
    Expect(StrEquals(Fmt(frac7, "fffffff"), "1234567"), "fffffff == 7 fraction digits");
    Expect(StrEquals(Fmt(frac7, "fff"), "123"), "fff truncates past 3 digits");
    Expect(StrEquals(Fmt(dt2020, "fff"), "000"), "fff on .000 s zero-pads");
    Expect(StrEquals(Fmt(dt2020, "FFF"), ""), "FFF on .000 s is empty");
    Expect(ThrowsWithType([&] { (void)icall_datetime_to_string_format(dt2020, reinterpret_cast<il2cpp::Il2CppObject*>(Fmt(dt2020, "ffffffff")), nullptr); },
                          "System.FormatException"),
           "8 fraction digits throws FormatException");

    // 10. Provider routing (cultureprobe regression fix): nullptr = host
    // default zh-CN (saudit5), "" = invariant (sections 1-9), "zh-CN" =
    // explicit current culture, unknown = invariant fallback + optional log.
    auto NullFmt = [&](const char* providerName) {
        il2cpp::Il2CppObject* provider = providerName == nullptr
            ? nullptr
            : reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(providerName));
        return reinterpret_cast<il2cpp::Il2CppString*>(
            icall_datetime_to_string_format(dt2020, nullptr, provider));
    };
    Expect(StrEquals(NullFmt(""), "06/15/2020 13:45:30"),
           "null format + invariant carrier == invariant G");
    Expect(StrEquals(NullFmt(nullptr), "2020/6/15 13:45:30"),
           "null format + null provider == zh-CN G (host default)");
    Expect(StrEquals(Fmt(dt2020, "d", nullptr), "2020/6/15"), "zh default 'd' == yyyy/M/d");
    Expect(StrEquals(Fmt(dt2020, "t", nullptr), "13:45"), "zh default 't' == H:mm");
    Expect(StrEquals(Fmt(dt2020, "T", nullptr), "13:45:30"), "zh default 'T' == H:mm:ss");
    Expect(StrEquals(Fmt(dt2020, "g", nullptr), "2020/6/15 13:45"), "zh default 'g' == yyyy/M/d H:mm");
    Expect(StrEquals(Fmt(dt2020, "G", nullptr), "2020/6/15 13:45:30"), "zh default 'G' == yyyy/M/d H:mm:ss");
    Expect(StrEquals(Fmt(dt2020, "", nullptr), "2020/6/15 13:45:30"),
           "zh default empty format == zh G");
    Expect(StrEqualsUtf16(Fmt(dt2020, "D", nullptr), u"2020\u5E746\u670815\u65E5"),
           "zh default 'D' == yyyy\u5E74M\u6708d\u65E5");
    Expect(StrEqualsUtf16(Fmt(dt2020, "f", nullptr), u"2020\u5E746\u670815\u65E5 13:45"),
           "zh default 'f' == yyyy\u5E74M\u6708d\u65E5 H:mm");
    Expect(StrEqualsUtf16(Fmt(dt2020, "F", nullptr), u"2020\u5E746\u670815\u65E5 13:45:30"),
           "zh default 'F' == yyyy\u5E74M\u6708d\u65E5 H:mm:ss");
    Expect(StrEqualsUtf16(Fmt(dt2020, "m", nullptr), u"6\u670815\u65E5"),
           "zh default 'm' == M\u6708d\u65E5");
    Expect(StrEqualsUtf16(Fmt(dt2020, "y", nullptr), u"2020\u5E746\u6708"),
           "zh default 'y' == yyyy\u5E74M\u6708");
    Expect(StrEquals(Fmt(dt2020, "d", "zh-CN"), "2020/6/15"),
           "explicit zh-CN carrier == host default");
    Expect(StrEquals(Fmt(dt2020, "d", "xx-YY"), "06/15/2020"),
           "unknown provider -> invariant 'd' fallback");
    // Unknown-name log (env-gated; NO stdout/stderr traffic -- the harness
    // byte-diffs both streams).
    std::remove("timeext_provider_probe.log");
#ifdef _WIN32
    _putenv_s("IL2CPP_CULTURE_PROVIDER_LOG", "timeext_provider_probe.log");
#else
    setenv("IL2CPP_CULTURE_PROVIDER_LOG", "timeext_provider_probe.log", 1);
#endif
    Expect(StrEquals(Fmt(dt2020, "d", "xx-YY"), "06/15/2020"),
           "unknown provider still invariant (logged run)");
    bool logged = false;
    std::FILE* lf = std::fopen("timeext_provider_probe.log", "rb");
    if (lf != nullptr)
    {
        char buf[256];
        const size_t n = std::fread(buf, 1, sizeof(buf) - 1, lf);
        buf[n] = '\0';
        logged = std::strstr(buf, "xx-YY") != nullptr
            && std::strstr(buf, "invariant fallback") != nullptr;
        std::fclose(lf);
    }
    Expect(logged, "unknown provider name appended to IL2CPP_CULTURE_PROVIDER_LOG");
#ifdef _WIN32
    _putenv_s("IL2CPP_CULTURE_PROVIDER_LOG", "");
#else
    unsetenv("IL2CPP_CULTURE_PROVIDER_LOG");
#endif
    std::remove("timeext_provider_probe.log");

    if (g_failures == 0)
    {
        std::printf("all runtime timeext tests passed\n");
    }
    return g_failures == 0 ? 0 : 1;
}
