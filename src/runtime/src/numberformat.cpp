//------------------------------------------------------------------------------
// numberformat.cpp -- invariant-culture numeric formatting (numberformat.h).
//
// Implementation notes:
//   * Integer G/N/X/E paths use the manual digit-loop style of
//     format.cpp/formatext.cpp (deterministic, locale-independent, GCC 7.5
//     compatible; the existing helpers are file-local so the loops are
//     re-implemented here -- reuse of the idea, not the symbols).
//   * Double F/N/E/P paths: the digits come from snprintf("%.14e") -- the
//     CRT's 15-significant-digit correctly-rounded decimal conversion, the
//     SAME base .NET Framework 4.8 produces (msvcrt-class converters).
//     .NET's round-half-AWAY-FROM-ZERO rule is then applied on those 15
//     digits and exactly <precision> fraction digits are re-emitted. The
//     15-digit base is what reproduces the net48 oracle on near-tie values
//     (2.675 N2 -> "2.68", 1.23499999 N2 -> "1.23", 0.15 N1 -> "0.2") --
//     see docs/audit/number-conformance.md. The base has no "guard region"
//     limitation: rounding happens on the printed digits only.
//   * Grouping (N/P) and the percent decoration are applied on the already-
//     rounded digit string -- no further floating point. P still scales the
//     VALUE by 100.0 first (net48's double P path multiplies in double, so
//     the double rounding of the product is part of the observable output).
//   * int64 E uses the EXACT decimal digits of the integer (no double
//     round-trip: int64 exceeds double's 2^53 exact-integer range).
//   * "G" for double reuses the existing G15 icall (il2cpp_format.h).
//   * CULTURE: FIXED INVARIANT separators/layout (',' / '.' / "n %") with
//     the zh-CN special spellings "NaN"/U+221E/-U+221E (U+221E -- the verification
//     host's net48 DEFAULT culture, aligned with format.cpp/formatext.cpp);
//     see the header.
//------------------------------------------------------------------------------

#include "numberformat.h"
#include "il2cpp_format.h" // icall_double_to_string (G15 path)
#include "il2cpp_exception_ext.h" // il2cpp_throw_format_exception
#include "il2cpp_array.h" // il2cpp_array_new (group sizes / native digits)
#include "il2cpp_gc.h" // il2cpp_gc_add/remove_static_root (native digits GC safety)

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

// Largest formatted value: fixed-point double with 99 fraction digits plus
// guard digits (309 integer digits + '.' + 103 + NUL); the percent path
// appends " %" on top (buffer + 8).
const int kMaxBuffer = 512;

// net48's double conversion base: 15 significant digits, correctly rounded
// (msvcrt-class CRT converters; snprintf("%.14e") is exact at this
// precision). All F/N/E/P rounding happens on these digits -- see the
// file header and docs/audit/number-conformance.md.
const int kBaseDigits = 15;

// The 15-digit base of a finite double (value == d[0].d[1].. x 10^exp10).
struct DoubleDigits
{
    char d[kBaseDigits]; // correctly rounded significant digits
    int  exp10;          // decimal exponent of d[0]
    bool neg;
};

void GetDoubleDigits(double value, DoubleDigits& out)
{
    char tmp[32];
    std::snprintf(tmp, sizeof tmp, "%.14e", value);
    out.neg = (tmp[0] == '-');
    const char* p = tmp + (out.neg ? 1 : 0);
    out.d[0] = *p++;
    ++p; // skip '.'
    for (int i = 1; i < kBaseDigits; ++i)
    {
        out.d[i] = *p++;
    }
    out.exp10 = std::atoi(p + 1); // "e+XX"/"e-XX"
}

// Decimal digits of 'value' (no sign for non-negative) into 'out' (24 chars).
// Returns the character count; NOT NUL-terminated. INT64_MIN is negated in
// unsigned arithmetic (no UB) -- same contract as format.cpp's helper.
int FormatInt64Decimal(char* out, int64_t value)
{
    if (value == 0)
    {
        out[0] = '0';
        return 1;
    }
    const bool negative = value < 0;
    uint64_t magnitude = negative
        ? (static_cast<uint64_t>(-(value + 1)) + 1ULL)
        : static_cast<uint64_t>(value);
    char tmp[21];
    int n = 0;
    do
    {
        tmp[n++] = static_cast<char>('0' + static_cast<int>(magnitude % 10ULL));
        magnitude /= 10ULL;
    } while (magnitude != 0ULL);
    int len = 0;
    if (negative)
    {
        out[len++] = '-';
    }
    while (n > 0)
    {
        out[len++] = tmp[--n];
    }
    return len;
}

bool IsAllZero(const char* s, int n)
{
    for (int i = 0; i < n; ++i)
    {
        if (s[i] != '0')
        {
            return false;
        }
    }
    return true;
}

// .NET formatting rounds half AWAY FROM ZERO. 'frac' holds
// (fractionDigits + kGuardDigits) digits; rounds it to exactly 'keep' digits
// (NUL-terminated at 'keep'). Returns true when the carry propagated out of
// the fraction (the integer part must then be incremented).
// Note: a first dropped digit of '5' ALWAYS rounds up -- a tie rounds away
// from zero, and a nonzero tail means the value is above the tie.
bool RoundFraction(char* frac, int keep)
{
    if (frac[keep] < '5')
    {
        frac[keep] = '\0';
        return false;
    }
    int i = keep - 1;
    while (i >= 0 && frac[i] == '9')
    {
        frac[i] = '0';
        --i;
    }
    frac[keep] = '\0';
    if (i < 0)
    {
        return true;
    }
    ++frac[i];
    return false;
}

// Increment an in-place decimal digit string ("123" -> "124", "999" -> "1000").
// Returns the new length.
int IncrementDigits(char* d, int len)
{
    int i = len - 1;
    while (i >= 0 && d[i] == '9')
    {
        d[i] = '0';
        --i;
    }
    if (i < 0)
    {
        std::memmove(d + 1, d, static_cast<size_t>(len));
        d[0] = '1';
        return len + 1;
    }
    ++d[i];
    return len;
}

// The zh-CN positive-infinity symbol: U+221E (infinity) (the verification host's
// net48 DEFAULT culture -- zh-CN -- renders infinities as this symbol; the
// same spelling as format.cpp's G15 and formatext.cpp's MakeSpecialString).
const char16_t kInfinitySymbol = u'\u221E';

// Specials spellings: "NaN"/U+221E/-U+221E (zh-CN NumberFormatInfo, net48-
// verified). Writes NUL-terminated ASCII/UTF-8 text into 'out'; returns the
// length, or 0 for a finite value. The infinity text uses the UTF-8 bytes
// E2 88 9E -- the char-buffer callers never emit it (the exported helpers
// intercept specials via MakeSpecialString, which builds UTF-16 units
// because il2cpp_string_new_from_cs skips the >= 0x80 bytes).
int FormatDoubleSpecials(double value, char* out)
{
    if (std::isnan(value))
    {
        std::memcpy(out, "NaN", 4); // includes NUL
        return 3;
    }
    if (std::isinf(value))
    {
        const char* inf = std::signbit(value) ? "-\xE2\x88\x9E" : "\xE2\x88\x9E";
        std::memcpy(out, inf, std::strlen(inf) + 1); // includes NUL
        return static_cast<int>(std::strlen(inf));
    }
    return 0;
}

// Format a finite double in fixed-point with exactly 'fracDigits' decimals.
// Writes into 'out' (kMaxBuffer); returns the length; NOT NUL-terminated.
// The sign is omitted when the rounded magnitude is zero (-0.0 -> "0.00",
// net48-verified); the '.' is omitted when fracDigits == 0. Rounding is
// half AWAY FROM ZERO on the 15-digit base (see the file header).
int FormatFixed(double value, int fracDigits, char* out)
{
    DoubleDigits dg;
    GetDoubleDigits(value, dg);

    // Integer part: d[k] sits at 10^(exp10 - k); the digits with
    // exp10 - k >= 0 are d[0..exp10], in order ("1234..."). A negative
    // exp10 means no integer digits ("0").
    const int intLen = dg.exp10 + 1;
    char intBuf[kMaxBuffer];
    int intN = 0;
    if (intLen > 0)
    {
        for (int i = 0; i < intLen; ++i)
        {
            intBuf[intN++] = (i < kBaseDigits) ? dg.d[i] : '0';
        }
    }

    // Fraction digits: the 1-based fraction position j holds digit
    // d[exp10 + j] (out of [0, 14] -> zero: leading zeros or beyond the
    // base -- the base-limited digits match net48's own base).
    char frac[kMaxBuffer];
    int f = 0;
    for (int i = 0; i <= fracDigits; ++i) // entry fracDigits = the dropped digit
    {
        const int k = dg.exp10 + i + 1;
        frac[f++] = (k >= 0 && k < kBaseDigits) ? dg.d[k] : '0';
    }
    frac[f] = '\0';

    const bool carry = RoundFraction(frac, fracDigits);
    if (carry)
    {
        if (intN == 0)
        {
            intBuf[intN++] = '1';
        }
        else
        {
            intN = IncrementDigits(intBuf, intN);
        }
    }
    const bool allZero = IsAllZero(intBuf, intN) && IsAllZero(frac, fracDigits);
    int o = 0;
    if (dg.neg && !allZero)
    {
        out[o++] = '-';
    }
    if (intN == 0)
    {
        out[o++] = '0'; // value < 1: "0.xxx"
    }
    else
    {
        std::memcpy(out + o, intBuf, static_cast<size_t>(intN));
        o += intN;
    }
    if (fracDigits > 0)
    {
        out[o++] = '.';
        std::memcpy(out + o, frac, static_cast<size_t>(fracDigits));
        o += fracDigits;
    }
    return o;
}

// Insert ',' every 3 digits of the integer part of "[-]int[.frac]" ('src' of
// length 'n'): "1234567.89" -> "1,234,567.89". The tail from the first '.'
// is copied verbatim.
int GroupDigits(const char* src, int n, char* out)
{
    int o = 0;
    int i = 0;
    if (src[0] == '-')
    {
        out[o++] = '-';
        ++i;
    }
    // 'src' is a length-bounded buffer that is NOT NUL-terminated (the
    // FormatFixed/FormatInt64Decimal callers write exactly 'n' bytes), so a
    // strchr scan would read past the valid data into stale stack memory and
    // a stray '.' there could even make 'tail' negative (memcpy SEGFAULT).
    // memchr bounds the search to the 'n' bytes we actually own.
    const char* dot = static_cast<const char*>(
        std::memchr(src + i, '.', static_cast<size_t>(n - i)));
    const int digitsLen = (dot != nullptr)
        ? static_cast<int>(dot - (src + i))
        : (n - i);
    for (int k = 0; k < digitsLen; ++k)
    {
        if (k != 0 && (digitsLen - k) % 3 == 0)
        {
            out[o++] = ',';
        }
        out[o++] = src[i + k];
    }
    if (dot != nullptr)
    {
        const int tail = n - static_cast<int>(dot - src);
        std::memcpy(out + o, dot, static_cast<size_t>(tail));
        o += tail;
    }
    return o;
}

// "N" on a double: grouped fixed-point; specials pass through verbatim.
int FormatNumberDouble(double value, int fracDigits, char* out)
{
    const int sp = FormatDoubleSpecials(value, out);
    if (sp != 0)
    {
        return sp;
    }
    char tmp[kMaxBuffer];
    const int n = FormatFixed(value, fracDigits, tmp);
    return GroupDigits(tmp, n, out);
}

// "N" on an integer: grouped integer part + '.' + 'precision' zeros.
int FormatNumberInt(char* out, int64_t value, int precision)
{
    char digits[24];
    const int n = FormatInt64Decimal(digits, value);
    int o = 0;
    int d = 0;
    if (digits[0] == '-')
    {
        out[o++] = '-';
        d = 1;
    }
    const int digitsLen = n - d;
    for (int k = 0; k < digitsLen; ++k)
    {
        if (k != 0 && (digitsLen - k) % 3 == 0)
        {
            out[o++] = ',';
        }
        out[o++] = digits[d + k];
    }
    if (precision > 0)
    {
        out[o++] = '.';
        for (int k = 0; k < precision; ++k)
        {
            out[o++] = '0';
        }
    }
    return o;
}

// Emit "[-]<lead>[.<frac digits>]E<sign><3-digit exponent>". The sign is
// omitted when the rounded mantissa is zero (-0.0 -> "0.00E+000").
int EmitSci(char* out, bool neg, char lead, const char* frac, int fracDigits, int exp)
{
    const bool allZero = (lead == '0') && IsAllZero(frac, fracDigits);
    int o = 0;
    if (neg && !allZero)
    {
        out[o++] = '-';
    }
    out[o++] = lead;
    if (fracDigits > 0)
    {
        out[o++] = '.';
        std::memcpy(out + o, frac, static_cast<size_t>(fracDigits));
        o += fracDigits;
    }
    out[o++] = 'E';
    out[o++] = (exp < 0) ? '-' : '+';
    if (exp < 0)
    {
        exp = -exp;
    }
    out[o++] = static_cast<char>('0' + (exp / 100));
    out[o++] = static_cast<char>('0' + ((exp / 10) % 10));
    out[o++] = static_cast<char>('0' + (exp % 10));
    return o;
}

// Format a finite double in scientific notation (E format). 'exp' is capped
// at 3 digits by the double range (|exp| <= 324). Non-finite values pass
// through as specials ("NaN"/U+221E/-U+221E -- the exported helpers intercept
// them via MakeSpecialString before this buffer path) -- snprintf
// prints "inf" with no exponent for them, so they must never reach the
// digit/strchr path.
int FormatSciFinite(double value, int fracDigits, char* out)
{
    const int sp = FormatDoubleSpecials(value, out);
    if (sp != 0)
    {
        return sp;
    }
    DoubleDigits dg;
    GetDoubleDigits(value, dg);

    // Mantissa fraction: d[1..fracDigits] (the base digit at mantissa
    // position i is d[i]); entry fracDigits holds the dropped digit
    // (position fracDigits + 1), zero beyond the base -- the same
    // base-limited rounding net48 applies.
    char frac[kMaxBuffer];
    int f = 0;
    for (int i = 1; i <= fracDigits + 1 && f < kMaxBuffer; ++i)
    {
        frac[f++] = (i < kBaseDigits) ? dg.d[i] : '0';
    }
    while (f <= fracDigits)
    {
        frac[f++] = '0';
    }
    frac[f] = '\0';

    int exp = dg.exp10;
    char lead = dg.d[0];
    if (RoundFraction(frac, fracDigits))
    {
        if (lead == '9')
        {
            // "9.99..." -> "1.00...": renormalize, exponent + 1. On the
            // carry-out RoundFraction already zeroed every fraction digit.
            ++exp;
            lead = '1';
        }
        else
        {
            ++lead;
        }
    }
    return EmitSci(out, dg.neg, lead, frac, fracDigits, exp);
}

// int64 -> scientific (E format) from the EXACT decimal digits (no double
// round-trip; int64 exceeds double's 2^53 exact-integer range).
int FormatSciInt(char* out, int64_t value, int fracDigits)
{
    char digits[24];
    int n = FormatInt64Decimal(digits, value);
    const bool neg = (digits[0] == '-');
    if (neg)
    {
        std::memmove(digits, digits + 1, static_cast<size_t>(n - 1));
        --n;
    }
    // mantissa: digits[0] + '.' + fracDigits more digits (zero-padded),
    // rounded half away from zero at digit 1 + fracDigits
    char mant[kMaxBuffer];
    int m = 0;
    mant[m++] = digits[0];
    const int copyCount = (n - 1 < fracDigits) ? (n - 1) : fracDigits;
    std::memcpy(mant + m, digits + 1, static_cast<size_t>(copyCount));
    m += copyCount;
    while (m < 1 + fracDigits)
    {
        mant[m++] = '0';
    }
    int exp = n - 1;
    if (n > 1 + fracDigits && digits[1 + fracDigits] >= '5')
    {
        int i = m - 1;
        while (i >= 0 && mant[i] == '9')
        {
            mant[i] = '0';
            --i;
        }
        if (i < 0)
        {
            mant[0] = '1';
            std::memset(mant + 1, '0', static_cast<size_t>(fracDigits));
            m = 1 + fracDigits;
            ++exp;
        }
        else
        {
            ++mant[i];
        }
    }
    return EmitSci(out, neg, mant[0], mant + 1, fracDigits, exp);
}

// X: uppercase hex of a 64-bit two's-complement value, zero-padded to
// 'precision' digits (int64 negatives give 16 digits).
int FormatHex(char* out, uint64_t value, int precision)
{
    char tmp[17];
    int n = 0;
    do
    {
        const uint32_t digit = static_cast<uint32_t>(value & 0x0F);
        tmp[n++] = static_cast<char>(digit < 10 ? ('0' + digit) : ('A' + digit - 10));
        value >>= 4;
    } while (value != 0);
    int o = 0;
    while (n < precision)
    {
        out[o++] = '0';
        --precision;
    }
    while (n > 0)
    {
        out[o++] = tmp[--n];
    }
    return o;
}

// P on a finite double: value*100 formatted like N, specials intercepted by
// the exported helper before this path ("NaN", U+221E, -U+221E). The caller
// appends the invariant " %" layout.
int FormatPercent(double value, int fracDigits, char* out)
{
    const int sp = FormatDoubleSpecials(value, out);
    if (sp != 0)
    {
        return sp;
    }
    char tmp[kMaxBuffer];
    const int n = FormatFixed(value * 100.0, fracDigits, tmp);
    return GroupDigits(tmp, n, out);
}

// F on a finite double: fixed WITHOUT grouping (the F/N difference -- N
// groups, F does not; net48: F2 of 1234567.89 -> "1234567.89"). Specials
// pass through verbatim.
int FormatFixedPlain(double value, int fracDigits, char* out)
{
    const int sp = FormatDoubleSpecials(value, out);
    if (sp != 0)
    {
        return sp;
    }
    return FormatFixed(value, fracDigits, out);
}

// F on an integer: the exact decimal digits + '.' + 'precision' zeros (no
// grouping, no rounding -- an integer never has fraction digits to drop).
int FormatFixedPlainInt(char* out, int64_t value, int precision)
{
    char digits[24];
    const int n = FormatInt64Decimal(digits, value);
    int o = 0;
    std::memcpy(out, digits, static_cast<size_t>(n));
    o += n;
    if (precision > 0)
    {
        out[o++] = '.';
        for (int k = 0; k < precision; ++k)
        {
            out[o++] = '0';
        }
    }
    return o;
}

// P on an integer: value*100 via an EXACT decimal digit shift (two zero
// digits appended -- no overflow, no double rounding; net48 int P is exact:
// P2 of long.MaxValue works), then the N grouping and the invariant " %"
// layout (the caller appends " %"). Zero keeps a single "0" integer part
// (net48: P2 of 0 -> "0.00 %", not "000.00 %").
int FormatPercentInt(char* out, int64_t value, int precision)
{
    char digits[24];
    int n = FormatInt64Decimal(digits, value);
    int o = 0;
    int d = 0;
    if (digits[0] == '-')
    {
        out[o++] = '-';
        d = 1;
    }
    if (value == 0)
    {
        out[o++] = '0';
        if (precision > 0)
        {
            out[o++] = '.';
            for (int k = 0; k < precision; ++k)
            {
                out[o++] = '0';
            }
        }
        return o;
    }
    // digits + "00" (the scale += 2 shift of .NET's NumberBuffer)
    char scaled[26];
    int s = 0;
    for (int k = d; k < n; ++k)
    {
        scaled[s++] = digits[k];
    }
    scaled[s++] = '0';
    scaled[s++] = '0';
    const int digitsLen = s;
    for (int k = 0; k < digitsLen; ++k)
    {
        if (k != 0 && (digitsLen - k) % 3 == 0)
        {
            out[o++] = ',';
        }
        out[o++] = scaled[k];
    }
    if (precision > 0)
    {
        out[o++] = '.';
        for (int k = 0; k < precision; ++k)
        {
            out[o++] = '0';
        }
    }
    return o;
}

//------------------------------------------------------------------------------
// Custom numeric format engine (net48 oracle-verified 2026-08-09 -- the same
// engine as formatext.cpp; the invariant symbols coincide with zh-CN for the
// custom subset: '.' / ',' / '%' / U+2030 and ASCII negative sign).
//
// SUPPORTED SUBSET (beyond the original '0'/'.' family):
//   '0'  digit placeholder (zero-padded)
//   '#'  optional digit placeholder (leading integer / trailing fraction
//        zeros suppressed)
//   '.'  decimal separator (any additional '.' ignored)
//   ','  thousands grouping (between two integer digit placeholders) or
//        number scaling (/1000 per trailing comma)
//   '%'  percent (x100), U+2030 (per-mille) per-mille (x1000) -- literal at position
//   '\'  escapes the next character as a literal
//   '..' / ".." quoted literal strings (unbalanced runs to the pattern end)
//   all other characters are copied as literals
//
// net48 semantics (oracle): value rounded to the fraction placeholder count
// (half away from zero); integer digits beyond the placeholder count are
// emitted before the first placeholder; the sign precedes every literal and
// is suppressed when the rounded magnitude is zero; a standard specifier with
// trailing junk falls back to custom ("D100" of 12345 -> "D112345").
//
// REMAINING OUT OF SUBSET (FormatException, documented divergence): scientific
// notation ("0.00E+00"/"0E0") and ';' section separators.
//------------------------------------------------------------------------------
struct CustomSpec
{
    bool grouping;
    bool fracMandatory[100]; // whether the k-th fraction placeholder is '0'
    int scale;               // +2 per '%', +3 per U+2030 (per-mille), -3 per scaling comma
    int intPh;
    int fracPh;
};

// Parse + validate a custom pattern (the caller already decided this is NOT a
// valid standard specifier). Returns false -> the caller throws FormatException.
bool ParseCustomPattern(const char16_t* p, int32_t len, CustomSpec& spec)
{
    spec = CustomSpec{};
    bool inSingle = false;
    bool inDouble = false;
    bool inFrac = false;
    for (int32_t i = 0; i < len; ++i)
    {
        const char16_t c = p[i];
        if (inSingle)
        {
            if (c == u'\'') inSingle = false;
            continue;
        }
        if (inDouble)
        {
            if (c == u'"') inDouble = false;
            continue;
        }
        if (c == u'\\') { ++i; continue; }
        if (c == u'\'') { inSingle = true; continue; }
        if (c == u'"') { inDouble = true; continue; }
        if (c == u';') return false; // ';' sections: out of subset
        if (c == u'.') { inFrac = true; continue; }
        if (c == u'0' || c == u'#')
        {
            if (inFrac)
            {
                spec.fracMandatory[spec.fracPh] = (c == u'0');
                ++spec.fracPh;
            }
            else
            {
                ++spec.intPh;
            }
            continue;
        }
        if (c == u',')
        {
            bool digitAfter = false;
            for (int32_t j = i + 1; j < len; ++j)
            {
                const char16_t d = p[j];
                if (d == u'.') break;
                if (d == u'0' || d == u'#') { digitAfter = true; break; }
            }
            if (digitAfter) spec.grouping = true;
            else spec.scale -= 3; // trailing scaling comma: /1000
            continue;
        }
        if (c == u'%') { spec.scale += 2; continue; }
        if (c == u'\u2030') { spec.scale += 3; continue; }
        if (c == u'E' || c == u'e')
        {
            int32_t j = i + 1;
            if (j < len && (p[j] == u'+' || p[j] == u'-')) ++j;
            if (j < len && p[j] == u'0') return false; // scientific: out of subset
            continue; // a plain 'E'/'e' is a literal
        }
        // any other character is a literal (copied at emission)
    }
    return true;
}

// Emit the pattern over the rounded digits ('intDigits' intLen chars, no sign;
// 'fracDigits' fracLen == spec.fracPh chars). Returns the output length.
// Grouping follows the integer digit count (every 3 from the right) -- net48
// oracle: "0,0" of 12345 -> "12,345", "#,##0" of 1234567 -> "1,234,567".
void EmitExtraDigits(const char* intDigits, int D, int P, bool grouping,
                     bool& intEmitted, char16_t* out, int& o)
{
    const int extra = (D > P) ? (D - P) : 0;
    for (int k = 0; k < extra; ++k)
    {
        const char d = intDigits[k];
        if (d == '0' && !intEmitted) continue;
        if (grouping && k > 0 && (D - k) % 3 == 0)
            out[o++] = u',';
        out[o++] = d;
        intEmitted = true;
    }
}

int EmitCustomFormat(const char* intDigits, int intLen,
                     const char* fracDigits, int fracLen,
                     bool neg, const CustomSpec& spec,
                     const char16_t* pat, int32_t patLen,
                     char16_t* out)
{
    const int D = intLen;
    const int P = spec.intPh;
    int o = 0;
    if (neg) out[o++] = u'-';
    bool inSingle = false;
    bool inDouble = false;
    bool inFrac = false;
    bool intEmitted = false;
    bool fracEmitted = false;
    bool extraEmitted = false;
    int ph = 0;
    int fph = 0;
    for (int32_t i = 0; i < patLen; ++i)
    {
        const char16_t c = pat[i];
        if (inSingle)
        {
            if (c == u'\'') inSingle = false;
            else out[o++] = c;
            continue;
        }
        if (inDouble)
        {
            if (c == u'"') inDouble = false;
            else out[o++] = c;
            continue;
        }
        if (c == u'\\')
        {
            if (i + 1 < patLen) out[o++] = pat[++i];
            continue;
        }
        if (c == u'\'') { inSingle = true; continue; }
        if (c == u'"') { inDouble = true; continue; }
        if (c == u'.')
        {
            if (!extraEmitted)
            {
                EmitExtraDigits(intDigits, D, P, spec.grouping, intEmitted, out, o);
                extraEmitted = true;
            }
            inFrac = true;
            continue;
        }
        if (c == u',') continue; // separator comes from the digit schedule
        if (c == u'0' || c == u'#')
        {
            if (!inFrac)
            {
                if (!extraEmitted)
                {
                    EmitExtraDigits(intDigits, D, P, spec.grouping, intEmitted, out, o);
                    extraEmitted = true;
                }
                const int idx = ph + D - P;
                if (idx >= 0 && idx < D)
                {
                    const char d = intDigits[idx];
                    if (c == u'0' || d != '0' || intEmitted)
                    {
                        if (spec.grouping && idx > 0 && (D - idx) % 3 == 0)
                            out[o++] = u',';
                        out[o++] = d;
                        intEmitted = true;
                    }
                }
                else if (c == u'0')
                {
                    out[o++] = u'0';
                    intEmitted = true;
                }
                ++ph;
            }
            else
            {
                const char d = (fph < fracLen) ? fracDigits[fph] : u'0';
                bool emit = (c == u'0');
                if (!emit)
                {
                    bool later = (d != u'0');
                    for (int k = fph + 1; !later && k < fracLen; ++k)
                    {
                        later = (fracDigits[k] != u'0');
                    }
                    for (int k = fph + 1; !later && k < spec.fracPh; ++k)
                    {
                        later = spec.fracMandatory[k];
                    }
                    emit = later;
                }
                if (emit)
                {
                    if (!fracEmitted) { out[o++] = u'.'; fracEmitted = true; }
                    out[o++] = d;
                }
                ++fph;
            }
            continue;
        }
        out[o++] = c; // literal (%, U+2030 (per-mille), letters, digits, symbols, ...)
    }
    return o;
}

// Exact decimal value = digits[0..len) * 10^scale, rounded to 'fracCount'
// fraction digits (half away from zero). Outputs the rounded integer part
// ('intOut', 'intN' chars, no sign) and exactly 'fracCount' fraction digits
// ('fracOut'). The int custom path scales the EXACT integer digits (percent/
// per-mille append zeros -- no overflow, oracle: INT64_MAX "0%" ->
// "922337203685477580700%"; scaling commas divide, "0,," of 123456789 ->
// "123").
void ShiftScaleInt64(const char* digits, int len, int scale, int fracCount,
                     char* intOut, int& intN, char* fracOut)
{
    const int intLen = len + scale;
    char raw[kMaxBuffer];
    int r = 0;
    for (int i = 0; i < intLen; ++i)
    {
        raw[r++] = (i < len) ? digits[i] : '0';
    }
    const int fracStart = r;
    for (int i = 0; i <= fracCount; ++i)
    {
        const int k = intLen + i;
        raw[r++] = (k >= 0 && k < len) ? digits[k] : '0';
    }
    raw[r] = '\0';
    const bool carry = RoundFraction(raw + fracStart, fracCount);
    int n = (intLen > 0) ? intLen : 0;
    if (carry)
    {
        if (n == 0)
        {
            raw[0] = '1';
            n = 1;
        }
        else
        {
            n = IncrementDigits(raw, n);
        }
    }
    intN = n;
    std::memcpy(intOut, raw, static_cast<size_t>(n));
    std::memcpy(fracOut, raw + n, static_cast<size_t>(fracCount));
}

Il2CppObject* MakeString(const char* ascii)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(ascii));
}

// zh-CN special spelling (net48-verified): "NaN", U+221E, -U+221E. Built from
// UTF-16 units -- MakeString would skip the >= 0x80 bytes of the U+221E
// symbol (il2cpp_string_new_from_cs's ASCII contract).
Il2CppObject* MakeSpecialString(double value)
{
    if (std::isnan(value))
    {
        return MakeString("NaN");
    }
    char16_t units[4];
    int o = 0;
    if (value < 0)
    {
        units[o++] = u'-';
    }
    units[o++] = kInfinitySymbol;
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(units, o));
}

} // namespace

//------------------------------------------------------------------------------
// Exported helpers (numberformat.h).
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_number_format_general(int64_t value)
{
    char buf[kMaxBuffer];
    const int len = FormatInt64Decimal(buf, value);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_general_double(double value)
{
    // net48 default double text (G15) -- single source, format.cpp; the
    // SPECIALS use the zh-CN spellings ("NaN"/U+221E/-U+221E, U+221E -- the
    // verification host's net48 default culture, matching format.cpp's G15
    // output instead of the old ASCII invariant spellings).
    if (std::isnan(value) || std::isinf(value))
    {
        return MakeSpecialString(value);
    }
    return reinterpret_cast<Il2CppObject*>(icall_double_to_string(value));
}

Il2CppObject* il2cpp_number_format_fixed(int64_t value, int32_t precision)
{
    char buf[kMaxBuffer];
    const int len = FormatNumberInt(buf, value, precision < 0 ? 2 : precision);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_fixed_double(double value, int32_t precision)
{
    // Specials first: the zh-CN symbol (U+221E) before any format work.
    if (std::isnan(value) || std::isinf(value))
    {
        return MakeSpecialString(value);
    }
    char buf[kMaxBuffer];
    const int len = FormatNumberDouble(value, precision < 0 ? 2 : precision, buf);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_ffixed(int64_t value, int32_t precision)
{
    char buf[kMaxBuffer];
    const int len = FormatFixedPlainInt(buf, value, precision < 0 ? 2 : precision);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_ffixed_double(double value, int32_t precision)
{
    if (std::isnan(value) || std::isinf(value))
    {
        return MakeSpecialString(value);
    }
    char buf[kMaxBuffer];
    const int len = FormatFixedPlain(value, precision < 0 ? 2 : precision, buf);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_hex(int64_t value, int32_t precision)
{
    char buf[kMaxBuffer];
    const int len = FormatHex(buf, static_cast<uint64_t>(value),
                              precision < 0 ? 1 : precision);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_scientific(int64_t value, int32_t precision)
{
    char buf[kMaxBuffer];
    const int len = FormatSciInt(buf, value, precision < 0 ? 6 : precision);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_scientific_double(double value, int32_t precision)
{
    if (std::isnan(value) || std::isinf(value))
    {
        return MakeSpecialString(value);
    }
    char buf[kMaxBuffer];
    const int len = FormatSciFinite(value, precision < 0 ? 6 : precision, buf);
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_percent(double value, int32_t precision)
{
    if (std::isnan(value) || std::isinf(value))
    {
        // net48: specials carry the culture symbol WITHOUT the percent
        // decoration (probe1: "P1|NaN" -> "NaN", "P1|Infinity" -> U+221E) --
        // the symbol is returned before any format work.
        return MakeSpecialString(value);
    }
    char buf[kMaxBuffer + 8];
    int len = FormatPercent(value, precision < 0 ? 2 : precision, buf);
    buf[len++] = ' ';
    buf[len++] = '%';
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_percent_int(int64_t value, int32_t precision)
{
    char buf[kMaxBuffer + 8];
    int len = FormatPercentInt(buf, value, precision < 0 ? 2 : precision);
    buf[len++] = ' ';
    buf[len++] = '%';
    buf[len] = '\0';
    return MakeString(buf);
}

Il2CppObject* il2cpp_number_format_custom(int64_t value, Il2CppObject* format)
{
    if (format == nullptr)
    {
        return il2cpp_number_format_general(value);
    }
    Il2CppString* fs = reinterpret_cast<Il2CppString*>(format);
    CustomSpec cs;
    if (!ParseCustomPattern(fs->chars, fs->length, cs))
    {
        il2cpp_throw_format_exception("Format specifier was invalid.");
    }
    char digits[24];
    const int n = FormatInt64Decimal(digits, value);
    const bool neg = (digits[0] == '-');
    char intOut[kMaxBuffer];
    int intN = 0;
    char fracOut[kMaxBuffer];
    ShiftScaleInt64(digits + (neg ? 1 : 0), n - (neg ? 1 : 0),
                    cs.scale, cs.fracPh, intOut, intN, fracOut);
    const bool allZero = IsAllZero(intOut, intN) && IsAllZero(fracOut, cs.fracPh);
    char16_t out[kMaxBuffer + 16];
    const int len = EmitCustomFormat(intOut, intN, fracOut, cs.fracPh,
                                     neg && !allZero, cs, fs->chars, fs->length, out);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(out, len));
}

Il2CppObject* il2cpp_number_format_custom_double(double value, Il2CppObject* format)
{
    if (format == nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(icall_double_to_string(value));
    }
    // net48: non-finite values return the culture symbol BEFORE any format
    // work -- even an invalid format specifier.
    if (std::isnan(value) || std::isinf(value))
    {
        return MakeSpecialString(value);
    }
    Il2CppString* fs = reinterpret_cast<Il2CppString*>(format);
    CustomSpec cs;
    if (!ParseCustomPattern(fs->chars, fs->length, cs))
    {
        il2cpp_throw_format_exception("Format specifier was invalid.");
    }
    double scaled = value;
    if (cs.scale > 0)
    {
        double f = 1.0;
        for (int k = 0; k < cs.scale; ++k) f *= 10.0;
        scaled *= f;
    }
    else if (cs.scale < 0)
    {
        double f = 1.0;
        for (int k = 0; k < -cs.scale; ++k) f *= 10.0;
        scaled /= f;
    }
    char fixedBuf[kMaxBuffer];
    const int fn = FormatFixed(scaled, cs.fracPh, fixedBuf);
    fixedBuf[fn] = '\0'; // FormatFixed does NOT NUL-terminate
    const bool neg = (fixedBuf[0] == '-');
    const int start = neg ? 1 : 0;
    const char* dot = std::strchr(fixedBuf + start, '.');
    const int intLen = (dot != nullptr)
        ? static_cast<int>(dot - (fixedBuf + start))
        : (fn - start);
    char16_t out[kMaxBuffer + 16];
    const int len = EmitCustomFormat(fixedBuf + start, intLen,
                                     (dot != nullptr) ? dot + 1 : nullptr,
                                     cs.fracPh, neg, cs, fs->chars, fs->length, out);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(out, len));
}

// saudit1/4: the culture-name dispatch shared by the value-dependent
// NumberFormatInfo getters. "" = invariant (net48 U+00A4 (currency sign) and
// "Infinity"/"-Infinity"); any other name = the pinned current culture
// (zh-CN: U+00A5 (yen sign) and U+221E (infinity)). The value IS the culture name string
// (CultureInfo handle-gap model).
bool IsInvariantCultureName(Il2CppObject* cultureName)
{
    if (cultureName == nullptr)
    {
        return false;
    }
    const Il2CppString* name = reinterpret_cast<const Il2CppString*>(cultureName);
    return name->length == 0;
}

Il2CppObject* il2cpp_number_format_currency_symbol(Il2CppObject* cultureName)
{
    return reinterpret_cast<Il2CppObject*>(
        IsInvariantCultureName(cultureName)
            ? il2cpp_string_new(u"\u00A4", 1)
            : il2cpp_string_new(u"\u00A5", 1));
}

Il2CppObject* il2cpp_number_format_positive_infinity_symbol(Il2CppObject* cultureName)
{
    return reinterpret_cast<Il2CppObject*>(
        IsInvariantCultureName(cultureName)
            ? il2cpp_string_new_from_cs("Infinity")
            : il2cpp_string_new(u"\u221E", 1));
}

Il2CppObject* il2cpp_number_format_negative_infinity_symbol(Il2CppObject* cultureName)
{
    return reinterpret_cast<Il2CppObject*>(
        IsInvariantCultureName(cultureName)
            ? il2cpp_string_new_from_cs("-Infinity")
            : il2cpp_string_new(u"-\u221E", 2));
}

//------------------------------------------------------------------------------
// gn20-numfmt (probe batch 3): the remaining NumberFormatInfo getters.
// All values are culture-INDEPENDENT on the invariant/zh-CN two-culture
// surface (zh-CN and invariant agree) EXCEPT the patterns marked below, which
// dispatch on the culture name. Values pinned from the net48 multi-culture
// oracle (oracle_net48.txt / oracle_xcult.txt): zh-CN CurrencySymbol
// U+00A5 (already in get_currency_symbol dispatch), CurrencyNegativePattern
// 2 vs invariant 0, PercentPositivePattern 1 vs 0, PercentNegativePattern
// 1 vs 0. Everything else identical. Group sizes = {3}, native digits =
// "0".."9" (ESP / ASCII) in BOTH.
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_number_format_currency_decimal_separator(Il2CppObject*)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("."));
}

Il2CppObject* il2cpp_number_format_currency_group_separator(Il2CppObject*)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(","));
}

Il2CppObject* il2cpp_number_format_per_mille_symbol(Il2CppObject*)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(u"\u2030", 1));
}

int32_t il2cpp_number_format_number_negative_pattern(Il2CppObject*)
{
    return 1; // zh-CN && invariant: "-n" (leading minus)
}

int32_t il2cpp_number_format_number_decimal_digits(Il2CppObject*)
{
    return 2;
}

int32_t il2cpp_number_format_currency_decimal_digits(Il2CppObject*)
{
    return 2;
}

int32_t il2cpp_number_format_currency_positive_pattern(Il2CppObject*)
{
    return 0; // zh-CN && invariant: "¥n" (symbol precedes the value)
}

int32_t il2cpp_number_format_currency_negative_pattern(Il2CppObject* cultureName)
{
    // zh-CN = 2 ("-¥n" sign before symbol), invariant = 0 ("(¥n)").
    return IsInvariantCultureName(cultureName) ? 0 : 2;
}

int32_t il2cpp_number_format_percent_positive_pattern(Il2CppObject* cultureName)
{
    // zh-CN = 1 ("n%"), invariant = 0 ("n %" trailing space).
    return IsInvariantCultureName(cultureName) ? 0 : 1;
}

int32_t il2cpp_number_format_percent_negative_pattern(Il2CppObject* cultureName)
{
    // zh-CN = 1 ("-n%"), invariant = 0 ("-n %").
    return IsInvariantCultureName(cultureName) ? 0 : 1;
}

int32_t il2cpp_number_format_percent_decimal_digits(Il2CppObject*)
{
    return 2;
}

static Il2CppObject* MakeGroupSizes(int32_t size)
{
    Il2CppArray* arr = il2cpp_array_new(nullptr, sizeof(int32_t), false, 1);
    reinterpret_cast<int32_t*>(arr->data)[0] = size;
    return reinterpret_cast<Il2CppObject*>(arr);
}

Il2CppObject* il2cpp_number_format_number_group_sizes(Il2CppObject*)
{
    return MakeGroupSizes(3);
}

Il2CppObject* il2cpp_number_format_currency_group_sizes(Il2CppObject*)
{
    return MakeGroupSizes(3);
}

Il2CppObject* il2cpp_number_format_percent_group_sizes(Il2CppObject*)
{
    return MakeGroupSizes(3);
}

Il2CppObject* il2cpp_number_format_native_digits(Il2CppObject*)
{
    Il2CppArray* arr = il2cpp_array_new(nullptr, sizeof(void*), true, 10);
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(arr->data);
    static const char kDigits[10][2] = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
    for (int32_t i = 0; i < 10; ++i)
    {
        slots[i] = reinterpret_cast<Il2CppObject*>(
            il2cpp_string_new_from_cs(kDigits[i]));
    }
    il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
    return reinterpret_cast<Il2CppObject*>(arr);
}

int32_t il2cpp_number_format_digit_substitution(Il2CppObject*)
{
    // net48 (zh-CN host, oracle_digitsub.txt): both the zh-CN current and the
    // invariant NumberFormatInfo report DigitShapes.None == 1 (the host prints
    // Western digits "0".."9"; Hebrew etc. would differ -- W7, no ILC surface).
    return 1;
}

bool il2cpp_number_format_is_read_only(Il2CppObject*)
{
    // net48 (oracle_digitsub.txt): both NumberFormatInfo.CurrentInfo and
    // NumberFormatInfo.InvariantInfo are the canonical read-only singletons
    // (IsReadOnly == true on the two-culture surface).
    return true;
}

}} // namespace il2cpp::runtime
