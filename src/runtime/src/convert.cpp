//------------------------------------------------------------------------------
// convert.cpp -- System.Convert icall implementation (see il2cpp_convert.h).
//
// The Convert family wraps the format.cpp Parse/Format icalls with the
// Convert-specific contract:
//   * "null means zero": the string conversions return 0 / 0.0 for a null
//     input instead of delegating to Parse (which would throw on null).
//   * CHECKED numeric conversions: ToInt32(long), ToInt32(double) and
//     ToChar(int) range-check and throw OverflowException (net48 empirical
//     contract -- see the header); in-range doubles round half-to-even
//     (Math.Round semantics: 2.5 -> 2, 3.5 -> 4), never truncate.
//   * ToString delegates to the format.cpp formatting icalls.
//   * int64 parsing lives here (format.cpp has no int64 parser and is
//     read-only): same grammar as icall_string_parse_int (ASCII whitespace,
//     optional +/-, decimal digits, FormatException / OverflowException).
//------------------------------------------------------------------------------

#include "il2cpp_convert.h"
#include "il2cpp_format.h"
#include "il2cpp_exception_ext.h"
#include "decimal_format.h" // p0conv2: icall_decimal_parse (Convert.ToDecimal(string))

#include <cmath>
#include <cstdint>
#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

// The .NET Parse whitespace set: ASCII 0x09-0x0D and 0x20 (mirrors the
// format.cpp helper; documented divergence: .NET also accepts Unicode
// whitespace such as U+00A0).
bool IsConvertWhitespace(char16_t c)
{
    return c == u' ' || (c >= u'\t' && c <= u'\r');
}

// The radix set accepted by the base-N conversions (net48: 2/8/10/16).
bool IsValidRadix(int32_t radix)
{
    return radix == 2 || radix == 8 || radix == 10 || radix == 16;
}

// net48 ParseNumbers.IsDigit: '0'-'9' -> 0-9, 'A'-'Z'/'a'-'z' -> 10-35, and
// the value must be below 'radix' (so '2' is not a binary digit and 'G' is
// not a hex digit). Returns false for a non-digit; 'digit' is untouched then.
bool TryParseDigit(char16_t c, int32_t radix, uint32_t& digit)
{
    uint32_t v;
    if (c >= u'0' && c <= u'9')
    {
        v = static_cast<uint32_t>(c - u'0');
    }
    else if (c >= u'A' && c <= u'Z')
    {
        v = static_cast<uint32_t>(c - u'A') + 10;
    }
    else if (c >= u'a' && c <= u'z')
    {
        v = static_cast<uint32_t>(c - u'a') + 10;
    }
    else
    {
        return false;
    }
    if (v >= static_cast<uint32_t>(radix))
    {
        return false;
    }
    digit = v;
    return true;
}

// The net48 Convert.ToInt32(double) boundary, applied to the ROUNDED value
// (empirically pinned: 2147483647.0 -> 2147483647 legal, 2147483647.5 ->
// rounds to 2^31 -> OverflowException, -2147483648.0/-2147483648.5 ->
// -2147483648 legal, anything below -2^31 -> OverflowException).
const double kInt32UpperExclusive = 2147483648.0;  // 2^31
const double kInt32LowerInclusive = -2147483648.0; // -2^31

// Parse a decimal int64 literal from 'text' (null checked by the caller):
// ASCII whitespace, optional +/-, decimal digits; leading zeros do not count
// toward the significant-digit limit. Grammar violations -> FormatException,
// out-of-range magnitude -> OverflowException (mirrors the int32 parse in
// format.cpp). Uses uint64 accumulation: 19 significant decimal digits
// (the int64 maximum) always fit in uint64, so no multiplication overflow is
// possible once the > 19 digit pre-check rejects.
int64_t ParseInt64Text(Il2CppString* text)
{
    const char16_t* p = text->chars;
    const char16_t* end = p + text->length;
    while (p < end && IsConvertWhitespace(*p))
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
    const char16_t* trail = p;
    while (trail < end && IsConvertWhitespace(*trail))
    {
        ++trail;
    }
    if (digitStart == p || trail != end)
    {
        il2cpp_throw_format_exception("Input string was not in a correct format.");
    }
    // Skip leading zeros: "000000000000000000042" is two significant digits.
    // More than 19 significant digits can never fit int64 -- reject
    // immediately (net48 throws OverflowException here).
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
    // acc <= 9,999,999,999,999,999,999 (< 2^63): no uint64 overflow; only
    // the int64 range check remains (INT64_MIN magnitude is 2^63).
    const uint64_t limit = negative ? 9223372036854775808ULL : 9223372036854775807ULL;
    if (acc > limit)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int64.");
    }
    const int64_t v = negative ? -static_cast<int64_t>(acc) : static_cast<int64_t>(acc);
    return v;
}

} // namespace

//------------------------------------------------------------------------------
// String conversions: null -> 0 / 0.0 (the Convert null rule); otherwise the
// format.cpp Parse semantics (FormatException / OverflowException propagate).
//------------------------------------------------------------------------------
int32_t icall_convert_to_int32_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    return icall_string_parse_int(value);
}

int32_t icall_convert_to_int32_string_base(Il2CppString* value, int32_t fromBase)
{
    // net48 check order (empirically pinned): the radix is validated BEFORE
    // the string, so Convert.ToInt32(null, 3) throws ArgumentException.
    if (!IsValidRadix(fromBase))
    {
        il2cpp_throw_argument_exception("The 'base' argument must be from 2 to 36.");
        return 0; // unreachable (the throw helper never returns)
    }
    if (value == nullptr)
    {
        return 0; // net48 "null means zero" rule for this overload (pinned)
    }
    const char16_t* p = value->chars;
    const char16_t* end = p + value->length;
    if (p == end)
    {
        // net48 quirk (pinned): ParseNumbers.StringToInt(IsTight) rejects an
        // empty tight string with ArgumentOutOfRangeException.
        il2cpp_throw_argument_out_of_range_exception("value", "String cannot be empty.");
        return 0;
    }
    // Sign: '-' is legal ONLY for radix 10 (ArgumentException for 2/8/16);
    // '+' is consumed for any radix ("+101", 2 parses to 5 -- pinned).
    bool negative = false;
    if (*p == u'-')
    {
        if (fromBase != 10)
        {
            il2cpp_throw_argument_exception("Negative numbers are not allowed in the given base.");
            return 0;
        }
        negative = true;
        ++p;
    }
    else if (*p == u'+')
    {
        ++p;
    }
    // Radix 16 consumes an optional "0x"/"0X" prefix AFTER the sign
    // ("0xFF", 16 == 255; "0b"/"0o" prefixes are NOT accepted -- pinned).
    if (fromBase == 16 && (end - p) >= 2 && p[0] == u'0' && (p[1] == u'x' || p[1] == u'X'))
    {
        p += 2;
    }
    const char16_t* digitStart = p;
    uint64_t acc = 0;
    bool overflow = false;
    if (fromBase == 10)
    {
        // Signed int32 range. More than 10 significant digits can never fit
        // (net48 throws OverflowException here); leading zeros do not count.
        const char16_t* sig = p;
        while (sig < end && *sig == u'0')
        {
            ++sig;
        }
        if (end - sig > 10)
        {
            overflow = true;
        }
        else
        {
            while (p < end)
            {
                uint32_t digit;
                if (!TryParseDigit(*p, fromBase, digit))
                {
                    break;
                }
                acc = acc * 10ULL + digit;
                ++p;
            }
            if (acc > (negative ? 2147483648ULL : 2147483647ULL))
            {
                overflow = true;
            }
        }
    }
    else
    {
        // Radix 2/8/16: uint32 accumulation with net48's per-digit overflow
        // pre-check (maxVal = 0xFFFFFFFF / radix). Wraparound INSIDE uint32
        // is legal ("80000000", 16 -> INT32_MIN; "FFFFFFFF", 16 -> -1) but
        // exceeding 0xFFFFFFFF throws ("100000000", 16 -- pinned).
        const uint32_t maxVal = 0xFFFFFFFFu / static_cast<uint32_t>(fromBase);
        uint32_t u = 0;
        while (p < end)
        {
            uint32_t digit;
            if (!TryParseDigit(*p, fromBase, digit))
            {
                break;
            }
            if (u > maxVal)
            {
                overflow = true;
                break;
            }
            u = u * static_cast<uint32_t>(fromBase) + digit;
            ++p;
        }
        acc = u;
    }
    if (overflow)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
        return 0;
    }
    if (p == digitStart)
    {
        il2cpp_throw_format_exception("Input string was not in a correct format.");
        return 0;
    }
    if (p != end)
    {
        il2cpp_throw_format_exception("Input string was not in a correct format.");
        return 0;
    }
    if (fromBase == 10)
    {
        const int64_t v = negative ? -static_cast<int64_t>(acc) : static_cast<int64_t>(acc);
        return static_cast<int32_t>(v);
    }
    // Bases 2/8/16: reinterpret the uint32 accumulation as the int32 bit
    // pattern (the sign bit is a legal value -- memcpy avoids any
    // implementation-defined narrowing).
    const uint32_t bits = static_cast<uint32_t>(acc);
    int32_t result = 0;
    std::memcpy(&result, &bits, sizeof(result));
    return result;
}

double icall_convert_to_double_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0.0;
    }
    return icall_string_parse_double(value);
}

int64_t icall_convert_to_int64_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    return ParseInt64Text(value);
}

//------------------------------------------------------------------------------
// Checked numeric conversions (net48 contract -- see the header comment):
// out-of-range input throws OverflowException instead of wrapping/truncating.
//------------------------------------------------------------------------------
int32_t icall_convert_to_int32_i8(int64_t value)
{
    if (value > INT32_MAX || value < INT32_MIN)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_convert_to_int32_d(double value)
{
    // NaN / infinities have no int32 representation; net48 throws
    // OverflowException for all of them (empirically verified).
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    }
    // net48 rounds FIRST (half-to-even, Math.Round semantics -- verified:
    // ToInt32(2.9)==3, ToInt32(2.5)==2, ToInt32(3.5)==4, ToInt32(-2.5)==-2),
    // then range-checks the ROUNDED value against [INT32_MIN, 2^31).
    // Truncation would be WRONG (2.9 -> 2) and would also let 2147483647.5
    // through (truncates to 2147483647) where net48 throws OverflowException.
    const double rounded = std::nearbyint(value);
    if (rounded >= kInt32UpperExclusive || rounded < kInt32LowerInclusive)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    }
    return static_cast<int32_t>(rounded);
}

// Round-half-to-even double conversions for the remaining Convert integer
// targets (parity-T1, 2026-09-20). Each mirrors icall_convert_to_int32_d:
// NaN/inf throw, nearbyint (FE_TONEAREST == half-to-even) rounds FIRST, the
// ROUNDED value is range-checked, and the final cast is strictly defined
// C++ (the guards leave an exact integer inside the destination range).
// Unsigned targets test the ORIGINAL value for negativity (net48 throws for
// ANY negative input, even fractions that would round to -0.0); -0.0 itself
// passes through to 0.
int64_t icall_convert_to_int64_d(double value)
{
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int64.");
    }
    const double rounded = std::nearbyint(value);
    if (rounded >= 9223372036854775808.0 || rounded < -9223372036854775808.0)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int64.");
    }
    return static_cast<int64_t>(rounded);
}

uint32_t icall_convert_to_uint32_d(double value)
{
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt32.");
    }
    const double rounded = std::nearbyint(value);
    if (rounded >= 4294967296.0 || value < 0.0)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt32.");
    }
    return static_cast<uint32_t>(rounded);
}

uint64_t icall_convert_to_uint64_d(double value)
{
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt64.");
    }
    const double rounded = std::nearbyint(value);
    if (rounded >= 18446744073709551616.0 || value < 0.0)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt64.");
    }
    return static_cast<uint64_t>(rounded);
}

int32_t icall_convert_to_byte_d(double value)
{
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Byte.");
    }
    const double rounded = std::nearbyint(value);
    if (rounded >= 256.0 || value < 0.0)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Byte.");
    }
    return static_cast<int32_t>(rounded);
}

int32_t icall_convert_to_sbyte_d(double value)
{
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a SByte.");
    }
    const double rounded = std::nearbyint(value);
    if (rounded >= 128.0 || rounded < -128.0)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a SByte.");
    }
    return static_cast<int32_t>(rounded);
}

int32_t icall_convert_to_uint16_d(double value)
{
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt16.");
    }
    const double rounded = std::nearbyint(value);
    if (rounded >= 65536.0 || value < 0.0)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt16.");
    }
    return static_cast<int32_t>(rounded);
}

int32_t icall_convert_to_int16_d(double value)
{
    if (std::isnan(value) || std::isinf(value))
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Int16.");
    }
    const double rounded = std::nearbyint(value);
    if (rounded >= 32768.0 || rounded < -32768.0)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Int16.");
    }
    return static_cast<int32_t>(rounded);
}

int32_t icall_convert_to_char_i4(int32_t value)
{
    // net48 Convert.ToChar(int) is a CHECKED conversion: the value must be a
    // valid UTF-16 unit (0..0xFFFF); outside -> OverflowException (verified:
    // ToChar(0x1F600) and ToChar(-1) throw; it does NOT truncate).
    if (value < 0 || value > 0xFFFF)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Char.");
    }
    return value;
}

//------------------------------------------------------------------------------
// Simple conversions (never throw): exact widening / zero-extension.
//------------------------------------------------------------------------------
double icall_convert_to_double_i4(int32_t value)
{
    return static_cast<double>(value);
}

int32_t icall_convert_to_bool_i4(int32_t value)
{
    return value != 0 ? 1 : 0;
}

int32_t icall_convert_to_int32_char(int32_t utf16)
{
    // The UTF-16 unit (0..0xFFFF) zero-extends into int32 -- no sign issues,
    // never throws.
    return utf16 & 0xFFFF;
}

int32_t icall_convert_to_int32_bool(int32_t value)
{
    // Convert.ToInt32(bool): true -> 1, false -> 0 (net48 contract; never
    // throws). The bool rides the int32 carrier (M2 slot model).
    return value != 0 ? 1 : 0;
}

//------------------------------------------------------------------------------
// ToString family: delegate to the format.cpp formatting icalls.
//------------------------------------------------------------------------------
Il2CppString* icall_convert_to_string_i4(int32_t value)
{
    return icall_int32_to_string(value);
}

Il2CppString* icall_convert_to_string_i8(int64_t value)
{
    return icall_int64_to_string(value);
}

Il2CppString* icall_convert_to_string_d(double value)
{
    return icall_double_to_string(value);
}

Il2CppString* icall_convert_to_string_bool(bool value)
{
    return icall_bool_to_string(value);
}

Il2CppString* icall_convert_to_string_i4_base(int32_t value, int32_t toBase)
{
    if (!IsValidRadix(toBase))
    {
        il2cpp_throw_argument_exception("The 'base' argument must be from 2 to 36.");
        return nullptr; // unreachable (the throw helper never returns)
    }
    if (toBase == 10)
    {
        return icall_int32_to_string(value);
    }
    // Bases 2/8/16 render the value as UNSIGNED (uint32): negative values
    // print their two's-complement representation ("-42", 16 -> "ffffffd6";
    // INT32_MIN, 8 -> "20000000000"). No leading zeros; zero prints "0".
    // The largest output is 32 binary digits ("11111111111111111111111111111111").
    char buf[33];
    const uint32_t u = static_cast<uint32_t>(value);
    if (u == 0)
    {
        buf[0] = '0';
        buf[1] = '\0';
        return il2cpp_string_new_from_cs(buf);
    }
    const uint32_t radix = static_cast<uint32_t>(toBase);
    char tmp[32];
    int n = 0;
    uint32_t remaining = u;
    while (remaining != 0)
    {
        const uint32_t digit = remaining % radix;
        tmp[n++] = static_cast<char>(digit < 10 ? ('0' + digit) : ('a' + digit - 10));
        remaining /= radix;
    }
    int len = 0;
    while (n > 0)
    {
        buf[len++] = tmp[--n];
    }
    buf[len] = '\0';
    return il2cpp_string_new_from_cs(buf);
}

//------------------------------------------------------------------------------
// p0conv2 (D1): System.Convert -> System.Decimal.
//
// Convert.ToDecimal(string) is NOT decimal.Parse: net48 applies the Convert
// "null means zero" rule FIRST (null -> 0m), then the Number.ParseDecimal
// grammar (ASCII whitespace trim, optional sign, digits, at most one '.',
// thousands ',' between digits; NO exponent). Malformed text -> FormatException,
// out-of-range -> OverflowException. icall_decimal_parse throws
// ArgumentNullException on null, so the null arm is handled here.
//
// Convert.ToDecimal(char): net48 throws InvalidCastException for EVERY char
// (probe-pinned for 'A' -- unlike Convert.ToInt32(char), which zero-extends).
// There is no value conversion, so the helper always throws.
//------------------------------------------------------------------------------
void icall_convert_to_decimal_string(Il2CppDecimal* out, Il2CppString* value)
{
    if (value == nullptr)
    {
        // Convert's null-means-zero rule (see the header contract).
        out->flags = 0;
        out->hi32 = 0;
        out->lo64 = 0;
        return;
    }
    // Delegate to the decimal parser (throws Format/Overflow as net48 does).
    icall_decimal_parse(out, value);
}

void icall_convert_to_decimal_char(Il2CppDecimal* out, int32_t utf16)
{
    (void)out;
    (void)utf16;
    il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                     "Invalid cast from 'Char' to 'Decimal'.");
}

}} // namespace il2cpp::runtime
