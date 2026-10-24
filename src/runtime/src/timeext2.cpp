//------------------------------------------------------------------------------
// timeext2.cpp -- System.DateTime extension module 2 implementation
// (see timeext2.h for the exact ParseExact subset and error contract).
//
// Design:
//   * Representation: raw int64 tick counts -- identical to time.cpp, so
//     values produced here interoperate with every icall_datetime_* icall.
//   * ParseExact walks format and input LEFT TO RIGHT in one pass. Token
//     width semantics match .NET Core DateTimeParse.ParseByFormat /
//     ParseDigits exactly (System/Globalization/DateTimeParse.cs): a single
//     token letter (M/d/H/m/s) consumes 1..2 digits, a doubled token (MM,
//     dd, HH, mm, ss) consumes EXACTLY 2, 'y' consumes 1..2 digits (or
//     exactly 2 for "yy") with the TwoDigitYearMax (2049) expansion for
//     values < 100, and 3+ 'y' tokens consume exactly that many digits.
//     Quoted literals ('...' / "..."), backslash escapes, and unknown
//     single letters (matched verbatim one char at a time) follow the
//     .NET default case. Every mismatch -- wrong width, non-digit, wrong
//     literal, trailing input, out-of-range component -- is a
//     FormatException, exactly like .NET Framework.
//   * Parse (il2cpp_datetime_parse) is a documented invariant-culture
//     subset pinned by the net48 oracle: "YYYY-MM-DD[ T]HH:MM[:SS]" and
//     "M/D/YY[YY][ T]HH:MM[:SS]" shapes with 1-2 digit month/day/time
//     fields, 1-2 digit US years expanded via TwoDigitYearMax, leading/
//     trailing whitespace trimmed, leap-day validation. Timezone suffixes,
//     fractional seconds, compact forms and time-only input are REJECTED
//     (documented divergences -- the net48 oracle converts timezone
//     offsets to local time and defaults time-only input to today).
//   * The calendar math (DaysFromNetEpoch / DaysInMonth) is the Hinnant
//     algorithm duplicated from time.cpp -- the established pattern
//     (timeext.cpp keeps its own pinned copies; see its header comment).
//   * Tick arithmetic never negates the delta, so INT64_MIN is safe, and
//     every addition/subtraction is provably int64-overflow-free (see the
//     per-branch comments in AddTicks/SubtractTicks).
//   * il2cpp_datetime_to_string_format and il2cpp_datetime_compare DELEGATE
//     to the canonical implementations (time_ext.cpp / time.cpp) -- no
//     duplicate formatter, no duplicate comparison logic.
//------------------------------------------------------------------------------

#include "timeext2.h"

#include "cultureinfo.h"
#include "il2cpp_array.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_string.h"
#include "il2cpp_time_ext.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

namespace {

// Tick constants (mirror time.cpp; anonymous-namespace locals there).
constexpr int64_t kTicksPerSecond = 10000000;
constexpr int64_t kTicksPerMinute = 600000000;
constexpr int64_t kTicksPerHour = 36000000000;
constexpr int64_t kTicksPerDay = 864000000000;
// DateTime.MaxValue ticks: 9999-12-31 23:59:59.9999999.
constexpr int64_t kMaxDateTimeTicks = 3155378975999999999;
// Gregorian Calendar.TwoDigitYearMax (invariant culture, net48 oracle).
constexpr int32_t kTwoDigitYearMax = 2049;

// Never returns (see il2cpp_exception_ext.h).
void ThrowArgOutOfRange()
{
    il2cpp_throw_argument_out_of_range_exception(
        nullptr, "Specified argument was out of the range of valid values.");
}

// Never returns -- .NET Framework ParseExact error texts.
void ThrowFormatInvalidSpecifier()
{
    il2cpp_throw_format_exception("Format specifier was invalid.");
}

void ThrowFormatUnrecognized()
{
    il2cpp_throw_format_exception("String was not recognized as a valid DateTime.");
}

// .NET DateTimeFormatInfo.ExpandTwoDigitYear against kTwoDigitYearMax
// (2049): 49 -> 2049, 50 -> 1950, 00 -> 2000 (net48 oracle).
int32_t ExpandTwoDigitYear(int32_t year)
{
    if (year >= 0 && year < 100)
    {
        const int32_t century = kTwoDigitYearMax - kTwoDigitYearMax % 100;
        return year <= kTwoDigitYearMax % 100 ? century + year : century - 100 + year;
    }
    return year;
}

// Days from the .NET epoch (0001-01-01) to the civil date (year, month,
// day). Hinnant's days_from_civil (1970 epoch) shifted by the 719162-day
// offset between the Unix and .NET epochs. Inputs are pre-validated.
int64_t DaysFromNetEpoch(int32_t year, int32_t month, int32_t day)
{
    int64_t y = static_cast<int64_t>(year) - (month <= 2 ? 1 : 0);
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const int64_t yoe = y - era * 400;                                    // [0, 399]
    const int64_t doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + (day - 1); // [0, 365]
    const int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;            // [0, 146096]
    return era * 146097 + doe - 719468 + 719162;                          // days since 0001-01-01
}

// Days in month for the proleptic Gregorian calendar (.NET leap rule).
int32_t DaysInMonth(int32_t year, int32_t month)
{
    static const int32_t kDaysInMonth[12] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
    if (month == 2 && year % 4 == 0 && (year % 100 != 0 || year % 400 == 0))
    {
        return 29;
    }
    return kDaysInMonth[month - 1];
}

bool IsAsciiDigit(char16_t c)
{
    return c >= u'0' && c <= u'9';
}

// The fields parsed by the supported format subset.
struct ParsedFields
{
    int32_t year;
    int32_t month;
    int32_t day;
    int32_t hour;
    int32_t minute;
    int32_t second;
};

// Consumes exactly 'width' ASCII digits at input[pos]. Returns false on any
// mismatch (too few units left, non-digit, ...); on success 'pos' advances
// and 'value' receives the number.
bool ParseFixedDigits(const char16_t* input, int32_t inputLen,
                      int32_t& pos, int32_t width, int32_t& value)
{
    if (pos + width > inputLen)
    {
        return false;
    }
    int32_t v = 0;
    for (int32_t i = 0; i < width; ++i)
    {
        const char16_t c = input[pos + i];
        if (!IsAsciiDigit(c))
        {
            return false;
        }
        v = v * 10 + (c - u'0');
    }
    pos += width;
    value = v;
    return true;
}

// .NET DateTimeParse.ParseDigits (DateTimeParse.cs:3170): digitLen == 1
// really means 1..2 digits, digitLen >= 2 means EXACTLY digitLen digits.
// Used for the token letters below (M/d/H/m/s).
bool ParseTokenDigits(const char16_t* input, int32_t inputLen,
                      int32_t& pos, int32_t run, int32_t& value)
{
    if (run == 1)
    {
        // Greedy 1..2 digits (stops at the first non-digit).
        if (pos >= inputLen || !IsAsciiDigit(input[pos]))
        {
            return false;
        }
        int32_t v = input[pos] - u'0';
        ++pos;
        if (pos < inputLen && IsAsciiDigit(input[pos]))
        {
            v = v * 10 + (input[pos] - u'0');
            ++pos;
        }
        value = v;
        return true;
    }
    return ParseFixedDigits(input, inputLen, pos, run, value);
}

// Scans a run of ASCII digits at input[pos]; 'digits' receives the run
// length (callers enforce the per-field width limits), 'value' the number.
// The run is capped at 10 digits so 'value' cannot overflow int32.
bool ScanDigits(const char16_t* input, int32_t end, int32_t& pos,
                int32_t& value, int32_t& digits)
{
    int32_t v = 0;
    int32_t n = 0;
    while (pos < end && IsAsciiDigit(input[pos]) && n < 10)
    {
        v = v * 10 + (input[pos] - u'0');
        ++pos;
        ++n;
    }
    if (n == 0)
    {
        return false;
    }
    value = v;
    digits = n;
    return true;
}

bool IsAsciiSpace(char16_t c)
{
    return c == u' ' || c == u'\t' || c == u'\r' || c == u'\n';
}

// Validated fields -> DateTime ticks (both ParseExact and Parse; validated
// components never overflow int64 -- see the ParseExact tail).
Il2CppDateTime ComposeDateTime(const ParsedFields& fields)
{
    const int64_t days = DaysFromNetEpoch(fields.year, fields.month, fields.day);
    const int64_t timeTicks = static_cast<int64_t>(fields.hour) * kTicksPerHour
                            + static_cast<int64_t>(fields.minute) * kTicksPerMinute
                            + static_cast<int64_t>(fields.second) * kTicksPerSecond;
    return Il2CppDateTime(days * kTicksPerDay + timeTicks);
}

// One-pass exact matcher: token letters parse digits with the .NET width
// rules above; quoted ('...' / "...") and backslash-escaped literals match
// verbatim; unknown single letters are matched one char at a time (the
// .NET ParseByFormat default case); every other format character is a
// literal matched exactly; trailing input is rejected. Returns false on ANY
// mismatch (the caller throws FormatException -- never partially applies).
bool TryParseExact(const char16_t* input, int32_t inputLen,
                   const char16_t* fmt, int32_t fmtLen, int32_t styles, ParsedFields& out)
{
    // DateTimeStyles.AllowWhiteSpaces (0x7: leading/trailing/inner) trims the
    // leading and trailing whitespace before matching (net48 ParseExact).
    int32_t begin = 0;
    int32_t end = inputLen;
    if ((styles & 0x7) != 0)
    {
        while (begin < end && IsAsciiSpace(input[begin]))
        {
            ++begin;
        }
        while (end > begin && IsAsciiSpace(input[end - 1]))
        {
            --end;
        }
    }
    out.year = out.month = out.day = out.hour = out.minute = out.second = 0;
    int32_t pos = 0;
    const char16_t* in = input + begin;
    const int32_t inLen = end - begin;
    for (int32_t f = 0; f < fmtLen;)
    {
        const char16_t c = fmt[f];
        if (c == u'\'' || c == u'"')
        {
            // Quoted literal group: the enclosed characters must appear
            // verbatim (DateTimeParse.cs TryParseQuoteString). Unterminated
            // quotes are invalid formats.
            int32_t q = f + 1;
            while (q < fmtLen && fmt[q] != c)
            {
                ++q;
            }
            if (q >= fmtLen)
            {
                return false;
            }
            for (int32_t i = f + 1; i < q; ++i)
            {
                if (pos >= inLen || in[pos] != fmt[i])
                {
                    return false;
                }
                ++pos;
            }
            f = q + 1;
            continue;
        }
        if (c == u'\\')
        {
            // Escape: the next format character is a literal (the
            // DateTimeParse.cs '\\' case).
            if (f + 1 >= fmtLen)
            {
                return false;
            }
            if (pos >= inLen || in[pos] != fmt[f + 1])
            {
                return false;
            }
            ++pos;
            f += 2;
            continue;
        }
        const bool isLetter = (c >= u'a' && c <= u'z') || (c >= u'A' && c <= u'Z');
        if (!isLetter)
        {
            // Literal: must appear in the input at the same position.
            if (pos >= inLen || in[pos] != c)
            {
                return false;
            }
            ++pos;
            ++f;
            continue;
        }
        int32_t run = 1;
        while (f + run < fmtLen && fmt[f + run] == c)
        {
            ++run;
        }
        int32_t value = 0;
        switch (c)
        {
        case u'y':
            // tokenLen <= 2: 1..2 digits (single 'y') or exactly 2 ("yy"),
            // then the TwoDigitYearMax expansion for years < 100
            // (DateTimeParse.cs:4122-4126). tokenLen >= 3: exactly that
            // many digits, NO expansion (fUseTwoDigitYear is only set for
            // tokenLen <= 2 -- "yyyy" with "0001" must stay year 1).
            if (run == 1)
            {
                if (!ParseTokenDigits(in, inLen, pos, 1, value))
                {
                    return false;
                }
            }
            else
            {
                if (!ParseFixedDigits(in, inLen, pos, run, value))
                {
                    return false;
                }
            }
            if (run <= 2 && value < 100)
            {
                value = ExpandTwoDigitYear(value);
            }
            if (value < 1 || value > 9999)
            {
                return false;
            }
            out.year = value;
            break;
        case u'M':
            // tokenLen <= 2: 1..2 digits ("M") or exactly 2 ("MM"); 3+
            // would be month names (unsupported here).
            if (run >= 3 || !ParseTokenDigits(in, inLen, pos, run, value)
                || value < 1 || value > 12)
            {
                return false;
            }
            out.month = value;
            break;
        case u'd':
            // Same width rule as 'M'; 3+ would be day names (unsupported).
            if (run >= 3 || !ParseTokenDigits(in, inLen, pos, run, value)
                || value < 1 || value > 31)
            {
                return false;
            }
            out.day = value;
            break;
        case u'H':
            // 1..2 digits ("H") or exactly 2 ("HH", "HHH"... --
            // DateTimeParse.cs:4250: tokenLen < 2 ? 1 : 2).
            if (!ParseTokenDigits(in, inLen, pos, run < 2 ? 1 : 2, value) || value > 23)
            {
                return false;
            }
            out.hour = value;
            break;
        case u'm':
            if (!ParseTokenDigits(in, inLen, pos, run < 2 ? 1 : 2, value) || value > 59)
            {
                return false;
            }
            out.minute = value;
            break;
        case u's':
            if (!ParseTokenDigits(in, inLen, pos, run < 2 ? 1 : 2, value) || value > 59)
            {
                return false;
            }
            out.second = value;
            break;
        // .NET token letters this subset does NOT implement (12-hour clock,
        // AM/PM, fraction, era, timezone): reject the format explicitly
        // rather than silently treating the letters as literals.
        case u'h': case u't': case u'f': case u'F': case u'g':
        case u'z': case u'Z': case u'K':
            return false;
        default:
            // Unknown single letter: matched verbatim, one character at a
            // time (the DateTimeParse.cs default case) -- so "yyyy-MM-ddTHH"
            // matches a literal 'T' in the input.
            if (pos >= inLen || in[pos] != c)
            {
                return false;
            }
            ++pos;
            ++f;
            continue;
        }
        f += run;
    }
    // Missing-field defaults (net48 / netcore DateTimeParse.CheckDefaultDateTime):
    //   year present, month/day missing  -> first month/day of that year (1/1)
    //   year missing, month/day present  -> current year (from DateTime.Now)
    //   all date fields missing (time-only) -> current date
    //   hour/minute/second missing -> 0 (already the initial value)
    // Without this fill, a partial format like "yy" leaves month/day at 0,
    // producing a bogus tick value (and DaysInMonth(year, 0) reads OOB).
    if (out.month == 0 && out.day == 0)
    {
        if (out.year == 0)
        {
            // DateTimeStyles.NoCurrentDateDefault (0x8): a time-only input
            // defaults to DateTime.MinValue (0001-01-01) instead of today.
            if ((styles & 0x8) != 0)
            {
                out.year = 1;
                out.month = 1;
                out.day = 1;
            }
            else
            {
                const Il2CppDateTime now = icall_datetime_now();
                out.year = icall_datetime_get_year(now);
                out.month = icall_datetime_get_month(now);
                out.day = icall_datetime_get_day(now);
            }
        }
        else
        {
            out.month = 1;
            out.day = 1;
        }
    }
    else
    {
        if (out.year == 0)
        {
            out.year = icall_datetime_get_year(icall_datetime_now());
        }
        if (out.month == 0)
        {
            out.month = 1;
        }
        if (out.day == 0)
        {
            out.day = 1;
        }
    }
    // Everything consumed AND the day exists in the month (calendar check;
    // month itself was validated 1..12 above).
    return pos == inLen && out.day <= DaysInMonth(out.year, out.month);
}

// DateTime.Parse subset (invariant culture), pinned by the net48 oracle:
//   ISO shape "YYYY-MM-DD[ T]HH:MM[:SS]"  -- 4-digit year, '-' or '/'
//   US shape  "M/D/YY[YY][ T]HH:MM[:SS]"  -- month-first, '-' or '/'
//   Year 1-2 digits (US shape) -> ExpandTwoDigitYear (TwoDigitYearMax 2049);
//   3-4 digits are taken as-is. Month/day/time fields are 1-2 digits.
//   Leading/trailing ASCII whitespace is trimmed (.NET Parse allows it; the
//   ParseExact path does NOT -- oracle: both probe families).
//   Rejected (documented divergences): timezone suffixes ("Z", "+HH:MM" --
//   net48 converts to local time), fractional seconds, compact forms
//   ("20240615"), time-only input (net48 defaults the date to today),
//   inner whitespace, 5+ digit years.
bool TryParseDefault(const char16_t* input, int32_t inputLen, ParsedFields& out)
{
    out.year = out.month = out.day = out.hour = out.minute = out.second = 0;
    int32_t begin = 0;
    int32_t end = inputLen;
    while (begin < end && IsAsciiSpace(input[begin]))
    {
        ++begin;
    }
    while (end > begin && IsAsciiSpace(input[end - 1]))
    {
        --end;
    }
    if (begin >= end)
    {
        return false;
    }
    int32_t pos = begin;

    // First field: 4 digits -> ISO (year); 1-2 digits -> US (month).
    int32_t first = 0;
    int32_t firstDigits = 0;
    if (!ScanDigits(input, end, pos, first, firstDigits))
    {
        return false;
    }
    bool iso;
    if (firstDigits == 4)
    {
        iso = true;
        out.year = first;
    }
    else if (firstDigits <= 2)
    {
        iso = false;
        out.month = first;
    }
    else
    {
        return false;
    }

    // Two identical separators ('-' or '/'), then the second and third
    // fields.
    if (pos >= end || (input[pos] != u'-' && input[pos] != u'/'))
    {
        return false;
    }
    const char16_t sep = input[pos];
    ++pos;
    int32_t second = 0;
    int32_t secondDigits = 0;
    if (!ScanDigits(input, end, pos, second, secondDigits))
    {
        return false;
    }
    if (pos >= end || input[pos] != sep)
    {
        return false;
    }
    ++pos;
    int32_t third = 0;
    int32_t thirdDigits = 0;
    if (!ScanDigits(input, end, pos, third, thirdDigits))
    {
        return false;
    }

    if (iso)
    {
        if (secondDigits > 2 || thirdDigits > 2)
        {
            return false;
        }
        out.month = second;
        out.day = third;
    }
    else
    {
        if (secondDigits > 2 || thirdDigits > 4)
        {
            return false;
        }
        out.day = second;
        out.year = thirdDigits <= 2 ? ExpandTwoDigitYear(third) : third;
    }

    // Optional time: 'T' or ' ' separator, then HH:MM[:SS] (1-2 digits
    // each; seconds optional -- oracle "2024-06-15 13:45" parses).
    if (pos < end)
    {
        if (input[pos] != u'T' && input[pos] != u' ')
        {
            return false;
        }
        ++pos;
        int32_t hourDigits = 0;
        if (!ScanDigits(input, end, pos, out.hour, hourDigits) || hourDigits > 2
            || pos >= end || input[pos] != u':')
        {
            return false;
        }
        ++pos;
        int32_t minuteDigits = 0;
        if (!ScanDigits(input, end, pos, out.minute, minuteDigits) || minuteDigits > 2)
        {
            return false;
        }
        if (pos < end && input[pos] == u':')
        {
            ++pos;
            int32_t secondDigits2 = 0;
            if (!ScanDigits(input, end, pos, out.second, secondDigits2) || secondDigits2 > 2)
            {
                return false;
            }
        }
    }

    // Everything consumed; component ranges (incl. the leap-day rule).
    if (pos != end)
    {
        return false;
    }
    if (out.year < 1 || out.year > 9999)
    {
        return false;
    }
    if (out.month < 1 || out.month > 12)
    {
        return false;
    }
    if (out.day < 1 || out.day > DaysInMonth(out.year, out.month))
    {
        return false;
    }
    if (out.hour > 23 || out.minute > 59 || out.second > 59)
    {
        return false;
    }
    return true;
}

// The managed string behind a nullable object argument; returns nullptr for
// null / non-string objects (defensive, like timeext.cpp's format handling).
const Il2CppString* AsStringOrNull(Il2CppObject* obj)
{
    const Il2CppString* s = reinterpret_cast<const Il2CppString*>(obj);
    return (s != nullptr && s->obj.klass == il2cpp_string_class()) ? s : nullptr;
}

//------------------------------------------------------------------------------
// TimeSpan Parse core -- net48 invariant "c" grammar:
//   [ws] [-] ["d."] h:m:s [.fffffff] [ws]
//   * A '.'-less form may drop trailing slots one at a time ("h:m", "6");
//     a lone number is DAYS ("6" == 6 days, net48). The days form requires
//     the '.' then a ':' layout; "6." (a bare trailing dot) is days only.
//   * The seconds fraction takes 1..7 digits (more -> Format). Component
//     digit values are NOT range-capped (hours may exceed 23) but the
//     accumulated ticks must fit int64 (else Overflow).
//   * Returns: 0 = format error, 1 = ok, 2 = overflow.
//------------------------------------------------------------------------------
int32_t TryParseTimespanCore(const char16_t* s, int32_t len, Il2CppTimeSpan& outTs)
{
    const char16_t* p = s;
    const char16_t* const end = s + len;

    auto IsWs = [](char16_t ch) -> bool
    {
        return ch == u' ' || ch == u'\t' || ch == u'\r' || ch == u'\n' || ch == u'\v' || ch == u'\f' ||
               ch == 0x00A0 || ch == 0x1680 || (ch >= 0x2000 && ch <= 0x200A) || ch == 0x2028 ||
               ch == 0x2029 || ch == 0x202F || ch == 0x205F || ch == 0x3000;
    };
    auto IsDigit = [](char16_t ch) -> bool { return ch >= u'0' && ch <= u'9'; };

    while (p < end && IsWs(*p)) ++p; // leading whitespace
    bool neg = false;
    if (p < end && *p == u'-')
    {
        neg = true;
        ++p;
    }

    auto ParseNum = [&](int64_t& v) -> int32_t
    {
        v = 0;
        bool any = false;
        while (p < end && IsDigit(*p))
        {
            any = true;
            int64_t d = *p - u'0';
            if (v > (int64_t(0x7FFFFFFFFFFFFFFFLL) - d) / 10) return 2; // digit overflow
            v = v * 10 + d;
            ++p;
        }
        return any ? 0 : 1; // 1 = missing/empty
    };

    // Group-slot mapping (net48 TimeSpanParse): values separated by '.' for
    // the first ('days') element and ':' for the rest, or colons only:
    //   1 group  -> days        ("6" == 6 days)
    //   2 groups -> hh:mm
    //   3 groups -> hh:mm:ss
    //   4 groups -> d.hh:mm:ss (introduced by a leading '.' OR a 4th ':').
    // If a leading '.' introduces the days element ("d."), all four groups
    // are required; the trailing-dot "d." alone is days-only.
    int64_t groups[4];
    int32_t gn = 0;
    bool daysPresent = false;

    int32_t r = ParseNum(groups[gn]);
    if (r == 1) return 0;
    if (r == 2) return 2;
    ++gn;

    if (p < end && *p == u'.')
    {
        ++p;
        if (p < end && *p == u':')
        {
            return 0; // "d.:" is not a valid layout
        }
        daysPresent = true;
        if (p < end && IsDigit(*p))
        {
            r = ParseNum(groups[gn]);
            if (r == 2) return 2;
            if (r == 1) return 0;
            ++gn;
        }
        // else "6." -- days-only (validated below via the empty-digit rule).
    }

    while (p < end && *p == u':')
    {
        if (gn >= 4) return 0; // at most four groups
        ++p;
        r = ParseNum(groups[gn]);
        if (r == 1) return 0;
        if (r == 2) return 2;
        ++gn;
    }

    int64_t days = 0, hours = 0, minutes = 0, seconds = 0;
    switch (gn)
    {
        case 1: days = groups[0]; break;
        case 2: hours = groups[0]; minutes = groups[1]; break;
        case 3: hours = groups[0]; minutes = groups[1]; seconds = groups[2]; break;
        case 4: days = groups[0]; hours = groups[1]; minutes = groups[2]; seconds = groups[3]; break;
        default: return 0;
    }
    if (daysPresent && gn == 1)
    {
        // "6." days-only: ok.
    }
    else if (daysPresent && gn != 4)
    {
        // "d.h" / "d.h:m" fragments are not valid layouts.
        return 0;
    }

    // Optional seconds fraction: '.' at the end of the s group.
    int64_t fracTicks = 0;
    if (p < end && *p == u'.')
    {
        ++p;
        int32_t digits = 0;
        int64_t frac = 0;
        while (p < end && IsDigit(*p))
        {
            ++digits;
            if (digits > 7) return 0; // more than 7 fraction digits -> Format
            frac = frac * 10 + (*p - u'0');
            ++p;
        }
        while (digits < 7) { frac *= 10; ++digits; }
        fracTicks = frac;
    }

    while (p < end && IsWs(*p)) ++p;
    if (p != end) return 0; // trailing garbage

    // Accumulate with overflow checks (each term and the sum must fit int64).
    const int64_t TicksPerDay = 864000000000LL;
    const int64_t TicksPerHour = 36000000000LL;
    const int64_t TicksPerMinute = 600000000LL;
    const int64_t TicksPerSecond = 10000000LL;
    if (days > int64_t(0x7FFFFFFFFFFFFFFFLL) / TicksPerDay) return 2;
    if (hours > int64_t(0x7FFFFFFFFFFFFFFFLL) / TicksPerHour) return 2;
    if (minutes > int64_t(0x7FFFFFFFFFFFFFFFLL) / TicksPerMinute) return 2;
    if (seconds > int64_t(0x7FFFFFFFFFFFFFFFLL) / TicksPerSecond) return 2;
    int64_t total = 0;
    if (total > int64_t(0x7FFFFFFFFFFFFFFFLL) - days * TicksPerDay) return 2;
    total += days * TicksPerDay;
    if (total > int64_t(0x7FFFFFFFFFFFFFFFLL) - hours * TicksPerHour) return 2;
    total += hours * TicksPerHour;
    if (total > int64_t(0x7FFFFFFFFFFFFFFFLL) - minutes * TicksPerMinute) return 2;
    total += minutes * TicksPerMinute;
    if (total > int64_t(0x7FFFFFFFFFFFFFFFLL) - seconds * TicksPerSecond) return 2;
    total += seconds * TicksPerSecond;
if (total > int64_t(0x7FFFFFFFFFFFFFFFLL) - fracTicks) return 2;
    total += fracTicks;

    if (neg)
    {
        total = -total;
    }
    outTs = Il2CppTimeSpan(total);
    return 1;
}

} // namespace

//------------------------------------------------------------------------------
// System.DateTime -- ParseExact / Parse.
//------------------------------------------------------------------------------
Il2CppDateTime il2cpp_datetime_parse_exact(Il2CppObject* input, Il2CppObject* format)
{
    // The 2-arg net48 overload == the 4-arg form with DateTimeStyles.None.
    return il2cpp_datetime_parse_exact_styles(input, format, 0);
}

// DateTime.ParseExact(string input, string format, IFormatProvider, DateTimeStyles):
// the 4-arg net48 overload. The provider is invariant-culture only (dropped by
// the transpiler); 'styles' carries the whitespace / NoCurrentDateDefault flags.
Il2CppDateTime il2cpp_datetime_parse_exact_styles(Il2CppObject* input, Il2CppObject* format,
                                                  int32_t styles)
{
    const Il2CppString* inStr = AsStringOrNull(input);
    if (inStr == nullptr)
    {
        il2cpp_throw_argument_null_exception("input");
    }
    const Il2CppString* fmtStr = AsStringOrNull(format);
    if (fmtStr == nullptr)
    {
        il2cpp_throw_argument_null_exception("format");
    }
    if (fmtStr->length == 0)
    {
        ThrowFormatInvalidSpecifier();
    }
    ParsedFields fields;
    if (!TryParseExact(inStr->chars, inStr->length, fmtStr->chars, fmtStr->length, styles, fields))
    {
        ThrowFormatUnrecognized();
    }
    return ComposeDateTime(fields);
}

// DateTime.ParseExact(string input, string[] formats, IFormatProvider,
// DateTimeStyles): try each format in order; the first match wins. A null
// formats array -> ArgumentNullException; an empty array -> FormatException; a
// null/empty ELEMENT is skipped (net48 treats it as a non-matching format).
Il2CppDateTime il2cpp_datetime_parse_exact_multi(Il2CppObject* input, Il2CppArray* formats,
                                                 int32_t styles)
{
    const Il2CppString* inStr = AsStringOrNull(input);
    if (inStr == nullptr)
    {
        il2cpp_throw_argument_null_exception("input");
    }
    if (formats == nullptr)
    {
        il2cpp_throw_argument_null_exception("formats");
    }
    if (formats->length == 0)
    {
        ThrowFormatInvalidSpecifier();
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(formats->data);
    ParsedFields fields;
    for (int32_t i = 0; i < formats->length; ++i)
    {
        const Il2CppString* fmt = reinterpret_cast<const Il2CppString*>(slots[i]);
        if (fmt == nullptr || fmt->length == 0)
        {
            continue; // a null/empty element never matches
        }
        if (TryParseExact(inStr->chars, inStr->length, fmt->chars, fmt->length, styles, fields))
        {
            return ComposeDateTime(fields);
        }
    }
    ThrowFormatUnrecognized();
    return Il2CppDateTime(0); // unreachable (the throw never returns)
}

// DateTime.TryParseExact(string input, string format, IFormatProvider,
// DateTimeStyles, out DateTime value): the no-throw twin of ParseExact -- a
// null input/format and a format mismatch all return false (net48), leaving
// 'value' untouched.
bool il2cpp_datetime_try_parse_exact(Il2CppObject* input, Il2CppObject* format,
                                     int32_t styles, Il2CppDateTime* value)
{
    const Il2CppString* inStr = AsStringOrNull(input);
    if (inStr == nullptr)
    {
        return false;
    }
    const Il2CppString* fmtStr = AsStringOrNull(format);
    if (fmtStr == nullptr || fmtStr->length == 0)
    {
        return false;
    }
    ParsedFields fields;
    if (!TryParseExact(inStr->chars, inStr->length, fmtStr->chars, fmtStr->length, styles, fields))
    {
        return false;
    }
    if (value != nullptr)
    {
        *value = ComposeDateTime(fields);
    }
    return true;
}

// DateTime.TryParseExact(string input, string[] formats, IFormatProvider,
// DateTimeStyles, out DateTime value): try each format in order; false when
// none match (null/empty array included).
bool il2cpp_datetime_try_parse_exact_multi(Il2CppObject* input, Il2CppArray* formats,
                                           int32_t styles, Il2CppDateTime* value)
{
    const Il2CppString* inStr = AsStringOrNull(input);
    if (inStr == nullptr || formats == nullptr)
    {
        return false;
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(formats->data);
    ParsedFields fields;
    for (int32_t i = 0; i < formats->length; ++i)
    {
        const Il2CppString* fmt = reinterpret_cast<const Il2CppString*>(slots[i]);
        if (fmt == nullptr || fmt->length == 0)
        {
            continue;
        }
        if (TryParseExact(inStr->chars, inStr->length, fmt->chars, fmt->length, styles, fields))
        {
            if (value != nullptr)
            {
                *value = ComposeDateTime(fields);
            }
            return true;
        }
    }
    return false;
}

Il2CppDateTime il2cpp_datetime_parse(Il2CppObject* input)
{
    const Il2CppString* inStr = AsStringOrNull(input);
    if (inStr == nullptr)
    {
        il2cpp_throw_argument_null_exception("input");
    }
    ParsedFields fields;
    if (!TryParseDefault(inStr->chars, inStr->length, fields))
    {
        ThrowFormatUnrecognized();
    }
    return ComposeDateTime(fields);
}

Il2CppTimeSpan il2cpp_timespan_parse(Il2CppString* input)
{
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("input");
    }
    Il2CppTimeSpan ts;
    switch (TryParseTimespanCore(input->chars, input->length, ts))
    {
        case 1:
            return ts;
        case 2:
            il2cpp_throw_overflow_exception(
                "The TimeSpan was not parsed because the value of at least one of its numeric components is out of range.");
            return ts;
        default:
            il2cpp_throw_format_exception("String was not recognized as a valid TimeSpan.");
            return ts;
    }
}

//------------------------------------------------------------------------------
// System.TimeSpan -- TryParse (no-throw twin of Parse; null/format/overflow
// all return false, exactly like net48 TimeSpan.TryParse).
//------------------------------------------------------------------------------
bool il2cpp_timespan_try_parse(Il2CppString* input, Il2CppTimeSpan* value)
{
    if (input == nullptr)
    {
        return false;
    }
    Il2CppTimeSpan ts;
    if (TryParseTimespanCore(input->chars, input->length, ts) != 1)
    {
        return false;
    }
    if (value != nullptr)
    {
        *value = ts;
    }
    return true;
}

//------------------------------------------------------------------------------
// System.DateTime -- ToString(format) / Compare: canonical delegation.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_datetime_to_string_format(Il2CppDateTime dt, Il2CppObject* format)
{
    // Canonical delegation stays INVARIANT (the pre-saudit5 contract this
    // wrapper and its unit tests pin): pass the invariant culture carrier
    // (empty name). The transpiled ToString(string) path passes nullptr
    // (host default zh-CN) and ToString(string, IFormatProvider) passes the
    // IL provider through (cultureprobe regression fix).
    return icall_datetime_to_string_format(
        dt, format,
        reinterpret_cast<Il2CppObject*>(il2cpp_culture_get_invariant()));
}

int32_t il2cpp_datetime_compare(Il2CppDateTime left, Il2CppDateTime right)
{
    return icall_datetime_compare_to(left, right);
}

//------------------------------------------------------------------------------
// System.DateTime -- tick arithmetic (AddTicks / - ticks).
//------------------------------------------------------------------------------
Il2CppDateTime il2cpp_datetime_add_ticks(Il2CppDateTime dt, int64_t ticks)
{
    if (ticks > 0)
    {
        // Adding: the result must stay <= MaxValue. The comparison form
        // never computes dt.ticks + ticks beyond the guard, so no overflow.
        if (dt.ticks > kMaxDateTimeTicks - ticks)
        {
            ThrowArgOutOfRange();
        }
        return Il2CppDateTime(dt.ticks + ticks);
    }
    // Subtracting (ticks <= 0): dt.ticks + ticks >= 0 + INT64_MIN is always
    // int64-representable; the only failure is dropping below MinValue (0).
    const int64_t result = dt.ticks + ticks;
    if (result < 0)
    {
        ThrowArgOutOfRange();
    }
    return Il2CppDateTime(result);
}

Il2CppDateTime il2cpp_datetime_subtract_ticks(Il2CppDateTime dt, int64_t ticks)
{
    if (ticks < 0)
    {
        // Subtracting a negative delta adds |ticks|: the result must stay
        // <= MaxValue. kMaxDateTimeTicks + ticks is safe for every negative
        // ticks (incl. INT64_MIN -- the comparison never negates it).
        if (dt.ticks > kMaxDateTimeTicks + ticks)
        {
            ThrowArgOutOfRange();
        }
        return Il2CppDateTime(dt.ticks - ticks);
    }
    // Subtracting (ticks >= 0): dt.ticks - ticks >= 0 - INT64_MAX is always
    // int64-representable; the only failure is dropping below MinValue (0).
    const int64_t result = dt.ticks - ticks;
    if (result < 0)
    {
        ThrowArgOutOfRange();
    }
    return Il2CppDateTime(result);
}

} // namespace runtime
} // namespace il2cpp
