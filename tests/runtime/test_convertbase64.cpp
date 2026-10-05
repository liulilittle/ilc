//------------------------------------------------------------------------------
// test_convertbase64.cpp -- System.Convert Base64 icall runtime unit tests
// (il2cpp_convertbase64.h) + ToChar spot verification (il2cpp_convert.h).
//
// SEMANTIC BASELINE: .NET Framework 4.8 standard RFC 4648 Base64
// (probe-verified against csc.exe net48 on 2026-08-04 -- see
// docs/audit/baseconv-conformance.md; .NET Core agrees on everything
// except the "XYZ==" shapes, which net48 accepts and Core rejects):
//   * ToBase64String: 3 bytes -> 4 chars; final partial quantum padded
//     with '='; output length is always a multiple of 4. Null -> ArgumentNull.
//   * FromBase64String: whitespace (space/tab/CR/LF) ignored anywhere;
//     '=' legal only in the final quantum: single '=' after exactly three
//     valid chars, or '==' after exactly two (whitespace may separate the
//     pads); "TWE==" (3 valid + '==') decodes to ONE byte (net48);
//     "AB= " (single '=' after two chars) throws; unpadded "TWE"/"TW"
//     throw; illegal character -> FormatException; leftover single char
//     (%4==1) -> FormatException; null -> ArgumentNullException.
//   * ToChar(int): CHECKED 0..0xFFFF (already covered by test_convert.cpp;
//     spot-verified here).
//
// Known-answer vectors:
//   "Man"          -> "TWFu"
//   {0xFF}         -> "/w=="
//   {0xFF,0xFE}    -> "//4="
//   ni hao shi jie UTF-8 (E4 BD A0 E5 A5 BD E4 B8 96 E7 95 8C) -> "5L2g5aW95LiW55WM"
//
// Coverage (normal + edge + error paths):
//   1. to_base64: empty array -> ""; exact vectors; 1/2/3-byte quantums.
//   2. from_base64: "" -> empty; exact vectors; unpadded 4-char quantum;
//      net48 final-quantum padding shapes; whitespace and CRLF inside the
//      string are ignored.
//   3. Roundtrip: UTF-8 Chinese bytes -> encode -> decode == original.
//   4. Error paths: null (ArgumentNullException); illegal character, '='
//      starting a quantum, char after '=', triple padding, single leftover
//      char (all FormatException).
//   5. ToChar spot check: passthrough 0 / 'A' / 0xFFFF; OverflowException
//      for -1 and 0x10000.
//
// Plain executable + minimal assertion harness (see test_convert.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_convertbase64.h"
#include "il2cpp_convert.h"
#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"
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

// Build a byte[] (elemSize 1) from a literal.
il2cpp::Il2CppArray* BytesFrom(const unsigned char* data, int32_t n)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 1, false, n);
    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<unsigned char*>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)) = data[i];
    }
    return arr;
}

// Build a managed string from explicit UTF-16 units (needed for control-char
// inputs like \x01 / \x0B -- il2cpp_string_new_from_cs takes ASCII literals).
il2cpp::Il2CppString* String16(const char16_t* units, int32_t n)
{
    return il2cpp::il2cpp_string_new(units, n);
}

bool ArrayBytesEqual(il2cpp::Il2CppArray* arr, const unsigned char* expected, int32_t n)
{
    if (arr == nullptr || il2cpp::il2cpp_array_get_length(arr) != n)
    {
        return false;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        if (*reinterpret_cast<const unsigned char*>(il2cpp::il2cpp_array_get_elem_ptr(arr, i))
            != expected[i])
        {
            return false;
        }
    }
    return true;
}

// ExpectConvertThrows: run fn(), require a DotNetException whose type-name
// chain starts with 'typeTag' (prefix match -- see test_convert.cpp).
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

} // namespace

int main()
{
    using il2cpp::il2cpp_array_get_elem_ptr;
    using il2cpp::runtime::il2cpp_convert_from_base64_string;
    using il2cpp::runtime::il2cpp_convert_to_base64_string;

    // --- 1. ToBase64String: empty + known-answer vectors (padding paths). ---
    Expect(StringIs(il2cpp_convert_to_base64_string(BytesFrom(nullptr, 0)), ""),
           "to_base64(empty) == \"\"");
    {
        static const unsigned char man[] = { 0x4D, 0x61, 0x6E }; // "Man"
        Expect(StringIs(il2cpp_convert_to_base64_string(BytesFrom(man, 3)), "TWFu"),
               "to_base64(\"Man\") == \"TWFu\"");
    }
    {
        static const unsigned char one[] = { 0xFF };
        Expect(StringIs(il2cpp_convert_to_base64_string(BytesFrom(one, 1)), "/w=="),
               "to_base64({0xFF}) == \"/w==\" (1 leftover byte -> '==' pad)");
    }
    {
        static const unsigned char two[] = { 0xFF, 0xFE };
        Expect(StringIs(il2cpp_convert_to_base64_string(BytesFrom(two, 2)), "//4="),
               "to_base64({0xFF,0xFE}) == \"//4=\" (2 leftover bytes -> '=' pad)");
    }
    {
        // 4 bytes = one full quantum + 1 leftover; 5 bytes = full + 2 leftover.
        static const unsigned char four[] = { 0x01, 0x02, 0x03, 0x04 };
        Expect(StringIs(il2cpp_convert_to_base64_string(BytesFrom(four, 4)), "AQIDBA=="),
               "to_base64({01,02,03,04}) == \"AQIDBA==\" (1 leftover byte)");
        static const unsigned char five[] = { 0x01, 0x02, 0x03, 0x04, 0x05 };
        Expect(StringIs(il2cpp_convert_to_base64_string(BytesFrom(five, 5)), "AQIDBAU="),
               "to_base64({01,02,03,04,05}) == \"AQIDBAU=\" (2 leftover bytes)");
    }
    {
        // The '+' and '/' alphabet chars are exercised by these bytes
        // (probe-verified net48: {FB,EF,BE,AD,DE} -> "++++rd4=").
        static const unsigned char plus[] = { 0xFB, 0xEF, 0xBE, 0xAD, 0xDE };
        Expect(StringIs(il2cpp_convert_to_base64_string(BytesFrom(plus, 5)), "++++rd4="),
               "to_base64({FB,EF,BE,AD,DE}) == \"++++rd4=\" (+/ alphabet edge)");
    }
    {
        // All 256 byte values, 3 full quantums + 1 leftover; roundtrip identity.
        unsigned char all[256];
        for (int i = 0; i < 256; ++i) all[i] = static_cast<unsigned char>(i);
        il2cpp::Il2CppString* enc = il2cpp_convert_to_base64_string(BytesFrom(all, 256));
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(enc), all, 256),
               "to_base64(all 256 bytes) roundtrips through from_base64");
    }

    // --- 2. FromBase64String: empty + vectors + padding shapes + ws/CRLF. ---
    Expect(il2cpp_convert_from_base64_string(il2cpp::il2cpp_string_new_from_cs("")) != nullptr
               && il2cpp::il2cpp_array_get_length(
                      il2cpp_convert_from_base64_string(il2cpp::il2cpp_string_new_from_cs(""))) == 0,
           "from_base64(\"\") -> empty array");
    {
        static const unsigned char man[] = { 0x4D, 0x61, 0x6E };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWFu")),
                               man, 3),
               "from_base64(\"TWFu\") == \"Man\"");
    }
    {
        static const unsigned char ff[] = { 0xFF };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("/w==")),
                               ff, 1),
               "from_base64(\"/w==\") == {0xFF}");
    }
    {
        static const unsigned char fffe[] = { 0xFF, 0xFE };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("//4=")),
                               fffe, 2),
               "from_base64(\"//4=\") == {0xFF,0xFE}");
    }
    {
        static const unsigned char ma[] = { 0x4D, 0x61 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWE=")),
                               ma, 2),
               "from_base64(\"TWE=\") padded input decodes to 2 bytes");
    }

    // --- 2b. net48 final-quantum padding shapes (probe-verified 2026-08-04):
    // "TWE==" (3 valid + "==") decodes to ONE byte -- the byte comes from
    // the LAST two valid chars (net48; .NET Core rejects this shape). ---
    {
        static const unsigned char twe2[] = { 0x58 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWE==")),
                               twe2, 1),
               "from_base64(\"TWE==\") == {0x58} (3 valid chars + '==' -> 1 byte, net48)");
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWE ==")),
                               twe2, 1),
               "from_base64(\"TWE ==\") == {0x58} (whitespace inside the final quantum)");
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWE= =")),
                               twe2, 1),
               "from_base64(\"TWE= =\") == {0x58} (whitespace between the pads)");
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWE== ")),
                               twe2, 1),
               "from_base64(\"TWE== \") == {0x58} (trailing whitespace after pads)");
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWE==\n")),
                               twe2, 1),
               "from_base64(\"TWE==<LF>\") == {0x58} (trailing newline after pads)");
    }
    {
        // "TW==" (2 valid + "==") is the standard 1-byte shape; "AB= ="
        // proves whitespace between the pads is legal.
        static const unsigned char tw2[] = { 0x4D };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TW==")),
                               tw2, 1),
               "from_base64(\"TW==\") == {0x4D}");
        static const unsigned char ab2[] = { 0x00 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("AB= =")),
                               ab2, 1),
               "from_base64(\"AB= =\") == {0x00} (whitespace between the pads)");
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("AB= = ")),
                               ab2, 1),
               "from_base64(\"AB= = \") == {0x00} (trailing whitespace after pads)");
    }
    {
        // "AAAAAA==" (6 valid + "==") -> 4 zero bytes; "AAAA==" / "AAAAA=="
        // throw (probe-verified net48).
        static const unsigned char zeros4[] = { 0, 0, 0, 0 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("AAAAAA==")),
                               zeros4, 4),
               "from_base64(\"AAAAAA==\") == 4 zero bytes (full quantum + '==')");
    }
    {
        static const unsigned char man[] = { 0x4D, 0x61, 0x6E };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TW\r\nFu\n")),
                               man, 3),
               "from_base64(\"TW<CRLF>Fu\") ignores CR/LF whitespace");
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("  T W\tF u  ")),
                               man, 3),
               "from_base64(\"  T W<TAB>F u  \") ignores space/tab");
    }

    // --- 3. Roundtrip: UTF-8 Chinese bytes encode/decode identity. ---
    {
        // ni hao shi jie in UTF-8 (12 bytes, 3 full quantums).
        static const unsigned char utf8[] =
            { 0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD, 0xE4, 0xB8, 0x96, 0xE7, 0x95, 0x8C };
        il2cpp::Il2CppString* enc =
            il2cpp_convert_to_base64_string(BytesFrom(utf8, 12));
        Expect(StringIs(enc, "5L2g5aW95LiW55WM"),
               "to_base64(你好世界 UTF-8) == \"5L2g5aW95LiW55WM\"");
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(enc), utf8, 12),
               "from_base64(to_base64(你好世界 UTF-8)) == original bytes");
    }

    // --- 3b. net48 re-probe 2026-08-08 (csc.exe Framework64 v4.0.30319):
    // unpadded length coupling, padded-length coupling, +/ alphabet edge. ---
    {
        // Unpadded full-quantum inputs are legal (leftover 0).
        static const unsigned char abcd[] = { 0x00, 0x10, 0x83 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("ABCD")),
                               abcd, 3),
               "from_base64(\"ABCD\") == {00,10,83} (unpadded 4-char quantum)");
        static const unsigned char abcdefgh[] = { 0x00, 0x10, 0x83, 0x10, 0x51, 0x87 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("ABCDEFGH")),
                               abcdefgh, 6),
               "from_base64(\"ABCDEFGH\") == 6 bytes (two unpadded quantums)");
        // Whitespace-only input -> empty array (net48 & Core agree).
        Expect(il2cpp_convert_from_base64_string(
                   il2cpp::il2cpp_string_new_from_cs(" \r\n\t ")) != nullptr
                   && il2cpp::il2cpp_array_get_length(
                          il2cpp_convert_from_base64_string(
                              il2cpp::il2cpp_string_new_from_cs(" \r\n\t "))) == 0,
               "from_base64(\" \\r\\n\\t \") -> empty array (whitespace only)");
        // Alphabet edge: 'z'/'/' -- net48: "z///" -> {CF,FF,FF}.
        static const unsigned char zslash[] = { 0xCF, 0xFF, 0xFF };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("z///")),
                               zslash, 3),
               "from_base64(\"z///\") == {CF,FF,FF}");
        // 6 valid + "==" (leftover 2 + 2 pads) -> 4 bytes (net48).
        static const unsigned char abcdef6[] = { 0x00, 0x10, 0x83, 0x10 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("ABCDEF==")),
                               abcdef6, 4),
               "from_base64(\"ABCDEF==\") == 4 bytes (leftover 2 + '==')");
        // 7 valid + '=' (leftover 3 + 1 pad) -> 5 bytes (net48).
        static const unsigned char abcdefg7[] = { 0x00, 0x10, 0x83, 0x10, 0x51 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("ABCDEFG=")),
                               abcdefg7, 5),
               "from_base64(\"ABCDEFG=\") == 5 bytes (leftover 3 + '=')");
        // 7 valid + "==" (leftover 3 + 2 pads) -> 4 bytes (net48 quirk).
        static const unsigned char abcdefg8[] = { 0x00, 0x10, 0x83, 0x14 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("ABCDEFG==")),
                               abcdefg8, 4),
               "from_base64(\"ABCDEFG==\") == 4 bytes (leftover 3 + '==')");
        // Cross-quantum net48 quirk: 3 valid + "==" AFTER a full quantum
        // ("TWETWE==": TWE flushes 3 bytes, the trailing TWE== yields 1).
        static const unsigned char twetwe[] = { 0x4D, 0x61, 0x13, 0x58 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWETWE==")),
                               twetwe, 4),
               "from_base64(\"TWETWE==\") == {4D,61,13,58} (full quantum + 3valid==' quirk)");
        // 6 valid + "==" (leftover 2 + 2 pads) -> 4 bytes (net48).
        static const unsigned char twe2[] = { 0x4D, 0x61, 0x6E, 0x58 };
        Expect(ArrayBytesEqual(il2cpp_convert_from_base64_string(
                                   il2cpp::il2cpp_string_new_from_cs("TWFuTWE==")),
                               twe2, 4),
               "from_base64(\"TWFuTWE==\") == {4D,61,6E,58} (full quantum + 3valid==' quirk)");
    }

    // --- 4. Error paths. ---
    ExpectConvertThrows("to_base64(null) throws ArgumentNullException",
                        "System.ArgumentNullException",
                        [] { (void)il2cpp_convert_to_base64_string(nullptr); });
    ExpectConvertThrows("from_base64(null) throws ArgumentNullException",
                        "System.ArgumentNullException",
                        [] { (void)il2cpp_convert_from_base64_string(nullptr); });
    ExpectConvertThrows("from_base64(\"A\") throws FormatException (length % 4 == 1)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("A")); });
    ExpectConvertThrows("from_base64(\"TW\") throws FormatException (unpadded 2 chars)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("TW")); });
    ExpectConvertThrows("from_base64(\"TWE\") throws FormatException (unpadded 3 chars)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("TWE")); });
    ExpectConvertThrows("from_base64(\"AB=\") throws FormatException (single '=' after 2 chars)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("AB=")); });
    ExpectConvertThrows("from_base64(\"AB= \") throws FormatException ('=' + trailing whitespace only)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("AB= ")); });
    ExpectConvertThrows("from_base64(\"A==\") throws FormatException ('==' after 1 char)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("A==")); });
    ExpectConvertThrows("from_base64(\"ABCD==\") throws FormatException (4 valid + '==')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCD==")); });
    ExpectConvertThrows("from_base64(\"AAAAA==\") throws FormatException (5 valid + '==')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("AAAAA==")); });
    ExpectConvertThrows("from_base64(\"!!!!\") throws FormatException (illegal chars)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("!!!!")); });
    ExpectConvertThrows("from_base64(\"=AAA\") throws FormatException ('=' starts a quantum)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("=AAA")); });
    ExpectConvertThrows("from_base64(\"TWE===\") throws FormatException (triple padding)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("TWE===")); });
    ExpectConvertThrows("from_base64(\"TWE=A\") throws FormatException (char after '=')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("TWE=A")); });
    ExpectConvertThrows("from_base64(\"TW$\") throws FormatException ('$' not in alphabet)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("TW$")); });

    // --- 4b. net48 length-coupling throws (re-probed 2026-08-08):
    // unpadded leftovers 1/2/3 chars and wrong padding-length pairs all
    // throw FormatException (probe: ABCDE/ABCDEF/ABCDEFG/ABCDEFGHI/ABCDEFGHIJK
    // /ABCDEFGHIJKLM -> throw; ABCD=/ABCDE=/ABCDEF=/ABCDEFGHI= -> throw;
    // ABCD==/ABCDE==/ABCDEFGHI== -> throw; ABCD===/ABCDEF=== -> throw). ---
    ExpectConvertThrows("from_base64(\"ABCDE\") throws (unpadded leftover 1)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDE")); });
    ExpectConvertThrows("from_base64(\"ABCDEF\") throws (unpadded leftover 2)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEF")); });
    ExpectConvertThrows("from_base64(\"ABCDEFG\") throws (unpadded leftover 3)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEFG")); });
    ExpectConvertThrows("from_base64(\"ABCDEFGHI\") throws (unpadded leftover 1)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEFGHI")); });
    ExpectConvertThrows("from_base64(\"ABCDEFGHIJK\") throws (unpadded leftover 3)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEFGHIJK")); });
    ExpectConvertThrows("from_base64(\"ABCDEFGHIJKLM\") throws (unpadded leftover 1)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEFGHIJKLM")); });
    ExpectConvertThrows("from_base64(\"ABCD=\") throws (leftover 0 + '=')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCD=")); });
    ExpectConvertThrows("from_base64(\"ABCDE=\") throws (leftover 1 + '=')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDE=")); });
    ExpectConvertThrows("from_base64(\"ABCDEF=\") throws (leftover 2 + '=')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEF=")); });
    ExpectConvertThrows("from_base64(\"ABCDEFGHI=\") throws (leftover 1 + '=')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEFGHI=")); });
    ExpectConvertThrows("from_base64(\"ABCD==\") throws (leftover 0 + '==')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCD==")); });
    ExpectConvertThrows("from_base64(\"ABCDE==\") throws (leftover 1 + '==')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDE==")); });
    ExpectConvertThrows("from_base64(\"ABCDEFGHI==\") throws (leftover 1 + '==')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEFGHI==")); });
    ExpectConvertThrows("from_base64(\"ABCD===\") throws (triple padding)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCD===")); });
    ExpectConvertThrows("from_base64(\"ABCDEF===\") throws (triple padding)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("ABCDEF===")); });

    // --- 4c. net48 control-character throws (re-probed 2026-08-08):
    // only space/tab/CR/LF are whitespace; \x01/\x08/\x0B/\x0C/\x1B/\x80/\x7F
    // and a NUL are NOT, so each throws FormatException -- even after '='. ---
    ExpectConvertThrows("from_base64(\"TW\\x01Fu\") throws (control char)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x01" u"Fu", 5)); });
    ExpectConvertThrows("from_base64(\"TW\\x08Fu\") throws (backspace)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x08" u"Fu", 5)); });
    ExpectConvertThrows("from_base64(\"TW\\x0BFu\") throws (vertical tab)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x0B" u"Fu", 5)); });
    ExpectConvertThrows("from_base64(\"TW\\x0CFu\") throws (form feed)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x0C" u"Fu", 5)); });
    ExpectConvertThrows("from_base64(\"TW\\x1BFu\") throws (escape)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x1B" u"Fu", 5)); });
    ExpectConvertThrows("from_base64(\"TW\\x00==\") throws (NUL)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x00" u"==", 5)); });
    ExpectConvertThrows("from_base64(\"TW\\x80\") throws (U+0080)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x80", 3)); });
    ExpectConvertThrows("from_base64(\"TW\\x7F\") throws (U+007F)",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TW\x7F", 3)); });
    ExpectConvertThrows("from_base64(\"TWE=\\x0B\") throws (control after '=')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 String16(u"TWE=\x0B", 5)); });
    ExpectConvertThrows("from_base64(\"TWETWE=\") throws (leftover 2 + 1 '=')",
                        "System.FormatException",
                        [] { (void)il2cpp_convert_from_base64_string(
                                 il2cpp::il2cpp_string_new_from_cs("TWETWE=")); });

    // --- 5. Convert.ToChar(int) spot verification (full coverage in
    // test_convert.cpp): CHECKED 0..0xFFFF conversion. ---
    Expect(il2cpp::runtime::icall_convert_to_char_i4(0) == 0, "to_char(0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_char_i4(0x41) == 0x41, "to_char('A') == 'A'");
    Expect(il2cpp::runtime::icall_convert_to_char_i4(0xFFFF) == 0xFFFF, "to_char(0xFFFF) == 0xFFFF");
    ExpectConvertThrows("to_char(-1) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)il2cpp::runtime::icall_convert_to_char_i4(-1); });
    ExpectConvertThrows("to_char(0x10000) throws OverflowException",
                        "System.OverflowException",
                        [] { (void)il2cpp::runtime::icall_convert_to_char_i4(0x10000); });

    if (g_failures == 0)
    {
        std::printf("PASS: test_convertbase64 (base64 encode/decode + ToChar spot)\n");
        return 0;
    }
    std::printf("FAILED: %d assertion(s)\n", g_failures);
    return 1;
}
