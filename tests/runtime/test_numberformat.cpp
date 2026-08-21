//------------------------------------------------------------------------------
// test_numberformat.cpp -- fixed-culture number formatting unit tests
// (numberformat.h).
//
// Every expected string pins the runtime to .NET Framework 4.8 ToString
// semantics under the verification host's DEFAULT zh-CN culture: ','
// thousands grouping, '.' decimals, "n %" percent layout, special
// spellings "NaN"/"∞"/"-∞" (U+221E -- the zh-CN NumberFormatInfo infinity
// symbol, byte-pinned: UTF-8 E2 88 9E / UTF-16 0x221E), rounding half
// away from zero, 3-digit scientific exponents.
//
// The three headline values are the .NET-known outputs:
//   1234567.89.ToString("N2") = "1,234,567.89"
//   255.ToString("X4")        = "00FF"
//   0.00123.ToString("E2")    = "1.23E-003"
//
// Coverage (normal + edge + error paths):
//   1. N2/N0: grouped thousands, 0/2 fraction digits, negatives, zero.
//   2. X4: zero-padding, no truncation when wider, two's-complement, zero.
//   3. E2: 3-digit exponent, negative exponent, int64 exact decimal digits
//      (INT64_MAX), mantissa carry.
//   4. G: plain decimal int64; G15 double.
//   5. P: value*100, grouped, invariant " %" layout, negatives.
//   6. Specials: NaN/+-Infinity in N/E/P; -0.0 keeps no sign in N/E/P.
//
// Plain executable + minimal assertion harness (see test_format.cpp): any
// failed Expect() prints FAIL and the process exits non-zero.
//------------------------------------------------------------------------------

#include "numberformat.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>

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

// Exact UTF-16 comparison against an explicit unit array -- required by the
// U+221E '∞' infinity symbol, which no ASCII literal can express.
bool StringIsUnits(il2cpp::Il2CppString* s, const char16_t* units, int32_t n)
{
    if (s == nullptr || s->length != n)
    {
        return false;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        if (s->chars[i] != units[i])
        {
            return false;
        }
    }
    return true;
}

void ExpectStr(il2cpp::Il2CppObject* obj, const char* expected, const char* what)
{
    Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(obj), expected), what);
}

void ExpectStrU(il2cpp::Il2CppObject* obj, const char16_t* expected, int32_t n, const char* what)
{
    Expect(StringIsUnits(reinterpret_cast<il2cpp::Il2CppString*>(obj), expected, n), what);
}

void ExpectI64(int64_t v, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_general(v), expected, what);
}

void ExpectF(int64_t v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_fixed(v, prec), expected, what);
}

void ExpectFD(double v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_fixed_double(v, prec), expected, what);
}

void ExpectX(int64_t v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_hex(v, prec), expected, what);
}

void ExpectS(int64_t v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_scientific(v, prec), expected, what);
}

void ExpectSD(double v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_scientific_double(v, prec), expected, what);
}

void ExpectP(double v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_percent(v, prec), expected, what);
}

void ExpectPI(int64_t v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_percent_int(v, prec), expected, what);
}

void ExpectFp(int64_t v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_ffixed(v, prec), expected, what);
}

void ExpectFDp(double v, int32_t prec, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_ffixed_double(v, prec), expected, what);
}

il2cpp::Il2CppObject* MakeFormat(const char* fmt)
{
    il2cpp::Il2CppString* f = il2cpp::il2cpp_string_new_from_cs(fmt);
    return reinterpret_cast<il2cpp::Il2CppObject*>(f);
}

void ExpectCustomI(int64_t v, const char* fmt, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_custom(v, MakeFormat(fmt)), expected, what);
}

void ExpectCustomD(double v, const char* fmt, const char* expected, const char* what)
{
    ExpectStr(il2cpp::runtime::il2cpp_number_format_custom_double(v, MakeFormat(fmt)),
              expected, what);
}

} // namespace

int main()
{
    // --- 1. headline .NET-known values (invariant culture) ---
    ExpectFD(1234567.89, 2, "1,234,567.89",
             "N2 of 1234567.89: grouped thousands + 2 decimals");
    ExpectX(255, 4, "00FF", "X4 of 255: zero-padded hex");
    ExpectSD(0.00123, 2, "1.23E-003",
             "E2 of 0.00123: mantissa + 3-digit negative exponent");

    // --- 2. N: grouped fixed (int64 and double) ---
    ExpectF(1234567, 0, "1,234,567", "N0 of 1234567: grouped, no decimals");
    ExpectF(-1234567, 0, "-1,234,567", "N0 of -1234567: sign before grouping");
    ExpectF(0, 0, "0", "N0 of 0");
    ExpectF(INT64_C(123456789012345), 0, "123,456,789,012,345",
            "N0 of int64: grouping across 15 digits");
    ExpectFD(-1234567.89, 2, "-1,234,567.89", "N2 of -1234567.89");
    ExpectFD(1234.5678, 1, "1,234.6", "N1 of 1234.5678: half away from zero");

    // --- 2b. grouping boundaries: the double/percent paths feed a NON-NUL-
    // terminated buffer into GroupDigits -- every row below has a '.' in a
    // different place (or none at all) so an unbounded strchr scan reads
    // stale stack bytes past 'n' (ASAN + SEGFAULT via negative memcpy len).
    ExpectFD(1000000000000000.0, 0, "1,000,000,000,000,000",
             "N0 of 1e15: large grouping, no fraction (no '.' in buffer)");
    ExpectFD(-1000000000000000.0, 0, "-1,000,000,000,000,000",
             "N0 of -1e15: negative large grouping");
    ExpectFD(123456789.0, 0, "123,456,789", "N0 of 123456789: three groups");
    ExpectFD(123456789.125, 3, "123,456,789.125",
             "N3 of 123456789.125: grouping + 3 fractions");
    ExpectFD(9876543210.0, 2, "9,876,543,210.00",
             "N2 of 9876543210: grouping with trailing .00");
    ExpectP(123456789.0, 0, "12,345,678,900 %",
            "P0 of 123456789: grouped x100, no fraction");
    ExpectP(-0.0001234, 4, "-0.0123 %", "P4 of -0.0001234: tiny negative");

    // --- 3. X: hex, padding, two's-complement ---
    ExpectX(255, 2, "FF", "X2 of 255: no truncation when wider than precision");
    ExpectX(0, 4, "0000", "X4 of 0: zero-padded");
    ExpectX(0xABCD, 0, "ABCD", "X of 0xABCD: uppercase, no padding");
    ExpectX(-1, 0, "FFFFFFFFFFFFFFFF", "X of -1: 16-digit two's complement");
    ExpectX(-12345, 4, "FFFFFFFFFFFFCFC7", "X4 of -12345: complement dominates pad");

    // --- 4. E: scientific (int64 exact + double) ---
    ExpectS(12345, 2, "1.23E+004", "E2 of 12345: int64 exact decimal digits");
    ExpectS(INT64_C(9223372036854775807), 2, "9.22E+018",
            "E2 of INT64_MAX: 19 digits, exponent 18");
    ExpectS(0, 2, "0.00E+000", "E2 of 0");
    ExpectSD(1234567.89, 2, "1.23E+006", "E2 of 1234567.89: positive exponent");
    ExpectSD(-0.00123, 2, "-1.23E-003", "E2 of -0.00123: sign on mantissa");
    ExpectSD(9999.99, 2, "1.00E+004", "E2 of 9999.99: mantissa carry + exponent");

    // --- 5. G: general ---
    ExpectI64(12345, "12345", "G of 12345");
    ExpectI64(-12345, "-12345", "G of -12345");
    ExpectStr(il2cpp::runtime::il2cpp_number_format_general_double(1234567.89),
              "1234567.89", "G15 of 1234567.89 (net48 default double text)");
    ExpectStr(il2cpp::runtime::il2cpp_number_format_general_double(-0.00123),
              "-0.00123", "G15 of -0.00123");
    ExpectStr(il2cpp::runtime::il2cpp_number_format_general_double(-0.0),
              "0", "G15 of -0.0 keeps no sign");
    // net48 default-culture (zh-CN) G15 specials: the U+221E symbol -- the
    // same spelling format.cpp's G15 emits for the host culture (this module
    // matches it instead of pinning the old ASCII invariant spellings).
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_general_double(
                   std::numeric_limits<double>::infinity()),
               u"\u221E", 1, "G15 of +Infinity (zh-CN U+221E)");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_general_double(
                   -std::numeric_limits<double>::infinity()),
               u"-\u221E", 2, "G15 of -Infinity (zh-CN U+221E)");
    ExpectStr(il2cpp::runtime::il2cpp_number_format_general_double(std::nan("")),
              "NaN", "G15 of NaN");

    // --- 6. P: percent, invariant " %" layout ---
    ExpectP(0.1234, 1, "12.3 %", "P1 of 0.1234: space before % (invariant)");
    ExpectP(0.1234, 2, "12.34 %", "P2 of 0.1234");
    ExpectP(-0.1234, 1, "-12.3 %", "P1 of -0.1234");
    ExpectP(1234.5, 2, "123,450.00 %", "P2 of 1234.5: value*100 + grouping");
    ExpectP(0.001, 1, "0.1 %", "P1 of 0.001: small value");
    // net48's double P scales the value by 100.0 IN DOUBLE -- the double
    // rounding of the product is observable (probe4: P1 of the double
    // 123456789012345680 -> "...600,000.0 %", not "...568,000.0 %").
    ExpectP(123456789012345678.0, 2, "12,345,678,901,234,600,000.00 %",
            "P2 of huge value: net48 double x100.0 rounding");

    // --- 7. specials (zh-CN U+221E spellings, NO percent decoration) ---
    ExpectFD(std::nan(""), 2, "NaN", "N2 of NaN");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_fixed_double(
                   std::numeric_limits<double>::infinity(), 2),
               u"\u221E", 1, "N2 of +Infinity (zh-CN U+221E)");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_scientific_double(
                   -std::numeric_limits<double>::infinity(), 2),
               u"-\u221E", 2, "E2 of -Infinity (zh-CN U+221E)");
    // net48: the special symbol is returned BEFORE any percent decoration
    // (probe1: "P1|NaN" -> "NaN", "P1|Infinity" -> "∞").
    ExpectP(std::nan(""), 2, "NaN", "P2 of NaN keeps no %");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_percent(
                   std::numeric_limits<double>::infinity(), 2),
               u"\u221E", 1, "P2 of +Infinity keeps no %");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_percent(
                   -std::numeric_limits<double>::infinity(), 2),
               u"-\u221E", 2, "P2 of -Infinity keeps no %");
    ExpectFD(-0.0, 2, "0.00", "N2 of -0.0 keeps no sign (net48-verified)");
    ExpectSD(-0.0, 2, "0.00E+000", "E2 of -0.0 keeps no sign");
    ExpectP(-0.0, 2, "0.00 %", "P2 of -0.0 keeps no sign");

    // --- 8. net48 15-digit-base rounding (probe2 oracle rows) ---
    // net48 converts the double to 15 significant digits (msvcrt-class CRT),
    // then rounds half away from zero on THOSE digits. Near-tie values are
    // the discriminator: the old k+4-guard-digit approach double-rounded.
    ExpectFD(1.23499999, 2, "1.23", "N2 of 1.23499999: below the 1.235 tie");
    ExpectFD(1.235, 2, "1.24", "N2 of 1.235: 15-digit base rounds to the tie");
    ExpectFD(0.1499999999, 1, "0.1", "N1 of 0.1499999999: below the 0.15 tie");
    ExpectFD(0.15, 1, "0.2", "N1 of 0.15: exact 15-digit tie rounds away");
    ExpectFD(2.675, 2, "2.68", "N2 of 2.675: net48 double-rounds to 2.68");
    ExpectFD(2.675, 3, "2.675", "N3 of 2.675");
    ExpectFD(1234567.89, 0, "1,234,568", "N0 of 1234567.89: rounds the units");
    ExpectSD(0.1499999999, 2, "1.50E-001", "E2 of 0.1499999999: 15-digit base");
    ExpectSD(1.23499999, 2, "1.23E+000", "E2 of 1.23499999");
    ExpectSD(2.675, 2, "2.68E+000", "E2 of 2.675");
    ExpectSD(1234567.89, 4, "1.2346E+006", "E4 of 1234567.89");

    // --- 9. F format: fixed WITHOUT grouping (the F/N difference) ---
    ExpectFDp(1234567.89, 2, "1234567.89", "F2 of 1234567.89: NO grouping");
    ExpectFp(255, 2, "255.00", "F2 of 255 (int64)");
    ExpectFp(255, 0, "255", "F0 of 255 (int64)");
    ExpectFp(-12345, 2, "-12345.00", "F2 of -12345 (int64)");
    ExpectFp(0, 2, "0.00", "F2 of 0 (int64)");
    ExpectFp(INT64_C(123456789012345), 2, "123456789012345.00",
             "F2 of int64: exact digits, no grouping");
    ExpectFDp(2.675, 2, "2.68", "F2 of 2.675: net48 double-rounds to 2.68");
    ExpectFDp(2.675, 0, "3", "F0 of 2.675: rounds the units");
    ExpectFDp(1234.5, 2, "1234.50", "F2 of 1234.5");
    ExpectFDp(-0.4, 2, "-0.40", "F2 of -0.4");
    ExpectFDp(-0.4, 0, "0", "F0 of -0.4: rounded magnitude zero keeps no sign");
    ExpectFDp(-0.0, 2, "0.00", "F2 of -0.0 keeps no sign");
    ExpectFDp(std::nan(""), 2, "NaN", "F2 of NaN");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_ffixed_double(
                   std::numeric_limits<double>::infinity(), 2),
               u"\u221E", 1, "F2 of +Infinity (zh-CN U+221E)");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_ffixed_double(
                   -std::numeric_limits<double>::infinity(), 2),
               u"-\u221E", 2, "F2 of -Infinity (zh-CN U+221E)");

    // --- 10. F default precision = 2 (net48: F of 255 -> "255.00") ---
    ExpectFp(255, -1, "255.00", "F (no precision) of 255 (int64): default 2");
    ExpectFDp(1234.5, -1, "1234.50", "F (no precision) of 1234.5: default 2");

    // --- 11. P on int64: EXACT x100 digit shift, invariant " %" layout ---
    ExpectPI(255, 2, "25,500.00 %", "P2 of 255 (int64): exact x100");
    ExpectPI(255, 0, "25,500 %", "P0 of 255 (int64)");
    ExpectPI(255, 1, "25,500.0 %", "P1 of 255 (int64)");
    ExpectPI(-12345, 2, "-1,234,500.00 %", "P2 of -12345 (int64)");
    ExpectPI(0, 2, "0.00 %", "P2 of 0 (int64)");
    ExpectPI(INT64_C(123456789012345), 2, "12,345,678,901,234,500.00 %",
             "P2 of int64: exact digit shift, no double rounding");
    ExpectPI(INT64_MAX, 2, "922,337,203,685,477,580,700.00 %",
             "P2 of INT64_MAX: exact x100 without overflow");

    // --- 12. custom "0.00" family (int64 + double), net48 oracle rows ---
    ExpectCustomI(255, "0.00", "255.00", "custom 0.00 of 255");
    ExpectCustomI(255, "0000.00", "0255.00", "custom 0000.00 of 255: int zero-pads");
    ExpectCustomI(-12345, "0.00", "-12345.00", "custom 0.00 of -12345");
    ExpectCustomI(0, "0.00", "0.00", "custom 0.00 of 0");
    ExpectCustomI(5, "0000.00", "0005.00", "custom 0000.00 of 5");
    ExpectCustomI(9999, "0", "9999", "custom 0 of 9999");
    ExpectCustomD(1234.5, "0.00", "1234.50", "custom 0.00 of 1234.5");
    ExpectCustomD(123.456, "0000.00", "0123.46", "custom 0000.00 of 123.456: pads + rounds");
    ExpectCustomD(0.5, "0000.00", "0000.50", "custom 0000.00 of 0.5");
    ExpectCustomD(-0.4, "0000.00", "-0000.40", "custom 0000.00 of -0.4");
    ExpectCustomD(2.675, "0.00", "2.68", "custom 0.00 of 2.675");
    ExpectCustomD(0.15, "0.0", "0.2", "custom 0.0 of 0.15: half away from zero");
    ExpectCustomD(0.15, "0", "0", "custom 0 of 0.15");
    ExpectCustomD(1.5, "0", "2", "custom 0 of 1.5: rounds the units");
    ExpectCustomD(0.00123, "0.00", "0.00", "custom 0.00 of 0.00123");
    ExpectCustomD(std::nan(""), "0.00", "NaN", "custom 0.00 of NaN");
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_custom_double(
                   std::numeric_limits<double>::infinity(), MakeFormat("0.00")),
               u"\u221E", 1, "custom 0.00 of +Infinity (zh-CN U+221E)");

    // --- 13. net48 default precisions (bare letters, precision < 0) ---
    ExpectF(1234567, -1, "1,234,567.00", "N (no precision) of int64: default 2");
    ExpectFD(1234.5, -1, "1,234.50", "N (no precision) of 1234.5: default 2");
    ExpectS(255, -1, "2.550000E+002", "E (no precision) of 255: default 6");
    ExpectSD(1234.5, -1, "1.234500E+003", "E (no precision) of 1234.5: default 6");
    ExpectP(0.1234, -1, "12.34 %", "P (no precision) of 0.1234: default 2");
    ExpectX(255, -1, "FF", "X (no precision) of 255: default min-width 1");

    // --- 14. custom engine: '#' / ',' / '%' / scaling (net48 oracle,
    // cstmprobe 2026-08-09 -- invariant custom output equals zh-CN for the
    // subset: '.' / ',' / '%' / U+2030 and ASCII '-' sign) ---
    ExpectCustomI(1234567, "#,##0", "1,234,567", "custom #,##0 of 1234567: grouping");
    ExpectCustomI(0, "#", "", "custom # of 0: optional placeholder suppressed");
    ExpectCustomI(12345, "0%", "1234500%", "custom 0% of 12345: exact x100 digit shift");
    ExpectCustomI(INT64_C(123456789), "0,,", "123", "custom 0,, of 123456789: divide by 1e6");
    ExpectCustomI(INT64_MAX, "0%", "922337203685477580700%",
                  "custom 0% of INT64_MAX: exact x100 without overflow");
    ExpectCustomI(INT64_C(9223372036854775807), "#,##0", "9,223,372,036,854,775,807",
                  "custom #,##0 of INT64_MAX: grouping");
    ExpectCustomD(0.5, "#.##", ".5", "custom #.## of 0.5");
    ExpectCustomD(0.0, "#.##", "", "custom #.## of 0");
    ExpectCustomD(0.1234, "0.00%", "12.34%", "custom 0.00% of 0.1234");
    ExpectCustomD(1234567.89, "#,##0.00", "1,234,567.89", "custom #,##0.00 of 1234567.89");
    ExpectCustomD(-1234.5, "#,#0", "-1,235", "custom #,#0 of -1234.5");
    ExpectCustomD(-0.0, "0.00%", "0.00%", "custom 0.00% of -0.0: rounded zero, no sign");
    ExpectCustomD(12345.0, "D100", "D112345", "custom D100: standard-with-junk falls back");
    ExpectCustomD(12345.0, "0.0#", "12345.0", "custom 0.0# of 12345: '#' trailing zero trimmed");

    // --- 15. currency symbol (D-CUL-1: NumberFormatInfo.CurrencySymbol) ---
    // The zh-CN currency symbol U+00A5 '¥' -- the SAME spelling formatext.cpp's
    // C format emits (get_CurrencySymbol == ToString("C")'s prefix symbol).
    ExpectStrU(il2cpp::runtime::il2cpp_number_format_currency_symbol(),
               u"\u00A5", 1, "get_CurrencySymbol: zh-CN U+00A5 '¥'");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d numberformat assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime numberformat tests passed\n");
    return 0;
}
