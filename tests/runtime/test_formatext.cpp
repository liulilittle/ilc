//------------------------------------------------------------------------------
// test_formatext.cpp -- ToString(format) custom-format icall unit tests
// (il2cpp_format_ext.h).
//
// Every expected string below is taken from the net48 oracle capture
// (tests/cases/formatext -- real .NET Framework 4.8 run on the verification
// host, zh-CN default culture), so the tests pin the runtime to the .NET
// semantics: D/X/N/C/E/G/F/P for int32/int64, F/N/E/G/P/custom for double,
// rounding half away from zero on the net48 15-significant-digit base, the
// fixed zh-CN currency symbol, the U+221E infinity symbol (every format,
// no percent decoration on specials), and the FormatException contract for
// unknown/out-of-subset formats.
//
// Coverage (normal + edge + error paths):
//   1. int32 D/D5/X/X4/N2/C/E2/G (positive, negative, zero, padding).
//   2. int64 D/D5/X/X4/N2/C/E2/G (16-digit two's-complement hex).
//   3. double F/F2/N2/E2/G/0.00/P2 + "0" (fixed, scientific, percent, custom).
//   4. Rounding probes: 2.5 F0 -> "3", -2.5 -> "-3", 1.25 F1 -> "1.3"
//      (away from zero), 0.005 F2 -> "0.01", 9.99 F1 -> "10.0" (carry),
//      plus the near-tie battery on the 15-digit base (1.23499999 F2 ->
//      "1.23", 0.1499999999 F1 -> "0.1", 0.15 F1 -> "0.2", 2.675 F2 ->
//      "2.68").
//   5. Specials: NaN/+-Infinity in F/N/E/G/P/custom -- "NaN" and the
//      zh-CN U+221E symbol; -0.0 keeps no sign in F/E.
//   6. Exceptions: unknown specifiers, out-of-subset formats, custom
//      formats with non-'0'/'.' characters, precision > 99 -> FormatException.
//
// Plain executable + minimal assertion harness (see test_format.cpp): any
// failed Expect() prints FAIL and the process exits non-zero.
//------------------------------------------------------------------------------

#include "il2cpp_format_ext.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "enumext.h"            // il2cpp_enum_register / Il2CppEnumInfo (boxed enum)
#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

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

// Exact comparison against a UTF-16 literal (for the currency symbol U+00A5).
bool StringIsWide(il2cpp::Il2CppString* s, const char16_t* expected)
{
    if (s == nullptr)
    {
        return false;
    }
    int len = 0;
    while (expected[len] != 0)
    {
        ++len;
    }
    if (s->length != len)
    {
        return false;
    }
    for (int i = 0; i < len; ++i)
    {
        if (s->chars[i] != expected[i])
        {
            return false;
        }
    }
    return true;
}

il2cpp::Il2CppObject* MakeFormat(const char* fmt)
{
    il2cpp::Il2CppString* f = il2cpp::il2cpp_string_new_from_cs(fmt);
    return reinterpret_cast<il2cpp::Il2CppObject*>(f);
}

il2cpp::Il2CppObject* MakeFormatWide(const char16_t* fmt)
{
    int len = 0;
    while (fmt[len] != 0)
    {
        ++len;
    }
    il2cpp::Il2CppString* f = il2cpp::il2cpp_string_new(fmt, len);
    return reinterpret_cast<il2cpp::Il2CppObject*>(f);
}

il2cpp::Il2CppString* FmtDW(double v, const char16_t* fmt)
{
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::runtime::icall_format_double_custom(v, MakeFormatWide(fmt), nullptr));
}

il2cpp::Il2CppString* FmtI32(int32_t v, const char* fmt)
{
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::runtime::icall_format_int32_custom(v, MakeFormat(fmt)));
}

il2cpp::Il2CppString* FmtI64(int64_t v, const char* fmt)
{
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::runtime::icall_format_int64_custom(v, MakeFormat(fmt)));
}

il2cpp::Il2CppString* FmtD(double v, const char* fmt)
{
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::runtime::icall_format_double_custom(v, MakeFormat(fmt), nullptr));
}

void ExpectFmtI32(int32_t v, const char* fmt, const char* expected, const char* what)
{
    Expect(StringIs(FmtI32(v, fmt), expected), what);
}

void ExpectFmtI64(int64_t v, const char* fmt, const char* expected, const char* what)
{
    Expect(StringIs(FmtI64(v, fmt), expected), what);
}

void ExpectFmtD(double v, const char* fmt, const char* expected, const char* what)
{
    Expect(StringIs(FmtD(v, fmt), expected), what);
}

// True when the format call throws a FormatException (chain prefix match).
bool FmtThrowsI32(int32_t v, const char* fmt)
{
    try
    {
        il2cpp::runtime::icall_format_int32_custom(v, MakeFormat(fmt));
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches("System.FormatException", e.type_name());
    }
    return false;
}

bool FmtThrowsI64(int64_t v, const char* fmt)
{
    try
    {
        il2cpp::runtime::icall_format_int64_custom(v, MakeFormat(fmt));
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches("System.FormatException", e.type_name());
    }
    return false;
}

bool FmtThrowsD(double v, const char* fmt)
{
    try
    {
        il2cpp::runtime::icall_format_double_custom(v, MakeFormat(fmt), nullptr);
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches("System.FormatException", e.type_name());
    }
    return false;
}

} // namespace

int main()
{
    // --- 1. int32 standard formats (oracle: 12345 / -12345) ---
    ExpectFmtI32(12345, "D", "12345", "int32 D: 12345");
    ExpectFmtI32(12345, "D5", "12345", "int32 D5: no padding beyond 5 digits");
    ExpectFmtI32(-12345, "D", "-12345", "int32 D: -12345");
    ExpectFmtI32(-12345, "D5", "-12345", "int32 D5: -12345 (padding after sign)");
    ExpectFmtI32(12345, "X", "3039", "int32 X: 0x3039");
    ExpectFmtI32(12345, "X4", "3039", "int32 X4: 3039 (already 4 digits)");
    ExpectFmtI32(-12345, "X", "FFFFCFC7", "int32 X: -12345 two's complement");
    ExpectFmtI32(-12345, "X4", "FFFFCFC7", "int32 X4: 8 digits dominate the pad");
    ExpectFmtI32(12345, "N2", "12,345.00", "int32 N2: grouped, 2 decimals");
    ExpectFmtI32(-12345, "N2", "-12,345.00", "int32 N2: negative grouped");
    Expect(StringIsWide(FmtI32(12345, "C"), u"¥12,345.00"),
           "int32 C: fixed zh-CN symbol + grouped");
    Expect(StringIsWide(FmtI32(-12345, "C"), u"¥-12,345.00"),
           "int32 C: negative pattern symbol-sign-number (zh-CN)");
    ExpectFmtI32(12345, "E2", "1.23E+004", "int32 E2: scientific, 3-digit exponent");
    ExpectFmtI32(-12345, "E2", "-1.23E+004", "int32 E2: negative");
    ExpectFmtI32(12345, "G", "12345", "int32 G: plain decimal");
    ExpectFmtI32(-12345, "G", "-12345", "int32 G: negative");
    // null / empty format behave like "G"
    Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(
                        il2cpp::runtime::icall_format_int32_custom(12345, nullptr)),
                    "12345"),
           "int32 null format behaves like G");
    ExpectFmtI32(12345, "", "12345", "int32 empty format behaves like G");
    ExpectFmtI32(12345, "D10", "0000012345", "int32 D10: zero-padded");
    ExpectFmtI32(42, "D5", "00042", "int32 D5 of 42: zero-padded");
    ExpectFmtI32(-42, "D5", "-00042", "int32 D5 of -42: pad after the sign");
    ExpectFmtI32(0, "X4", "0000", "int32 X4 of 0: zero-padded");
    ExpectFmtI32(-123, "X4", "FFFFFF85", "int32 X4 of -123: two's complement");
    ExpectFmtI32(12345, "d5", "12345", "int32 d5: lowercase specifier accepted");
    ExpectFmtI32(12345, "N0", "12,345", "int32 N0: no decimal point");
    Expect(StringIsWide(FmtI32(12345, "C1"), u"¥12,345.0"),
           "int32 C1: one decimal");

    // --- 2. int64 standard formats (oracle: 123456789012345) ---
    ExpectFmtI64(INT64_C(123456789012345), "D", "123456789012345",
                 "int64 D: magnitude");
    ExpectFmtI64(INT64_C(123456789012345), "D5", "123456789012345",
                 "int64 D5: 15 digits dominate the pad");
    ExpectFmtI64(INT64_C(-123456789012345), "D", "-123456789012345",
                 "int64 D: negative magnitude");
    ExpectFmtI64(INT64_C(123456789012345), "X", "7048860DDF79",
                 "int64 X: uppercase hex");
    ExpectFmtI64(INT64_C(123456789012345), "X4", "7048860DDF79",
                 "int64 X4: no padding when longer");
    ExpectFmtI64(INT64_C(-1), "X", "FFFFFFFFFFFFFFFF",
                 "int64 X of -1: 16-digit two's complement");
    ExpectFmtI64(INT64_C(-123456789012345), "X", "FFFF8FB779F22087",
                 "int64 X: negative two's complement");
    ExpectFmtI64(INT64_C(123456789012345), "N2", "123,456,789,012,345.00",
                 "int64 N2: grouped");
    ExpectFmtI64(INT64_C(-123456789012345), "N2", "-123,456,789,012,345.00",
                 "int64 N2: negative grouped");
    Expect(StringIsWide(FmtI64(INT64_C(123456789012345), "C"),
                        u"¥123,456,789,012,345.00"),
           "int64 C: grouped currency");
    ExpectFmtI64(INT64_C(123456789012345), "E2", "1.23E+014",
                 "int64 E2: exact decimal digits (no double round-trip)");
    ExpectFmtI64(INT64_C(9223372036854775807), "E2", "9.22E+018",
                 "int64 E2 of max: 19 digits, exponent 18");
    ExpectFmtI64(INT64_C(123456789012345), "G", "123456789012345",
                 "int64 G: plain decimal");

    // --- 3. double standard + custom formats (oracle) ---
    ExpectFmtD(1234.5678, "F", "1234.57", "double F: default 2 decimals");
    ExpectFmtD(1234.5678, "F2", "1234.57", "double F2");
    ExpectFmtD(-1234.5678, "F2", "-1234.57", "double F2: negative");
    ExpectFmtD(0.001234, "F2", "0.00", "double F2: small value");
    ExpectFmtD(1234.5678, "N2", "1,234.57", "double N2: grouped");
    ExpectFmtD(1234.5678, "E2", "1.23E+003", "double E2");
    ExpectFmtD(0.001234, "E2", "1.23E-003", "double E2: negative exponent");
    ExpectFmtD(1234.5678, "G", "1234.5678", "double G: G15 default");
    ExpectFmtD(1234.5678, "0.00", "1234.57", "double custom 0.00");
    ExpectFmtD(-1234.5678, "0.00", "-1234.57", "double custom 0.00: negative");
    ExpectFmtD(123.456, "0.00", "123.46", "double custom 0.00: rounding");
    ExpectFmtD(1.234, "00.000", "01.234", "double custom 00.000: min int digits");
    ExpectFmtD(0.5, "0", "1", "double custom 0: away-from-zero rounding");
    // "0" custom format with zero fraction digits (net48 probe 2026-08-05):
    // the fixed output carries NO '.' and must still pad/emit correctly
    // (PadIntegerPart gets a dot-less input -- regression for the strchr
    // nullptr UB that corrupted the stack).
    ExpectFmtD(0.0, "0", "0", "double custom 0 of 0.0");
    ExpectFmtD(1.5, "0", "2", "double custom 0 of 1.5");
    ExpectFmtD(-0.5, "0", "-1", "double custom 0 of -0.5");
    ExpectFmtD(-1.5, "0", "-2", "double custom 0 of -1.5");
    ExpectFmtD(0.4, "0", "0", "double custom 0 of 0.4");
    ExpectFmtD(0.0001, "0", "0", "double custom 0 of 0.0001");
    ExpectFmtD(2.5, "0", "3", "double custom 0 of 2.5");
    ExpectFmtD(7.0, "0", "7", "double custom 0 of 7.0");
    ExpectFmtD(999.9, "0", "1000", "double custom 0 of 999.9: carry");
    ExpectFmtD(-0.0, "0", "0", "double custom 0 of -0.0 keeps no sign");
    ExpectFmtD(1e20, "0", "100000000000000000000", "double custom 0 of 1e20");
    ExpectFmtD(123456789012345678.0, "0", "123456789012346000",
               "double custom 0 of 18-digit value: 15-digit base + zeros");
    ExpectFmtD(-123456789012345678.0, "0", "-123456789012346000",
               "double custom 0 of negative 18-digit value");
    ExpectFmtD(1234.5678, "P2", "123,456.78%", "double P2: percent, no space (zh-CN)");
    ExpectFmtD(-1234.5678, "P2", "-123,456.78%", "double P2: negative");
    ExpectFmtD(0.001234, "P2", "0.12%", "double P2: small value");
    Expect(StringIsWide(FmtD(1234.5678, "C"), u"¥1,234.57"),
           "double C: grouped currency");
    ExpectFmtD(12345.0, "E0", "1E+004", "double E0: no mantissa fraction");
    ExpectFmtD(1234.5678, "E", "1.234568E+003", "double E: default 6 mantissa digits");
    ExpectFmtD(0.15, "E0", "2E-001", "double E0 of 0.15: rounds the lead digit");

    // --- 3a. grouping boundaries: FormatGroupedFinite (N/P/C on double) feeds
    // a NON-NUL-terminated buffer into the '.' scan -- an unbounded strchr
    // reads past 'n' into stale stack bytes (the same gap b57650b fixed in
    // numberformat's GroupDigits). Each row puts the '.' in a different place
    // or omits it entirely. ---
    ExpectFmtD(1e15, "N0", "1,000,000,000,000,000",
               "double N0 of 1e15: large grouping, no '.' in buffer");
    ExpectFmtD(-1e15, "N0", "-1,000,000,000,000,000",
               "double N0 of -1e15: negative large grouping");
    ExpectFmtD(123456789.0, "N0", "123,456,789",
               "double N0 of 123456789: three groups");
    ExpectFmtD(123456789.125, "N3", "123,456,789.125",
               "double N3 of 123456789.125: grouping + 3 fractions");
    ExpectFmtD(9876543210.0, "N2", "9,876,543,210.00",
               "double N2 of 9876543210: grouping with trailing .00");

    // --- 3b. G with an explicit precision (net48 oracle rows) ---
    ExpectFmtD(1234567.89, "G2", "1.2E+06", "double G2: scientific, 2-digit exponent");
    ExpectFmtD(1234567.89, "G4", "1.235E+06", "double G4");
    ExpectFmtD(1234567.89, "G7", "1234568", "double G7: fixed, rounded");
    ExpectFmtD(1234567.89, "G8", "1234567.9", "double G8");
    ExpectFmtD(1234567.89, "G15", "1234567.89", "double G15");
    ExpectFmtD(0.00123, "G2", "0.0012", "double G2 of 0.00123: fixed below e-4");
    ExpectFmtD(0.0000123, "G2", "1.2E-05", "double G2 of 1.23e-5: scientific");
    ExpectFmtD(1.5, "G1", "2", "double G1 of 1.5: half away at 1 digit");
    ExpectFmtD(10.5, "G2", "11", "double G2 of 10.5: fixed carry");
    ExpectFmtD(99.5, "G2", "1E+02", "double G2 of 99.5: carry to scientific");
    ExpectFmtD(2.675, "G1", "3", "double G1 of 2.675");
    ExpectFmtD(2.675, "G2", "2.7", "double G2 of 2.675");
    ExpectFmtD(2.675, "G3", "2.68", "double G3 of 2.675");
    ExpectFmtD(9999.99, "G4", "1E+04", "double G4 of 9999.99");
    ExpectFmtD(0.5, "G15", "0.5", "double G15 of 0.5: trailing zeros trimmed");
    ExpectFmtD(1e-7, "G2", "1E-07", "double G2 of 1e-7");
    ExpectFmtD(123456789012345678.0, "G5", "1.2346E+17",
               "double G5 of 18-digit value: 15-digit base rounds first");
    ExpectFmtD(123456789012345678.0, "G15", "1.23456789012346E+17",
               "double G15 of 18-digit value");
    ExpectFmtD(-0.0, "G2", "0", "double G2 of -0.0 keeps no sign");
    ExpectFmtD(1.15, "G1", "1", "double G1 of 1.15: below the tie");
    ExpectFmtD(255.0, "e2", "2.55e+002", "double e2: lowercase exponent marker");

    // --- 3c. int F/P (net48 supports them) ---
    ExpectFmtI32(255, "F2", "255.00", "int32 F2: fixed with 2 zeros");
    ExpectFmtI32(-255, "F2", "-255.00", "int32 F2: negative");
    ExpectFmtI32(255, "F0", "255", "int32 F0");
    ExpectFmtI32(255, "P1", "25,500.0%", "int32 P1: exact x100 + zh-CN layout");
    ExpectFmtI32(-255, "P1", "-25,500.0%", "int32 P1: negative");
    ExpectFmtI32(255, "P2", "25,500.00%", "int32 P2");
    // int P zero keeps a single "0" integer part (net48: P0 of 0 -> "0%", not
    // the "000%" the naive x100 digit shift would produce).
    ExpectFmtI32(0, "P0", "0%", "int32 P0 of 0: single 0 integer part");
    ExpectFmtI32(0, "P1", "0.0%", "int32 P1 of 0");
    ExpectFmtI32(0, "P2", "0.00%", "int32 P2 of 0");
    ExpectFmtI64(0, "P0", "0%", "int64 P0 of 0");
    ExpectFmtI64(INT64_C(9223372036854775807), "P1",
                 "922,337,203,685,477,580,700.0%",
                 "int64 P1 of max: exact digit shift (no overflow)");

    // --- 3d. int G with precision + lowercase x/e (net48 oracle rows) ---
    ExpectFmtI32(12345, "G2", "1.2E+04", "int32 G2: scientific");
    ExpectFmtI32(255, "G2", "2.6E+02", "int32 G2: half away");
    ExpectFmtI32(-255, "G2", "-2.6E+02", "int32 G2: negative");
    ExpectFmtI32(-1, "G2", "-1", "int32 G2 of -1: fixed");
    ExpectFmtI32(255, "G1", "3E+02", "int32 G1");
    ExpectFmtI32(100, "G2", "1E+02", "int32 G2 of 100: rounded to 1E+02");
    ExpectFmtI32(99, "G2", "99", "int32 G2 of 99: fixed");
    ExpectFmtI64(INT64_C(9223372036854775807), "G15", "9.22337203685478E+18",
                 "int64 G15 of max: 15 sig digits, 2-digit exponent");
    ExpectFmtI32(255, "x4", "00ff", "int32 x4: lowercase hex digits");
    ExpectFmtI32(-12345, "x", "ffffcfc7", "int32 x: lowercase two's complement");
    ExpectFmtI32(255, "e2", "2.55e+002", "int32 e2: lowercase exponent marker");
    ExpectFmtI64(INT64_C(123456789012345), "x4", "7048860ddf79",
                 "int64 x4: lowercase hex");

    // --- 4. rounding probes (half away from zero, net48-verified) ---
    ExpectFmtD(2.5, "F0", "3", "F0 of 2.5 rounds away from zero (not half-even)");
    ExpectFmtD(-2.5, "F0", "-3", "F0 of -2.5 rounds away from zero");
    ExpectFmtD(1.25, "F1", "1.3", "F1 of 1.25: exact tie rounds away from zero");
    ExpectFmtD(0.005, "F2", "0.01", "F2 of 0.005 (binary value above the tie)");
    ExpectFmtD(9.99, "F1", "10.0", "F1 of 9.99: carry into the integer part");
    ExpectFmtD(9999.99, "E2", "1.00E+004", "E2 of 9999.99: mantissa carry + exponent");
    // net48's 15-significant-digit base (probe2): near-tie values decide the
    // rounding on the BASE digits, not on the exact binary value.
    ExpectFmtD(1.23499999, "F2", "1.23", "F2 of 1.23499999: below the tie");
    ExpectFmtD(1.235, "F2", "1.24", "F2 of 1.235: base rounds to the tie");
    ExpectFmtD(0.1499999999, "F1", "0.1", "F1 of 0.1499999999: below the tie");
    ExpectFmtD(0.15, "F1", "0.2", "F1 of 0.15: tie rounds away");
    ExpectFmtD(2.675, "F2", "2.68", "F2 of 2.675: net48 double-rounds to 2.68");
    ExpectFmtD(2.675, "F1", "2.7", "F1 of 2.675");
    ExpectFmtD(0.1499999999, "E2", "1.50E-001", "E2 of 0.1499999999");
    ExpectFmtD(1.23499999, "E2", "1.23E+000", "E2 of 1.23499999");
    ExpectFmtD(0.15, "F2", "0.15", "F2 of 0.15");

    // --- 5. specials (net48 probe3/probe4: zh-CN culture symbols, no
    // percent decoration, EVERY format) ---
    ExpectFmtD(std::nan(""), "F2", "NaN", "F2 of NaN");
    ExpectFmtD(std::nan(""), "P2", "NaN", "P2 of NaN keeps no %");
    ExpectFmtD(std::nan(""), "G2", "NaN", "G2 of NaN");
    ExpectFmtD(std::nan(""), "0.00", "NaN", "custom 0.00 of NaN");
    Expect(StringIsWide(FmtD(std::numeric_limits<double>::infinity(), "F2"),
                        u"\u221E"),
           "F2 of +Infinity: zh-CN U+221E symbol");
    Expect(StringIsWide(FmtD(-std::numeric_limits<double>::infinity(), "F2"),
                        u"-\u221E"),
           "F2 of -Infinity: zh-CN U+221E symbol");
    Expect(StringIsWide(FmtD(std::numeric_limits<double>::infinity(), "P2"),
                        u"\u221E"),
           "P2 of +Infinity keeps no %");
    Expect(StringIsWide(FmtD(std::numeric_limits<double>::infinity(), "N2"),
                        u"\u221E"),
           "N2 of +Infinity");
    Expect(StringIsWide(FmtD(std::numeric_limits<double>::infinity(), "E2"),
                        u"\u221E"),
           "E2 of +Infinity");
    Expect(StringIsWide(FmtD(std::numeric_limits<double>::infinity(), "G2"),
                        u"\u221E"),
           "G2 of +Infinity");
    Expect(StringIsWide(FmtD(std::numeric_limits<double>::infinity(), "0.00"),
                        u"\u221E"),
           "custom 0.00 of +Infinity");
    ExpectFmtD(-0.0, "F2", "0.00", "F2 of -0.0 keeps no sign (net48-verified)");
    ExpectFmtD(-0.0, "E2", "0.00E+000", "E2 of -0.0 keeps no sign");
    ExpectFmtD(-0.0, "0.00", "0.00", "custom 0.00 of -0.0 keeps no sign");

    // --- 6. exception paths (FormatException) ---
    Expect(FmtThrowsI32(12345, "Q"), "int32 unknown specifier Q -> FormatException");
    Expect(FmtThrowsI32(12345, "Q4"), "int32 Q4 -> FormatException");
    Expect(FmtThrowsI64(INT64_C(123), "Q"), "int64 unknown specifier Q -> FormatException");
    Expect(FmtThrowsD(1.5, "Q"), "double unknown specifier Q -> FormatException");
    Expect(FmtThrowsD(1.5, "D5"), "double D5 -> FormatException (out of subset)");
    // scientific notation IS implemented (numexp1 net48 oracle, 2026-10-23);
    // ';' sections remain out of subset (documented).
    ExpectFmtD(1.5, "0.00E+00", "1.50E+00",
               "double custom scientific 0.00E+00");
    ExpectFmtD(1.5, "0E0", "2E0", "double custom scientific 0E0 (rounds up)");
    ExpectFmtD(0.000123, "0.00E+00", "1.23E-04",
               "double custom scientific 0.00E+00 of 0.000123");
    ExpectFmtD(1234.5, "0.00e+00", "1.23e+03",
               "double custom scientific lowercase e");
    ExpectFmtD(1234.5, "0.00E-00", "1.23E03",
               "double custom scientific E-00 (sign only when negative)");
    ExpectFmtD(1234.5, "##0.0E+0", "123.5E+1",
               "double custom scientific ##0.0E+0 shifts the mantissa");
    ExpectFmtD(1234.5, "#,##0.0E+0", "1,234.5E+0",
               "double custom scientific with grouping");
    ExpectFmtD(0.0, "0.00E+00", "0.00E+00",
               "double custom scientific of zero");
    ExpectFmtD(-1234.5, "0.00E+00", "-1.23E+03",
               "double custom scientific negative");
    ExpectFmtD(1234.5, "0.00\\E+00", "1234.50E+00",
               "escaped E is a literal (not a marker)");
    Expect(FmtThrowsD(1.5, "0.##;-#.##"),
           "double custom ';' sections -> FormatException (out of subset)");
    ExpectFmtI32(12345, "0.00E+0", "1.23E+4",
                 "int32 custom scientific");
    ExpectFmtI32(0, "0.00E+00", "0.00E+00",
                 "int32 custom scientific of zero");
    Expect(FmtThrowsI32(12345, "0;#"),
           "int32 custom ';' sections -> FormatException (out of subset)");

    // --- 7. custom numeric format engine (net48 oracle, cstmprobe 2026-08-09) ---
    // '#' digit placeholders + leading/trailing-zero suppression
    ExpectFmtI32(12345, "#", "12345", "int32 #: all digits");
    ExpectFmtI32(0, "#", "", "int32 # of 0: optional placeholder suppressed");
    ExpectFmtI32(12, "0000", "0012", "int32 0000 of 12: zero-padded");
    ExpectFmtI32(0, "0", "0", "int32 0 of 0");
    ExpectFmtI32(12, "#000", "012", "int32 #000 of 12: leading # suppresses");
    ExpectFmtD(0.5, "#.##", ".5", "double #.## of 0.5: leading # suppresses 0");
    ExpectFmtD(0.0, "#.##", "", "double #.## of 0: empty");
    ExpectFmtD(2.675, "#.##", "2.68", "double #.## of 2.675: rounds half away");
    ExpectFmtD(0.5, "0.##", "0.5", "double 0.## of 0.5");
    ExpectFmtD(0.0, "0.##", "0", "double 0.## of 0");
    ExpectFmtD(1234.5, "#.##", "1234.5", "double #.## of 1234.5: extra digits first");
    ExpectFmtD(0.00123, "#.##", "", "double #.## of 0.00123: rounds to empty");
    ExpectFmtD(0.05, "0.##", "0.05", "double 0.## of 0.05");
    ExpectFmtD(0.05, "0.#0", "0.05", "double 0.#0 of 0.05: '#' kept before mandatory 0");
    ExpectFmtD(0.0, "0.0#0", "0.000", "double 0.0#0 of 0: '#' kept before mandatory 0");
    ExpectFmtD(0.5, "0.0#", "0.5", "double 0.0# of 0.5: trailing '#' zero trimmed");
    ExpectFmtD(0.25, "0.0#", "0.25", "double 0.0# of 0.25");
    ExpectFmtD(1.5, "0.00.0", "1.500", "double 0.00.0: additional '.' ignored");
    ExpectFmtD(12345.0, ".00", "12345.00", "double .00 of 12345: no int placeholder");
    ExpectFmtD(0.5, ".00", ".50", "double .00 of 0.5");
    ExpectFmtD(0.0, "###", "", "double ### of 0: all optional suppressed");

    // ',' grouping (net48 oracle: separator at the digit-position schedule)
    ExpectFmtI32(1234567, "#,##0", "1,234,567", "int32 #,##0 of 1234567");
    ExpectFmtI32(12345, "0,0", "12,345", "int32 0,0 of 12345");
    ExpectFmtI32(1234, "0,0", "1,234", "int32 0,0 of 1234");
    ExpectFmtI32(1000, "#,0", "1,000", "int32 #,0 of 1000");
    ExpectFmtI32(999, "#,0", "999", "int32 #,0 of 999");
    ExpectFmtD(1234.5, "#,##0.00", "1,234.50", "double #,##0.00 of 1234.5");
    ExpectFmtD(1234567.89, "#,##0.00", "1,234,567.89", "double #,##0.00 of 1234567.89");
    ExpectFmtD(999999.99, "#,##0.0#", "999,999.99", "double #,##0.0# of 999999.99");

    // scaling commas (÷1000 per trailing comma)
    ExpectFmtI32(123456789, "0,,", "123", "int32 0,, of 123456789: divide by 1e6");
    ExpectFmtI32(100000000, "0,,", "100", "int32 0,, of 100000000");
    ExpectFmtI32(123456789, "#,##0,,", "123", "int32 #,##0,, of 123456789: scale + group");
    ExpectFmtD(123456789.5, "0,,", "123", "double 0,, of 123456789.5: divide by 1e6");

    // '%' percent (×100, literal at pattern position) and U+2030 per-mille (×1000)
    ExpectFmtD(0.1234, "0.00%", "12.34%", "double 0.00% of 0.1234");
    ExpectFmtD(0.1234, "0.00 %", "12.34 %", "double 0.00 % keeps the literal space");
    ExpectFmtD(-0.1234, "0.00%", "-12.34%", "double 0.00% of -0.1234");
    ExpectFmtD(0.1234, "%0.00", "%12.34", "double %0.00: symbol before digits");
    ExpectFmtD(0.0, "0.00%", "0.00%", "double 0.00% of 0");
    ExpectFmtD(0.5, "0.0%%", "5000.0%%", "double 0.0%% of 0.5: times 10000");
    ExpectFmtD(123456789012345678.0, "0.0%", "12345678901234600000.0%",
               "double custom %: net48 scales the VALUE by 100.0 in double");
    Expect(StringIsWide(FmtDW(0.1234, u"0.00\u2030"), u"123.40\u2030"),
           "double 0.00 per-mille of 0.1234: times 1000");
    ExpectFmtI32(12345, "0%", "1234500%", "int32 0% of 12345: exact digit shift");
    ExpectFmtI32(12345, "0.00%", "1234500.00%", "int32 0.00% of 12345");
    ExpectFmtI64(INT64_C(9223372036854775807), "0%",
                 "922337203685477580700%", "int64 0% of max: exact x100, no overflow");

    // literals, escapes, quotes, D100-style custom fallback
    ExpectFmtI32(12345, "D100", "D112345", "int32 D100: custom fallback (D+1+12345)");
    ExpectFmtI32(12345, "N5x", "N5x", "int32 N5x: no placeholders -> literals only");
    ExpectFmtI32(12345, "abc", "abc", "int32 abc: all literals");
    ExpectFmtI32(12345, "0abc", "12345abc", "int32 0abc: extra digits before literals");
    ExpectFmtI32(123, "##' units'", "123 units", "int32 quoted literal");
    ExpectFmtI32(5, "0\\x", "5x", "int32 0\\x: escaped literal");
    ExpectFmtI32(5, "'0'", "0", "int32 '0': quoted placeholder is a literal");
    ExpectFmtI32(12345, "000-00", "123-45", "int32 000-00 of 12345: literal '-'");
    ExpectFmtI32(123, "000-00", "001-23", "int32 000-00 of 123: digits fill from the right");
    ExpectFmtD(5.0, "0E1", "5E1", "double 0E1: 'E' without a trailing 0 is a literal");
    ExpectFmtD(5.0, "0abc", "5abc", "double 0abc of 5");
    ExpectFmtD(5.0, "0.00abc", "5.00abc", "double 0.00abc of 5");

    // negative sign placement (net48: '-' precedes every literal)
    ExpectFmtI32(-12345, "abc0000", "-abc12345", "int32 abc0000 of -12345: sign first");
    ExpectFmtI32(-12345, "0000abc", "-12345abc", "int32 0000abc of -12345");
    ExpectFmtD(-0.5, "#.##", "-.5", "double #.## of -0.5");
    ExpectFmtD(-1234.5, "#,#0", "-1,235", "double #,#0 of -1234.5: rounds away");
    ExpectFmtI32(-12345, "0%", "-1234500%", "int32 0% of -12345");
    ExpectFmtD(-0.1234, "%0.00", "-%12.34", "double %0.00 of -0.1234: sign before %");
    ExpectFmtD(-0.0, "#,##0.00", "0.00", "double #,##0.00 of -0.0: rounded zero, no sign");
    ExpectFmtD(-0.0, "0.00%", "0.00%", "double 0.00% of -0.0");
    ExpectFmtD(-12345.0, "D100", "-D112345", "double D100 of -12345: sign before literals");

    // specials short-circuit the format (net48 probe9: even invalid formats)
    ExpectFmtD(std::nan(""), "Q", "NaN", "NaN with invalid Q returns NaN");
    ExpectFmtD(std::nan(""), "D100", "NaN", "NaN with D100 returns NaN");
    ExpectFmtD(std::nan(""), "0.00%", "NaN", "NaN with 0.00% returns NaN");

    // --- 8. composite-format item engine (icall_format_composite_item) ---
    {
        using il2cpp::runtime::icall_format_composite_item;
        // helper: boxed Int32 -> composite item
        auto FmtI32C = [&](int32_t v, const char* fmt, int32_t align) {
            return reinterpret_cast<il2cpp::Il2CppString*>(icall_format_composite_item(
                reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_box_int32(nullptr, v)),
                fmt != nullptr ? MakeFormat(fmt) : nullptr, align));
        };
        auto FmtDC = [&](double v, const char* fmt, int32_t align) {
            return reinterpret_cast<il2cpp::Il2CppString*>(icall_format_composite_item(
                reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::runtime::icall_box_double(v)),
                fmt != nullptr ? MakeFormat(fmt) : nullptr, align));
        };
        Expect(StringIs(FmtI32C(42, nullptr, 5), "   42"),
               "composite item {0,5} of 42 right-aligns to width 5");
        Expect(StringIs(FmtI32C(42, nullptr, -5), "42   "),
               "composite item {0,-5} of 42 left-aligns to width 5");
        Expect(StringIs(FmtI32C(42, nullptr, 2), "42"),
               "composite item {0,2} of 42 does not truncate a wider value");
        Expect(StringIs(FmtI32C(255, "x", 0), "ff"),
               "composite item {0:x} of 255 renders lowercase hex");
        Expect(StringIs(FmtI32C(255, "X", 0), "FF"),
               "composite item {0:X} of 255 renders uppercase hex");
        Expect(StringIs(FmtI32C(255, "x", 6), "    ff"),
               "composite item {0,6:x} of 255 combines specifier + alignment");
        Expect(StringIs(FmtDC(3.14159, "f2", 0), "3.14"),
               "composite item {0:f2} of 3.14159 applies F2");
        Expect(StringIs(FmtDC(3.14159, "f2", 8), "    3.14"),
               "composite item {0,8:f2} of 3.14159 aligns the formatted text");
        Expect(StringIs(FmtDC(-1234.5678, "n2", 0), "-1,234.57"),
               "composite item {0:n2} of -1234.5678 applies N2 (grouped)");
        {
            il2cpp::Il2CppObject* empty = icall_format_composite_item(
                nullptr, nullptr, 0);
            Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(empty), ""),
                   "composite item of null arg renders the empty string");
        }
        {
            il2cpp::Il2CppObject* s = icall_format_composite_item(
                reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")),
                MakeFormat("f2"), 0);
            Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(s), "abc"),
                   "composite item of a string arg ignores the specifier (net48)");
        }
        {
            il2cpp::Il2CppObject* s = icall_format_composite_item(
                reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")),
                nullptr, 5);
            Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(s), "   ab"),
                   "composite item {0,5} of a string arg aligns the payload");
        }
    }

    // --- 8b. composite-format item over a BOXED ENUM (B7: AppendFormatted<T>) ---
    // net48 routes a boxed enum through IFormattable.ToString(format), so the
    // specifier IS honored (G -> the member name, D -> the decimal value,
    // X -> uppercase hex).
    {
        using il2cpp::runtime::icall_format_composite_item;
        const char* names[] = {"Red", "Green", "Blue"};
        const int64_t values[] = {1, 2, 4};
        il2cpp::runtime::Il2CppEnumInfo info = {
            il2cpp::runtime::kEnumUnderlyingInt32, 3, names, values,
        };
        il2cpp_enum_register("Cases.Color", &info);
        il2cpp::Il2CppClass colorClass = {
            "Cases.Color", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
        };
        il2cpp::il2cpp_class_register(&colorClass);
        il2cpp::Il2CppObject* boxedGreen = il2cpp::runtime::icall_box_enum(&colorClass, 2);
        Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(
                   icall_format_composite_item(boxedGreen, nullptr, 0)), "Green"),
               "composite item of a boxed enum (null format) -> member name");
        Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(
                   icall_format_composite_item(boxedGreen, MakeFormat("D"), 0)), "2"),
               "composite item of a boxed enum honors D");
        Expect(StringIs(reinterpret_cast<il2cpp::Il2CppString*>(
                   icall_format_composite_item(boxedGreen, MakeFormat("X"), 0)), "00000002"),
               "composite item of a boxed enum honors X");
    }

    // --- 9. provider routing (saudit4): the invariant culture through the
    // custom formatters and the composite engine (net48 oracle-pinned:
    // 1234.ToString("C", Invariant) == U+00A4 "1,234.00"; negative wraps
    // "(¤n)"; P becomes "n %"; nullptr keeps the zh-CN U+00A5 layout). ---
    {
        using il2cpp::runtime::icall_format_composite_item;
        il2cpp::Il2CppObject* inv = MakeFormat("");
        auto FmtI32P = [&](int32_t v, const char* fmt) {
            return reinterpret_cast<il2cpp::Il2CppString*>(
                il2cpp::runtime::icall_format_int32_custom(v, MakeFormat(fmt), inv));
        };
        Expect(StringIsWide(FmtI32P(1234, "C"), u"\u00A41,234.00"),
               "int32 C invariant positive uses U+00A4");
        Expect(StringIsWide(FmtI32P(-1234, "C2"), u"(\u00A41,234.00)"),
               "int32 C invariant negative wraps (currency)");
        Expect(StringIsWide(FmtI32P(1234, "P0"), u"123,400 %"),
               "int32 P invariant has the space before %");
        Expect(StringIsWide(FmtI32P(-1234, "P0"), u"-123,400 %"),
               "int32 P invariant negative keeps the sign");
        Expect(StringIsWide(
                   reinterpret_cast<il2cpp::Il2CppString*>(
                       il2cpp::runtime::icall_format_int64_custom(1234, MakeFormat("C"), inv)),
                   u"\u00A41,234.00"),
               "int64 C invariant positive uses U+00A4");
        Expect(StringIsWide(
                   reinterpret_cast<il2cpp::Il2CppString*>(
                       il2cpp::runtime::icall_format_int32_custom(1234, MakeFormat("C"), nullptr)),
                   u"\u00A51,234.00"),
               "int32 C nullptr provider stays zh-CN U+00A5");
        {
            il2cpp::Il2CppObject* s = icall_format_composite_item(
                reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_box_int32(nullptr, 1234)),
                MakeFormat("C"), 0, inv);
            Expect(StringIsWide(reinterpret_cast<il2cpp::Il2CppString*>(s), u"\u00A41,234.00"),
                   "composite int C with invariant provider");
        }
        {
            il2cpp::Il2CppObject* s = icall_format_composite_item(
                reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::runtime::icall_box_double(12.5)),
                MakeFormat("C"), 0, inv);
            Expect(StringIsWide(reinterpret_cast<il2cpp::Il2CppString*>(s), u"\u00A412.50"),
                   "composite double C with invariant provider");
        }
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d formatext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime formatext tests passed\n");
    return 0;
}
