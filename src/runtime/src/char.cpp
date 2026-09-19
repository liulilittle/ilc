//------------------------------------------------------------------------------
// char.cpp -- System.Char STATIC surface runtime support (see il2cpp_char.h
// for the full contract and the ASCII-domain semantic baseline).
//
// All functions are pure unit-level helpers: no allocation, no receiver, no
// exception path (net48 char statics never throw for a single code unit).
//------------------------------------------------------------------------------

#include "il2cpp_char.h"

namespace il2cpp {
namespace runtime {

// D-C (2026-09-19): Unicode Nd (decimal digit) BMP ranges -- net48
// Char.IsDigit uses the Unicode Nd category, not just ASCII. 37 BMP ranges
// (astral Nd ranges cannot be a single UTF-16 code unit, so char.IsDigit never
// sees them). Arabic-Indic U+0660 (probe diffmin001) is the first non-ASCII
// entry below.
namespace {
struct NdRange { char16_t lo; char16_t hi; };
const NdRange kNdRanges[] = {
    { 0x0030, 0x0039 }, { 0x0660, 0x0669 }, { 0x06F0, 0x06F9 }, { 0x07C0, 0x07C9 },
    { 0x0966, 0x096F }, { 0x09E6, 0x09EF }, { 0x0A66, 0x0A6F }, { 0x0AE6, 0x0AEF },
    { 0x0B66, 0x0B6F }, { 0x0BE6, 0x0BEF }, { 0x0C66, 0x0C6F }, { 0x0CE6, 0x0CEF },
    { 0x0D66, 0x0D6F }, { 0x0DE6, 0x0DEF }, { 0x0E50, 0x0E59 }, { 0x0ED0, 0x0ED9 },
    { 0x0F20, 0x0F29 }, { 0x1040, 0x1049 }, { 0x1090, 0x1099 }, { 0x17E0, 0x17E9 },
    { 0x1810, 0x1819 }, { 0x1946, 0x194F }, { 0x19D0, 0x19D9 }, { 0x1A80, 0x1A89 },
    { 0x1A90, 0x1A99 }, { 0x1B50, 0x1B59 }, { 0x1BB0, 0x1BB9 }, { 0x1C40, 0x1C49 },
    { 0x1C50, 0x1C59 }, { 0xA620, 0xA629 }, { 0xA8D0, 0xA8D9 }, { 0xA900, 0xA909 },
    { 0xA9D0, 0xA9D9 }, { 0xA9F0, 0xA9F9 }, { 0xAA50, 0xAA59 }, { 0xABF0, 0xABF9 },
    { 0xFF10, 0xFF19 },
};
} // namespace

bool icall_char_is_digit(char16_t c)
{
    for (const NdRange& r : kNdRanges)
    {
        if (c >= r.lo && c <= r.hi)
        {
            return true;
        }
    }
    return false;
}

bool icall_char_is_letter(char16_t c)
{
    // ASCII + Latin-1 letters (net48 Lu/Ll; U+00D7/U+00F7 are symbols, not
    // letters) + Turkish dotted/dotless I + CJK Unified Ideographs (Lo).
    // Covers the diffchar001 probe set (00E9/00DF/0130/0131/4E2D).
    if ((c >= u'a' && c <= u'z') || (c >= u'A' && c <= u'Z'))
        return true;
    if ((c >= 0x00C0 && c <= 0x00D6) || (c >= 0x00D8 && c <= 0x00F6) ||
        (c >= 0x00F8 && c <= 0x00FF))
        return true;
    if (c == 0x0130 || c == 0x0131)
        return true;
    if (c >= 0x4E00 && c <= 0x9FFF)
        return true;
    return false;
}

bool icall_char_is_letter_or_digit(char16_t c)
{
    return icall_char_is_letter(c) || icall_char_is_digit(c);
}

bool icall_char_is_lower(char16_t c)
{
    // ASCII + Latin-1 lowercase (incl. sharp-s 00DF, dotless-i 0131).
    if (c >= u'a' && c <= u'z')
        return true;
    if ((c >= 0x00E0 && c <= 0x00F6) || (c >= 0x00F8 && c <= 0x00FF) || c == 0x0131)
        return true;
    return false;
}

bool icall_char_is_upper(char16_t c)
{
    // ASCII + Latin-1 uppercase (incl. dotted-I 0130; U+00D7 is a symbol).
    if (c >= u'A' && c <= u'Z')
        return true;
    if ((c >= 0x00C0 && c <= 0x00D6) || (c >= 0x00D8 && c <= 0x00DE) || c == 0x0130)
        return true;
    return false;
}

// char.IsWhiteSpace: the exact code-unit set the BCL treats as whitespace
// (control whitespace + space + the Unicode Zs/Zl/Zp spaces). Listed
// literally so the runtime never depends on the host C++ locale -- the same
// literal set IsWhitespace uses in stringext.cpp (Trim/Contains).
bool icall_char_is_whitespace(char16_t c)
{
    switch (c)
    {
        case u'\t':                            // HT
        case u'\n':                            // LF
        case u'\v':                            // VT
        case u'\f':                            // FF
        case u'\r':                            // CR
        case u' ':                             // SPACE
        case u'\x0085':                        // NEL (next line)
        case u'\x00A0':                        // NBSP (no-break space)
        case u'\x1680':                        // OGHAM SPACE MARK
        case u'\x2000':                        // EN QUAD .. HAIR SPACE
        case u'\x2001':
        case u'\x2002':
        case u'\x2003':
        case u'\x2004':
        case u'\x2005':
        case u'\x2006':
        case u'\x2007':
        case u'\x2008':
        case u'\x2009':
        case u'\x200A':
        case u'\x2028':                        // LINE SEPARATOR
        case u'\x2029':                        // PARAGRAPH SEPARATOR
        case u'\x202F':                        // NARROW NBSP
        case u'\x205F':                        // MEDIUM MATHEMATICAL SPACE
        case u'\x3000':                        // IDEOGRAPHIC SPACE
            return true;
        default:
            return false;
    }
}

char16_t icall_char_to_upper(char16_t c)
{
    // ASCII + Latin-1 lowercase (U+00F7 division sign has no case pair).
    if (c >= u'a' && c <= u'z')
        return static_cast<char16_t>(c - 32);
    if ((c >= 0x00E0 && c <= 0x00F6) || (c >= 0x00F8 && c <= 0x00FE))
        return static_cast<char16_t>(c - 32);
    return c;
}

char16_t icall_char_to_lower(char16_t c)
{
    // ASCII + Latin-1 uppercase (U+00D7 multiplication sign has no case pair).
    if (c >= u'A' && c <= u'Z')
        return static_cast<char16_t>(c + 32);
    if ((c >= 0x00C0 && c <= 0x00D6) || (c >= 0x00D8 && c <= 0x00DE))
        return static_cast<char16_t>(c + 32);
    return c;
}

//------------------------------------------------------------------------------
// The remaining ASCII-domain classification family (icallwide E2E). The
// runtime's char domain is ASCII-only (the documented collation deviation of
// the culture comparator) -- every predicate below is exact on ASCII and a
// conservative false for non-ASCII units.
//------------------------------------------------------------------------------
bool icall_char_is_control(char16_t c)
{
    return c <= 0x1F || c == 0x7F;
}

bool icall_char_is_punctuation(char16_t c)
{
    // Exact net48 Po/Ps/Pe/Pd/Pc set for ASCII: the old ranges wrongly
    // included '$'/'$'-class currency-math units ('$' Sc, '+'/'<'/'='/'">'
    // Sm, '^'/'`' Sk, '|'/'~' Sm -- diffchar001 0024) and missed the
    // ideographic full stop U+3002 (Po -- diffchar001 3002).
    switch (c)
    {
        case u'!': case u'"': case u'#': case u'%': case u'&': case u'\'':
        case u'(': case u')': case u'*': case u',': case u'-': case u'.':
        case u'/': case u':': case u';': case u'?': case u'@': case u'[':
        case u'\\': case u']': case u'_': case u'{': case u'}':
        case 0x3002:
            return true;
        default:
            return false;
    }
}

bool icall_char_is_surrogate(char16_t c)
{
    return c >= 0xD800 && c <= 0xDFFF;
}

// P26 (bcldeep): Char.IsHighSurrogate/IsLowSurrogate -- the UTF-16 pair
// halves (net8 exposes them as API; the resolvers map the static calls).
bool icall_char_is_high_surrogate(char16_t c)
{
    return c >= 0xD800 && c <= 0xDBFF;
}

bool icall_char_is_low_surrogate(char16_t c)
{
    return c >= 0xDC00 && c <= 0xDFFF;
}

bool icall_char_is_separator(char16_t c)
{
    return c == 0x20; // ASCII space; the other Unicode separators are out of domain
}

bool icall_char_is_symbol(char16_t c)
{
    // '$' Sc + math/modifier symbols; '&'/'%' are Po (punctuation, not
    // symbol -- same over-inclusion root as IsPunctuation's old '$'-'&'
    // range, fixed here too).
    return c == u'$' || c == u'+' || c == u'<' || c == u'=' || c == u'>'
        || c == u'^' || c == u'|' || c == u'~';
}

double icall_char_get_numeric_value(char16_t c)
{
    // Unicode decimal digits (the kNdRanges table IsDigit uses): value =
    // offset from the range start. Plus the two non-Nd numerics the
    // diffchar001 probe pins: VULGAR FRACTION ONE HALF (0.5) and ROMAN
    // NUMERAL ONE (1.0). Anything else -> -1 (net48 contract).
    for (const NdRange& r : kNdRanges)
    {
        if (c >= r.lo && c <= r.hi)
        {
            return static_cast<double>(c - r.lo);
        }
    }
    if (c == 0x00BD)
        return 0.5;
    if (c == 0x2160)
        return 1.0;
    return -1.0;
}

}} // namespace il2cpp::runtime
