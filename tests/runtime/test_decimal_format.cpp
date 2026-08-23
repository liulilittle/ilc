//------------------------------------------------------------------------------
// test_decimal_format.cpp -- System.Decimal formatting + parsing (T2) runtime
// unit tests. net48 oracle values (the project's semantic baseline; all
// expected strings below are verified .NET Framework 4.8 outputs).
//
// Coverage, mirroring decimal_format.h:
//   1. G default layout: 1.5m -> "1.5" (user oracle), 0.100m keeps trailing
//      zeros (user oracle), negative sign incl. negative zero, scale 0,
//      zero integer part with leading fraction zeros, the 29-digit maximum
//      (exercises all four DecDivMod1E9 groups), 1.50m.
//   2. Format subset: F (fixed, half-away rounding, carry overflow, pad),
//      N (grouping), E (scientific exponent form, specifier-case letter,
//      short-mantissa zero-padding), P (percent x100 + '%'), C (fixed '¥'
//      culture contract); dispatch boundary: unknown standard letters
//      (D/X/Q) -> FormatException, non-standard multi-char patterns fall
//      back to CUSTOM literal passthrough (decimal.ToString("xyz") == "xyz").
//   3. Parse: "123.45" (user oracle), sign, thousands, whitespace, trailing
//      zero/scale preservation, max value, and every overflow path
//      (30 significant digits incl. "79228162514264337593543950335.1" --
//      user oracle; >2^96-1; scale > 28) -> OverflowException; malformed ->
//      FormatException; null -> ArgumentNullException.
//   4. TryParse: never throws; false + untouched output on every failure
//      path, including null and overflow.
//   5. Round-trips parse(toString(d)) == d.
//
// Like the other runtime tests: plain executable, minimal assertion harness
// (Expect prints FAIL and bumps the counter; non-zero exit on failure).
//------------------------------------------------------------------------------

#include "decimal_format.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

using il2cpp::runtime::Il2CppDecimal;
using il2cpp::runtime::icall_decimal_to_string;
using il2cpp::runtime::icall_decimal_to_string_custom;
using il2cpp::runtime::icall_decimal_parse;
using il2cpp::runtime::icall_decimal_try_parse;
using il2cpp::runtime::icall_decimal_round;

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Build a decimal from mantissa+scale+sign (net48 value semantics).
Il2CppDecimal MakeDec(uint64_t lo64, uint32_t hi32, int scale, bool negative)
{
    Il2CppDecimal d;
    d.flags = (negative ? 0x80000000 : 0) | (scale << 16);
    d.hi32 = hi32;
    d.lo64 = lo64;
    return d;
}

Il2CppDecimal MakeDec(uint64_t lo64, int scale) { return MakeDec(lo64, 0, scale, false); }

Il2CppDecimal MakeDec(uint64_t lo64, int scale, bool negative)
{
    return MakeDec(lo64, 0, scale, negative);
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

// UTF-16 expected comparison (for the fixed-culture '¥' currency tests).
bool StringIs16(il2cpp::Il2CppString* s, const char16_t* expected, int32_t len)
{
    if (s == nullptr || s->length != len)
    {
        return false;
    }
    for (int32_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != expected[i])
        {
            return false;
        }
    }
    return true;
}

// G-format a decimal and compare against the expected net48 text.
void ExpectG(const Il2CppDecimal& d, const char* expected)
{
    Expect(StringIs(icall_decimal_to_string(&d), expected), expected);
}

// Custom-format a decimal (format via ASCII literal) and compare.
void ExpectCustom(const Il2CppDecimal& d, const char* fmt, const char* expected)
{
    il2cpp::Il2CppString* f = il2cpp::il2cpp_string_new_from_cs(fmt);
    Expect(StringIs(icall_decimal_to_string_custom(&d, f), expected), expected);
}

// Custom-format must throw FormatException (invalid specifier contract).
void ExpectCustomThrowsFormat(const char* what, const Il2CppDecimal& d, const char* fmt)
{
    bool caught = false;
    try
    {
        (void)icall_decimal_to_string_custom(&d, il2cpp::il2cpp_string_new_from_cs(fmt));
    }
    catch (const il2cpp::DotNetException& e)
    {
        caught = true;
        Expect(std::strncmp(e.type_name(), "System.FormatException",
                            std::strlen("System.FormatException")) == 0,
               "exception type tag matches FormatException");
    }
    catch (...)
    {
        caught = true;
        Expect(false, "wrong exception carrier type (not DotNetException)");
    }
    Expect(caught, what);
}

// Parse-throws expectation (type-tag prefix match on the throw chain).
void ExpectParseThrows(const char* what, const char* typeTag, const char* text)
{
    bool caught = false;
    try
    {
        Il2CppDecimal out = MakeDec(0, 0);
        icall_decimal_parse(&out, il2cpp::il2cpp_string_new_from_cs(text));
    }
    catch (const il2cpp::DotNetException& e)
    {
        caught = true;
        Expect(std::strncmp(e.type_name(), typeTag, std::strlen(typeTag)) == 0,
               "exception type tag matches the managed type");
    }
    catch (...)
    {
        caught = true;
        Expect(false, "wrong exception carrier type (not DotNetException)");
    }
    Expect(caught, what);
}

// decimal.Round(mode) validation: the net48 oracle accepts ONLY 0 (ToEven)
// and 1 (AwayFromZero); every other MidpointRounding value (2/3/4/5/...) throws
// ArgumentException (probe-verified on the reference runtime). D-DR-1 fix.
void ExpectRoundThrowsArg(const char* what, int32_t decimals, int32_t mode)
{
    bool caught = false;
    try
    {
        Il2CppDecimal d = MakeDec(27, 1);   // 2.7m
        icall_decimal_round(&d, &d, decimals, mode);
    }
    catch (const il2cpp::DotNetException& e)
    {
        caught = true;
        Expect(std::strncmp(e.type_name(), "System.ArgumentException",
                            std::strlen("System.ArgumentException")) == 0,
               "exception type tag matches ArgumentException");
    }
    catch (...)
    {
        caught = true;
        Expect(false, "wrong exception carrier type (not DotNetException)");
    }
    Expect(caught, what);
}

// TryParse a text; on success compare the result's G text against expected.
bool TryParseIs(const char* text, const char* expected)
{
    Il2CppDecimal out = MakeDec(0xDEADBEEFDEADBEEFULL, 0xDEADBEEF, 0, false);
    if (!icall_decimal_try_parse(il2cpp::il2cpp_string_new_from_cs(text), &out))
    {
        return false;
    }
    return StringIs(icall_decimal_to_string(&out), expected);
}

} // namespace

int main()
{
    // --- 1. G default layout (net48 oracle; user cases first) ---
    ExpectG(MakeDec(15, 1), "1.5");                    // 1.5m
    ExpectG(MakeDec(100, 3), "0.100");                 // 0.100m keeps trailing zeros
    ExpectG(MakeDec(150, 2), "1.50");                  // 1.50m
    ExpectG(MakeDec(15, 1, true), "-1.5");          // -1.5m
    ExpectG(MakeDec(0, 0), "0");
    ExpectG(MakeDec(0, 3, true), "-0.000");         // negative zero keeps sign
    ExpectG(MakeDec(1000, 0), "1000");                 // scale 0
    ExpectG(MakeDec(1, 4), "0.0001");                  // zero integer part
    ExpectG(MakeDec(12345, 2), "123.45");              // parse oracle round-trip
    // 29-digit maximum (4 x DecDivMod1E9 groups; net48 ToString is fixed).
    ExpectG(MakeDec(0xFFFFFFFFFFFFFFFFULL, 0xFFFFFFFF, 0, false),
            "79228162514264337593543950335");
    // null format == G; "" == G; "G"/"g" == G; precision ignored for G.
    {
        const Il2CppDecimal d = MakeDec(15, 1);
        Expect(StringIs(icall_decimal_to_string_custom(&d, nullptr), "1.5"),
               "custom(null format) == G");
    }
    ExpectCustom(MakeDec(15, 1), "", "1.5");
    ExpectCustom(MakeDec(15, 1), "G", "1.5");
    ExpectCustom(MakeDec(15, 1), "g", "1.5");
    ExpectCustom(MakeDec(15, 1), "G29", "1.5");

    // --- 2. F: fixed, net48 half-AWAY-from-zero rounding ---
    ExpectCustom(MakeDec(15, 1), "F", "1.50");
    ExpectCustom(MakeDec(15, 1), "F2", "1.50");
    ExpectCustom(MakeDec(15, 1), "f0", "2");           // 1.5 rounds up
    ExpectCustom(MakeDec(12345678, 3), "F2", "12345.68");
    ExpectCustom(MakeDec(25, 1), "F0", "3");           // 2.5 half away
    ExpectCustom(MakeDec(25, 1, true), "F0", "-3"); // -2.5
    ExpectCustom(MakeDec(999, 2), "F1", "10.0");       // carry overflow
    ExpectCustom(MakeDec(5, 3), "F2", "0.01");         // 0.005 rounds up
    ExpectCustom(MakeDec(4, 3), "F2", "0.00");         // 0.004 rounds down
    ExpectCustom(MakeDec(15, 1, true), "F2", "-1.50");
    ExpectCustom(MakeDec(0, 2, true), "F2", "-0.00"); // negative zero

    // --- 3. N: grouping of the integer part ---
    ExpectCustom(MakeDec(12345678, 3), "N2", "12,345.68");
    ExpectCustom(MakeDec(1000000, 0), "N0", "1,000,000");
    ExpectCustom(MakeDec(12345, 1, true), "N2", "-1,234.50");
    ExpectCustom(MakeDec(5, 0), "N2", "5.00");

    // --- 4. E: scientific ---
    ExpectCustom(MakeDec(12345678, 3), "E", "1.234568E+004");
    ExpectCustom(MakeDec(12345678, 3), "E2", "1.23E+004");
    ExpectCustom(MakeDec(12345678, 3, true), "E", "-1.234568E+004");
    ExpectCustom(MakeDec(1, 4), "E", "1.000000E-004");
    ExpectCustom(MakeDec(999, 2), "E0", "1E+001");
    ExpectCustom(MakeDec(0, 4), "E2", "0.00E+000");
    ExpectCustom(MakeDec(100, 2), "E1", "1.0E+000");
    // specifier case is preserved in the exponent letter (net48 {0:e2})
    ExpectCustom(MakeDec(12345678, 3), "e2", "1.23e+004");
    ExpectCustom(MakeDec(12345678, 3, true), "e0", "-1e+004");
    // short mantissas are zero-padded to exactly 'prec' fraction digits
    ExpectCustom(MakeDec(25, 1), "E2", "2.50E+000");  // 2.5m (net48 pads)
    ExpectCustom(MakeDec(15, 1), "e2", "1.50e+000");  // lowercase + pad
    ExpectCustom(MakeDec(15, 1), "E1", "1.5E+000");   // exactly prec, no extra
    ExpectCustom(MakeDec(5, 0), "E2", "5.00E+000");   // single-digit mantissa

    // --- 5. P: percent (value x 100, grouped, trailing '%') ---
    ExpectCustom(MakeDec(12345678, 4), "P", "123,456.78%");      // default prec 2
    ExpectCustom(MakeDec(12345678, 4), "P0", "123,457%");
    ExpectCustom(MakeDec(12345678, 4), "P1", "123,456.8%");
    ExpectCustom(MakeDec(12345678, 4, true), "P2", "-123,456.78%");
    ExpectCustom(MakeDec(0, 0), "P2", "0.00%");
    ExpectCustom(MakeDec(0, 0), "P0", "0%");
    ExpectCustom(MakeDec(0, 2, true), "P2", "0.00%");             // -0.00m drops sign
    ExpectCustom(MakeDec(1, 28), "P4", "0.0000%");                // scale-28 unit
    ExpectCustom(MakeDec(15, 1), "P2", "150.00%");
    ExpectCustom(MakeDec(9999, 3), "P0", "1,000%");               // 9.999m carry
    ExpectCustom(MakeDec(1, 4), "P1", "0.0%");                    // 0.0001m
    ExpectCustom(MakeDec(12345678, 4), "p2", "123,456.78%");      // lowercase letter

    // --- 6. C: currency (fixed '¥' culture contract, formatext precedent) ---
    {
        il2cpp::Il2CppString* s = icall_decimal_to_string_custom(
            &(const Il2CppDecimal&)MakeDec(15, 1), il2cpp::il2cpp_string_new_from_cs("C"));
        Expect(StringIs16(s, u"\u00A5""1.50", 5), "C of 1.5 == \"\xA5""1.50\"");
    }
    {
        il2cpp::Il2CppString* s = icall_decimal_to_string_custom(
            &(const Il2CppDecimal&)MakeDec(15, 1), il2cpp::il2cpp_string_new_from_cs("C2"));
        Expect(StringIs16(s, u"\u00A5""1.50", 5), "C2 of 1.5 == \"\xA5""1.50\"");
    }
    {
        il2cpp::Il2CppString* s = icall_decimal_to_string_custom(
            &(const Il2CppDecimal&)MakeDec(12345, 1, true),
            il2cpp::il2cpp_string_new_from_cs("C"));
        Expect(StringIs16(s, u"\u00A5-1,234.50", 10), "C of -1234.5 == \"\xA5-1,234.50\"");
    }

    // --- 7. Format paths (net48 dispatch boundary) ---
    // Unknown single letters still parse as standard specs -> FormatException.
    ExpectCustomThrowsFormat("decimal \"D\" -> FormatException", MakeDec(15, 1), "D");
    ExpectCustomThrowsFormat("decimal \"X0\" -> FormatException", MakeDec(15, 1), "X0");
    ExpectCustomThrowsFormat("unknown letter -> FormatException", MakeDec(15, 1), "Q");
    // Non-standard multi-char patterns fall back to CUSTOM formatting (the
    // net48 native NumberToStringFormat port). Pure literal patterns pass
    // through verbatim ("xyz"), digit placeholders round ("F100" -> "F102"
    // because the 1.5 rounds to 2 in the trailing '0' placeholder).
    ExpectCustom(MakeDec(15, 1), "xyz", "xyz");
    ExpectCustom(MakeDec(15, 1), "ab12", "ab12");
    ExpectCustom(MakeDec(15, 1), "F2x", "F2x");
    ExpectCustom(MakeDec(15, 1), "F100", "F102");

    // --- 7b. CUSTOM format semantics (net48 oracle, zh-CN) ---
    // 0 = zero-fill digit, # = optional digit, '.' decimal point.
    ExpectCustom(MakeDec(12345678, 4), "00.00", "1234.57");          // rounding
    ExpectCustom(MakeDec(12345678, 4), "#,##0.00", "1,234.57");      // grouping
    ExpectCustom(MakeDec(1, 4), "0.###", "0");                        // 0.0001 -> 0
    ExpectCustom(MakeDec(5, 4), "0.###", "0.001");                    // 0.0005 rounds up
    ExpectCustom(MakeDec(4, 4), "0.###", "0");                        // 0.0004 rounds down
    ExpectCustom(MakeDec(15, 1), "0.0", "1.5");
    ExpectCustom(MakeDec(0, 0), "#.00", ".00");                       // # drops the zero
    ExpectCustom(MakeDec(0, 0), "000000", "000000");                  // zero-fill
    ExpectCustom(MakeDec(12, 0), "0000", "0012");
    ExpectCustom(MakeDec(0, 0), "####", "");
    ExpectCustom(MakeDec(12345678, 4, true), "000.00#", "-1234.568");
    ExpectCustom(MakeDec(12345678, 3), "0.00", "12345.68");           // extra digits overflow
    ExpectCustom(MakeDec(12, 0), "0.##", "12");                       // trailing # trimmed
    ExpectCustom(MakeDec(15, 1), "0.0#", "1.5");
    ExpectCustom(MakeDec(150, 2), "0.0#", "1.5");                     // 1.50m drops trailing 0
    ExpectCustom(MakeDec(105, 2), "0.##", "1.05");
    ExpectCustom(MakeDec(12, 1), "0.00#", "1.20");                    // 0-fill then # suppressed
    ExpectCustom(MakeDec(125, 2), "0.0", "1.3");                      // half away from zero
    ExpectCustom(MakeDec(135, 2), "0.0", "1.4");
    ExpectCustom(MakeDec(1005, 3), "0.00", "1.01");                   // half away: 1.005 -> 1.01
    ExpectCustom(MakeDec(25, 1), "0", "3");                           // 2.5 rounds up
    ExpectCustom(MakeDec(25, 1, true), "0", "-3");
    ExpectCustom(MakeDec(50, 1), "0", "5");
    // negative zero formats as zero (net48 decimal rule)
    ExpectCustom(MakeDec(0, 2, true), "0.00", "0.00");
    // percent: value x 100 with the symbol at its position
    ExpectCustom(MakeDec(15, 1), "0.00%", "150.00%");
    ExpectCustom(MakeDec(123456, 6), "0.00%", "12.35%");
    ExpectCustom(MakeDec(15, 1), "%0.00", "%150.00");
    ExpectCustom(MakeDec(15, 1), "0%0", "15%0");
    // quoted/escaped literals
    ExpectCustom(MakeDec(12345678, 4), "'Q'0", "Q1235");
    ExpectCustom(MakeDec(12345678, 4), "'#'0", "#1235");
    ExpectCustom(MakeDec(12345678, 4), "\\#0", "#1235");
    ExpectCustom(MakeDec(12345678, 4), "\"00\"0", "001235");
    // custom scientific notation
    ExpectCustom(MakeDec(12345678, 4), "0.00E+00", "1.23E+03");
    ExpectCustom(MakeDec(12345678, 4), "0.00e-00", "1.23e03");
    ExpectCustom(MakeDec(12345678, 4), "##.#E+0", "12.3E+2");
    ExpectCustom(MakeDec(999, 0), "0.0E+00", "1.0E+03");              // mantissa carry
    ExpectCustom(MakeDec(0, 0), "0.00E+00", "0.00E+00");
    ExpectCustom(MakeDec(123, 6), "0.00E+00", "1.23E-04");
    // sections: positive;negative;zero
    ExpectCustom(MakeDec(5, 0), "0.0;(-0.0);zero", "5.0");
    ExpectCustom(MakeDec(5, 0, true), "0.0;(-0.0);zero", "(-5.0)");
    ExpectCustom(MakeDec(0, 0), "0.0;(-0.0);zero", "zero");
    ExpectCustom(MakeDec(5, 0, true), "0;0", "5");                    // negative section no sign
    ExpectCustom(MakeDec(5, 0, true), "0;-0", "-5");
    ExpectCustom(MakeDec(5, 0, true), "0;", "-5");                    // empty neg -> sign + pos
    ExpectCustom(MakeDec(5, 0, true), "0;0;0;0", "5");                // 4th section ignored
    // ',' scaling comma (divide by 1000)
    ExpectCustom(MakeDec(123456789, 0), "##0,", "123457");            // 123456789 /1000 rounds
    ExpectCustom(MakeDec(1234567890, 0), "#,##0,", "1,234,568");      // scale + group
    ExpectCustom(MakeDec(0, 0), "##0,", "0");

    // --- 8. Parse: net48 semantics (user case first) ---
    Expect(TryParseIs("123.45", "123.45"), "parse \"123.45\" == 123.45");
    Expect(TryParseIs("1.50", "1.50"), "parse keeps trailing zeros (scale 2)");
    Expect(TryParseIs("-123.45", "-123.45"), "parse negative");
    Expect(TryParseIs("+123.45", "123.45"), "parse leading +");
    Expect(TryParseIs("1,234.5", "1234.5"), "parse tolerates thousands separator");
    Expect(TryParseIs("0001.500", "1.500"), "parse ignores leading zeros, keeps scale");
    Expect(TryParseIs("  1.5  ", "1.5"), "parse trims whitespace");
    Expect(TryParseIs("79228162514264337593543950335", "79228162514264337593543950335"),
           "parse max value");
    Expect(TryParseIs("0.0000000000000000000000000001",
                      "0.0000000000000000000000000001"),
           "parse scale 28");
    Expect(TryParseIs("-0.00", "-0.00"), "parse negative zero");

    // Field-level check of the user oracle: "123.45" -> mantissa 12345,
    // scale 2, positive.
    {
        Il2CppDecimal d = MakeDec(0, 0);
        icall_decimal_parse(&d, il2cpp::il2cpp_string_new_from_cs("123.45"));
        Expect(d.lo64 == 12345 && d.hi32 == 0 && d.flags == (2 << 16),
               "parse \"123.45\" -> lo64 12345, scale 2, positive");
    }

    // --- 9. Parse overflow: net48 never rounds; all three paths throw ---
    ExpectParseThrows("30 significant digits -> OverflowException (user oracle)",
                      "System.OverflowException",
                      "79228162514264337593543950335.1");
    ExpectParseThrows("mantissa past 2^96-1 -> OverflowException",
                      "System.OverflowException",
                      "79228162514264337593543950336");
    ExpectParseThrows("scale > 28 -> OverflowException",
                      "System.OverflowException",
                      "0.00000000000000000000000000001");
    ExpectParseThrows("30 digits integer -> OverflowException",
                      "System.OverflowException",
                      "100000000000000000000000000000");

    // --- 10. Parse malformed -> FormatException ---
    ExpectParseThrows("empty -> FormatException", "System.FormatException", "");
    ExpectParseThrows("junk -> FormatException", "System.FormatException", "abc");
    ExpectParseThrows("two points -> FormatException", "System.FormatException", "1.5.5");
    ExpectParseThrows("exponent rejected -> FormatException", "System.FormatException", "1e5");
    ExpectParseThrows("trailing sign -> FormatException", "System.FormatException", "1-");
    ExpectParseThrows("lone sign -> FormatException", "System.FormatException", "+");
    ExpectParseThrows("inner space -> FormatException", "System.FormatException", "1 2");
    ExpectParseThrows("comma first -> FormatException", "System.FormatException", ",5");
    ExpectParseThrows("comma last -> FormatException", "System.FormatException", "5,");

    // Parse(null) -> ArgumentNullException.
    {
        bool caught = false;
        try
        {
            Il2CppDecimal out = MakeDec(0, 0);
            icall_decimal_parse(&out, nullptr);
        }
        catch (const il2cpp::DotNetException& e)
        {
            caught = true;
            Expect(std::strncmp(e.type_name(), "System.ArgumentNullException",
                                std::strlen("System.ArgumentNullException")) == 0,
                   "exception type tag matches ArgumentNullException");
        }
        Expect(caught, "parse(null) throws ArgumentNullException");
    }

    // --- 11. TryParse: never throws, false + untouched on failure ---
    Expect(icall_decimal_try_parse(nullptr, nullptr) == false,
           "try_parse(null) == false");
    Expect(TryParseIs("abc", "") == false, "try_parse junk == false");
    Expect(TryParseIs("79228162514264337593543950335.1", "") == false,
           "try_parse overflow == false");
    Expect(TryParseIs("", "") == false, "try_parse empty == false");
    {
        Il2CppDecimal out = MakeDec(0xDEADBEEFDEADBEEFULL, 0xDEADBEEF, 0, false);
        icall_decimal_try_parse(il2cpp::il2cpp_string_new_from_cs("nope"), &out);
        // .NET TryParse contract: a FAILED parse writes decimal.Zero.
        Expect(out.lo64 == 0 && out.hi32 == 0 && out.flags == 0,
               "try_parse failure writes zero (TryParse contract)");
    }

    // --- 12. Round-trips ---
    Expect(TryParseIs("0.0000000000000000000000000001",
                      "0.0000000000000000000000000001"),
           "round-trip 1e-28");

    // --- 13. decimal.Round mode validation (net48 oracle; D-DR-1) ---
    // 0 (ToEven) / 1 (AwayFromZero) are the only accepted MidpointRounding
    // values; 2/3/4 (the netcore ToZero/ToNegativeInfinity/ToPositiveInfinity)
    // and everything beyond throw ArgumentException on net48.
    {
        Il2CppDecimal d = MakeDec(27, 1);   // 2.7m
        icall_decimal_round(&d, &d, 0, 0);      // ToEven still accepted
        Expect(StringIs(icall_decimal_to_string(&d), "3"),
               "Round(2.7m, ToEven) == 3");
    }
    {
        Il2CppDecimal d = MakeDec(27, 1);   // 2.7m
        icall_decimal_round(&d, &d, 0, 1);      // AwayFromZero still accepted
        Expect(StringIs(icall_decimal_to_string(&d), "3"),
               "Round(2.7m, AwayFromZero) == 3");
    }
    ExpectRoundThrowsArg("Round(mode=2 ToZero) -> ArgumentException", 0, 2);
    ExpectRoundThrowsArg("Round(mode=3 ToNegativeInfinity) -> ArgumentException", 0, 3);
    ExpectRoundThrowsArg("Round(mode=4 ToPositiveInfinity) -> ArgumentException", 0, 4);
    ExpectRoundThrowsArg("Round(mode=5) -> ArgumentException", 0, 5);

    if (g_failures == 0)
    {
        std::printf("all decimal format tests passed\n");
        return 0;
    }
    std::printf("%d decimal format test(s) failed\n", g_failures);
    return 1;
}

