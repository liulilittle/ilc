//------------------------------------------------------------------------------
// test_hmac.cpp -- HMAC (RFC 2104 / FIPS 198-1) runtime unit tests (hmac.h).
//
// Coverage (normal + edge paths), per the three .NET KeyedHashAlgorithm
// algorithms the runtime backs:
//   1. RFC 4231 HMAC-SHA-256, cases 1-7 (one-shot):
//        key 0x0b x20 / "Hi There" -> b0344c61d8db38535ca8afceaf0bf12b...
//        key "Jefe"               -> 5bdcc146bf60754e6a042426089575c7...
//        key 0xaa x20 / 0xdd x50  -> 773ea91e36800e46854db8ebd09181a7...
//        key 0x01..0x19 / 0xcd x50-> 82558a389a443c0ea4cc819899f2083a...
//        key 0x0c x20 / "Test With Truncation" -> a3b6167473100ee06e0c...
//        key 0xaa x131 (K'=H(K))  -> 60e431591ee0b67f0d8a26aacbf5b77f...
//        key 0xaa x131, long data -> 9b09ffa71b942fcb27635fbcd5b0e944...
//   2. RFC 2202 HMAC-SHA-1, cases 1-7 (key 0xaa x80 exercises the
//      K' = H(K) over-long-key compression on cases 6-7).
//   3. RFC 2202 HMAC-MD5, cases 1-7 (same over-long-key path, 16-byte MAC).
//   4. Edge keys pinned against the net48 oracles (RFC 2104 key handling:
//      empty key = zero-padded block; keyLen == blockSize used as-is;
//      keyLen > blockSize compresses via H(K)). Empty key, 1-byte key,
//      block-size key, block-size+1 key and digest-size key, all three
//      algorithms.
//   5. Empty-message MAC for every algorithm.
//   6. Streaming (create/update/final) == one-shot equivalence for every
//      algorithm, byte-at-a-time and chunked.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "hmac.h"
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

// Compare a 'digestSize'-byte MAC against an expected lowercase hex string.
void ExpectHex(const uint8_t* mac, size_t digestSize,
               const char* expected, const char* what)
{
    char hex[129];
    for (size_t i = 0; i < digestSize; ++i)
        std::snprintf(hex + i * 2, sizeof(hex) - i * 2, "%02x", mac[i]);
    hex[digestSize * 2] = '\0';
    Expect(std::strcmp(hex, expected) == 0, what);
}

// One-shot HMAC under the given hash descriptor.
void HmacOneShot(const il2cpp::runtime::Il2CppHmacHash& hash,
                 const void* key, size_t keyLen,
                 const void* data, size_t dataLen,
                 size_t digestSize, uint8_t* out)
{
    (void)digestSize; // the output buffer's caller-declared size; unused here
    il2cpp::runtime::Il2CppHmacCtx ctx;
    il2cpp::runtime::il2cpp_hmac_create(&ctx, &hash, key, keyLen);
    il2cpp::runtime::il2cpp_hmac_update(&ctx, data, dataLen);
    il2cpp::runtime::il2cpp_hmac_final(&ctx, out);
}

// Stream 'data' through hmac update() in 'chunk'-byte pieces.
void HmacStream(const il2cpp::runtime::Il2CppHmacHash& hash,
                const void* key, size_t keyLen,
                const void* data, size_t dataLen, size_t chunk,
                size_t digestSize, uint8_t* out)
{
    (void)digestSize; // the output buffer's caller-declared size; unused here
    il2cpp::runtime::Il2CppHmacCtx ctx;
    il2cpp::runtime::il2cpp_hmac_create(&ctx, &hash, key, keyLen);
    const uint8_t* p = static_cast<const uint8_t*>(data);
    for (size_t i = 0; i < dataLen; i += chunk)
    {
        const size_t n = (chunk < dataLen - i) ? chunk : (dataLen - i);
        il2cpp::runtime::il2cpp_hmac_update(&ctx, p + i, n);
    }
    il2cpp::runtime::il2cpp_hmac_final(&ctx, out);
}

void PrintHex(const uint8_t* mac, size_t len)
{
    for (size_t i = 0; i < len; ++i)
        std::printf("%02x", mac[i]);
    std::printf("\n");
}

} // namespace

int main()
{
    uint8_t mac[32];
    uint8_t streamed[32];

    // --- 1. RFC 4231 vectors (HMAC-SHA-256), cases 1-7 ---
    {
        uint8_t key[20];
        std::memset(key, 0x0b, 20);
        const char* msg = "Hi There";
        il2cpp::runtime::il2cpp_hmac_sha256(key, 20, msg, 8, mac);
        ExpectHex(mac, 32,
                  "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7",
                  "RFC 4231 case 1 (HMAC-SHA-256) matches");
    }
    {
        const char* key = "Jefe";
        const char* msg = "what do ya want for nothing?";
        il2cpp::runtime::il2cpp_hmac_sha256(key, 4, msg, 28, mac);
        ExpectHex(mac, 32,
                  "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
                  "RFC 4231 case 2 (HMAC-SHA-256) matches");
    }
    {
        uint8_t key[20];
        uint8_t msg[50];
        std::memset(key, 0xaa, 20);
        std::memset(msg, 0xdd, 50);
        il2cpp::runtime::il2cpp_hmac_sha256(key, 20, msg, 50, mac);
        ExpectHex(mac, 32,
                  "773ea91e36800e46854db8ebd09181a72959098b3ef8c122d9635514ced565fe",
                  "RFC 4231 case 3 (HMAC-SHA-256) matches");
    }
    {
        uint8_t key[25];
        uint8_t msg[50];
        for (int i = 0; i < 25; ++i)
            key[i] = static_cast<uint8_t>(i + 1);
        std::memset(msg, 0xcd, 50);
        il2cpp::runtime::il2cpp_hmac_sha256(key, 25, msg, 50, mac);
        ExpectHex(mac, 32,
                  "82558a389a443c0ea4cc819899f2083a85f0faa3e578f8077a2e3ff46729665b",
                  "RFC 4231 case 4 (HMAC-SHA-256) matches");
    }
    {
        uint8_t key[20];
        std::memset(key, 0x0c, 20);
        const char* msg = "Test With Truncation";
        il2cpp::runtime::il2cpp_hmac_sha256(key, 20, msg, 20, mac);
        ExpectHex(mac, 32,
                  "a3b6167473100ee06e0c796c2955552bfa6f7c0a6a8aef8b93f860aab0cd20c5",
                  "RFC 4231 case 5 (HMAC-SHA-256) matches");
    }
    {
        uint8_t key[131];
        std::memset(key, 0xaa, 131);
        const char* msg = "Test Using Larger Than Block-Size Key - Hash Key First";
        il2cpp::runtime::il2cpp_hmac_sha256(key, 131, msg, 54, mac);
        ExpectHex(mac, 32,
                  "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54",
                  "RFC 4231 case 6 (HMAC-SHA-256) matches");
    }
    {
        uint8_t key[131];
        std::memset(key, 0xaa, 131);
        const char* msg =
            "This is a test using a larger than block-size key and a larger "
            "than block-size data. The key needs to be hashed before being "
            "used by the HMAC algorithm.";
        il2cpp::runtime::il2cpp_hmac_sha256(key, 131, msg, 152, mac);
        ExpectHex(mac, 32,
                  "9b09ffa71b942fcb27635fbcd5b0e944bfdc63644f0713938a7f51535c3a35e2",
                  "RFC 4231 case 7 (HMAC-SHA-256) matches");
    }

    // --- 2. RFC 2202 vectors (HMAC-SHA-1), cases 1-7 ---
    {
        uint8_t key[20];
        std::memset(key, 0x0b, 20);
        const char* msg = "Hi There";
        il2cpp::runtime::il2cpp_hmac_sha1(key, 20, msg, 8, mac);
        ExpectHex(mac, 20,
                  "b617318655057264e28bc0b6fb378c8ef146be00",
                  "RFC 2202 case 1 (HMAC-SHA-1) matches");
    }
    {
        const char* key = "Jefe";
        const char* msg = "what do ya want for nothing?";
        il2cpp::runtime::il2cpp_hmac_sha1(key, 4, msg, 28, mac);
        ExpectHex(mac, 20,
                  "effcdf6ae5eb2fa2d27416d5f184df9c259a7c79",
                  "RFC 2202 case 2 (HMAC-SHA-1) matches");
    }
    {
        uint8_t key[20];
        uint8_t msg[50];
        std::memset(key, 0xaa, 20);
        std::memset(msg, 0xdd, 50);
        il2cpp::runtime::il2cpp_hmac_sha1(key, 20, msg, 50, mac);
        ExpectHex(mac, 20,
                  "125d7342b9ac11cd91a39af48aa17b4f63f175d3",
                  "RFC 2202 case 3 (HMAC-SHA-1) matches");
    }
    {
        uint8_t key[25];
        uint8_t msg[50];
        for (int i = 0; i < 25; ++i)
            key[i] = static_cast<uint8_t>(i + 1);
        std::memset(msg, 0xcd, 50);
        il2cpp::runtime::il2cpp_hmac_sha1(key, 25, msg, 50, mac);
        ExpectHex(mac, 20,
                  "4c9007f4026250c6bc8414f9bf50c86c2d7235da",
                  "RFC 2202 case 4 (HMAC-SHA-1) matches");
    }
    {
        uint8_t key[20];
        std::memset(key, 0x0c, 20);
        const char* msg = "Test With Truncation";
        il2cpp::runtime::il2cpp_hmac_sha1(key, 20, msg, 20, mac);
        ExpectHex(mac, 20,
                  "4c1a03424b55e07fe7f27be1d58bb9324a9a5a04",
                  "RFC 2202 case 5 (HMAC-SHA-1) matches");
    }
    {
        uint8_t key[80];
        std::memset(key, 0xaa, 80);
        const char* msg = "Test Using Larger Than Block-Size Key - Hash Key First";
        il2cpp::runtime::il2cpp_hmac_sha1(key, 80, msg, 54, mac);
        ExpectHex(mac, 20,
                  "aa4ae5e15272d00e95705637ce8a3b55ed402112",
                  "RFC 2202 case 6 (HMAC-SHA-1) matches");
    }
    {
        uint8_t key[80];
        std::memset(key, 0xaa, 80);
        const char* msg =
            "Test Using Larger Than Block-Size Key and Larger Than One "
            "Block-Size Data";
        il2cpp::runtime::il2cpp_hmac_sha1(key, 80, msg, 73, mac);
        ExpectHex(mac, 20,
                  "e8e99d0f45237d786d6bbaa7965c7808bbff1a91",
                  "RFC 2202 case 7 (HMAC-SHA-1) matches");
    }

    // --- 3. RFC 2202 vectors (HMAC-MD5), cases 1-7 ---
    {
        uint8_t key[16];
        std::memset(key, 0x0b, 16);
        const char* msg = "Hi There";
        il2cpp::runtime::il2cpp_hmac_md5(key, 16, msg, 8, mac);
        ExpectHex(mac, 16,
                  "9294727a3638bb1c13f48ef8158bfc9d",
                  "RFC 2202 case 1 (HMAC-MD5) matches");
    }
    {
        const char* key = "Jefe";
        const char* msg = "what do ya want for nothing?";
        il2cpp::runtime::il2cpp_hmac_md5(key, 4, msg, 28, mac);
        ExpectHex(mac, 16,
                  "750c783e6ab0b503eaa86e310a5db738",
                  "RFC 2202 case 2 (HMAC-MD5) matches");
    }
    {
        uint8_t key[16];
        uint8_t msg[50];
        std::memset(key, 0xaa, 16);
        std::memset(msg, 0xdd, 50);
        il2cpp::runtime::il2cpp_hmac_md5(key, 16, msg, 50, mac);
        ExpectHex(mac, 16,
                  "56be34521d144c88dbb8c733f0e8b3f6",
                  "RFC 2202 case 3 (HMAC-MD5) matches");
    }
    {
        uint8_t key[25];
        uint8_t msg[50];
        for (int i = 0; i < 25; ++i)
            key[i] = static_cast<uint8_t>(i + 1);
        std::memset(msg, 0xcd, 50);
        il2cpp::runtime::il2cpp_hmac_md5(key, 25, msg, 50, mac);
        ExpectHex(mac, 16,
                  "697eaf0aca3a3aea3a75164746ffaa79",
                  "RFC 2202 case 4 (HMAC-MD5) matches");
    }
    {
        uint8_t key[16];
        std::memset(key, 0x0c, 16);
        const char* msg = "Test With Truncation";
        il2cpp::runtime::il2cpp_hmac_md5(key, 16, msg, 20, mac);
        ExpectHex(mac, 16,
                  "56461ef2342edc00f9bab995690efd4c",
                  "RFC 2202 case 5 (HMAC-MD5) matches");
    }
    {
        uint8_t key[80];
        std::memset(key, 0xaa, 80);
        const char* msg = "Test Using Larger Than Block-Size Key - Hash Key First";
        il2cpp::runtime::il2cpp_hmac_md5(key, 80, msg, 54, mac);
        ExpectHex(mac, 16,
                  "6b1ab7fe4bd7bf8f0b62e6ce61b9d0cd",
                  "RFC 2202 case 6 (HMAC-MD5) matches");
    }
    {
        uint8_t key[80];
        std::memset(key, 0xaa, 80);
        const char* msg =
            "Test Using Larger Than Block-Size Key and Larger Than One "
            "Block-Size Data";
        il2cpp::runtime::il2cpp_hmac_md5(key, 80, msg, 73, mac);
        ExpectHex(mac, 16,
                  "6f630fad67cda0ee1fb1f562db3aa53e",
                  "RFC 2202 case 7 (HMAC-MD5) matches");
    }

    // --- 4. Edge keys (pinned against net48 oracles; RFC 2104 handling) ---
    // Empty key: RFC 2104 pads it to a full zero block (B = 64). All three
    // algorithms.
    {
        const char* msg = "Hi There";
        il2cpp::runtime::il2cpp_hmac_sha256(nullptr, 0, msg, 8, mac);
        ExpectHex(mac, 32,
                  "e48411262715c8370cd5e7bf8e82bef53bd53712d007f3429351843b77c7bb9b",
                  "empty key HMAC-SHA-256 matches the net48 oracle");
        il2cpp::runtime::il2cpp_hmac_sha1(nullptr, 0, msg, 8, mac);
        ExpectHex(mac, 20,
                  "69536cc84eee5fe51c5b051aff8485f5c9ef0b58",
                  "empty key HMAC-SHA-1 matches the net48 oracle");
        il2cpp::runtime::il2cpp_hmac_md5(nullptr, 0, msg, 8, mac);
        ExpectHex(mac, 16,
                  "72c33c78cac0b7a581ac263a344ed01d",
                  "empty key HMAC-MD5 matches the net48 oracle");
    }
    // 1-byte key.
    {
        const char* msg = "abc";
        const uint8_t one = 0x00;
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha256, &one, 1, msg, 3, 32, mac);
        ExpectHex(mac, 32,
                  "fd7adb152c05ef80dccf50a1fa4c05d5a3ec6da95575fc312ae7c5d091836351",
                  "1-byte key HMAC-SHA-256 matches the net48 oracle");
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha1, &one, 1, msg, 3, 20, mac);
        ExpectHex(mac, 20,
                  "9b4a918f398d74d3e367970aba3cbe54e4d2b5d9",
                  "1-byte key HMAC-SHA-1 matches the net48 oracle");
        HmacOneShot(il2cpp::runtime::kIl2CppHmacMd5, &one, 1, msg, 3, 16, mac);
        ExpectHex(mac, 16,
                  "dd2701993d29fdd0b032c233cec63403",
                  "1-byte key HMAC-MD5 matches the net48 oracle");
    }
    // Key exactly blockSize bytes (64): used as-is, no H(K) compression.
    {
        uint8_t key[64];
        for (int i = 0; i < 64; ++i)
            key[i] = static_cast<uint8_t>(i);
        const char* msg = "abc";
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha256, key, 64, msg, 3, 32, mac);
        ExpectHex(mac, 32,
                  "6ab541b4869dca71c4ca11d8bb1b02533b789a557583161429292c7404bc21f6",
                  "64-byte key HMAC-SHA-256 matches the net48 oracle");
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha1, key, 64, msg, 3, 20, mac);
        ExpectHex(mac, 20,
                  "89e392852da6b647490d3f287218824a2e2101b0",
                  "64-byte key HMAC-SHA-1 matches the net48 oracle");
        HmacOneShot(il2cpp::runtime::kIl2CppHmacMd5, key, 64, msg, 3, 16, mac);
        ExpectHex(mac, 16,
                  "a0d72bdfa6e9cd3a56e660eca892bfb0",
                  "64-byte key HMAC-MD5 matches the net48 oracle");
    }
    // Key longer than the block size (65 bytes): K' = H(K) compression.
    {
        uint8_t key[65];
        for (int i = 0; i < 65; ++i)
            key[i] = static_cast<uint8_t>(i);
        const char* msg = "abc";
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha256, key, 65, msg, 3, 32, mac);
        ExpectHex(mac, 32,
                  "dfbffee4671bad00ed5d1e1999d55ed3b0cc774ac357f9ebf649c1612414fcec",
                  "65-byte key HMAC-SHA-256 matches the net48 oracle");
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha1, key, 65, msg, 3, 20, mac);
        ExpectHex(mac, 20,
                  "7636c08e7b7c0f0c391ca01d34ef4208399fbcf8",
                  "65-byte key HMAC-SHA-1 matches the net48 oracle");
        HmacOneShot(il2cpp::runtime::kIl2CppHmacMd5, key, 65, msg, 3, 16, mac);
        ExpectHex(mac, 16,
                  "5b85979048f0effd21a05556dfa2faac",
                  "65-byte key HMAC-MD5 matches the net48 oracle");
    }
    // Key of exactly the digest size (16/20/32 bytes): used as-is.
    {
        const char* msg = "abc";
        uint8_t k32[32];
        std::memset(k32, 0x11, sizeof(k32));
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha256, k32, sizeof(k32), msg, 3, 32, mac);
        ExpectHex(mac, 32,
                  "8094a4aa0311ceadd62bd4b17fa76cb0066cff4f125cbfc64dea5bab2493f2cd",
                  "digest-size key HMAC-SHA-256 matches the net48 oracle");
        uint8_t k20[20];
        std::memset(k20, 0x11, sizeof(k20));
        HmacOneShot(il2cpp::runtime::kIl2CppHmacSha1, k20, sizeof(k20), msg, 3, 20, mac);
        ExpectHex(mac, 20,
                  "062c4668943216e606003378dd578e2ec2509617",
                  "digest-size key HMAC-SHA-1 matches the net48 oracle");
        uint8_t k16[16];
        std::memset(k16, 0x11, sizeof(k16));
        HmacOneShot(il2cpp::runtime::kIl2CppHmacMd5, k16, sizeof(k16), msg, 3, 16, mac);
        ExpectHex(mac, 16,
                  "d61a40def505eb9352e3c22cc60e49af",
                  "digest-size key HMAC-MD5 matches the net48 oracle");
    }

    // --- 5. Empty message: inner hash sees only the ipad block ---
    {
        uint8_t key[20];
        std::memset(key, 0x0b, 20);
        il2cpp::runtime::il2cpp_hmac_sha256(key, 20, nullptr, 0, mac);
        ExpectHex(mac, 32,
                  "999a901219f032cd497cadb5e6051e97b6a29ab297bd6ae722bd6062a2f59542",
                  "empty message HMAC-SHA-256 matches the net48 oracle");
        uint8_t key1[20];
        std::memset(key1, 0x0b, 20);
        il2cpp::runtime::il2cpp_hmac_sha1(key1, 20, nullptr, 0, mac);
        ExpectHex(mac, 20,
                  "123fd78bda0100786ae86b76f50f01bd18e477f3",
                  "empty message HMAC-SHA-1 matches the net48 oracle");
        uint8_t keym[16];
        std::memset(keym, 0x0b, 16);
        il2cpp::runtime::il2cpp_hmac_md5(keym, 16, nullptr, 0, mac);
        ExpectHex(mac, 16,
                  "c9e99a43cd8fa24a840aa85c7cca0061",
                  "empty message HMAC-MD5 matches the net48 oracle");
    }

    // --- 6. Streaming (create/update/final) == one-shot, all algorithms ---
    {
        uint8_t key[20];
        std::memset(key, 0x0b, 20);
        const char* msg = "Hi There";
        il2cpp::runtime::il2cpp_hmac_sha256(key, 20, msg, 8, mac);
        HmacStream(il2cpp::runtime::kIl2CppHmacSha256, key, 20, msg, 8, 1, 32, streamed);
        Expect(std::memcmp(mac, streamed, 32) == 0,
               "byte-at-a-time streaming SHA-256 HMAC == one-shot");
        il2cpp::runtime::il2cpp_hmac_sha1(key, 20, msg, 8, mac);
        HmacStream(il2cpp::runtime::kIl2CppHmacSha1, key, 20, msg, 8, 1, 20, streamed);
        Expect(std::memcmp(mac, streamed, 20) == 0,
               "byte-at-a-time streaming SHA-1 HMAC == one-shot");
        il2cpp::runtime::il2cpp_hmac_md5(key, 16, msg, 8, mac);
        HmacStream(il2cpp::runtime::kIl2CppHmacMd5, key, 16, msg, 8, 1, 16, streamed);
        Expect(std::memcmp(mac, streamed, 16) == 0,
               "byte-at-a-time streaming MD5 HMAC == one-shot");
    }
    {
        uint8_t key[65];
        for (int i = 0; i < 65; ++i)
            key[i] = static_cast<uint8_t>(i);
        const char* msg = "what do ya want for nothing?";
        il2cpp::runtime::il2cpp_hmac_sha256(key, 65, msg, 28, mac);
        HmacStream(il2cpp::runtime::kIl2CppHmacSha256, key, 65, msg, 28, 7, 32, streamed);
        Expect(std::memcmp(mac, streamed, 32) == 0,
               "chunked streaming SHA-256 HMAC (long key) == one-shot");
        il2cpp::runtime::il2cpp_hmac_sha1(key, 65, msg, 28, mac);
        HmacStream(il2cpp::runtime::kIl2CppHmacSha1, key, 65, msg, 28, 7, 20, streamed);
        Expect(std::memcmp(mac, streamed, 20) == 0,
               "chunked streaming SHA-1 HMAC (long key) == one-shot");
        il2cpp::runtime::il2cpp_hmac_md5(key, 65, msg, 28, mac);
        HmacStream(il2cpp::runtime::kIl2CppHmacMd5, key, 65, msg, 28, 7, 16, streamed);
        Expect(std::memcmp(mac, streamed, 16) == 0,
               "chunked streaming MD5 HMAC (long key) == one-shot");
    }

    // --- Report ---
    uint8_t key[20];
    std::memset(key, 0x0b, 20);
    il2cpp::runtime::il2cpp_hmac_sha256(key, 20, "Hi There", 8, mac);
    std::printf("hmac_sha256(case1) = ");
    PrintHex(mac, 32);
    if (g_failures != 0)
    {
        std::printf("%d hmac assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime hmac tests passed\n");
    return 0;
}
