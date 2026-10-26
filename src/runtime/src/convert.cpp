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
#include "il2cpp_value_semantics.h" // p0narrow3: icall_object_to_string (Convert.ToString(object))
#include "il2cpp_metadata.h" // batch 8: the System_*_class box descriptors
#include "il2cpp_conv.h"      // batch 8: the conv.ovf.* / conv.* object-source helpers

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

// Parse the base-10 MAGNITUDE of a Convert string text into a uint64, with the
// sign returned separately. net48 Convert.ToXxx(string) grammar: ASCII
// whitespace trim, optional '+'/'-', decimal digits; malformed text throws
// FormatException; a magnitude that cannot be represented in uint64 throws
// OverflowException. Each caller then applies its target range check (the
// signed/unsigned bound differs per target, and unsigned targets also reject a
// negative sign with OverflowException). Leading zeros do not count toward the
// uint64 capacity guard ("000000000000000000042" is two significant digits).
uint64_t ConvertParseMagnitude(Il2CppString* text, bool& negative)
{
    const char16_t* p = text->chars;
    const char16_t* end = p + text->length;
    while (p < end && IsConvertWhitespace(*p))
    {
        ++p;
    }
    negative = false;
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
    const char16_t* sig = digitStart;
    while (sig < p && *sig == u'0')
    {
        ++sig;
    }
    uint64_t acc = 0;
    for (const char16_t* q = sig; q < p; ++q)
    {
        const uint64_t d = static_cast<uint64_t>(*q - u'0');
        if (acc > (UINT64_MAX - d) / 10ULL)
        {
            il2cpp_throw_overflow_exception("Value was either too large or too small.");
        }
        acc = acc * 10ULL + d;
    }
    return acc;
}

// Shared range-check helper for the narrow integer string targets: rejects a
// negative sign and/or a magnitude outside [0, maxMag] with OverflowException
// (the net48 Convert contract; a null input is handled by each icall).
void ConvertThrowRangeUnsigned(uint64_t mag, bool negative, uint64_t maxMag)
{
    if (negative || mag > maxMag)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small.");
    }
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

// Convert.ToByte(string): null -> 0 (Convert null semantics), otherwise
// byte.Parse -- grammar/overflow semantics of ConvertParseMagnitude over [0, 255].
int32_t icall_convert_to_byte_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    bool negative = false;
    const uint64_t mag = ConvertParseMagnitude(value, negative);
    ConvertThrowRangeUnsigned(mag, negative, 255ULL);
    return static_cast<int32_t>(mag);
}

// Convert.ToSByte(string): [-128, 127].
int32_t icall_convert_to_sbyte_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    bool negative = false;
    const uint64_t mag = ConvertParseMagnitude(value, negative);
    if (negative ? mag > 128ULL : mag > 127ULL)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a SByte.");
    }
    return negative ? -static_cast<int32_t>(mag) : static_cast<int32_t>(mag);
}

// Convert.ToInt16(string): [-32768, 32767].
int32_t icall_convert_to_int16_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    bool negative = false;
    const uint64_t mag = ConvertParseMagnitude(value, negative);
    if (negative ? mag > 32768ULL : mag > 32767ULL)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int16.");
    }
    return negative ? -static_cast<int32_t>(mag) : static_cast<int32_t>(mag);
}

// Convert.ToUInt16(string): [0, 65535].
int32_t icall_convert_to_uint16_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    bool negative = false;
    const uint64_t mag = ConvertParseMagnitude(value, negative);
    ConvertThrowRangeUnsigned(mag, negative, 65535ULL);
    return static_cast<int32_t>(mag);
}

// Convert.ToUInt32(string): [0, 2^32).
uint32_t icall_convert_to_uint32_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    bool negative = false;
    const uint64_t mag = ConvertParseMagnitude(value, negative);
    ConvertThrowRangeUnsigned(mag, negative, 4294967295ULL);
    return static_cast<uint32_t>(mag);
}

// Convert.ToUInt64(string): [0, 2^64). A null input yields 0.
uint64_t icall_convert_to_uint64_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0;
    }
    bool negative = false;
    const uint64_t mag = ConvertParseMagnitude(value, negative);
    ConvertThrowRangeUnsigned(mag, negative, UINT64_MAX);
    return mag;
}

// Convert.ToSingle(string): null -> 0f, otherwise float.Parse over the
// double-parse grammar; a magnitude beyond Single.MaxValue throws
// OverflowException (net48: NOT a silent Infinity). NaN is legal.
float icall_convert_to_single_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        return 0.0f;
    }
    const double d = icall_string_parse_double(value);
    if (!std::isnan(d))
    {
        const double fMax = static_cast<double>(3.4028234663852886e38);
        if (d > fMax || d < -fMax)
        {
            il2cpp_throw_overflow_exception("Value was either too large or too small for a Single.");
        }
    }
    return static_cast<float>(d);
}

// Convert.ToChar(string): null -> ArgumentNullException (net48 contract --
// Convert's "null means zero" rule does NOT apply to the char target); a text
// that is not exactly one UTF-16 unit -> FormatException; otherwise the unit.
int32_t icall_convert_to_char_string(Il2CppString* value)
{
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    if (value->length != 1)
    {
        il2cpp_throw_format_exception("String must be exactly one character long.");
    }
    return value->chars[0];
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

int32_t icall_iconvertible_to_int32(int32_t value)
{
    // System.IConvertible::ToInt32 on a boxed Int32 receiver: net48's
    // Int32.ToInt32(IFormatProvider) returns this value unchanged (the
    // provider is ignored). The transpiler unboxes the receiver before this
    // call (convic1).
    return value;
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

//------------------------------------------------------------------------------
// icall_convert_to_string_obj -- Convert.ToString(object): null -> the empty
// string (net48), otherwise the value's ToString() text. The boxed dispatch
// (icall_object_to_string) throws NullReferenceException on null -- the
// Object.ToString contract, NOT the Convert one -- so the null test lives
// here. P0-narrow3, 2026-10-22.
//------------------------------------------------------------------------------
Il2CppString* icall_convert_to_string_obj(Il2CppObject* value)
{
    if (value == nullptr)
    {
        return il2cpp_string_new(nullptr, 0);
    }
    return icall_object_to_string(value);
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

//------------------------------------------------------------------------------
// icall_convert_to_single_char -- Convert.ToSingle(char): net48 throws
// InvalidCastException (char has no IConvertible.ToSingle conversion; the
// implicit char -> float widening exists but Convert does not expose it).
// No managed char can be a Single, so the helper always throws.
//------------------------------------------------------------------------------
float icall_convert_to_single_char(int32_t utf16)
{
    (void)utf16;
    il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                     "Invalid cast from 'Char' to 'Single'.");
}

//------------------------------------------------------------------------------
// Object-source Convert dispatch (batch 8): Convert.ToXxx(object value).
//
// net48: Convert.ToXxx(object) applies the Convert "null means zero" rule
// FIRST (null -> 0 / "" / 0m), then dispatches on the boxed value's
// IConvertible implementation. Every boxed primitive in this runtime implements
// IConvertible, so the dispatch is a klass switch over the box classes
// (metadata.cpp); a non-IConvertible object (a user struct, DateTime, Guid,
// TimeSpan, an enum, ...) -> InvalidCastException.
//
// The per-source conversions REUSE the same value-source icalls the
// statically-typed overloads map to, so the object overloads cannot drift from
// the value overloads. The boxed value is read through the boxing.cpp/ext2
// payload contract (offset 8; byte/sbyte/short/ushort store the masked low
// bits; uint/long/ulong/float/double store the full pattern; decimal is 16
// bytes).
//------------------------------------------------------------------------------
namespace {

// boxing.cpp/ext2 contract: the payload sits one 8-byte-aligned slot after the
// Il2CppObject header.
constexpr int32_t kObjBoxPayloadOffset = 8;

int32_t BoxedI32(Il2CppObject* v)
{
    return *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(v) + kObjBoxPayloadOffset);
}

int64_t BoxedI64(Il2CppObject* v)
{
    return *reinterpret_cast<int64_t*>(reinterpret_cast<char*>(v) + kObjBoxPayloadOffset);
}

uint64_t BoxedU64(Il2CppObject* v)
{
    return *reinterpret_cast<uint64_t*>(reinterpret_cast<char*>(v) + kObjBoxPayloadOffset);
}

double BoxedDouble(Il2CppObject* v)
{
    return *reinterpret_cast<double*>(reinterpret_cast<char*>(v) + kObjBoxPayloadOffset);
}

const Il2CppDecimal& BoxedDecimalRef(Il2CppObject* v)
{
    return *reinterpret_cast<const Il2CppDecimal*>(reinterpret_cast<char*>(v) + kObjBoxPayloadOffset);
}

enum class BoxKind : int32_t
{
    None, Boolean, Char, SByte, Byte, Int16, UInt16, Int32, UInt32,
    Int64, UInt64, Single, Double, Decimal, String, Other
};

BoxKind ClassifyBoxed(Il2CppObject* v)
{
    if (v == nullptr) return BoxKind::None;
    const Il2CppClass* k = v->klass;
    if (k == &System_Boolean_class) return BoxKind::Boolean;
    if (k == &System_Char_class)    return BoxKind::Char;
    if (k == &System_SByte_class)   return BoxKind::SByte;
    if (k == &System_Byte_class)    return BoxKind::Byte;
    if (k == &System_Int16_class)   return BoxKind::Int16;
    if (k == &System_UInt16_class)  return BoxKind::UInt16;
    if (k == &System_Int32_class)   return BoxKind::Int32;
    if (k == &System_UInt32_class)  return BoxKind::UInt32;
    if (k == &System_Int64_class)   return BoxKind::Int64;
    if (k == &System_UInt64_class)  return BoxKind::UInt64;
    if (k == &System_Single_class)  return BoxKind::Single;
    if (k == &System_Double_class)  return BoxKind::Double;
    if (k == &System_Decimal_class) return BoxKind::Decimal;
    if (k == &System_String_class)  return BoxKind::String;
    return BoxKind::Other;
}

bool IsIntFamilyKind(BoxKind kind)
{
    switch (kind)
    {
        case BoxKind::SByte:
        case BoxKind::Byte:
        case BoxKind::Int16:
        case BoxKind::UInt16:
        case BoxKind::Int32:
        case BoxKind::UInt32:
        case BoxKind::Int64:
        case BoxKind::UInt64:
            return true;
        default:
            return false;
    }
}

[[noreturn]] void ThrowConvertObjectInvalidCast()
{
    il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                     "Invalid cast in Convert: the object is not IConvertible.");
}

[[noreturn]] void ThrowConvertObjectCharOverflow()
{
    il2cpp_throw_overflow_exception("Value was either too large or too small for a Char.");
}

// The int64 carrier for the int-family boxes: sign/zero-extended exactly as
// the value-source "l"/"q" casts would (a boxed uint >= 2^31 stays positive, so
// a checked conversion range-checks it out).
int64_t BoxedIntFamilyCarrier(Il2CppObject* v, BoxKind kind)
{
    switch (kind)
    {
        case BoxKind::Int64:   return BoxedI64(v);
        case BoxKind::UInt64:  return static_cast<int64_t>(BoxedU64(v));
        case BoxKind::UInt32:  return static_cast<int64_t>(static_cast<uint32_t>(BoxedI32(v)));
        case BoxKind::UInt16:  return static_cast<int64_t>(static_cast<uint16_t>(BoxedI32(v)));
        case BoxKind::Byte:    return static_cast<int64_t>(BoxedI32(v) & 0xFF);
        case BoxKind::SByte:   return static_cast<int64_t>(static_cast<int8_t>(BoxedI32(v)));
        case BoxKind::Int16:   return static_cast<int64_t>(static_cast<int16_t>(BoxedI32(v)));
        default:               return BoxedI32(v); // Int32
    }
}

} // namespace

// Convert.ToInt32(object): null -> 0; the boxed dispatch mirrors the
// statically-typed overloads (string parse / bool / char / double RNE / decimal
// round / int-family checked).
int32_t icall_convert_to_int32_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_int32_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Boolean: return icall_convert_to_int32_bool(BoxedI32(value));
        case BoxKind::Char:    return icall_convert_to_int32_char(BoxedI32(value));
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_int32_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_int32_round(&BoxedDecimalRef(value));
        case BoxKind::Int32:   return BoxedI32(value);
        case BoxKind::SByte:   return static_cast<int32_t>(static_cast<int8_t>(BoxedI32(value)));
        case BoxKind::Int16:   return static_cast<int32_t>(static_cast<int16_t>(BoxedI32(value)));
        case BoxKind::Byte:    return BoxedI32(value) & 0xFF;
        case BoxKind::UInt16:  return BoxedI32(value) & 0xFFFF;
        case BoxKind::UInt32:
        case BoxKind::Int64:
        case BoxKind::UInt64:  return icall_convert_to_int32_i8(BoxedIntFamilyCarrier(value, kind));
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToInt64(object): null -> 0; ulong is CHECKED (values >= 2^63 throw).
int64_t icall_convert_to_int64_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_int64_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Boolean: return BoxedI32(value) != 0 ? 1 : 0;
        case BoxKind::Char:    return static_cast<int64_t>(BoxedI32(value) & 0xFFFF);
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_int64_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_int64_round(&BoxedDecimalRef(value));
        case BoxKind::UInt64:  return icall_conv_ovf_i8_u8(BoxedU64(value));
        case BoxKind::UInt32:  return static_cast<int64_t>(static_cast<uint32_t>(BoxedI32(value)));
        case BoxKind::Int64:   return BoxedI64(value);
        case BoxKind::Int32:   return BoxedI32(value);
        case BoxKind::SByte:   return static_cast<int8_t>(BoxedI32(value));
        case BoxKind::Int16:   return static_cast<int16_t>(BoxedI32(value));
        case BoxKind::Byte:    return BoxedI32(value) & 0xFF;
        case BoxKind::UInt16:  return BoxedI32(value) & 0xFFFF;
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToUInt32(object): null -> 0; negative signed sources throw.
uint32_t icall_convert_to_uint32_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_uint32_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Boolean: return BoxedI32(value) != 0 ? 1u : 0u;
        case BoxKind::Char:    return static_cast<uint32_t>(BoxedI32(value) & 0xFFFF);
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_uint32_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_uint32_round(&BoxedDecimalRef(value));
        case BoxKind::UInt32:  return static_cast<uint32_t>(BoxedI32(value));
        case BoxKind::UInt64:  return icall_conv_ovf_u4_u8(BoxedU64(value));
        case BoxKind::Int64:   return icall_conv_ovf_u4_i8(BoxedI64(value));
        case BoxKind::Int32:
        case BoxKind::SByte:
        case BoxKind::Int16:   return icall_conv_ovf_u4_i4(BoxedI32(value));
        case BoxKind::Byte:    return static_cast<uint32_t>(BoxedI32(value) & 0xFF);
        case BoxKind::UInt16:  return static_cast<uint32_t>(BoxedI32(value) & 0xFFFF);
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToUInt64(object): null -> 0; negative signed sources throw.
uint64_t icall_convert_to_uint64_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_uint64_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Boolean: return BoxedI32(value) != 0 ? 1u : 0u;
        case BoxKind::Char:    return static_cast<uint64_t>(BoxedI32(value) & 0xFFFF);
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_uint64_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_uint64_round(&BoxedDecimalRef(value));
        case BoxKind::UInt64:  return BoxedU64(value);
        case BoxKind::UInt32:  return static_cast<uint64_t>(static_cast<uint32_t>(BoxedI32(value)));
        case BoxKind::Int64:   return icall_conv_ovf_u8_i8(BoxedI64(value));
        case BoxKind::Int32:
        case BoxKind::SByte:
        case BoxKind::Int16:   return icall_conv_ovf_u8_i4(BoxedI32(value));
        case BoxKind::Byte:    return static_cast<uint64_t>(BoxedI32(value) & 0xFF);
        case BoxKind::UInt16:  return static_cast<uint64_t>(BoxedI32(value) & 0xFFFF);
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToByte/ToSByte/ToInt16/ToUInt16(object): null -> 0; the checked
// narrow range over the existing narrow icalls. The int64/uint64 sources ride
// the full-width checked helpers (no low-32 truncation).
int32_t icall_convert_to_byte_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::Boolean: return icall_conv_bool_i4(BoxedI32(value));
        case BoxKind::Char:    return icall_conv_ovf_u1_i8(BoxedI32(value));
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_byte_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_byte_round(&BoxedDecimalRef(value));
        case BoxKind::String:  return icall_convert_to_byte_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::UInt64:  return icall_conv_ovf_u1_u8(BoxedU64(value));
        default: break;
    }
    if (IsIntFamilyKind(kind)) return icall_conv_ovf_u1_i8(BoxedIntFamilyCarrier(value, kind));
    ThrowConvertObjectInvalidCast();
}

int32_t icall_convert_to_sbyte_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::Boolean: return icall_conv_bool_i4(BoxedI32(value));
        case BoxKind::Char:    return icall_conv_ovf_i1_i8(BoxedI32(value));
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_sbyte_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_sbyte_round(&BoxedDecimalRef(value));
        case BoxKind::String:  return icall_convert_to_sbyte_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::UInt64:  return icall_conv_ovf_i1_u8(BoxedU64(value));
        default: break;
    }
    if (IsIntFamilyKind(kind)) return icall_conv_ovf_i1_i8(BoxedIntFamilyCarrier(value, kind));
    ThrowConvertObjectInvalidCast();
}

int32_t icall_convert_to_int16_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::Boolean: return icall_conv_bool_i4(BoxedI32(value));
        case BoxKind::Char:    return icall_conv_ovf_i2_i8(BoxedI32(value));
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_int16_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_int16_round(&BoxedDecimalRef(value));
        case BoxKind::String:  return icall_convert_to_int16_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::UInt64:  return icall_conv_ovf_i2_u8(BoxedU64(value));
        default: break;
    }
    if (IsIntFamilyKind(kind)) return icall_conv_ovf_i2_i8(BoxedIntFamilyCarrier(value, kind));
    ThrowConvertObjectInvalidCast();
}

int32_t icall_convert_to_uint16_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::Boolean: return icall_conv_bool_i4(BoxedI32(value));
        case BoxKind::Char:    return icall_conv_ovf_u2_i8(BoxedI32(value));
        case BoxKind::Double:
        case BoxKind::Single:  return icall_convert_to_uint16_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_uint16_round(&BoxedDecimalRef(value));
        case BoxKind::String:  return icall_convert_to_uint16_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::UInt64:  return icall_conv_ovf_u2_u8(BoxedU64(value));
        default: break;
    }
    if (IsIntFamilyKind(kind)) return icall_conv_ovf_u2_i8(BoxedIntFamilyCarrier(value, kind));
    ThrowConvertObjectInvalidCast();
}

// Convert.ToDouble(object): null -> 0.0; bool -> 1.0/0.0 (net48), char is NOT
// IConvertible to Double on net48 (InvalidCastException).
double icall_convert_to_double_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0.0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_double_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Boolean: return BoxedI32(value) != 0 ? 1.0 : 0.0;
        case BoxKind::Double:
        case BoxKind::Single:  return BoxedDouble(value);
        case BoxKind::Decimal: return icall_decimal_to_double(&BoxedDecimalRef(value));
        case BoxKind::UInt64:  return icall_conv_d_u8(BoxedU64(value));
        case BoxKind::UInt32:  return icall_conv_d_u4(static_cast<uint32_t>(BoxedI32(value)));
        case BoxKind::Int64:   return icall_conv_d_i8(BoxedI64(value));
        case BoxKind::Int32:   return icall_conv_d_i4(BoxedI32(value));
        case BoxKind::SByte:   return icall_conv_d_i4(BoxedI32(value));
        case BoxKind::Int16:   return icall_conv_d_i4(BoxedI32(value));
        case BoxKind::Byte:    return icall_conv_d_i4(BoxedI32(value) & 0xFF);
        case BoxKind::UInt16:  return icall_conv_d_i4(BoxedI32(value) & 0xFFFF);
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToSingle(object): null -> 0f; char is NOT IConvertible to Single on
// net48 (InvalidCastException).
float icall_convert_to_single_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0.0f;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_single_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Double:
        case BoxKind::Single:  return icall_conv_r4_d(BoxedDouble(value));
        case BoxKind::Decimal: return icall_decimal_to_float(&BoxedDecimalRef(value));
        case BoxKind::UInt64:  return icall_conv_r4_u8(BoxedU64(value));
        case BoxKind::UInt32:  return icall_conv_r4_u4(static_cast<uint32_t>(BoxedI32(value)));
        case BoxKind::Int64:   return icall_conv_r4_i8(BoxedI64(value));
        case BoxKind::Boolean:
        case BoxKind::Int32:   return icall_conv_r4_i4(BoxedI32(value));
        case BoxKind::SByte:   return icall_conv_r4_i4(BoxedI32(value));
        case BoxKind::Int16:   return icall_conv_r4_i4(BoxedI32(value));
        case BoxKind::Byte:    return icall_conv_r4_i4(BoxedI32(value) & 0xFF);
        case BoxKind::UInt16:  return icall_conv_r4_i4(BoxedI32(value) & 0xFFFF);
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToBoolean(object): null -> false; the full 64-bit int sources are
// tested for non-zero (no low-32 truncation), and float/double/decimal are the
// net48 `value != 0` conversions. char is NOT IConvertible to Boolean on net48
// (InvalidCastException).
int32_t icall_convert_to_bool_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_boolean_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Boolean:
        case BoxKind::Int32:
        case BoxKind::SByte:
        case BoxKind::Int16:
        case BoxKind::Byte:
        case BoxKind::UInt16:
        case BoxKind::UInt32:  return BoxedI32(value) != 0 ? 1 : 0;
        case BoxKind::Int64:   return BoxedI64(value) != 0 ? 1 : 0;
        case BoxKind::UInt64:  return BoxedU64(value) != 0 ? 1 : 0;
        case BoxKind::Double:
        case BoxKind::Single:  return BoxedDouble(value) != 0.0 ? 1 : 0;
        case BoxKind::Decimal: return (BoxedDecimalRef(value).lo64 != 0
                                       || BoxedDecimalRef(value).hi32 != 0) ? 1 : 0;
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToChar(object): null -> '\0'; int-family is CHECKED against the UTF-16
// unit range (the 64-bit sources use the full-width check, not a low-32
// truncation). bool/float/double/decimal are NOT IConvertible to Char on net48
// (InvalidCastException).
int32_t icall_convert_to_char_obj(Il2CppObject* value)
{
    if (value == nullptr) return 0;
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  return icall_convert_to_char_string(reinterpret_cast<Il2CppString*>(value));
        case BoxKind::Char:    return BoxedI32(value) & 0xFFFF;
        case BoxKind::Int32:
        case BoxKind::SByte:
        case BoxKind::Int16:   return icall_convert_to_char_i4(BoxedI32(value));
        case BoxKind::Byte:    return icall_convert_to_char_i4(BoxedI32(value) & 0xFF);
        case BoxKind::UInt16:  return icall_convert_to_char_i4(BoxedI32(value) & 0xFFFF);
        case BoxKind::UInt32:
        {
            const uint32_t u = static_cast<uint32_t>(BoxedI32(value));
            if (u > 0xFFFF) ThrowConvertObjectCharOverflow();
            return static_cast<int32_t>(u);
        }
        case BoxKind::Int64:
        {
            const int64_t v = BoxedI64(value);
            if (v < 0 || v > 0xFFFF) ThrowConvertObjectCharOverflow();
            return static_cast<int32_t>(v);
        }
        case BoxKind::UInt64:
        {
            const uint64_t v = BoxedU64(value);
            if (v > 0xFFFF) ThrowConvertObjectCharOverflow();
            return static_cast<int32_t>(v);
        }
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

// Convert.ToDecimal(object): null -> 0m; the decimal target reuses the
// existing ctor icalls. char is NOT IConvertible to Decimal on net48
// (InvalidCastException).
void icall_convert_to_decimal_obj(Il2CppDecimal* out, Il2CppObject* value)
{
    if (value == nullptr)
    {
        out->flags = 0;
        out->hi32 = 0;
        out->lo64 = 0;
        return;
    }
    const BoxKind kind = ClassifyBoxed(value);
    switch (kind)
    {
        case BoxKind::String:  icall_convert_to_decimal_string(out, reinterpret_cast<Il2CppString*>(value)); return;
        case BoxKind::Decimal: *out = BoxedDecimalRef(value); return;
        case BoxKind::Boolean:
        case BoxKind::Int32:
        case BoxKind::SByte:
        case BoxKind::Int16:   icall_decimal_ctor_int32(out, BoxedI32(value)); return;
        case BoxKind::Byte:    icall_decimal_ctor_int32(out, BoxedI32(value) & 0xFF); return;
        case BoxKind::UInt16:  icall_decimal_ctor_int32(out, BoxedI32(value) & 0xFFFF); return;
        case BoxKind::UInt32:  icall_decimal_ctor_uint32(out, static_cast<uint32_t>(BoxedI32(value))); return;
        case BoxKind::Int64:   icall_decimal_ctor_int64(out, BoxedI64(value)); return;
        case BoxKind::UInt64:  icall_decimal_ctor_uint64(out, BoxedU64(value)); return;
        case BoxKind::Double:  icall_decimal_ctor_double(out, BoxedDouble(value)); return;
        case BoxKind::Single:  icall_decimal_ctor_float(out, static_cast<float>(BoxedDouble(value))); return;
        default: break;
    }
    ThrowConvertObjectInvalidCast();
}

//------------------------------------------------------------------------------
// Value-source residuals (batch 8): the net48 conversions the batch-5 sweep
// LOUD-rejected with an "InvalidCastException" note but that net48 actually
// performs. Each is pinned by the net48 oracle (tmp-convprobe):
//   * Convert.ToBoolean(double/float) == (value != 0)
//   * Convert.ToBoolean(long/ulong)  == (value != 0)   (full 64-bit, no truncation)
//   * Convert.ToBoolean(decimal)     == (value != 0)
//   * Convert.ToChar(long) / ToChar(ulong) is the CHECKED UTF-16 range test.
//------------------------------------------------------------------------------
int32_t icall_convert_to_bool_d(double value)
{
    return value != 0.0 ? 1 : 0;
}

int32_t icall_convert_to_bool_i8(int64_t value)
{
    return value != 0 ? 1 : 0;
}

int32_t icall_convert_to_bool_dec(const Il2CppDecimal* d)
{
    return (d->lo64 != 0 || d->hi32 != 0) ? 1 : 0;
}

int32_t icall_convert_to_char_i8(int64_t value)
{
    if (value < 0 || value > 0xFFFF)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Char.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_convert_to_char_u8(uint64_t value)
{
    if (value > 0xFFFF)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Char.");
    }
    return static_cast<int32_t>(value);
}

//------------------------------------------------------------------------------
// InvalidCastException residuals (batch 8): net48 throws InvalidCastException
// for these source/target pairs (the source has no IConvertible conversion to
// the target). The helpers always throw through the managed carrier so the
// generated try/catch handles them like any other managed throw.
//------------------------------------------------------------------------------
[[noreturn]] static void ThrowConvertIce(const char* message)
{
    il2cpp_throw_new("System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
                     message); // the message is not byte-compared by the harness
}

double icall_convert_to_double_char(int32_t utf16)
{
    (void)utf16;
    ThrowConvertIce("Invalid cast from 'Char' to 'Double'.");
}

int32_t icall_convert_to_bool_char(int32_t utf16)
{
    (void)utf16;
    ThrowConvertIce("Invalid cast from 'Char' to 'Boolean'.");
}

int32_t icall_convert_to_char_bool(int32_t value)
{
    (void)value;
    ThrowConvertIce("Invalid cast from 'Boolean' to 'Char'.");
}

int32_t icall_convert_to_char_d(double value)
{
    (void)value;
    ThrowConvertIce("Invalid cast from 'Double' to 'Char'.");
}

}} // namespace il2cpp::runtime
