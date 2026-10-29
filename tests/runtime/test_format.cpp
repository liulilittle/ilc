//------------------------------------------------------------------------------
// test_format.cpp -- numeric formatting icall unit tests (il2cpp_format.h).
//
// Coverage (normal + edge + error paths):
//   1. int32 decimal: 0, positive, negative, INT32_MAX, INT32_MIN.
//   2. int64 decimal: 0, -1, INT64_MAX, INT64_MIN, long magnitude.
//   3. bool: "True" / "False" (capitalized, .NET semantics).
//   4. char: single UTF-16 unit (ASCII, NUL unit, astral-adjacent BMP unit).
//   5. double ToString (net48 "G15" baseline): 0, -0.0 -> "0", 1.5, 1/3
//      (15 digits), PI, 0.1+0.2 -> "0.3" (net48, NOT .NET Core 3.0+), the
//      fixed/scientific boundaries (1e-5 scientific, 1e-6 scientific, 1e14
//      fixed, 1e15 scientific -- all net48-verified), big/small exponents,
//      NaN, +-Infinity, double.Epsilon.
//   6. int32 hex: 0 -> "0", no padding ("FF"), full 8 digits for negatives
//      ("FFFFFFFF", "80000000").
//   7. int.Parse: whitespace/sign/leading zeros, round-trip of the int32
//      range boundaries; FormatException (empty/junk/embedded junk) and
//      OverflowException (one past the range, >10 significant digits).
//   8. double.Parse: legal texts (fraction, exponent, ".5", "5.", specials,
//      whitespace, negative zero); the zh-CN culture infinity words U+221E /
//      "-" + U+221E -> +/-Infinity and the ASCII "Infinity"/"-Infinity"
//      words -> FormatException (net48 zh-CN probe-pinned); FormatException
//      (junk, "inf", hex float, incomplete exponent); OverflowException
//      ("1e999"); underflow -> 0.
//
// Plain executable + minimal assertion harness (see test_string.cpp): any
// failed Expect() prints FAIL and the process exits non-zero.
//------------------------------------------------------------------------------

#include "il2cpp_format.h"
#include "il2cpp_exception.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <string>

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

// Exact UTF-16 comparison of a managed string against an ASCII literal.
bool StringIs(il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const size_t asciiLen = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != asciiLen)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// True when icall_string_parse_int throws a managed exception matching
// 'typeName' for the given ASCII text.
bool ParseIntThrows(const char* text, const char* typeName)
{
    il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs(text);
    try
    {
        il2cpp::runtime::icall_string_parse_int(s);
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches(typeName, e.type_name());
    }
    return false;
}

// True when icall_string_parse_double throws a managed exception matching
// 'typeName' for the given ASCII text.
bool ParseDoubleThrows(const char* text, const char* typeName)
{
    il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs(text);
    try
    {
        il2cpp::runtime::icall_string_parse_double(s);
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches(typeName, e.type_name());
    }
    return false;
}

// int32 parse helper: text -> expected value.
bool ParseIntIs(const char* text, int32_t expected)
{
    il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs(text);
    try
    {
        return il2cpp::runtime::icall_string_parse_int(s) == expected;
    }
    catch (const il2cpp::DotNetException&)
    {
        return false;
    }
}

// double parse helper: text -> expected value (exact equality is fine: the
// test values are exactly representable; specials use isnan/isinf below).
bool ParseDoubleIs(const char* text, double expected)
{
    il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs(text);
    try
    {
        return il2cpp::runtime::icall_string_parse_double(s) == expected;
    }
    catch (const il2cpp::DotNetException&)
    {
        return false;
    }
}

} // namespace

int main()
{
    using il2cpp::runtime::icall_int32_to_string;
    using il2cpp::runtime::icall_int64_to_string;
    using il2cpp::runtime::icall_uint32_to_string;
    using il2cpp::runtime::icall_uint64_to_string;
    using il2cpp::runtime::icall_bool_to_string;
    using il2cpp::runtime::icall_char_to_string;
    using il2cpp::runtime::icall_double_to_string;
    using il2cpp::runtime::icall_single_to_string;
    using il2cpp::runtime::icall_int32_to_string_hex;

    // --- 1. int32 decimal ---
    Expect(StringIs(icall_int32_to_string(0), "0"), "int32 0 formats as \"0\"");
    Expect(StringIs(icall_int32_to_string(42), "42"), "int32 42 formats as \"42\"");
    Expect(StringIs(icall_int32_to_string(-42), "-42"), "int32 -42 formats as \"-42\"");
    Expect(StringIs(icall_int32_to_string(INT32_MAX), "2147483647"),
           "int32 max formats as \"2147483647\"");
    Expect(StringIs(icall_int32_to_string(INT32_MIN), "-2147483648"),
           "int32 min formats as \"-2147483648\"");

    // --- 2. int64 decimal ---
    Expect(StringIs(icall_int64_to_string(INT64_C(0)), "0"), "int64 0 formats as \"0\"");
    Expect(StringIs(icall_int64_to_string(INT64_C(-1)), "-1"), "int64 -1 formats as \"-1\"");
    Expect(StringIs(icall_int64_to_string(INT64_C(1234567890123)), "1234567890123"),
           "int64 magnitude formats without grouping");
    Expect(StringIs(icall_int64_to_string(INT64_MAX), "9223372036854775807"),
           "int64 max formats as \"9223372036854775807\"");
    Expect(StringIs(icall_int64_to_string(INT64_MIN), "-9223372036854775808"),
           "int64 min formats as \"-9223372036854775808\" (no UB)");

    // --- 2b. uint32/uint64 decimal (decimal-domain C44: full unsigned range) ---
    Expect(StringIs(icall_uint32_to_string(0U), "0"), "uint32 0 formats as \"0\"");
    Expect(StringIs(icall_uint32_to_string(4294967295U), "4294967295"),
           "uint32 max formats as \"4294967295\" (never the signed -1)");
    Expect(StringIs(icall_uint32_to_string(2147483648U), "2147483648"),
           "uint32 2^31 formats without sign");
    Expect(StringIs(icall_uint64_to_string(0ULL), "0"), "uint64 0 formats as \"0\"");
    Expect(StringIs(icall_uint64_to_string(9223372036854775807ULL), "9223372036854775807"),
           "uint64 INT64_MAX formats as \"9223372036854775807\"");
    Expect(StringIs(icall_uint64_to_string(18446744073709551615ULL), "18446744073709551615"),
           "uint64 max formats as \"18446744073709551615\" (never the signed -1)");
    Expect(StringIs(icall_uint64_to_string(9223372036854775808ULL), "9223372036854775808"),
           "uint64 2^63 formats without sign");

    // --- 3. bool ---
    Expect(StringIs(icall_bool_to_string(true), "True"), "true formats as \"True\" (capital T)");
    Expect(StringIs(icall_bool_to_string(false), "False"), "false formats as \"False\" (capital F)");

    // --- 4. char ---
    Expect(StringIs(icall_char_to_string(u'A'), "A"), "char 'A' formats as \"A\"");
    il2cpp::Il2CppString* nulChar = icall_char_to_string(0);
    Expect(nulChar != nullptr && nulChar->length == 1 && nulChar->chars[0] == u'\0',
           "char 0 formats as a 1-unit string containing NUL");
    il2cpp::Il2CppString* bmpChar = icall_char_to_string(0x4E2D);
    Expect(bmpChar != nullptr && bmpChar->length == 1 && bmpChar->chars[0] == u'\u4E2D',
           "BMP char keeps its UTF-16 unit");

    // --- 5. double ToString (net48 "G15" baseline) ---
    Expect(StringIs(icall_double_to_string(0.0), "0"), "double 0 formats as \"0\"");
    Expect(StringIs(icall_double_to_string(-0.0), "0"),
           "negative zero formats as \"0\" (.NET drops the sign)");
    Expect(StringIs(icall_double_to_string(1.0), "1"), "double 1 formats as \"1\"");
    Expect(StringIs(icall_double_to_string(1.5), "1.5"), "double 1.5 formats as \"1.5\"");
    Expect(StringIs(icall_double_to_string(-2.25), "-2.25"), "double -2.25 formats as \"-2.25\"");
    Expect(StringIs(icall_double_to_string(1.0 / 3.0), "0.333333333333333"),
           "1/3 formats with 15 significant digits (net48 G15)");
    Expect(StringIs(icall_double_to_string(3.14159265358979323846), "3.14159265358979"),
           "pi formats as \"3.14159265358979\" (15 digits)");
    Expect(StringIs(icall_double_to_string(0.1 + 0.2), "0.3"),
           "0.1+0.2 formats as \"0.3\" (net48; .NET Core 3.0+ differs)");
    Expect(StringIs(icall_double_to_string(1e-5), "1E-05"),
           "1e-5 formats scientific (exponent -5 is scientific in .NET; net48 verified)");
    Expect(StringIs(icall_double_to_string(1e-6), "1E-06"),
           "1e-6 formats scientific (exponent -6)");
    Expect(StringIs(icall_double_to_string(100000000000000.0), "100000000000000"),
           "1e14 formats fixed (exponent 14)");
    Expect(StringIs(icall_double_to_string(1e15), "1E+15"),
           "1e15 formats scientific (exponent >= 15)");
    Expect(StringIs(icall_double_to_string(1.5e20), "1.5E+20"),
           "1.5e20 formats scientific with a fraction");
    Expect(StringIs(icall_double_to_string(2.5e-10), "2.5E-10"),
           "2.5e-10 formats scientific");
    Expect(StringIs(icall_double_to_string(123456789012345678.0), "1.23456789012346E+17"),
           "large value rounds to 15 digits in scientific form");
    Expect(StringIs(icall_double_to_string(4.9406564584124654e-324), "4.94065645841247E-324"),
           "double.Epsilon formats as \"4.94065645841247E-324\"");
    Expect(StringIs(icall_double_to_string(std::nan("")), "NaN"), "NaN formats as \"NaN\"");
    {
        il2cpp::Il2CppString* inf = icall_double_to_string(
            std::numeric_limits<double>::infinity());
        Expect(inf != nullptr && inf->length == 1 && inf->chars[0] == u'\u221E',
               "positive infinity formats as the culture symbol U+221E");
    }
    {
        il2cpp::Il2CppString* ninf = icall_double_to_string(
            -std::numeric_limits<double>::infinity());
        Expect(ninf != nullptr && ninf->length == 2 && ninf->chars[0] == u'-'
                   && ninf->chars[1] == u'\u221E',
               "negative infinity formats as \"-\" + U+221E");
    }

    // --- 5b. Single.ToString() no-argument (net48 float "G7" text) ---
    // miscfmt: the default float text is the FLOAT G7 form (3.14159265f ->
    // "3.141593", 2^24 -> "1.677722E+07"), and a zero magnitude drops the
    // sign (-0.0f -> "0"). Oracle: probe-miscfmt (net48 host capture
    // 2026-10-29).
    Expect(StringIs(icall_single_to_string(0.0), "0"), "single 0 formats as \"0\"");
    Expect(StringIs(icall_single_to_string(-0.0), "0"),
           "single -0.0 formats as \"0\" (net48 drops the float zero sign)");
    Expect(StringIs(icall_single_to_string(static_cast<double>(3.14159265f)), "3.141593"),
           "single pi formats with 7 significant digits (net48 G7)");
    Expect(StringIs(icall_single_to_string(static_cast<double>(0.1f)), "0.1"),
           "single 0.1 formats as \"0.1\"");
    Expect(StringIs(icall_single_to_string(static_cast<double>(1.0f / 3.0f)), "0.3333333"),
           "single 1/3 formats as \"0.3333333\" (7 digits, not shortest round-trip)");
    Expect(StringIs(icall_single_to_string(16777216.0), "1.677722E+07"),
           "single 2^24 formats scientific with 7 digits");
    Expect(StringIs(icall_single_to_string(static_cast<double>(1e-8f)), "1E-08"),
           "single 1e-8 formats as \"1E-08\"");
    Expect(StringIs(icall_single_to_string(static_cast<double>(1e-5f)), "1E-05"),
           "single 1e-5 formats as \"1E-05\"");
    Expect(StringIs(icall_single_to_string(static_cast<double>(3.40282347e38f)),
                    "3.402823E+38"),
           "single float.MaxValue formats as \"3.402823E+38\"");
    Expect(StringIs(icall_single_to_string(std::nan("")), "NaN"),
           "single NaN formats as \"NaN\"");
    {
        il2cpp::Il2CppString* inf = icall_single_to_string(
            std::numeric_limits<double>::infinity());
        Expect(inf != nullptr && inf->length == 1 && inf->chars[0] == u'\u221E',
               "single positive infinity formats as the culture symbol U+221E");
    }

    // --- 6. int32 hex ---
    Expect(StringIs(icall_int32_to_string_hex(0), "0"), "hex 0 formats as \"0\"");
    Expect(StringIs(icall_int32_to_string_hex(1), "1"), "hex 1 formats as \"1\" (no padding)");
    Expect(StringIs(icall_int32_to_string_hex(255), "FF"), "hex 255 formats as \"FF\"");
    Expect(StringIs(icall_int32_to_string_hex(16), "10"), "hex 16 formats as \"10\"");
    Expect(StringIs(icall_int32_to_string_hex(0xABCDEF), "ABCDEF"),
           "hex 0xABCDEF formats as \"ABCDEF\" (uppercase)");
    Expect(StringIs(icall_int32_to_string_hex(-1), "FFFFFFFF"),
           "hex -1 formats as \"FFFFFFFF\" (twos complement)");
    Expect(StringIs(icall_int32_to_string_hex(INT32_MIN), "80000000"),
           "hex int.MinValue formats as \"80000000\"");

    // --- 7. int.Parse ---
    Expect(ParseIntIs("42", 42), "parse \"42\" -> 42");
    Expect(ParseIntIs("  -42  ", -42), "parse whitespace-padded negative -> -42");
    Expect(ParseIntIs("+123", 123), "parse \"+123\" -> 123");
    Expect(ParseIntIs("00042", 42), "parse leading zeros \"00042\" -> 42");
    Expect(ParseIntIs("\t42\n", 42), "parse tab/newline padding -> 42");
    Expect(ParseIntIs("0", 0), "parse \"0\" -> 0");
    Expect(ParseIntIs("-0", 0), "parse \"-0\" -> 0");
    Expect(ParseIntIs("2147483647", INT32_MAX), "parse int32 max round-trips");
    Expect(ParseIntIs("-2147483648", INT32_MIN), "parse int32 min round-trips");
    Expect(ParseIntIs("00000000000000000000042", 42),
           "parse many leading zeros -> 42 (no digit-limit overflow)");
    Expect(ParseIntThrows("", "System.FormatException"),
           "parse empty text -> FormatException");
    Expect(ParseIntThrows("   ", "System.FormatException"),
           "parse whitespace-only text -> FormatException");
    Expect(ParseIntThrows("abc", "System.FormatException"),
           "parse \"abc\" -> FormatException");
    Expect(ParseIntThrows("12a", "System.FormatException"),
           "parse \"12a\" -> FormatException (trailing junk)");
    Expect(ParseIntThrows("1 2", "System.FormatException"),
           "parse \"1 2\" -> FormatException (embedded whitespace)");
    Expect(ParseIntThrows("++1", "System.FormatException"),
           "parse \"++1\" -> FormatException");
    Expect(ParseIntThrows("2147483648", "System.OverflowException"),
           "parse \"2147483648\" -> OverflowException");
    Expect(ParseIntThrows("-2147483649", "System.OverflowException"),
           "parse \"-2147483649\" -> OverflowException");
    Expect(ParseIntThrows("99999999999999999999", "System.OverflowException"),
           "parse 20-digit text -> OverflowException (not FormatException)");

    // --- 8. double.Parse ---
    Expect(ParseDoubleIs("1.5", 1.5), "parse \"1.5\" -> 1.5");
    Expect(ParseDoubleIs("  -2.25  ", -2.25), "parse padded \"-2.25\" -> -2.25");
    Expect(ParseDoubleIs(".5", 0.5), "parse \".5\" -> 0.5");
    Expect(ParseDoubleIs("5.", 5.0), "parse \"5.\" -> 5.0");
    Expect(ParseDoubleIs("1e5", 100000.0), "parse \"1e5\" -> 100000.0");
    Expect(ParseDoubleIs("1E+5", 100000.0), "parse \"1E+5\" -> 100000.0");
    Expect(ParseDoubleIs("1.5e-3", 0.0015), "parse \"1.5e-3\" -> 0.0015");
    Expect(ParseDoubleIs("  42  ", 42.0), "parse padded \"42\" -> 42.0");
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs("NaN");
        Expect(std::isnan(il2cpp::runtime::icall_string_parse_double(s)),
               "parse \"NaN\" -> NaN");
    }
    {
        // zh-CN culture infinity symbols (net48 probe-pinned): U+221E parses
        // to +Infinity, '-' + U+221E to -Infinity; the ASCII words are NOT
        // legal and throw FormatException.
        const char16_t posUnits[] = { u'\u221E' };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(posUnits, 1);
        const double v = il2cpp::runtime::icall_string_parse_double(s);
        Expect(std::isinf(v) && v > 0, "parse U+221E -> +Infinity (zh-CN infinity symbol)");
    }
    {
        const char16_t negUnits[] = { u'-', u'\u221E' };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(negUnits, 2);
        const double v = il2cpp::runtime::icall_string_parse_double(s);
        Expect(std::isinf(v) && v < 0, "parse \"-\" + U+221E -> -Infinity");
    }
    {
        const char16_t padUnits[] = { u' ', u'\u221E', u' ' };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(padUnits, 3);
        const double v = il2cpp::runtime::icall_string_parse_double(s);
        Expect(std::isinf(v) && v > 0, "parse padded U+221E -> +Infinity");
    }
    Expect(ParseDoubleThrows("Infinity", "System.FormatException"),
           "parse \"Infinity\" -> FormatException (zh-CN rejects ASCII infinity)");
    Expect(ParseDoubleThrows("-Infinity", "System.FormatException"),
           "parse \"-Infinity\" -> FormatException");
    Expect(ParseDoubleThrows("+Infinity", "System.FormatException"),
           "parse \"+Infinity\" -> FormatException (sign is not combined with the symbol)");
    {
        // "+∞" is NOT legal: the whole-text symbol match has no '+' form.
        const char16_t plusUnits[] = { u'+', u'\u221E' };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(plusUnits, 2);
        bool threw = false;
        try
        {
            (void)il2cpp::runtime::icall_string_parse_double(s);
        }
        catch (const il2cpp::DotNetException& e)
        {
            threw = il2cpp::il2cpp_exception_matches("System.FormatException", e.type_name());
        }
        Expect(threw, "parse \"+\" + U+221E -> FormatException (net48 zh-CN)");
    }
    {
        // Negative zero: .NET preserves the sign of "-0".
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs("-0");
        const double v = il2cpp::runtime::icall_string_parse_double(s);
        Expect(v == 0.0 && std::signbit(v), "parse \"-0\" -> negative zero");
    }
    Expect(ParseDoubleIs("1e-999", 0.0), "parse \"1e-999\" -> 0.0 (underflow, no exception)");
    Expect(ParseDoubleThrows("", "System.FormatException"),
           "parse empty text -> FormatException");
    Expect(ParseDoubleThrows("abc", "System.FormatException"),
           "parse \"abc\" -> FormatException");
    Expect(ParseDoubleThrows("inf", "System.FormatException"),
           "parse \"inf\" -> FormatException (abbreviated infinity is illegal)");
    Expect(ParseDoubleThrows("0x10", "System.FormatException"),
           "parse \"0x10\" -> FormatException (hex floats are illegal)");
    Expect(ParseDoubleThrows("1e", "System.FormatException"),
           "parse \"1e\" -> FormatException (exponent needs digits)");
    Expect(ParseDoubleThrows("1 2", "System.FormatException"),
           "parse \"1 2\" -> FormatException (embedded whitespace)");
    Expect(ParseDoubleThrows("1.2.3", "System.FormatException"),
           "parse \"1.2.3\" -> FormatException (two separators)");
    Expect(ParseDoubleIs("1,000", 1000.0),
           "parse \"1,000\" -> 1000.0 (AllowThousands; saudit2 / net48 oracle)");
    Expect(ParseDoubleThrows("1e999", "System.OverflowException"),
           "parse \"1e999\" -> OverflowException (net48 contract)");
    Expect(ParseDoubleThrows("-1e999", "System.OverflowException"),
           "parse \"-1e999\" -> OverflowException");
    {
        // Long text must NOT be truncated: trailing junk beyond 63 chars still
        // throws FormatException (a truncated buffer would have parsed "1.5").
        const std::string junk = "1.5" + std::string(80, ' ') + "x";
        Expect(ParseDoubleThrows(junk.c_str(), "System.FormatException"),
               "parse long text with trailing junk -> FormatException (no truncation)");
    }
    {
        // Long digit text is legal .NET (no length limit): 100 digits parse
        // to ~1.11e99 (a fixed 64-byte buffer would have silently misparsed).
        const std::string big(100, '1');
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs(big.c_str());
        try
        {
            const double v = il2cpp::runtime::icall_string_parse_double(s);
            Expect(v > 1e99 && v < 1.2e99, "parse 100-digit text -> ~1.11e99 (no truncation)");
        }
        catch (const il2cpp::DotNetException&)
        {
            Expect(false, "parse 100-digit text must not throw");
        }
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d format assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime format tests passed\n");
    return 0;
}
