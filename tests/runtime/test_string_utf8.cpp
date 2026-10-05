//------------------------------------------------------------------------------
// test_string_utf8.cpp -- il2cpp_string_new_from_utf8 transcoding tests.
//
// Coverage (normal + edge + error paths; expectations verified against a
// net48 reference run with UTF8Encoding replacement fallback on
// 2026-08-03 -- the net48 decoder consumes lead + offending continuation
// byte as ONE U+FFFD for overlong/surrogate-range sequences, and a single
// U+FFFD for a truncated sequence):
//   1. BMP 3-byte sequences (Chinese) decode to single UTF-16 units.
//   2. Astral 4-byte sequences decode to a surrogate pair.
//   3. Mixed ASCII + Chinese round-trip in one buffer.
//   4. Empty input yields a zero-length string.
//   5. Invalid bytes (lone continuation, invalid leads C0/C1/F5) -> U+FFFD.
//   6. Truncated sequences at the NUL terminator -> ONE U+FFFD.
//   7. Overlong / surrogate-range / beyond-U+10FFFF sequences -> U+FFFD,
//      consumed per the net48 reference (2/3 units, not one per byte).
//   8. Boundary code points (U+D7FF valid; U+10FFFF -> U+DBFF U+DFFF).
//
// Byte buffers are written as explicit hex arrays so the test is immune to
// source-file encoding. Same harness as the other runtime tests: any failed
// Expect() prints FAIL and exits non-zero.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>

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

// Decode 'utf8' (explicit length, NUL-free content) and check the result
// against the expected UTF-16 units.
void CheckDecode(const char* what, const unsigned char* utf8, size_t byteLen,
                 const char16_t* expected, int32_t expectedLen)
{
    char* buf = new char[byteLen + 1];
    for (size_t i = 0; i < byteLen; ++i)
    {
        buf[i] = static_cast<char>(utf8[i]);
    }
    buf[byteLen] = '\0';
    il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_utf8(buf);
    delete[] buf;
    if (s == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (s->length != expectedLen)
    {
        std::printf("FAIL: %s: length %d, expected %d\n", what, s->length, expectedLen);
        ++g_failures;
        return;
    }
    bool ok = true;
    for (int32_t i = 0; i < expectedLen; ++i)
    {
        if (s->chars[i] != expected[i])
        {
            ok = false;
            std::printf("FAIL: %s: unit %d is U+%04X, expected U+%04X\n",
                        what, i, static_cast<unsigned int>(s->chars[i]),
                        static_cast<unsigned int>(expected[i]));
        }
    }
    if (ok)
    {
        Expect(true, what);
    }
}

// C99 anonymous variadic macro (NOT the GNU named form `bytes...`, which
// MSVC rejects with C2010): every non-empty case passes at least one byte.
#define CHECK(what, ...) \
    do { \
        static const unsigned char b[] = { __VA_ARGS__ }; \
        CheckDecode(what, b, sizeof(b), expected, expectedLen); \
    } while (0)

} // namespace

int main()
{
    // --- 1. Chinese BMP: "ni hao" (E4 BD A0 E5 A5 BD) ---
    {
        const char16_t expected[] = { 0x4F60, 0x597D };
        const int32_t expectedLen = 2;
        CHECK("Chinese \"ni hao\" decodes to U+4F60 U+597D", 0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD);
    }

    // --- 2. Single 3-byte character: "zhong" (E4 B8 AD) ---
    {
        const char16_t expected[] = { 0x4E2D };
        const int32_t expectedLen = 1;
        CHECK("Chinese \"zhong\" decodes to U+4E2D", 0xE4, 0xB8, 0xAD);
    }

    // --- 3. Astral 4-byte: U+1F600 (F0 9F 98 80) -> surrogate pair ---
    {
        const char16_t expected[] = { 0xD83D, 0xDE00 };
        const int32_t expectedLen = 2;
        CHECK("U+1F600 emoji decodes to a surrogate pair", 0xF0, 0x9F, 0x98, 0x80);
    }

    // --- 4. Mixed ASCII + Chinese: "AzhongB" ---
    {
        const char16_t expected[] = { 0x0041, 0x4E2D, 0x0042 };
        const int32_t expectedLen = 3;
        CHECK("mixed ASCII + Chinese decodes in order", 0x41, 0xE4, 0xB8, 0xAD, 0x42);
    }

    // --- 5. Empty input ---
    {
        // MSVC rejects the zero-length array initializer `expected[] = {}`
        // (C2466) and a `CHECK(...)` with no bytes would expand to `{ }`
        // (same error), so the empty case calls CheckDecode directly with an
        // explicit zero length; the unused slot makes the array legal C++.
        const char16_t expected[1] = { 0 };
        const int32_t expectedLen = 0;
        static const unsigned char noBytes[1] = { 0 };
        CheckDecode("empty input yields a zero-length string", noBytes, 0,
                    expected, expectedLen);
    }

    // --- 6. Lone continuation byte (0x80) -> U+FFFD ---
    {
        const char16_t expected[] = { 0xFFFD };
        const int32_t expectedLen = 1;
        CHECK("lone continuation byte becomes U+FFFD", 0x80);
    }

    // --- 7. Invalid lead bytes C0/C1/FF -> U+FFFD ---
    {
        const char16_t expected[] = { 0xFFFD };
        const int32_t expectedLen = 1;
        CHECK("lead byte 0xC1 becomes U+FFFD", 0xC1);
        CHECK("lead byte 0xFF becomes U+FFFD", 0xFF);
    }

    // --- 8. Truncated sequences at the terminator -> ONE U+FFFD (net48) ---
    {
        const char16_t expected[] = { 0xFFFD };
        const int32_t expectedLen = 1;
        CHECK("truncated 2-byte sequence becomes one U+FFFD", 0xC2);
        CHECK("truncated 3-byte sequence becomes one U+FFFD", 0xE4, 0xB8);
        CHECK("truncated 4-byte sequence becomes one U+FFFD", 0xF0, 0x9F, 0x98);
    }

    // --- 9. Non-continuation mid-sequence: E4 B8 41 -> [U+FFFD, 'A'] ---
    {
        const char16_t expected[] = { 0xFFFD, 0x0041 };
        const int32_t expectedLen = 2;
        CHECK("invalid continuation yields U+FFFD then the offending byte", 0xE4, 0xB8, 0x41);
    }

    // --- 10. Overlong 2-byte: C0 AF -> [U+FFFD, U+FFFD] (net48) ---
    {
        const char16_t expected[] = { 0xFFFD, 0xFFFD };
        const int32_t expectedLen = 2;
        CHECK("overlong 2-byte C0 AF yields two U+FFFD", 0xC0, 0xAF);
    }

    // --- 11. Overlong 3-byte: E0 80 80 -> [U+FFFD, U+FFFD] (net48: 2, not 3) ---
    {
        const char16_t expected[] = { 0xFFFD, 0xFFFD };
        const int32_t expectedLen = 2;
        CHECK("overlong 3-byte E0 80 80 yields two U+FFFD", 0xE0, 0x80, 0x80);
    }

    // --- 12. Surrogate-range 3-byte: ED A0 80 -> [U+FFFD, U+FFFD] (net48) ---
    {
        const char16_t expected[] = { 0xFFFD, 0xFFFD };
        const int32_t expectedLen = 2;
        CHECK("surrogate-range ED A0 80 yields two U+FFFD", 0xED, 0xA0, 0x80);
    }

    // --- 13. Overlong 4-byte: F0 80 80 80 -> 3x U+FFFD (net48) ---
    {
        const char16_t expected[] = { 0xFFFD, 0xFFFD, 0xFFFD };
        const int32_t expectedLen = 3;
        CHECK("overlong 4-byte F0 80 80 80 yields three U+FFFD", 0xF0, 0x80, 0x80, 0x80);
    }

    // --- 14. Beyond U+10FFFF: F4 90 80 80 -> 3x U+FFFD (net48) ---
    {
        const char16_t expected[] = { 0xFFFD, 0xFFFD, 0xFFFD };
        const int32_t expectedLen = 3;
        CHECK("F4 90 80 80 (past U+10FFFF) yields three U+FFFD", 0xF4, 0x90, 0x80, 0x80);
    }

    // --- 15. Maximum valid code point: F4 8F BF BF -> U+DBFF U+DFFF ---
    {
        const char16_t expected[] = { 0xDBFF, 0xDFFF };
        const int32_t expectedLen = 2;
        CHECK("U+10FFFF decodes to U+DBFF U+DFFF", 0xF4, 0x8F, 0xBF, 0xBF);
    }

    // --- 16. Just below the surrogate range: ED 9F BF -> U+D7FF ---
    {
        const char16_t expected[] = { 0xD7FF };
        const int32_t expectedLen = 1;
        CHECK("U+D7FF decodes to U+D7FF", 0xED, 0x9F, 0xBF);
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d utf8 string assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime utf8 string tests passed\n");
    return 0;
}
