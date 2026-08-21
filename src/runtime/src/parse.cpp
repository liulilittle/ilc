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
//   * The double core mirrors format.cpp: the ASCII projection is
//     validated against the same grammar (IsValidDoubleText replica) and
//     converted with strtod (correctly rounded, C locale); the only
//     difference from Parse is that ERANGE+inf is a FAILURE (return 0)
//     instead of an OverflowException. A fixed stack buffer covers the
//     common case; longer text falls back to malloc (a failed allocation
//     is a plain failure -- no throw, no leak).
//   * Whitespace is the ASCII set (0x09-0x0D, 0x20) -- same documented
//     divergence as the Parse family.
//------------------------------------------------------------------------------

#include "il2cpp_parse.h"

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

bool IsAsciiWhitespaceAscii(char c)
{
    return c == ' ' || (c >= '\t' && c <= '\r');
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

// Grammar validation for double text (see il2cpp_format.h): optional ASCII
// whitespace, the exact word "NaN", OR a decimal literal (sign, digits,
// optional '.', optional e/E exponent). The ASCII words
// "Infinity"/"-Infinity" are REJECTED -- net48 reads the special words from
// the culture's NumberFormatInfo (zh-CN uses U+221E, so the ASCII spelling
// throws FormatException there); the U+221E/-U+221E culture words are
// matched on the UTF-16 text in TryParseDoubleCore BEFORE the projection.
// Replica of format.cpp's IsValidDoubleText minus the Infinity family; hex
// floats, abbreviated "inf"/"nan" and "+Infinity"/"-NaN" are also rejected.
bool IsValidDoubleText(const char* s)
{
    const char* p = s;
    while (IsAsciiWhitespaceAscii(*p))
    {
        ++p;
    }
    const char* spec = nullptr;
    if (std::strncmp(p, "NaN", 3) == 0)
    {
        spec = p + 3;
    }
    // "Infinity"/"-Infinity" intentionally NOT matched here: net48's
    // special words are culture-dependent, so the ASCII spelling must
    // fall through to the decimal-literal path below and fail.
    if (spec != nullptr)
    {
        p = spec;
        while (IsAsciiWhitespaceAscii(*p))
        {
            ++p;
        }
        return *p == '\0';
    }
    if (*p == '+' || *p == '-')
    {
        ++p;
    }
    int before = 0;
    while (*p >= '0' && *p <= '9')
    {
        ++before;
        ++p;
    }
    if (*p == '.')
    {
        ++p;
    }
    int after = 0;
    while (*p >= '0' && *p <= '9')
    {
        ++after;
        ++p;
    }
    if (before + after == 0)
    {
        return false;
    }
    if (*p == 'e' || *p == 'E')
    {
        ++p;
        if (*p == '+' || *p == '-')
        {
            ++p;
        }
        int expDigits = 0;
        while (*p >= '0' && *p <= '9')
        {
            ++expDigits;
            ++p;
        }
        if (expDigits == 0)
        {
            return false;
        }
    }
    while (IsAsciiWhitespaceAscii(*p))
    {
        ++p;
    }
    return *p == '\0';
}

// Match the culture infinity words on the ORIGINAL UTF-16 text (the ASCII
// projection below cannot represent U+221E). net48 zh-CN probe: the
// positive-infinity symbol is exactly U+221E and the negative form is
// exactly '-' + U+221E; ASCII whitespace may pad either side, and a '+' sign
// is NOT accepted (whole-text symbol match, so "+∞" fails). Returns 1 with
// *out set on a full match; 0 otherwise (caller falls through to the
// decimal-literal path, which rejects the text).
bool TryMatchInfinityWord(const char16_t* p, const char16_t* end, double* out)
{
    while (p < end && IsWs(*p))
    {
        ++p;
    }
    bool negative = false;
    if (p < end && *p == u'-' && p + 1 < end && p[1] == u'\u221E')
    {
        negative = true;
        p += 2;
    }
    else if (p < end && *p == u'\u221E')
    {
        p += 1;
    }
    else
    {
        return false;
    }
    while (p < end && IsWs(*p))
    {
        ++p;
    }
    if (p != end)
    {
        return false; // trailing junk after the symbol ("∞x", "∞∞", ...)
    }
    *out = negative ? -std::numeric_limits<double>::infinity()
                    : std::numeric_limits<double>::infinity();
    return true;
}

// Double TryParse core: validate the text against the double grammar and
// convert with strtod. SUCCESS: *out holds the value, true. FAILURE
// (invalid text, out-of-range magnitude, allocation failure): false.
// Stack buffer first -- the vast majority of real texts are short; only
// longer text allocates (format.cpp allocates unconditionally).
bool TryParseDoubleCore(const char16_t* p, int32_t len, double* out)
{
    // Culture infinity words first, on the ORIGINAL UTF-16 text (the ASCII
    // projection below cannot represent U+221E): zh-CN net48 reads the
    // infinity words from the NumberFormatInfo (U+221E), so "∞"/"-∞" are
    // successes while the ASCII "Infinity"/"-Infinity" spellings fall
    // through and fail the validator (net48 zh-CN probe-pinned).
    {
        double special = 0.0;
        if (TryMatchInfinityWord(p, p + len, &special))
        {
            *out = special;
            return true;
        }
    }
    char stackBuf[256];
    std::unique_ptr<char, void (*)(void*)> ownedBuf(nullptr, &std::free);
    char* buf = stackBuf;
    if (len >= static_cast<int32_t>(sizeof(stackBuf)))
    {
        ownedBuf.reset(static_cast<char*>(std::malloc(static_cast<size_t>(len) + 1)));
        if (ownedBuf == nullptr)
        {
            return false; // TryParse never throws: allocation failure -> 0
        }
        buf = ownedBuf.get();
    }
    for (int32_t i = 0; i < len; ++i)
    {
        const char16_t c = p[i];
        if (c > 0x7F)
        {
            return false; // non-ASCII text is never legal double text
        }
        buf[i] = static_cast<char>(c);
    }
    buf[len] = '\0';
    if (!IsValidDoubleText(buf))
    {
        return false;
    }
    errno = 0;
    char* end = nullptr;
    const double result = std::strtod(buf, &end);
    (void)end; // the validator guarantees the full buffer was consumed
    if (errno == ERANGE && std::isinf(result))
    {
        return false; // net48: TryParse fails on out-of-range magnitude
    }
    *out = result;
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
// icall_try_parse_int32_base -- Convert.ToInt32(string, fromBase) style
// parse for base 2/8/10/16 (see header for the invalid-base deviation).
//------------------------------------------------------------------------------
int32_t icall_try_parse_int32_base(Il2CppObject* s, int32_t fromBase, int32_t* result)
{
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
    {
        return 0; // documented deviation: no throw for an invalid base
    }
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    int64_t v = 0;
    // allowSign: base 10 accepts '+'/'-'; base 2/8/16 accept '+' (skipped)
    // and reject '-' (net48 ArgumentException -> TryParse failure).
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
// icall_try_parse_double -- double.TryParse (net48, zh-CN culture): the
// culture words "NaN" and U+221E/-U+221E or a decimal literal; the ASCII
// "Infinity"/"-Infinity" words are rejected (net48 culture semantics);
// out-of-range magnitude -> failure.
//------------------------------------------------------------------------------
int32_t icall_try_parse_double(Il2CppObject* s, double* result)
{
    if (s == nullptr || result == nullptr)
    {
        return 0;
    }
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    double out = 0.0;
    if (!TryParseDoubleCore(str->chars, str->length, &out))
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
