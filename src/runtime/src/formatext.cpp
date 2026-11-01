//------------------------------------------------------------------------------
// formatext.cpp -- ToString(format) custom-format icalls (il2cpp_format_ext.h).
//
// Implementation notes:
//   * Integer D/X/N/C/E/G/F/P paths use the manual digit-loop style of
//     format.cpp (deterministic, locale-independent, GCC 7.5 compatible;
//     format.cpp's helpers are file-local so the loop is re-implemented
//     here -- reuse of the idea, not the symbols).
//   * Double F/N/E/P/G/custom paths: the digits come from snprintf("%.14e")
//     -- the CRT's 15-significant-digit correctly-rounded decimal
//     conversion, the SAME base .NET Framework 4.8 produces (msvcrt-class
//     converters). .NET's round-half-AWAY-FROM-ZERO rule is then applied on
//     those 15 digits and exactly <precision> fraction digits are
//     re-emitted. The 15-digit base reproduces the net48 oracle on near-tie
//     values (2.675 F2 -> "2.68", 1.23499999 F2 -> "1.23", 0.15 F1 -> "0.2")
//     -- see docs/audit/number-conformance.md.
//     tiebreak2: an EXACT tie at the base width is the one place where the
//     CRT's conversion and net48 disagree (CRT rounds half to EVEN, net48
//     half AWAY FROM ZERO). GetTieBaseDigits detects that tie with exact
//     integer arithmetic and increments the digit string, so every
//     floating face (float 7/9, double 15/17) rounds like net48.
//   * Grouping (N/C/P) and the currency/percent decorations are applied on
//     the already-rounded digit string -- no further floating point. P
//     shifts the digit string's decimal scale by 2 (net48 NumberToString:
//     `case 'P': number.Scale += 2`) instead of multiplying the value --
//     the product of a double and 100 can round, and that rounding is
//     observable (tiebreak2: P2 of 1234567890123445.0 is
//     "123,456,789,012,345,000.00", the value*100.0 product would print
//     "...344,000.00"; probe4: P1 of 1.23456789012345678e17 ->
//     "12,345,678,901,234,600,000.0%" -- the same under both models).
//   * int32/int64 E uses the EXACT decimal digits of the integer (no double
//     round-trip: int64 exceeds double's 2^53 exact-integer range).
//   * "G" for double reuses the existing G15 icall (il2cpp_format.h) --
//     single formatting source for the default path; G with an explicit
//     precision is implemented here (net48: fixed vs scientific switch at
//     scale > precision or scale - 1 < -4, exponent min 2 digits).
//   * SPECIALS (net48-verified, probe4): zh-CN renders NaN as "NaN" and
//     +-Infinity as the culture symbol U+221E (infinity) for EVERY format
//     (F/N/E/G/P/C/custom) -- the symbol is returned before any format
//     work. (format.cpp's G15 path emits ASCII "Infinity" -- an out-of-
//     domain divergence, documented there.)
//------------------------------------------------------------------------------

#include "il2cpp_format_ext.h"
#include "il2cpp_format.h"        // icall_double_to_string (G path reuse)
#include "il2cpp_exception_ext.h" // il2cpp_throw_format_exception
#include "il2cpp_metadata.h"      // Il2CppClass (boxed-klass dispatch)
#include "il2cpp_value_semantics.h" // icall_object_to_string (composite fallback)
#include "decimal_format.h"        // icall_decimal_to_string_custom (boxed decimal)
#include "il2cpp_time_ext.h"       // icall_datetime_to_string_format (boxed DateTime)
#include "il2cpp_dtoffset.h"       // icall_dtoffset_to_string_format (boxed DateTimeOffset)
#include "guid.h"                  // il2cpp_guid_to_string_format (boxed Guid)
#include "enumext.h"               // il2cpp_enum_boxed_to_string_format (boxed enum)

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <string>

namespace il2cpp { namespace runtime {

namespace {

// Largest formatted value: fixed-point double with 99 fraction digits plus
// guard digits (309 integer digits + '.' + 103 + NUL); the currency paths
// compose a 2-byte UTF-8 symbol in front (buffer + 8).
const int kMaxBuffer = 512;

// net48's double conversion base: 15 significant digits, correctly rounded
// (msvcrt-class CRT converters; snprintf("%.14e") is exact at this
// precision). All double rounding happens on these digits -- see the file
// header and docs/audit/number-conformance.md.
const int kBaseDigits = 15;

// D-DBLF-1b (dblfmt_r R ladder): the wide base for sigDigits > kBaseDigits
// (net48 "G16"/"G17" and the R round-trip fallback). 17 significant digits
// always round-trip a double; snprintf("%.16e") is correctly rounded at this
// width (an exact tie at the 18th digit is not representable in a 53-bit
// mantissa).
const int kWideDigits = 17;

// The fixed currency symbol: U+00A5 (yen sign) -- the verification host's net48
// default culture (zh-CN). See the header's CULTURE FIXING divergence note.
const char16_t kCurrencySymbol = u'\u00A5';

// singlefmt: the net48 FLOAT digit base for F/N/C/P/custom (and the default
// E/G tiers) -- net48's native Number.FormatSingle converts the widened
// double at SEVEN significant digits (nine for wide E/G), not the double
// engine's 15. Oracle: probe-singlefmt (0.1f "F10" -> "0.1000000000").
const int kFloatBaseDigits = 7;

// The zh-CN positive-infinity symbol: U+221E (infinity) (net48 probe4: EVERY
// format renders +-Infinity as the culture symbol).
const char16_t kInfinitySymbol = u'\u221E';

// The digit base of a finite floating value (value == d[0].d[1].. x
// 10^exp10). 'd' holds kWideDigits bytes; only the first 'digits' are
// meaningful (kBaseDigits for the double engine, 7/9 for the float engine).
struct DoubleDigits
{
    char d[kWideDigits]; // correctly rounded significant digits
    int  exp10;          // decimal exponent of d[0]
    bool neg;
};

// Correctly rounded 'digits'-significant-digit decimal of 'value'
// (snprintf("%.(digits-1)e") -- the msvcrt-class conversion base net48 uses
// for BOTH widths; digits is 15 for the double engine and 7/9 for the float
// engine, per the net48 Number.FormatSingle oracle).
void GetDoubleDigits(double value, DoubleDigits& out, int digits)
{
    char tmp[32];
    std::snprintf(tmp, sizeof tmp, "%.*e", digits - 1, value);
    out.neg = (tmp[0] == '-');
    const char* p = tmp + (out.neg ? 1 : 0);
    out.d[0] = *p++;
    ++p; // skip '.'
    for (int i = 1; i < digits; ++i)
    {
        out.d[i] = *p++;
    }
    out.exp10 = std::atoi(p + 1); // "e+XX"/"e-XX"
}

// tiebreak: TRUE when the magnitude of 'value' is EXACTLY a half-way tie at
// 'digits' significant decimal digits, i.e. its exact decimal expansion has
// digits+1 significant digits and the last one is 5 (value = (10X+5) *
// 10^(exp10 - digits + 1)). This is the only place where net48's half AWAY
// FROM ZERO rule and the CRT's "%.*e" half to EVEN rule disagree.
//
// The test is exact integer arithmetic -- no floating point comparison:
// value = M * 2^n with M the 53-bit significand. With k = digits - 1 - exp10,
// the tie digit is exactly 5 iff 2*value*10^k is an ODD integer, i.e.
//     M * 2^(n + 1 + k) * 5^k
// is odd and integral. Writing M = M' * 2^t (M' odd) that is
//     t + n + 1 + k == 0        (the 2-adic part is exactly one)
//     k >= 0  OR  5^(-k) divides M'   (the 5-adic part is integral)
// The caller passes the magnitude (sign is irrelevant to the tie) and an
// exp10 taken from a WIDE conversion; the test self-validates an off-by-one
// exponent (it then fails). A tie value has at most digits+1 <= 18
// significant digits, so a 30-significant-digit conversion reports its
// exponent exactly (see GetTieBaseDigits).
bool IsDigitTie(double magnitude, int exp10, int digits)
{
    if (!(magnitude > 0.0) || !std::isfinite(magnitude))
    {
        return false;
    }
    int e2 = 0;
    const double frac = std::frexp(magnitude, &e2); // magnitude = frac * 2^e2
    uint64_t mant = static_cast<uint64_t>(std::ldexp(frac, 53)); // 53-bit
    const int n = e2 - 53;                                       // = mant * 2^n
    int twos = 0;
    while ((mant & 1ULL) == 0ULL)
    {
        mant >>= 1;
        ++twos;
    }
    const int k = digits - 1 - exp10;
    if (twos + n + 1 + k != 0)
    {
        return false; // fractional, or an even multiple of the half unit
    }
    if (k >= 0)
    {
        return true; // 5^k is integral; the 2-adic part is exactly one
    }
    uint64_t p5 = 1ULL;
    for (int i = 0; i < -k; ++i)
    {
        if (p5 > mant / 5ULL)
        {
            return false; // 5^(-k) exceeds the significand -> not integral
        }
        p5 *= 5ULL;
    }
    return (mant % p5) == 0ULL;
}

// The net48 digit base for EVERY floating face: GetDoubleDigits, except that
// an EXACT tie at the base width rounds half AWAY FROM ZERO (net48) instead of
// half to EVEN (the CRT). The widths are 7/9 for the float face and 15 (the
// default/G15/F/N/C/P/custom base) or 17 (the G16/G17/E wide base) for the
// double face; net48's half-away rule is the same at every width (oracle:
// 1000000000000000.25 G17 -> "1000000000000000.3", E16 -> "...0003E+015").
// Only ties differ, so every non-tie conversion stays byte-identical. A tie
// value has exactly digits+1 significant digits, so the digits+1-wide
// conversion below is exact -- no CRT rounding is involved -- and the
// away-from-zero increment is then done on the digit string.
//
// exp10 is read from a 30-significant-digit conversion, NOT from the
// digits+1-wide one: at the 17-digit width the tie itself can round the
// digits+1-wide text up a decade, which would feed IsDigitTie a wrong
// exponent. A double is never within 1e-30 relative of a decade boundary
// (its ulp is >= 1e-16 relative), so 30 digits always report the true
// exponent (the tie test stays exact integer arithmetic).
void GetTieBaseDigits(double value, DoubleDigits& out, int digits)
{
    char wide[48];
    std::snprintf(wide, sizeof wide, "%.29e", value);
    const int exp10 = std::atoi(std::strchr(wide, 'e') + 1);
    if (!IsDigitTie(std::fabs(value), exp10, digits))
    {
        GetDoubleDigits(value, out, digits);
        return;
    }
    char tmp[48];
    std::snprintf(tmp, sizeof tmp, "%.*e", digits, value); // exact at digits+1
    out.neg = (tmp[0] == '-');
    const char* p = tmp + (out.neg ? 1 : 0);
    out.d[0] = *p++;
    ++p; // skip '.'
    // Only the 'digits' base digits are stored (out.d has kWideDigits = 17
    // bytes, and the 17-digit base leaves no room for the tie digit); the
    // tie digit itself is dropped -- the away-from-zero increment below is
    // unconditional once IsDigitTie fired.
    for (int i = 1; i < digits; ++i)
    {
        out.d[i] = *p++;
    }
    out.exp10 = std::atoi(std::strchr(p, 'e') + 1);
    int i = digits - 1;
    while (i >= 0 && out.d[i] == '9')
    {
        out.d[i] = '0';
        --i;
    }
    if (i < 0)
    {
        out.d[0] = '1'; // 999...5 -> 1000...: the base rolls to the next decade
        ++out.exp10;
    }
    else
    {
        ++out.d[i];
    }
}

bool IsSpecial(double value)
{
    return std::isnan(value) || std::isinf(value);
}

// zh-CN special spelling (net48-verified): "NaN", U+221E, -U+221E.
Il2CppObject* MakeSpecialString(double value)
{
    if (std::isnan(value))
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("NaN"));
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

// saudit1: the INVARIANT special spelling ("Infinity"/"-Infinity", ASCII);
// NaN is "NaN" in every culture. The zh-CN path stays MakeSpecialString.
Il2CppObject* MakeSpecialStringInvariant(double value)
{
    if (std::isnan(value))
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("NaN"));
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new_from_cs(value < 0 ? "-Infinity" : "Infinity"));
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

// Parse a standard-format specifier: one ASCII letter + up to 2 precision
// digits. 'letter' keeps the ORIGINAL case ('x'/'e'/'g' render lowercase
// digits/markers on net48); 'precision' is -1 when no digits were present
// (the caller applies the per-format default). Returns false when the
// string is not a fully-consumed standard specifier (unknown letter,
// trailing junk after the digits, precision > 99, or a custom/junk first
// character) -- the caller decides between the custom path (double, first
// char '0'/'.') and FormatException. Lowercase letters are accepted (.NET
// accepts both cases).
bool TryParseStandard(const char16_t* chars, int32_t len, char& letter, int& precision)
{
    if (len == 0)
    {
        letter = 'G';
        precision = 0;
        return true;
    }
    const char16_t c0 = chars[0];
    const bool isLetter = (c0 >= u'A' && c0 <= u'Z') || (c0 >= u'a' && c0 <= u'z');
    if (!isLetter)
    {
        return false;
    }
    letter = static_cast<char>(c0);
    precision = -1;
    int i = 1;
    while (i < len && chars[i] >= u'0' && chars[i] <= u'9')
    {
        const int digit = static_cast<int>(chars[i] - u'0');
        precision = (precision < 0) ? digit : precision * 10 + digit;
        if (precision > 99)
        {
            return false; // net48: precision 0-99 only
        }
        ++i;
    }
    // Trailing junk: net48 falls back to custom formatting with literals
    // ("D100" -> "D112345"); our custom subset is '0'/'.' only -> reject.
    return i == len;
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

// Format a finite double in fixed-point with exactly 'fracDigits' decimals.
// Writes into 'out' (kMaxBuffer); returns the length; NOT NUL-terminated.
// The sign is omitted when the rounded magnitude is zero (-0.0 -> "0.00",
// net48-verified); the '.' is omitted when fracDigits == 0 (N0/F0 layout).
// Rounding is half AWAY FROM ZERO on the 'baseDigits' significant-digit base
// (kBaseDigits/kWideDigits for the double engine, 7/9 for the float engine;
// GetTieBaseDigits fixes the CRT's half-to-even tie).
//
// 'scaleShift' moves the decimal point of the DIGIT STRING (exp10 + shift)
// instead of scaling the value in floating point. net48's P is exactly that
// (corert Number.Formatting.cs NumberToString: `case 'P': number.Scale += 2`)
// and the difference is observable: P2 of 1234567890123445.0 is
// "123,456,789,012,345,000.00" (the 15-digit tie of the ORIGINAL value,
// shifted) while value*100.0 rounds in double to 123456789012344496 and
// prints "...344,000.00" (tiebreak2 oracle).
int FormatFixed(double value, int fracDigits, char* out, int baseDigits,
                int scaleShift = 0)
{
    DoubleDigits dg;
    GetTieBaseDigits(value, dg, baseDigits);
    // A zero magnitude keeps exp10 = 0 whatever the scale shift: its digit
    // string is all zeros, so shifting would emit leading zeros ("000" for
    // P2 of 0.0) instead of net48's plain "0".
    bool allZeroDigits = true;
    for (int i = 0; i < baseDigits; ++i)
    {
        if (dg.d[i] != '0')
        {
            allZeroDigits = false;
            break;
        }
    }
    const int exp10 = allZeroDigits ? 0 : dg.exp10 + scaleShift;

    // Integer part: d[k] sits at 10^(exp10 - k); the digits with
    // exp10 - k >= 0 are d[0..exp10], in order ("1234..."). A negative
    // exp10 means no integer digits ("0").
    const int intLen = exp10 + 1;
    char intBuf[kMaxBuffer];
    int intN = 0;
    if (intLen > 0)
    {
        for (int i = 0; i < intLen; ++i)
        {
            intBuf[intN++] = (i < baseDigits) ? dg.d[i] : '0';
        }
    }

    // Fraction digits: the 1-based fraction position j holds digit
    // d[exp10 + j] (out of [0, baseDigits-1] -> zero: leading zeros or
    // beyond the base -- the base-limited digits match net48's own base).
    char frac[kMaxBuffer];
    int f = 0;
    for (int i = 0; i <= fracDigits; ++i) // entry fracDigits = the dropped digit
    {
        const int k = exp10 + i + 1;
        frac[f++] = (k >= 0 && k < baseDigits) ? dg.d[k] : '0';
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

// FormatFixed on a finite value (the caller handled the specials).
int FormatFinite(double value, int fracDigits, char* out, int baseDigits,
                 int scaleShift = 0)
{
    return FormatFixed(value, fracDigits, out, baseDigits, scaleShift);
}

// FormatFinite with ',' inserted every 3 digits in the integer part (N/P).
int FormatGroupedFinite(double value, int fracDigits, char* out, int baseDigits,
                        int scaleShift = 0)
{
    char tmp[kMaxBuffer];
    const int n = FormatFinite(value, fracDigits, tmp, baseDigits, scaleShift);
    int o = 0;
    int i = 0;
    if (tmp[0] == '-')
    {
        out[o++] = '-';
        ++i;
    }
    // 'tmp' is a length-bounded buffer that is NOT NUL-terminated (FormatFixed
    // writes exactly 'n' bytes -- the same gap b57650b fixed in numberformat's
    // GroupDigits), so a strchr scan reads past the valid data into stale stack
    // memory and a stray '.' there can make 'tail' negative (memcpy SEGFAULT).
    // memchr bounds the search to the 'n' bytes we actually own.
    const char* dot = static_cast<const char*>(
        std::memchr(tmp + i, '.', static_cast<size_t>(n - i)));
    const int digitsLen = (dot != nullptr)
        ? static_cast<int>(dot - (tmp + i))
        : (n - i);
    for (int k = 0; k < digitsLen; ++k)
    {
        if (k != 0 && (digitsLen - k) % 3 == 0)
        {
            out[o++] = ',';
        }
        out[o++] = tmp[i + k];
    }
    if (dot != nullptr)
    {
        const int tail = n - static_cast<int>(dot - tmp);
        std::memcpy(out + o, dot, static_cast<size_t>(tail));
        o += tail;
    }
    return o;
}

// Emit "[-]<lead>[.<frac digits>]E<sign><3-digit exponent>". The sign is
// omitted when the rounded mantissa is zero (-0.0 -> "0.00E+000").
int EmitSci(char* out, bool neg, char lead, const char* frac, int fracDigits, int exp, char expChar)
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
    out[o++] = expChar;
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
// at 3 digits by the double range (|exp| <= 324). Rounding is half AWAY
// FROM ZERO on the 'baseDigits' significant-digit base (GetTieBaseDigits, as
// in FormatFixed).
int FormatSciFinite(double value, int fracDigits, char* out, char expChar, int baseDigits)
{
    DoubleDigits dg;
    GetTieBaseDigits(value, dg, baseDigits);

    // Mantissa fraction: d[1..fracDigits]; entry fracDigits holds the
    // dropped digit (mantissa position fracDigits + 1), zero beyond the
    // base -- the same base-limited rounding net48 applies.
    char frac[kMaxBuffer];
    int f = 0;
    for (int i = 1; i <= fracDigits + 1 && f < kMaxBuffer; ++i)
    {
        frac[f++] = (i < baseDigits) ? dg.d[i] : '0';
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
    return EmitSci(out, dg.neg, lead, frac, fracDigits, exp, expChar);
}

// int64 -> scientific (E format) from the EXACT decimal digits (no double
// round-trip; int64 exceeds double's 2^53 exact-integer range).
int FormatSciInt(char* out, int64_t value, int fracDigits, char expChar)
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
    return EmitSci(out, neg, mant[0], mant + 1, fracDigits, exp, expChar);
}

// D: decimal, zero-padded to 'precision' digits after the sign.
int FormatDecimalPadded(char* out, int64_t value, int precision)
{
    char digits[24];
    const int n = FormatInt64Decimal(digits, value);
    if (precision <= 0)
    {
        std::memcpy(out, digits, static_cast<size_t>(n));
        return n;
    }
    int o = 0;
    int d = 0;
    if (digits[0] == '-')
    {
        out[o++] = '-';
        d = 1;
    }
    const int digitsLen = n - d;
    while (digitsLen < precision)
    {
        out[o++] = '0';
        --precision;
    }
    std::memcpy(out + o, digits + d, static_cast<size_t>(digitsLen));
    o += digitsLen;
    return o;
}

// X/x: hex of a 32/64-bit two's-complement value, zero-padded to
// 'precision' digits (32-bit negatives give 8 digits, 64-bit 16 digits).
// 'hexBase' is 'A' (uppercase X) or 'a' (lowercase x) -- net48 renders the
// digit case from the specifier case.
int FormatHex(char* out, uint64_t value, int precision, char hexBase)
{
    char tmp[17];
    int n = 0;
    do
    {
        const uint32_t digit = static_cast<uint32_t>(value & 0x0F);
        tmp[n++] = static_cast<char>(digit < 10 ? ('0' + digit) : (hexBase + digit - 10));
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

// F on an integer: the exact decimal digits + '.' + 'precision' zeros (no
// rounding -- an integer never has fraction digits to drop).
int FormatFixedInt(char* out, int64_t value, int precision)
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
    std::memcpy(out + o, digits + d, static_cast<size_t>(n - d));
    o += n - d;
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
// digits appended -- no overflow, no double rounding), then the N grouping
// and the zh-CN "n%" percent layout (the caller appends '%'). Zero keeps a
// single "0" integer part (net48: P0 of 0 -> "0%", not "000%").
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

// .NET G on an integer: round to 'sigDigits' significant digits (half away
// from zero on the EXACT decimal digits), strip trailing zeros, then emit
// fixed (scale <= sigDigits) or scientific (scale > sigDigits) with the
// min-2-digit exponent (net48: (12345).ToString("G2") -> "1.2E+04",
// long.MaxValue G15 -> "9.22337203685478E+18").
int FormatGeneralInt(char* out, int64_t value, int sigDigits, char expChar)
{
    char digits[24];
    int n = FormatInt64Decimal(digits, value);
    const bool neg = (digits[0] == '-');
    const int d0 = neg ? 1 : 0;
    int scale = n - d0;
    int count = scale;
    if (sigDigits > 0 && sigDigits < scale)
    {
        if (digits[d0 + sigDigits] >= '5')
        {
            int i = d0 + sigDigits - 1;
            while (i >= d0 && digits[i] == '9')
            {
                digits[i] = '0';
                --i;
            }
            if (i < d0)
            {
                digits[d0] = '1';
                ++scale;
            }
            else
            {
                ++digits[i];
            }
        }
        count = sigDigits;
    }
    while (count > 1 && digits[d0 + count - 1] == '0')
    {
        --count;
    }
    int o = 0;
    if (neg)
    {
        out[o++] = '-';
    }
    if (scale > sigDigits)
    {
        // scientific: d[0][.rest]E[+-]ee
        out[o++] = digits[d0];
        if (count > 1)
        {
            out[o++] = '.';
            std::memcpy(out + o, digits + d0 + 1, static_cast<size_t>(count - 1));
            o += count - 1;
        }
        int e = scale - 1;
        out[o++] = expChar;
        out[o++] = (e < 0) ? '-' : '+';
        if (e < 0)
        {
            e = -e;
        }
        if (e < 10)
        {
            out[o++] = '0';
            out[o++] = static_cast<char>('0' + e);
        }
        else if (e < 100)
        {
            out[o++] = static_cast<char>('0' + (e / 10));
            out[o++] = static_cast<char>('0' + (e % 10));
        }
        else
        {
            out[o++] = static_cast<char>('0' + (e / 100));
            out[o++] = static_cast<char>('0' + ((e / 10) % 10));
            out[o++] = static_cast<char>('0' + (e % 10));
        }
        return o;
    }
    // fixed
    for (int i = 0; i < count; ++i)
    {
        out[o++] = digits[d0 + i];
    }
    return o;
}

// Emit "E[+-]dd.." with the exponent zero-padded to 'minDigits' digits.
int EmitExponent(char* out, int e, char expChar, int minDigits)
{
    int o = 0;
    out[o++] = expChar;
    out[o++] = (e < 0) ? '-' : '+';
    if (e < 0)
    {
        e = -e;
    }
    char tmp[8];
    int n = 0;
    do
    {
        tmp[n++] = static_cast<char>('0' + (e % 10));
        e /= 10;
    } while (e != 0);
    while (n < minDigits)
    {
        tmp[n++] = '0';
    }
    while (n > 0)
    {
        out[o++] = tmp[--n];
    }
    return o;
}

// .NET G on a finite double with an explicit precision: round the 15-digit
// base to 'sigDigits' significant digits (half away from zero), strip
// trailing zeros, then fixed (scale <= sigDigits and scale >= -3) or
// scientific (scale > sigDigits or scale - 1 < -4) with the min-2-digit
// exponent (net48: G2 of 1234567.89 -> "1.2E+06", G7 -> "1234568",
// G2 of 0.000123 -> "0.00012", G1 of 1e-6 -> "1E-06").
// The shared G body over a correctly rounded base of 'baseDigits' digits
// (d[] is a mutable copy -- the half-away-from-zero rounding at sigDigits
// rewrites it). The net48 semantics are those documented above.
int FormatGeneralFromDigits(char* d, int baseDigits, int exp10, bool neg,
                            int sigDigits, char expChar, char* out)
{
    int scale = exp10 + 1;
    int count = baseDigits;
    if (sigDigits > 0 && sigDigits < baseDigits)
    {
        if (d[sigDigits] >= '5')
        {
            int i = sigDigits - 1;
            while (i >= 0 && d[i] == '9')
            {
                d[i] = '0';
                --i;
            }
            if (i < 0)
            {
                d[0] = '1';
                ++scale;
            }
            else
            {
                ++d[i];
            }
        }
        count = sigDigits;
    }
    while (count > 1 && d[count - 1] == '0')
    {
        --count;
    }
    const bool allZero = (count == 1 && d[0] == '0');
    int o = 0;
    if (neg && !allZero)
    {
        out[o++] = '-';
    }
    if (scale > sigDigits || scale < -3)
    {
        out[o++] = d[0];
        if (count > 1)
        {
            out[o++] = '.';
            std::memcpy(out + o, d + 1, static_cast<size_t>(count - 1));
            o += count - 1;
        }
        o += EmitExponent(out + o, scale - 1, expChar, 2);
        return o;
    }
    if (scale > 0)
    {
        for (int i = 0; i < scale; ++i)
        {
            out[o++] = (i < count) ? d[i] : '0';
        }
    }
    else
    {
        out[o++] = '0';
    }
    if (count > scale)
    {
        out[o++] = '.';
        if (scale < 0)
        {
            for (int i = 0; i < -scale; ++i)
            {
                out[o++] = '0';
            }
        }
        for (int i = (scale > 0 ? scale : 0); i < count; ++i)
        {
            out[o++] = d[i];
        }
    }
    return o;
}

int FormatGeneralDouble(double value, int sigDigits, char expChar, char* out)
{
    if (sigDigits > kBaseDigits)
    {
        // D-DBLF-1b (dblfmt_r): the R ladder's G17 step and user "G16"/"G17"
        // need 16-17 significant digits; the 15-digit base cannot express
        // them, so "G17" previously re-emitted the G15 text and R never
        // round-tripped (0.1+0.2 -> "0.3" instead of "0.30000000000000004").
        // Generate the wide base and run the same G body. The 17-digit base
        // has the same half-AWAY-FROM-ZERO tie rule as the 15-digit one
        // (tiebreak2 oracle: 1000000000000000.25 "G17" -> "1000000000000000.3",
        // the CRT's half-to-even base gives "1000000000000000.2"), so it comes
        // from GetTieBaseDigits too.
        DoubleDigits dg;
        GetTieBaseDigits(value, dg, kWideDigits);
        return FormatGeneralFromDigits(dg.d, kWideDigits, dg.exp10, dg.neg,
                                       sigDigits, expChar, out);
    }
    DoubleDigits dg;
    GetTieBaseDigits(value, dg, kBaseDigits);
    return FormatGeneralFromDigits(dg.d, kBaseDigits, dg.exp10, dg.neg,
                                   sigDigits, expChar, out);
}

// N on an integer: grouped integer part + '.' + 'precision' zeros (the same
// layout is the ASCII tail of the C format -- the symbol is composed by the
// caller in front of the sign, "U+00A5-12,345.00").
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

//------------------------------------------------------------------------------
// Custom numeric format engine (net48 oracle-verified 2026-08-09 -- probes in
// %TEMP%\opencode\cstmprobe\oracle{1..9}.txt on the verification host).
//
// SUPPORTED SUBSET (beyond the original '0'/'.' family):
//   '0'  digit placeholder (zero-padded)
//   '#'  optional digit placeholder (leading integer / trailing fraction
//        zeros suppressed)
//   '.'  decimal separator (any additional '.' ignored)
//   ','  thousands grouping (between two integer digit placeholders) or
//        number scaling (/1000 per trailing comma: "0,," -> /1e6)
//   '%'  percent (x100), U+2030 (per-mille) per-mille (x1000) -- the symbol is a
//        literal at its pattern position
//   '\'  escapes the next character as a literal
//   '..' / ".." quoted literal strings (unbalanced runs to the pattern end)
//   all other characters are copied as literals
//
// net48 semantics (oracle): the value is rounded to the fraction placeholder
// count (half away from zero); integer digits beyond the placeholder count are
// emitted before the first placeholder; the sign precedes every literal and is
// suppressed when the rounded magnitude is zero; a standard-specifier format
// with trailing junk falls back to custom formatting ("D100" of 12345 ->
// "D112345").
//
// REMAINING OUT OF SUBSET (FormatException, documented divergence): ';'
// section separators. Scientific notation ("0.00E+00"/"0E0"/"0.##e-0") IS
// implemented -- see FormatSciCustom below (a direct port of the net48
// NumberToStringFormat scientific path).
//------------------------------------------------------------------------------
struct CustomSpec
{
    bool grouping;
    bool scientific;         // the pattern contains an E+/E-/e+/e- marker + '0'
    bool fracMandatory[100]; // whether the k-th fraction placeholder is '0'
    int scale;               // decimal shift: +2 per '%', +3 per U+2030 (per-mille), -3 per scaling comma
    int intPh;               // integer '0'/'#' placeholder count
    int fracPh;              // fraction '0'/'#' placeholder count
    // Placeholder-space geometry (net48 NumberToStringFormat): 'digitCount'
    // is the TOTAL 0/# count, 'decimalPos' the placeholder index of the '.'
    // (digitCount when absent), 'firstDigit'/'lastDigit' the index of the
    // first / one-past-the-last '0' placeholder.
    int digitCount;
    int decimalPos;
    int firstDigit;
    int lastDigit;
};

// Parse + validate a custom pattern (the caller already decided this is NOT a
// valid standard specifier). Returns false -> the caller throws FormatException.
bool ParseCustomPattern(const char16_t* p, int32_t len, CustomSpec& spec)
{
    spec = CustomSpec{};
    spec.decimalPos = -1;
    spec.firstDigit = 0x7FFFFFFF;
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
        if (c == u'.')
        {
            if (spec.decimalPos < 0) spec.decimalPos = spec.digitCount;
            inFrac = true;
            continue;
        }
        if (c == u'0' || c == u'#')
        {
            if (c == u'0' && spec.firstDigit == 0x7FFFFFFF) spec.firstDigit = spec.digitCount;
            ++spec.digitCount;
            if (c == u'0') spec.lastDigit = spec.digitCount;
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
            // The net48 scientific marker: E/e followed by '0' (E0, the same
            // as E-0) or by '+'/'-' and then '0'. The marker and its zero run
            // are not digit placeholders.
            int32_t j = i + 1;
            if (j < len && (p[j] == u'+' || p[j] == u'-')) ++j;
            if (j < len && p[j] == u'0')
            {
                spec.scientific = true;
                while (j < len && p[j] == u'0') ++j;
                i = j - 1;
            }
            continue; // a plain 'E'/'e' is a literal
        }
        // any other character is a literal (copied at emission)
    }
    if (spec.decimalPos < 0) spec.decimalPos = spec.digitCount;
    return true;
}

// Emit the pattern over the rounded digits. 'intDigits' (intLen, no sign),
// 'fracDigits' (fracLen == spec.fracPh). Returns the output length. The
// grouping separator schedule follows the integer digit count (every 3 from
// the right), matching the net48 oracle ("0,0" of 12345 -> "12,345").
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

//------------------------------------------------------------------------------
// SCIENTIFIC custom formats ("0.00E+00", "0E0", "0.##e-0", "#,##0.0E+0").
//
// Direct port of the net48 NumberToStringFormat scientific path
// (referencesource mscorlib/system/globalization/numberformatting.cs). The
// value is held as (digits, scale): value == digits * 10^(scale - len), i.e.
// 'scale' is the position of the decimal point inside the digit string. The
// pattern contributes the placeholder geometry from CustomSpec (digitCount /
// decimalPos / firstDigit / lastDigit) and the significant-digit count to
// round to is digitCount (net48: `pos = scientific ? digitCount : ...`).
//
// Pinned by the numexp1 oracle: 0.000123 "0.00E+00" -> "1.23E-04",
// "0.00e+00" -> "1.23e-04", "0.###E+0" -> "1.23E-4", "E" -> "1.230000E-004",
// 1234.5 "##0.0E+0" -> "100.0E-2" (the # placeholders shift the mantissa),
// 0 "##0.0E+0" -> "000.0E+0", "#,##0.0E+0" -> "1,234.5E+0".
//------------------------------------------------------------------------------

// net48 RoundNumber over (digits, scale): keep 'pos' significant digits,
// rounding half away from zero, trim trailing zeros, a front carry bumps the
// scale (999 keep 2 -> digits "10", scale + 1). A zero result clears both.
void RoundDigitsCustom(std::string& digits, int& scale, int pos)
{
    size_t i = 0;
    while (static_cast<int>(i) < pos && i < digits.size())
    {
        ++i;
    }
    if (static_cast<int>(i) == pos && i < digits.size() && digits[i] >= '5')
    {
        while (i > 0 && digits[i - 1] == '9')
        {
            digits[i - 1] = '0';
            --i;
        }
        if (i > 0)
        {
            ++digits[i - 1];
        }
        else
        {
            ++scale;
            digits.assign(1, '1');
            i = 1;
        }
    }
    else
    {
        while (i > 0 && digits[i - 1] == '0')
        {
            --i;
        }
    }
    if (i == 0)
    {
        scale = 0;
        digits.clear();
        return;
    }
    digits.resize(i);
}

int EmitSciCustomFormat(std::string digits, int scale, bool neg,
                        const CustomSpec& spec, const char16_t* pat,
                        int32_t patLen, char16_t* out)
{
    if (!digits.empty())
    {
        scale += spec.scale;
        RoundDigitsCustom(digits, scale, spec.digitCount);
    }
    else
    {
        scale = 0; // zero: net48 resets the scale (no negative zero either)
    }

    int firstDigit = (spec.firstDigit < spec.decimalPos)
        ? spec.decimalPos - spec.firstDigit : 0;
    int lastDigit = (spec.lastDigit > spec.decimalPos)
        ? spec.decimalPos - spec.lastDigit : 0;
    int digPos = spec.decimalPos;

    // Thousands-separator insertion points (group size 3), precomputed so the
    // forward walk can emit them. net48 stores the RUNNING TOTALS (3, 6, 9,
    // ...) and walks them downwards, so the emit test is
    // `digPos == sepPos[sepCtr] + 1`.
    int sepPos[128];
    int sepCtr = -1;
    if (spec.grouping)
    {
        int total = 3;
        const int numDigits = (firstDigit > digPos) ? firstDigit : digPos;
        while (numDigits > total && sepCtr + 1 < 128)
        {
            sepPos[++sepCtr] = total;
            total += 3;
        }
    }

    int o = 0;
    if (neg && !digits.empty())
    {
        out[o++] = u'-';
    }

    bool decimalWritten = false;
    size_t cur = 0;
    bool sciPending = spec.scientific;

    for (int32_t i = 0; i < patLen; ++i)
    {
        const char16_t c = pat[i];
        switch (c)
        {
        case u'#':
        case u'0':
        {
            const char16_t dc = (cur < digits.size())
                ? static_cast<char16_t>(digits[cur++])
                : (digPos > lastDigit ? u'0' : u'\0');
            if (dc != u'\0')
            {
                out[o++] = dc;
                if (spec.grouping && digPos > 1 && sepCtr >= 0 && digPos == sepPos[sepCtr] + 1)
                {
                    out[o++] = u',';
                    --sepCtr;
                }
            }
            --digPos;
            break;
        }
        case u'.':
            if (digPos != 0 || decimalWritten)
            {
                break; // repeated '.' is a literal no-op
            }
            if (lastDigit < 0
                || (spec.decimalPos < spec.digitCount && cur < digits.size()))
            {
                out[o++] = u'.';
                decimalWritten = true;
            }
            break;
        case u'\u2030':
            out[o++] = u'\u2030';
            break;
        case u'%':
            out[o++] = u'%';
            break;
        case u',':
            break; // separators come from the schedule
        case u'\'':
        case u'"':
        {
            const char16_t close = c;
            int32_t q = i + 1;
            while (q < patLen && pat[q] != close)
            {
                if (pat[q] == u'\\' && q + 1 < patLen)
                {
                    out[o++] = pat[q + 1];
                    q += 2;
                    continue;
                }
                out[o++] = pat[q];
                ++q;
            }
            i = q; // skip the closing quote (or run to the end)
            break;
        }
        case u'\\':
            if (i + 1 < patLen)
            {
                out[o++] = pat[++i];
            }
            break;
        case u'E':
        case u'e':
        {
            // Positional marker test (net48): E/e + '0', or E/e + '+'/'-' + '0'.
            int32_t j = i + 1;
            bool positiveSign = false;
            int digitsInExp = 0;
            bool marker = false;
            if (sciPending)
            {
                if (j < patLen && pat[j] == u'0')
                {
                    digitsInExp = 1;
                    marker = true;
                }
                else if (j + 1 < patLen && (pat[j] == u'+' || pat[j] == u'-')
                         && pat[j + 1] == u'0')
                {
                    positiveSign = (pat[j] == u'+');
                    marker = true;
                }
            }
            if (!marker)
            {
                // Literal E/e (and its optional sign / zero run).
                out[o++] = c;
                if (i + 1 < patLen && (pat[i + 1] == u'+' || pat[i + 1] == u'-'))
                {
                    out[o++] = pat[++i];
                }
                while (i + 1 < patLen && pat[i + 1] == u'0')
                {
                    out[o++] = pat[++i];
                }
                break;
            }
            while (++j < patLen && pat[j] == u'0')
            {
                ++digitsInExp;
            }
            if (digitsInExp > 10)
            {
                digitsInExp = 10;
            }
            int exp = digits.empty() ? 0 : scale - spec.decimalPos;
            out[o++] = c;
            if (exp < 0)
            {
                out[o++] = u'-';
                exp = -exp;
            }
            else if (positiveSign)
            {
                out[o++] = u'+';
            }
            char ebuf[12];
            int en = 0;
            if (exp == 0)
            {
                ebuf[en++] = '0';
            }
            else
            {
                while (exp > 0)
                {
                    ebuf[en++] = static_cast<char>('0' + exp % 10);
                    exp /= 10;
                }
            }
            while (en < digitsInExp && en < 11)
            {
                ebuf[en++] = '0';
            }
            for (int k = en; k > 0; --k)
            {
                out[o++] = static_cast<char16_t>(ebuf[k - 1]);
            }
            i = j - 1; // consume the marker's zero run
            sciPending = false;
            break;
        }
        default:
            out[o++] = c;
            break;
        }
    }
    return o;
}

// Exact decimal value = digits[0..len) * 10^scale (scale may be negative),
// rounded to 'fracCount' fraction digits (half away from zero, net48 rule).
// Outputs the rounded integer part ('intOut', 'intN' chars, no sign) and
// exactly 'fracCount' fraction digits ('fracOut'). Used by the int custom
// path: percent/per-mille scale the exact integer digits (no overflow), and
// scaling commas divide them -- net48 oracle: INT64_MAX "0%" ->
// "922337203685477580700%", 123456789 "0,," -> "123".
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

// Compose the fixed currency string: U+00A5 (yen sign) followed by the ASCII tail
// (sign after the symbol: "U+00A5-12,345.00" -- the zh-CN negative pattern
// observed on net48). The UTF-16 units are built directly because
// il2cpp_string_new_from_utf8 DROPS multi-byte sequences (string.cpp M3
// minimal transcoder -- the symbol bytes would never reach the string).
Il2CppObject* MakeCurrencyString(const char* tail, int tailLen)
{
    char16_t units[kMaxBuffer + 8];
    int o = 0;
    units[o++] = kCurrencySymbol;
    for (int i = 0; i < tailLen; ++i)
    {
        units[o++] = static_cast<char16_t>(static_cast<unsigned char>(tail[i]));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(units, o));
}

// cultureprobe (provider routing): the invariant culture is the EMPTY name
// (cultureinfo.cpp il2cpp_culture_get_invariant -> Intern("")); nullptr and
// any non-empty name (incl. "zh-CN") keep the zh-CN host layout, so every
// pre-provider call site stays byte-identical.
bool IsInvariantProvider(Il2CppObject* provider)
{
    if (provider == nullptr) return false;
    const Il2CppString* s = reinterpret_cast<const Il2CppString*>(provider);
    return s->length == 0;
}

// Invariant currency pattern 0: U+00A4 symbol, negatives wrapped in parens
// with the symbol INSIDE ("(U+00A41,234.50)"; net48 invariant).
Il2CppObject* MakeCurrencyStringInvariant(const char* tail, int tailLen)
{
    char16_t units[kMaxBuffer + 8];
    int o = 0;
    const bool neg = (tailLen > 0 && tail[0] == '-');
    if (neg) units[o++] = u'(';
    units[o++] = u'\u00A4';
    for (int i = neg ? 1 : 0; i < tailLen; ++i)
    {
        units[o++] = static_cast<char16_t>(static_cast<unsigned char>(tail[i]));
    }
    if (neg) units[o++] = u')';
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(units, o));
}

//------------------------------------------------------------------------------
// Shared int dispatcher (int32/int64 differ only in the hex width).
//------------------------------------------------------------------------------
Il2CppObject* ToStringInt(int64_t value, uint64_t hexValue, Il2CppObject* format, bool inv)
{
    char spec = 'G';
    int precision = -1;
    if (format != nullptr)
    {
        Il2CppString* fs = reinterpret_cast<Il2CppString*>(format);
        if (!TryParseStandard(fs->chars, fs->length, spec, precision))
        {
            // net48: a non-standard format is a CUSTOM format ("D100" of
            // 12345 -> "D112345", "abc" -> "abc") -- the full engine below.
            CustomSpec cs;
            if (!ParseCustomPattern(fs->chars, fs->length, cs))
            {
                il2cpp_throw_format_exception("Format specifier was invalid.");
            }
            char digits[24];
            const int n = FormatInt64Decimal(digits, value);
            const bool neg = (digits[0] == '-');
            if (cs.scientific)
            {
                // net48 NumberBuffer for an integer: the decimal digits with
                // the scale after the last one.
                std::string sciDigits(digits + (neg ? 1 : 0),
                                      static_cast<size_t>(n - (neg ? 1 : 0)));
                char16_t sciOut[kMaxBuffer + 16];
                const int len = EmitSciCustomFormat(sciDigits, n - (neg ? 1 : 0),
                                                    neg, cs, fs->chars, fs->length,
                                                    sciOut);
                return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(sciOut, len));
            }
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
    }
    const char upper = static_cast<char>(spec & ~0x20);
    const bool lower = (spec >= 'a' && spec <= 'z');
    const char expChar = lower ? 'e' : 'E';
    const char hexBase = lower ? 'a' : 'A';
    char buf[kMaxBuffer];
    int len = 0;
    switch (upper)
    {
        case 'D':
            len = FormatDecimalPadded(buf, value, precision < 0 ? 1 : precision);
            break;
        case 'X':
            len = FormatHex(buf, hexValue, precision < 0 ? 1 : precision, hexBase);
            break;
        case 'N':
            len = FormatNumberInt(buf, value, precision < 0 ? 2 : precision);
            break;
        case 'C':
        {
            char tail[kMaxBuffer];
            const int t = FormatNumberInt(tail, value, precision < 0 ? 2 : precision);
            // saudit4: provider routing -- invariant uses U+00A4 and the
            // "(U+00A4 n)" negative pattern (MakeCurrencyStringInvariant), nullptr /
            // any non-empty name keeps the zh-CN U+00A5 "U+00A5-n" layout.
            return inv ? MakeCurrencyStringInvariant(tail, t) : MakeCurrencyString(tail, t);
        }
        case 'E':
            len = FormatSciInt(buf, value, precision < 0 ? 6 : precision, expChar);
            break;
        case 'F':
            len = FormatFixedInt(buf, value, precision < 0 ? 2 : precision);
            break;
        case 'P':
        {
            const int t0 = FormatPercentInt(buf, value, precision < 0 ? 2 : precision);
            int t = t0;
            if (inv) buf[t++] = ' '; // invariant "n %" (space before '%', saudit4)
            buf[t] = '%';            // zh-CN "n%" layout (no space)
            len = t + 1;
            break;
        }
        case 'G':
            // G without a precision (or G0) is the plain decimal; an explicit
            // precision rounds to significant digits (net48: G2 of 12345 ->
            // "1.2E+04", G15 of long.MaxValue -> "9.22337203685478E+18").
            len = (precision < 1)
                ? FormatInt64Decimal(buf, value)
                : FormatGeneralInt(buf, value, precision, expChar);
            break;
        default:
            // full custom formats on integers are out of subset
            il2cpp_throw_format_exception("Format specifier was invalid.");
    }
    buf[len] = '\0';
    return MakeString(buf);
}

} // namespace

//------------------------------------------------------------------------------
// icall_format_int32_custom -- int32 ToString(format[, provider]).
// provider semantics: see il2cpp_format_ext.h (nullptr/non-empty -> zh-CN,
// invariant name -> invariant C/P; saudit4).
//------------------------------------------------------------------------------
Il2CppObject* icall_format_int32_custom(int32_t value, Il2CppObject* format,
                                        Il2CppObject* provider)
{
    // Hex is 32-bit two's-complement: -123 -> "FFFFFF85" (not 16 digits).
    return ToStringInt(static_cast<int64_t>(value),
                       static_cast<uint64_t>(static_cast<uint32_t>(value)),
                       format, IsInvariantProvider(provider));
}

//------------------------------------------------------------------------------
// icall_format_int64_custom -- int64 ToString(format[, provider]).
//------------------------------------------------------------------------------
Il2CppObject* icall_format_int64_custom(int64_t value, Il2CppObject* format,
                                        Il2CppObject* provider)
{
    return ToStringInt(value, static_cast<uint64_t>(value), format,
                       IsInvariantProvider(provider));
}

//------------------------------------------------------------------------------
// icall_format_narrow_custom -- ToString(format[, provider]) for the narrow
// integer types (Byte/SByte/Int16/UInt16). See il2cpp_format_ext.h: every
// specifier matches the int32 engine on the sign/zero-extended value; only the
// hex bit pattern is masked to the type width (net48: (sbyte)-1 "X" -> "FF",
// (short)-1 "X" -> "FFFF" -- the int32 engine would print FFFFFFFF).
//------------------------------------------------------------------------------
Il2CppObject* icall_format_narrow_custom(int32_t value, Il2CppObject* format,
                                         Il2CppObject* provider, int32_t hexMask)
{
    const uint32_t mask = static_cast<uint32_t>(hexMask);
    return ToStringInt(static_cast<int64_t>(value),
                       static_cast<uint64_t>(static_cast<uint32_t>(value) & mask),
                       format, IsInvariantProvider(provider));
}

//------------------------------------------------------------------------------
// icall_format_double_custom -- double ToString(format[, provider]).
// cultureprobe: 'provider' (the optional 3rd icall param, nullptr for the
// 1-arg ToString(format) shape and the composite-format engine) routes the
// P/C layouts: nullptr/non-empty name -> zh-CN ("n%", U+00A5, "U+00A5-n");
// the invariant name -> "n %" and U+00A4 with "(U+00A4 n)" negatives.
//------------------------------------------------------------------------------
Il2CppObject* icall_format_double_custom(double value, Il2CppObject* format,
                                         Il2CppObject* provider)
{
    const bool inv = IsInvariantProvider(provider);
    // saudit1: non-finite values take the PROVIDER's spelling (oracle:
    // ToString("G", InvariantCulture) == "Infinity").
    const auto special = [inv](double v) -> Il2CppObject*
    {
        return inv ? MakeSpecialStringInvariant(v) : MakeSpecialString(v);
    };
    // net48: non-finite values return the culture symbol BEFORE any format
    // work -- even an invalid format specifier (probe9: NaN with "Q" ->
    // "NaN", +Inf with "Q" -> U+221E).
    if (IsSpecial(value))
    {
        return special(value);
    }
    char buf[kMaxBuffer + 8];
    char spec = 'G';
    int precision = -1;
    if (format != nullptr)
    {
        Il2CppString* fs = reinterpret_cast<Il2CppString*>(format);
        if (fs->length == 0)
        {
            spec = 'G';
            precision = 0;
        }
        else if (!TryParseStandard(fs->chars, fs->length, spec, precision))
        {
            // net48: a non-standard format is a CUSTOM format -- the full
            // engine (oracle: "0.00" -> "12.34%", "#.##" -> ".5", "D100" ->
            // "D112345", "0.00.0" -> "1.500", "abc" -> "abc").
            CustomSpec cs;
            if (!ParseCustomPattern(fs->chars, fs->length, cs))
            {
                il2cpp_throw_format_exception("Format specifier was invalid.");
            }
            double scaled = value;
            if (cs.scientific)
            {
                // net48 NumberBuffer for a double: the 15 significant digits
                // (the custom-format precision) with the scale after the last
                // one -- value == digits * 10^(scale - len). The pattern's
                // '%'/per-mille/scaling-comma shift is applied by the emitter
                // (spec.scale), NOT here (scaling twice would square it).
                DoubleDigits dg;
                GetTieBaseDigits(value, dg, kBaseDigits);
                std::string sciDigits(dg.d, kBaseDigits);
                char16_t sciOut[kMaxBuffer + 16];
                const int len = EmitSciCustomFormat(sciDigits, dg.exp10 + 1,
                                                    dg.neg, cs, fs->chars,
                                                    fs->length, sciOut);
                return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(sciOut, len));
            }
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
            const int fn = FormatFixed(scaled, cs.fracPh, fixedBuf, kBaseDigits);
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
    }
    const char upper = static_cast<char>(spec & ~0x20);
    const bool lower = (spec >= 'a' && spec <= 'z');
    const char expChar = lower ? 'e' : 'E';
    int len = 0;
    switch (upper)
    {
        case 'G':
        {
            if (IsSpecial(value))
            {
                return special(value);
            }
            if (precision < 1)
            {
                // net48 default double text (G15) -- single source, format.cpp
                Il2CppString* s = icall_double_to_string(value);
                return reinterpret_cast<Il2CppObject*>(s);
            }
            len = FormatGeneralDouble(value, precision, expChar, buf);
            break;
        }
        case 'F':
            if (IsSpecial(value))
            {
                return special(value);
            }
            len = FormatFinite(value, precision < 0 ? 2 : precision, buf, kBaseDigits);
            break;
        case 'N':
            if (IsSpecial(value))
            {
                return special(value);
            }
            len = FormatGroupedFinite(value, precision < 0 ? 2 : precision, buf, kBaseDigits);
            break;
        case 'E':
        {
            if (IsSpecial(value))
            {
                return special(value);
            }
            // net48's E needs prec+1 significant digits, so above the
            // 15-digit default base it widens (17 max; extra fraction
            // digits are zero padding). Oracle (tiebreak2): E15 of
            // 1234567890123445.0 -> "1.234567890123445E+015" (16 digits),
            // E16 -> "...4450E+015" (17), E17 -> "...44500E+015" (padded);
            // pi E15 -> "3.141592653589793E+000" (the 15-digit base could
            // only give "...790"). F/N/C/P stay on the 15-digit base (pi F15
            // -> "3.141592653589790", net48-verified).
            // The widened base is the FULL 17-digit one, not prec+1: net48
            // rounds its 17-digit buffer down to prec+1 digits, so E15 rounds
            // 17 -> 16 digits and the double rounding is observable
            // (tiebreak2 randomized battery: 1.467306437335953... -> net48
            // "1.467306437335954E+057"). E14 and below stay on the 15-digit
            // base (byte-identical to the pre-tiebreak2 text).
            const int prec = (precision < 0) ? 6 : precision;
            const int eBase = (prec + 1 > kBaseDigits) ? kWideDigits : kBaseDigits;
            len = FormatSciFinite(value, prec, buf, expChar, eBase);
            break;
        }
        case 'C':
        {
            if (IsSpecial(value))
            {
                return special(value);
            }
            char tail[kMaxBuffer];
            const int t = FormatGroupedFinite(value, precision < 0 ? 2 : precision, tail, kBaseDigits);
            return inv ? MakeCurrencyStringInvariant(tail, t) : MakeCurrencyString(tail, t);
        }
        case 'P':
        {
            if (IsSpecial(value))
            {
                return special(value);
            }
            // net48's P shifts the DIGITS' decimal scale by 2
            // (NumberToString: `case 'P': number.Scale += 2`), it does NOT
            // multiply the value: value*100.0 rounds in double, and at a tie
            // that rounding is observable (tiebreak2 oracle: P2 of
            // 1234567890123445.0 -> "123,456,789,012,345,000.00"; the
            // product double is 123456789012344496 -> "...344,000.00").
            len = FormatGroupedFinite(value, precision < 0 ? 2 : precision, buf,
                                      kBaseDigits, 2);
            if (inv) buf[len++] = ' '; // invariant "n %" (space before '%')
            buf[len++] = '%'; // zh-CN "n%" layout (no space)
            break;
        }
        case 'R':
        {
            // D-DBLF-1 (dblfmt_r): net48 R = G15 when it round-trips, else
            // G17 (Number.FormatDouble's precision ladder). Specials were
            // already routed through special() above.
            len = FormatGeneralDouble(value, 15, expChar, buf);
            buf[len] = '\0';
            if (std::strtod(buf, nullptr) == value)
            {
                break;
            }
            len = FormatGeneralDouble(value, 17, expChar, buf);
            break;
        }
        default:
            il2cpp_throw_format_exception("Format specifier was invalid.");
    }
    buf[len] = '\0';
    return MakeString(buf);
}

//------------------------------------------------------------------------------
// icall_format_composite_item -- format ONE composite-format argument (the
// "{N[,width][:fmt]}" item of String.Format / StringBuilder.AppendFormat --
// see il2cpp_format_ext.h for the full dispatch/alignment semantics).
//
// The composite parser (AppendFormatCore, stringbuilder.cpp) extracts the
// index, alignment and format-string components and calls this engine with
// the BOXED argument; the box is opened here by klass name (the numeric box
// payloads sit at byte offset 8 -- the valuesemantics.cpp contract).
// 'provider' (saudit4) is the IFormatProvider of the enclosing String.Format
// call: it is threaded to the numeric custom formatters so the invariant
// culture is honored inside composite items too; nullptr keeps the zh-CN
// host layout.
//------------------------------------------------------------------------------
Il2CppObject* icall_format_composite_item(Il2CppObject* boxed, Il2CppObject* format, int32_t alignment,
                                          Il2CppObject* provider)
{
    // net48 composite formatting: null -> the empty string; a string arg
    // returns its payload unchanged (System.String is not IFormattable, so a
    // specifier is ignored); boxed numerics honor the specifier; every other
    // object prints its plain ToString() text (specifier ignored).
    Il2CppString* text = nullptr;
    if (boxed == nullptr)
    {
        text = il2cpp_string_new(nullptr, 0); // the empty string
    }
    else
    {
        const char* name = (boxed->klass != nullptr) ? boxed->klass->name : nullptr;
        if (name != nullptr && std::strcmp(name, "System.String") == 0)
        {
            text = reinterpret_cast<Il2CppString*>(boxed); // identity, specifier ignored
        }
        else if (name != nullptr && std::strcmp(name, "System.Int32") == 0)
        {
            const int32_t v = *reinterpret_cast<const int32_t*>(
                reinterpret_cast<const char*>(boxed) + 8);
            text = reinterpret_cast<Il2CppString*>(icall_format_int32_custom(v, format, provider));
        }
        else if (name != nullptr && std::strcmp(name, "System.Int64") == 0)
        {
            const int64_t v = *reinterpret_cast<const int64_t*>(
                reinterpret_cast<const char*>(boxed) + 8);
            text = reinterpret_cast<Il2CppString*>(icall_format_int64_custom(v, format, provider));
        }
        else if (name != nullptr && std::strcmp(name, "System.UInt32") == 0)
        {
            // Board gn20-numfmt: a boxed UInt32 routes through the UNSIGNED
            // 32-bit value (bit pattern in the int32 slot); the signed int32
            // engine would render values >= 2^31 as negatives. net48 formats
            // uint through the unsigned uint32 engine; the shared int64 engine
            // renders the full 0..2^32-1 range exactly (zero-extended).
            const uint64_t u = static_cast<uint64_t>(static_cast<uint32_t>(
                *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(boxed) + 8)));
            text = reinterpret_cast<Il2CppString*>(icall_format_int64_custom(static_cast<int64_t>(u), format, provider));
        }
        else if (name != nullptr && std::strcmp(name, "System.UInt64") == 0)
        {
            // Board gn20-numfmt: a boxed UInt64 carries the full 8-byte payload.
            // Values <= INT64_MAX route through the signed int64 engine exactly;
            // values > INT64_MAX stay at the documented subset boundary (the
            // signed engine cannot render them -- MethodMap.Format.cs).
            const uint64_t u = *reinterpret_cast<const uint64_t*>(
                reinterpret_cast<const char*>(boxed) + 8);
            if (u <= static_cast<uint64_t>(INT64_MAX))
            {
                text = reinterpret_cast<Il2CppString*>(icall_format_int64_custom(static_cast<int64_t>(u), format, provider));
            }
            else
            {
                // Out of the signed subset: the plain decimal (valuesemantics
                // UInt64ToString -- the pre-existing default path) -- the
                // documented boundary (the unsigned full-range format engine is
                // future work; the large ulong renders as raw "G" text).
                text = icall_object_to_string(boxed);
            }
        }
        else if (name != nullptr && std::strcmp(name, "System.Double") == 0)
        {
            const double v = *reinterpret_cast<const double*>(
                reinterpret_cast<const char*>(boxed) + 8);
            text = reinterpret_cast<Il2CppString*>(icall_format_double_custom(v, format, provider));
        }
        else if (name != nullptr && std::strcmp(name, "System.Single") == 0)
        {
            // singlefmt: a boxed Single rides the SAME float-width engine as
            // Single.ToString(format) (net48 AppendFormat -> IFormattable ->
            // Number.FormatSingle: the 7/9-digit float base). The box payload
            // is the exact float widening (boxingext2.h contract).
            const double v = *reinterpret_cast<const double*>(
                reinterpret_cast<const char*>(boxed) + 8);
            text = reinterpret_cast<Il2CppString*>(icall_format_float_custom(v, format, provider));
        }
        else if (name != nullptr && std::strcmp(name, "System.Decimal") == 0)
        {
            // 16-byte decimal payload at byte offset 8 (the decimal box).
            Il2CppDecimal dec;
            std::memcpy(&dec, reinterpret_cast<const char*>(boxed) + 8, sizeof(dec));
            text = icall_decimal_to_string_custom(&dec, reinterpret_cast<Il2CppString*>(format), provider);
        }
        else if (name != nullptr &&
                 (std::strcmp(name, "System.Byte") == 0 || std::strcmp(name, "System.SByte") == 0 ||
                  std::strcmp(name, "System.Int16") == 0 || std::strcmp(name, "System.UInt16") == 0))
        {
            // P0-narrow (2026-10-22): the narrow integer boxes carry the masked
            // int32 payload (boxingext2.cpp); net48's composite formatting
            // applies the SAME narrow-width rule as the direct ToString(format)
            // overload (fmtprobe: "{0:X}" of (sbyte)-1 -> "FF", of (short)-1 ->
            // "FFFF", "{0:D5}" of (byte)5 -> "00005"). Sign/zero-extend by the
            // box's OWN klass, then run the narrow engine.
            const int32_t raw = *reinterpret_cast<const int32_t*>(
                reinterpret_cast<const char*>(boxed) + 8);
            const bool wide16 = (std::strcmp(name, "System.Int16") == 0 ||
                                 std::strcmp(name, "System.UInt16") == 0);
            int32_t v;
            if (std::strcmp(name, "System.SByte") == 0)
                v = static_cast<int32_t>(static_cast<int8_t>(raw));
            else if (std::strcmp(name, "System.Int16") == 0)
                v = static_cast<int32_t>(static_cast<int16_t>(raw));
            else
                v = raw & (wide16 ? 0xFFFF : 0xFF);
            text = reinterpret_cast<Il2CppString*>(
                icall_format_narrow_custom(v, format, provider, wide16 ? 0xFFFF : 0xFF));
        }
        else if (name != nullptr && std::strcmp(name, "System.DateTime") == 0)
        {
            // net48 composite formatting applies the format specifier through
            // IFormattable (DateTime.ToString(format, provider)); the plain
            // ToString is the no-format path. The box payload sits at byte
            // offset 8 (the valuesemantics.cpp box contract).
            Il2CppDateTime v;
            std::memcpy(&v, reinterpret_cast<const char*>(boxed) + 8, sizeof(v));
            // iformatb: a null format is IFormattable.ToString(null, provider)
            // -- the PROVIDER-selected General "G" pattern (net48 oracle:
            // null + InvariantCulture -> "02/29/2024 13:45:06"; the old
            // icall_datetime_to_string(v) hard-coded the zh-CN pattern and
            // dropped the provider). icall_datetime_to_string_format handles
            // the null/empty-format General arm itself.
            text = reinterpret_cast<Il2CppString*>(
                icall_datetime_to_string_format(v, format, provider));
        }
        else if (name != nullptr && std::strcmp(name, "System.Guid") == 0)
        {
            // Guid.ToString(format): the format specifier is honored (net48).
            const uint8_t* bytes = reinterpret_cast<const uint8_t*>(boxed) + 8;
            text = reinterpret_cast<Il2CppString*>(
                format != nullptr
                    ? il2cpp_guid_to_string_format(bytes, reinterpret_cast<Il2CppString*>(format))
                    : il2cpp_guid_to_string(bytes));
        }
        else if (name != nullptr && std::strcmp(name, "System.TimeSpan") == 0)
        {
            // net48 TimeSpan implements IFormattable; ToString(format,
            // provider) is the dedicated c/g/G/t/T/custom formatter (the
            // provider is accepted and ignored -- every TimeSpan layout is
            // culture-invariant). A null format is the "c" default. The box
            // payload sits at byte offset 8 (the valuesemantics.cpp box
            // contract), same as the DateTime arm above.
            Il2CppTimeSpan v;
            std::memcpy(&v, reinterpret_cast<const char*>(boxed) + 8, sizeof(v));
            text = reinterpret_cast<Il2CppString*>(
                icall_timespan_to_string_format(v, format));
        }
        else if (name != nullptr && std::strcmp(name, "System.DateTimeOffset") == 0)
        {
            // net48 DateTimeOffset implements IFormattable. A null/empty
            // format is the provider-selected General pattern PLUS " zzz";
            // a standard/custom specifier runs the dedicated formatter. The
            // 16-byte Il2CppDateTimeOffset image sits at byte offset 8
            // (nullable.cpp icall_box_date_time_offset).
            Il2CppDateTimeOffset v;
            std::memcpy(&v, reinterpret_cast<const char*>(boxed) + 8, sizeof(v));
            text = reinterpret_cast<Il2CppString*>(
                icall_dtoffset_to_string_format(v, format, provider));
        }
        else if (il2cpp_enum_is_registered(boxed->klass))
        {
            // B7 (AppendFormatted<T> over an enum): net48's composite formatting
            // routes a boxed enum through IFormattable.ToString(format, null),
            // so the specifier IS honored ("G" -> the member name, "D" -> the
            // decimal value, "F" -> the flags decomposition, "X" -> hex). A null
            // format is the "G" default (il2cpp_enum_to_string_format).
            text = reinterpret_cast<Il2CppString*>(
                il2cpp_enum_boxed_to_string_format(boxed, format));
        }
        else
        {
            // bool/char/other objects: not IFormattable -> plain ToString.
            text = icall_object_to_string(boxed);
        }
    }

    // Alignment: pad with spaces to the MINIMUM field width (net48: a longer
    // formatted text is never truncated). Positive width right-aligns (spaces
    // on the left), negative left-aligns.
    const int32_t width = (alignment < 0) ? -alignment : alignment;
    if (width == 0 || text->length >= width)
    {
        return reinterpret_cast<Il2CppObject*>(text);
    }
    const int32_t pad = width - text->length;
    const bool rightAlign = (alignment > 0);
    char16_t* buf = static_cast<char16_t*>(std::malloc(static_cast<size_t>(width) * sizeof(char16_t)));
    if (buf == nullptr)
    {
        il2cpp_throw_new("System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                         "Insufficient memory to continue the execution of the program.");
    }
    if (rightAlign)
    {
        for (int32_t k = 0; k < pad; ++k)
        {
            buf[k] = u' ';
        }
        std::memcpy(buf + pad, text->chars, static_cast<size_t>(text->length) * sizeof(char16_t));
    }
    else
    {
        std::memcpy(buf, text->chars, static_cast<size_t>(text->length) * sizeof(char16_t));
        for (int32_t k = 0; k < pad; ++k)
        {
            buf[text->length + k] = u' ';
        }
    }
    Il2CppObject* result = reinterpret_cast<Il2CppObject*>(il2cpp_string_new(buf, width));
    std::free(buf);
    return result;
}

//------------------------------------------------------------------------------
// icall_format_iface_tostring -- IFormattable.ToString(format, provider) on a
// boxed primitive receiver. See il2cpp_format_ext.h: the composite formatter
// with alignment 0 is the exact net48 semantics (including the narrow-integer
// hex width rule), but net48 throws NullReferenceException for a null
// receiver while the composite formatter renders the empty string.
//------------------------------------------------------------------------------
Il2CppObject* icall_format_iface_tostring(Il2CppObject* boxed, Il2CppObject* format,
                                          Il2CppObject* provider)
{
    if (boxed == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    return icall_format_composite_item(boxed, format, 0, provider);
}

//------------------------------------------------------------------------------
// floatfix/singlefmt: net48 Single.ToString(format[, provider]).
//
// "R" is the two-tier FLOAT round-trip (floatfix, unchanged below). Every
// OTHER format runs the FLOAT-width digit base -- net48's native
// Number.FormatSingle converts the widened double at a 7-significant-digit
// base (9 for wide E/G), NOT the double engine's 15-digit base. Oracle
// (probe-singlefmt, net48 host capture 2026-10-29):
//   * F/N/C/P/custom: base 7 -- 0.1f "F10" -> "0.1000000000" (the double
//     engine's 15-digit base would print "0.1000000015"); 16777216f "F" ->
//     "16777220.00".
//   * E: base 7 for precision <= 6, base 9 for precision > 6 -- 0.1f "E9" ->
//     "1.000000010E-001" (double: "1.000000015E-001").
//   * G: base 7 for precision <= 7, base 9 for precision > 7 -- 0.1f "G10" ->
//     "0.100000001" (double: "0.1000000015"); "G99" stays the 9-digit base.
//   * specials: NaN / +-Infinity return the PROVIDER spelling BEFORE any
//     format work (probe: InvariantCulture "R" of +Inf -> "Infinity"; the
//     default zh-CN culture -> U+221E), like the double engine.
// The non-finite/finite split and the rounding machinery are the double
// engine's, re-invoked at the float base width (kBaseDigits stays the
// double face; the float face passes 7/9).
//------------------------------------------------------------------------------
namespace {
Il2CppObject* FormatSingleR(double value)
{
    const float f = static_cast<float>(value);
    // net48 R of a zero magnitude drops the sign (oracle: -0.0f "R"/"r" ->
    // "0"); the G7/G9 ladder would print "-0".
    if (f == 0.0f)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8("0"));
    }
    char buf[kMaxBuffer];
    // net48 R (two tiers): G7 first; parse the digits back and compare the
    // FLOAT bits -- a mismatch falls back to G9 (nine digits always
    // round-trip an IEEE float). Oracle: 0.1f -> "0.1", 0.66666668653f ->
    // "0.6666667", 0.33333334327f -> "0.333333343", 1.00000011921f ->
    // "1.00000012".
    // tiebreak: both tiers run the FLOAT digit base, so an exact tie rounds
    // away from zero here too -- the CRT's half-to-even G7/G9 text differs on
    // 12345645f (G7 "1.234564E+07") and on the base-9 ties (1234567.625f
    // G9 "1234567.62"; oracle "1234567.63").
    {
        DoubleDigits dg;
        GetTieBaseDigits(static_cast<double>(f), dg, kFloatBaseDigits);
        int len = FormatGeneralFromDigits(dg.d, kFloatBaseDigits, dg.exp10, dg.neg,
                                          7, 'E', buf);
        buf[len] = '\0';
    }
    if (std::strtof(buf, nullptr) != f)
    {
        DoubleDigits dg;
        GetTieBaseDigits(static_cast<double>(f), dg, 9);
        int len = FormatGeneralFromDigits(dg.d, 9, dg.exp10, dg.neg, 9, 'E', buf);
        buf[len] = '\0';
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(buf));
}

// The net48 float digit base per standard specifier (oracle probe-singlefmt):
// F/N/C/P/custom always 7; E widens to 9 above precision 6; G above 7.
int FloatBaseDigits(char upper, int precision)
{
    if (upper == 'E')
    {
        return (precision > 6) ? 9 : 7;
    }
    if (upper == 'G')
    {
        return (precision > 7) ? 9 : 7;
    }
    return 7;
}
} // namespace

// tiebreak2: the net48 DEFAULT double text (see il2cpp_format_ext.h). The
// finite-value G15 body, shared by icall_double_to_string (format.cpp) and
// console_format_double (console.cpp) so both ride ONE tie-corrected base.
void format_double_default(double value, char* out, int capacity)
{
    if (out == nullptr || capacity <= 0)
    {
        return;
    }
    char buf[kMaxBuffer];
    const int len = FormatGeneralDouble(value, kBaseDigits, 'E', buf);
    const int n = (len < capacity - 1) ? len : (capacity - 1);
    std::memcpy(out, buf, static_cast<size_t>(n));
    out[n] = '\0';
}

Il2CppObject* icall_format_float_custom(double value, Il2CppObject* format,
                                        Il2CppObject* provider)
{
    const bool inv = IsInvariantProvider(provider);
    // net48: non-finite values return the PROVIDER's spelling BEFORE any
    // format work -- even an invalid format specifier ("NaN"/"Infinity" on
    // InvariantCulture; the zh-CN host renders U+221E/-U+221E). The carrier
    // is the exact float widening, so the double status IS the float status.
    if (IsSpecial(value))
    {
        return inv ? MakeSpecialStringInvariant(value) : MakeSpecialString(value);
    }

    char buf[kMaxBuffer + 8];
    char spec = 'G';
    int precision = -1;
    if (format != nullptr)
    {
        Il2CppString* fs = reinterpret_cast<Il2CppString*>(format);
        if (fs->length == 0)
        {
            spec = 'G';
            precision = 0;
        }
        else if (!TryParseStandard(fs->chars, fs->length, spec, precision))
        {
            // net48: a non-standard format is a CUSTOM format over the
            // 7-digit float base (oracle: 0.1f "0.0000000000" ->
            // "0.1000000000"; custom scientific "0.00000000E+00" of pi ->
            // "3.14159300E+00" -- base 7, too).
            CustomSpec cs;
            if (!ParseCustomPattern(fs->chars, fs->length, cs))
            {
                il2cpp_throw_format_exception("Format specifier was invalid.");
            }
            double scaled = value;
            if (cs.scientific)
            {
                DoubleDigits dg;
                GetTieBaseDigits(value, dg, kFloatBaseDigits);
                std::string sciDigits(dg.d, kFloatBaseDigits);
                char16_t sciOut[kMaxBuffer + 16];
                const int len = EmitSciCustomFormat(sciDigits, dg.exp10 + 1,
                                                    dg.neg, cs, fs->chars,
                                                    fs->length, sciOut);
                return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(sciOut, len));
            }
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
            const int fn = FormatFixed(scaled, cs.fracPh, fixedBuf, kFloatBaseDigits);
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
    }
    const char upper = static_cast<char>(spec & ~0x20);
    const bool lower = (spec >= 'a' && spec <= 'z');
    const char expChar = lower ? 'e' : 'E';
    int len = 0;
    switch (upper)
    {
        case 'G':
        {
            // net48 float G: the 7-digit base (9 above precision 7); NO
            // explicit precision is G7 semantics ("G"/"G0"/""/null all
            // render the 7-digit base -- oracle: 16777216f "G" ->
            // "1.677722E+07"). No wide-base branch: the float base caps at
            // 9 digits ("G99" -> the 9-digit base text).
            const int baseDigits = FloatBaseDigits('G', precision);
            const int sigDigits = (precision < 1) ? 7 : precision;
            DoubleDigits dg;
            GetTieBaseDigits(value, dg, baseDigits);
            len = FormatGeneralFromDigits(dg.d, baseDigits, dg.exp10, dg.neg,
                                          sigDigits, expChar, buf);
            break;
        }
        case 'F':
            len = FormatFinite(value, precision < 0 ? 2 : precision, buf,
                               FloatBaseDigits('F', precision));
            break;
        case 'N':
            len = FormatGroupedFinite(value, precision < 0 ? 2 : precision, buf,
                                      FloatBaseDigits('N', precision));
            break;
        case 'E':
        {
            const int prec = (precision < 0) ? 6 : precision;
            len = FormatSciFinite(value, prec, buf, expChar,
                                  FloatBaseDigits('E', prec));
            break;
        }
        case 'C':
        {
            char tail[kMaxBuffer];
            const int t = FormatGroupedFinite(value, precision < 0 ? 2 : precision, tail,
                                              kFloatBaseDigits);
            return inv ? MakeCurrencyStringInvariant(tail, t) : MakeCurrencyString(tail, t);
        }
        case 'P':
        {
            // net48's P shifts the DIGITS' decimal scale by 2 (the same
            // `number.Scale += 2` the double engine's P path uses). For a
            // float the value*100.0 product is exact in the double carrier,
            // so this is byte-identical to the previous multiply -- it just
            // keeps one P rule for both widths.
            len = FormatGroupedFinite(value, precision < 0 ? 2 : precision, buf,
                                      kFloatBaseDigits, 2);
            if (inv) buf[len++] = ' '; // invariant "n %" (space before '%')
            buf[len++] = '%'; // zh-CN "n%" layout (no space)
            break;
        }
        case 'R':
        {
            // The two-tier float round-trip (floatfix); specials were routed
            // above with the provider spelling.
            return FormatSingleR(value);
        }
        default:
            il2cpp_throw_format_exception("Format specifier was invalid.");
    }
    buf[len] = '\0';
    return MakeString(buf);
}

}} // namespace il2cpp::runtime
