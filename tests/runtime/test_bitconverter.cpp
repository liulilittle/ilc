//------------------------------------------------------------------------------
// test_bitconverter.cpp -- System.BitConverter runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in bitconverter.h:
//   1. Platform probe: BitConverter.IsLittleEndian must be TRUE on Windows
//      x86-64 (the byte-layout expectations below depend on it).
//   2. GetBytes known layouts (little-endian): bool {01}/{00}, char
//      0x4142 -> {42,41}, int16 -2 -> {FE,FF}, uint16 0x0102 -> {02,01},
//      int32 1 -> {01,00,00,00}, uint32 0x01020304 -> {04,03,02,01},
//      int64/uint64 0x0102030405060708 -> {08,...,01}, single 1.0f ->
//      {00,00,80,3F}, double 1.0 -> {00,...,F0,3F}.
//   3. Byte-count audit (bit-width check): every GetBytes result has
//      exactly sizeof(T) elements (1/2/4/8).
//   4. Round-trips across ALL types: normal values, extrema (min/max of
//      every width), signed zero (signbit preserved), infinities and NaN
//      (bit-exact through the byte[] channel).
//   5. Bit bridges: SingleToInt32Bits(1.0f) == 0x3F800000 (the canonical
//      IEEE-754 pattern), DoubleToInt64Bits(1.0) == 0x3FF0000000000000,
//      -0.0f -> 0x80000000, NaN payload passthrough, and bidirectional
//      round-trips (bits -> value -> bits is the identity).
//   6. Error paths: null byte[] -> ArgumentNullException; EMPTY byte[] ->
//      ArgumentOutOfRangeException (net48/Core probe-verified); non-empty
//      undersized byte[] -> ArgumentException (fewer than 1/2/4/8 bytes);
//      an array LARGER than needed still works (.NET reads only the first
//      N bytes).
//   7. ToString(byte[]): the net48 uppercase hex formatter -- "00-FF-80-12-AB"
//      (two-digit pairs, '-' separated); a known probe sequence, single-byte
//      arrays {01}/{00}, the GetBytes layouts of 0x12345678 / 1.0f /
//      0xDEADBEEF (the bitconvcase oracle forms), EMPTY array -> "" and
//      null -> ArgumentNullException.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "bitconverter.h"
#include "il2cpp_array.h"
#include "il2cpp_exception.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <functional>

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

// ExpectDotNetException: run fn(), require it to throw a DotNetException of
// the given type tag carrying a message that contains 'messageFragment'.
void ExpectDotNetException(const char* what, const char* typeTag,
                           const char* messageFragment,
                           const std::function<void()>& fn)
{
    bool caught = false;
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        caught = true;
        Expect(std::strcmp(e.type_name(), typeTag) == 0,
               "exception type tag matches the managed type");
        Expect(std::strstr(e.what(), messageFragment) != nullptr,
               "exception message fragment present");
    }
    catch (...)
    {
        caught = true;
        Expect(false, "wrong exception carrier type (not DotNetException)");
    }
    Expect(caught, what);
}

// Exception type tags (must match exceptionext.cpp exactly).
const char* const kNullArgExTag =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char* const kArgExTag =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
const char* const kOutOfRangeTag =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";

// BytesEqual: the array must have exactly 'count' elements matching
// 'expected' byte-for-byte (little-endian layout asserted by callers).
bool BytesEqual(il2cpp::Il2CppArray* arr, const uint8_t* expected, size_t count)
{
    if (il2cpp::il2cpp_array_get_length(arr) != static_cast<int32_t>(count))
    {
        return false;
    }
    for (size_t i = 0; i < count; ++i)
    {
        if (static_cast<uint8_t>(arr->data[i]) != expected[i])
        {
            return false;
        }
    }
    return true;
}

// ByteCount: element count of a byte[] (bit-width audit helper).
int32_t ByteCount(il2cpp::Il2CppArray* arr)
{
    return il2cpp::il2cpp_array_get_length(arr);
}

// Wrappers for the throw-expecting calls so their values are consumed (the
// helpers return their result only on the non-throwing path).
bool NoThrowToBool(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_bool(v); }
char16_t NoThrowToChar(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_char(v); }
int16_t NoThrowToInt16(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_int16(v); }
uint16_t NoThrowToUInt16(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_uint16(v); }
int32_t NoThrowToInt32(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_int32(v); }
int64_t NoThrowToInt64(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_int64(v); }
float NoThrowToSingle(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_single(v); }
double NoThrowToDouble(il2cpp::Il2CppArray* v) { return il2cpp::runtime::il2cpp_bitconverter_to_double(v); }

// GetBytes wrappers (single call sites in main keep the expressions short).
il2cpp::Il2CppArray* BytesBool(bool v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_bool(v); }
il2cpp::Il2CppArray* BytesChar(char16_t v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_char(v); }
il2cpp::Il2CppArray* BytesInt16(int16_t v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_int16(v); }
il2cpp::Il2CppArray* BytesUInt16(uint16_t v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_uint16(v); }
il2cpp::Il2CppArray* BytesInt32(int32_t v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_int32(v); }
il2cpp::Il2CppArray* BytesUInt32(uint32_t v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_uint32(v); }
il2cpp::Il2CppArray* BytesInt64(int64_t v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_int64(v); }
il2cpp::Il2CppArray* BytesUInt64(uint64_t v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_uint64(v); }
il2cpp::Il2CppArray* BytesSingle(float v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_single(v); }
il2cpp::Il2CppArray* BytesDouble(double v) { return il2cpp::runtime::il2cpp_bitconverter_get_bytes_double(v); }

// NewByteArray: zero-initialized byte[] of 'length' elements (used to build
// undersized / oversized inputs that GetBytes never produces).
il2cpp::Il2CppArray* NewByteArray(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 1, false, length);
}

// HexStringEquals: 'str' must hold exactly the ASCII 'expected' characters
// (the hex formatter output is pure ASCII: 0-9 A-F and '-').
bool HexStringEquals(il2cpp::Il2CppString* str, const char* expected)
{
    const size_t len = std::strlen(expected);
    if (str->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (int32_t i = 0; i < str->length; ++i)
    {
        if (static_cast<char>(str->chars[i]) != expected[i])
        {
            return false;
        }
    }
    return true;
}

// ToHex: single call site wrapper for the throw-expecting null test.
il2cpp::Il2CppString* ToHex(il2cpp::Il2CppArray* v)
{
    return il2cpp::runtime::il2cpp_bitconverter_to_hex_string(v);
}

} // namespace

int main()
{
    using il2cpp::runtime::il2cpp_bitconverter_double_to_int64_bits;
    using il2cpp::runtime::il2cpp_bitconverter_int32_bits_to_single;
    using il2cpp::runtime::il2cpp_bitconverter_int64_bits_to_double;
    using il2cpp::runtime::il2cpp_bitconverter_single_to_int32_bits;
    using il2cpp::runtime::il2cpp_bitconverter_to_uint32;
    using il2cpp::runtime::il2cpp_bitconverter_to_uint64;

    // --- 1. Platform probe: BitConverter.IsLittleEndian ---
    const uint32_t kProbe = 1u;
    const bool littleEndian = *reinterpret_cast<const uint8_t*>(&kProbe) == 1u;
    Expect(littleEndian, "BitConverter.IsLittleEndian == true (little-endian host)");

    // --- 2a. GetBytes known layouts (little-endian) ---
    {
        const uint8_t one[] = { 0x01 };
        const uint8_t zero[] = { 0x00 };
        Expect(BytesEqual(BytesBool(true), one, 1), "GetBytes(bool true) == {01}");
        Expect(BytesEqual(BytesBool(false), zero, 1), "GetBytes(bool false) == {00}");

        const uint8_t ab[] = { 0x42, 0x41 };
        Expect(BytesEqual(BytesChar(0x4142), ab, 2), "GetBytes(char 0x4142) == {42,41}");

        const uint8_t m2[] = { 0xFE, 0xFF };
        Expect(BytesEqual(BytesInt16(-2), m2, 2), "GetBytes(int16 -2) == {FE,FF}");

        const uint8_t u102[] = { 0x02, 0x01 };
        Expect(BytesEqual(BytesUInt16(0x0102), u102, 2), "GetBytes(uint16 0x0102) == {02,01}");

        const uint8_t oneI32[] = { 0x01, 0x00, 0x00, 0x00 };
        Expect(BytesEqual(BytesInt32(1), oneI32, 4), "GetBytes(int32 1) == {01,00,00,00}");

        const uint8_t u1234[] = { 0x04, 0x03, 0x02, 0x01 };
        Expect(BytesEqual(BytesUInt32(0x01020304u), u1234, 4), "GetBytes(uint32 0x01020304) == {04,03,02,01}");

        const uint8_t i64pat[] = { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 };
        Expect(BytesEqual(BytesInt64(0x0102030405060708LL), i64pat, 8),
               "GetBytes(int64 0x0102030405060708) == {08,...,01}");
        Expect(BytesEqual(BytesUInt64(0x0102030405060708ULL), i64pat, 8),
               "GetBytes(uint64 0x0102030405060708) == {08,...,01}");

        const uint8_t oneF[] = { 0x00, 0x00, 0x80, 0x3F };
        Expect(BytesEqual(BytesSingle(1.0f), oneF, 4), "GetBytes(single 1.0f) == {00,00,80,3F}");

        const uint8_t oneD[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F };
        Expect(BytesEqual(BytesDouble(1.0), oneD, 8), "GetBytes(double 1.0) == {00,...,F0,3F}");
    }

    // --- 2b. Byte-count audit (bit widths) ---
    Expect(ByteCount(BytesBool(true)) == 1, "bool -> 1 byte");
    Expect(ByteCount(BytesChar(u'a')) == 2, "char -> 2 bytes");
    Expect(ByteCount(BytesInt16(0)) == 2, "int16 -> 2 bytes");
    Expect(ByteCount(BytesUInt16(0)) == 2, "uint16 -> 2 bytes");
    Expect(ByteCount(BytesInt32(0)) == 4, "int32 -> 4 bytes");
    Expect(ByteCount(BytesUInt32(0)) == 4, "uint32 -> 4 bytes");
    Expect(ByteCount(BytesInt64(0)) == 8, "int64 -> 8 bytes");
    Expect(ByteCount(BytesUInt64(0)) == 8, "uint64 -> 8 bytes");
    Expect(ByteCount(BytesSingle(0.0f)) == 4, "single -> 4 bytes");
    Expect(ByteCount(BytesDouble(0.0)) == 8, "double -> 8 bytes");

    // --- 3a. Round-trips: bool / char ---
    Expect(NoThrowToBool(BytesBool(true)), "round-trip bool true");
    Expect(!NoThrowToBool(BytesBool(false)), "round-trip bool false");
    Expect(NoThrowToChar(BytesChar(0x41)) == 0x41, "round-trip char 'A'");
    Expect(NoThrowToChar(BytesChar(0x4E2D)) == 0x4E2D, "round-trip char U+4E2D");

    // --- 3b. Round-trips: int16 / uint16 (incl. extrema) ---
    const int16_t i16vals[] = { 0, 1, -1, 32767, -32768, 0x1234, -0x5678 };
    for (const int16_t v : i16vals)
    {
        Expect(NoThrowToInt16(BytesInt16(v)) == v, "round-trip int16");
    }
    const uint16_t u16vals[] = { 0u, 1u, 65535u, 0xABCDu };
    for (const uint16_t v : u16vals)
    {
        Expect(NoThrowToInt16(BytesUInt16(v)) == static_cast<int16_t>(v),
               "cross-read uint16 bytes as int16 (bit pattern)");
        Expect(NoThrowToUInt16(BytesUInt16(v)) == v, "round-trip uint16");
    }

    // --- 3c. Round-trips: int32 / uint32 (incl. extrema) ---
    const int32_t i32vals[] = { 0, 1, -1, INT32_MAX, INT32_MIN, 0x12345678, -0x12345678 };
    for (const int32_t v : i32vals)
    {
        Expect(NoThrowToInt32(BytesInt32(v)) == v, "round-trip int32");
    }
    const uint32_t u32vals[] = { 0u, 1u, UINT32_MAX, 0xFEDCBA98u };
    for (const uint32_t v : u32vals)
    {
        Expect(il2cpp_bitconverter_to_uint32(BytesUInt32(v)) == v, "round-trip uint32");
    }

    // --- 3d. Round-trips: int64 / uint64 (incl. extrema) ---
    const int64_t i64vals[] = { 0LL, 1LL, -1LL, INT64_MAX, INT64_MIN, 0x0102030405060708LL };
    for (const int64_t v : i64vals)
    {
        Expect(NoThrowToInt64(BytesInt64(v)) == v, "round-trip int64");
    }
    const uint64_t u64vals[] = { 0ULL, 1ULL, UINT64_MAX, 0xFEDCBA9876543210ULL };
    for (const uint64_t v : u64vals)
    {
        Expect(il2cpp_bitconverter_to_uint64(BytesUInt64(v)) == v, "round-trip uint64");
    }

    // --- 3e. Round-trips: single (incl. signed zero / inf / NaN) ---
    const float fvals[] = { 0.0f, 1.5f, -7.25f, 3.14159265f, -1e30f, 1e-30f,
                            INFINITY, -INFINITY };
    for (const float v : fvals)
    {
        Expect(NoThrowToSingle(BytesSingle(v)) == v, "round-trip single");
    }
    const float negZeroF = NoThrowToSingle(BytesSingle(-0.0f));
    Expect(negZeroF == 0.0f && std::signbit(negZeroF), "round-trip single -0.0 preserves signbit");
    const float nanF = NoThrowToSingle(BytesSingle(NAN));
    Expect(nanF != nanF, "round-trip single NaN stays NaN");

    // --- 3f. Round-trips: double (incl. signed zero / inf / NaN) ---
    const double dvals[] = { 0.0, 1.5, -7.25, 3.141592653589793, -2.5e300, 1e-300,
                             INFINITY, -INFINITY };
    for (const double v : dvals)
    {
        Expect(NoThrowToDouble(BytesDouble(v)) == v, "round-trip double");
    }
    const double negZeroD = NoThrowToDouble(BytesDouble(-0.0));
    Expect(negZeroD == 0.0 && std::signbit(negZeroD), "round-trip double -0.0 preserves signbit");
    const double nanD = NoThrowToDouble(BytesDouble(NAN));
    Expect(nanD != nanD, "round-trip double NaN stays NaN");

    // --- 4a. Bit bridges: known IEEE-754 patterns ---
    Expect(il2cpp_bitconverter_single_to_int32_bits(1.0f) == 0x3F800000,
           "SingleToInt32Bits(1.0f) == 0x3F800000");
    Expect(il2cpp_bitconverter_int32_bits_to_single(0x3F800000) == 1.0f,
           "Int32BitsToSingle(0x3F800000) == 1.0f");
    Expect(il2cpp_bitconverter_double_to_int64_bits(1.0) == 0x3FF0000000000000LL,
           "DoubleToInt64Bits(1.0) == 0x3FF0000000000000");
    Expect(il2cpp_bitconverter_int64_bits_to_double(0x3FF0000000000000LL) == 1.0,
           "Int64BitsToDouble(0x3FF0000000000000) == 1.0");
    Expect(static_cast<uint32_t>(il2cpp_bitconverter_single_to_int32_bits(-0.0f)) == 0x80000000u,
           "SingleToInt32Bits(-0.0f) == 0x80000000");
    Expect(il2cpp_bitconverter_double_to_int64_bits(-0.0) == INT64_MIN,
           "DoubleToInt64Bits(-0.0) == INT64_MIN (0x8000000000000000)");

    // --- 4b. Bit bridges: round-trips (bits -> value -> bits is identity) ---
    const float fbitsVals[] = { 3.25f, -7.5f, 1e-30f, -1e30f };
    for (const float v : fbitsVals)
    {
        Expect(il2cpp_bitconverter_int32_bits_to_single(il2cpp_bitconverter_single_to_int32_bits(v)) == v,
               "single bit-bridge round-trip");
    }
    const double dbitsVals[] = { 3.25, -7.5, 1e-300, -2.5e300 };
    for (const double v : dbitsVals)
    {
        Expect(il2cpp_bitconverter_int64_bits_to_double(il2cpp_bitconverter_double_to_int64_bits(v)) == v,
               "double bit-bridge round-trip");
    }
    const int64_t nanPayload = 0x7FF8000000000001LL;
    const double bridgedNaN = il2cpp_bitconverter_int64_bits_to_double(nanPayload);
    Expect(bridgedNaN != bridgedNaN, "Int64BitsToDouble(0x7FF8000000000001) is NaN");
    Expect(il2cpp_bitconverter_double_to_int64_bits(bridgedNaN) == nanPayload,
           "NaN payload survives the double bit-bridge");

    // --- 5a. Error paths: null byte[] -> ArgumentNullException ---
    ExpectDotNetException("to_int32(null) throws ArgumentNullException",
                          kNullArgExTag,
                          "null",
                          [] { (void)NoThrowToInt32(nullptr); });
    ExpectDotNetException("to_double(null) throws ArgumentNullException",
                          kNullArgExTag,
                          "null",
                          [] { (void)NoThrowToDouble(nullptr); });

    // --- 5b. Error paths: EMPTY byte[] -> ArgumentOutOfRangeException
    // (net48 AND .NET Core probe-verified: startIndex 0 >= length 0);
    // non-empty undersized byte[] -> ArgumentException. ---
    il2cpp::Il2CppArray* const emptyBytes = NewByteArray(0);
    il2cpp::Il2CppArray* const oneByte = NewByteArray(1);
    il2cpp::Il2CppArray* const threeBytes = NewByteArray(3);
    il2cpp::Il2CppArray* const sevenBytes = NewByteArray(7);

    ExpectDotNetException("to_bool(empty) throws ArgumentOutOfRangeException",
                          kOutOfRangeTag,
                          "startIndex",
                          [emptyBytes] { (void)NoThrowToBool(emptyBytes); });
    ExpectDotNetException("to_int16(empty) throws ArgumentOutOfRangeException",
                          kOutOfRangeTag,
                          "startIndex",
                          [emptyBytes] { (void)NoThrowToInt16(emptyBytes); });
    ExpectDotNetException("to_int32(empty) throws ArgumentOutOfRangeException",
                          kOutOfRangeTag,
                          "startIndex",
                          [emptyBytes] { (void)NoThrowToInt32(emptyBytes); });
    ExpectDotNetException("to_char(byte[1]) throws ArgumentException",
                          kArgExTag,
                          "at least 2",
                          [oneByte] { (void)NoThrowToChar(oneByte); });
    ExpectDotNetException("to_int16(byte[1]) throws ArgumentException",
                          kArgExTag,
                          "at least 2",
                          [oneByte] { (void)NoThrowToInt16(oneByte); });
    ExpectDotNetException("to_uint16(byte[1]) throws ArgumentException",
                          kArgExTag,
                          "at least 2",
                          [oneByte] { (void)NoThrowToUInt16(oneByte); });
    ExpectDotNetException("to_int32(byte[3]) throws ArgumentException",
                          kArgExTag,
                          "at least 4",
                          [threeBytes] { (void)NoThrowToInt32(threeBytes); });
    ExpectDotNetException("to_single(byte[3]) throws ArgumentException",
                          kArgExTag,
                          "at least 4",
                          [threeBytes] { (void)NoThrowToSingle(threeBytes); });
    ExpectDotNetException("to_int64(byte[7]) throws ArgumentException",
                          kArgExTag,
                          "at least 8",
                          [sevenBytes] { (void)NoThrowToInt64(sevenBytes); });
    ExpectDotNetException("to_double(byte[7]) throws ArgumentException",
                          kArgExTag,
                          "at least 8",
                          [sevenBytes] { (void)NoThrowToDouble(sevenBytes); });

    // --- 5c. Oversized array: reads only the first N bytes (net48 contract) ---
    // Bytes [0..3] = 0x12345678 little-endian; a trailing 0xFF must be ignored.
    il2cpp::Il2CppArray* const bigBytes = NewByteArray(5);
    const uint8_t pat[] = { 0x78, 0x56, 0x34, 0x12, 0xFF };
    for (int32_t i = 0; i < 5; ++i)
    {
        bigBytes->data[i] = static_cast<char>(pat[i]);
    }
    Expect(NoThrowToInt32(bigBytes) == 0x12345678,
           "to_int32(byte[5]) reads only the first 4 bytes");
    Expect(NoThrowToUInt16(bigBytes) == 0x5678,
           "to_uint16(byte[5]) reads only the first 2 bytes");
    Expect(NoThrowToBool(bigBytes),
           "to_bool(byte[5]) reads only the first byte (0x78 != 0)");

    // --- 7. ToString(byte[]): net48 uppercase hex pairs, '-' separated ---
    Expect(HexStringEquals(ToHex(BytesInt32(0x12345678)), "78-56-34-12"),
           "ToString(0x12345678 LE bytes) == 78-56-34-12");
    Expect(HexStringEquals(ToHex(BytesSingle(1.0f)), "00-00-80-3F"),
           "ToString(1.0f LE bytes) == 00-00-80-3F");
    Expect(HexStringEquals(ToHex(BytesInt32(0xDEADBEEF)), "EF-BE-AD-DE"),
           "ToString(0xDEADBEEF LE bytes) == EF-BE-AD-DE");
    Expect(HexStringEquals(ToHex(BytesBool(true)), "01"),
           "ToString(true bytes) == 01");
    Expect(HexStringEquals(ToHex(BytesBool(false)), "00"),
           "ToString(false bytes) == 00");

    // Known probe sequence (net48-verified 2026-08-05): {00,FF,80,12,AB}.
    il2cpp::Il2CppArray* const knownHexBytes = NewByteArray(5);
    const uint8_t knownHex[] = { 0x00, 0xFF, 0x80, 0x12, 0xAB };
    for (int32_t i = 0; i < 5; ++i)
    {
        knownHexBytes->data[i] = static_cast<char>(knownHex[i]);
    }
    Expect(HexStringEquals(ToHex(knownHexBytes), "00-FF-80-12-AB"),
           "ToString({00,FF,80,12,AB}) == 00-FF-80-12-AB");

    // EMPTY array -> "" (net48 ToString(byte[]) has no startIndex, so the
    // empty case is legal -- the ToT-family empty throw does NOT apply).
    il2cpp::Il2CppString* const emptyHex = ToHex(NewByteArray(0));
    Expect(emptyHex->length == 0, "ToString(empty) == empty string");

    // null -> ArgumentNullException (paramName "value").
    ExpectDotNetException("ToString(null) throws ArgumentNullException",
                          kNullArgExTag,
                          "value",
                          [] { (void)ToHex(nullptr); });

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d bitconverter assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime bitconverter tests passed\n");
    return 0;
}
