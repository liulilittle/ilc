//------------------------------------------------------------------------------
// test_sha256.cpp -- SHA-256 (FIPS 180-4) runtime unit tests (sha256.h).
//
// Coverage (normal + edge paths):
//   1. FIPS 180-4 vectors (one-shot hash_bytes):
//        ""              -> e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
//        "abc"           -> ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
//        1,000,000 x 'a' -> cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0
//   2. Incremental consistency: byte-at-a-time / chunked streaming updates
//      produce the same digest as the one-shot call.
//   3. Block-boundary edges: 63+37 split (padding forces a second block) and
//      a 1000-byte chunked feed over 1M 'a' (many 512-bit boundaries) all
//      match the one-shot digest.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "sha256.h"
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

// Compare a 32-byte digest against an expected lowercase hex string.
// Manual hex encoding (no sprintf -- MSVC C4996-clean, same table as
// test_sha1.cpp).
void ExpectHex(const uint8_t digest[32], const char* expected, const char* what)
{
    static const char kHex[] = "0123456789abcdef";
    char hex[65];
    for (int i = 0; i < 32; ++i)
    {
        hex[2 * i] = kHex[digest[i] >> 4];
        hex[2 * i + 1] = kHex[digest[i] & 0x0F];
    }
    hex[64] = '\0';
    Expect(std::strcmp(hex, expected) == 0, what);
}

// Print a 32-byte digest as lowercase hex (informational output).
void PrintHex(const uint8_t digest[32])
{
    for (int i = 0; i < 32; ++i)
        std::printf("%02x", digest[i]);
    std::printf("\n");
}

// Stream 'text' through update() in 'chunk'-byte pieces, then finalize.
void StreamInChunks(const char* text, size_t textLen, size_t chunk, uint8_t out[32])
{
    il2cpp::runtime::Il2CppSha256Ctx ctx;
    il2cpp::runtime::il2cpp_sha256_create(&ctx);
    for (size_t i = 0; i < textLen; i += chunk)
    {
        const size_t n = (chunk < textLen - i) ? chunk : (textLen - i);
        il2cpp::runtime::il2cpp_sha256_update(&ctx, text + i, n);
    }
    il2cpp::runtime::il2cpp_sha256_final(&ctx, out);
}

} // namespace

int main()
{
    uint8_t oneShot[32];
    uint8_t streamed[32];

    // --- 1. FIPS 180-4 vectors (one-shot) ---
    {
        il2cpp::runtime::il2cpp_sha256_hash_bytes("", 0, oneShot);
        ExpectHex(oneShot, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                  "SHA-256(\"\") matches the FIPS empty-string vector");
    }
    {
        const char* abc = "abc";
        il2cpp::runtime::il2cpp_sha256_hash_bytes(abc, 3, oneShot);
        ExpectHex(oneShot, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                  "SHA-256(\"abc\") matches the FIPS vector");
    }
    {
        uint8_t* a = new uint8_t[1000000];
        std::memset(a, 'a', 1000000);
        il2cpp::runtime::il2cpp_sha256_hash_bytes(a, 1000000, oneShot);
        delete[] a;
        ExpectHex(oneShot, "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0",
                  "SHA-256(1,000,000 x 'a') matches the FIPS vector");
    }

    // --- 2. Incremental consistency: byte-at-a-time == one-shot ---
    {
        const char* abc = "abc";
        il2cpp::runtime::il2cpp_sha256_hash_bytes(abc, 3, oneShot);
        StreamInChunks(abc, 3, 1, streamed);
        Expect(std::memcmp(oneShot, streamed, 32) == 0,
               "byte-at-a-time streaming digest == one-shot digest");
    }

    // --- 2b. Chunked update (7-byte pieces, no block boundary) == one-shot ---
    {
        const char* fox = "The quick brown fox jumps over the lazy dog"; // 43 bytes
        const size_t len = std::strlen(fox);
        il2cpp::runtime::il2cpp_sha256_hash_bytes(fox, len, oneShot);
        StreamInChunks(fox, len, 7, streamed);
        Expect(std::memcmp(oneShot, streamed, 32) == 0,
               "chunked streaming digest == one-shot digest");
    }

    // --- 3. Block-boundary edge: 100-byte input split 63 + 37 ---
    // The 63-byte first chunk leaves buffered == 63, so final() must emit a
    // second padding block (0x80 + zeros + length in a fresh block).
    {
        uint8_t data[100];
        for (size_t i = 0; i < 100; ++i)
            data[i] = static_cast<uint8_t>('A' + (i % 26));
        il2cpp::runtime::il2cpp_sha256_hash_bytes(data, 100, oneShot);
        StreamInChunks(reinterpret_cast<const char*>(data), 100, 63, streamed);
        Expect(std::memcmp(oneShot, streamed, 32) == 0,
               "63+37 split (padding in a fresh block) digest == one-shot digest");
    }

    // --- 3b. 1,000,000 x 'a' in 1000-byte chunks (many 512-bit boundaries) ---
    {
        uint8_t* a = new uint8_t[1000000];
        std::memset(a, 'a', 1000000);
        il2cpp::runtime::il2cpp_sha256_hash_bytes(a, 1000000, oneShot);
        StreamInChunks(reinterpret_cast<const char*>(a), 1000000, 1000, streamed);
        delete[] a;
        Expect(std::memcmp(oneShot, streamed, 32) == 0,
               "1M 'a' in 1000-byte chunks digest == one-shot digest");
    }

    // --- 4. Managed byte[] overload (the transpiler ComputeHash target) ---
    {
        il2cpp::Il2CppArray* abc = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        std::memcpy(abc->data, "abc", 3);
        il2cpp::Il2CppArray* digest =
            il2cpp::runtime::il2cpp_sha256_hash_bytes(abc);
        Expect(digest != nullptr && digest->length == 32 && digest->elemSize == 1,
               "array overload returns a 32-byte byte array");
        ExpectHex(reinterpret_cast<const uint8_t*>(digest->data),
                  "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                  "array overload digest matches the 'abc' vector");
        il2cpp::Il2CppArray* empty = il2cpp::il2cpp_array_new(nullptr, 1, false, 0);
        il2cpp::Il2CppArray* ed =
            il2cpp::runtime::il2cpp_sha256_hash_bytes(empty);
        ExpectHex(reinterpret_cast<const uint8_t*>(ed->data),
                  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                  "array overload of an empty array matches the empty vector");
        try
        {
            il2cpp::runtime::il2cpp_sha256_hash_bytes(nullptr);
            Expect(false, "array overload of null throws ArgumentNullException");
        }
        catch (const il2cpp::DotNetException& e)
        {
            Expect(std::strstr(e.type_name(), "System.ArgumentNullException") != nullptr,
                   "array overload of null throws ArgumentNullException");
        }
        catch (...)
        {
            Expect(false, "array overload of null throws ArgumentNullException");
        }
    }

    // --- 5. Edge paths: len 0 / null data hash the empty message; update
    // with len 0 or null data changes nothing; null digest / null ctx are
    // safe no-ops; create() resets a used context. ---
    {
        uint8_t digest[32];

        il2cpp::runtime::il2cpp_sha256_hash_bytes(nullptr, 0, digest);
        ExpectHex(digest, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                  "hash_bytes(null, 0) == empty-message vector");

        il2cpp::runtime::Il2CppSha256Ctx ctx;
        il2cpp::runtime::il2cpp_sha256_create(&ctx);
        il2cpp::runtime::il2cpp_sha256_update(&ctx, "abc", 0);
        il2cpp::runtime::il2cpp_sha256_update(&ctx, nullptr, 3);
        il2cpp::runtime::il2cpp_sha256_final(&ctx, digest);
        ExpectHex(digest, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                  "zero-length/null updates leave the hash empty");

        il2cpp::runtime::il2cpp_sha256_create(nullptr);            // no-op
        il2cpp::runtime::il2cpp_sha256_update(nullptr, "abc", 3);  // no-op
        il2cpp::runtime::il2cpp_sha256_final(nullptr, nullptr);    // no-op
        il2cpp::runtime::il2cpp_sha256_hash_bytes("abc", 3, nullptr); // no-op
    }

    // --- 6. create() resets a used context: hashing "abc" twice through the
    // same context (with final in between) yields the same digest. ---
    {
        il2cpp::runtime::Il2CppSha256Ctx ctx;
        uint8_t first[32], second[32];

        il2cpp::runtime::il2cpp_sha256_create(&ctx);
        il2cpp::runtime::il2cpp_sha256_update(&ctx, "abc", 3);
        il2cpp::runtime::il2cpp_sha256_final(&ctx, first);

        il2cpp::runtime::il2cpp_sha256_create(&ctx);
        il2cpp::runtime::il2cpp_sha256_update(&ctx, "abc", 3);
        il2cpp::runtime::il2cpp_sha256_final(&ctx, second);

        Expect(std::memcmp(first, second, 32) == 0, "create() resets a used context");
        ExpectHex(first, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                  "reused context digest still matches vector abc");
    }

    // --- Report (print the three FIPS vector digests) ---
    std::printf("sha256(\"\")  = ");
    il2cpp::runtime::il2cpp_sha256_hash_bytes("", 0, oneShot);
    PrintHex(oneShot);
    std::printf("sha256(abc)  = ");
    il2cpp::runtime::il2cpp_sha256_hash_bytes("abc", 3, oneShot);
    PrintHex(oneShot);
    {
        uint8_t* a = new uint8_t[1000000];
        std::memset(a, 'a', 1000000);
        il2cpp::runtime::il2cpp_sha256_hash_bytes(a, 1000000, oneShot);
        delete[] a;
        std::printf("sha256(1e6a) = ");
        PrintHex(oneShot);
    }
    if (g_failures != 0)
    {
        std::printf("%d sha256 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime sha256 tests passed\n");
    return 0;
}
