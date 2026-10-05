//------------------------------------------------------------------------------
// test_sha1.cpp -- SHA-1 (FIPS 180-4) runtime domain tests.
//
// Published vectors (FIPS 180-4 / the standard SHA-1 test suite):
//   ""             -> da39a3ee5e6b4b0d3255bfef95601890afd80709
//   "abc"          -> a9993e364706816aba3e25717850c26c9cd0d89d
//   "abcdbcde...nopq" (56 bytes -> exactly two 512-bit blocks)
//                   -> 84983e441c3bd26ebaae4aa1f95129e5e54670f1
//
// Coverage:
//   1. One-shot hash_bytes on all three published vectors.
//   2. Incremental create/update/final EQUALS one-shot: byte-by-byte feeds
//      and arbitrary chunk splits.
//   3. Padding-branch vectors: 55/56/57 (the 56/57 boundary selects the
//      two-block padding), 63/64 (block-aligned), 65 bytes -- every split
//      point must reproduce the one-shot digest.
//   4. create() resets a used context (reuse contract).
//   5. Edge paths: len 0 / null data (empty-message digest), null digest
//      (no-op, no crash), update with len 0 changes nothing.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "sha1.h"
#include "il2cpp_exception.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <initializer_list>

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

// Compare a digest against its expected 40-hex-digit lowercase string.
void ExpectDigest(const uint8_t digest[20], const char* expectedHex, const char* what)
{
    static const char kHex[] = "0123456789abcdef";
    char actual[41];
    for (int i = 0; i < 20; ++i)
    {
        actual[2 * i] = kHex[digest[i] >> 4];
        actual[2 * i + 1] = kHex[digest[i] & 0x0F];
    }
    actual[40] = '\0';
    Expect(std::strcmp(actual, expectedHex) == 0, what);
}

// Deterministic message bytes (never all-zero, length-independent).
void FillMessage(uint8_t* out, size_t len)
{
    for (size_t i = 0; i < len; ++i)
    {
        out[i] = static_cast<uint8_t>(i * 17 + 3);
    }
}

// Incremental hash of 'data' split at every possible single point.
void ExpectAllSplitsEqual(const uint8_t* data, size_t len,
                          const uint8_t expected[20], const char* what)
{
    for (size_t split = 0; split <= len; ++split)
    {
        il2cpp::Il2CppSha1Context ctx;
        il2cpp::il2cpp_sha1_create(&ctx);
        il2cpp::il2cpp_sha1_update(&ctx, data, split);
        il2cpp::il2cpp_sha1_update(&ctx, data + split, len - split);
        uint8_t digest[20];
        il2cpp::il2cpp_sha1_final(&ctx, digest);
        Expect(std::memcmp(digest, expected, 20) == 0, what);
    }
}

} // anonymous namespace

int main()
{
    std::printf("test_sha1: SHA-1 (FIPS 180-4) domain tests\n");

    // 1. Published one-shot vectors.
    {
        uint8_t digest[20];
        il2cpp::il2cpp_sha1_hash_bytes("", 0, digest);
        ExpectDigest(digest, "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                     "vector empty message");

        il2cpp::il2cpp_sha1_hash_bytes("abc", 3, digest);
        ExpectDigest(digest, "a9993e364706816aba3e25717850c26c9cd0d89d",
                     "vector abc");

        static const char kTwoBlock[] =
            "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";
        il2cpp::il2cpp_sha1_hash_bytes(kTwoBlock, 56, digest);
        ExpectDigest(digest, "84983e441c3bd26ebaae4aa1f95129e5e54670f1",
                     "vector 56-byte two-block string");
    }

    // 2. Incremental == one-shot: byte-by-byte feeds.
    {
        static const char kAbc[] = "abc";
        il2cpp::Il2CppSha1Context ctx;
        il2cpp::il2cpp_sha1_create(&ctx);
        il2cpp::il2cpp_sha1_update(&ctx, kAbc, 1);
        il2cpp::il2cpp_sha1_update(&ctx, kAbc + 1, 1);
        il2cpp::il2cpp_sha1_update(&ctx, kAbc + 2, 1);
        uint8_t digest[20];
        il2cpp::il2cpp_sha1_final(&ctx, digest);
        ExpectDigest(digest, "a9993e364706816aba3e25717850c26c9cd0d89d",
                     "incremental abc byte-by-byte");

        static const char kTwoBlock[] =
            "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";
        il2cpp::il2cpp_sha1_create(&ctx);
        for (int i = 0; i < 56; ++i)
        {
            il2cpp::il2cpp_sha1_update(&ctx, kTwoBlock + i, 1);
        }
        il2cpp::il2cpp_sha1_final(&ctx, digest);
        ExpectDigest(digest, "84983e441c3bd26ebaae4aa1f95129e5e54670f1",
                     "incremental two-block string byte-by-byte");

        // Chunked split (7 / 23 / 26) must match too.
        il2cpp::il2cpp_sha1_create(&ctx);
        il2cpp::il2cpp_sha1_update(&ctx, kTwoBlock, 7);
        il2cpp::il2cpp_sha1_update(&ctx, kTwoBlock + 7, 23);
        il2cpp::il2cpp_sha1_update(&ctx, kTwoBlock + 30, 26);
        il2cpp::il2cpp_sha1_final(&ctx, digest);
        ExpectDigest(digest, "84983e441c3bd26ebaae4aa1f95129e5e54670f1",
                     "incremental two-block string chunked 7/23/26");
    }

    // 3. Padding branches + block alignment: every split point of 55, 56,
    //    57, 63, 64 and 65 bytes must reproduce the one-shot digest.
    {
        for (size_t len : {size_t(55), size_t(56), size_t(57),
                           size_t(63), size_t(64), size_t(65)})
        {
            uint8_t data[65];
            FillMessage(data, len);
            uint8_t expected[20];
            il2cpp::il2cpp_sha1_hash_bytes(data, len, expected);

            char what[64];
            std::snprintf(what, sizeof(what), "all splits of %zu-byte message", len);
            ExpectAllSplitsEqual(data, len, expected, what);
        }
    }

    // 4. create() resets a used context: hashing "abc" twice through the
    //    same context (with final in between) yields the same digest.
    {
        il2cpp::Il2CppSha1Context ctx;
        uint8_t first[20], second[20];

        il2cpp::il2cpp_sha1_create(&ctx);
        il2cpp::il2cpp_sha1_update(&ctx, "abc", 3);
        il2cpp::il2cpp_sha1_final(&ctx, first);

        il2cpp::il2cpp_sha1_create(&ctx);
        il2cpp::il2cpp_sha1_update(&ctx, "abc", 3);
        il2cpp::il2cpp_sha1_final(&ctx, second);

        Expect(std::memcmp(first, second, 20) == 0, "create() resets a used context");
        ExpectDigest(first, "a9993e364706816aba3e25717850c26c9cd0d89d",
                     "reused context digest still matches vector abc");
    }

    // 5. Edge paths: len 0 and null data hash the empty message; update
    //    with len 0 changes nothing; null digest is a safe no-op.
    {
        uint8_t digest[20];

        il2cpp::il2cpp_sha1_hash_bytes(nullptr, 0, digest);
        ExpectDigest(digest, "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                     "hash_bytes(null, 0) == empty-message vector");

        il2cpp::Il2CppSha1Context ctx;
        il2cpp::il2cpp_sha1_create(&ctx);
        il2cpp::il2cpp_sha1_update(&ctx, "abc", 0);
        il2cpp::il2cpp_sha1_update(&ctx, nullptr, 3);
        il2cpp::il2cpp_sha1_final(&ctx, digest);
        ExpectDigest(digest, "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                     "zero-length/null updates leave the hash empty");

        il2cpp::il2cpp_sha1_create(nullptr);            // no-op, must not crash
        il2cpp::il2cpp_sha1_update(&ctx, nullptr, 0);   // no-op
        il2cpp::il2cpp_sha1_final(&ctx, nullptr);       // no-op
        il2cpp::il2cpp_sha1_hash_bytes("abc", 3, nullptr); // no-op
    }

    // --- 6. Managed byte[] overload (the transpiler ComputeHash target) ---
    {
        il2cpp::Il2CppArray* abc = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        std::memcpy(abc->data, "abc", 3);
        il2cpp::Il2CppArray* digest = il2cpp::il2cpp_sha1_hash_bytes(abc);
        Expect(digest != nullptr && digest->length == 20 && digest->elemSize == 1,
               "array overload returns a 20-byte byte array");
        ExpectDigest(reinterpret_cast<const uint8_t*>(digest->data),
                     "a9993e364706816aba3e25717850c26c9cd0d89d",
                     "array overload digest matches the 'abc' vector");
        il2cpp::Il2CppArray* empty = il2cpp::il2cpp_array_new(nullptr, 1, false, 0);
        il2cpp::Il2CppArray* ed = il2cpp::il2cpp_sha1_hash_bytes(empty);
        ExpectDigest(reinterpret_cast<const uint8_t*>(ed->data),
                     "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                     "array overload of an empty array matches the empty vector");
        try
        {
            il2cpp::il2cpp_sha1_hash_bytes(nullptr);
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

    if (g_failures == 0)
    {
        std::printf("test_sha1: ALL PASS\n");
        return 0;
    }
    std::printf("test_sha1: %d FAILURES\n", g_failures);
    return 1;
}
