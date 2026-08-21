//------------------------------------------------------------------------------
// test_encodingconv.cpp -- System.Text.Encoding UTF-16BE (BigEndianUnicode)
// runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the UTF-16BE surface
// added to il2cpp_encoding.h / encoding.cpp:
//   1. il2cpp_encoding_unicodebe_get_bytes: ASCII "AB" -> {00 41 00 42}
//      (each unit's high byte first), Chinese "中" (U+4E2D) -> {4E 2D},
//      empty string -> byte[0], null text (ArgumentNullException).
//   2. il2cpp_encoding_unicodebe_get_string: {00 41 00 42} -> "AB",
//      {4E 2D} -> "中", empty byte[0] -> "", null array
//      (ArgumentNullException), an odd byte count decodes the dangling byte
//      to U+FFFD (the net48 default UnicodeEncoding replacement fallback).
//   3. Round trip: get_bytes then get_string reproduces the original string
//      (ASCII + Chinese).
//   4. il2cpp_encoding_unicodebe_get_chars (ranged form): a sub-range decodes
//      to the expected char[]; a valid surrogate pair passes through as two
//      units; an unpaired surrogate -> U+FFFD; null/negative/out-of-range
//      throws.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_encoding.h"
#include "il2cpp_exception.h"
#include "il2cpp_string.h"

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

// Runs fn; returns true iff it throws a DotNetException whose type chain
// contains 'typeFragment'.
template <typename Fn>
bool ThrowsWithType(Fn fn, const char* typeFragment)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeFragment) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// The managed string's UTF-16 units equal the expected unit array.
bool StringEqualsUnits(il2cpp::Il2CppString* s, const char16_t* expected, int32_t count)
{
    if (s == nullptr || s->length != count)
    {
        return false;
    }
    for (int32_t i = 0; i < count; ++i)
    {
        if (s->chars[i] != expected[i])
        {
            return false;
        }
    }
    return true;
}

// The managed byte[]'s bytes equal the expected byte array.
bool ArrayEqualsBytes(il2cpp::Il2CppArray* a, const uint8_t* expected, int32_t count)
{
    if (a == nullptr || il2cpp_array_get_length(a) != count)
    {
        return false;
    }
    if (count == 0)
    {
        return true;
    }
    const uint8_t* p = reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(a, 0));
    return std::memcmp(p, expected, static_cast<size_t>(count)) == 0;
}

il2cpp::Il2CppArray* MakeBytes(const uint8_t* bytes, int32_t count)
{
    il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, count);
    if (count > 0)
    {
        std::memcpy(il2cpp_array_get_elem_ptr(a, 0), bytes, static_cast<size_t>(count));
    }
    return a;
}

} // namespace

int main()
{
    // --- 1. GetBytes: UTF-16BE encoding ---
    {
        const char16_t ab[2] = { u'A', u'B' };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(ab, 2);
        const uint8_t expected[4] = { 0x00, 0x41, 0x00, 0x42 };
        Expect(ArrayEqualsBytes(il2cpp::runtime::il2cpp_encoding_unicodebe_get_bytes(s),
                                expected, 4),
               "GetBytes(\"AB\") -> {00 41 00 42} (big-endian units)");
    }
    {
        const char16_t zh[1] = { 0x4E2D }; // U+4E2D '中'
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(zh, 1);
        const uint8_t expected[2] = { 0x4E, 0x2D };
        Expect(ArrayEqualsBytes(il2cpp::runtime::il2cpp_encoding_unicodebe_get_bytes(s),
                                expected, 2),
               "GetBytes(\"中\") -> {4E 2D} (high byte first)");
    }
    {
        il2cpp::Il2CppString* empty = il2cpp::il2cpp_string_new(nullptr, 0);
        Expect(ArrayEqualsBytes(il2cpp::runtime::il2cpp_encoding_unicodebe_get_bytes(empty),
                                nullptr, 0),
               "GetBytes(\"\") -> byte[0]");
    }
    {
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::il2cpp_encoding_unicodebe_get_bytes(nullptr);
               }, "System.ArgumentNullException"),
               "GetBytes(null) throws ArgumentNullException");
    }

    // --- 2. GetString: UTF-16BE decoding ---
    {
        const uint8_t bytes[4] = { 0x00, 0x41, 0x00, 0x42 };
        const char16_t ab[2] = { u'A', u'B' };
        Expect(StringEqualsUnits(
                   reinterpret_cast<il2cpp::Il2CppString*>(
                       il2cpp::runtime::il2cpp_encoding_unicodebe_get_string(MakeBytes(bytes, 4))),
                   ab, 2),
               "GetString({00 41 00 42}) -> \"AB\"");
    }
    {
        const uint8_t bytes[2] = { 0x4E, 0x2D };
        const char16_t zh[1] = { 0x4E2D };
        Expect(StringEqualsUnits(
                   reinterpret_cast<il2cpp::Il2CppString*>(
                       il2cpp::runtime::il2cpp_encoding_unicodebe_get_string(MakeBytes(bytes, 2))),
                   zh, 1),
               "GetString({4E 2D}) -> \"中\"");
    }
    {
        Expect(StringEqualsUnits(
                   reinterpret_cast<il2cpp::Il2CppString*>(
                       il2cpp::runtime::il2cpp_encoding_unicodebe_get_string(MakeBytes(nullptr, 0))),
                   nullptr, 0),
               "GetString(byte[0]) -> \"\"");
    }
    {
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::il2cpp_encoding_unicodebe_get_string(nullptr);
               }, "System.ArgumentNullException"),
               "GetString(null) throws ArgumentNullException");
    }
    {
        // Odd byte count: the dangling byte decodes to U+FFFD (net48 default
        // UnicodeEncoding replacement fallback).
        const uint8_t bytes[3] = { 0x00, 0x41, 0x00 };
        const char16_t expected[2] = { u'A', 0xFFFD };
        Expect(StringEqualsUnits(
                   reinterpret_cast<il2cpp::Il2CppString*>(
                       il2cpp::runtime::il2cpp_encoding_unicodebe_get_string(MakeBytes(bytes, 3))),
                   expected, 2),
               "GetString with an odd byte count decodes the dangling byte to U+FFFD");
    }

    // --- 3. Round trip: GetBytes then GetString reproduces the string ---
    {
        const char16_t mixed[3] = { u'A', 0x4E2D, u'B' }; // "A中B"
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(mixed, 3);
        il2cpp::Il2CppArray* bytes = il2cpp::runtime::il2cpp_encoding_unicodebe_get_bytes(s);
        il2cpp::Il2CppString* back = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::runtime::il2cpp_encoding_unicodebe_get_string(bytes));
        Expect(StringEqualsUnits(back, mixed, 3),
               "GetBytes then GetString round-trips \"A中B\"");
    }

    // --- 4. GetChars (ranged form) ---
    {
        // {00 41 00 42 4E 2D} -> char[] { 'A', 'B', 0x4E2D } (full range).
        const uint8_t bytes[6] = { 0x00, 0x41, 0x00, 0x42, 0x4E, 0x2D };
        il2cpp::Il2CppArray* arr = il2cpp::runtime::il2cpp_encoding_unicodebe_get_chars(
            MakeBytes(bytes, 6), 0, 6);
        const char16_t expected[3] = { u'A', u'B', 0x4E2D };
        Expect(arr != nullptr && il2cpp_array_get_length(arr) == 3
               && std::memcmp(il2cpp_array_get_elem_ptr(arr, 0), expected, 6) == 0,
               "GetChars full range decodes to { 'A', 'B', 0x4E2D }");
    }
    {
        // Sub-range {00 42} (index 2, count 2) -> char[] { 'B' }.
        const uint8_t bytes[6] = { 0x00, 0x41, 0x00, 0x42, 0x4E, 0x2D };
        il2cpp::Il2CppArray* arr = il2cpp::runtime::il2cpp_encoding_unicodebe_get_chars(
            MakeBytes(bytes, 6), 2, 2);
        const char16_t expected[1] = { u'B' };
        Expect(arr != nullptr && il2cpp_array_get_length(arr) == 1
               && std::memcmp(il2cpp_array_get_elem_ptr(arr, 0), expected, 2) == 0,
               "GetChars sub-range decodes to { 'B' }");
    }
    {
        // A valid surrogate pair passes through as two units.
        const uint8_t bytes[4] = { 0xD8, 0x3D, 0xDE, 0x00 }; // U+1F600 (big-endian)
        il2cpp::Il2CppArray* arr = il2cpp::runtime::il2cpp_encoding_unicodebe_get_chars(
            MakeBytes(bytes, 4), 0, 4);
        const char16_t expected[2] = { 0xD83D, 0xDE00 };
        Expect(arr != nullptr && il2cpp_array_get_length(arr) == 2
               && std::memcmp(il2cpp_array_get_elem_ptr(arr, 0), expected, 4) == 0,
               "GetChars passes a valid surrogate pair through as two units");
    }
    {
        // An unpaired high surrogate -> U+FFFD.
        const uint8_t bytes[2] = { 0xD8, 0x3D };
        il2cpp::Il2CppArray* arr = il2cpp::runtime::il2cpp_encoding_unicodebe_get_chars(
            MakeBytes(bytes, 2), 0, 2);
        const char16_t expected[1] = { 0xFFFD };
        Expect(arr != nullptr && il2cpp_array_get_length(arr) == 1
               && std::memcmp(il2cpp_array_get_elem_ptr(arr, 0), expected, 2) == 0,
               "GetChars decodes an unpaired high surrogate to U+FFFD");
    }
    {
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::il2cpp_encoding_unicodebe_get_chars(nullptr, 0, 0);
               }, "System.ArgumentNullException"),
               "GetChars(null) throws ArgumentNullException");
        Expect(ThrowsWithType([&] {
                   const uint8_t bytes[2] = { 0x00, 0x41 };
                   il2cpp::runtime::il2cpp_encoding_unicodebe_get_chars(
                       MakeBytes(bytes, 2), -1, 1);
               }, "System.ArgumentOutOfRangeException"),
               "GetChars(negative index) throws ArgumentOutOfRangeException");
        Expect(ThrowsWithType([&] {
                   const uint8_t bytes[2] = { 0x00, 0x41 };
                   il2cpp::runtime::il2cpp_encoding_unicodebe_get_chars(
                       MakeBytes(bytes, 2), 1, 2);
               }, "System.ArgumentOutOfRangeException"),
               "GetChars(index+count past the end) throws ArgumentOutOfRangeException");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d encodingconv assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime encodingconv tests passed\n");
    return 0;
}
