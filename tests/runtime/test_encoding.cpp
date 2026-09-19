//------------------------------------------------------------------------------
// test_encoding.cpp -- System.Text.Encoding runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in il2cpp_encoding.h:
//   1. il2cpp_encoding_utf8_convert: ASCII, Chinese (3-byte sequence), empty
//      input, size-query mode, exact-fit capacity, undersized buffer
//      (ArgumentException), negative length (ArgumentOutOfRangeException).
//   2. il2cpp_encoding_unicode_convert: Chinese -> 0x4E2D, ASCII, empty,
//      invalid byte 0xFF -> U+FFFD, truncated multi-byte tail -> U+FFFD,
//      undersized buffer (ArgumentException).
//   3. il2cpp_encoding_ascii_convert: 7-bit passthrough, non-ASCII -> '?',
//      size-query mode.
//   4. il2cpp_encoding_get_bytes: UTF-8 / UTF-16LE / ASCII dispatch on a
//      managed string, null text (ArgumentNullException), unknown code page
//      (ArgumentException).
//   5. il2cpp_encoding_get_chars: UTF-8 / UTF-16LE / ASCII decode into a
//      managed string, odd UTF-16 length -> dangling byte decodes to U+FFFD
//      (net48 default -- no ArgumentException), unknown code page
//      (ArgumentException), null source (ArgumentNullException), empty
//      input -> empty string.
//   6. Round trip: UTF-8 bytes -> string -> UTF-8 bytes equality.
//   7. Allocating icalls (Encoding.UTF8.GetBytes / GetString): 'ni hao' ->
//      E4 BD A0 E5 A5 BD, GetString round trip, ASCII out-of-range '?',
//      ASCII byte 0xFF -> U+FFFD, UTF-16LE byte order, empty byte[0]/empty
//      string, null text / null array (ArgumentNullException).
//   8. Allocating GetChars (Encoding.UTF8.GetChars(byte[])): 'ni hao' bytes
//      -> char[] { U+4F60, U+597D }, ASCII passthrough + invalid byte 0xFF
//      -> U+FFFD, truncated UTF-8 tail -> one U+FFFD, empty byte[0] ->
//      char[0], null array (ArgumentNullException).
//   9. Allocating GetChars(byte[], int, int) -> char[] on all three pages
//      (net48 range semantics: null bytes -> ArgumentNullException, negative
//      index/count or index+count past the end -> ArgumentOutOfRangeException,
//      invalid sequence -> per-page default replacement). UTF-8: full-range
//      'ni hao' round trip, sub-range decode, 0xFF + truncated tail -> U+FFFD,
//      empty/zero-count ranges -> char[0], null/negative/out-of-range throws.
//      ASCII: passthrough + 0xFF -> U+FFFD, sub-range slice, null/range throws.
//      Unicode: UTF-16LE passthrough, valid surrogate pair passes through as
//      two units, unpaired high/low surrogate -> U+FFFD, dangling odd trailing
//      byte -> U+FFFD, null/negative/out-of-range throws.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_encoding.h"
#include "il2cpp_exception.h"

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

void ExpectBytes(const char* what, const char* actual, int32_t actualLen,
                 const char* expected, int32_t expectedLen)
{
    if (actualLen != expectedLen ||
        std::memcmp(actual, expected, static_cast<size_t>(actualLen)) != 0)
    {
        std::printf("FAIL: %s (len %d, expected %d)\n", what, actualLen, expectedLen);
        ++g_failures;
    }
}

void ExpectDotNetException(const char* what, const char* typeTag,
                           const char* messageFragment,
                           void (*fn)())
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

// Wrappers so the throw-expecting lambdas can ignore the return values.
int32_t NoThrowUtf8(const char16_t* src, int32_t srcLen, char* dst, int32_t cap)
{ return il2cpp::runtime::il2cpp_encoding_utf8_convert(src, srcLen, dst, cap); }
int32_t NoThrowUnicode(const char* src, int32_t len, char16_t* dst, int32_t cap)
{ return il2cpp::runtime::il2cpp_encoding_unicode_convert(src, len, dst, cap); }
// String length through the object header (the runtime API takes Il2CppObject*).
int32_t StrLen(il2cpp::Il2CppString* s)
{ return il2cpp::il2cpp_string_get_length(reinterpret_cast<il2cpp::Il2CppObject*>(s)); }

// Managed byte[] element read (the allocating-icall results are Il2CppArray*).
uint8_t ByteAt(il2cpp::Il2CppArray* arr, int32_t i)
{ return *reinterpret_cast<const uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)); }
// Managed char[] element read (the allocating GetChars result is Il2CppArray*).
char16_t CharAt(il2cpp::Il2CppArray* arr, int32_t i)
{ return *reinterpret_cast<const char16_t*>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)); }
int32_t ArrLen(il2cpp::Il2CppArray* arr)
{ return il2cpp::il2cpp_array_get_length(arr); }

} // namespace

int main()
{
    // --- 1. utf8_convert: ASCII / Chinese / empty / query / capacity ---
    Expect(il2cpp::runtime::il2cpp_encoding_utf8_convert(u"Hello", 5, nullptr, 0) == 5,
           "utf8 query: 'Hello' needs 5 bytes");
    {
        char buf[8];
        ExpectBytes("utf8: 'Hello' encodes as ASCII",
                    buf,
                    il2cpp::runtime::il2cpp_encoding_utf8_convert(u"Hello", 5, buf, 8),
                    "Hello", 5);
    }
    Expect(il2cpp::runtime::il2cpp_encoding_utf8_convert(u"\x4E2D", 1, nullptr, 0) == 3,
           "utf8 query: U+4E2D needs 3 bytes");
    {
        char buf[4];
        const int32_t len = il2cpp::runtime::il2cpp_encoding_utf8_convert(u"\x4E2D", 1, buf, 4);
        const char expected[3] = { static_cast<char>(0xE4), static_cast<char>(0xB8), static_cast<char>(0xAD) };
        ExpectBytes("utf8: U+4E2D encodes as E4 B8 AD", buf, len, expected, 3);
    }
    Expect(il2cpp::runtime::il2cpp_encoding_utf8_convert(u"", 0, nullptr, 0) == 0,
           "utf8: empty input yields 0 bytes");
    {
        // Exact-fit capacity: query size, then convert into exactly that.
        const int32_t need = il2cpp::runtime::il2cpp_encoding_utf8_convert(u"\x4E2D\x4E2D", 2, nullptr, 0);
        char buf[6];
        Expect(need == 6 && il2cpp::runtime::il2cpp_encoding_utf8_convert(u"\x4E2D\x4E2D", 2, buf, need) == 6,
               "utf8: exact-fit capacity converts fully");
    }
    ExpectDotNetException("utf8: undersized destination throws ArgumentException",
                          "System.ArgumentException,System.Exception",
                          "Insufficient",
                          [] { char b[2]; (void)NoThrowUtf8(u"\x4E2D", 1, b, 2); });
    ExpectDotNetException("utf8: negative length throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "Non-negative",
                          [] { (void)NoThrowUtf8(u"a", -1, nullptr, 0); });
    ExpectDotNetException("utf8: null source with length throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)NoThrowUtf8(nullptr, 1, nullptr, 0); });

    // --- 2. unicode_convert: decode / invalid bytes / U+FFFD replacement ---
    Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert("", 0, nullptr, 0) == 0,
           "unicode: empty input yields 0 units");
    Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert("ABC", 3, nullptr, 0) == 3,
           "unicode query: 'ABC' needs 3 units");
    {
        char16_t buf[4];
        const char e4b8ad[3] = { static_cast<char>(0xE4), static_cast<char>(0xB8), static_cast<char>(0xAD) };
        const int32_t len = il2cpp::runtime::il2cpp_encoding_unicode_convert(e4b8ad, 3, buf, 4);
        Expect(len == 1 && buf[0] == 0x4E2D, "unicode: E4 B8 AD decodes to U+4E2D");
    }
    {
        char16_t buf[4];
        const char bad[1] = { static_cast<char>(0xFF) };
        const int32_t len = il2cpp::runtime::il2cpp_encoding_unicode_convert(bad, 1, buf, 4);
        Expect(len == 1 && buf[0] == 0xFFFD, "unicode: invalid byte 0xFF decodes to U+FFFD");
    }
    {
        // Truncated 3-byte tail (E4 B8 without the final AD): the incomplete
        // sequence collapses into ONE U+FFFD replacement unit (observed
        // MultiByteToWideChar behavior, matches .NET's default fallback).
        char16_t buf[4];
        const char truncated[2] = { static_cast<char>(0xE4), static_cast<char>(0xB8) };
        const int32_t len = il2cpp::runtime::il2cpp_encoding_unicode_convert(truncated, 2, buf, 4);
        Expect(len == 1 && buf[0] == 0xFFFD,
               "unicode: truncated tail decodes to one U+FFFD");
    }
    {
        // Broken sequence (E4 B8) followed by an ASCII byte: the lead and
        // continuation collapse into one U+FFFD, then 'A' decodes normally.
        char16_t buf[4];
        const char broken[3] = { static_cast<char>(0xE4), static_cast<char>(0xB8), 'A' };
        const int32_t len = il2cpp::runtime::il2cpp_encoding_unicode_convert(broken, 3, buf, 4);
        Expect(len == 2 && buf[0] == 0xFFFD && buf[1] == 'A',
               "unicode: broken sequence + ASCII decodes to U+FFFD 'A'");
    }
    // net48 replacement-fallback edge vectors (probe-verified against .NET
    // Framework 4.8, matching the Win32 CP_UTF8 decoder on Windows 10 1809+
    // and the hand-written codec): an invalid lead consumes its byte alone;
    // a broken, truncated or completed-but-invalid (overlong / surrogate /
    // above U+10FFFF) sequence consumes the lead plus at most one
    // continuation byte and reprocesses the remaining bytes fresh.
    {
        char16_t buf[8];
        const char surrogateEnc[3] = { static_cast<char>(0xED), static_cast<char>(0xA0), static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(surrogateEnc, 3, buf, 8) == 2 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD,
               "unicode: surrogate-encoding ED A0 80 -> two U+FFFD");
    }
    {
        char16_t buf[8];
        const char overlong[3] = { static_cast<char>(0xE0), static_cast<char>(0x80), static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(overlong, 3, buf, 8) == 2 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD,
               "unicode: overlong E0 80 80 -> two U+FFFD");
    }
    {
        char16_t buf[8];
        const char overlong4[4] = { static_cast<char>(0xF0), static_cast<char>(0x80), static_cast<char>(0x80), static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(overlong4, 4, buf, 8) == 3 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD && buf[2] == 0xFFFD,
               "unicode: overlong F0 80 80 80 -> three U+FFFD");
    }
    {
        char16_t buf[8];
        const char badLead[2] = { static_cast<char>(0xC0), static_cast<char>(0xAF) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(badLead, 2, buf, 8) == 2 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD,
               "unicode: invalid lead C0 + lone continuation AF -> two U+FFFD");
    }
    {
        char16_t buf[8];
        const char above[4] = { static_cast<char>(0xF5), static_cast<char>(0x80), static_cast<char>(0x80), static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(above, 4, buf, 8) == 4 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD && buf[2] == 0xFFFD && buf[3] == 0xFFFD,
               "unicode: invalid lead F5 80 80 80 -> four U+FFFD");
    }
    {
        char16_t buf[8];
        const char broken4[4] = { static_cast<char>(0xF0), static_cast<char>(0x80), static_cast<char>(0x80), 'A' };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(broken4, 4, buf, 8) == 3 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD && buf[2] == 'A',
               "unicode: F0 80 80 A -> two U+FFFD then 'A'");
    }
    {
        char16_t buf[8];
        const char broken3[3] = { static_cast<char>(0xE1), static_cast<char>(0x80), 'A' };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(broken3, 3, buf, 8) == 2 &&
               buf[0] == 0xFFFD && buf[1] == 'A',
               "unicode: E1 80 A -> U+FFFD then 'A'");
    }
    {
        char16_t buf[8];
        const char outOfRange[4] = { static_cast<char>(0xF4), static_cast<char>(0x90), static_cast<char>(0x80), static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(outOfRange, 4, buf, 8) == 3 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD && buf[2] == 0xFFFD,
               "unicode: out-of-range F4 90 80 80 -> three U+FFFD");
    }
    {
        char16_t buf[8];
        const char maxPair[4] = { static_cast<char>(0xF0), static_cast<char>(0x90), static_cast<char>(0x80), static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(maxPair, 4, buf, 8) == 2 &&
               buf[0] == 0xD800 && buf[1] == 0xDC00,
               "unicode: valid 4-byte F0 90 80 80 -> U+10000 surrogate pair");
    }
    {
        char16_t buf[8];
        const char lone[1] = { static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(lone, 1, buf, 8) == 1 && buf[0] == 0xFFFD,
               "unicode: lone continuation byte 0x80 -> U+FFFD");
    }
    {
        char16_t buf[8];
        const char trunc4[3] = { static_cast<char>(0xF0), static_cast<char>(0x80), static_cast<char>(0x80) };
        Expect(il2cpp::runtime::il2cpp_encoding_unicode_convert(trunc4, 3, buf, 8) == 2 &&
               buf[0] == 0xFFFD && buf[1] == 0xFFFD,
               "unicode: truncated F0 80 80 -> two U+FFFD");
    }
    ExpectDotNetException("unicode: undersized destination throws ArgumentException",
                          "System.ArgumentException,System.Exception",
                          "Insufficient",
                          [] { char16_t b[1]; (void)NoThrowUnicode("ABC", 3, b, 1); });

    // --- 3. ascii_convert: 7-bit passthrough / '?' fallback ---
    {
        char buf[4];
        ExpectBytes("ascii: 'ABC' passes through",
                    buf, il2cpp::runtime::il2cpp_encoding_ascii_convert(u"ABC", 3, buf, 4),
                    "ABC", 3);
    }
    Expect(il2cpp::runtime::il2cpp_encoding_ascii_convert(u"A\x4E2D""B", 3, nullptr, 0) == 3,
           "ascii query: length equals input unit count");
    {
        char buf[4];
        const int32_t len = il2cpp::runtime::il2cpp_encoding_ascii_convert(u"A\x4E2D""B", 3, buf, 4);
        ExpectBytes("ascii: non-ASCII unit becomes '?'", buf, len, "A?B", 3);
    }

    // --- 4. get_bytes: dispatch on code page ---
    {
        // Built with explicit units: il2cpp_string_new_from_utf8 is M3-minimal
        // (drops multi-byte input), so non-ASCII fixtures go through new().
        const char16_t zhzh[2] = { 0x4E2D, 0x4E2D };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(zhzh, 2); // zhong zhong
        uint8_t buf[8];
        Expect(il2cpp::runtime::il2cpp_encoding_get_bytes(s, il2cpp::runtime::il2cpp_encoding_code_page::utf8,
                                                          buf, 8) == 6,
               "get_bytes(utf8): 中中 -> 6 bytes");
        Expect(il2cpp::runtime::il2cpp_encoding_get_bytes(s, il2cpp::runtime::il2cpp_encoding_code_page::unicode,
                                                          buf, 8) == 4,
               "get_bytes(unicode): 中中 -> 4 bytes");
        Expect(buf[0] == 0x2D && buf[1] == 0x4E && buf[2] == 0x2D && buf[3] == 0x4E,
               "get_bytes(unicode): UTF-16LE byte order (2D 4E 2D 4E)");
        Expect(il2cpp::runtime::il2cpp_encoding_get_bytes(s, il2cpp::runtime::il2cpp_encoding_code_page::ascii,
                                                          buf, 8) == 2 && buf[0] == '?' && buf[1] == '?',
               "get_bytes(ascii): non-ASCII units become '?'");
        Expect(il2cpp::runtime::il2cpp_encoding_get_bytes(s, il2cpp::runtime::il2cpp_encoding_code_page::utf8,
                                                          nullptr, 0) == 6,
               "get_bytes(utf8) query mode: 6 bytes required");
    }
    ExpectDotNetException("get_bytes: null text throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { uint8_t b[1];
                               (void)il2cpp::runtime::il2cpp_encoding_get_bytes(
                                   nullptr, il2cpp::runtime::il2cpp_encoding_code_page::utf8, b, 1); });
    ExpectDotNetException("get_bytes: unknown code page throws ArgumentException",
                          "System.ArgumentException,System.Exception",
                          "Unknown",
                          [] { il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_utf8("x");
                               uint8_t b[1];
                               (void)il2cpp::runtime::il2cpp_encoding_get_bytes(
                                   s, static_cast<il2cpp::runtime::il2cpp_encoding_code_page>(9999), b, 1); });

    // --- 5. get_chars: decode into a managed string ---
    {
        const char e4b8ad[3] = { static_cast<char>(0xE4), static_cast<char>(0xB8), static_cast<char>(0xAD) };
        il2cpp::Il2CppString* s = il2cpp::runtime::il2cpp_encoding_get_chars(
            reinterpret_cast<const uint8_t*>(e4b8ad), 3, il2cpp::runtime::il2cpp_encoding_code_page::utf8);
        Expect(StrLen(s) == 1 && s->chars[0] == 0x4E2D,
               "get_chars(utf8): E4 B8 AD -> U+4E2D");
    }
    {
        const uint8_t le[4] = { 0x2D, 0x4E, 0x2D, 0x4E };
        il2cpp::Il2CppString* s = il2cpp::runtime::il2cpp_encoding_get_chars(
            le, 4, il2cpp::runtime::il2cpp_encoding_code_page::unicode);
        Expect(StrLen(s) == 2 && s->chars[0] == 0x4E2D && s->chars[1] == 0x4E2D,
               "get_chars(unicode): UTF-16LE bytes -> two U+4E2D");
    }
    {
        const uint8_t mixed[3] = { 'A', static_cast<uint8_t>(0xFF), 'B' };
        il2cpp::Il2CppString* s = il2cpp::runtime::il2cpp_encoding_get_chars(
            mixed, 3, il2cpp::runtime::il2cpp_encoding_code_page::ascii);
        Expect(StrLen(s) == 3 && s->chars[0] == 'A' &&
               s->chars[1] == 0x3F && s->chars[2] == 'B',
               "get_chars(ascii): byte 0xFF decodes to '?' (net48 ASCII fallback)");
    }
    {
        il2cpp::Il2CppString* s = il2cpp::runtime::il2cpp_encoding_get_chars(
            nullptr, 0, il2cpp::runtime::il2cpp_encoding_code_page::utf8);
        Expect(StrLen(s) == 0, "get_chars: empty input -> empty string");
    }
    {
        // net48 default UnicodeEncoding: an odd byte count does NOT throw --
        // the dangling trailing byte 0x42 decodes to ONE U+FFFD (probe-
        // verified against .NET Framework 4.8; the throw-on-invalid
        // UnicodeEncoding ctor variant is out of scope).
        const uint8_t odd[3] = { 'A', 0, 0x42 };
        il2cpp::Il2CppString* s = il2cpp::runtime::il2cpp_encoding_get_chars(
            odd, 3, il2cpp::runtime::il2cpp_encoding_code_page::unicode);
        Expect(StrLen(s) == 2 && s->chars[0] == 'A' && s->chars[1] == 0xFFFD,
               "get_chars(unicode): odd length -> trailing byte decodes to U+FFFD");
    }
    ExpectDotNetException("get_chars: unknown code page throws ArgumentException",
                          "System.ArgumentException,System.Exception",
                          "Unknown",
                          [] { const uint8_t b[1] = { 'x' };
                               (void)il2cpp::runtime::il2cpp_encoding_get_chars(
                                   b, 1, static_cast<il2cpp::runtime::il2cpp_encoding_code_page>(9999)); });
    ExpectDotNetException("get_chars: null source with length throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)il2cpp::runtime::il2cpp_encoding_get_chars(
                                   nullptr, 1, il2cpp::runtime::il2cpp_encoding_code_page::utf8); });

    // --- 6. Round trip: string -> UTF-8 bytes -> string -> bytes ---
    {
        const char16_t greeting[5] = { 0x4F60, 0x597D, 0xFF0C, 0x4E16, 0x754C }; // ni hao, shi jie
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(greeting, 5);
        const int32_t need = il2cpp::runtime::il2cpp_encoding_get_bytes(
            s, il2cpp::runtime::il2cpp_encoding_code_page::utf8, nullptr, 0);
        uint8_t buf[32];
        const int32_t len = il2cpp::runtime::il2cpp_encoding_get_bytes(
            s, il2cpp::runtime::il2cpp_encoding_code_page::utf8, buf, 32);
        il2cpp::Il2CppString* back = il2cpp::runtime::il2cpp_encoding_get_chars(
            buf, len, il2cpp::runtime::il2cpp_encoding_code_page::utf8);
        const uint8_t head[3] = { 0xE4, 0xBD, 0xA0 }; // ni
        Expect(need == 15 && len == 15,
               "round trip: 你好，世界 encodes to 15 UTF-8 bytes");
        Expect(std::memcmp(buf, head, 3) == 0,
               "round trip: first character encodes as E4 BD A0");
        Expect(StrLen(back) == StrLen(s) &&
               std::memcmp(back->chars, s->chars,
                           static_cast<size_t>(StrLen(s)) * 2) == 0,
               "round trip: decoded string equals the original");
    }

    // --- 7. Allocating GetBytes/GetString (Encoding.UTF8.GetBytes etc.) ---
    {
        // GetBytes("ni hao") -> E4 BD A0 E5 A5 BD (the 2x3-byte UTF-8 form).
        const char16_t hello[2] = { 0x4F60, 0x597D };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(hello, 2);
        il2cpp::Il2CppArray* u8 = il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(s);
        const uint8_t expected[6] = { 0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD };
        Expect(ArrLen(u8) == 6 &&
               ByteAt(u8, 0) == expected[0] && ByteAt(u8, 1) == expected[1] &&
               ByteAt(u8, 2) == expected[2] && ByteAt(u8, 3) == expected[3] &&
               ByteAt(u8, 4) == expected[4] && ByteAt(u8, 5) == expected[5],
               "utf8_get_bytes: '你好' -> E4 BD A0 E5 A5 BD");
        // GetString round trip: decode the bytes back to the identical string.
        il2cpp::Il2CppString* back = il2cpp::runtime::il2cpp_encoding_utf8_get_string(u8);
        Expect(StrLen(back) == 2 && back->chars[0] == 0x4F60 && back->chars[1] == 0x597D,
               "utf8_get_string: E4 BD A0 E5 A5 BD round-trips to '你好'");
    }
    {
        // Supplementary-plane round trip: a valid surrogate pair encodes to a
        // 4-byte UTF-8 sequence (F0 9F 98 80 for U+1F600) and decodes back to
        // the identical pair; an unpaired surrogate encodes to EF BF BD (the
        // net48 default encoder fallback).
        const char16_t smile[2] = { 0xD83D, 0xDE00 }; // U+1F600
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(smile, 2);
        il2cpp::Il2CppArray* u8 = il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(s);
        const uint8_t exp4[4] = { 0xF0, 0x9F, 0x98, 0x80 };
        Expect(ArrLen(u8) == 4 && ByteAt(u8, 0) == exp4[0] && ByteAt(u8, 1) == exp4[1] &&
               ByteAt(u8, 2) == exp4[2] && ByteAt(u8, 3) == exp4[3],
               "utf8_get_bytes: surrogate pair U+1F600 -> F0 9F 98 80");
        il2cpp::Il2CppString* back = il2cpp::runtime::il2cpp_encoding_utf8_get_string(u8);
        Expect(StrLen(back) == 2 && back->chars[0] == 0xD83D && back->chars[1] == 0xDE00,
               "utf8_get_string: F0 9F 98 80 -> surrogate pair U+1F600");
        const char16_t hi[1] = { 0xD83D };
        il2cpp::Il2CppString* hs = il2cpp::il2cpp_string_new(hi, 1);
        il2cpp::Il2CppArray* hb = il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(hs);
        Expect(ArrLen(hb) == 3 && ByteAt(hb, 0) == 0xEF && ByteAt(hb, 1) == 0xBF && ByteAt(hb, 2) == 0xBD,
               "utf8_get_bytes: unpaired high surrogate -> EF BF BD");
        const char16_t lo[1] = { 0xDE00 };
        il2cpp::Il2CppString* ls = il2cpp::il2cpp_string_new(lo, 1);
        il2cpp::Il2CppArray* lb = il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(ls);
        Expect(ArrLen(lb) == 3 && ByteAt(lb, 0) == 0xEF && ByteAt(lb, 1) == 0xBF && ByteAt(lb, 2) == 0xBD,
               "utf8_get_bytes: unpaired low surrogate -> EF BF BD");
    }
    {
        // ASCII page: non-ASCII units become '?' (the .NET ASCII default
        // encoder fallback), so 'A' zhong 'B' encodes as "A?B".
        const char16_t mixed[3] = { 'A', 0x4E2D, 'B' };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(mixed, 3);
        il2cpp::Il2CppArray* ascii = il2cpp::runtime::il2cpp_encoding_ascii_get_bytes(s);
        Expect(ArrLen(ascii) == 3 && ByteAt(ascii, 0) == 'A' &&
               ByteAt(ascii, 1) == '?' && ByteAt(ascii, 2) == 'B',
               "ascii_get_bytes: out-of-range unit becomes '?'");
        // ASCII decode: bytes above 0x7F become U+FFFD.
        const uint8_t hi[1] = { 0xFF };
        il2cpp::Il2CppArray* bad = il2cpp::il2cpp_array_new(nullptr, 1, false, 1);
        *reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(bad, 0)) = hi[0];
        il2cpp::Il2CppString* badStr = il2cpp::runtime::il2cpp_encoding_ascii_get_string(bad);
        Expect(StrLen(badStr) == 1 && badStr->chars[0] == 0x3F,
               "ascii_get_string: byte 0xFF decodes to '?' (net48 ASCII fallback)");
    }
    {
        // Unicode page: UTF-16LE passthrough bytes (2D 4E for U+4E2D).
        const char16_t zh[1] = { 0x4E2D };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(zh, 1);
        il2cpp::Il2CppArray* le = il2cpp::runtime::il2cpp_encoding_unicode_get_bytes(s);
        Expect(ArrLen(le) == 2 && ByteAt(le, 0) == 0x2D && ByteAt(le, 1) == 0x4E,
               "unicode_get_bytes: U+4E2D -> UTF-16LE 2D 4E");
        il2cpp::Il2CppString* back = il2cpp::runtime::il2cpp_encoding_unicode_get_string(le);
        Expect(StrLen(back) == 1 && back->chars[0] == 0x4E2D,
               "unicode_get_string: 2D 4E round-trips to U+4E2D");
    }
    {
        // Empty inputs: byte[0] / empty string -- the empty array must not
        // reach the bounds-checked element pointer.
        il2cpp::Il2CppString* empty = il2cpp::il2cpp_string_new(nullptr, 0);
        il2cpp::Il2CppArray* u8 = il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(empty);
        Expect(ArrLen(u8) == 0, "utf8_get_bytes: empty string -> byte[0]");
        il2cpp::Il2CppArray* z = il2cpp::il2cpp_array_new(nullptr, 1, false, 0);
        il2cpp::Il2CppString* s = il2cpp::runtime::il2cpp_encoding_utf8_get_string(z);
        Expect(StrLen(s) == 0, "utf8_get_string: byte[0] -> empty string");
    }
    {
        // net48 default UnicodeEncoding GetString: an odd byte count does not
        // throw -- the dangling trailing byte 0x42 decodes to ONE U+FFFD.
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 'A'; p[1] = 0x00; p[2] = 0x42;
        il2cpp::Il2CppString* s = il2cpp::runtime::il2cpp_encoding_unicode_get_string(a);
        Expect(StrLen(s) == 2 && s->chars[0] == 'A' && s->chars[1] == 0xFFFD,
               "unicode_get_string: odd byte count -> 'A' + U+FFFD (no throw)");
    }
    ExpectDotNetException("utf8_get_bytes: null text throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(nullptr); });
    ExpectDotNetException("utf8_get_string: null array throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)il2cpp::runtime::il2cpp_encoding_utf8_get_string(nullptr); });

    // --- 8. Allocating GetChars (Encoding.UTF8.GetChars(byte[]) -> char[]) ---
    {
        // Round trip: GetBytes('ni hao') -> bytes -> GetChars -> char[] with
        // the identical UTF-16 units (U+4F60 U+597D).
        const char16_t hello[2] = { 0x4F60, 0x597D };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(hello, 2);
        il2cpp::Il2CppArray* u8 = il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(s);
        il2cpp::Il2CppArray* back = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(u8);
        Expect(ArrLen(back) == 2 && CharAt(back, 0) == 0x4F60 && CharAt(back, 1) == 0x597D,
               "utf8_get_chars: E4 BD A0 E5 A5 BD -> char[] '你好'");
    }
    {
        // ASCII bytes pass through; invalid byte 0xFF decodes to U+FFFD and
        // the truncated E4 tail collapses into one U+FFFD (maximal subpart).
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 'A';
        p[1] = static_cast<uint8_t>(0xFF);
        p[2] = 'B';
        p[3] = static_cast<uint8_t>(0xE4);
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(a);
        Expect(ArrLen(c) == 4 && CharAt(c, 0) == 'A' &&
               CharAt(c, 1) == 0xFFFD && CharAt(c, 2) == 'B' &&
               CharAt(c, 3) == 0xFFFD,
               "utf8_get_chars: 0xFF and truncated E4 both decode to U+FFFD");
    }
    {
        // Empty byte[0] -> char[0] (the empty array must not reach the
        // bounds-checked element pointer).
        il2cpp::Il2CppArray* z = il2cpp::il2cpp_array_new(nullptr, 1, false, 0);
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(z);
        Expect(ArrLen(c) == 0, "utf8_get_chars: byte[0] -> char[0]");
    }
    ExpectDotNetException("utf8_get_chars: null array throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)il2cpp::runtime::il2cpp_encoding_utf8_get_chars(nullptr); });

    // --- 9. Allocating GetChars(byte[], int, int) -> char[] (3 pages) ---
    {
        // UTF-8 page: full-range decode of 'ni hao' (E4 BD A0 E5 A5 BD).
        const char16_t hello[2] = { 0x4F60, 0x597D };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(hello, 2);
        il2cpp::Il2CppArray* u8 = il2cpp::runtime::il2cpp_encoding_utf8_get_bytes(s);
        il2cpp::Il2CppArray* back = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(u8, 0, 6);
        Expect(ArrLen(back) == 2 && CharAt(back, 0) == 0x4F60 && CharAt(back, 1) == 0x597D,
               "utf8_get_chars(bytes,0,6): full-range 'ni hao' -> char[]");
    }
    {
        // UTF-8 page: sub-range decode (only the second 3-byte char).
        const uint8_t bytes[6] = { 0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD };
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 6);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        std::memcpy(p, bytes, 6);
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(a, 3, 3);
        Expect(ArrLen(c) == 1 && CharAt(c, 0) == 0x597D,
               "utf8_get_chars(bytes,3,3): sub-range decodes the second char");
    }
    {
        // UTF-8 page: invalid byte 0xFF and truncated E4 tail -> one U+FFFD
        // each (net48 default replacement), then 'B' decodes normally.
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 'A';
        p[1] = static_cast<uint8_t>(0xFF);
        p[2] = 'B';
        p[3] = static_cast<uint8_t>(0xE4);
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(a, 0, 4);
        Expect(ArrLen(c) == 4 && CharAt(c, 0) == 'A' &&
               CharAt(c, 1) == 0xFFFD && CharAt(c, 2) == 'B' &&
               CharAt(c, 3) == 0xFFFD,
               "utf8_get_chars(bytes,0,4): 0xFF and truncated E4 both -> U+FFFD");
    }
    {
        // UTF-8 page: zero-count ranges -> char[0] (start and end of array).
        il2cpp::Il2CppArray* z = il2cpp::il2cpp_array_new(nullptr, 1, false, 0);
        il2cpp::Il2CppArray* c0 = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(z, 0, 0);
        Expect(ArrLen(c0) == 0, "utf8_get_chars(byte[0],0,0): zero count -> char[0]");
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
        il2cpp::Il2CppArray* cEnd = il2cpp::runtime::il2cpp_encoding_utf8_get_chars(a, 2, 0);
        Expect(ArrLen(cEnd) == 0, "utf8_get_chars(bytes,2,0): zero count at end -> char[0]");
    }
    ExpectDotNetException("utf8_get_chars(bytes,int,int): null bytes throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)il2cpp::runtime::il2cpp_encoding_utf8_get_chars(nullptr, 0, 1); });
    ExpectDotNetException("utf8_get_chars(bytes,int,int): negative index throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "Non-negative",
                          [] { il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
                               (void)il2cpp::runtime::il2cpp_encoding_utf8_get_chars(a, -1, 1); });
    ExpectDotNetException("utf8_get_chars(bytes,int,int): negative count throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "Non-negative",
                          [] { il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
                               (void)il2cpp::runtime::il2cpp_encoding_utf8_get_chars(a, 0, -1); });
    ExpectDotNetException("utf8_get_chars(bytes,int,int): index+count past end throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "out of bounds",
                          [] { il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
                               (void)il2cpp::runtime::il2cpp_encoding_utf8_get_chars(a, 1, 2); });

    {
        // ASCII page: full-range decode, 0xFF -> U+FFFD (default replacement).
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 'A';
        p[1] = static_cast<uint8_t>(0xFF);
        p[2] = 'B';
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_ascii_get_chars(a, 0, 3);
        Expect(ArrLen(c) == 3 && CharAt(c, 0) == 'A' &&
               CharAt(c, 1) == 0x3F && CharAt(c, 2) == 'B',
               "ascii_get_chars(bytes,0,3): 0xFF -> '?' (net48 ASCII fallback)");
    }
    {
        // ASCII page: sub-range slice decodes only the requested bytes.
        const uint8_t abc[3] = { 'A', 'B', 'C' };
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        std::memcpy(p, abc, 3);
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_ascii_get_chars(a, 1, 2);
        Expect(ArrLen(c) == 2 && CharAt(c, 0) == 'B' && CharAt(c, 1) == 'C',
               "ascii_get_chars(bytes,1,2): sub-range decodes BC");
    }
    ExpectDotNetException("ascii_get_chars(bytes,int,int): null bytes throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)il2cpp::runtime::il2cpp_encoding_ascii_get_chars(nullptr, 0, 1); });
    ExpectDotNetException("ascii_get_chars(bytes,int,int): negative count throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "Non-negative",
                          [] { il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
                               (void)il2cpp::runtime::il2cpp_encoding_ascii_get_chars(a, 0, -1); });
    ExpectDotNetException("ascii_get_chars(bytes,int,int): index past end throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "out of bounds",
                          [] { il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
                               (void)il2cpp::runtime::il2cpp_encoding_ascii_get_chars(a, 3, 0); });

    {
        // Unicode page: UTF-16LE passthrough (2D 4E for U+4E2D).
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 0x2D;
        p[1] = 0x4E;
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, 0, 2);
        Expect(ArrLen(c) == 1 && CharAt(c, 0) == 0x4E2D,
               "unicode_get_chars(bytes,0,2): 2D 4E -> U+4E2D");
    }
    {
        // Unicode page: valid surrogate pair passes through as two units.
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 0x00; p[1] = 0xD8; // U+D800 (high)
        p[2] = 0x00; p[3] = 0xDC; // U+DC00 (low)
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, 0, 4);
        Expect(ArrLen(c) == 2 && CharAt(c, 0) == 0xD800 && CharAt(c, 1) == 0xDC00,
               "unicode_get_chars: valid high+low surrogate pair passes through");
    }
    {
        // Unicode page: unpaired high surrogate and unpaired low surrogate ->
        // one U+FFFD each (net48 default replacement).
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 6);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 0x00; p[1] = 0xD8; // unpaired high
        p[2] = 'A';  p[3] = 0x00; // U+0041 normal
        p[4] = 0x00; p[5] = 0xDC; // unpaired low
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, 0, 6);
        Expect(ArrLen(c) == 3 && CharAt(c, 0) == 0xFFFD &&
               CharAt(c, 1) == 'A' && CharAt(c, 2) == 0xFFFD,
               "unicode_get_chars: unpaired surrogates -> U+FFFD");
    }
    {
        // Unicode page: dangling odd trailing byte -> one U+FFFD.
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 0x2D; p[1] = 0x4E; p[2] = 0x42; // U+4E2D then dangling 0x42
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, 0, 3);
        Expect(ArrLen(c) == 2 && CharAt(c, 0) == 0x4E2D && CharAt(c, 1) == 0xFFFD,
               "unicode_get_chars: dangling odd byte -> U+FFFD");
    }
    {
        // Unicode page: consecutive unpaired surrogates -> one U+FFFD each
        // (high-high; the net48 default replacement collapses each unit).
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 0x00; p[1] = 0xD8; // unpaired high
        p[2] = 0x00; p[3] = 0xD8; // unpaired high
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, 0, 4);
        Expect(ArrLen(c) == 2 && CharAt(c, 0) == 0xFFFD && CharAt(c, 1) == 0xFFFD,
               "unicode_get_chars: two unpaired highs -> two U+FFFD");
    }
    {
        // Unicode page: a leading BOM (FF FE) is NOT stripped by the encoding
        // decoder (net48: GetString keeps U+FEFF as the first character).
        il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        uint8_t* p = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0));
        p[0] = 0xFF; p[1] = 0xFE; // U+FEFF (BOM)
        p[2] = 0x41; p[3] = 0x00; // 'A'
        il2cpp::Il2CppArray* c = il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, 0, 4);
        Expect(ArrLen(c) == 2 && CharAt(c, 0) == 0xFEFF && CharAt(c, 1) == 'A',
               "unicode_get_chars: BOM bytes FF FE decode to U+FEFF (not stripped)");
    }
    ExpectDotNetException("unicode_get_chars(bytes,int,int): null bytes throws ArgumentNullException",
                          "System.ArgumentNullException,System.Exception",
                          "null",
                          [] { (void)il2cpp::runtime::il2cpp_encoding_unicode_get_chars(nullptr, 0, 1); });
    ExpectDotNetException("unicode_get_chars(bytes,int,int): negative index throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "Non-negative",
                          [] { il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 2);
                               (void)il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, -1, 1); });
    ExpectDotNetException("unicode_get_chars(bytes,int,int): index+count past end throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.Exception",
                          "out of bounds",
                          [] { il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
                               (void)il2cpp::runtime::il2cpp_encoding_unicode_get_chars(a, 2, 4); });

    if (g_failures == 0)
    {
        std::printf("test_encoding: all checks passed\n");
        return 0;
    }
    std::printf("test_encoding: %d check(s) FAILED\n", g_failures);
    return 1;
}
