//------------------------------------------------------------------------------
// test_deflate.cpp -- System.IO.Compression.DeflateStream runtime unit tests
// (the raw DEFLATE primitives).
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in il2cpp_deflate.h:
//   1. il2cpp_deflate_compress: a stored-block DEFLATE stream -- the output
//      structure (BFINAL + BTYPE=00 header, LEN/NLEN, data) and the
//      size-query mode.
//   2. il2cpp_deflate_decompress of a stored block: round-trips the
//      compressed output back to the original bytes.
//   3. il2cpp_deflate_decompress of a FIXED-HUFFMAN block: a manually
//      constructed RFC 1951 fixed-Huffman stream for "hello" decodes to the
//      original bytes (the complex path -- the fixed literal/length +
//      distance tables).
//   4. Round trip: compress then decompress reproduces the original data
//      (ASCII + binary + empty).
//   5. Malformed input: a dynamic-Huffman block (BTYPE=10), a truncated
//      stream, and a bad stored-block LEN/NLEN return -1.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_deflate.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

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

// Compress 'data' and decompress it back; returns true iff the round-trip
// reproduces the original bytes.
bool RoundTrips(const uint8_t* data, int32_t len)
{
    const int32_t compLen = il2cpp::runtime::il2cpp_deflate_compress(data, len, nullptr, 0);
    if (compLen < 0)
    {
        return false;
    }
    std::vector<uint8_t> comp(static_cast<size_t>(compLen));
    if (il2cpp::runtime::il2cpp_deflate_compress(data, len, comp.data(), compLen) != compLen)
    {
        return false;
    }
    const int32_t decompLen = il2cpp::runtime::il2cpp_deflate_decompress(
        comp.data(), compLen, nullptr, 0);
    if (decompLen != len)
    {
        return false;
    }
    std::vector<uint8_t> decomp(static_cast<size_t>(decompLen));
    if (il2cpp::runtime::il2cpp_deflate_decompress(
            comp.data(), compLen, decomp.data(), decompLen) != decompLen)
    {
        return false;
    }
    return std::memcmp(data, decomp.data(), static_cast<size_t>(len)) == 0;
}

} // namespace

int main()
{
    // --- 1. Compress: stored-block structure ---
    {
        const uint8_t data[5] = { 'h', 'e', 'l', 'l', 'o' };
        const int32_t required = il2cpp::runtime::il2cpp_deflate_compress(data, 5, nullptr, 0);
        // 1 header byte + 4 (LEN/NLEN) + 5 data = 10 bytes (one block).
        Expect(required == 10, "compress size-query returns 10 for 5 bytes (one stored block)");
        uint8_t out[16];
        const int32_t written = il2cpp::runtime::il2cpp_deflate_compress(data, 5, out, 16);
        Expect(written == 10, "compress writes 10 bytes");
        // Header: BFINAL=1 (bit 0), BTYPE=00 (bits 1-2) -> 0x01.
        Expect(out[0] == 0x01, "stored-block header is 0x01 (BFINAL=1, BTYPE=00)");
        // LEN = 5 (little-endian), NLEN = 0xFFFA.
        Expect(out[1] == 0x05 && out[2] == 0x00, "stored-block LEN = 5 (little-endian)");
        Expect(out[3] == 0xFA && out[4] == 0xFF, "stored-block NLEN = ~5");
        Expect(std::memcmp(out + 5, data, 5) == 0, "stored-block data follows LEN/NLEN");
    }

    // --- 2. Decompress a stored block ---
    {
        const uint8_t data[5] = { 'h', 'e', 'l', 'l', 'o' };
        const int32_t compLen = il2cpp::runtime::il2cpp_deflate_compress(data, 5, nullptr, 0);
        std::vector<uint8_t> comp(static_cast<size_t>(compLen));
        il2cpp::runtime::il2cpp_deflate_compress(data, 5, comp.data(), compLen);
        uint8_t out[8];
        const int32_t got = il2cpp::runtime::il2cpp_deflate_decompress(
            comp.data(), compLen, out, 8);
        Expect(got == 5 && std::memcmp(out, data, 5) == 0,
               "decompress of a stored block reproduces the data");
    }

    // --- 3. Decompress a FIXED-HUFFMAN block ---
    {
        // A manually constructed RFC 1951 fixed-Huffman stream for "hello":
        //   header BFINAL=1, BTYPE=01 (fixed) -> bits 1,1,0
        //   'h' (0x68) code 0x98, 'e' (0x65) code 0x95,
        //   'l' (0x6C) code 0x9C, 'l' code 0x9C, 'o' (0x6F) code 0x9F
        //   EOB (256) code 0x00 (7 bits)
        // Packed LSB-first (see the test header comment for the derivation).
        const uint8_t fixed[7] = { 0xCB, 0x48, 0xCD, 0xC9, 0xC9, 0x07, 0x00 };
        uint8_t out[8];
        const int32_t got = il2cpp::runtime::il2cpp_deflate_decompress(fixed, 7, out, 8);
        const uint8_t expected[5] = { 'h', 'e', 'l', 'l', 'o' };
        Expect(got == 5 && std::memcmp(out, expected, 5) == 0,
               "decompress of a fixed-Huffman block reproduces \"hello\"");
    }

    // --- 4. Round trips ---
    {
        const uint8_t ascii[11] = { 'h', 'e', 'l', 'l', 'o', ' ', 'w', 'o', 'r', 'l', 'd' };
        Expect(RoundTrips(ascii, 11), "round trip: \"hello world\"");
        const uint8_t binary[8] = { 0x00, 0x01, 0x7F, 0x80, 0xFF, 0x10, 0x20, 0x30 };
        Expect(RoundTrips(binary, 8), "round trip: binary bytes");
        Expect(RoundTrips(nullptr, 0), "round trip: empty input");
        // A larger input (multiple stored blocks: 70000 bytes > 65535).
        {
            std::vector<uint8_t> big(70000);
            for (size_t i = 0; i < big.size(); ++i)
            {
                big[i] = static_cast<uint8_t>(i * 31 + 7);
            }
            Expect(RoundTrips(big.data(), 70000),
                   "round trip: 70000 bytes (multiple stored blocks)");
        }
    }

    // --- 5. Malformed input ---
    {
        // A dynamic-Huffman block (BTYPE=10) is out of scope -> -1.
        const uint8_t dyn[2] = { 0x05, 0x00 }; // BFINAL=1, BTYPE=10
        Expect(il2cpp::runtime::il2cpp_deflate_decompress(dyn, 2, nullptr, 0) == -1,
               "dynamic-Huffman block returns -1 (out of scope)");
        // A truncated stream (header only) -> -1.
        const uint8_t trunc[1] = { 0x01 };
        Expect(il2cpp::runtime::il2cpp_deflate_decompress(trunc, 1, nullptr, 0) == -1,
               "truncated stream returns -1");
        // A bad stored-block LEN/NLEN -> -1.
        const uint8_t bad[6] = { 0x01, 0x05, 0x00, 0x00, 0x00, 0x00 }; // NLEN != ~LEN
        Expect(il2cpp::runtime::il2cpp_deflate_decompress(bad, 6, nullptr, 0) == -1,
               "bad stored-block LEN/NLEN returns -1");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d deflate assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime deflate tests passed\n");
    return 0;
}
