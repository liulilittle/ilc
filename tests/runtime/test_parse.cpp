//------------------------------------------------------------------------------
// test_parse.cpp -- TryParse-family icall unit tests (il2cpp_parse.h).
//
// Coverage (normal + edge + error paths):
//   1. int32.TryParse: 42, +/- sign, whitespace padding, leading zeros,
//      INT32_MAX/INT32_MIN round-trip; null text, empty, whitespace-only,
//      junk, embedded junk, double sign, one-past-range, 20-digit overflow.
//   2. int64.TryParse: INT64_MAX/INT64_MIN round-trip, 20-digit overflow,
//      "-0".
//   3. double.TryParse: "NaN" (net48 spelling), the zh-CN culture infinity
//      words U+221E / "-" + U+221E -> +/-Infinity, fraction/exponent forms,
//      ".5"/"5.", "-0" negative zero sign, underflow -> 0.0 success;
//      junk, "inf", hex float, incomplete exponent, overflow ("1e999")
//      AND the ASCII "Infinity"/"-Infinity" words (net48 zh-CN rejects
//      them: its PositiveInfinitySymbol is U+221E) -> false.
//   4. bool.TryParse: case-insensitive "true"/"false" with whitespace;
//      junk, "1", empty, null -> false.
//   5. byte.TryParse: 0..255; "256"/"-1" -> false.
//   6. Convert-style base parsing: 2/8/16 (sign allowed, .NET semantics);
//      "0x" prefix rejected, overflow -> false; invalid base -> false
//      (documented deviation: .NET throws ArgumentException, this runtime
//      follows the no-throw TryParse contract).
//   7. Contract: failure does NOT write the out parameter; null out
//      pointer -> 0; every path returns exactly 0/1 without throwing.
//
// Plain executable + minimal assertion harness (see test_format.cpp): any
// failed Expect() prints FAIL and the process exits non-zero.
//------------------------------------------------------------------------------

#include "il2cpp_parse.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

il2cpp::Il2CppString* MakeStr(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

// The icalls take the generic object slot type; a managed string converts
// via its embedded object header (first member, offset 0).
il2cpp::Il2CppObject* AsObj(il2cpp::Il2CppString* s)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(s);
}

struct I4 { int32_t ok; int32_t value; };

I4 TryI4(const char* text)
{
    int32_t out = 0;
    const int32_t ok = il2cpp::runtime::icall_try_parse_int32(
        text != nullptr ? AsObj(MakeStr(text)) : nullptr, &out);
    return I4{ok, out};
}

void ExpectI4(const char* text, int32_t expectedOk, int32_t expectedValue, const char* what)
{
    const I4 r = TryI4(text);
    Expect(r.ok == expectedOk && r.value == expectedValue, what);
}

struct I8 { int32_t ok; int64_t value; };

I8 TryI8(const char* text)
{
    int64_t out = 0;
    const int32_t ok = il2cpp::runtime::icall_try_parse_int64(
        text != nullptr ? AsObj(MakeStr(text)) : nullptr, &out);
    return I8{ok, out};
}

void ExpectI8(const char* text, int32_t expectedOk, int64_t expectedValue, const char* what)
{
    const I8 r = TryI8(text);
    Expect(r.ok == expectedOk && r.value == expectedValue, what);
}

struct D { int32_t ok; double value; };

D TryD(const char* text)
{
    double out = 0.0;
    const int32_t ok = il2cpp::runtime::icall_try_parse_double(
        text != nullptr ? AsObj(MakeStr(text)) : nullptr, &out);
    return D{ok, out};
}

void ExpectD(const char* text, int32_t expectedOk, const char* what)
{
    const D r = TryD(text);
    Expect(r.ok == expectedOk, what);
}

void ExpectDIs(const char* text, double expected, const char* what)
{
    const D r = TryD(text);
    Expect(r.ok == 1 && r.value == expected, what);
}

void ExpectDNaN(const char* text, const char* what)
{
    const D r = TryD(text);
    Expect(r.ok == 1 && std::isnan(r.value), what);
}

// Wide (UTF-16) double.TryParse probe -- the culture infinity words carry
// U+221E, which no ASCII literal can express.
D TryDWide(const char16_t* units, int32_t len)
{
    double out = 0.0;
    const int32_t ok = il2cpp::runtime::icall_try_parse_double(
        AsObj(il2cpp::il2cpp_string_new(units, len)), &out);
    return D{ok, out};
}

struct B { int32_t ok; int32_t value; };

B TryB(const char* text)
{
    int32_t out = 0;
    const int32_t ok = il2cpp::runtime::icall_try_parse_bool(
        text != nullptr ? AsObj(MakeStr(text)) : nullptr, &out);
    return B{ok, out};
}

void ExpectB(const char* text, int32_t expectedOk, int32_t expectedValue, const char* what)
{
    const B r = TryB(text);
    Expect(r.ok == expectedOk && r.value == expectedValue, what);
}

void ExpectByte(const char* text, int32_t expectedOk, int32_t expectedValue, const char* what)
{
    int32_t out = 0;
    const int32_t ok = il2cpp::runtime::icall_try_parse_byte(
        text != nullptr ? AsObj(MakeStr(text)) : nullptr, &out);
    Expect(ok == expectedOk && out == expectedValue, what);
}

void ExpectBase(const char* text, int32_t fromBase, int32_t expectedOk, int32_t expectedValue,
                const char* what)
{
    int32_t out = 0;
    const int32_t ok = il2cpp::runtime::icall_try_parse_int32_base(
        text != nullptr ? AsObj(MakeStr(text)) : nullptr, fromBase, &out);
    Expect(ok == expectedOk && out == expectedValue, what);
}

// The invalid-radix contract: net48 throws ArgumentException for any base
// outside {2,8,10,16}, BEFORE looking at the text. e.type_name() carries the
// full throw chain ("System.ArgumentException,..."), so this is a prefix
// match on the first element.
void ExpectBaseThrowsImpl(int32_t fromBase, bool nullText, const char* what)
{
    bool caught = false;
    try
    {
        int32_t out = 0;
        (void)il2cpp::runtime::icall_try_parse_int32_base(
            nullText ? nullptr : AsObj(MakeStr("42")), fromBase, &out);
    }
    catch (const il2cpp::DotNetException& e)
    {
        caught = true;
        Expect(std::strncmp(e.type_name(), "System.ArgumentException",
                            std::strlen("System.ArgumentException")) == 0,
               "invalid-base exception type is ArgumentException");
    }
    catch (...)
    {
        caught = true;
        Expect(false, "wrong exception carrier type (not DotNetException)");
    }
    Expect(caught, what);
}

void ExpectBaseThrows(int32_t fromBase, const char* what)
{
    ExpectBaseThrowsImpl(fromBase, false, what);
}

void ExpectBaseThrowsNull(int32_t fromBase, const char* what)
{
    ExpectBaseThrowsImpl(fromBase, true, what);
}

} // namespace

int main()
{
    // --- 1. int32.TryParse ---
    ExpectI4("42", 1, 42, "try-parse \"42\" -> 42");
    ExpectI4("-42", 1, -42, "try-parse \"-42\" -> -42");
    ExpectI4("+42", 1, 42, "try-parse \"+42\" -> 42");
    ExpectI4("  42  ", 1, 42, "try-parse whitespace-padded -> 42");
    ExpectI4("\t42\n", 1, 42, "try-parse tab/newline padding -> 42");
    ExpectI4("00042", 1, 42, "try-parse leading zeros \"00042\" -> 42");
    ExpectI4("0", 1, 0, "try-parse \"0\" -> 0");
    ExpectI4("-0", 1, 0, "try-parse \"-0\" -> 0");
    ExpectI4("2147483647", 1, INT32_MAX, "try-parse int32 max round-trips");
    ExpectI4("-2147483648", 1, INT32_MIN, "try-parse int32 min round-trips");
    ExpectI4("00000000000000000000042", 1, 42,
             "try-parse many leading zeros -> 42 (no digit-limit overflow)");
    ExpectI4(nullptr, 0, 0, "try-parse null text -> 0");
    ExpectI4("", 0, 0, "try-parse empty text -> 0");
    ExpectI4("   ", 0, 0, "try-parse whitespace-only text -> 0");
    ExpectI4("abc", 0, 0, "try-parse \"abc\" -> 0");
    ExpectI4("12a", 0, 0, "try-parse \"12a\" -> 0 (trailing junk)");
    ExpectI4("1 2", 0, 0, "try-parse \"1 2\" -> 0 (embedded whitespace)");
    ExpectI4("++1", 0, 0, "try-parse \"++1\" -> 0");
    ExpectI4("--1", 0, 0, "try-parse \"--1\" -> 0");
    ExpectI4("+", 0, 0, "try-parse bare sign -> 0");
    ExpectI4("2147483648", 0, 0, "try-parse \"2147483648\" -> 0 (overflow)");
    ExpectI4("-2147483649", 0, 0, "try-parse \"-2147483649\" -> 0 (overflow)");
    ExpectI4("99999999999999999999", 0, 0, "try-parse 20-digit text -> 0 (overflow)");
    ExpectI4("-99999999999999999999", 0, 0, "try-parse negative 20-digit text -> 0");

    // --- 2. int64.TryParse ---
    ExpectI8("0", 1, INT64_C(0), "try-parse int64 \"0\" -> 0");
    ExpectI8("-0", 1, INT64_C(0), "try-parse int64 \"-0\" -> 0");
    ExpectI8("9223372036854775807", 1, INT64_MAX, "try-parse int64 max round-trips");
    ExpectI8("-9223372036854775808", 1, INT64_MIN, "try-parse int64 min round-trips");
    ExpectI8("9223372036854775808", 0, 0, "try-parse int64 one-past-max -> 0");
    ExpectI8("-9223372036854775809", 0, 0, "try-parse int64 one-past-min -> 0");
    ExpectI8("99999999999999999999", 0, 0, "try-parse int64 20-digit text -> 0");
    ExpectI8(" 1234567890123456789 ", 1, INT64_C(1234567890123456789),
             "try-parse int64 19-digit with padding round-trips");

    // --- 3. double.TryParse ---
    ExpectDNaN("NaN", "try-parse \"NaN\" -> NaN (success)");
    ExpectD("Infinity", 0, "try-parse \"Infinity\" -> 0 (net48 zh-CN rejects ASCII infinity)");
    ExpectD("-Infinity", 0, "try-parse \"-Infinity\" -> 0 (net48 zh-CN rejects ASCII -infinity)");
    {
        // zh-CN culture infinity symbols (net48 probe-pinned): U+221E ->
        // +Infinity, '-' + U+221E -> -Infinity, ASCII-whitespace padded.
        const char16_t pos[] = { u'\u221E' };
        const D r = TryDWide(pos, 1);
        Expect(r.ok == 1 && std::isinf(r.value) && r.value > 0,
               "try-parse U+221E -> +Infinity (zh-CN infinity symbol)");
    }
    {
        const char16_t neg[] = { u'-', u'\u221E' };
        const D r = TryDWide(neg, 2);
        Expect(r.ok == 1 && std::isinf(r.value) && r.value < 0,
               "try-parse \"-\" + U+221E -> -Infinity");
    }
    {
        const char16_t pad[] = { u' ', u'\u221E', u' ' };
        const D r = TryDWide(pad, 3);
        Expect(r.ok == 1 && std::isinf(r.value) && r.value > 0,
               "try-parse padded U+221E -> +Infinity");
    }
    {
        const char16_t plus[] = { u'+', u'\u221E' };
        const D r = TryDWide(plus, 2);
        Expect(r.ok == 0, "try-parse \"+\" + U+221E -> 0 (sign not combined with the symbol)");
    }
    {
        const char16_t junk[] = { u'\u221E', u'x' };
        const D r = TryDWide(junk, 2);
        Expect(r.ok == 0, "try-parse U+221E + \"x\" -> 0 (trailing junk)");
    }
    ExpectDIs("1.5", 1.5, "try-parse \"1.5\" -> 1.5");
    ExpectDIs("  -2.25  ", -2.25, "try-parse padded \"-2.25\" -> -2.25");
    ExpectDIs(".5", 0.5, "try-parse \".5\" -> 0.5");
    ExpectDIs("5.", 5.0, "try-parse \"5.\" -> 5.0");
    ExpectDIs("1e5", 100000.0, "try-parse \"1e5\" -> 100000.0");
    ExpectDIs("1E+5", 100000.0, "try-parse \"1E+5\" -> 100000.0");
    ExpectDIs("1.5e-3", 0.0015, "try-parse \"1.5e-3\" -> 0.0015");
    ExpectDIs("  42  ", 42.0, "try-parse padded \"42\" -> 42.0");
    {
        const D r = TryD("-0");
        Expect(r.ok == 1 && r.value == 0.0 && std::signbit(r.value),
               "try-parse \"-0\" -> negative zero (sign preserved)");
    }
    ExpectDIs("1e-999", 0.0, "try-parse \"1e-999\" -> 0.0 (underflow, success)");
    ExpectD(nullptr, 0, "try-parse double null text -> 0");
    ExpectD("", 0, "try-parse double empty text -> 0");
    ExpectD("   ", 0, "try-parse double whitespace-only -> 0");
    ExpectD("abc", 0, "try-parse double \"abc\" -> 0");
    ExpectD("inf", 0, "try-parse double \"inf\" -> 0 (abbreviated infinity illegal)");
    ExpectD("0x10", 0, "try-parse double \"0x10\" -> 0 (hex floats illegal)");
    ExpectD("1e", 0, "try-parse double \"1e\" -> 0 (exponent needs digits)");
    ExpectD("1 2", 0, "try-parse double \"1 2\" -> 0 (embedded whitespace)");
    ExpectD("1.2.3", 0, "try-parse double \"1.2.3\" -> 0 (two separators)");
    ExpectDIs("1,000", 1000.0, "try-parse double \"1,000\" -> 1000.0 (AllowThousands; net48 oracle)");
    ExpectD("1e999", 0, "try-parse double \"1e999\" -> 0 (overflow -> false, no throw)");
    ExpectD("-1e999", 0, "try-parse double \"-1e999\" -> 0");
    ExpectD("nan", 0, "try-parse double \"nan\" -> 0 (net48 is case-sensitive)");
    ExpectD("+Infinity", 0, "try-parse double \"+Infinity\" -> 0 (not a net48 spelling)");

    // --- 4. bool.TryParse (case-insensitive) ---
    ExpectB("true", 1, 1, "try-parse bool \"true\" -> true");
    ExpectB("True", 1, 1, "try-parse bool \"True\" -> true (case-insensitive)");
    ExpectB("TRUE", 1, 1, "try-parse bool \"TRUE\" -> true");
    ExpectB("false", 1, 0, "try-parse bool \"false\" -> false");
    ExpectB("False", 1, 0, "try-parse bool \"False\" -> false");
    ExpectB("FALSE", 1, 0, "try-parse bool \"FALSE\" -> false");
    ExpectB("  true  ", 1, 1, "try-parse bool padded \"true\" -> true");
    ExpectB("\ttrue\n", 1, 1, "try-parse bool tab/newline padded -> true");
    ExpectB("TruE", 1, 1, "try-parse bool \"TruE\" -> true (mixed case)");
    ExpectB(nullptr, 0, 0, "try-parse bool null text -> 0");
    ExpectB("", 0, 0, "try-parse bool empty text -> 0");
    ExpectB("   ", 0, 0, "try-parse bool whitespace-only -> 0");
    ExpectB("1", 0, 0, "try-parse bool \"1\" -> 0");
    ExpectB("tru", 0, 0, "try-parse bool \"tru\" -> 0 (prefix is not a match)");
    ExpectB("truefalse", 0, 0, "try-parse bool \"truefalse\" -> 0");
    ExpectB("tr ue", 0, 0, "try-parse bool \"tr ue\" -> 0 (embedded whitespace)");
    ExpectB("yes", 0, 0, "try-parse bool \"yes\" -> 0");

    // --- 5. byte.TryParse ---
    ExpectByte("0", 1, 0, "try-parse byte \"0\" -> 0");
    ExpectByte("255", 1, 255, "try-parse byte \"255\" -> 255 (max)");
    ExpectByte("+255", 1, 255, "try-parse byte \"+255\" -> 255");
    ExpectByte(" 42 ", 1, 42, "try-parse byte padded \"42\" -> 42");
    ExpectByte("256", 0, 0, "try-parse byte \"256\" -> 0 (overflow)");
    ExpectByte("-1", 0, 0, "try-parse byte \"-1\" -> 0 (negative)");
    ExpectByte("2559", 0, 0, "try-parse byte \"2559\" -> 0 (overflow)");
    ExpectByte("12a", 0, 0, "try-parse byte \"12a\" -> 0 (junk)");
    ExpectByte(nullptr, 0, 0, "try-parse byte null text -> 0");

    // --- 6. Convert-style base parsing (2/8/10/16). net48 empirical
    // semantics (ParseNumbers.StringToInt with IsTight|NoSpace): NO
    // whitespace for any radix; '+' accepted for every radix, '-' only for
    // radix 10; radix 16 strips a "0x"/"0X" prefix; radix 2/8/16 accumulate
    // in uint32 with WRAPAROUND up to a 32-bit cap ("80000000",16 ->
    // INT32_MIN, "FFFFFFFF",16 -> -1) and FAIL past UINT32_MAX
    // ("100000000",16, 11 octal digits); radix 10 range-checks ---
    ExpectBase("1010", 2, 1, 10, "try-parse base-2 \"1010\" -> 10");
    ExpectBase("-101", 2, 0, 0, "try-parse base-2 \"-101\" -> 0 (radix 2 rejects '-')");
    ExpectBase("+10", 2, 1, 2, "try-parse base-2 \"+10\" -> 2 (radix 2 accepts '+')");
    ExpectBase("11111111111111111111111111111111", 2, 1, -1,
               "try-parse base-2 32 ones -> -1 (uint32 wraparound, net48)");
    ExpectBase("111111111111111111111111111111111", 2, 0, 0,
               "try-parse base-2 33 ones -> 0 (past 32-bit cap, net48 overflow)");
    ExpectBase("100000000000000000000000000000000", 2, 0, 0,
               "try-parse base-2 2^32 -> 0 (past 32-bit cap)");
    ExpectBase("2", 2, 0, 0, "try-parse base-2 \"2\" -> 0 (invalid digit)");
    ExpectBase("0b101", 2, 0, 0, "try-parse base-2 \"0b101\" -> 0 (no prefix for base 2)");
    ExpectBase("0x10", 2, 0, 0, "try-parse base-2 \"0x10\" -> 0 (prefix only for base 16)");
    ExpectBase("777", 8, 1, 511, "try-parse base-8 \"777\" -> 511");
    ExpectBase("0x10", 8, 0, 0, "try-parse base-8 \"0x10\" -> 0 (prefix only for base 16)");
    ExpectBase("-10", 8, 0, 0, "try-parse base-8 \"-10\" -> 0 (radix 8 rejects '-')");
    ExpectBase("+10", 8, 1, 8, "try-parse base-8 \"+10\" -> 8 (radix 8 accepts '+')");
    ExpectBase("7777777777", 8, 1, 1073741823, "try-parse base-8 10 digits -> 1073741823");
    ExpectBase("77777777777", 8, 0, 0,
               "try-parse base-8 11 digits -> 0 (past 32-bit cap, net48 overflow)");
    ExpectBase("18", 8, 0, 0, "try-parse base-8 \"18\" -> 0 (8 is invalid octal digit)");
    ExpectBase("42", 10, 1, 42, "try-parse base-10 \"42\" -> 42");
    ExpectBase("-42", 10, 1, -42, "try-parse base-10 \"-42\" -> -42 (base 10 allows '-')");
    ExpectBase("+42", 10, 1, 42, "try-parse base-10 \"+42\" -> 42");
    ExpectBase("0x10", 10, 0, 0, "try-parse base-10 \"0x10\" -> 0 (prefix only for base 16)");
    ExpectBase("2147483648", 10, 0, 0, "try-parse base-10 \"2147483648\" -> 0 (range check)");
    ExpectBase("-2147483648", 10, 1, INT32_MIN, "try-parse base-10 int32 min round-trips");
    ExpectBase("FF", 16, 1, 255, "try-parse base-16 \"FF\" -> 255 (uppercase)");
    ExpectBase("ff", 16, 1, 255, "try-parse base-16 \"ff\" -> 255 (lowercase)");
    ExpectBase("-80000000", 16, 0, 0, "try-parse base-16 \"-80000000\" -> 0 (radix 16 rejects '-')");
    ExpectBase("+1F", 16, 1, 31, "try-parse base-16 \"+1F\" -> 31 (radix 16 accepts '+')");
    ExpectBase("80000000", 16, 1, INT32_MIN,
               "try-parse base-16 \"80000000\" -> int32 min (uint32 wraparound, net48)");
    ExpectBase("FFFFFFFF", 16, 1, -1, "try-parse base-16 \"FFFFFFFF\" -> -1 (uint32 wraparound)");
    ExpectBase("100000000", 16, 0, 0,
               "try-parse base-16 \"100000000\" -> 0 (past 32-bit cap, net48 overflow)");
    ExpectBase("FFFFFFFFF", 16, 0, 0,
               "try-parse base-16 \"FFFFFFFFF\" -> 0 (past 32-bit cap, net48 overflow)");
    ExpectBase("7FFFFFFF", 16, 1, INT32_MAX, "try-parse base-16 \"7FFFFFFF\" -> int32 max");
    ExpectBase("0x10", 16, 1, 16, "try-parse base-16 \"0x10\" -> 16 (0x prefix stripped)");
    ExpectBase("0X10", 16, 1, 16, "try-parse base-16 \"0X10\" -> 16 (0X prefix stripped)");
    ExpectBase("0x80000000", 16, 1, INT32_MIN,
               "try-parse base-16 \"0x80000000\" -> int32 min (prefix + wraparound)");
    ExpectBase("0xFFFFFFFF", 16, 1, -1, "try-parse base-16 \"0xFFFFFFFF\" -> -1 (prefix + wrap)");
    ExpectBase("0x", 16, 0, 0, "try-parse base-16 \"0x\" -> 0 (prefix needs digits)");
    ExpectBase("0xG", 16, 0, 0, "try-parse base-16 \"0xG\" -> 0 (prefix then invalid digit)");
    ExpectBase("+0x10", 16, 1, 16, "try-parse base-16 \"+0x10\" -> 16 (sign + prefix)");
    ExpectBase("-0x10", 16, 0, 0, "try-parse base-16 \"-0x10\" -> 0 (radix 16 rejects '-')");
    ExpectBase("g", 16, 0, 0, "try-parse base-16 \"g\" -> 0 (invalid digit)");
    ExpectBase(" 1F ", 16, 0, 0, "try-parse base-16 \" 1F \" -> 0 (no whitespace, net48)");
    ExpectBase(" 0x10 ", 16, 0, 0, "try-parse base-16 \" 0x10 \" -> 0 (no whitespace, net48)");
    ExpectBase(" 42 ", 10, 0, 0, "try-parse base-10 \" 42 \" -> 0 (no whitespace, net48)");
    // The radix contract (MEASURED net48): the legal set is {2,8,10,16}
    // EXACTLY; any other base THROWS ArgumentException and is checked BEFORE
    // the text (so a null value with a bad base throws too). The previous
    // "invalid base -> 0" pins locked in this runtime's bug.
    ExpectBaseThrows(5, "try-parse base 5 -> ArgumentException (net48)");
    ExpectBaseThrows(0, "try-parse base 0 -> ArgumentException");
    ExpectBaseThrows(-1, "try-parse base -1 -> ArgumentException");
    ExpectBaseThrows(1, "try-parse base 1 -> ArgumentException");
    ExpectBaseThrows(3, "try-parse base 3 -> ArgumentException");
    ExpectBaseThrows(17, "try-parse base 17 -> ArgumentException");
    ExpectBaseThrows(36, "try-parse base 36 -> ArgumentException");
    ExpectBaseThrows(37, "try-parse base 37 -> ArgumentException");
    ExpectBaseThrowsNull(3, "try-parse base 3 + null text -> ArgumentException (radix first)");
    ExpectBase(nullptr, 16, 0, 0, "try-parse base-16 null text -> 0");

    // --- 7. Contract: failure does not write out; null out -> 0; never throws ---
    {
        int32_t out = static_cast<int32_t>(0xDEADBEEF);
        const int32_t ok = il2cpp::runtime::icall_try_parse_int32(AsObj(MakeStr("abc")), &out);
        Expect(ok == 0 && out == 0,
               "int32 failure writes zero (TryParse contract)");
    }
    {
        int64_t out = static_cast<int64_t>(UINT64_C(0xDEADBEEFDEADBEEF));
        const int32_t ok = il2cpp::runtime::icall_try_parse_int64(AsObj(MakeStr("abc")), &out);
        Expect(ok == 0 && out == 0,
               "int64 failure writes zero (TryParse contract)");
    }
    {
        double out = 123.5;
        const int32_t ok = il2cpp::runtime::icall_try_parse_double(AsObj(MakeStr("abc")), &out);
        Expect(ok == 0 && out == 0.0, "double failure writes zero (TryParse contract)");
    }
    {
        int32_t out = 7;
        const int32_t ok = il2cpp::runtime::icall_try_parse_bool(AsObj(MakeStr("abc")), &out);
        Expect(ok == 0 && out == 0, "bool failure writes zero (TryParse contract)");
    }
    {
        int32_t out = 7;
        const int32_t ok = il2cpp::runtime::icall_try_parse_byte(AsObj(MakeStr("abc")), &out);
        Expect(ok == 0 && out == 0, "byte failure writes zero (TryParse contract)");
    }
    Expect(il2cpp::runtime::icall_try_parse_int32(AsObj(MakeStr("42")), nullptr) == 0,
           "int32 null out pointer -> 0");
    Expect(il2cpp::runtime::icall_try_parse_int64(AsObj(MakeStr("42")), nullptr) == 0,
           "int64 null out pointer -> 0");
    Expect(il2cpp::runtime::icall_try_parse_double(AsObj(MakeStr("42")), nullptr) == 0,
           "double null out pointer -> 0");
    Expect(il2cpp::runtime::icall_try_parse_bool(AsObj(MakeStr("true")), nullptr) == 0,
           "bool null out pointer -> 0");
    Expect(il2cpp::runtime::icall_try_parse_byte(AsObj(MakeStr("42")), nullptr) == 0,
           "byte null out pointer -> 0");
    Expect(il2cpp::runtime::icall_try_parse_int32_base(AsObj(MakeStr("42")), 16, nullptr) == 0,
           "base null out pointer -> 0");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d parse assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime parse tests passed\n");
    return 0;
}
