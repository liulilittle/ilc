//------------------------------------------------------------------------------
// test_convert.cpp -- System.Convert icall runtime unit tests (il2cpp_convert.h).
//
// SEMANTIC BASELINE: .NET Framework 4.8, EMPIRICALLY PINNED by the
// convertcase e2e reference run (tests/cases/convertcase/convertcase.cs):
//   * Convert.ToInt32(null) / ToInt64(null) / ToDouble(null) == 0 -- the
//     "null means zero" rule of the Convert family (int.Parse would throw).
//   * Convert.ToInt32(long) / ToInt32(double) / ToChar(int) are CHECKED
//     conversions: out-of-range inputs throw OverflowException (net48
//     verified: ToInt32(2147483648L), ToInt32(2147483648.0), ToInt32(NaN),
//     ToChar(0x1F600), ToChar(-1) all throw). In-range doubles ROUND
//     half-to-even (ToInt32(2.9) == 3, ToInt32(-2.9) == -3).
//   * Convert.ToBoolean(int): non-zero -> true.
//   * Convert.ToInt32(char): zero-extends 0..0xFFFF (never throws).
//   * The ToString family delegates to the format.cpp icalls.
//
// Coverage (normal + edge + error paths):
//   1. to_int32_string: null -> 0; decimal texts incl. whitespace/sign;
//      FormatException (junk/empty); OverflowException (one past int32 range).
//   2. to_int32_i8: in-range truncation; OverflowException both sides.
//   3. to_int32_d: round-half-to-even; OverflowException for
//      NaN / +-Infinity / out-of-range magnitudes; INT32_MIN boundary legal.
//   4. to_double_i4: exact widening, extrema.
//   5. to_double_string: null -> 0.0; legal texts; specials; FormatException.
//   6. to_int64_string: null -> 0; decimal texts; int64 range boundaries;
//      OverflowException one past the range; FormatException (junk).
//   7. to_string_i4/i8/d/bool: exact net48 texts (strcmp, not ==).
//   8. to_bool_i4: 0 -> 0, any non-zero (incl. -1) -> 1.
//   9. to_char_i4: 0..0xFFFF passthrough; OverflowException outside.
//  10. to_int32_char: zero-extension of a UTF-16 unit.
//  11. to_int32_string_base: net48 ParseNumbers.StringToInt(IsTight) --
//      null -> 0, invalid radix -> ArgumentException (checked first), tight
//      parse, sign rules, "0x" prefix for 16, int32/uint32 range semantics.
//  12. to_string_i4_base: net48 ParseNumbers.IntToString -- base 2/8/16
//      render the unsigned uint32 value (lowercase hex), base 10 signed;
//      invalid toBase -> ArgumentException.
//
// Plain executable + minimal assertion harness (see test_math.cpp): any
// failed Expect() prints FAIL and the process exits non-zero.
//------------------------------------------------------------------------------

#include "il2cpp_convert.h"
#include "il2cpp_exception.h"
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

// Exact UTF-16 comparison of a managed string against an ASCII literal
// (strcmp-style content compare -- never `==` on char pointers).
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

// ExpectConvertThrows: run fn() (which takes no managed string), require it to
// throw a DotNetException of the given managed type. e.type_name() carries the
// FULL throw chain ("System.FormatException,System.FormatException,..."), so
// the assertion is a PREFIX match on the chain's first element -- the exact
// managed full name (mirrors the strcmp-vs-== lesson: never pointer-compare).
void ExpectConvertThrows(const char* what, const char* typeTag, void (*fn)())
{
    bool caught = false;
    try
    {
        fn();
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

// Wrappers so throw-expecting calls still compile with return values.
int32_t NoThrowToInt32String(il2cpp::Il2CppString* s) { return il2cpp::runtime::icall_convert_to_int32_string(s); }
int32_t NoThrowToInt32I8(int64_t v) { return il2cpp::runtime::icall_convert_to_int32_i8(v); }
int32_t NoThrowToInt32D(double v) { return il2cpp::runtime::icall_convert_to_int32_d(v); }
int64_t NoThrowToInt64D(double v) { return il2cpp::runtime::icall_convert_to_int64_d(v); }
uint32_t NoThrowToUInt32D(double v) { return il2cpp::runtime::icall_convert_to_uint32_d(v); }
uint64_t NoThrowToUInt64D(double v) { return il2cpp::runtime::icall_convert_to_uint64_d(v); }
int32_t NoThrowToByteD(double v) { return il2cpp::runtime::icall_convert_to_byte_d(v); }
int32_t NoThrowToSByteD(double v) { return il2cpp::runtime::icall_convert_to_sbyte_d(v); }
int32_t NoThrowToUInt16D(double v) { return il2cpp::runtime::icall_convert_to_uint16_d(v); }
int32_t NoThrowToInt16D(double v) { return il2cpp::runtime::icall_convert_to_int16_d(v); }
double NoThrowToDoubleString(il2cpp::Il2CppString* s) { return il2cpp::runtime::icall_convert_to_double_string(s); }
int64_t NoThrowToInt64String(il2cpp::Il2CppString* s) { return il2cpp::runtime::icall_convert_to_int64_string(s); }
int32_t NoThrowToCharI4(int32_t v) { return il2cpp::runtime::icall_convert_to_char_i4(v); }
int32_t NoThrowToInt32StringBase(il2cpp::Il2CppString* s, int32_t b) { return il2cpp::runtime::icall_convert_to_int32_string_base(s, b); }
il2cpp::Il2CppString* NoThrowToStringI4Base(int32_t v, int32_t b) { return il2cpp::runtime::icall_convert_to_string_i4_base(v, b); }

} // namespace

int main()
{
    // --- 1. Convert.ToInt32(string): null -> 0; parse; error paths. ---
    Expect(il2cpp::runtime::icall_convert_to_int32_string(nullptr) == 0,
           "to_int32_string(null) == 0 (Convert null semantics)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string(il2cpp::il2cpp_string_new_from_cs("0")) == 0,
           "to_int32_string(\"0\") == 0");
    Expect(il2cpp::runtime::icall_convert_to_int32_string(il2cpp::il2cpp_string_new_from_cs("42")) == 42,
           "to_int32_string(\"42\") == 42");
    Expect(il2cpp::runtime::icall_convert_to_int32_string(il2cpp::il2cpp_string_new_from_cs("  -7  ")) == -7,
           "to_int32_string(\"  -7  \") == -7");
    Expect(il2cpp::runtime::icall_convert_to_int32_string(il2cpp::il2cpp_string_new_from_cs("+123")) == 123,
           "to_int32_string(\"+123\") == 123");
    Expect(il2cpp::runtime::icall_convert_to_int32_string(il2cpp::il2cpp_string_new_from_cs("2147483647")) == INT32_MAX,
           "to_int32_string(max) == INT32_MAX");
    Expect(il2cpp::runtime::icall_convert_to_int32_string(il2cpp::il2cpp_string_new_from_cs("-2147483648")) == INT32_MIN,
           "to_int32_string(min) == INT32_MIN");
    ExpectConvertThrows("to_int32_string(\"abc\") throws FormatException",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32String(il2cpp::il2cpp_string_new_from_cs("abc")); });
    ExpectConvertThrows("to_int32_string(\"\") throws FormatException",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32String(il2cpp::il2cpp_string_new_from_cs("")); });
    ExpectConvertThrows("to_int32_string(\"1.5\") throws FormatException",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32String(il2cpp::il2cpp_string_new_from_cs("1.5")); });
    ExpectConvertThrows("to_int32_string(\"2147483648\") throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32String(il2cpp::il2cpp_string_new_from_cs("2147483648")); });

    // --- 2. Convert.ToInt32(long): CHECKED truncation (net48 e2e-verified:
    // out-of-range input throws OverflowException, it does NOT wrap). ---
    Expect(il2cpp::runtime::icall_convert_to_int32_i8(0LL) == 0, "to_int32_i8(0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_int32_i8(42LL) == 42, "to_int32_i8(42) == 42");
    Expect(il2cpp::runtime::icall_convert_to_int32_i8(-42LL) == -42, "to_int32_i8(-42) == -42");
    Expect(il2cpp::runtime::icall_convert_to_int32_i8(INT32_MAX) == INT32_MAX,
           "to_int32_i8(INT32_MAX) == INT32_MAX");
    Expect(il2cpp::runtime::icall_convert_to_int32_i8(INT32_MIN) == INT32_MIN,
           "to_int32_i8(INT32_MIN) == INT32_MIN");
    ExpectConvertThrows("to_int32_i8(2147483648) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32I8(2147483648LL); });
    ExpectConvertThrows("to_int32_i8(-2147483649) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32I8(-2147483649LL); });

    // --- 3. Convert.ToInt32(double): net48 ROUNDS half-to-even (reference
    // Convert.ToInt32(double) algorithm -- NOT truncation; convertcase e2e:
    // ToInt32(2.9)==3, ToInt32(-2.9)==-3). Midpoints round to the EVEN
    // neighbor (ToInt32(2.5)==2, ToInt32(3.5)==4). Range boundaries:
    // 2147483647.5 (exclusive) throws, -2147483648.5 (inclusive) yields
    // INT32_MIN. NaN / +-Infinity / out-of-range -> OverflowException. ---
    Expect(il2cpp::runtime::icall_convert_to_int32_d(0.0) == 0, "to_int32_d(0.0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(2.9) == 3, "to_int32_d(2.9) == 3 (round)");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(-2.9) == -3, "to_int32_d(-2.9) == -3 (round)");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(2.5) == 2, "to_int32_d(2.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(3.5) == 4, "to_int32_d(3.5) == 4 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(-2.5) == -2, "to_int32_d(-2.5) == -2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(1000000000.0) == 1000000000,
           "to_int32_d(1e9) == 1000000000");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(2147483647.0) == INT32_MAX,
           "to_int32_d(2147483647.0) == INT32_MAX (legal)");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(-2147483648.0) == INT32_MIN,
           "to_int32_d(-2147483648.0) == INT32_MIN (legal boundary)");
    Expect(il2cpp::runtime::icall_convert_to_int32_d(-2147483648.5) == INT32_MIN,
           "to_int32_d(-2147483648.5) == INT32_MIN (half-to-even at the min boundary)");
    ExpectConvertThrows("to_int32_d(NaN) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32D(std::nan("")); });
    ExpectConvertThrows("to_int32_d(+inf) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32D(INFINITY); });
    ExpectConvertThrows("to_int32_d(-inf) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32D(-INFINITY); });
    ExpectConvertThrows("to_int32_d(2147483647.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32D(2147483647.5); });
    ExpectConvertThrows("to_int32_d(2147483648.0) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32D(2147483648.0); });
    ExpectConvertThrows("to_int32_d(-2147483649.0) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32D(-2147483649.0); });

    // --- 3b. Convert.ToInt64/ToUInt32/ToUInt64/ToByte/ToSByte/ToUInt16/
    // ToInt16(double): same half-to-even-first contract (parity-T1). ---
    Expect(il2cpp::runtime::icall_convert_to_int64_d(2.5) == 2LL, "to_int64_d(2.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_int64_d(3.5) == 4LL, "to_int64_d(3.5) == 4 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_int64_d(-2.5) == -2LL, "to_int64_d(-2.5) == -2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_int64_d(-9223372036854775808.0) == INT64_MIN,
           "to_int64_d(-2^63) == INT64_MIN (legal boundary)");
    Expect(il2cpp::runtime::icall_convert_to_int64_d(9223372036854774784.0) == 9223372036854774784LL,
           "to_int64_d(2^63-1024) legal (largest double below 2^63)");
    ExpectConvertThrows("to_int64_d(NaN) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt64D(std::nan("")); });
    ExpectConvertThrows("to_int64_d(2^63) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt64D(9223372036854775807.0); });
    ExpectConvertThrows("to_int64_d(-2^63-2048) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt64D(-9223372036854777856.0); });
    Expect(il2cpp::runtime::icall_convert_to_uint32_d(2.5) == 2U, "to_uint32_d(2.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_uint32_d(3.5) == 4U, "to_uint32_d(3.5) == 4 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_uint32_d(4294967295.0) == UINT32_MAX,
           "to_uint32_d(2^32-1) == UINT32_MAX (legal boundary)");
    Expect(il2cpp::runtime::icall_convert_to_uint32_d(-0.0) == 0U, "to_uint32_d(-0.0) == 0 (negative zero passes)");
    ExpectConvertThrows("to_uint32_d(-0.4) throws OverflowException (any negative throws for unsigned targets)",
                        "System.OverflowException",
                        [] { (void)NoThrowToUInt32D(-0.4); });
    ExpectConvertThrows("to_uint32_d(4294967295.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToUInt32D(4294967295.5); });
    ExpectConvertThrows("to_uint32_d(-1.0) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToUInt32D(-1.0); });
    Expect(il2cpp::runtime::icall_convert_to_uint64_d(2.5) == 2ULL, "to_uint64_d(2.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_uint64_d(18446744073709549568.0) == 18446744073709549568ULL,
           "to_uint64_d(2^64-2048) legal (largest double below 2^64)");
    ExpectConvertThrows("to_uint64_d(2^64-1-as-double) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToUInt64D(18446744073709551615.0); });
    ExpectConvertThrows("to_uint64_d(-0.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToUInt64D(-0.5); });
    Expect(il2cpp::runtime::icall_convert_to_byte_d(2.5) == 2, "to_byte_d(2.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_byte_d(3.5) == 4, "to_byte_d(3.5) == 4 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_byte_d(255.0) == 255, "to_byte_d(255) == 255 (legal boundary)");
    ExpectConvertThrows("to_byte_d(255.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToByteD(255.5); });
    ExpectConvertThrows("to_byte_d(-0.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToByteD(-0.5); });
    Expect(il2cpp::runtime::icall_convert_to_sbyte_d(2.5) == 2, "to_sbyte_d(2.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_sbyte_d(-128.0) == -128, "to_sbyte_d(-128) == -128 (legal boundary)");
    Expect(il2cpp::runtime::icall_convert_to_sbyte_d(-128.5) == -128,
           "to_sbyte_d(-128.5) == -128 (half-to-even at the min boundary)");
    ExpectConvertThrows("to_sbyte_d(127.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToSByteD(127.5); });
    Expect(il2cpp::runtime::icall_convert_to_uint16_d(1.5) == 2, "to_uint16_d(1.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_uint16_d(65535.0) == 65535,
           "to_uint16_d(65535) == 65535 (legal boundary)");
    ExpectConvertThrows("to_uint16_d(65535.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToUInt16D(65535.5); });
    Expect(il2cpp::runtime::icall_convert_to_int16_d(2.5) == 2, "to_int16_d(2.5) == 2 (half-to-even)");
    Expect(il2cpp::runtime::icall_convert_to_int16_d(-32768.0) == -32768,
           "to_int16_d(-32768) == -32768 (legal boundary)");
    ExpectConvertThrows("to_int16_d(32767.5) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt16D(32767.5); });

    // --- 4. Convert.ToDouble(int32): exact widening. ---
    Expect(il2cpp::runtime::icall_convert_to_double_i4(0) == 0.0, "to_double_i4(0) == 0.0");
    Expect(il2cpp::runtime::icall_convert_to_double_i4(42) == 42.0, "to_double_i4(42) == 42.0");
    Expect(il2cpp::runtime::icall_convert_to_double_i4(-7) == -7.0, "to_double_i4(-7) == -7.0");
    Expect(il2cpp::runtime::icall_convert_to_double_i4(INT32_MAX) == 2147483647.0,
           "to_double_i4(INT32_MAX) exact");
    Expect(il2cpp::runtime::icall_convert_to_double_i4(INT32_MIN) == -2147483648.0,
           "to_double_i4(INT32_MIN) exact");

    // --- 5. Convert.ToDouble(string): null -> 0.0; legal texts; error. ---
    Expect(il2cpp::runtime::icall_convert_to_double_string(nullptr) == 0.0,
           "to_double_string(null) == 0.0 (Convert null semantics)");
    Expect(il2cpp::runtime::icall_convert_to_double_string(il2cpp::il2cpp_string_new_from_cs("3.5")) == 3.5,
           "to_double_string(\"3.5\") == 3.5");
    Expect(il2cpp::runtime::icall_convert_to_double_string(il2cpp::il2cpp_string_new_from_cs("  -1.25e2  ")) == -125.0,
           "to_double_string(\"  -1.25e2  \") == -125");
    Expect(il2cpp::runtime::icall_convert_to_double_string(il2cpp::il2cpp_string_new_from_cs("0")) == 0.0,
           "to_double_string(\"0\") == 0.0");
    const double dNaN = il2cpp::runtime::icall_convert_to_double_string(il2cpp::il2cpp_string_new_from_cs("NaN"));
    Expect(dNaN != dNaN, "to_double_string(\"NaN\") is NaN");
    {
        // zh-CN culture infinity symbol: U+221E parses to +inf (the ASCII
        // "Infinity" word is rejected on net48 zh-CN -- see test_format.cpp).
        const char16_t infUnits[] = { u'\u221E' };
        Expect(il2cpp::runtime::icall_convert_to_double_string(il2cpp::il2cpp_string_new(infUnits, 1)) == INFINITY,
               "to_double_string(U+221E) is +inf (zh-CN infinity symbol)");
    }
    {
        const char16_t ninfUnits[] = { u'-', u'\u221E' };
        Expect(il2cpp::runtime::icall_convert_to_double_string(il2cpp::il2cpp_string_new(ninfUnits, 2)) == -INFINITY,
               "to_double_string(\"-\" + U+221E) is -inf");
    }
    ExpectConvertThrows("to_double_string(\"abc\") throws FormatException",
                        "System.FormatException",
                        [] { (void)NoThrowToDoubleString(il2cpp::il2cpp_string_new_from_cs("abc")); });

    // --- 6. Convert.ToInt64(string): null -> 0; decimal parse with the full
    // int64 range; OverflowException one past the range. ---
    Expect(il2cpp::runtime::icall_convert_to_int64_string(nullptr) == 0,
           "to_int64_string(null) == 0 (Convert null semantics)");
    Expect(il2cpp::runtime::icall_convert_to_int64_string(il2cpp::il2cpp_string_new_from_cs("0")) == 0,
           "to_int64_string(\"0\") == 0");
    Expect(il2cpp::runtime::icall_convert_to_int64_string(il2cpp::il2cpp_string_new_from_cs("42")) == 42,
           "to_int64_string(\"42\") == 42");
    Expect(il2cpp::runtime::icall_convert_to_int64_string(il2cpp::il2cpp_string_new_from_cs("  -7  ")) == -7,
           "to_int64_string(\"  -7  \") == -7");
    Expect(il2cpp::runtime::icall_convert_to_int64_string(il2cpp::il2cpp_string_new_from_cs("9223372036854775807")) == INT64_MAX,
           "to_int64_string(max) == INT64_MAX");
    Expect(il2cpp::runtime::icall_convert_to_int64_string(il2cpp::il2cpp_string_new_from_cs("-9223372036854775808")) == INT64_MIN,
           "to_int64_string(min) == INT64_MIN");
    ExpectConvertThrows("to_int64_string(\"9223372036854775808\") throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt64String(il2cpp::il2cpp_string_new_from_cs("9223372036854775808")); });
    ExpectConvertThrows("to_int64_string(\"abc\") throws FormatException",
                        "System.FormatException",
                        [] { (void)NoThrowToInt64String(il2cpp::il2cpp_string_new_from_cs("abc")); });

    // --- 7. ToString family: delegate to the format.cpp icalls (strcmp). ---
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4(123), "123"),
           "to_string_i4(123) == \"123\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4(-123), "-123"),
           "to_string_i4(-123) == \"-123\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4(0), "0"),
           "to_string_i4(0) == \"0\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4(INT32_MIN), "-2147483648"),
           "to_string_i4(INT32_MIN) == \"-2147483648\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i8(0), "0"),
           "to_string_i8(0) == \"0\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i8(-45), "-45"),
           "to_string_i8(-45) == \"-45\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i8(INT64_MIN), "-9223372036854775808"),
           "to_string_i8(INT64_MIN) == \"-9223372036854775808\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_d(1.5), "1.5"),
           "to_string_d(1.5) == \"1.5\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_d(0.0), "0"),
           "to_string_d(0.0) == \"0\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_d(std::nan("")), "NaN"),
           "to_string_d(NaN) == \"NaN\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_bool(true), "True"),
           "to_string_bool(true) == \"True\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_bool(false), "False"),
           "to_string_bool(false) == \"False\"");

    // --- 8. Convert.ToBoolean(int32): non-zero -> true (0/1 result). ---
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(0) == 0, "to_bool_i4(0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(1) == 1, "to_bool_i4(1) == 1");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(42) == 1, "to_bool_i4(42) == 1 (non-zero)");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(-1) == 1, "to_bool_i4(-1) == 1 (non-zero)");

    // --- 9. Convert.ToChar(int32): CHECKED (net48 e2e-verified: 0..0xFFFF,
    // outside throws OverflowException -- it does NOT truncate to 16 bits). ---
    Expect(il2cpp::runtime::icall_convert_to_char_i4(0) == 0, "to_char_i4(0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_char_i4(65) == 65, "to_char_i4(65) == 65 ('A')");
    Expect(il2cpp::runtime::icall_convert_to_char_i4(0xFFFF) == 0xFFFF,
           "to_char_i4(0xFFFF) == 0xFFFF (upper boundary)");
    ExpectConvertThrows("to_char_i4(0x10000) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToCharI4(0x10000); });
    ExpectConvertThrows("to_char_i4(-1) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToCharI4(-1); });

    // --- 10. Convert.ToInt32(char): zero-extension of the UTF-16 unit. ---
    Expect(il2cpp::runtime::icall_convert_to_int32_char(0) == 0, "to_int32_char(0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_int32_char(65) == 65, "to_int32_char(65) == 65 ('A')");
    Expect(il2cpp::runtime::icall_convert_to_int32_char(0xFFFF) == 0xFFFF,
           "to_int32_char(0xFFFF) == 0xFFFF");

    // --- 11. Convert.ToInt32(string, fromBase): net48 ParseNumbers.StringToInt
    // (IsTight) semantics, EMPIRICALLY PINNED against a net48 reference run.
    // null -> 0; invalid radix -> ArgumentException (checked first); tight
    // parse (no whitespace, empty -> ArgumentOutOfRangeException); sign rules
    // ('-' only base 10, '+' any base); "0x" prefix for base 16; base 10
    // range-checked, bases 2/8/16 wrap inside uint32. ---
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(nullptr, 10) == 0,
           "to_int32_string_base(null, 10) == 0 (Convert null semantics)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(nullptr, 16) == 0,
           "to_int32_string_base(null, 16) == 0 (Convert null semantics)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("1010"), 2) == 10,
           "to_int32_string_base(\"1010\", 2) == 10");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("7777777777"), 8) == 1073741823,
           "to_int32_string_base(\"7777777777\", 8) == 1073741823");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("FF"), 16) == 255,
           "to_int32_string_base(\"FF\", 16) == 255 (uppercase)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("ff"), 16) == 255,
           "to_int32_string_base(\"ff\", 16) == 255 (lowercase)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("80000000"), 16) == INT32_MIN,
           "to_int32_string_base(\"80000000\", 16) == INT32_MIN (sign-bit wrap)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("FFFFFFFF"), 16) == -1,
           "to_int32_string_base(\"FFFFFFFF\", 16) == -1 (uint32 wrap)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("-42"), 10) == -42,
           "to_int32_string_base(\"-42\", 10) == -42");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("+42"), 10) == 42,
           "to_int32_string_base(\"+42\", 10) == 42");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("+101"), 2) == 5,
           "to_int32_string_base(\"+101\", 2) == 5 ('+' is legal for any radix)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("0xFF"), 16) == 255,
           "to_int32_string_base(\"0xFF\", 16) == 255 (0x prefix)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("0X1A"), 16) == 26,
           "to_int32_string_base(\"0X1A\", 16) == 26 (0X prefix)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("+0xFF"), 16) == 255,
           "to_int32_string_base(\"+0xFF\", 16) == 255 (sign then 0x)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("0000FF"), 16) == 255,
           "to_int32_string_base(\"0000FF\", 16) == 255 (leading zeros)");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("0"), 2) == 0,
           "to_int32_string_base(\"0\", 2) == 0");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("4000000000"), 8) == 536870912,
           "to_int32_string_base(\"4000000000\", 8) == 536870912");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("10000000000"), 2) == 1024,
           "to_int32_string_base(\"10000000000\", 2) == 1024");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("-0"), 10) == 0,
           "to_int32_string_base(\"-0\", 10) == 0");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("2147483647"), 10) == INT32_MAX,
           "to_int32_string_base(\"2147483647\", 10) == INT32_MAX");
    Expect(il2cpp::runtime::icall_convert_to_int32_string_base(il2cpp::il2cpp_string_new_from_cs("-2147483648"), 10) == INT32_MIN,
           "to_int32_string_base(\"-2147483648\", 10) == INT32_MIN");
    ExpectConvertThrows("to_int32_string_base(\"42\", 3) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("42"), 3); });
    ExpectConvertThrows("to_int32_string_base(\"42\", 0) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("42"), 0); });
    ExpectConvertThrows("to_int32_string_base(\"42\", -2) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("42"), -2); });
    ExpectConvertThrows("to_int32_string_base(null, 3) throws ArgumentException (radix checked first)",
                        "System.ArgumentException",
                        [] { (void)NoThrowToInt32StringBase(nullptr, 3); });
    ExpectConvertThrows("to_int32_string_base(\"-101\", 2) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("-101"), 2); });
    ExpectConvertThrows("to_int32_string_base(\"-80000000\", 16) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("-80000000"), 16); });
    ExpectConvertThrows("to_int32_string_base(\" 1F \", 16) throws FormatException (tight parse)",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs(" 1F "), 16); });
    ExpectConvertThrows("to_int32_string_base(\" 42 \", 10) throws FormatException (tight parse)",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs(" 42 "), 10); });
    ExpectConvertThrows("to_int32_string_base(\"0b101\", 2) throws FormatException (no 0b prefix)",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("0b101"), 2); });
    ExpectConvertThrows("to_int32_string_base(\"0x\", 16) throws FormatException (empty after prefix)",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("0x"), 16); });
    ExpectConvertThrows("to_int32_string_base(\"1F2G\", 16) throws FormatException (invalid digit)",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("1F2G"), 16); });
    ExpectConvertThrows("to_int32_string_base(\"123\", 2) throws FormatException (invalid binary digits)",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("123"), 2); });
    ExpectConvertThrows("to_int32_string_base(\"-\", 10) throws FormatException (sign only)",
                        "System.FormatException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("-"), 10); });
    ExpectConvertThrows("to_int32_string_base(\"2147483648\", 10) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("2147483648"), 10); });
    ExpectConvertThrows("to_int32_string_base(\"-2147483649\", 10) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("-2147483649"), 10); });
    ExpectConvertThrows("to_int32_string_base(\"100000000\", 16) throws OverflowException (past uint32)",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("100000000"), 16); });
    ExpectConvertThrows("to_int32_string_base(\"40000000000\", 8) throws OverflowException (past uint32)",
                        "System.OverflowException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs("40000000000"), 8); });
    ExpectConvertThrows("to_int32_string_base(\"\", 10) throws ArgumentOutOfRangeException (net48 quirk)",
                        "System.ArgumentOutOfRangeException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs(""), 10); });
    ExpectConvertThrows("to_int32_string_base(\"\", 16) throws ArgumentOutOfRangeException (net48 quirk)",
                        "System.ArgumentOutOfRangeException",
                        [] { (void)NoThrowToInt32StringBase(il2cpp::il2cpp_string_new_from_cs(""), 16); });

    // --- 12. Convert.ToString(int, toBase): net48 ParseNumbers.IntToString
    // (EMPIRICALLY PINNED): invalid toBase -> ArgumentException; base 10 is
    // the signed decimal; bases 2/8/16 render the UNSIGNED (uint32) value with
    // lowercase hex, no leading zeros, "0" for zero (negative values print
    // their two's-complement representation). ---
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(42, 2), "101010"),
           "to_string_i4_base(42, 2) == \"101010\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(42, 8), "52"),
           "to_string_i4_base(42, 8) == \"52\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(42, 10), "42"),
           "to_string_i4_base(42, 10) == \"42\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(42, 16), "2a"),
           "to_string_i4_base(42, 16) == \"2a\" (lowercase)");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(-42, 2), "11111111111111111111111111010110"),
           "to_string_i4_base(-42, 2) == twos-complement binary");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(-42, 8), "37777777726"),
           "to_string_i4_base(-42, 8) == \"37777777726\" (unsigned octal)");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(-42, 10), "-42"),
           "to_string_i4_base(-42, 10) == \"-42\" (signed decimal)");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(-42, 16), "ffffffd6"),
           "to_string_i4_base(-42, 16) == \"ffffffd6\" (twos-complement hex)");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(0, 2), "0"),
           "to_string_i4_base(0, 2) == \"0\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(0, 8), "0"),
           "to_string_i4_base(0, 8) == \"0\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(0, 16), "0"),
           "to_string_i4_base(0, 16) == \"0\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(INT32_MAX, 16), "7fffffff"),
           "to_string_i4_base(INT32_MAX, 16) == \"7fffffff\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(INT32_MIN, 16), "80000000"),
           "to_string_i4_base(INT32_MIN, 16) == \"80000000\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(INT32_MIN, 8), "20000000000"),
           "to_string_i4_base(INT32_MIN, 8) == \"20000000000\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(INT32_MIN, 2), "10000000000000000000000000000000"),
           "to_string_i4_base(INT32_MIN, 2) == 32-bit binary");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(255, 16), "ff"),
           "to_string_i4_base(255, 16) == \"ff\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(7, 2), "111"),
           "to_string_i4_base(7, 2) == \"111\"");
    Expect(StringIs(il2cpp::runtime::icall_convert_to_string_i4_base(64, 8), "100"),
           "to_string_i4_base(64, 8) == \"100\"");
    ExpectConvertThrows("to_string_i4_base(42, 3) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToStringI4Base(42, 3); });
    ExpectConvertThrows("to_string_i4_base(42, 0) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToStringI4Base(42, 0); });
    ExpectConvertThrows("to_string_i4_base(42, -16) throws ArgumentException",
                        "System.ArgumentException",
                        [] { (void)NoThrowToStringI4Base(42, -16); });

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d convert assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime convert tests passed\n");
    return 0;
}
