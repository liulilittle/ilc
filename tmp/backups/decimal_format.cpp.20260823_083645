//------------------------------------------------------------------------------
// decimal_format.cpp -- System.Decimal formatting + parsing (decimal domain
// T2). Algorithm base: dotnet/runtime Decimal.cs + Decimal.DecCalc.cs +
// Number.* (pulled at reference/netcore) -- the digit-extraction primitive
// (DecDivMod1E9) is a direct port of DecCalc.cs; the layouts follow
// Number.FormatDecimal / ParseDecimal with the net48 deviations documented
// in decimal_format.h (fixed-point G, no parse rounding).
//
// The contract is contract-first: T1's decimal.h (representation +
// arithmetic) owns the 16-byte Il2CppDecimal slot; decimal_format.h
// includes decimal.h and this file works against that slot.
//------------------------------------------------------------------------------

#include "decimal_format.h"

#include "il2cpp_exception_ext.h"

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace il2cpp { namespace runtime {
namespace {

constexpr uint32_t kTenToPowerNine = 1000000000u;
constexpr int32_t kMaxScale = 28;          // decimal-domain.md §1.1
constexpr int32_t kMaxSignificantDigits = 29;

// DecDivMod1E9 -- direct port of Decimal.DecCalc.cs (the current netcore
// form, two-step 64-bit division): divides the 96-bit mantissa
// (hi:mid:lo) by 1e9 in place and returns the remainder -- the next 9
// decimal digits (least significant first).
uint32_t DecDivMod1E9(uint32_t& uhi, uint32_t& umid, uint32_t& ulo)
{
    const uint64_t high64 = (static_cast<uint64_t>(uhi) << 32) + umid;
    const uint64_t div64 = high64 / kTenToPowerNine;
    uhi = static_cast<uint32_t>(div64 >> 32);
    umid = static_cast<uint32_t>(div64);

    const uint64_t num = ((high64 - static_cast<uint32_t>(div64) * kTenToPowerNine) << 32) + ulo;
    const uint32_t div = static_cast<uint32_t>(num / kTenToPowerNine);
    ulo = div;
    return static_cast<uint32_t>(num) - div * kTenToPowerNine;
}

// Decimal expansion of the 96-bit mantissa ('0'-'9', no leading zeros; "0"
// for a zero mantissa).
std::string MantissaDigits(uint32_t hi, uint64_t lo64)
{
    if (hi == 0 && lo64 == 0)
    {
        return "0";
    }
    uint32_t ulo = static_cast<uint32_t>(lo64);
    uint32_t umid = static_cast<uint32_t>(lo64 >> 32);
    uint32_t uhi = hi;

    std::vector<uint32_t> groups; // 9-digit groups, least significant first
    while (uhi != 0 || umid != 0 || ulo != 0)
    {
        groups.push_back(DecDivMod1E9(uhi, umid, ulo));
    }

    std::string digits;
    digits.reserve(29);
    char buf[10];
    for (size_t i = groups.size(); i-- > 0;)
    {
        std::snprintf(buf, sizeof buf, "%09u", static_cast<unsigned>(groups[i]));
        if (i + 1 == groups.size())
        {
            const char* p = buf; // strip leading zeros of the top group
            while (*p == '0')
            {
                ++p;
            }
            digits.append(p);
        }
        else
        {
            digits.append(buf, 9);
        }
    }
    return digits;
}

struct DecimalParts
{
    std::string digits; // decimal expansion of the mantissa
    int32_t scale;      // 0..28
    bool negative;      // sign bit
};

DecimalParts ExtractParts(const Il2CppDecimal* value)
{
    DecimalParts p;
    p.digits = MantissaDigits(value->hi32, value->lo64);
    p.scale = (value->flags >> 16) & 0xFF;
    p.negative = (value->flags & 0x80000000) != 0;
    return p;
}

// Keep 'keep' leading digits of 'digits', rounding HALF AWAY FROM ZERO
// (net48 F/N/E rule) on the first discarded digit. On a front carry the
// result grows to 'keep' + 1 digits ("999" keep 1 -> "10").
void RoundAway(std::string& digits, size_t keep)
{
    if (keep >= digits.size())
    {
        return;
    }
    if (digits[keep] < '5')
    {
        digits.resize(keep);
        return;
    }
    size_t i = keep;
    while (i > 0 && digits[i - 1] == '9')
    {
        digits[i - 1] = '0';
        --i;
    }
    if (i == 0)
    {
        digits.assign(keep + 1, '0');
        digits[0] = '1';
    }
    else
    {
        ++digits[i - 1];
        digits.resize(keep);
    }
}

// G default layout (decimal-domain.md §2.3; net48 fixed point, trailing
// zeros preserved, negative sign incl. negative zero).
std::string FormatG(const DecimalParts& p)
{
    std::string out;
    if (p.negative)
    {
        out += '-';
    }
    const size_t len = p.digits.size();
    if (p.scale == 0)
    {
        out += p.digits;
    }
    else if (static_cast<int32_t>(len) > p.scale)
    {
        const size_t ipLen = len - static_cast<size_t>(p.scale);
        out.append(p.digits, 0, ipLen);
        out += '.';
        out.append(p.digits, ipLen, len - ipLen);
    }
    else
    {
        out += "0.";
        out.append(static_cast<size_t>(p.scale) - len, '0');
        out += p.digits;
    }
    return out;
}

// Fixed-point with exactly 'prec' fraction digits, rounding half away from
// zero (shared core of F/N/C). Returns (integer part, fraction part).
void FormatFixedParts(const DecimalParts& p, int prec, std::string& intPart,
                      std::string& frac)
{
    std::string digits = p.digits;
    const int drop = p.scale - prec; // trailing mantissa digits discarded
    if (drop > 0)
    {
        if (static_cast<int>(digits.size()) > drop)
        {
            RoundAway(digits, digits.size() - static_cast<size_t>(drop));
        }
        else
        {
            // value * 10^prec < 1: rounds to 0 or 1 (half away from zero).
            // mantissa >= 5 * 10^(drop-1) rounds up.
            const std::string half = "5" + std::string(static_cast<size_t>(drop) - 1, '0');
            const std::string padded = std::string(half.size() - digits.size(), '0') + digits;
            digits = padded >= half ? "1" : "";
        }
    }
    else
    {
        digits.append(static_cast<size_t>(-drop), '0'); // prec >= scale: pad
    }

    if (digits.size() <= static_cast<size_t>(prec))
    {
        intPart = "0";
        frac.assign(static_cast<size_t>(prec) - digits.size(), '0');
        frac += digits;
    }
    else
    {
        const size_t ipLen = digits.size() - static_cast<size_t>(prec);
        intPart.assign(digits, 0, ipLen);
        frac.assign(digits, ipLen, static_cast<size_t>(prec));
    }
}

// Thousands grouping of the integer part (net48 NumberGroupSizes [3]).
std::string Group3(const std::string& intPart)
{
    std::string out;
    const size_t len = intPart.size();
    for (size_t i = 0; i < len; ++i)
    {
        if (i > 0 && (len - i) % 3 == 0)
        {
            out += ',';
        }
        out += intPart[i];
    }
    return out;
}

std::string FormatF(const DecimalParts& p, int prec)
{
    std::string intPart, frac;
    FormatFixedParts(p, prec, intPart, frac);
    std::string out;
    if (p.negative)
    {
        out += '-';
    }
    out += intPart;
    if (prec > 0)
    {
        out += '.';
        out += frac;
    }
    return out;
}

std::string FormatN(const DecimalParts& p, int prec)
{
    std::string intPart, frac;
    FormatFixedParts(p, prec, intPart, frac);
    std::string out;
    if (p.negative)
    {
        out += '-';
    }
    out += Group3(intPart);
    if (prec > 0)
    {
        out += '.';
        out += frac;
    }
    return out;
}

// C body: sign + grouped integer + fraction (no symbol -- the fixed '¥'
// is prepended at the UTF-16 level; il2cpp_string_new_from_utf8 drops
// multi-byte sequences, formatext.cpp MakeCurrencyString precedent).
std::string FormatCTail(const DecimalParts& p, int prec)
{
    std::string intPart, frac;
    FormatFixedParts(p, prec, intPart, frac);
    std::string out;
    if (p.negative)
    {
        out += '-';
    }
    out += Group3(intPart);
    if (prec > 0)
    {
        out += '.';
        out += frac;
    }
    return out;
}

// E: scientific "d.dddE+xxx" -- mantissa digits with EXACTLY 'prec' fraction
// digits (short mantissas are zero-padded, net48: 2.5m E2 -> "2.50E+000"),
// exponent letter in the specifier's case (net48: {0:e2} -> "1.23e+003"),
// exponent sign + at least 3 digits.
std::string FormatE(const DecimalParts& p, int prec, char letter)
{
    std::string digits = p.digits;
    int exp = (digits == "0") ? 0
                              : static_cast<int>(digits.size()) - p.scale - 1;
    if (prec + 1 < static_cast<int>(digits.size()))
    {
        RoundAway(digits, static_cast<size_t>(prec) + 1);
        if (static_cast<int>(digits.size()) > prec + 1)
        {
            ++exp; // front carry ("999" -> "10")
        }
    }

    std::string out;
    if (p.negative)
    {
        out += '-';
    }
    out += digits[0];
    if (prec > 0)
    {
        out += '.';
        const size_t have = digits.size() - 1; // mantissa digits after the point
        const size_t take = have < static_cast<size_t>(prec) ? have
                                                             : static_cast<size_t>(prec);
        if (take > 0)
        {
            out.append(digits, 1, take);
        }
        out.append(static_cast<size_t>(prec) - take, '0'); // pad to 'prec'
    }
    out += letter;
    if (exp < 0)
    {
        out += '-';
        exp = -exp;
    }
    else
    {
        out += '+';
    }
    char ebuf[12];
    std::snprintf(ebuf, sizeof ebuf, "%03d", exp);
    out += ebuf;
    return out;
}

// P: percent -- value x 100, N-style grouped integer + 'prec' fraction
// digits, then a trailing '%' (zh-CN "n%" layout, no space; net48 default
// precision 2: 1234.5678m "P" -> "123,456.78%").
std::string FormatP(const DecimalParts& p, int prec)
{
    DecimalParts scaled = p;
    const int32_t ns = p.scale - 2;
    if (ns < 0)
    {
        scaled.digits.append(static_cast<size_t>(-ns), '0');
        scaled.scale = 0;
    }
    else
    {
        scaled.scale = ns;
    }
    // zero x 100 stays zero: the scale drop can turn "0" into "000", which
    // N-layout would render as "000%" instead of net48's "0%".
    const size_t nz = scaled.digits.find_first_not_of('0');
    if (nz == std::string::npos)
    {
        scaled.digits = "0";
    }
    else if (nz > 0)
    {
        scaled.digits.erase(0, nz);
    }
    std::string intPart, frac;
    FormatFixedParts(scaled, prec, intPart, frac);
    std::string out;
    // net48: a zero percent value drops the negative sign (-0m "P2" -> "0.00%").
    if (scaled.negative && scaled.digits != "0")
    {
        out += '-';
    }
    out += Group3(intPart);
    if (prec > 0)
    {
        out += '.';
        out += frac;
    }
    out += '%';
    return out;
}

//------------------------------------------------------------------------------
// CUSTOM numeric format engine (net48 mscorlib System.Number.NumberToStringFormat
// / clr classlibnative number.cpp port). A non-standard format string is a
// user-defined pattern: '0' zero-fill digit, '#' optional digit, '.' decimal
// point, ',' group separator / 1000-scale, '%'/'‰' percent scaling, E+/E-/E0
// scientific, ';' sections, quotes and '\' literals. The port follows the
// net48 native algorithm step by step (NumberBuffer: digits + scale + sign),
// so decimal custom formats match the zh-CN net48 oracle byte for byte.
//------------------------------------------------------------------------------

// FindSection: start index of the requested section (0 = positive/whole,
// 1 = negative, 2 = zero); a missing or empty section falls back to 0 (the
// positive section) exactly like net48.
int FindSectionFormat(const char16_t* format, int section)
{
    if (section == 0)
    {
        return 0;
    }
    int src = 0;
    for (;;)
    {
        const char16_t ch = format[src++];
        switch (ch)
        {
        case u'\'':
        case u'"':
            while (format[src] != 0 && format[src++] != ch)
            {
            }
            break;
        case u'\\':
            if (format[src] != 0)
            {
                ++src;
            }
            break;
        case u';':
            if (--section != 0)
            {
                break;
            }
            if (format[src] != 0 && format[src] != u';')
            {
                return src;
            }
            // empty section -> treat as if it did not exist (use positive)
            return 0;
        case 0:
            return 0;
        default:
            break;
        }
    }
}

// The NumberBuffer for custom formatting: digits ('' = zero) + scale
// (digitCount - decimalScale) + sign. RoundNumber mirrors net48 exactly
// (half AWAY from zero, front-carry bumps scale, rounding to zero clears
// sign+scale).
struct CustomNum
{
    std::string digits;
    int scale;
    bool sign;
};

void RoundNumberCustom(CustomNum& n, int pos)
{
    size_t i = 0;
    while (static_cast<int>(i) < pos && i < n.digits.size())
    {
        ++i;
    }
    if (static_cast<int>(i) == pos && i < n.digits.size() && n.digits[i] >= '5')
    {
        while (i > 0 && n.digits[i - 1] == '9')
        {
            --i;
        }
        if (i > 0)
        {
            ++n.digits[i - 1];
        }
        else
        {
            ++n.scale;
            n.digits.assign(1, '1');
            i = 1;
        }
    }
    else
    {
        while (i > 0 && n.digits[i - 1] == '0')
        {
            --i;
        }
    }
    if (i == 0)
    {
        n.scale = 0;
        n.sign = false;
    }
    n.digits.resize(i);
}

// Render one custom-format section (net48 NumberToStringFormat render loop).
// Returns the UTF-16 result; literals, separators and symbols are the zh-CN
// fixed culture ('.' ',' '-' '+' '%' U+2030) -- the project's net48 baseline.
std::u16string FormatCustom(const DecimalParts& p, const char16_t* format)
{
    CustomNum n;
    n.digits = p.digits;
    if (n.digits == "0") // zero mantissa == empty digit buffer (net48)
    {
        n.digits.clear();
    }
    n.scale = static_cast<int>(p.digits.size()) - p.scale;
    n.sign = p.negative;

    int section = FindSectionFormat(
        format, n.digits.empty() ? 2 : (n.sign ? 1 : 0));

ParseSection:
    int digitCount = 0;
    int decimalPos = -1;
    int firstDigit = 0x7FFFFFFF;
    int lastDigit = 0;
    bool scientific = false;
    int thousandPos = -1;
    int thousandCount = 0;
    bool thousandSeps = false;
    int scaleAdjust = 0;
    int src = section;
    for (;;)
    {
        const char16_t ch = format[src++];
        if (ch == 0 || ch == u';')
        {
            break;
        }
        switch (ch)
        {
        case u'#':
            ++digitCount;
            break;
        case u'0':
            if (firstDigit == 0x7FFFFFFF)
            {
                firstDigit = digitCount;
            }
            ++digitCount;
            lastDigit = digitCount;
            break;
        case u'.':
            if (decimalPos < 0)
            {
                decimalPos = digitCount;
            }
            break;
        case u',':
            if (digitCount > 0 && decimalPos < 0)
            {
                if (thousandPos >= 0)
                {
                    if (thousandPos == digitCount)
                    {
                        ++thousandCount;
                        break;
                    }
                    thousandSeps = true;
                }
                thousandPos = digitCount;
                thousandCount = 1;
            }
            break;
        case u'%':
            scaleAdjust += 2;
            break;
        case 0x2030:
            scaleAdjust += 3;
            break;
        case u'\'':
        case u'"':
            while (format[src] != 0 && format[src++] != ch)
            {
            }
            break;
        case u'\\':
            if (format[src] != 0)
            {
                ++src;
            }
            break;
        case u'E':
        case u'e':
            if (format[src] == u'0' ||
                ((format[src] == u'+' || format[src] == u'-') &&
                 format[src + 1] == u'0'))
            {
                while (format[++src] == u'0')
                {
                }
                scientific = true;
            }
            break;
        default:
            break;
        }
    }

    if (decimalPos < 0)
    {
        decimalPos = digitCount;
    }
    if (thousandPos >= 0)
    {
        if (thousandPos == decimalPos)
        {
            scaleAdjust -= thousandCount * 3; // ',' right of the point = /1000
        }
        else
        {
            thousandSeps = true;
        }
    }

    if (!n.digits.empty())
    {
        n.scale += scaleAdjust;
        const int pos = scientific ? digitCount : n.scale + digitCount - decimalPos;
        RoundNumberCustom(n, pos);
        if (n.digits.empty())
        {
            const int ns = FindSectionFormat(format, 2);
            if (ns != section)
            {
                section = ns;
                goto ParseSection;
            }
        }
    }
    else
    {
        n.sign = false; // -0 formats as 0 (net48 decimal rule)
        n.scale = 0;
    }

    firstDigit = firstDigit < decimalPos ? decimalPos - firstDigit : 0;
    lastDigit = lastDigit > decimalPos ? decimalPos - lastDigit : 0;
    int digPos;
    int adjust;
    if (scientific)
    {
        digPos = decimalPos;
        adjust = 0;
    }
    else
    {
        digPos = n.scale > decimalPos ? n.scale : decimalPos;
        adjust = n.scale - decimalPos;
    }

    // Precompute the thousands-separator insertion points (net48 logic with
    // NumberGroupSizes [3] for the zh-CN culture; group size is a compile-time
    // constant, never 0, so the loop always terminates).
    std::vector<int> thousandsSepPos;
    int thousandsSepCtr = -1;
    if (thousandSeps)
    {
        long long groupTotalSizeCount = 0;
        const int groupSize = 3;
        const int totalDigits = digPos + (adjust < 0 ? adjust : 0);
        const int numDigits = firstDigit > totalDigits ? firstDigit : totalDigits;
        while (numDigits > groupTotalSizeCount)
        {
            thousandsSepPos.push_back(static_cast<int>(groupTotalSizeCount));
            groupTotalSizeCount += groupSize;
        }
        thousandsSepCtr = static_cast<int>(thousandsSepPos.size()) - 1;
    }

    std::u16string out;
    if (n.sign && section == 0)
    {
        out += u'-';
    }

    bool decimalWritten = false;
    size_t dpos = 0; // index into n.digits (out of range == net48 '\0')
    src = section;
    for (;;)
    {
        const char16_t ch = format[src++];
        if (ch == 0 || ch == u';')
        {
            break;
        }

        if (adjust > 0)
        {
            switch (ch)
            {
            case u'#':
            case u'0':
            case u'.':
                while (adjust > 0)
                {
                    out += dpos < n.digits.size() ? n.digits[dpos++] : u'0';
                    if (thousandSeps && digPos > 1 && thousandsSepCtr >= 0 &&
                        digPos == thousandsSepPos[thousandsSepCtr] + 1)
                    {
                        out += u',';
                        --thousandsSepCtr;
                    }
                    --digPos;
                    --adjust;
                }
                break;
            default:
                break;
            }
        }

        switch (ch)
        {
        case u'#':
        case u'0':
        {
            char16_t dc;
            if (adjust < 0)
            {
                ++adjust;
                dc = digPos <= firstDigit ? u'0' : 0;
            }
            else
            {
                dc = dpos < n.digits.size()
                         ? n.digits[dpos++]
                         : (digPos > lastDigit ? u'0' : 0);
            }
            if (dc != 0)
            {
                out += dc;
                if (thousandSeps && digPos > 1 && thousandsSepCtr >= 0 &&
                    digPos == thousandsSepPos[thousandsSepCtr] + 1)
                {
                    out += u',';
                    --thousandsSepCtr;
                }
            }
            --digPos;
            break;
        }
        case u'.':
            if (digPos != 0 || decimalWritten)
            {
                break; // extra '.' is a literal no-op (net48 compat)
            }
            if (lastDigit < 0 || (decimalPos < digitCount && dpos < n.digits.size()))
            {
                out += u'.';
                decimalWritten = true;
            }
            break;
        case 0x2030:
            out += 0x2030;
            break;
        case u'%':
            out += u'%';
            break;
        case u',':
            break; // separators are emitted via thousandsSepPos only
        case u'\'':
        case u'"':
            while (format[src] != 0 && format[src] != ch)
            {
                out += format[src++];
            }
            if (format[src] != 0)
            {
                ++src;
            }
            break;
        case u'\\':
            if (format[src] != 0)
            {
                out += format[src++];
            }
            break;
        case u'E':
        case u'e':
        {
            bool positiveSign = false;
            int i = 0;
            if (scientific)
            {
                if (format[src] == u'0')
                {
                    ++i; // E0 behaves like E-0 (no sign for positive exp)
                }
                else if (format[src] == u'+' && format[src + 1] == u'0')
                {
                    positiveSign = true;
                }
                else if (format[src] == u'-' && format[src + 1] == u'0')
                {
                    // E-0: sign only for negative exponents
                }
                else
                {
                    out += ch; // not an exponent marker after all
                    break;
                }
                while (format[++src] == u'0')
                {
                    ++i;
                }
                if (i > 10)
                {
                    i = 10;
                }
                int exp = n.digits.empty() ? 0 : n.scale - decimalPos;
                out += ch;
                if (exp < 0)
                {
                    out += u'-';
                    exp = -exp;
                }
                else if (positiveSign)
                {
                    out += u'+';
                }
                std::string ebuf;
                if (exp == 0)
                {
                    ebuf = "0";
                }
                else
                {
                    while (exp > 0)
                    {
                        ebuf.insert(ebuf.begin(), static_cast<char>('0' + exp % 10));
                        exp /= 10;
                    }
                }
                while (static_cast<int>(ebuf.size()) < i)
                {
                    ebuf.insert(ebuf.begin(), '0');
                }
                for (size_t k = 0; k < ebuf.size(); ++k)
                {
                    out += static_cast<char16_t>(ebuf[k]);
                }
                scientific = false;
            }
            else
            {
                out += ch; // literal E/e (not an exponent marker)
                if (format[src] == u'+' || format[src] == u'-')
                {
                    out += format[src++];
                }
                while (format[src] == u'0')
                {
                    out += format[src++];
                }
            }
            break;
        }
        default:
            out += ch;
            break;
        }
    }

    return out;
}

// Strict format-specifier parse: one ASCII letter (case-insensitive) +
// optional 0..99 precision digits; anything else -> false (FormatException).
struct FormatSpec
{
    char letter;    // specifier letter, uppercased
    char rawLetter; // original ASCII case (E output keeps the specifier case)
    int precision;  // -1 = none
};

bool TryParseFormat(Il2CppString* format, FormatSpec& spec)
{
    if (format == nullptr || format->length == 0)
    {
        spec.letter = 'G';
        spec.rawLetter = 'G';
        spec.precision = -1;
        return true;
    }
    const char16_t c0 = format->chars[0];
    if (c0 < 'A' || c0 > 'Z')
    {
        if (c0 < 'a' || c0 > 'z')
        {
            return false;
        }
        spec.letter = static_cast<char>(c0 - ('a' - 'A'));
    }
    else
    {
        spec.letter = static_cast<char>(c0);
    }
    spec.rawLetter = static_cast<char>(c0);

    int prec = 0;
    bool any = false;
    for (int32_t i = 1; i < format->length; ++i)
    {
        const char16_t d = format->chars[i];
        if (d < '0' || d > '9')
        {
            return false; // strict: no trailing junk
        }
        prec = prec * 10 + (d - '0');
        if (prec > 99)
        {
            return false; // precision out of range
        }
        any = true;
    }
    spec.precision = any ? prec : -1;
    return true;
}

int DefaultPrec(const FormatSpec& spec, int fallback)
{
    return spec.precision >= 0 ? spec.precision : fallback;
}

Il2CppString* ToManaged(const std::string& ascii)
{
    return il2cpp_string_new_from_utf8(ascii.c_str());
}

// Shared parse core (net48 semantics -- never rounds). Writes 'out' only on
// Ok (verify-then-mutate).
enum class ParseStatus
{
    Ok,
    Format,
    Overflow,
};

bool IsAsciiWs(char16_t c)
{
    return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f';
}

// (hi32 : lo64) = (hi32 : lo64) * 10 + d; false when the result no longer
// fits 96 bits (net48 OverflowException path).
bool MulAdd96(uint32_t& hi32, uint64_t& lo64, uint32_t d)
{
    const uint32_t aLo = static_cast<uint32_t>(lo64);
    const uint32_t aHi = static_cast<uint32_t>(lo64 >> 32);
    const uint64_t t = static_cast<uint64_t>(aLo) * 10 + d;      // < 2^36
    const uint64_t mid = static_cast<uint64_t>(aHi) * 10 + (t >> 32); // < 2^36
    lo64 = ((mid & 0xFFFFFFFFull) << 32) | (t & 0xFFFFFFFFull);
    const uint64_t hi = static_cast<uint64_t>(hi32) * 10 + (mid >> 32); // < 2^36
    if (hi > 0xFFFFFFFFull)
    {
        return false;
    }
    hi32 = static_cast<uint32_t>(hi);
    return true;
}

ParseStatus ParseCore(Il2CppString* text, Il2CppDecimal* out)
{
    const char16_t* p = text->chars;
    int32_t n = text->length;

    while (n > 0 && IsAsciiWs(p[0])) // trim leading whitespace
    {
        ++p;
        --n;
    }
    while (n > 0 && IsAsciiWs(p[n - 1])) // trim trailing whitespace
    {
        --n;
    }
    if (n == 0)
    {
        return ParseStatus::Format; // net48: empty -> FormatException
    }

    bool negative = false;
    size_t i = 0;
    if (p[0] == '-' || p[0] == '+')
    {
        negative = p[0] == '-';
        ++i;
        if (i == static_cast<size_t>(n))
        {
            return ParseStatus::Format;
        }
    }

    bool sawDigit = false;
    bool sawNonZero = false;
    bool sawPoint = false;
    int32_t scale = 0;
    int32_t significant = 0;
    uint32_t hi32 = 0;
    uint64_t lo64 = 0;

    for (; i < static_cast<size_t>(n); ++i)
    {
        const char16_t c = p[i];
        if (c >= '0' && c <= '9')
        {
            sawDigit = true;
            if (sawPoint)
            {
                ++scale;
                if (scale > kMaxScale)
                {
                    return ParseStatus::Overflow;
                }
            }
            if (c != '0')
            {
                sawNonZero = true;
            }
            if (sawNonZero)
            {
                ++significant;
                if (significant > kMaxSignificantDigits)
                {
                    return ParseStatus::Overflow;
                }
                if (!MulAdd96(hi32, lo64, static_cast<uint32_t>(c - '0')))
                {
                    return ParseStatus::Overflow; // mantissa > 2^96 - 1
                }
            }
        }
        else if (c == '.')
        {
            if (sawPoint)
            {
                return ParseStatus::Format;
            }
            sawPoint = true;
        }
        else if (c == ',')
        {
            // thousands separator (NumberStyles.Number AllowThousands):
            // tolerated only between digits
            if (!sawDigit || i + 1 >= static_cast<size_t>(n) ||
                p[i + 1] < '0' || p[i + 1] > '9')
            {
                return ParseStatus::Format;
            }
        }
        else
        {
            return ParseStatus::Format;
        }
    }

    if (!sawDigit)
    {
        return ParseStatus::Format;
    }

    out->hi32 = hi32;
    out->lo64 = lo64;
    out->flags = (negative ? 0x80000000 : 0) | (static_cast<int32_t>(scale) << 16);
    return ParseStatus::Ok;
}

} // namespace

Il2CppString* icall_decimal_to_string(const Il2CppDecimal* value)
{
    const DecimalParts p = ExtractParts(value);
    return ToManaged(FormatG(p));
}

Il2CppString* icall_decimal_to_string_custom(const Il2CppDecimal* value,
                                             Il2CppString* format)
{
    FormatSpec spec;
    if (!TryParseFormat(format, spec))
    {
        // net48: a non-standard multi-char pattern is a CUSTOM format string
        // ('0'/'#' digit placeholders, '.' ',' '%' 'E+' literals, ';'
        // sections, quotes/escapes). Ported from the net48 native
        // NumberToStringFormat (see FormatCustom above). Single unknown
        // letters (D/X/Q) still parse as standard specs and throw in the
        // switch below.
        const DecimalParts p = ExtractParts(value);
        // FormatCustom walks the pattern as a null-terminated UTF-16 buffer
        // (net48 native style); the managed payload is not guaranteed to carry
        // a terminator, so take a local copy that does.
        std::u16string pattern;
        if (format != nullptr)
        {
            pattern.assign(format->chars, format->length);
        }
        std::u16string result = FormatCustom(p, pattern.c_str());
        return il2cpp_string_new(result.data(),
                                 static_cast<int32_t>(result.size()));
    }

    const DecimalParts p = ExtractParts(value);
    switch (spec.letter)
    {
    case 'G':
        return ToManaged(FormatG(p));
    case 'F':
        return ToManaged(FormatF(p, DefaultPrec(spec, 2)));
    case 'N':
        return ToManaged(FormatN(p, DefaultPrec(spec, 2)));
    case 'E':
        return ToManaged(FormatE(p, DefaultPrec(spec, 6), spec.rawLetter));
    case 'P':
        return ToManaged(FormatP(p, DefaultPrec(spec, 2)));
    case 'C':
    {
        // fixed '¥' symbol + ASCII tail, composed at the UTF-16 level
        const std::string tail = FormatCTail(p, DefaultPrec(spec, 2));
        std::vector<char16_t> units;
        units.reserve(tail.size() + 1);
        units.push_back(u'\u00A5');
        for (size_t i = 0; i < tail.size(); ++i)
        {
            units.push_back(static_cast<char16_t>(static_cast<unsigned char>(tail[i])));
        }
        return il2cpp_string_new(units.data(), static_cast<int32_t>(units.size()));
    }
    default:
        il2cpp_throw_format_exception("Format specifier was invalid.");
        return nullptr; // unreachable; the throw factories are not noreturn
    }
}

void icall_decimal_parse(Il2CppDecimal* result, Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("s");
    }
    switch (ParseCore(text, result))
    {
    case ParseStatus::Ok:
        return;
    case ParseStatus::Format:
        il2cpp_throw_format_exception(nullptr);
        [[fallthrough]];
    case ParseStatus::Overflow:
        il2cpp_throw_overflow_exception(nullptr);
    }
}

bool icall_decimal_try_parse(Il2CppString* text, Il2CppDecimal* result)
{
    if (text == nullptr)
    {
        return false;
    }
    return ParseCore(text, result) == ParseStatus::Ok;
}

}} // namespace il2cpp::runtime
