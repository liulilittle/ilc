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
//   * The provider -> culture mapping and the custom-format engine are
//     exported through il2cpp_time_fmt.h so the DateTimeOffset formatter
//     (dtoffset.cpp) reuses them (offset-aware z / zz / zzz / K tokens and
//     the " zzz" default suffix).
//   * Standard single-letter formats are PROVIDER-AWARE (cultureprobe
//     regression fix): the trailing IFormatProvider slot selects the pattern
//     family -- nullptr => host default zh-CN (saudit5), the empty culture
//     name => invariant (culturedeep/timeformat/datetime2 pass
//     CultureInfo.InvariantCulture), "zh-CN" => zh-CN, an unknown name =>
//     invariant fallback (+ optional log). Both families format through the
//     SAME custom engine (DRY).
//   * null / empty format -> General "G" (net48 rule) in the SELECTED
//     culture: zh-CN "yyyy/M/d H:mm:ss" (host default, saudit5) vs invariant
//     "MM/dd/yyyy HH:mm:ss" (empty-name carrier).
//   * Unknown single LETTER format -> FormatException (net48: unknown
//     standard specifier). Custom strings treat every character outside the
//     y/M/d/H/h/m/s/f/F/t subset as a literal (net48 would interpret
//     timezone specifiers -- reported divergence; the AM/PM 't' token IS
//     implemented, pinned by the net48 oracle). '%' forces the NEXT character
//     to be read as a single custom specifier (net48: "%d" -> day digits);
//     the timezone letters stay literal for DateTime.
//   * f/F tokens format the fractional second: the first N digits of the
//     7-digit sub-second tick count, zero-padded ('f') or with trailing
//     zeros trimmed ('F'); N > 7 throws FormatException (net48 oracle).
//     "G" is a standard specifier mapping to the same pattern as the
//     default ToString() within the selected culture (net48 oracle: zh-CN
//     "yyyy/M/d H:mm:ss" on the host, invariant "MM/dd/yyyy HH:mm:ss").
//   * Output is built in a std::u16string and allocated with ONE
//     il2cpp_string_new call (no intermediate allocations, no GC window).
//------------------------------------------------------------------------------

#include "il2cpp_time_ext.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_string.h"
#include "il2cpp_time_fmt.h"
#include "timezone.h" // il2cpp_datetime_from_local (standard "U" universal format)

#include <cstdint>
#include <cstdio>
#include <cstdlib>
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

// Optional diagnostic: appends the unknown provider name to the file named
// by IL2CPP_CULTURE_PROVIDER_LOG. NO stdout/stderr traffic (the e2e harness
// byte-diffs both streams); unset -> silent deterministic fallback.
void LogUnknownProvider(const Il2CppString* name)
{
    const char* path = std::getenv("IL2CPP_CULTURE_PROVIDER_LOG");
    if (path == nullptr || path[0] == '\0')
    {
        return;
    }
    std::FILE* f = std::fopen(path, "ab");
    if (f == nullptr)
    {
        return;
    }
    std::fputs("datetime-to-string: unknown IFormatProvider culture '", f);
    for (int32_t i = 0; i < name->length; ++i)
    {
        const char16_t c = name->chars[i];
        std::fputc(c < 0x80 ? static_cast<char>(c) : '?', f);
    }
    std::fputs("' -> invariant fallback\n", f);
    std::fclose(f);
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

// DateTimeOffset-only custom tokens (only reachable when the caller passed
// offset minutes; DateTime keeps them as literals).
bool IsOffsetToken(char16_t c)
{
    return c == u'z' || c == u'K';
}

// net48 custom offset token body, 'count' repetitions of z (or K, which is
// the full "+HH:mm" form):
//   z   -> "+8"      (hours, no zero padding)
//   zz  -> "+08"     (hours, 2-digit)
//   zzz -> "+08:00"  (hours + minutes; zzzz+ keep the same HH:mm form)
// A zero offset is "+0" / "+00" / "+00:00" (the sign is always present).
void AppendOffsetZToken(std::u16string& out, int32_t offsetMinutes, int32_t count)
{
    const int32_t abs = offsetMinutes < 0 ? -offsetMinutes : offsetMinutes;
    out.push_back(offsetMinutes < 0 ? u'-' : u'+');
    if (count >= 3)
    {
        AppendInt(out, abs / 60, 2);
        out.push_back(u':');
        AppendInt(out, abs % 60, 2);
    }
    else
    {
        AppendInt(out, abs / 60, count == 2 ? 2 : 1);
    }
}

// One custom-format token: 'count' consecutive copies of 'spec' (net48
// repeat semantics, pinned by the timeext e2e oracle). 'offsetMinutes' is
// non-null only for the DateTimeOffset formatter (z / K token expansion).
void AppendToken(std::u16string& out, const CivilTime& t, int32_t dow,
                 char16_t spec, int32_t count, const int32_t* offsetMinutes)
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
        const size_t start = out.size();
        AppendInt(out, frac, count);
        if (spec == u'F')
        {
            // 'F' is 'f' with trailing zeros trimmed; ONLY the digits this
            // token appended are trimmed (a literal '0' before the token must
            // survive). When the whole fraction trims away, a preceding '.'
            // is removed too (net48 oracle: "ss.FFF" on .000 -> "30",
            // "ss'.'FFF" -> "30" too -- the rule is output-adjacent -- while
            // "ss-FFF" -> "30-" keeps the '-').
            while (out.size() > start && out.back() == u'0')
            {
                out.pop_back();
            }
            if (out.size() == start && start > 0 && out[start - 1] == u'.')
            {
                out.pop_back();
            }
        }
        break;
    }
    case u'z':
        // DateTimeOffset numeric offset ("+8"/"+08"/"+08:00"); DateTime
        // never reaches this arm (see IsOffsetToken).
        if (offsetMinutes != nullptr)
        {
            AppendOffsetZToken(out, *offsetMinutes, count);
        }
        break;
    case u'K':
        // net48 DateTimeOffset custom "K" is the full "+HH:mm" offset, once
        // per K in the run ("KK" -> "+08:00+08:00"). (A single-character "K"
        // format never reaches the custom engine -- it is a
        // standard-specifier lookup that throws, like "z".)
        if (offsetMinutes != nullptr)
        {
            for (int32_t k = 0; k < count; ++k)
            {
                AppendOffsetZToken(out, *offsetMinutes, 3);
            }
        }
        break;
    }
}

} // namespace

//------------------------------------------------------------------------------
// Shared civil-time helpers (il2cpp_time_fmt.h).
//------------------------------------------------------------------------------

CivilTime CivilTimeFromTicks(Il2CppDateTime dt)
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

int32_t WeekdayFromTicks(Il2CppDateTime dt)
{
    // .NET DayOfWeek: (days since 0001-01-01 + 1) % 7 (Sunday = 0;
    // 0001-01-01 was a Monday) -- same formula as time.cpp's
    // icall_datetime_get_dayofweek.
    return static_cast<int32_t>((dt.ticks / kTicksPerDay + 1) % 7);
}

// Resolves the provider carrier to a pattern family (see the header banner):
//   nullptr                       -> ZhCn (host default: saudit5)
//   non-string object (defensive) -> ZhCn (same as a null provider)
//   empty culture name            -> Invariant (the InvariantCulture carrier)
//   "zh-CN"                       -> ZhCn (explicit current-culture call)
//   any other name                -> Invariant fallback + optional log
DateCulture ResolveProviderCulture(Il2CppObject* provider)
{
    if (provider == nullptr)
    {
        return DateCulture::ZhCn;
    }
    const Il2CppString* name = reinterpret_cast<const Il2CppString*>(provider);
    if (name->obj.klass != il2cpp_string_class())
    {
        return DateCulture::ZhCn;
    }
    if (name->length == 0)
    {
        return DateCulture::Invariant;
    }
    static const char16_t kZhCn[5] = {u'z', u'h', u'-', u'C', u'N'};
    if (name->length == 5)
    {
        bool same = true;
        for (int32_t i = 0; i < 5; ++i)
        {
            if (name->chars[i] != kZhCn[i])
            {
                same = false;
                break;
            }
        }
        if (same)
        {
            return DateCulture::ZhCn;
        }
    }
    LogUnknownProvider(name);
    return DateCulture::Invariant;
}

// net48 standard single-letter pattern table. PROVIDER-AWARE: zh-CN (host
// default, the saudit5 oracle) vs invariant (the empty-name carrier and the
// unknown-name fallback). "s"/"u"/"O"/"R"/"r" are culture-invariant by the
// net48 contract. "U" (universal full) resolves to the FullDateTime pattern;
// the caller decides the conversion ('U' throws for DateTimeOffset).
const char16_t* LookupStandardPattern(char16_t spec, DateCulture culture)
{
    const bool zh = (culture == DateCulture::ZhCn);
    switch (spec)
    {
    case u'd': return zh ? u"yyyy/M/d" : u"MM/dd/yyyy";
    case u'D': return zh ? u"yyyy\u5E74M\u6708d\u65E5" : u"dddd, dd MMMM yyyy";
    case u't': return zh ? u"H:mm" : u"HH:mm";
    case u'T': return zh ? u"H:mm:ss" : u"HH:mm:ss";
    case u'f': return zh ? u"yyyy\u5E74M\u6708d\u65E5 H:mm" : u"dddd, dd MMMM yyyy HH:mm";
    case u'F': return zh ? u"yyyy\u5E74M\u6708d\u65E5 H:mm:ss" : u"dddd, dd MMMM yyyy HH:mm:ss";
    case u'U': return zh ? u"yyyy\u5E74M\u6708d\u65E5 H:mm:ss" : u"dddd, dd MMMM yyyy HH:mm:ss";
    case u'G': return zh ? u"yyyy/M/d H:mm:ss" : u"MM/dd/yyyy HH:mm:ss";
    case u'g': return zh ? u"yyyy/M/d H:mm" : u"MM/dd/yyyy HH:mm";
    case u'm':
    case u'M': return zh ? u"M\u6708d\u65E5" : u"MMMM dd";
    case u'y':
    case u'Y': return zh ? u"yyyy\u5E74M\u6708" : u"yyyy MMMM";
    case u's': return u"yyyy-MM-ddTHH:mm:ss";
    case u'u': return u"yyyy-MM-dd HH:mm:ss'Z'";
    case u'o':
    case u'O': return u"yyyy-MM-ddTHH:mm:ss.fffffff";
    case u'r':
    case u'R': return u"ddd, dd MMM yyyy HH:mm:ss 'GMT'";
    default: return nullptr;
    }
}

void AppendOffsetZzz(std::u16string& out, int32_t offsetMinutes)
{
    AppendOffsetZToken(out, offsetMinutes, 3);
}

// Custom-format engine: token letters (y/M/d/H/h/m/s/f/F/t, plus z/K when
// offsetMinutes is non-null) expand per AppendToken; quoted literal groups
// ('...' / "...", net48 strips the quote characters) and backslash escapes
// ('\x' -> literal x) emit verbatim; '%' makes the next character a single
// custom specifier (net48: "%d" -> day digits, "%z" -> "+8"); every other
// character is emitted literally. Unterminated quotes / a trailing backslash
// are invalid formats (net48 FormatException). 'fmt'/'len' are UTF-16 units.
std::u16string FormatPattern(const CivilTime& t, int32_t dow,
                             const char16_t* fmt, int32_t len,
                             const int32_t* offsetMinutes)
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
        else if (c == u'%')
        {
            // net48 '%' escape: the NEXT character is read as a single
            // custom specifier ("%d" -> day digits; "%z" -> offset hours).
            // A character this engine does not expand keeps the literal '%'
            // (DateTime: the timezone letters stay literals).
            if (i + 1 < len
                && (IsTokenLetter(fmt[i + 1])
                    || (offsetMinutes != nullptr && IsOffsetToken(fmt[i + 1]))))
            {
                AppendToken(out, t, dow, fmt[i + 1], 1, offsetMinutes);
                i += 2;
            }
            else
            {
                out.push_back(c);
                ++i;
            }
        }
        else if (IsTokenLetter(c)
                 || (offsetMinutes != nullptr && IsOffsetToken(c)))
        {
            int32_t run = 1;
            while (i + run < len && fmt[i + run] == c)
            {
                ++run;
            }
            AppendToken(out, t, dow, c, run, offsetMinutes);
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

//------------------------------------------------------------------------------
// System.DateTime -- ToString(format).
//------------------------------------------------------------------------------
Il2CppObject* icall_datetime_to_string_format(Il2CppDateTime dt, Il2CppObject* format,
                                              Il2CppObject* provider)
{
    const Il2CppString* fs = reinterpret_cast<const Il2CppString*>(format);

    // Provider-aware culture selection: the trailing slot carries the IL
    // IFormatProvider when the call site has one (the transpiler passes the
    // culture carrier through; the format-only/parameterless paths pass
    // nullptr). See ResolveProviderCulture for the mapping.
    const DateCulture culture = ResolveProviderCulture(provider);

    const CivilTime t = CivilTimeFromTicks(dt);
    const int32_t dow = WeekdayFromTicks(dt);
    std::u16string out;

    // null, non-string, or empty -> General "G" (net48: empty is treated as
    // "G" too) in the SELECTED culture: host default zh-CN (saudit5) vs
    // invariant for the empty-name carrier (culturedeep/timeformat/datetime2).
    if (fs == nullptr || fs->obj.klass != il2cpp_string_class() || fs->length == 0)
    {
        const char16_t* pattern = LookupStandardPattern(u'G', culture);
        out = FormatPattern(t, dow, pattern,
            static_cast<int32_t>(std::char_traits<char16_t>::length(pattern)), nullptr);
        return reinterpret_cast<Il2CppObject*>(
            il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
    }

    if (fs->length == 1)
    {
        const char16_t c = fs->chars[0];
        const bool isLetter = (c >= u'a' && c <= u'z') || (c >= u'A' && c <= u'Z');
        if (isLetter)
        {
            if (c == u'U')
            {
                // net48 universal full: the LOCAL value converted to UTC
                // (il2cpp_datetime_from_local, host timezone) formatted with
                // the culture FullDateTime pattern.
                const Il2CppDateTime utc = il2cpp_datetime_from_local(dt);
                const char16_t* pattern = LookupStandardPattern(u'U', culture);
                out = FormatPattern(CivilTimeFromTicks(utc), WeekdayFromTicks(utc), pattern,
                    static_cast<int32_t>(std::char_traits<char16_t>::length(pattern)), nullptr);
            }
            else
            {
                const char16_t* pattern = LookupStandardPattern(c, culture);
                if (pattern == nullptr)
                {
                    // net48: an unknown single-letter standard specifier
                    // throws FormatException.
                    il2cpp_throw_format_exception("Format specifier was invalid.");
                }
                out = FormatPattern(t, dow, pattern,
                    static_cast<int32_t>(std::char_traits<char16_t>::length(pattern)), nullptr);
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
        out = FormatPattern(t, dow, fs->chars, fs->length, nullptr);
    }

    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
}

} // namespace runtime
} // namespace il2cpp
