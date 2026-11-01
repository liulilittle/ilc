//------------------------------------------------------------------------------
// format.cpp -- .NET-style numeric formatting icalls (see il2cpp_format.h).
//
// Implementation notes:
//   * Integer paths use a manual digit loop (no std::to_chars -- the GCC 7.5
//     compatibility baseline has no <charconv>; no snprintf -- fully
//     deterministic and locale-independent).
//   * The double path produces net48's DEFAULT "G15" output (15 significant
//     digits). The correctly-rounded digits come from snprintf("%.15g")
//     (15 significant digits; C's fixed/scientific boundary -- fixed for a
//     decimal exponent in [-4, 14] -- matches .NET's empirically, verified
//     against a net48 reference run); only the C-isms are adapted: the
//     lowercase 'e' exponent marker -> 'E', and the specials (-0.0 -> "0",
//     NaN/+-Infinity spellings).
//   * The DOUBLE parse entries delegate to parse_styles.cpp (the shared
//     net48 Number.TryParseNumber port), so the group/decimal separators come
//     from the provider's NumberFormatInfo and AllowThousands is ON by
//     default -- double.Parse("1,000") == 1000. The INTEGER parsers in this
//     file keep the ASCII whitespace set and reject group separators, which
//     is what net48 int.Parse does in every culture.
//------------------------------------------------------------------------------

#include "il2cpp_format.h"
#include "il2cpp_format_ext.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_parse_styles.h" // double.Parse default-provider core (shared grammar)

#include <cerrno>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <memory>

namespace il2cpp { namespace runtime {

namespace {

// Buffer for any formatted value (largest case: fixed-point double, 31 chars
// + NUL; int64 decimal, 21 chars + NUL; hex, 8 chars + NUL).
const int kFormatBufferSize = 48;

// Format 'value' as a decimal string (no sign for non-negative) into 'out'
// (which must hold at least 24 chars). Returns the character count; the
// buffer is NOT NUL-terminated. INT64_MIN is handled without UB (the value
// is negated as -(v + 1) + 1 in unsigned arithmetic).
int FormatInt64Decimal(char* out, int64_t value)
{
    if (value == 0)
    {
        out[0] = '0';
        return 1;
    }
    const bool negative = value < 0;
    // Negation without UB: -(INT64_MIN) is not representable, so negate as
    // -(value + 1) + 1 in unsigned arithmetic (exact for every int64 value).
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

// Format 'value' (unsigned 64-bit) as a full-range decimal string into 'out'
// (must hold at least 24 chars). Returns the character count; the buffer is
// NOT NUL-terminated. Decimal-domain C44 (decgap): uint32/uint64 ToString
// needs the UNSIGNED digit loop -- feeding the raw bit pattern into
// FormatInt64Decimal would print UInt64.MaxValue as "-1".
int FormatU64Decimal(char* out, uint64_t value)
{
    if (value == 0)
    {
        out[0] = '0';
        return 1;
    }
    char tmp[21];
    int n = 0;
    do
    {
        tmp[n++] = static_cast<char>('0' + static_cast<int>(value % 10ULL));
        value /= 10ULL;
    } while (value != 0ULL);
    int len = 0;
    while (n > 0)
    {
        out[len++] = tmp[--n];
    }
    return len;
}

// Format 'value' (unsigned 32-bit) as uppercase hex into 'out' (must hold at
// least 9 chars). Returns the character count; NOT NUL-terminated.
int FormatU32Hex(char* out, uint32_t value)
{
    if (value == 0)
    {
        out[0] = '0';
        return 1;
    }
    char tmp[9];
    int n = 0;
    while (value != 0)
    {
        const uint32_t digit = value & 0x0F;
        tmp[n++] = static_cast<char>(digit < 10 ? ('0' + digit) : ('A' + digit - 10));
        value >>= 4;
    }
    int len = 0;
    while (n > 0)
    {
        out[len++] = tmp[--n];
    }
    return len;
}

// net48 default double.ToString ("G15"): 15 significant digits with .NET's
// layout rules. The finite text comes from the SHARED double G15 engine
// (format_double_default -> formatext FormatGeneralDouble over
// GetTieBaseDigits), whose base is the same correctly rounded 15-digit
// conversion the previous snprintf("%.15g") used -- with net48's half AWAY
// FROM ZERO tie rule instead of the CRT's half to even (tiebreak2 oracle:
// 1234567890123445.0 -> "1.23456789012345E+15"). The layout rule is .NET's
// (fixed iff -5 < exponent < 15), EMPIRICALLY VERIFIED against net48:
//   1e-5  -> "1E-05" (scientific),  1e14 -> "100000000000000" (fixed),
//   1e15  -> "1E+15" (scientific),  0.1+0.2 -> "0.3" (net48 G15, no
//   round-trip fallback -- .NET Core 3.0+ differs).
// The engine already spells the exponent with 'E' and signs / zero-pads it to
// 2 digits, and drops the -0.0 sign. NaN/+-Infinity -> .NET words: NaN as
// "NaN", infinities as the culture symbol U+221E/-U+221E (U+221E, UTF-8
// E2 88 9E -- the .NET Framework invariant-culture infinity symbol, net48
// zh-CN verified, same spelling as console.cpp's console_format_double).
// 'out' must hold kFormatBufferSize chars; always NUL-terminates.
void FormatDoubleG15(double value, char* out)
{
    // Specials first: NaN and the infinities have fixed .NET spellings;
    // -0.0 prints "0" (the sign of zero is not rendered by .NET).
    if (std::isnan(value))
    {
        std::memcpy(out, "NaN", 4); // includes NUL
        return;
    }
    if (std::isinf(value))
    {
        // net48 (zh-CN host, empirical): infinities render as the culture
        // symbol U+221E (infinity) (UTF-8 bytes E2 88 9E) with a leading '-'
        // for the negative form; the UTF-8 bytes are decoded downstream
        // by il2cpp_string_new_from_utf8 (il2cpp_string_new_from_cs would
        // skip the >= 0x80 bytes).
        const char* inf = std::signbit(value) ? "-\xE2\x88\x9E" : "\xE2\x88\x9E";
        std::memcpy(out, inf, std::strlen(inf) + 1); // includes NUL
        return;
    }
    if (value == 0.0)
    {
        out[0] = '0';
        out[1] = '\0';
        return;
    }

    // The shared G15 engine (see the header comment): tie-corrected digit
    // base, .NET layout, uppercase exponent.
    il2cpp::runtime::format_double_default(value, out, kFormatBufferSize);
}

// .NET Parse whitespace for the INTEGER/BOOL parsers below: the ASCII set
// (0x09-0x0D and 0x20). net48's int.Parse DOES accept only these in
// practice for the values this runtime pins -- int.Parse rejects group
// separators and the Unicode spaces in every culture, so the integer half
// needs no culture plumbing.
bool IsAsciiWhitespace(char16_t c)
{
    return c == u' ' || (c >= u'\t' && c <= u'\r');
}

} // namespace

//------------------------------------------------------------------------------
// icall_int32_to_string / icall_int64_to_string -- decimal formatting.
//------------------------------------------------------------------------------
Il2CppString* icall_int32_to_string(int32_t value)
{
    char buf[kFormatBufferSize];
    const int len = FormatInt64Decimal(buf, static_cast<int64_t>(value));
    buf[len] = '\0';
    return il2cpp_string_new_from_cs(buf);
}

Il2CppString* icall_int64_to_string(int64_t value)
{
    char buf[kFormatBufferSize];
    const int len = FormatInt64Decimal(buf, value);
    buf[len] = '\0';
    return il2cpp_string_new_from_cs(buf);
}

// Decimal-domain C44 (decgap): uint32/uint64 ToString -- full-range unsigned
// formatting (UInt32.MaxValue prints "4294967295", never the signed -1;
// UInt64.MaxValue prints "18446744073709551615"). The value lives in a
// 4/8-byte signed slot (int32_t/int64_t); the bit pattern passes through to
// the unsigned parameter (implicit conversion is modulo, bit-identical).
Il2CppString* icall_uint32_to_string(uint32_t value)
{
    char buf[kFormatBufferSize];
    const int len = FormatU64Decimal(buf, static_cast<uint64_t>(value));
    buf[len] = '\0';
    return il2cpp_string_new_from_cs(buf);
}

Il2CppString* icall_uint64_to_string(uint64_t value)
{
    char buf[kFormatBufferSize];
    const int len = FormatU64Decimal(buf, value);
    buf[len] = '\0';
    return il2cpp_string_new_from_cs(buf);
}

//------------------------------------------------------------------------------
// icall_bool_to_string -- "True" / "False" (capitalized, .NET semantics).
//------------------------------------------------------------------------------
Il2CppString* icall_bool_to_string(bool value)
{
    return il2cpp_string_new_from_cs(value ? "True" : "False");
}

//------------------------------------------------------------------------------
// icall_char_to_string -- one UTF-16 code unit as a 1-unit string (unit 0
// yields a string containing NUL; il2cpp_string_new handles NUL payloads).
//------------------------------------------------------------------------------
Il2CppString* icall_char_to_string(int32_t utf16)
{
    const char16_t unit = static_cast<char16_t>(utf16 & 0xFFFF);
    return il2cpp_string_new(&unit, 1);
}

//------------------------------------------------------------------------------
// icall_int32_to_string_hex -- uppercase hex, no "0x", no padding; negative
// values print the full 8-digit twos-complement ("FFFFFFFF" for -1).
//------------------------------------------------------------------------------
Il2CppString* icall_int32_to_string_hex(int32_t value)
{
    char buf[kFormatBufferSize];
    const int len = FormatU32Hex(buf, static_cast<uint32_t>(value));
    buf[len] = '\0';
    return il2cpp_string_new_from_cs(buf);
}

//------------------------------------------------------------------------------
// icall_double_to_string -- net48 "G15" approximation (see FormatDoubleG15).
//------------------------------------------------------------------------------
//------------------------------------------------------------------------------
// icall_single_to_string -- net48 Single.ToString(): the FLOAT "G7" text for
// the FLOAT value (not the widened double). The receiver slot holds the exact
// double widening of the float, so the target is (float)value.
//
// tiebreak: the text comes from the shared FLOAT digit-base engine
// (icall_format_float_custom with a null format == "G"), whose base rounds
// half AWAY FROM ZERO like net48. The previous snprintf("%.7g") ride used the
// CRT's half-to-EVEN conversion and printed an exact 8-digit tie one digit
// low (oracle: 12345645f -> "1.234565E+07", CRT "1.234564E+07"). The special
// spellings (NaN / U+221E) and the -0.0f sign drop are the engine's too.
//------------------------------------------------------------------------------
Il2CppString* icall_single_to_string(double value)
{
    const float f = static_cast<float>(value);
    return reinterpret_cast<Il2CppString*>(
        il2cpp::runtime::icall_format_float_custom(static_cast<double>(f), nullptr, nullptr));
}

Il2CppString* icall_double_to_string(double value)
{
    char buf[kFormatBufferSize];
    FormatDoubleG15(value, buf);
    // UTF-8 decode (not il2cpp_string_new_from_cs, which skips >= 0x80
    // bytes): the infinity spelling carries the U+221E UTF-8 sequence.
    return il2cpp_string_new_from_utf8(buf);
}

//------------------------------------------------------------------------------
// icall_string_parse_int -- int.Parse (net48):
//   * skips leading/trailing ASCII whitespace
//   * optional '+'/'-' sign
//   * decimal digits (leading zeros allowed)
//   * empty text, sign without digits, or trailing junk -> FormatException
//   * |value| > int32 range -> OverflowException
//------------------------------------------------------------------------------
int32_t icall_string_parse_int(Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_format_exception("String reference not set to an instance of a String.");
    }
    const char16_t* p = text->chars;
    const char16_t* end = p + text->length;
    while (p < end && IsAsciiWhitespace(*p))
    {
        ++p;
    }
    bool negative = false;
    if (p < end && (*p == u'+' || *p == u'-'))
    {
        negative = (*p == u'-');
        ++p;
    }
    const char16_t* digitStart = p;
    while (p < end && *p >= u'0' && *p <= u'9')
    {
        ++p;
    }
    const int digitCount = static_cast<int>(p - digitStart);
    const char16_t* trail = p;
    while (trail < end && IsAsciiWhitespace(*trail))
    {
        ++trail;
    }
    if (digitCount == 0 || trail != end)
    {
        il2cpp_throw_format_exception("Input string was not in a correct format.");
    }
    // Leading zeros do not count toward the digit limit: "00042" is two
    // significant digits. More than 10 significant digits can never fit
    // int32 -- reject immediately (net48 throws OverflowException here).
    const char16_t* sig = digitStart;
    while (sig < p && *sig == u'0')
    {
        ++sig;
    }
    const int sigCount = static_cast<int>(p - sig);
    if (sigCount > 10)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    }
    uint64_t acc = 0;
    for (const char16_t* q = sig; q < p; ++q)
    {
        acc = acc * 10ULL + static_cast<uint64_t>(*q - u'0');
    }
    // acc <= 9,999,999,999 (< 2^63): no uint64 overflow is possible with
    // sigCount <= 10; only the int32 range check remains.
    const uint64_t limit = negative ? 2147483648ULL : 2147483647ULL;
    if (acc > limit)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    }
    const int64_t v = negative ? -static_cast<int64_t>(acc) : static_cast<int64_t>(acc);
    return static_cast<int32_t>(v);
}

//------------------------------------------------------------------------------
// icall_string_parse_long -- Int64.Parse: the same strict contract as
// icall_string_parse_int with int64 bounds (19 significant digits; the
// negation limit is 9223372036854775808).
//------------------------------------------------------------------------------
int64_t icall_string_parse_long(Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_format_exception("String reference not set to an instance of a String.");
    }
    const char16_t* p = text->chars;
    const char16_t* end = p + text->length;
    while (p < end && IsAsciiWhitespace(*p))
    {
        ++p;
    }
    bool negative = false;
    if (p < end && (*p == u'+' || *p == u'-'))
    {
        negative = (*p == u'-');
        ++p;
    }
    const char16_t* digitStart = p;
    while (p < end && *p >= u'0' && *p <= u'9')
    {
        ++p;
    }
    const int digitCount = static_cast<int>(p - digitStart);
    const char16_t* trail = p;
    while (trail < end && IsAsciiWhitespace(*trail))
    {
        ++trail;
    }
    if (digitCount == 0 || trail != end)
    {
        il2cpp_throw_format_exception("Input string was not in a correct format.");
    }
    // Leading zeros do not count toward the digit limit. More than 19
    // significant digits can never fit int64 -- OverflowException.
    const char16_t* sig = digitStart;
    while (sig < p && *sig == u'0')
    {
        ++sig;
    }
    const int sigCount = static_cast<int>(p - sig);
    if (sigCount > 19)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int64.");
    }
    uint64_t acc = 0;
    for (const char16_t* q = sig; q < p; ++q)
    {
        acc = acc * 10ULL + static_cast<uint64_t>(*q - u'0');
    }
    // acc <= 999,999,999,999,999,999,999 bound from sigCount <= 19 fits
    // uint64 (< 2^63.4); only the int64 range check remains.
    const uint64_t limit = negative ? 9223372036854775808ULL : 9223372036854775807ULL;
    if (acc > limit)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int64.");
    }
    const int64_t v = negative ? -static_cast<int64_t>(acc) : static_cast<int64_t>(acc);
    return v;
}

//------------------------------------------------------------------------------
// icall_convert_to_boolean_string -- Convert.ToBoolean(string): null is
// FALSE (the documented Convert contract, unlike bool.Parse which throws);
// "true"/"false" case-insensitive with surrounding ASCII whitespace;
// anything else -> FormatException.
//------------------------------------------------------------------------------
int32_t icall_convert_to_boolean_string(Il2CppString* text)
{
    if (text == nullptr)
    {
        return 0;
    }
    const char16_t* p = text->chars;
    const char16_t* end = p + text->length;
    while (p < end && IsAsciiWhitespace(*p))
    {
        ++p;
    }
    const char16_t* wordStart = p;
    while (p < end && !IsAsciiWhitespace(*p))
    {
        ++p;
    }
    const char16_t* wordEnd = p;
    while (p < end && IsAsciiWhitespace(*p))
    {
        ++p;
    }
    if (p != end)
    {
        il2cpp_throw_format_exception("String was not recognized as a valid Boolean.");
    }
    auto matches = [&](const char16_t* wBegin, const char16_t* wEnd, const char* word)
    {
        size_t n = std::strlen(word);
        if (static_cast<size_t>(wEnd - wBegin) != n)
        {
            return false;
        }
        for (size_t i = 0; i < n; ++i)
        {
            char16_t c = wBegin[i];
            if (c >= u'A' && c <= u'Z')
            {
                c = static_cast<char16_t>(c - u'A' + u'a');
            }
            if (c != static_cast<char16_t>(word[i]))
            {
                return false;
            }
        }
        return true;
    };
    if (matches(wordStart, wordEnd, "true"))
    {
        return 1;
    }
    if (matches(wordStart, wordEnd, "false"))
    {
        return 0;
    }
    il2cpp_throw_format_exception("String was not recognized as a valid Boolean.");
    return 0; // unreachable
}

//------------------------------------------------------------------------------
// icall_bool_parse -- bool.Parse(string): the STRICT contract -- null throws
// ArgumentNullException (unlike Convert.ToBoolean's null -> false); otherwise
// identical word matching ("true"/"false", case-insensitive, trimmed).
//------------------------------------------------------------------------------
int32_t icall_bool_parse(Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    return icall_convert_to_boolean_string(text);
}

//------------------------------------------------------------------------------
// icall_char_parse -- char.Parse(string): exactly one UTF-16 unit, anything
// else (null included) is a FormatException / ArgumentNullException per the
// net48 contract.
//------------------------------------------------------------------------------
int32_t icall_char_parse(Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("s");
    }
    if (text->length != 1)
    {
        il2cpp_throw_format_exception("String must be exactly one character long.");
    }
    return text->chars[0];
}

//------------------------------------------------------------------------------
// icall_string_parse_double -- double.Parse(string) (net48 default):
// NumberStyles.Float | AllowThousands against the CURRENT culture, which is
// what the shared provider-aware core in parse_styles.cpp implements. That
// means:
//   * the group separator of the current culture IS accepted ("1,000" ->
//     1000 on an invariant/zh-CN host, "1.000" -> 1000 on de-DE);
//   * leading/trailing char.IsWhiteSpace units are trimmed (not just ASCII);
//   * the special words come from the culture's NumberFormatInfo (zh-CN
//     U+221E/-U+221E; the ASCII "Infinity"/"-Infinity" spellings are NOT
//     legal there -> FormatException).
// Invalid text -> FormatException; |value| beyond double.MaxValue ->
// OverflowException (net48 contract; .NET Core 3.0+ returns Infinity);
// underflow -> 0.0.
//------------------------------------------------------------------------------
double icall_string_parse_double(Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_format_exception("String reference not set to an instance of a String.");
    }
    double value = 0.0;
    switch (il2cpp_parse_double_default(text->chars, text->length, nullptr, &value))
    {
    case 0:
        return value;
    case 2:
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Double.");
        break;
    default:
        il2cpp_throw_format_exception("Input string was not in a correct format.");
        break;
    }
    return 0.0; // unreachable (the throw helpers do not return)
}

}} // namespace il2cpp::runtime

