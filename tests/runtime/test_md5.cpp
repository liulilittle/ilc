//------------------------------------------------------------------------------
// test_md5.cpp -- MD5 (RFC 1321) runtime unit tests.
//
// Coverage (normal + boundary + error paths), mirroring md5.h:
//   1. Full RFC 1321 §A.5 standard test suite (8 vectors; identical to the
//      .NET framework's MD5.ComputeHash digests):
//        MD5("")                                 = d41d8cd98f00b204e9800998ecf8427e
//        MD5("a")                                = 0cc175b9c0f1b6a831c399e269772661
//        MD5("abc")                              = 900150983cd24fb0d6963f7d28e17f72
//        MD5("message digest")                   = f96b697d7cb7938d525a2f31aaf161d0
//        MD5("abcdefghijklmnopqrstuvwxyz")       = c3fcd3d76192e4007dfb496cca67e13b
//        MD5("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
//                                                = d174ab98d277d9f5a5611c2c9f419d9f
//        MD5("1234567890" x8)                    = 57edf4a22be3c955ac49da2e2107b67a
//        MD5(1M 'a')                             = 7707d6ae4e027c70eea2a935c2296f21
//   2. One-shot byte[] form (il2cpp_md5_hash_bytes): same digests as the
//      three-phase path, correct byte[] shape (length 16, elemSize 1).
//   3. Incremental update consistency: every split of "abc" and of a 1M
//      stream into arbitrary chunks yields the same digest as one update.
//   4. Boundary chunk sizes: updates sized 1..67 bytes (exercise the
//      partial-buffer wrap, full-block transform, and pad-to-56 paths).
//   5. Context reuse: a context can be finalized and used for a second
//      message (create is re-seeded by final).
//   6. Error path: hash_bytes(null, 1) throws ArgumentNullException.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "md5.h"
#include "il2cpp_exception.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
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

// Hex helper: render 16 digest bytes as a lowercase 32-char string.
std::string HexDigest(const uint8_t digest[16])
{
    static const char kHex[] = "0123456789abcdef";
    std::string out;
    out.reserve(32);
    for (int32_t i = 0; i < 16; ++i)
    {
        out.push_back(kHex[digest[i] >> 4]);
        out.push_back(kHex[digest[i] & 0x0F]);
    }
    return out;
}

// Three-phase hash of a byte buffer (single update), hex string result.
std::string HashHex(const void* data, size_t length)
{
    il2cpp::runtime::Il2CppMd5Context ctx = il2cpp::runtime::il2cpp_md5_create();
    if (length > 0)
    {
        il2cpp::runtime::il2cpp_md5_update(&ctx, data, length);
    }
    uint8_t digest[16];
    il2cpp::runtime::il2cpp_md5_final(&ctx, digest);
    return HexDigest(digest);
}

void ExpectVector(const char* what, const void* data, size_t length,
                  const char* expectedHex)
{
    Expect(HashHex(data, length) == expectedHex, what);
}

// Incremental variant: feed the buffer in pieces of the given chunk size
// (the final partial chunk is whatever remains), then finalize.
std::string HashChunked(const void* data, size_t length, size_t chunk)
{
    il2cpp::runtime::Il2CppMd5Context ctx = il2cpp::runtime::il2cpp_md5_create();
    const uint8_t* p = static_cast<const uint8_t*>(data);
    size_t remaining = length;
    while (remaining > 0)
    {
        const size_t take = (remaining < chunk) ? remaining : chunk;
        il2cpp::runtime::il2cpp_md5_update(&ctx, p, take);
        p += take;
        remaining -= take;
    }
    uint8_t digest[16];
    il2cpp::runtime::il2cpp_md5_final(&ctx, digest);
    return HexDigest(digest);
}

bool ThrowsDotNetException(void (*fn)(), const char* typeTag)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeTag) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

} // namespace

int main()
{
    // --- 1. Full RFC 1321 A.5 standard vector suite ---
    ExpectVector("MD5 of empty string",
                 "", 0, "d41d8cd98f00b204e9800998ecf8427e");
    ExpectVector("MD5 of 'a'",
                 "a", 1, "0cc175b9c0f1b6a831c399e269772661");
    ExpectVector("MD5 of 'abc'",
                 "abc", 3, "900150983cd24fb0d6963f7d28e17f72");
    ExpectVector("MD5 of 'message digest'",
                 "message digest", 14, "f96b697d7cb7938d525a2f31aaf161d0");
    ExpectVector("MD5 of lowercase alphabet",
                 "abcdefghijklmnopqrstuvwxyz", 26,
                 "c3fcd3d76192e4007dfb496cca67e13b");
    ExpectVector("MD5 of mixed-case alphanumeric",
                 "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 62,
                 "d174ab98d277d9f5a5611c2c9f419d9f");
    ExpectVector("MD5 of 80 digits",
                 "12345678901234567890123456789012345678901234567890123456789012345678901234567890", 80,
                 "57edf4a22be3c955ac49da2e2107b67a");

    std::vector<uint8_t> millionA(1000000, 'a');
    ExpectVector("MD5 of one million 'a'",
                 millionA.data(), millionA.size(),
                 "7707d6ae4e027c70eea2a935c2296f21");

    // --- 2. One-shot byte[] form agrees with the three-phase path ---
    {
        il2cpp::Il2CppArray* arr =
            il2cpp::runtime::il2cpp_md5_hash_bytes("abc", 3);
        Expect(arr != nullptr, "hash_bytes returns a non-null array");
        Expect(arr->length == 16, "hash_bytes returns a 16-element array");
        Expect(arr->elemSize == 1, "hash_bytes array is a byte array");
        const uint8_t* bytes = reinterpret_cast<const uint8_t*>(arr->data);
        Expect(HexDigest(bytes) == "900150983cd24fb0d6963f7d28e17f72",
               "hash_bytes digest matches the three-phase digest");
    }
    {
        il2cpp::Il2CppArray* arr =
            il2cpp::runtime::il2cpp_md5_hash_bytes("", 0);
        const uint8_t* bytes = reinterpret_cast<const uint8_t*>(arr->data);
        Expect(HexDigest(bytes) == "d41d8cd98f00b204e9800998ecf8427e",
               "hash_bytes of empty input matches the RFC vector");
    }

    // --- 3. Incremental update consistency: 'abc' split at every position ---
    {
        static const char kAbc[] = "abc";
        for (size_t split = 1; split <= 2; ++split)
        {
            il2cpp::runtime::Il2CppMd5Context ctx =
                il2cpp::runtime::il2cpp_md5_create();
            il2cpp::runtime::il2cpp_md5_update(&ctx, kAbc, split);
            il2cpp::runtime::il2cpp_md5_update(&ctx, kAbc + split, 3 - split);
            uint8_t digest[16];
            il2cpp::runtime::il2cpp_md5_final(&ctx, digest);
            Expect(HexDigest(digest) == "900150983cd24fb0d6963f7d28e17f72",
                   "incremental 'abc' split matches the one-shot digest");
        }
    }

    // --- 4. Boundary chunk sizes over the 1M stream (partial-buffer,
    //        full-block transform, pad-to-56 wrap) ---
    {
        const std::string expected = "7707d6ae4e027c70eea2a935c2296f21";
        const size_t chunks[] = { 1, 2, 3, 55, 56, 57, 63, 64, 65, 67,
                                  1000, 100000, 999999, 1000000 };
        for (size_t chunk : chunks)
        {
            char what[128];
            std::snprintf(what, sizeof(what),
                          "1M stream chunked at %zu matches the vector", chunk);
            Expect(HashChunked(millionA.data(), millionA.size(), chunk) == expected,
                   what);
        }
    }

    // --- 5. Context reuse: final resets the context to a fresh state ---
    {
        il2cpp::runtime::Il2CppMd5Context ctx =
            il2cpp::runtime::il2cpp_md5_create();
        il2cpp::runtime::il2cpp_md5_update(&ctx, "abc", 3);
        uint8_t first[16];
        il2cpp::runtime::il2cpp_md5_final(&ctx, first);
        uint8_t second[16];
        il2cpp::runtime::il2cpp_md5_final(&ctx, second);
        Expect(HexDigest(second) == "d41d8cd98f00b204e9800998ecf8427e",
               "context after final behaves like a fresh context (empty digest)");
        uint8_t third[16];
        il2cpp::runtime::il2cpp_md5_update(&ctx, "abc", 3);
        il2cpp::runtime::il2cpp_md5_final(&ctx, third);
        Expect(HexDigest(third) == "900150983cd24fb0d6963f7d28e17f72",
               "reused context produces the correct 'abc' digest");
    }

    // --- 6. Error path: hash_bytes with null data and length > 0 ---
    {
        Expect(ThrowsDotNetException(
                   []() { il2cpp::runtime::il2cpp_md5_hash_bytes(nullptr, 1); },
                   "System.ArgumentNullException"),
               "hash_bytes(null, 1) throws ArgumentNullException");
        il2cpp::Il2CppArray* arr =
            il2cpp::runtime::il2cpp_md5_hash_bytes(nullptr, 0);
        Expect(arr != nullptr, "hash_bytes(null, 0) is tolerated (length 0)");
    }

    // --- 7. Managed byte[] overload (the transpiler ComputeHash target) ---
    {
        il2cpp::Il2CppArray* abc = il2cpp::il2cpp_array_new(nullptr, 1, false, 3);
        std::memcpy(abc->data, "abc", 3);
        il2cpp::Il2CppArray* digest =
            il2cpp::runtime::il2cpp_md5_hash_bytes(abc);
        Expect(digest != nullptr && digest->length == 16 && digest->elemSize == 1,
               "array overload returns a 16-byte byte array");
        Expect(HexDigest(reinterpret_cast<const uint8_t*>(digest->data))
                   == "900150983cd24fb0d6963f7d28e17f72",
               "array overload digest matches the 'abc' vector");
        il2cpp::Il2CppArray* empty = il2cpp::il2cpp_array_new(nullptr, 1, false, 0);
        il2cpp::Il2CppArray* ed =
            il2cpp::runtime::il2cpp_md5_hash_bytes(empty);
        Expect(HexDigest(reinterpret_cast<const uint8_t*>(ed->data))
                   == "d41d8cd98f00b204e9800998ecf8427e",
               "array overload of an empty array matches the RFC vector");
        Expect(ThrowsDotNetException(
                   []() { il2cpp::runtime::il2cpp_md5_hash_bytes(nullptr); },
                   "System.ArgumentNullException"),
               "array overload of null throws ArgumentNullException");
    }

    if (g_failures == 0)
    {
        std::printf("ALL PASS (md5)\n");
        return 0;
    }
    std::printf("%d FAILURES (md5)\n", g_failures);
    return 1;
}
