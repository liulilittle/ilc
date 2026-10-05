//------------------------------------------------------------------------------
// test_guid.cpp -- System.Guid runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in guid.h:
//   1. il2cpp_guid_to_string: exact lowercase "D" formatting for known byte
//      patterns (zero, all-FF, a Data1/2/3-ordered pattern), 36 chars with
//      dashes at 8/13/18/23.
//   2. il2cpp_guid_new_guid: two fresh GUIDs, each formatted correctly and
//      round-tripping through parse; neither is the all-zero Guid; each is
//      RFC 4122 v4 (version nibble byte[7] == 4 -- Data3's MSB, the first
//      hex char of the third "D"-form group -- and variant byte[8] top two
//      bits == 10 -- guaranteed because new_guid's bytes come from the OS
//      CSPRNG with the v4 bits set explicitly). A 1000-GUID sample has no
//      duplicates.
//   3. il2cpp_guid_parse round-trip: parse(to_string(bytes)) == bytes for
//      the known patterns and for new_guid output; uppercase input parses
//      and re-formats to lowercase (net48 ToString behavior).
//   4. il2cpp_guid_parse errors: null, empty, wrong length (35/37 for "D",
//      31/33 for "N"), missing dashes, wrong dash position, non-hex digits,
//      braces and whitespace rejected (strict contract); output untouched on
//      failure.
//   5. il2cpp_guid_equals: identical bytes, one-byte difference, all-FF,
//      fresh GUIDs are pairwise distinct.
//   6. il2cpp_guid_compare: net48 field-wise contract -- Data1/Data2/Data3
//      compared UNSIGNED (a high-bit field sorts AFTER a low-bit field),
//      Data4 bytes unsigned; equality returns 0; antisymmetry
//      compare(a,b) == -compare(b,a).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "guid.h"
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

// Build a managed string from an ASCII literal (test input for parse).
il2cpp::Il2CppString* MakeString(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_utf8(ascii);
}

// to_string result must be exactly 'expected' (36 lowercase chars + NUL).
bool ExpectGuidText(const uint8_t bytes[16], const char* expected)
{
    il2cpp::Il2CppString* s = il2cpp::il2cpp_guid_to_string(bytes);
    const size_t len = std::strlen(expected);
    if (s == nullptr || s->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(expected[i]))
        {
            return false;
        }
    }
    return true;
}

// parse(from) == to (16 bytes) round-trip helper.
bool RoundTrips(const char* from, const uint8_t to[16])
{
    uint8_t got[16];
    if (!il2cpp::il2cpp_guid_parse(MakeString(from), got))
    {
        return false;
    }
    return std::memcmp(got, to, 16) == 0;
}

const uint8_t kZero[16] = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

// Guid(0x00112233, 0x4455, 0x6677, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff)
// in .NET byte order (Data1/2/3 little-endian, Data4 in order).
const uint8_t kKnown[16] = {
    0x33, 0x22, 0x11, 0x00,
    0x55, 0x44,
    0x77, 0x66,
    0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
};

const uint8_t kAllFF[16] = {
    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
};

} // namespace

int main()
{
    // --- 1. to_string formatting (exact lowercase "D" text) ---
    Expect(ExpectGuidText(kZero, "00000000-0000-0000-0000-000000000000"),
           "zero Guid formats as all-zero lowercase");
    Expect(ExpectGuidText(kKnown, "00112233-4455-6677-8899-aabbccddeeff"),
           "known byte pattern formats in .NET Data1/2/3 byte order, lowercase");
    Expect(ExpectGuidText(kAllFF, "ffffffff-ffff-ffff-ffff-ffffffffffff"),
           "all-FF Guid formats lowercase");

    // --- 2. new_guid: format + round-trip + distinctness ---
    uint8_t g1[16];
    uint8_t g2[16];
    il2cpp::il2cpp_guid_new_guid(g1);
    il2cpp::il2cpp_guid_new_guid(g2);
    Expect(ExpectGuidText(g1, "00000000-0000-0000-0000-000000000000") == false,
           "new_guid output is not the zero Guid");
    il2cpp::Il2CppString* g1Text = il2cpp::il2cpp_guid_to_string(g1);
    Expect(g1Text != nullptr && g1Text->length == 36,
           "new_guid text has the 36-char D length");
    {
        uint8_t reparsed[16];
        Expect(il2cpp::il2cpp_guid_parse(g1Text, reparsed)
               && std::memcmp(reparsed, g1, 16) == 0,
               "new_guid output round-trips through parse");
    }
    Expect(!il2cpp::il2cpp_guid_equals(g1, g2),
           "two consecutive new_guid values differ");
    Expect(il2cpp::il2cpp_guid_equals(g1, g1),
           "new_guid equals itself");

    // RFC 4122 v4 structure: version nibble (byte[7]) == 4, variant top two
    // bits (byte[8]) == 10.
    Expect(((g1[7] >> 4) & 0x0F) == 0x4,
           "new_guid version nibble is 4 (RFC 4122 v4)");
    Expect((g1[8] & 0xC0) == 0x80,
           "new_guid variant bits are 10xx xxxx");
    Expect(((g2[7] >> 4) & 0x0F) == 0x4,
           "second new_guid version nibble is 4");
    Expect((g2[8] & 0xC0) == 0x80,
           "second new_guid variant bits are 10xx xxxx");
    // The version nibble lands in the first hex char of the third "D" group
    // (string index 14) -- the RFC 4122 layout position, not byte[6] (which
    // is Data3's LSB and must NOT carry the version nibble).
    Expect(g1Text != nullptr && g1Text->length == 36
           && g1Text->chars[14] == u'4',
           "new_guid D-string has '4' as the first char of group 3");

    // --- 2b. 1000 fresh GUIDs are pairwise distinct (no-collision sample) ---
    {
        uint8_t sample[1000][16];
        for (int32_t i = 0; i < 1000; ++i)
        {
            il2cpp::il2cpp_guid_new_guid(sample[i]);
        }
        bool dup = false;
        for (int32_t i = 0; i < 1000 && !dup; ++i)
        {
            for (int32_t j = i + 1; j < 1000; ++j)
            {
                if (std::memcmp(sample[i], sample[j], 16) == 0)
                {
                    dup = true;
                    break;
                }
            }
        }
        Expect(!dup, "1000 fresh GUIDs have no duplicates");
    }

    // --- 3. parse round-trip (known patterns + case-insensitivity) ---
    Expect(RoundTrips("00000000-0000-0000-0000-000000000000", kZero),
           "parse of zero Guid string round-trips");
    Expect(RoundTrips("00112233-4455-6677-8899-aabbccddeeff", kKnown),
           "parse of lowercase known string round-trips");
    Expect(RoundTrips("00112233-4455-6677-8899-AABBCCDDEEFF", kKnown),
           "uppercase hex parses (case-insensitive)");
    Expect(RoundTrips("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", kAllFF),
           "parse of uppercase all-FF string round-trips");
    Expect(ExpectGuidText(kKnown, "00112233-4455-6677-8899-aabbccddeeff"),
           "uppercase parse re-formats to lowercase (net48 ToString)");

    // --- 3b. "N" form (32 hex digits, no dashes -- net48 accepts it) ---
    Expect(RoundTrips("00000000000000000000000000000000", kZero),
           "N-form zero Guid string round-trips");
    Expect(RoundTrips("00112233445566778899aabbccddeeff", kKnown),
           "N-form known string round-trips (same byte order as D form)");
    Expect(RoundTrips("00112233445566778899AABBCCDDEEFF", kKnown),
           "N-form uppercase hex parses (case-insensitive)");
    Expect(ExpectGuidText(kKnown, "00112233-4455-6677-8899-aabbccddeeff"),
           "N-form parse re-formats to the D-form lowercase string");

    // --- 4. parse errors (strict D/N contract) ---
    {
        uint8_t out[16];
        Expect(!il2cpp::il2cpp_guid_parse(nullptr, out),
               "null string rejected");
        Expect(!il2cpp::il2cpp_guid_parse(MakeString(""), out),
               "empty string rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("00000000-0000-0000-0000-00000000000"), out),
               "35-char string rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("00000000-0000-0000-0000-0000000000000"), out),
               "37-char string rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("0000000000000000000000000000000"), out),
               "31-char N string rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("000000000000000000000000000000000"), out),
               "33-char N string rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("00000000-0000-00000-000-000000000000"), out),
               "wrong dash position rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("00000000 0000 0000 0000 000000000000"), out),
               "spaces instead of dashes rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("g0000000-0000-0000-0000-000000000000"), out),
               "non-hex digit in D-form data rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("00000000-0000-0000-0000-00000000000g"), out),
               "non-hex digit at end rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("0000000000000000000000000000000g"), out),
               "non-hex digit in N form rejected");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString("{00000000-0000-0000-0000-000000000000}"), out),
               "braced form rejected (strict D/N contract)");
        Expect(!il2cpp::il2cpp_guid_parse(
                   MakeString(" 0000000-0000-0000-0000-000000000000"), out),
               "leading whitespace rejected");
        const uint8_t sentinel[16] = { 1, 2, 3, 4, 5, 6, 7, 8,
                                       9, 10, 11, 12, 13, 14, 15, 16 };
        uint8_t out2[16];
        std::memcpy(out2, sentinel, 16);
        il2cpp::il2cpp_guid_parse(MakeString("00000000-0000-0000-0000-00000000000"), out2);
        Expect(std::memcmp(out2, sentinel, 16) == 0,
               "failed parse leaves the output buffer untouched");
    }

    // --- 5. equals ---
    Expect(il2cpp::il2cpp_guid_equals(kKnown, kKnown),
           "identical bytes are equal");
    {
        uint8_t flipped[16];
        std::memcpy(flipped, kKnown, 16);
        flipped[15] ^= 0x01;
        Expect(!il2cpp::il2cpp_guid_equals(kKnown, flipped),
               "single-byte difference is not equal");
        flipped[15] ^= 0x01;
        flipped[0] ^= 0x80;
        Expect(!il2cpp::il2cpp_guid_equals(kKnown, flipped),
               "high-bit difference in Data1 is not equal");
    }
    Expect(!il2cpp::il2cpp_guid_equals(kZero, kAllFF),
           "zero vs all-FF are not equal");

    // --- 6. compare (net48 field-wise contract) ---
    {
        uint8_t a[16] = { 0 };
        uint8_t b[16] = { 0 };
        Expect(il2cpp::il2cpp_guid_compare(a, b) == 0,
               "identical GUIDs compare equal");
        b[15] = 0x01;
        Expect(il2cpp::il2cpp_guid_compare(a, b) == -1,
               "differing last Data4 byte: lower byte is smaller");
        Expect(il2cpp::il2cpp_guid_compare(b, a) == 1,
               "antisymmetry: higher byte is larger");
    }
    {
        // Data1 UNSIGNED (net48): 0x80000000 (2147483648) > 0x00000001.
        uint8_t neg[16] = { 0x00, 0x00, 0x00, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        uint8_t small[16] = { 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Expect(il2cpp::il2cpp_guid_compare(neg, small) == 1,
               "Data1 compared unsigned (0x80000000 sorts after 0x00000001, net48)");
        Expect(il2cpp::il2cpp_guid_compare(small, neg) == -1,
               "unsigned comparison is antisymmetric");
        // 0x7fffffff < 0x80000000 (both positive as unsigned).
        uint8_t mid[16] = { 0xFF, 0xFF, 0xFF, 0x7F, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Expect(il2cpp::il2cpp_guid_compare(mid, neg) == -1,
               "Data1 0x7fffffff sorts before 0x80000000 (unsigned, net48)");
    }
    {
        // Data2 UNSIGNED: 0x8000 (32768) > 0x0001.
        uint8_t neg2[16] = { 0, 0, 0, 0, 0x00, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        uint8_t small2[16] = { 0, 0, 0, 0, 0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Expect(il2cpp::il2cpp_guid_compare(neg2, small2) == 1,
               "Data2 compared unsigned (0x8000 sorts after 0x0001, net48)");
    }
    {
        // Data3 UNSIGNED: 0x8000 (32768) > 0x0001.
        uint8_t neg3[16] = { 0, 0, 0, 0, 0, 0, 0x00, 0x80, 0, 0, 0, 0, 0, 0, 0, 0 };
        uint8_t small3[16] = { 0, 0, 0, 0, 0, 0, 0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0 };
        Expect(il2cpp::il2cpp_guid_compare(neg3, small3) == 1,
               "Data3 compared unsigned (0x8000 sorts after 0x0001, net48)");
    }
    {
        // High-bit Data1 dominates a maxed Data2/Data3/Data4 (unsigned).
        uint8_t big1[16] = { 0x00, 0x00, 0x00, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        uint8_t big2[16] = { 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(il2cpp::il2cpp_guid_compare(big1, big2) == 1,
               "Data1 field decides before Data2/3/4 (0x80000000 > 0x00000001 unsigned)");
        Expect(il2cpp::il2cpp_guid_compare(kZero, kAllFF) == -1,
               "all-FF is the largest Guid (all fields unsigned)");
    }
    {
        // Data4 unsigned byte ordering: first differing Data4 byte decides.
        uint8_t low[16] = { 0, 0, 0, 0, 0, 0, 0, 0, 0x01, 0, 0, 0, 0, 0, 0, 0 };
        uint8_t high[16] = { 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0, 0, 0, 0, 0, 0, 0 };
        Expect(il2cpp::il2cpp_guid_compare(low, high) == -1,
               "Data4 byte comparison is unsigned and first-difference-based");
    }
    {
        // Data1 decides before Data2: differing Data1 + differing Data2.
        uint8_t big1[16] = { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        uint8_t big2[16] = { 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Expect(il2cpp::il2cpp_guid_compare(big1, big2) == 1,
               "Data1 field decides before Data2");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d guid assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime guid tests passed\n");
    return 0;
}
