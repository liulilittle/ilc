//------------------------------------------------------------------------------
// parse_styles.cpp -- IFormatProvider / NumberStyles-aware double parsing
// (see il2cpp_parse_styles.h).
//
// The grammar is a port of the net48 Number.TryParseNumber state machine
// (comnumber.cpp) restricted to the DOUBLE kind: it produces the cleaned
// ASCII literal ("[-]digits[.digits][e[+-]digits]") which strtod then
// converts with correct rounding. The separator characters come from the
// PROVIDER's NumberFormatInfo (cultureinfo.cpp il2cpp_culture_number_info),
// never from a hardcoded ','.
//
// Measured net48 facts this encodes (see tests/cases/parsegroupsep1):
//   * NumberStyles.Float|AllowThousands is the double.Parse(string) default;
//     NumberStyles.Float alone rejects "1,000".
//   * Group separators are skipped WITHOUT grouping validation ("1,,2" -> 12,
//     "1,00" -> 100, "1," -> 1); a leading separator is a FormatException.
//   * The decimal separator is the provider's, so de-DE turns "1.000,5" into
//     1000.5 while "1,000" becomes 1.
//   * The currency symbol is matched before OR after the number, and may be
//     separated from a following sign by whitespace ("-¤ 1.5", "¤-1.5").
//   * The NaN/Infinity words are matched REGARDLESS of the style flags
//     (net48 checks the symbols before digit scanning) and come from the
//     provider (invariant: ASCII words; others: U+221E).
//------------------------------------------------------------------------------

#include "il2cpp_parse_styles.h"

#include "cultureinfo.h"
#include "il2cpp_exception_ext.h"

#include <cerrno>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <string>

namespace il2cpp { namespace runtime {

namespace {

constexpr int32_t kStyleAllowLeadingWhite = 0x0001;
constexpr int32_t kStyleAllowTrailingWhite = 0x0002;
constexpr int32_t kStyleAllowLeadingSign = 0x0004;
constexpr int32_t kStyleAllowTrailingSign = 0x0008;
constexpr int32_t kStyleAllowParentheses = 0x0010;
constexpr int32_t kStyleAllowDecimalPoint = 0x0020;
constexpr int32_t kStyleAllowThousands = 0x0040;
constexpr int32_t kStyleAllowExponent = 0x0080;
constexpr int32_t kStyleAllowCurrencySymbol = 0x0100;
constexpr int32_t kStyleAllowHexSpecifier = 0x0200;
constexpr int32_t kStyleDefinedMask = 0x03FF;

// net48 validates the style BEFORE anything else: undefined bits and the hex
// specifier are ArgumentException for the floating-point parse too (measured:
// double.Parse("1", (NumberStyles)0x203) and 0x400 both throw).
void ValidateStyles(int32_t styles)
{
    if ((styles & ~kStyleDefinedMask) != 0)
    {
        il2cpp_throw_argument_exception(
            "An undefined NumberStyles value is being used.\r\nParameter name: style");
    }
    if ((styles & kStyleAllowHexSpecifier) != 0)
    {
        il2cpp_throw_argument_exception(
            "The number style AllowHexSpecifier is not supported on floating point data types.");
    }
}

bool IsAsciiWs(char16_t c)
{
    return c == u' ' || (c >= u'\t' && c <= u'\r');
}

bool UnitEquals(const char16_t* p, int32_t len, const char16_t* sym, int32_t symLen)
{
    if (len < symLen)
    {
        return false;
    }
    for (int32_t i = 0; i < symLen; ++i)
    {
        if (p[i] != sym[i])
        {
            return false;
        }
    }
    return true;
}

// Match one of the provider's special words, padded by (style-permitted)
// whitespace, against the WHOLE text. Returns 1 + sets *out on a full match.
int32_t MatchSpecial(const char16_t* p, int32_t n, int32_t styles,
                     const Il2CppCultureNumberInfo& ci, double* out)
{
    const int32_t lead = (styles & kStyleAllowLeadingWhite) ? 0 : -1;
    (void)lead;
    int32_t i = 0;
    if ((styles & kStyleAllowLeadingWhite) != 0)
    {
        while (i < n && IsAsciiWs(p[i]))
        {
            ++i;
        }
    }
    bool negative = false;
    int32_t len = n - i;
    const char16_t* q = p + i;
    const char16_t* sym = nullptr;
    int32_t symLen = 0;
    if (UnitEquals(q, len, u"NaN", 3))
    {
        const int32_t rest = i + 3;
        int32_t j = rest;
        if ((styles & kStyleAllowTrailingWhite) != 0)
        {
            while (j < n && IsAsciiWs(p[j]))
            {
                ++j;
            }
        }
        if (j == n)
        {
            *out = std::numeric_limits<double>::quiet_NaN();
            return 1;
        }
        return 0;
    }
    if (ci.asciiInfinityWords)
    {
        if (UnitEquals(q, len, u"-Infinity", 9))
        {
            negative = true;
            sym = q + 1;
            symLen = 8;
        }
        else if (UnitEquals(q, len, u"Infinity", 8))
        {
            sym = q;
            symLen = 8;
        }
    }
    else
    {
        if (UnitEquals(q, len, u"-\u221E", 2))
        {
            negative = true;
            sym = q;
            symLen = 2;
        }
        else if (UnitEquals(q, len, u"\u221E", 1))
        {
            sym = q;
            symLen = 1;
        }
    }
    if (sym == nullptr)
    {
        return 0;
    }
    int32_t j = i + symLen;
    if ((styles & kStyleAllowTrailingWhite) != 0)
    {
        while (j < n && IsAsciiWs(p[j]))
        {
            ++j;
        }
    }
    if (j != n)
    {
        return 0;
    }
    *out = negative ? -std::numeric_limits<double>::infinity()
                    : std::numeric_limits<double>::infinity();
    return 1;
}

enum class Status
{
    Ok,
    Format,
    Overflow,
};

// The net48 Number.TryParseNumber state machine, DOUBLE kind. On success
// 'clean' holds the ASCII literal strtod consumes.
Status ParseStyledDouble(const char16_t* p, int32_t n, int32_t styles,
                         const Il2CppCultureNumberInfo& ci,
                         std::string& clean)
{
    const char16_t* const end = p + n;
    bool negative = false;
    bool afterSign = false;
    bool parens = false;
    bool anyDigits = false;
    bool decimalSeen = false;
    bool currencySeen = false;
    bool currencyPending = (styles & kStyleAllowCurrencySymbol) != 0;

    // --- leading whitespace / sign / parenthesis / currency symbol ---
    for (;;)
    {
        if (p >= end)
        {
            break;
        }
        const char16_t ch = *p;
        if (IsAsciiWs(ch) && (styles & kStyleAllowLeadingWhite) != 0 &&
            !(afterSign && !currencySeen))
        {
            ++p;
            continue;
        }
        if ((styles & kStyleAllowLeadingSign) != 0 && !afterSign && (ch == u'+' || ch == u'-'))
        {
            if (ch == u'-')
            {
                negative = true;
            }
            afterSign = true;
            ++p;
            continue;
        }
        if (ch == u'(' && (styles & kStyleAllowParentheses) != 0 && !afterSign)
        {
            parens = true;
            negative = true;
            afterSign = true;
            ++p;
            continue;
        }
        if (currencyPending && UnitEquals(p, static_cast<int32_t>(end - p),
                                          ci.currencySymbol, ci.currencySymbolLength))
        {
            currencySeen = true;
            currencyPending = false;
            p += ci.currencySymbolLength;
            continue;
        }
        break;
    }

    // --- digits / decimal separator / group separators ---
    for (;;)
    {
        if (p >= end)
        {
            break;
        }
        const char16_t ch = *p;
        if (ch >= u'0' && ch <= u'9')
        {
            anyDigits = true;
            clean.push_back(static_cast<char>(ch));
            ++p;
            continue;
        }
        if ((styles & kStyleAllowThousands) != 0 && anyDigits && !decimalSeen &&
            ci.numberGroupSeparatorLength > 0 &&
            UnitEquals(p, static_cast<int32_t>(end - p), ci.numberGroupSeparator,
                       ci.numberGroupSeparatorLength))
        {
            p += ci.numberGroupSeparatorLength; // validated, NOT copied
            continue;
        }
        if ((styles & kStyleAllowDecimalPoint) != 0 && !decimalSeen &&
            ci.numberDecimalSeparatorLength > 0 &&
            UnitEquals(p, static_cast<int32_t>(end - p), ci.numberDecimalSeparator,
                       ci.numberDecimalSeparatorLength))
        {
            decimalSeen = true;
            clean.push_back('.');
            p += ci.numberDecimalSeparatorLength;
            continue;
        }
        break;
    }
    if (!anyDigits)
    {
        return Status::Format;
    }

    // --- exponent (e/E + optional sign + at least one digit) ---
    if (p < end && (*p == u'e' || *p == u'E') && (styles & kStyleAllowExponent) != 0)
    {
        const char16_t* q = p + 1;
        bool expNeg = false;
        if (q < end && (*q == u'+' || *q == u'-'))
        {
            expNeg = (*q == u'-');
            ++q;
        }
        int32_t expDigits = 0;
        while (q < end && *q >= u'0' && *q <= u'9')
        {
            ++expDigits;
            ++q;
        }
        if (expDigits == 0)
        {
            // "1e" / "1e+": the marker is not consumed, so the trailing
            // check below sees 'e' as junk -> FormatException (net48).
        }
        else
        {
            clean.push_back('e');
            if (expNeg)
            {
                clean.push_back('-');
            }
            // copy the exponent digits verbatim
            const char16_t* d = q - expDigits;
            for (const char16_t* r = d; r < q; ++r)
            {
                clean.push_back(static_cast<char>(*r));
            }
            p = q;
        }
    }

    // --- trailing whitespace / sign / ')' / currency symbol ---
    for (;;)
    {
        if (p >= end)
        {
            break;
        }
        const char16_t ch = *p;
        if (IsAsciiWs(ch) && (styles & kStyleAllowTrailingWhite) != 0)
        {
            ++p;
            continue;
        }
        if ((styles & kStyleAllowTrailingSign) != 0 && !afterSign && (ch == u'+' || ch == u'-'))
        {
            if (ch == u'-')
            {
                negative = true;
            }
            afterSign = true;
            ++p;
            continue;
        }
        if (ch == u')' && parens)
        {
            parens = false;
            ++p;
            continue;
        }
        if (currencyPending && UnitEquals(p, static_cast<int32_t>(end - p),
                                          ci.currencySymbol, ci.currencySymbolLength))
        {
            currencyPending = false;
            p += ci.currencySymbolLength;
            continue;
        }
        break;
    }

    if (p != end || parens)
    {
        return Status::Format;
    }
    if (negative)
    {
        clean.insert(clean.begin(), '-');
    }
    return Status::Ok;
}

Status ConvertClean(const std::string& clean, double* out)
{
    errno = 0;
    char* stop = nullptr;
    const double value = std::strtod(clean.c_str(), &stop);
    if (stop == nullptr || *stop != '\0')
    {
        return Status::Format;
    }
    if (errno == ERANGE && std::isinf(value))
    {
        return Status::Overflow;
    }
    *out = value;
    return Status::Ok;
}

Status ParseDoubleCore(Il2CppString* text, int32_t styles, Il2CppObject* provider, double* out)
{
    Il2CppCultureNumberInfo ci;
    il2cpp_culture_number_info(provider, &ci);
    if (MatchSpecial(text->chars, text->length, styles, ci, out))
    {
        return Status::Ok;
    }
    std::string clean;
    clean.reserve(static_cast<size_t>(text->length) + 1);
    const Status st = ParseStyledDouble(text->chars, text->length, styles, ci, clean);
    if (st != Status::Ok)
    {
        return st;
    }
    return ConvertClean(clean, out);
}

Status ParseDoubleChars(const char16_t* chars, int32_t length, int32_t styles,
                        Il2CppObject* provider, double* out)
{
    Il2CppCultureNumberInfo ci;
    il2cpp_culture_number_info(provider, &ci);
    if (MatchSpecial(chars, length, styles, ci, out))
    {
        return Status::Ok;
    }
    std::string clean;
    clean.reserve(static_cast<size_t>(length) + 1);
    const Status st = ParseStyledDouble(chars, length, styles, ci, clean);
    if (st != Status::Ok)
    {
        return st;
    }
    return ConvertClean(clean, out);
}

} // namespace

int32_t il2cpp_parse_double_default(const char16_t* chars, int32_t length,
                                    Il2CppObject* provider, double* out)
{
    // The net48 double.Parse(string)/TryParse(string) default style:
    // NumberStyles.Float | AllowThousands against the CURRENT culture (a
    // nullptr provider). That is why "1,000" parses to 1000 on an
    // invariant/zh-CN host and "1.000" does on de-DE.
    switch (ParseDoubleChars(chars, length,
                             kStyleAllowLeadingWhite | kStyleAllowTrailingWhite |
                                 kStyleAllowLeadingSign | kStyleAllowTrailingSign |
                                 kStyleAllowDecimalPoint | kStyleAllowThousands |
                                 kStyleAllowExponent,
                             provider, out))
    {
    case Status::Ok:
        return 0;
    case Status::Format:
        return 1;
    case Status::Overflow:
        return 2;
    }
    return 1; // unreachable
}

double icall_string_parse_double_styles(Il2CppString* text, int32_t styles,
                                       Il2CppObject* provider)
{
    ValidateStyles(styles);
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("s");
    }
    double value = 0.0;
    switch (ParseDoubleCore(text, styles, provider, &value))
    {
    case Status::Ok:
        return value;
    case Status::Format:
        il2cpp_throw_format_exception("Input string was not in a correct format.");
        break;
    case Status::Overflow:
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Double.");
        break;
    }
    return 0.0; // unreachable (the throw helpers do not return)
}

int32_t icall_try_parse_double_styles(Il2CppObject* s, int32_t styles,
                                      Il2CppObject* provider, double* result)
{
    ValidateStyles(styles);
    if (s == nullptr || result == nullptr)
    {
        if (result != nullptr)
        {
            *result = 0.0;
        }
        return 0;
    }
    *result = 0.0; // .NET TryParse contract: a failed parse writes 0
    const Il2CppString* text = reinterpret_cast<const Il2CppString*>(s);
    double value = 0.0;
    if (ParseDoubleCore(const_cast<Il2CppString*>(text), styles, provider, &value) != Status::Ok)
    {
        return 0;
    }
    *result = value;
    return 1;
}

}} // namespace il2cpp::runtime


