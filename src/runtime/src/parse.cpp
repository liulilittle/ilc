//------------------------------------------------------------------------------
// parse.cpp -- .NET TryParse-family icalls (see il2cpp_parse.h).
//
// Implementation notes:
//   * NO-THROW BY CONSTRUCTION: every helper returns bool/int32 and every
//     failure path returns 0 -- there is no try/catch and no throw site in
//     this file. The parsing rules are re-implemented here (error-path
//     first) instead of wrapping the throwing Parse icalls in try/catch:
//     same observable semantics as icall_string_parse_int/parse_double
//     (il2cpp_format.h) at a lower cost, and the no-throw property cannot
//     be broken by an unexpected exception type.
//   * The integer core (TryParseInteger) accumulates in uint64 with a
//     per-step overflow guard -- there is no digit-count upper bound, so
//     arbitrarily long digit runs fail cleanly without overflow UB, and
//     the final [minVal, maxVal] check gives the exact .NET ranges.
//   * The DOUBLE core lives in parse_styles.cpp, shared with the throwing
//     Parse entry point and with the NumberStyles/IFormatProvider overloads:
//     it is the net48 Number.TryParseNumber state machine, so the group and
//     decimal separators come from the provider's NumberFormatInfo and
//     AllowThousands is ON by default (double.Parse(string) is
//     Float|AllowThousands in net48).
//   * The INTEGER family (this file) keeps the ASCII whitespace set
//     (0x09-0x0D, 0x20); that matches net48 for int.Parse, which rejects
//     group separators in every culture (measured: invariant, de-DE, fr-FR).
//------------------------------------------------------------------------------

#include "il2cpp_parse.h"
#include "il2cpp_parse_styles.h" // shared provider-aware double parse core
#include "il2cpp_exception_ext.h" // the invalid-fromBase ArgumentException

#include <cerrno>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <memory>

namespace il2cpp { namespace runtime {

namespace {

// .NET Parse whitespace: the ASCII set (0x09-0x0D and 0x20).
bool IsWs(char16_t c)
{
    return c == u' ' || (c >= u'\t' && c <= u'\r');
}

// Digit value of a char16_t for base 2/8/10/16 ('0'-'9', 'a'-'f', 'A'-'F');
// -1 for anything else (so hex letters never leak into decimal parsing).
int DigitValue(char16_t c)
{
    if (c >= u'0' && c <= u'9')
    {
        return static_cast<int>(c - u'0');
    }
    if (c >= u'a' && c <= u'f')
    {
        return static_cast<int>(c - u'a') + 10;
    }
    if (c >= u'A' && c <= u'F')
    {
        return static_cast<int>(c - u'A') + 10;
    }
    return -1;
}

// Core integer TryParse over [p, end) in 'base' with an optional sign:
//   digits (valid in 'base'), optional leading sign ('allowSign').
// 'allowWs': when true, leading/trailing ASCII whitespace is skipped and
//   tolerated (TryParse-family contract); when false, ANY whitespace is an
//   invalid character (net48 Convert.ToInt32(string, fromBase) passes
//   ParseNumbers.IsTight|NoSpace -- empirically NO whitespace for any
//   radix). 'allowSign' has THREE behaviours (net48 empirical probe of
//   Convert.ToInt32(string, fromBase)):
//     - radix 10 (allowSign=true): a leading '+'/'-' is accepted.
//     - radix 2/8/16 (allowSign=false): a leading '+' is ACCEPTED (it is
//       skipped, no sign effect); a leading '-' is REJECTED (net48 throws
//       ArgumentException, which the TryParse contract turns into
//       failure). Empirically verified: "+10",2 -> 2, "+10",8 -> 8,
//       "+1F",16 -> 31 all succeed on net48.
//   radix 16 additionally accepts a "0x"/"0X" prefix (net48 strips it;
//   no other radix accepts the prefix -- "0x10",2/8/10 -> FormatException;
//   "0x" with no digits or with an invalid digit also fails).
// 'rangeCheck': when true, the value must fit [minVal, maxVal] and the
//   magnitude is accumulated in uint64 with a per-step overflow guard
//   (radix 10 Convert/TryParse contract); when false, the magnitude is
//   accumulated in uint32 and FAILS when it would exceed UINT32_MAX --
//   net48 Convert.ToInt32 for radix 2/8/16 has NO int32 range check but a
//   hard 32-bit magnitude cap: "80000000",16 -> INT32_MIN and
//   "FFFFFFFF",16 -> -1 (wrap within uint32), while "100000000",16 and 11
//   octal digits -> OverflowException (empirically verified).
// SUCCESS: *out holds the value and true is returned.
// FAILURE (no digits, trailing junk, sign/range/prefix violation): false,
// *out untouched.
bool TryParseInteger(const char16_t* p, const char16_t* end, int base, bool allowSign,
                     bool allowWs, bool rangeCheck,
                     int64_t minVal, int64_t maxVal, int64_t* out)
{
    while (allowWs && p < end && IsWs(*p))
    {
        ++p;
    }
    bool negative = false;
    if (p < end && (*p == u'+' || *p == u'-'))
    {
        // net48: '+' is accepted for EVERY radix (skipped, no sign effect);
        // '-' requires allowSign (radix 10). radix 2/8/16 reject '-' with
        // ArgumentException -> TryParse failure.
        if (*p == u'-' && !allowSign)
        {
            return false;
        }
        negative = (*p == u'-');
        ++p;
    }
    // radix 16: strip a "0x"/"0X" prefix (net48 does; no other radix).
    // The prefix must be followed by at least one valid digit -- "0x" or
    // "0xG" falls through to the no-digits failure below.
    if (base == 16 && end - p >= 2 && p[0] == u'0' && (p[1] == u'x' || p[1] == u'X'))
    {
        p += 2;
    }
    if (!rangeCheck)
    {
        // radix 2/8/16: uint32 accumulation (net48). A value that fits in
        // uint32 wraps through the bit pattern ("80000000",16 ->
        // INT32_MIN); a value whose next digit would exceed UINT32_MAX is
        // an OverflowException on net48 -> TryParse failure (no wrap
        // across 32 bits: "100000000",16 and "77777777777",8 fail).
        uint32_t acc = 0;
        bool anyDigit = false;
        while (p < end)
        {
            const int d = DigitValue(*p);
            if (d < 0 || d >= base)
            {
                break;
            }
            // Guard: acc * base + d must stay <= UINT32_MAX.
            if (acc > (UINT32_MAX - static_cast<uint32_t>(d)) / static_cast<uint32_t>(base))
            {
                return false;
            }
            acc = acc * static_cast<uint32_t>(base) + static_cast<uint32_t>(d);
            anyDigit = true;
            ++p;
        }
        if (!anyDigit || p != end)
        {
            return false;
        }
        // uint32 -> int32 is the bit pattern (implementation-defined but
        // two's-complement on every supported target; same convention as
        // conv.cpp); widen with sign extension.
        *out = static_cast<int64_t>(static_cast<int32_t>(acc));
        return true;
    }
    uint64_t acc = 0;
    bool anyDigit = false;
    while (p < end)
    {
        const int d = DigitValue(*p);
        if (d < 0 || d >= base)
        {
            break;
        }
        // Guard: acc * base + d must stay <= UINT64_MAX.
        if (acc > (UINT64_MAX - static_cast<uint64_t>(d)) / static_cast<uint64_t>(base))
        {
            return false;
        }
        acc = acc * static_cast<uint64_t>(base) + static_cast<uint64_t>(d);
        anyDigit = true;
        ++p;
    }
    if (!anyDigit)
    {
        return false;
    }
    if (allowWs)
    {
        const char16_t* trail = p;
        while (trail < end && IsWs(*trail))
        {
            ++trail;
        }
        if (trail != end)
        {
            return false;
        }
    }
    else if (p != end)
    {
        return false;
    }
    // Range gates BEFORE the narrowing cast: the magnitude can exceed int64
    // even when the per-step guard passes (positive 2^63, negative 2^63+1)
    // -- the cast would wrap around and silently pass the range check, so
    // gate in uint64 first.
    int64_t v;
    if (negative)
    {
        if (acc > 9223372036854775808ULL)
        {
            return false;
        }
        v = (acc == 9223372036854775808ULL) ? INT64_MIN : -static_cast<int64_t>(acc);
    }
    else
    {
        if (acc > static_cast<uint64_t>(INT64_MAX))
        {
            return false;
        }
        v = static_cast<int64_t>(acc);
    }
    if (v < minVal || v > maxVal)
    {
        return false;
    }
    *out = v;
    return true;
}


// Ordinal-ignore-case match of [p, end) against an ASCII word.
bool MatchesIgnoreCase(const char16_t* p, const char16_t* end, const char* word)
{
    size_t i = 0;
    for (; p != end; ++p, ++i)
    {
        if (word[i] == '\0')
        {
            return false; // text longer than the word
        }
        char16_t w = static_cast<char16_t>(static_cast<unsigned char>(word[i]));
        char16_t c = *p;
        if (w >= u'A' && w <= u'Z')
        {
            w = static_cast<char16_t>(w - u'A' + u'a');
        }
        if (c >= u'A' && c <= u'Z')
        {
            c = static_cast<char16_t>(c - u'A' + u'a');
        }
        if (c != w)
        {
            return false;
        }
    }
    return word[i] == '\0'; // the word must be consumed exactly
}

} // namespace

//------------------------------------------------------------------------------
// icall_try_parse_int32 / int64 / byte -- base-10 integers.
//------------------------------------------------------------------------------
int32_t icall_try_parse_int32(Il2CppObject* s, int32_t* result)
{
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    *result = 0; // .NET TryParse contract: failed parse writes 0
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    int64_t v = 0;
    if (!TryParseInteger(str->chars, str->chars + str->length, 10, true, true, true,
                         static_cast<int64_t>(INT32_MIN), static_cast<int64_t>(INT32_MAX), &v))
    {
        return 0;
    }
    *result = static_cast<int32_t>(v);
    return 1;
}

int32_t icall_try_parse_int64(Il2CppObject* s, int64_t* result)
{
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    *result = 0;
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    int64_t v = 0;
    if (!TryParseInteger(str->chars, str->chars + str->length, 10, true, true, true,
                         INT64_MIN, INT64_MAX, &v))
    {
        return 0;
    }
    *result = v;
    return 1;
}

int32_t icall_try_parse_byte(Il2CppObject* s, int32_t* result)
{
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    *result = 0;
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    int64_t v = 0;
    if (!TryParseInteger(str->chars, str->chars + str->length, 10, true, true, true,
                         0, 255, &v))
    {
        return 0;
    }
    *result = static_cast<int32_t>(v);
    return 1;
}

//------------------------------------------------------------------------------
// icall_try_parse_int32_base -- Convert.ToInt32(string, fromBase) style parse.
//
// RADIX CONTRACT (measured on a real net48 CLR, one process per probe): the
// legal set is {2, 8, 10, 16} EXACTLY. Every other base -- including 1, 3,
// 17, 36 and 37 -- throws ArgumentException before the string is even
// examined (net48 convert.cs: value == null && fromBase != 10 -> the
// Arg_InvalidBase check runs first, so Convert.ToInt32(null, 3) throws while
// Convert.ToInt32(null, 2/8/10/16) returns 0). The earlier "2..36, and an
// invalid base returns 0" reading was WRONG on both counts.
//
// This is the TryParse-SHAPED entry point (it returns 1/0 so the transpiler
// can fold it), so only the format/range failures return 0; the invalid-base
// ArgumentException cannot be folded into a return value and is thrown, the
// same way net48's ArgumentNullException/ArgumentException propagate through
// TryParse in general.
//------------------------------------------------------------------------------
int32_t icall_try_parse_int32_base(Il2CppObject* s, int32_t fromBase, int32_t* result)
{
    if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
    {
        // net48 checks the radix BEFORE the text, so a null string with a bad
        // base still throws ArgumentException (measured).
        il2cpp_throw_argument_exception("The 'base' argument must be 2, 8, 10 or 16.");
    }
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    int64_t v = 0;
    // allowSign: base 10 accepts '+'/'-'; base 2/8/16 accept '+' (skipped)
    // and reject '-' (net48 ArgumentException -> failure).
    // rangeCheck: base 10 only (int32 range); base 2/8/16 use the uint32
    // wrap-with-32-bit-cap path.
    if (!TryParseInteger(str->chars, str->chars + str->length, fromBase, fromBase == 10,
                         false, fromBase == 10,
                         static_cast<int64_t>(INT32_MIN), static_cast<int64_t>(INT32_MAX), &v))
    {
        return 0;
    }
    *result = static_cast<int32_t>(v);
    return 1;
}

//------------------------------------------------------------------------------
// icall_try_parse_double -- double.TryParse(string) (net48 default):
// NumberStyles.Float | AllowThousands against the CURRENT culture, via the
// shared provider-aware core in parse_styles.cpp. The current culture's
// group separator is accepted ("1,000" -> 1000 here), the special words come
// from its NumberFormatInfo (zh-CN U+221E), and out-of-range magnitude is a
// plain failure (never a throw).
//------------------------------------------------------------------------------
int32_t icall_try_parse_double(Il2CppObject* s, double* result)
{
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    *result = 0.0; // failed parse writes 0.0 (.NET contract)
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    double out = 0.0;
    if (il2cpp_parse_double_default(str->chars, str->length, nullptr, &out) != 0)
    {
        return 0;
    }
    *result = out;
    return 1;
}

//------------------------------------------------------------------------------
// icall_try_parse_bool -- bool.TryParse: exact words "true"/"false" in any
// case, ASCII-whitespace padded; anything else fails.
//------------------------------------------------------------------------------
int32_t icall_try_parse_bool(Il2CppObject* s, int32_t* result)
{
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    *result = 0;
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    const char16_t* p = str->chars;
    const char16_t* end = p + str->length;
    while (p < end && IsWs(*p))
    {
        ++p;
    }
    const char16_t* wordStart = p;
    while (p < end && !IsWs(*p))
    {
        ++p;
    }
    const char16_t* wordEnd = p;
    const char16_t* trail = p;
    while (trail < end && IsWs(*trail))
    {
        ++trail;
    }
    if (trail != end)
    {
        return 0; // embedded whitespace is not a valid bool word
    }
    if (MatchesIgnoreCase(wordStart, wordEnd, "true"))
    {
        *result = 1;
        return 1;
    }
    if (MatchesIgnoreCase(wordStart, wordEnd, "false"))
    {
        *result = 0;
        return 1;
    }
    return 0;
}

}} // namespace il2cpp::runtime

