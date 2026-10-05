//------------------------------------------------------------------------------
// test_sha512.cpp -- SHA-384/SHA-512 (FIPS 180-4) runtime unit tests
// (sha512.h).
//
// Coverage (normal + edge paths):
//   1. FIPS 180-4 vectors (one-shot hash_bytes):
//        SHA-512("")      -> cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc...
//        SHA-512("abc")   -> ddaf35a193617abacc417349ae20413112e6fa4e89a97ea2...
//        SHA-512(1M 'a')  -> e718483d0ce769644e2e42c7bc15b4638e1f98b13b20442...
//        SHA-384("")      -> 38b060a751ac96384cd9327eb1b1e36a21fdb71114be074...
//        SHA-384("abc")   -> cb00753f45a35e8bb5a03d699ac65007272c32ab0eded16...
//        SHA-384(1M 'a')  -> 9d0e1809716474cb086e834e310a4a1ced149e9c00f248...
//   2. Incremental consistency: byte-at-a-time / chunked streaming updates
//      produce the same digest as the one-shot call (both algorithms).
//   3. Block-boundary edges: 63+65 split (128-byte blocks) and a 1000-byte
//      chunked feed over 1M 'a' (many 1024-bit boundaries).
//   4. Managed byte[] overloads (64/48-byte output, null -> ArgumentNull
//      Exception), same shape as the T1 sha256 overload.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "sha512.h"
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

// Compare a digest against an expected lowercase hex string.
// Manual hex encoding (no sprintf -- MSVC C4996-clean, same table as
// test_sha1.cpp).
void ExpectHex(const uint8_t* digest, size_t len, const char* expected,
               const char* what)
{
    static const char kHex[] = "0123456789abcdef";
    char hex[129];
    for (size_t i = 0; i < len; ++i)
    {
        hex[2 * i] = kHex[digest[i] >> 4];
        hex[2 * i + 1] = kHex[digest[i] & 0x0F];
    }
    hex[len * 2] = '\0';
    Expect(std::strcmp(hex, expected) == 0, what);
}

// Print a digest as lowercase hex (informational output).
void PrintHex(const uint8_t* digest, size_t len)
{
    for (size_t i = 0; i < len; ++i)
        std::printf("%02x", digest[i]);
    std::printf("\n");
}

// Stream 'text' through update() in 'chunk'-byte pieces, then finalize.
// Takes the create/final entry points explicitly so one helper serves both
// the SHA-512 and the SHA-384 exits (identical update path).
void StreamInChunks(void (*create)(il2cpp::runtime::Il2CppSha512Ctx*),
                    void (*final)(il2cpp::runtime::Il2CppSha512Ctx*, uint8_t*),
                    const char* text, size_t textLen, size_t chunk,
                    uint8_t* out)
{
    il2cpp::runtime::Il2CppSha512Ctx ctx;
    create(&ctx);
    for (size_t i = 0; i < textLen; i += chunk)
    {
        const size_t n = (chunk < textLen - i) ? chunk : (textLen - i);
        il2cpp::runtime::il2cpp_sha512_update(&ctx, text + i, n);
    }
    final(&ctx, out);
}

} // namespace

int main()
{
    uint8_t oneShot[64];
    uint8_t streamed[64];

    // --- 1. FIPS 180-4 vectors (one-shot) ---
    {
        il2cpp::runtime::il2cpp_sha512_hash_bytes("", 0, oneShot);
        ExpectHex(oneShot, 64,
                  "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
                  "SHA-512(\"\") matches the FIPS empty-string vector");
    }
    {
        const char* abc = "abc";
        il2cpp::runtime::il2cpp_sha512_hash_bytes(abc, 3, oneShot);
        ExpectHex(oneShot, 64,
                  "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
                  "SHA-512(\"abc\") matches the FIPS vector");
    }
    {
        uint8_t* a = new uint8_t[1000000];
        std::memset(a, 'a', 1000000);
        il2cpp::runtime::il2cpp_sha512_hash_bytes(a, 1000000, oneShot);
        delete[] a;
        ExpectHex(oneShot, 64,
                  "e718483d0ce769644e2e42c7bc15b4638e1f98b13b2044285632a803afa973ebde0ff244877ea60a4cb0432ce577c31beb009c5c2c49aa2e4eadb217ad8cc09b",
                  "SHA-512(1,000,000 x 'a') matches the FIPS vector");
    }
    {
        il2cpp::runtime::il2cpp_sha384_hash_bytes("", 0, oneShot);
        ExpectHex(oneShot, 48,
                  "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b",
                  "SHA-384(\"\") matches the FIPS empty-string vector");
    }
    {
        const char* abc = "abc";
        il2cpp::runtime::il2cpp_sha384_hash_bytes(abc, 3, oneShot);
        ExpectHex(oneShot, 48,
                  "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7",
                  "SHA-384(\"abc\") matches the FIPS vector");
    }
    {
        uint8_t* a = new uint8_t[1000000];
        std::memset(a, 'a', 1000000);
        il2cpp::runtime::il2cpp_sha384_hash_bytes(a, 1000000, oneShot);
        delete[] a;
        ExpectHex(oneShot, 48,
                  "9d0e1809716474cb086e834e310a4a1ced149e9c00f248527972cec5704c2a5b07b8b3dc38ecc4ebae97ddd87f3d8985",
                  "SHA-384(1,000,000 x 'a') matches the FIPS vector");
    }

    // --- 2. Incremental consistency: byte-at-a-time == one-shot ---
    {
        const char* abc = "abc";
        il2cpp::runtime::il2cpp_sha512_hash_bytes(abc, 3, oneShot);
        StreamInChunks(il2cpp::runtime::il2cpp_sha512_create,
                       il2cpp::runtime::il2cpp_sha512_final,
                       abc, 3, 1, streamed);
        Expect(std::memcmp(oneShot, streamed, 64) == 0,
               "SHA-512 byte-at-a-time streaming digest == one-shot digest");
    }
    {
        const char* abc = "abc";
        il2cpp::runtime::il2cpp_sha384_hash_bytes(abc, 3, oneShot);
        StreamInChunks(il2cpp::runtime::il2cpp_sha384_create,
                       il2cpp::runtime::il2cpp_sha384_final,
                       abc, 3, 1, streamed);
        Expect(std::memcmp(oneShot, streamed, 48) == 0,
               "SHA-384 byte-at-a-time streaming digest == one-shot digest");
    }

    // --- 2b. Chunked update (7-byte pieces, no block boundary) ---
    {
        const char* fox = "The quick brown fox jumps over the lazy dog"; // 43 bytes
        const size_t len = std::strlen(fox);
        il2cpp::runtime::il2cpp_sha512_hash_bytes(fox, len, oneShot);
        StreamInChunks(il2cpp::runtime::il2cpp_sha512_create,
                       il2cpp::runtime::il2cpp_sha512_final,
                       fox, len, 7, streamed);
        Expect(std::memcmp(oneShot, streamed, 64) == 0,
               "SHA-512 chunked streaming digest == one-shot digest");
    }

    // --- 3. Block-boundary edge: 128-byte input split 63 + 65 ---
    // The 63-byte first chunk leaves buffered == 63, so final() must emit a
    // second padding block (0x80 + zeros + length in a fresh 128-byte block).
    {
        uint8_t data[128];
        for (size_t i = 0; i < 128; ++i)
            data[i] = static_cast<uint8_t>('A' + (i % 26));
        il2cpp::runtime::il2cpp_sha512_hash_bytes(data, 128, oneShot);
        StreamInChunks(il2cpp::runtime::il2cpp_sha512_create,
                       il2cpp::runtime::il2cpp_sha512_final,
                       reinterpret_cast<const char*>(data), 128, 63, streamed);
        Expect(std::memcmp(oneShot, streamed, 64) == 0,
               "SHA-512 63+65 split (padding in a fresh block) == one-shot");
    }

    // --- 3b. 1,000,000 x 'a' in 1000-byte chunks (many 1024-bit boundaries) ---
    {
        uint8_t* a = new uint8_t[1000000];
        std::memset(a, 'a', 1000000);
        il2cpp::runtime::il2cpp_sha512_hash_bytes(a, 1000000, oneShot);
        StreamInChunks(il2cpp::runtime::il2cpp_sha512_create,
                       il2cpp::runtime::il2cpp_sha512_final,
                       reinterpret_cast<const char*>(a), 1000000, 1000, streamed);
        delete[] a;
        Expect(std::memcmp(oneShot, streamed, 64) == 0,
               "SHA-512 1M 'a' in 1000-byte chunks == one-shot digest");
    }

    // --- 4. Managed byte[] overloads (the transpiler ComputeHash target) ---
    {
        il2cpp::Il2CppArray* abc = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        std::memcpy(abc->data, "abc", 3);
        il2cpp::Il2CppArray* digest =
            il2cpp::runtime::il2cpp_sha512_hash_bytes(abc);
        Expect(digest != nullptr && digest->length == 64 && digest->elemSize == 1,
               "SHA-512 array overload returns a 64-byte byte array");
        ExpectHex(reinterpret_cast<const uint8_t*>(digest->data), 64,
                  "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
                  "SHA-512 array overload digest matches the 'abc' vector");
        il2cpp::Il2CppArray* digest384 =
            il2cpp::runtime::il2cpp_sha384_hash_bytes(abc);
        Expect(digest384 != nullptr && digest384->length == 48,
               "SHA-384 array overload returns a 48-byte byte array");
        ExpectHex(reinterpret_cast<const uint8_t*>(digest384->data), 48,
                  "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7",
                  "SHA-384 array overload digest matches the 'abc' vector");
        try
        {
            il2cpp::runtime::il2cpp_sha512_hash_bytes(nullptr);
            Expect(false, "SHA-512 array overload of null throws ArgumentNullException");
        }
        catch (const il2cpp::DotNetException& e)
        {
            Expect(std::strstr(e.type_name(), "System.ArgumentNullException") != nullptr,
                   "SHA-512 array overload of null throws ArgumentNullException");
        }
        try
        {
            il2cpp::runtime::il2cpp_sha384_hash_bytes(nullptr);
            Expect(false, "SHA-384 array overload of null throws ArgumentNullException");
        }
        catch (const il2cpp::DotNetException& e)
        {
            Expect(std::strstr(e.type_name(), "System.ArgumentNullException") != nullptr,
                   "SHA-384 array overload of null throws ArgumentNullException");
        }
    }

    // --- 5. Edge paths: len 0 / null data hash the empty message; update
    // with len 0 or null data changes nothing; null digest / null ctx are
    // safe no-ops; create() resets a used context. ---
    {
        uint8_t digest[64];

        il2cpp::runtime::il2cpp_sha512_hash_bytes(nullptr, 0, digest);
        ExpectHex(digest, 64,
                  "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
                  "SHA-512 hash_bytes(null, 0) == empty-message vector");

        il2cpp::runtime::Il2CppSha512Ctx ctx;
        il2cpp::runtime::il2cpp_sha512_create(&ctx);
        il2cpp::runtime::il2cpp_sha512_update(&ctx, "abc", 0);
        il2cpp::runtime::il2cpp_sha512_update(&ctx, nullptr, 3);
        il2cpp::runtime::il2cpp_sha512_final(&ctx, digest);
        ExpectHex(digest, 64,
                  "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
                  "SHA-512 zero-length/null updates leave the hash empty");

        il2cpp::runtime::il2cpp_sha384_hash_bytes(nullptr, 0, digest);
        ExpectHex(digest, 48,
                  "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b",
                  "SHA-384 hash_bytes(null, 0) == empty-message vector");

        il2cpp::runtime::il2cpp_sha512_create(nullptr);              // no-op
        il2cpp::runtime::il2cpp_sha384_create(nullptr);              // no-op
        il2cpp::runtime::il2cpp_sha512_update(nullptr, "abc", 3);    // no-op
        il2cpp::runtime::il2cpp_sha512_final(nullptr, nullptr);      // no-op
        il2cpp::runtime::il2cpp_sha384_final(nullptr, nullptr);      // no-op
        il2cpp::runtime::il2cpp_sha512_hash_bytes("abc", 3, nullptr); // no-op
        il2cpp::runtime::il2cpp_sha384_hash_bytes("abc", 3, nullptr); // no-op
    }

    // --- 6. create() resets a used context: hashing "abc" twice through the
    // same context (with final in between) yields the same digest. ---
    {
        il2cpp::runtime::Il2CppSha512Ctx ctx;
        uint8_t first[64], second[64];

        il2cpp::runtime::il2cpp_sha512_create(&ctx);
        il2cpp::runtime::il2cpp_sha512_update(&ctx, "abc", 3);
        il2cpp::runtime::il2cpp_sha512_final(&ctx, first);

        il2cpp::runtime::il2cpp_sha512_create(&ctx);
        il2cpp::runtime::il2cpp_sha512_update(&ctx, "abc", 3);
        il2cpp::runtime::il2cpp_sha512_final(&ctx, second);

        Expect(std::memcmp(first, second, 64) == 0, "create() resets a used context");
        ExpectHex(first, 64,
                  "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
                  "reused context digest still matches vector abc");
    }

    // --- Report (print the FIPS vector digests) ---
    std::printf("sha512(\"\")  = ");
    il2cpp::runtime::il2cpp_sha512_hash_bytes("", 0, oneShot);
    PrintHex(oneShot, 64);
    std::printf("sha512(abc)  = ");
    il2cpp::runtime::il2cpp_sha512_hash_bytes("abc", 3, oneShot);
    PrintHex(oneShot, 64);
    std::printf("sha384(\"\")  = ");
    il2cpp::runtime::il2cpp_sha384_hash_bytes("", 0, oneShot);
    PrintHex(oneShot, 48);
    std::printf("sha384(abc)  = ");
    il2cpp::runtime::il2cpp_sha384_hash_bytes("abc", 3, oneShot);
    PrintHex(oneShot, 48);
    if (g_failures != 0)
    {
        std::printf("%d sha512 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime sha512 tests passed\n");
    return 0;
}
