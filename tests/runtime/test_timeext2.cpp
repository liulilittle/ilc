//------------------------------------------------------------------------------
// test_timeext2.cpp -- DateTime extension module 2 unit tests: ParseExact,
// ToString(format) delegation, tick arithmetic, and Compare.
//
// Pinned values (same fixed-tick DateTime as test_time.cpp / test_timeext.cpp):
//   2020-06-15 13:45:30 = 637278255300000000 ticks (midnight
//   637277760000000000 + 49530 s). MaxValue = 3155378975999999999 ticks
//   (9999-12-31 23:59:59), MinValue = 0 (0001-01-01).
//
// Coverage:
//   1. ParseExact round trip through ToString(format) for both supported
//      formats ("yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd" -> midnight).
//   2. ParseExact == construction icall (cross-domain pin) and edges
//      (year 1, MaxValue, leap day).
//   3. ParseExact error matrix: wrong widths, wrong literals, trailing
//      input, out-of-range components, unsupported format tokens, empty
//      format (all FormatException); null input/format (ArgumentNullException).
//   4. AddTicks / SubtractTicks: arithmetic, cross-midnight, exact bounds,
//      overflow both sides (ArgumentOutOfRangeException), INT64 extremes.
//   5. Compare: -1/0/+1 and consistency with icall_datetime_compare_to.
//   6. ToString(format) delegation: round trip + null-format "G" default.
//
// Like the other runtime tests: plain executable, failed Expect() prints
// FAIL and bumps the counter; the process exits non-zero on any failure.
//------------------------------------------------------------------------------

#include "timeext2.h"

#include "il2cpp_exception.h"
#include "il2cpp_string.h"
#include "il2cpp_time.h"

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

// ParseExact with ASCII literals (the production entry point).
il2cpp::runtime::Il2CppDateTime Parse(const char* asciiInput, const char* asciiFormat)
{
    return il2cpp::runtime::il2cpp_datetime_parse_exact(
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(asciiInput)),
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(asciiFormat)));
}

// ToString(format) with an ASCII format string (the production entry point).
il2cpp::Il2CppString* Fmt(il2cpp::runtime::Il2CppDateTime dt, const char* asciiFormat)
{
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::runtime::il2cpp_datetime_to_string_format(
            dt, reinterpret_cast<il2cpp::Il2CppObject*>(
                    il2cpp::il2cpp_string_new_from_cs(asciiFormat))));
}

// Fixed test values (see the header comment).
constexpr int64_t kTicks2020Midnight = 637277760000000000;
constexpr int64_t kTicks2020 = kTicks2020Midnight + 495300000000; // 13:45:30
constexpr int64_t kMaxTicks = 3155378975999999999;

} // namespace

int main()
{
    // Unbuffered stdout: an aborted process (uncaught managed exception)
    // must still show the FAIL lines that led to it.
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    using il2cpp::runtime::Il2CppDateTime;
    using il2cpp::runtime::il2cpp_datetime_parse_exact;
    using il2cpp::runtime::il2cpp_datetime_add_ticks;
    using il2cpp::runtime::il2cpp_datetime_subtract_ticks;
    using il2cpp::runtime::il2cpp_datetime_compare;
    using il2cpp::runtime::icall_datetime_from_ymdhms;
    using il2cpp::runtime::icall_datetime_get_year;
    using il2cpp::runtime::icall_datetime_get_month;
    using il2cpp::runtime::icall_datetime_get_day;
    using il2cpp::runtime::icall_datetime_get_hour;
    using il2cpp::runtime::icall_datetime_get_minute;
    using il2cpp::runtime::icall_datetime_get_second;
    using il2cpp::runtime::icall_datetime_compare_to;

    const Il2CppDateTime dt2020(kTicks2020);
    const Il2CppDateTime midnight(kTicks2020Midnight);
    const Il2CppDateTime year1(0);
    const Il2CppDateTime maxDt(kMaxTicks);

    // 1. ParseExact round trips through ToString(format).
    const Il2CppDateTime parsed = Parse("2020-06-15 13:45:30", "yyyy-MM-dd HH:mm:ss");
    Expect(parsed.ticks == kTicks2020, "parse 'yyyy-MM-dd HH:mm:ss' == fixed ticks");
    Expect(StrEquals(Fmt(parsed, "yyyy-MM-dd HH:mm:ss"), "2020-06-15 13:45:30"),
           "round trip 'yyyy-MM-dd HH:mm:ss'");
    const Il2CppDateTime parsedDate = Parse("2020/06/15", "yyyy/MM/dd");
    Expect(parsedDate.ticks == kTicks2020Midnight, "parse 'yyyy/MM/dd' == midnight ticks");
    Expect(StrEquals(Fmt(parsedDate, "yyyy/MM/dd"), "2020/06/15"),
           "round trip 'yyyy/MM/dd'");
    Expect(icall_datetime_get_hour(parsedDate) == 0
               && icall_datetime_get_second(parsedDate) == 0,
           "date-only parse defaults time to 00:00:00");

    // 2. Cross-domain pin: parse == construction icall; year-1 / MaxValue /
    // leap-day edges.
    const Il2CppDateTime ctor = icall_datetime_from_ymdhms(2020, 6, 15, 13, 45, 30);
    Expect(parsed.ticks == ctor.ticks, "parse == construction icall");
    Expect(Parse("0001-01-01 00:00:00", "yyyy-MM-dd HH:mm:ss").ticks == 0,
           "year 1 parses to tick 0");
    Expect(Parse("9999-12-31 23:59:59", "yyyy-MM-dd HH:mm:ss").ticks
               == icall_datetime_from_ymdhms(9999, 12, 31, 23, 59, 59).ticks,
           "MaxValue whole-second parse == construction icall");
    Expect(Parse("2020-02-29 00:00:00", "yyyy-MM-dd HH:mm:ss").ticks
               == icall_datetime_from_ymdhms(2020, 2, 29, 0, 0, 0).ticks,
           "leap day 2020-02-29 parses");
    Expect(Parse("2000/02/29", "yyyy/MM/dd").ticks
               == icall_datetime_from_ymdhms(2000, 2, 29, 0, 0, 0).ticks,
           "century leap day 2000-02-29 parses");

    // 3. ParseExact error matrix (FormatException unless noted).
    Expect(ThrowsWithType([&] { (void)Parse("2020-6-15 13:45:30", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "single-digit month rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15 13:45:3", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "single-digit second rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "truncated input rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15 13:45:30x", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "trailing input rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15T13:45:30", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "wrong literal 'T' rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020/06/15 00:00:00", "yyyy/MM/dd"); },
                          "System.FormatException"),
           "trailing time rejects date-only format");
    Expect(ThrowsWithType([&] { (void)Parse("2020-13-15 00:00:00", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "month 13 rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-02-30 00:00:00", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "Feb 30 rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2021-02-29 00:00:00", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "non-leap Feb 29 rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15 24:00:00", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "hour 24 rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15 13:60:00", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "minute 60 rejects");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15 13:45:60", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "second 60 rejects");
    Expect(ThrowsWithType([&] { (void)Parse("0000-06-15 00:00:00", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "year 0 rejects");
    Expect(ThrowsWithType([&] { (void)Parse("", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "empty input rejects");
    // .NET token widths (DateTimeParse.cs ParseByFormat/ParseDigits + net48
    // oracle): single-char tokens are 1..2 digits, doubled tokens are
    // EXACTLY 2, "yy" is exactly 2 + TwoDigitYearMax expansion.
    Expect(Parse("20-06-15 13:45:30", "yy-MM-dd HH:mm:ss").ticks
               == icall_datetime_from_ymdhms(2020, 6, 15, 13, 45, 30).ticks,
           "'yy' parses exactly 2 digits with TwoDigitYearMax expansion");
    Expect(Parse("2020-6-15 13:45:30", "yyyy-M-dd HH:mm:ss").ticks
               == icall_datetime_from_ymdhms(2020, 6, 15, 13, 45, 30).ticks,
           "single 'M' accepts 2 digits");
    Expect(Parse("2020-6-15 13:45:30", "yyyy-M-dd HH:mm:ss").ticks
               == icall_datetime_from_ymdhms(2020, 6, 15, 13, 45, 30).ticks,
           "single 'M' accepts 1 digit");
    Expect(ThrowsWithType([&] { (void)Parse("2020-6-15 13:45:30", "yyyy-MM-dd HH:mm:ss"); },
                          "System.FormatException"),
           "'MM' requires exactly 2 digits");
    Expect(Parse("2020-06-15 13:45:3", "yyyy-MM-dd H:m:s").ticks
               == icall_datetime_from_ymdhms(2020, 6, 15, 13, 45, 3).ticks,
           "single H/m/s accept 1-2 digits");
    Expect(Parse("2020-06-15T13:45:30", "yyyy-MM-dd'T'HH:mm:ss").ticks
               == icall_datetime_from_ymdhms(2020, 6, 15, 13, 45, 30).ticks,
           "quoted literal 'T' matches verbatim");
    Expect(Parse("2020-06-15T13:45:30", "yyyy-MM-ddT13:45:30").ticks
               == icall_datetime_from_ymdhms(2020, 6, 15, 0, 0, 0).ticks,
           "unquoted unknown letters match literal chars (digits after 'T' "
           "are literals too -- time stays 00:00:00)");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15 13:45:30", "yyyy-MM-dd hh:mm:ss"); },
                          "System.FormatException"),
           "unsupported .NET token 'h' rejects (not literal)");
    // Two-digit year expansion (net48 oracle, TwoDigitYearMax 2049).
    Expect(Parse("29-06-15", "yy-MM-dd").ticks
               == icall_datetime_from_ymdhms(2029, 6, 15, 0, 0, 0).ticks,
           "'yy' 29 expands to 2029");
    Expect(Parse("50-06-15", "yy-MM-dd").ticks
               == icall_datetime_from_ymdhms(1950, 6, 15, 0, 0, 0).ticks,
           "'yy' 50 expands to 1950");
    Expect(ThrowsWithType([&] { (void)Parse("6-06-15", "yy-MM-dd"); },
                          "System.FormatException"),
           "'yy' requires exactly 2 digits");
    Expect(Parse("024-06-15", "yyy-MM-dd").ticks
               == icall_datetime_from_ymdhms(24, 6, 15, 0, 0, 0).ticks,
           "'yyy' is exactly 3 digits, no expansion");
    Expect(Parse("2024-2-29", "yyyy-M-d").ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 0, 0, 0).ticks,
           "leap day with single-char tokens parses");
    Expect(ThrowsWithType([&] { (void)Parse("2023-2-29", "yyyy-M-d"); },
                          "System.FormatException"),
           "non-leap Feb 29 rejects with single-char tokens");
    Expect(Parse("2024-02-29 13:4:5", "yyyy-MM-dd HH:m:s").ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 13, 4, 5).ticks,
           "net48 oracle width case (HH:m:s) parses");
    Expect(ThrowsWithType([&] { (void)Parse("2020-06-15 13:45:30", ""); },
                          "System.FormatException"),
           "empty format rejects");
    Expect(ThrowsWithType(
               [&] { (void)il2cpp_datetime_parse_exact(
                         nullptr,
                         reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("yyyy"))); },
               "System.ArgumentNullException"),
           "null input throws ArgumentNullException");
    Expect(ThrowsWithType(
               [&] { (void)il2cpp_datetime_parse_exact(
                         reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("2020")),
                         nullptr); },
               "System.ArgumentNullException"),
           "null format throws ArgumentNullException");

    // 3aa. Missing-field defaults (net48 / netcore
    // DateTimeParse.CheckDefaultDateTime): a partial format leaves the
    // unparsed date fields to default -- month/day -> 1 when the year is
    // present, year -> the CURRENT year when month/day are present.
    // The datetime2 e2e "yy" probes ("24"/"49"/"50"/"29"/"00") all exercise
    // the "year present, month/day missing" path.
    Expect(Parse("24", "yy").ticks == icall_datetime_from_ymdhms(2024, 1, 1, 0, 0, 0).ticks,
           "'yy' 24 -> 2024-01-01 (month/day default 1)");
    Expect(Parse("49", "yy").ticks == icall_datetime_from_ymdhms(2049, 1, 1, 0, 0, 0).ticks,
           "'yy' 49 -> 2049-01-01 (TwoDigitYearMax boundary)");
    Expect(Parse("50", "yy").ticks == icall_datetime_from_ymdhms(1950, 1, 1, 0, 0, 0).ticks,
           "'yy' 50 -> 1950-01-01 (expands below the century)");
    Expect(Parse("29", "yy").ticks == icall_datetime_from_ymdhms(2029, 1, 1, 0, 0, 0).ticks,
           "'yy' 29 -> 2029-01-01");
    Expect(Parse("00", "yy").ticks == icall_datetime_from_ymdhms(2000, 1, 1, 0, 0, 0).ticks,
           "'yy' 00 -> 2000-01-01");
    Expect(Parse("2024", "yyyy").ticks == icall_datetime_from_ymdhms(2024, 1, 1, 0, 0, 0).ticks,
           "'yyyy' alone -> 2024-01-01");
    Expect(Parse("2024-02", "yyyy-MM").ticks == icall_datetime_from_ymdhms(2024, 2, 1, 0, 0, 0).ticks,
           "'yyyy-MM' fills missing day with 1");
    {
        // "MM-dd" has no year token -> the CURRENT year (net48 oracle:
        // ParseExact("02-28", "MM-dd") -> Year == DateTime.Now.Year).
        const int32_t nowYear = icall_datetime_get_year(il2cpp::runtime::icall_datetime_now());
        Expect(Parse("02-28", "MM-dd").ticks == icall_datetime_from_ymdhms(nowYear, 2, 28, 0, 0, 0).ticks,
               "'MM-dd' fills missing year with the current year");
    }
    // net48 CheckDefaultDateTime table (source lines 3949-3956) -- the
    // single-field formats exercise the remaining branches:
    //   --  X  --  -> current year + parsed month + first day
    //   --  --  X  -> current year + first month + parsed day
    //   --  --  -- -> current date (time-only input)
    {
        const int32_t nowYear = icall_datetime_get_year(il2cpp::runtime::icall_datetime_now());
        const int32_t nowMonth = icall_datetime_get_month(il2cpp::runtime::icall_datetime_now());
        const int32_t nowDay = icall_datetime_get_day(il2cpp::runtime::icall_datetime_now());
        Expect(Parse("06", "MM").ticks == icall_datetime_from_ymdhms(nowYear, 6, 1, 0, 0, 0).ticks,
               "'MM' alone fills year = current, day = 1 (net48)");
        Expect(Parse("15", "dd").ticks == icall_datetime_from_ymdhms(nowYear, 1, 15, 0, 0, 0).ticks,
               "'dd' alone fills year = current, month = 1 (net48)");
        Expect(Parse("13:45:30", "HH:mm:ss").ticks
                   == icall_datetime_from_ymdhms(nowYear, nowMonth, nowDay, 13, 45, 30).ticks,
               "time-only 'HH:mm:ss' fills the date from today (net48)");
        Expect(Parse("2024", "yyyy").ticks == icall_datetime_from_ymdhms(2024, 1, 1, 0, 0, 0).ticks,
               "'yyyy' alone keeps the parsed year (X -- -- row)");
    }

    // 3b. Parse (invariant-culture subset, net48 oracle: shapes
    // "YYYY-MM-DD[ T]HH:MM[:SS]" and "M/D/YY[YY][ T]HH:MM[:SS]", 1-2 digit
    // month/day/time fields, TwoDigitYearMax 2049 expansion, whitespace
    // trimmed, leap-day validated).
    using il2cpp::runtime::il2cpp_datetime_parse;
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-02-29"))).ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 0, 0, 0).ticks,
           "Parse leap day 2024-02-29");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-02-29"))).ticks
               == 638447616000000000LL,
           "Parse 2024-02-29 == net48 oracle ticks 638447616000000000");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("2023-02-29")));
           }, "System.FormatException"),
           "Parse 2023-02-29 throws (non-leap)");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("1900-02-29")));
           }, "System.FormatException"),
           "Parse 1900-02-29 throws (century non-leap)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2000-02-29"))).ticks
               == icall_datetime_from_ymdhms(2000, 2, 29, 0, 0, 0).ticks,
           "Parse 2000-02-29 ok (400-year leap)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-2-29"))).ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 0, 0, 0).ticks,
           "Parse 2024-2-29 (single-digit month)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-6-1"))).ticks
               == icall_datetime_from_ymdhms(2024, 6, 1, 0, 0, 0).ticks,
           "Parse 2024-6-1 (single-digit month and day)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("6/15/24"))).ticks
               == icall_datetime_from_ymdhms(2024, 6, 15, 0, 0, 0).ticks,
           "Parse 6/15/24 (US order, 2-digit year -> 2024)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("6/15/49"))).ticks
               == icall_datetime_from_ymdhms(2049, 6, 15, 0, 0, 0).ticks,
           "Parse 6/15/49 -> 2049 (TwoDigitYearMax boundary)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("6/15/50"))).ticks
               == icall_datetime_from_ymdhms(1950, 6, 15, 0, 0, 0).ticks,
           "Parse 6/15/50 -> 1950");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("6/15/2024"))).ticks
               == icall_datetime_from_ymdhms(2024, 6, 15, 0, 0, 0).ticks,
           "Parse 6/15/2024 (4-digit year)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024/02/29"))).ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 0, 0, 0).ticks,
           "Parse 2024/02/29 (slash separators)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-02-29T13:45:30"))).ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 13, 45, 30).ticks,
           "Parse 2024-02-29T13:45:30 ('T' separator)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-02-29 13:45:30"))).ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 13, 45, 30).ticks,
           "Parse 2024-02-29 13:45:30 (space separator)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-2-29 3:4:5"))).ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 3, 4, 5).ticks,
           "Parse 2024-2-29 3:4:5 (single-digit time)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("2024-06-15 13:45"))).ticks
               == icall_datetime_from_ymdhms(2024, 6, 15, 13, 45, 0).ticks,
           "Parse 2024-06-15 13:45 (seconds optional)");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs(" 2024-02-29 "))).ticks
               == icall_datetime_from_ymdhms(2024, 2, 29, 0, 0, 0).ticks,
           "Parse trims leading/trailing whitespace");
    Expect(il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
               il2cpp::il2cpp_string_new_from_cs("0001-01-01"))).ticks == 0,
           "Parse year 1 == tick 0");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("24-06-15")));
           }, "System.FormatException"),
           "Parse 24-06-15 throws (US order: month 24)");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("29/02/2024")));
           }, "System.FormatException"),
           "Parse 29/02/2024 throws (US order: month 29)");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("2024-02-29 junk")));
           }, "System.FormatException"),
           "Parse trailing junk throws");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("0000-01-01")));
           }, "System.FormatException"),
           "Parse year 0 throws");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("2024-02-29T13:45:30+08:00")));
           }, "System.FormatException"),
           "Parse timezone suffix throws (documented divergence)");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("2024-02-29T13:45:30.123")));
           }, "System.FormatException"),
           "Parse fractional seconds throw (documented divergence)");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("13:45")));
           }, "System.FormatException"),
           "Parse time-only throws (documented divergence)");
    Expect(ThrowsWithType([&] {
               (void)il2cpp_datetime_parse(reinterpret_cast<il2cpp::Il2CppObject*>(
                   il2cpp::il2cpp_string_new_from_cs("")));
           }, "System.FormatException"),
           "Parse empty input throws");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_parse(nullptr); },
                          "System.ArgumentNullException"),
           "Parse null input throws ArgumentNullException");

    // 4. AddTicks / SubtractTicks.
    const Il2CppDateTime plus50s = il2cpp_datetime_add_ticks(dt2020, 500000000);
    Expect(icall_datetime_get_hour(plus50s) == 13 && icall_datetime_get_minute(plus50s) == 46
               && icall_datetime_get_second(plus50s) == 20,
           "add 50 s -> 13:46:20");
    const Il2CppDateTime minus30s = il2cpp_datetime_add_ticks(dt2020, -300000000);
    Expect(icall_datetime_get_second(minus30s) == 0 && icall_datetime_get_minute(minus30s) == 45,
           "add -30 s -> 13:45:00");
    const Il2CppDateTime beforeMidnight = il2cpp_datetime_add_ticks(midnight, -1);
    Expect(icall_datetime_get_year(beforeMidnight) == 2020
               && icall_datetime_get_month(beforeMidnight) == 6
               && icall_datetime_get_day(beforeMidnight) == 14
               && icall_datetime_get_hour(beforeMidnight) == 23
               && icall_datetime_get_minute(beforeMidnight) == 59
               && icall_datetime_get_second(beforeMidnight) == 59,
           "midnight - 1 tick -> previous day 23:59:59");
    Expect(il2cpp_datetime_add_ticks(year1, 0).ticks == 0, "add 0 keeps tick 0");
    Expect(il2cpp_datetime_add_ticks(maxDt, 0).ticks == kMaxTicks, "add 0 keeps MaxValue");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_add_ticks(maxDt, 1); },
                          "System.ArgumentOutOfRangeException"),
           "MaxValue + 1 throws");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_add_ticks(year1, -1); },
                          "System.ArgumentOutOfRangeException"),
           "MinValue - 1 throws");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_add_ticks(dt2020, INT64_MAX); },
                          "System.ArgumentOutOfRangeException"),
           "add INT64_MAX throws");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_add_ticks(dt2020, INT64_MIN); },
                          "System.ArgumentOutOfRangeException"),
           "add INT64_MIN throws");
    const Il2CppDateTime sub50s = il2cpp_datetime_subtract_ticks(dt2020, 500000000);
    Expect(icall_datetime_get_minute(sub50s) == 44 && icall_datetime_get_second(sub50s) == 40,
           "subtract 50 s -> 13:44:40");
    const Il2CppDateTime subNegative = il2cpp_datetime_subtract_ticks(dt2020, -500000000);
    Expect(subNegative.ticks == plus50s.ticks,
           "subtract -50 s == add +50 s");
    Expect(il2cpp_datetime_subtract_ticks(maxDt, 1).ticks == kMaxTicks - 1,
           "MaxValue - 1 ok");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_subtract_ticks(year1, 1); },
                          "System.ArgumentOutOfRangeException"),
           "MinValue - 1 throws (subtract)");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_subtract_ticks(dt2020, INT64_MIN); },
                          "System.ArgumentOutOfRangeException"),
           "subtract INT64_MIN throws");
    Expect(ThrowsWithType([&] { (void)il2cpp_datetime_subtract_ticks(maxDt, INT64_MAX); },
                          "System.ArgumentOutOfRangeException"),
           "subtract INT64_MAX throws");

    // 5. Compare.
    Expect(il2cpp_datetime_compare(dt2020, dt2020) == 0, "compare equal == 0");
    Expect(il2cpp_datetime_compare(year1, dt2020) == -1, "compare less == -1");
    Expect(il2cpp_datetime_compare(dt2020, year1) == 1, "compare greater == 1");
    Expect(il2cpp_datetime_compare(maxDt, midnight) == 1, "compare MaxValue > midnight");
    Expect(il2cpp_datetime_compare(dt2020, dt2020)
               == icall_datetime_compare_to(dt2020, dt2020)
           && il2cpp_datetime_compare(year1, dt2020)
               == icall_datetime_compare_to(year1, dt2020)
           && il2cpp_datetime_compare(dt2020, year1)
               == icall_datetime_compare_to(dt2020, year1),
           "compare consistent with icall_datetime_compare_to");

    // 6. ToString(format) delegation: round trip + null-format "G" default.
    Expect(StrEquals(Fmt(dt2020, "yyyy"), "2020"), "to_string_format 'yyyy' == 2020");
    Expect(StrEquals(reinterpret_cast<il2cpp::Il2CppString*>(
                         il2cpp::runtime::il2cpp_datetime_to_string_format(dt2020, nullptr)),
                     "06/15/2020 13:45:30"),
           "null format -> General 'G' default");

    if (g_failures == 0)
    {
        std::printf("all runtime timeext2 tests passed\n");
    }
    return g_failures == 0 ? 0 : 1;
}
