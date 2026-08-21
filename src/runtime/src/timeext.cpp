//------------------------------------------------------------------------------
// timeext.cpp -- System.DateTime.ToString(format) implementation
// (see il2cpp_time_ext.h for the supported format subset and the net48
// empirical contract that pins it).
//
// Design:
//   * The civil components are computed HERE from the raw tick count (the
//     Hinnant civil_from_days algorithm, identical to time.cpp's private
//     helper -- time.cpp's functions live in an anonymous namespace and are
//     deliberately not shared; the two copies are pinned against each other
//     by the fixed-tick unit tests).
//   * Standard single-letter formats are mapped onto their invariant-culture
//     patterns and formatted through the SAME custom engine (DRY).
//   * null / empty format -> General "G" (net48 rule): delegates to the
//     existing culture-neutral default formatter icall_datetime_to_string
//     (time.cpp), which outputs exactly invariant "G".
//   * Unknown single LETTER format -> FormatException (net48: unknown
//     standard specifier). Custom strings treat every character outside the
//     y/M/d/H/h/m/s/f/F/t subset as a literal (net48 would interpret
//     timezone specifiers -- reported divergence; the AM/PM 't' token IS
//     implemented, pinned by the net48 oracle).
//   * f/F tokens format the fractional second: the first N digits of the
//     7-digit sub-second tick count, zero-padded ('f') or with trailing
//     zeros trimmed ('F'); N > 7 throws FormatException (net48 oracle).
//     "G" is a standard specifier mapping to the same pattern as the
//     default ToString() (net48 oracle: invariant "MM/dd/yyyy HH:mm:ss").
//   * Output is built in a std::u16string and allocated with ONE
//     il2cpp_string_new call (no intermediate allocations, no GC window).
//------------------------------------------------------------------------------

#include "il2cpp_time_ext.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_string.h"
#include "timezone.h" // il2cpp_datetime_from_local (standard "U" universal format)

#include <cstdint>
#include <string>

namespace il2cpp {
namespace runtime {

namespace {

// Tick constants (mirror time.cpp; anonymous-namespace locals there).
constexpr int64_t kTicksPerSecond = 10000000;
constexpr int64_t kTicksPerMinute = 600000000;
constexpr int64_t kTicksPerHour = 36000000000;
constexpr int64_t kTicksPerDay = 864000000000;

// en-US (net48 invariant) month names -- index [month - 1].
const char* kMonthNamesFull[12] = {"January",  "February", "March",   "April",
                                   "May",      "June",     "July",    "August",
                                   "September", "October", "November", "December"};
const char* kMonthNamesAbbr[12] = {"Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                   "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"};

// en-US (net48 invariant) weekday names -- index = DayOfWeek (Sunday = 0).
const char* kDayNamesFull[7] = {"Sunday",   "Monday", "Tuesday", "Wednesday",
                                "Thursday", "Friday", "Saturday"};
const char* kDayNamesAbbr[7] = {"Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"};

// Civil components of a DateTime (valid dates have non-negative ticks).
struct CivilTime
{
    int32_t year;
    int32_t month;
    int32_t day;
    int32_t hour;
    int32_t minute;
    int32_t second;
    int32_t fraction; // sub-second ticks, 0..9999999 (7 fraction digits)
};

// Hinnant's civil_from_days on the .NET epoch day count (see time.cpp
// header comment; the constant pair is the Unix/.NET epoch offset).
void CivilFromNetDays(int64_t days, int32_t* year, int32_t* month, int32_t* day)
{
    const int64_t z = days - 719162 + 719468; // 1970-based day count for the algorithm
    const int64_t era = (z >= 0 ? z : z - 146096) / 146097;
    const int64_t doe = z - era * 146097;                                       // [0, 146096]
    const int64_t yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365; // [0, 399]
    const int64_t y = yoe + era * 400;
    const int64_t doy = doe - (365 * yoe + yoe / 4 - yoe / 100);                // [0, 365]
    const int64_t mp = (5 * doy + 2) / 153;                                     // [0, 11]
    const int64_t d = doy - (153 * mp + 2) / 5 + 1;                             // [1, 31]
    const int64_t m = mp < 10 ? mp + 3 : mp - 9;                                // [1, 12]
    *year = static_cast<int32_t>(y + (m <= 2 ? 1 : 0));
    *month = static_cast<int32_t>(m);
    *day = static_cast<int32_t>(d);
}

CivilTime GetCivilTime(Il2CppDateTime dt)
{
    const int64_t days = dt.ticks / kTicksPerDay;
    const int64_t timeTicks = dt.ticks % kTicksPerDay; // 0..TicksPerDay-1 for valid dates
    CivilTime result;
    CivilFromNetDays(days, &result.year, &result.month, &result.day);
    result.hour = static_cast<int32_t>(timeTicks / kTicksPerHour);
    result.minute = static_cast<int32_t>((timeTicks % kTicksPerHour) / kTicksPerMinute);
    result.second = static_cast<int32_t>((timeTicks % kTicksPerMinute) / kTicksPerSecond);
    result.fraction = static_cast<int32_t>(timeTicks % kTicksPerSecond);
    return result;
}

// .NET DayOfWeek: (days since 0001-01-01 + 1) % 7 (Sunday = 0; 0001-01-01
// was a Monday) -- same formula as time.cpp's icall_datetime_get_dayofweek.
int32_t DayOfWeek(Il2CppDateTime dt)
{
    return static_cast<int32_t>((dt.ticks / kTicksPerDay + 1) % 7);
}

//------------------------------------------------------------------------------
// Output helpers (std::u16string accumulates UTF-16 units; allocated once
// into a managed string at the end).
//------------------------------------------------------------------------------

void AppendAscii(std::u16string& out, const char* text)
{
    for (; *text != '\0'; ++text)
    {
        out.push_back(static_cast<char16_t>(*text));
    }
}

// Decimal digits, zero-padded to 'minDigits' (never truncated). 'value' is
// non-negative (validated at DateTime construction).
void AppendInt(std::u16string& out, int32_t value, int32_t minDigits)
{
    char digits[10];
    int32_t n = 0;
    do
    {
        digits[n++] = static_cast<char>('0' + value % 10);
        value /= 10;
    } while (value > 0);
    for (int32_t pad = n; pad < minDigits; ++pad)
    {
        out.push_back(u'0');
    }
    while (n > 0)
    {
        --n;
        out.push_back(static_cast<char16_t>(digits[n]));
    }
}

bool IsTokenLetter(char16_t c)
{
    return c == u'y' || c == u'M' || c == u'd' || c == u'H'
        || c == u'h' || c == u'm' || c == u's' || c == u'f' || c == u'F'
        || c == u't';
}

// One custom-format token: 'count' consecutive copies of 'spec' (net48
// repeat semantics, pinned by the timeext e2e oracle).
void AppendToken(std::u16string& out, const CivilTime& t, int32_t dow,
                 char16_t spec, int32_t count)
{
    switch (spec)
    {
    case u'y':
        // "yy" is the LAST TWO digits; other lengths are the full year
        // zero-padded to the token length (min 1, i.e. no padding).
        if (count == 2)
        {
            AppendInt(out, t.year % 100, 2);
        }
        else
        {
            AppendInt(out, t.year, count < 2 ? 1 : count);
        }
        break;
    case u'M':
        if (count <= 2)
        {
            AppendInt(out, t.month, count);
        }
        else
        {
            AppendAscii(out, count == 3 ? kMonthNamesAbbr[t.month - 1]
                                        : kMonthNamesFull[t.month - 1]);
        }
        break;
    case u'd':
        if (count <= 2)
        {
            AppendInt(out, t.day, count);
        }
        else
        {
            AppendAscii(out, count == 3 ? kDayNamesAbbr[dow] : kDayNamesFull[dow]);
        }
        break;
    case u'H':
        // 2-hour-or-more tokens are 2-digit padded (net48: "HHH" -> "13").
        AppendInt(out, t.hour, count <= 1 ? 1 : 2);
        break;
    case u'h':
    {
        int32_t hour12 = t.hour % 12;
        if (hour12 == 0)
        {
            hour12 = 12;
        }
        AppendInt(out, hour12, count <= 1 ? 1 : 2);
        break;
    }
    case u'm':
        AppendInt(out, t.minute, count <= 1 ? 1 : 2);
        break;
    case u's':
        AppendInt(out, t.second, count <= 1 ? 1 : 2);
        break;
    case u't':
        // AM/PM designator: "t" -> first character ("A"/"P"), "tt" -> full
        // ("AM"/"PM"), longer runs -> the full designator (net48 rule:
        // DateTimeFormatInfo.AMDesignator/PMDesignator, invariant culture).
        if (t.hour < 12)
        {
            AppendAscii(out, count <= 1 ? "A" : "AM");
        }
        else
        {
            AppendAscii(out, count <= 1 ? "P" : "PM");
        }
        break;
    case u'f':
    case u'F':
    {
        // Fractional second: the first 'count' digits of the 7-digit
        // sub-second tick count (net48 oracle: .123 s -> fff "123",
        // ffffff "123000"; .000 s -> fff "000", FFF ""). More than 7
        // digits is an invalid format (net48: "ffffffff" throws
        // FormatException; DateTimeFormat.MaxSecondsFractionDigits == 7).
        if (count > 7)
        {
            il2cpp_throw_format_exception("Format specifier was invalid.");
        }
        int32_t frac = t.fraction;
        for (int32_t i = count; i < 7; ++i)
        {
            frac /= 10;
        }
        AppendInt(out, frac, count);
        if (spec == u'F')
        {
            // 'F' is 'f' with trailing zeros trimmed (net48: FFF on
            // .000 s is the empty string).
            while (!out.empty() && out.back() == u'0')
            {
                out.pop_back();
            }
        }
        break;
    }
    }
}

// Custom-format engine: token letters (y/M/d/H/h/m/s/f/F/t) expand per
// AppendToken; quoted literal groups ('...' / "...", net48 strips the quote
// characters) and backslash escapes ('\x' -> literal x) emit verbatim;
// every other character is emitted literally. Unterminated quotes / a
// trailing backslash are invalid formats (net48 FormatException). 'fmt'/'len'
// are UTF-16 units.
std::u16string FormatCustom(const CivilTime& t, int32_t dow,
                            const char16_t* fmt, int32_t len)
{
    std::u16string out;
    int32_t i = 0;
    while (i < len)
    {
        const char16_t c = fmt[i];
        if (c == u'\'' || c == u'"')
        {
            // Quoted literal group (net48: 'at' -> "at", a'b'c -> "abc",
            // d'dd'd -> "15dd15"). Backslash still escapes inside (net48:
            // '\\' -> "\"); an escaped closing quote does NOT close the
            // group. Unterminated -> FormatException.
            const char16_t close = c;
            int32_t q = i + 1;
            while (q < len && fmt[q] != close)
            {
                if (fmt[q] == u'\\')
                {
                    if (q + 1 >= len)
                    {
                        il2cpp_throw_format_exception("Format specifier was invalid.");
                    }
                    out.push_back(fmt[q + 1]);
                    q += 2;
                    continue;
                }
                out.push_back(fmt[q]);
                ++q;
            }
            if (q >= len)
            {
                il2cpp_throw_format_exception("Format specifier was invalid.");
            }
            i = q + 1;
        }
        else if (c == u'\\')
        {
            // Backslash escape: the next character is a literal (net48:
            // \T -> "T", \\ -> "\"). A trailing backslash is invalid.
            if (i + 1 >= len)
            {
                il2cpp_throw_format_exception("Format specifier was invalid.");
            }
            out.push_back(fmt[i + 1]);
            i += 2;
        }
        else if (IsTokenLetter(c))
        {
            int32_t run = 1;
            while (i + run < len && fmt[i + run] == c)
            {
                ++run;
            }
            AppendToken(out, t, dow, c, run);
            i += run;
        }
        else
        {
            out.push_back(c);
            ++i;
        }
    }
    return out;
}

// Formats an ASCII pattern through the custom engine (used for the standard
// single-letter patterns, which are literal invariant strings).
std::u16string FormatAsciiPattern(const CivilTime& t, int32_t dow, const char* pattern)
{
    char16_t buf[64];
    int32_t n = 0;
    for (const char* p = pattern; *p != '\0' && n < 63; ++p)
    {
        buf[n++] = static_cast<char16_t>(*p);
    }
    return FormatCustom(t, dow, buf, n);
}

} // namespace

//------------------------------------------------------------------------------
// System.DateTime -- ToString(format).
//------------------------------------------------------------------------------
Il2CppObject* icall_datetime_to_string_format(Il2CppDateTime dt, Il2CppObject* format)
{
    const Il2CppString* fs = reinterpret_cast<const Il2CppString*>(format);

    // null, non-string, or empty -> General "G" (net48: empty is treated as
    // "G" too). Reuses the existing culture-neutral default formatter, whose
    // fixed "MM/dd/yyyy HH:mm:ss" output IS the invariant "G" pattern.
    if (fs == nullptr || fs->obj.klass != il2cpp_string_class() || fs->length == 0)
    {
        return icall_datetime_to_string(dt);
    }

    const CivilTime t = GetCivilTime(dt);
    const int32_t dow = DayOfWeek(dt);
    std::u16string out;

    if (fs->length == 1)
    {
        const char16_t c = fs->chars[0];
        const bool isLetter = (c >= u'a' && c <= u'z') || (c >= u'A' && c <= u'Z');
        const char* pattern = nullptr;
        if (isLetter)
        {
            switch (c)
            {
            case u'd': pattern = "MM/dd/yyyy"; break;
            case u'D': pattern = "dddd, dd MMMM yyyy"; break;
            case u't': pattern = "HH:mm"; break;
            case u'T': pattern = "HH:mm:ss"; break;
            case u'f': pattern = "dddd, dd MMMM yyyy HH:mm"; break;
            case u'F': pattern = "dddd, dd MMMM yyyy HH:mm:ss"; break;
            case u'G': pattern = "MM/dd/yyyy HH:mm:ss"; break;
            case u'g': pattern = "MM/dd/yyyy HH:mm"; break;
            case u'm': pattern = "MMMM dd"; break;
            case u'y': pattern = "yyyy MMMM"; break;
            case u's': pattern = "yyyy-MM-ddTHH:mm:ss"; break;
            case u'O': pattern = "yyyy-MM-ddTHH:mm:ss.fffffff"; break;
            case u'R':
            case u'r': pattern = "ddd, dd MMM yyyy HH:mm:ss 'GMT'"; break;
            case u'u': pattern = "yyyy-MM-dd HH:mm:ss'Z'"; break;
            case u'U':
                // Universal full: the UTC-converted value, then the full
                // date/time pattern (net48 oracle: the net48-invariant U on
                // an Unspecified DateTime converts to UTC first -- same as
                // ToUniversalTime on this machine).
                {
                    const Il2CppDateTime utc = il2cpp_datetime_from_local(dt);
                    out = FormatAsciiPattern(GetCivilTime(utc), DayOfWeek(utc),
                                             "dddd, dd MMMM yyyy HH:mm:ss");
                }
                break;
            default:
                // net48: an unknown single-letter standard specifier throws
                // FormatException.
                il2cpp_throw_format_exception("Format specifier was invalid.");
            }
            if (pattern != nullptr)
            {
                out = FormatAsciiPattern(t, dow, pattern);
            }
        }
        else
        {
            // Single non-letter character: custom format of one literal.
            out.push_back(c);
        }
    }
    else
    {
        out = FormatCustom(t, dow, fs->chars, fs->length);
    }

    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
}

} // namespace runtime
} // namespace il2cpp
