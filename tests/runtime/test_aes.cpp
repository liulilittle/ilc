//------------------------------------------------------------------------------
// test_aes.cpp -- AES (FIPS 197) + CBC/PKCS7 runtime unit tests (aes.h).
//
// Coverage (normal + edge + error paths):
//   1. Block cipher KATs -- SP 800-38A Appendix F:
//        F.1.1 (ECB-AES128): key 2b7e151628aed2a6abf7158809cf4f3c,
//             6bc1bee22e409f96e93d7e117393172a -> 3ad77bb40d7a3660a89ecaf32466ef97
//        F.1.3 (ECB-AES256): key 603deb1015ca71be2b73aef0857d7781
//             1f352c073b6108d72d9810a30914dff4,
//             6bc1bee22e409f96e93d7e117393172a -> f3eed1bdb5d2a03c064b5a7e3db181f8
//      (encrypt and decrypt both directions, in-place buffers too).
//   2. CBC chaining KATs -- SP 800-38A F.2.1 (AES-128) and F.2.5 (AES-256),
//      three blocks each, chained manually over the block API (the mode
//      layer's PKCS7 padding keeps raw unpadded CBC vectors out of the
//      padded entry points; F.2.1 ciphertexts are asserted again in test 5
//      against the padded mode entry point's first block).
//   3. Mode round trips (encrypt_cbc -> decrypt_cbc) over lengths
//      {0, 1, 15, 16, 17, 31, 32, 33, 47, 48, 100}: plaintext equality +
//      returned plaintext length.
//   4. PKCS7 padding shape: empty input -> 16-byte ciphertext; 15-byte
//      input -> 16-byte ciphertext; 16-byte input -> 32-byte ciphertext;
//      decrypt of each returns the original length.
//   5. Mode-layer KAT: encrypt_cbc of the F.2.1 first plaintext block
//      (16 bytes, padding appended as block 2) must produce the F.2.1
//      first ciphertext block as its first 16 output bytes.
//   6. Error paths: tampered last ciphertext byte -> CryptographicException;
//      tampered pad byte -> CryptographicException; ciphertext length not a
//      multiple of 16 -> CryptographicException; a 0-length decrypt input is
//      LEGAL and returns an empty plaintext (net48 TransformFinalBlock);
//      AES-192 key -> NotSupportedException; other bad key sizes ->
//      CryptographicException (net48 SymmetricAlgorithm.Key setter); null
//      key/iv/data/out -> ArgumentNullException.
//   7. net48 oracle mode-layer KATs: CBC+PKCS7 ciphertexts captured from a
//      live .NET Framework 4.8 Aes run (same key/iv/message) are asserted
//      byte-for-byte, and the captured empty-plaintext ciphertext decrypts
//      to zero bytes on both key sizes.
//
// Plain executable + minimal assertion harness (see test_sha256.cpp).
//------------------------------------------------------------------------------

#include "aes.h"
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

void ExpectHex(const uint8_t* bytes, int count, const char* expected, const char* what)
{
    char hex[64 * 2 + 1];
    for (int i = 0; i < count && i < 64; ++i)
        std::snprintf(hex + i * 2, sizeof(hex) - static_cast<size_t>(i) * 2, "%02x", bytes[i]);
    hex[count * 2] = '\0';
    Expect(std::strcmp(hex, expected) == 0, what);
}

// Parse a lowercase hex string into bytes.
void HexToBytes(const char* hex, uint8_t* out)
{
    for (const char* p = hex; *p != '\0'; p += 2)
    {
        const char hi = p[0], lo = p[1];
        const auto nib = [](char c) -> uint8_t {
            return static_cast<uint8_t>(c <= '9' ? c - '0' : c - 'a' + 10);
        };
        *out++ = static_cast<uint8_t>((nib(hi) << 4) | nib(lo));
    }
}

// True when the thrown DotNetException is of the given .NET type.
bool ThrowsType(void (*fn)(), const char* typeName)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeName) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

//------------------------------------------------------------------------------
// Manual CBC chaining over the BLOCK API (SP 800-38A §6.2) -- feeds the
// F.2.x vectors through the block cipher without the mode layer's padding.
//------------------------------------------------------------------------------
void CbcEncryptManual(const uint8_t* key, int32_t keyLen, const uint8_t iv[16],
                      const uint8_t* pt, int32_t len, uint8_t* ct)
{
    il2cpp::runtime::Il2CppAesContext ctx;
    il2cpp::runtime::il2cpp_aes_create(&ctx, key, keyLen);
    uint8_t prev[16];
    uint8_t block[16];
    std::memcpy(prev, iv, 16);
    for (int32_t i = 0; i < len; i += 16)
    {
        for (uint32_t j = 0; j < 16; ++j)
            block[j] = pt[i + j] ^ prev[j];
        il2cpp::runtime::il2cpp_aes_encrypt_block(&ctx, block, ct + i);
        std::memcpy(prev, ct + i, 16);
    }
}

void CbcDecryptManual(const uint8_t* key, int32_t keyLen, const uint8_t iv[16],
                      const uint8_t* ct, int32_t len, uint8_t* pt)
{
    il2cpp::runtime::Il2CppAesContext ctx;
    il2cpp::runtime::il2cpp_aes_create(&ctx, key, keyLen);
    uint8_t prev[16];
    uint8_t block[16];
    std::memcpy(prev, iv, 16);
    for (int32_t i = 0; i < len; i += 16)
    {
        il2cpp::runtime::il2cpp_aes_decrypt_block(&ctx, ct + i, block);
        for (uint32_t j = 0; j < 16; ++j)
            pt[i + j] = block[j] ^ prev[j];
        std::memcpy(prev, ct + i, 16);
    }
}

// One encrypt_cbc + decrypt_cbc round trip; asserts plaintext equality and
// the returned plaintext length. The ciphertext length is input length
// rounded up to a block PLUS the padding block (PKCS7 never shrinks).
void RoundTrip(const uint8_t* key, int32_t keyLen, const uint8_t iv[16],
               const uint8_t* plain, int32_t plainLen, const char* what)
{
    uint8_t ct[256];
    uint8_t pt[256];
    il2cpp::runtime::il2cpp_aes_encrypt_cbc(key, keyLen, iv, plain, plainLen, ct);
    const int32_t ctLen = plainLen + (16 - plainLen % 16);
    const int32_t back = il2cpp::runtime::il2cpp_aes_decrypt_cbc(key, keyLen, iv, ct,
                                                                 ctLen, pt);
    Expect(back == plainLen, what);
    Expect(std::memcmp(plain, pt, static_cast<size_t>(plainLen)) == 0, what);
}

//------------------------------------------------------------------------------
// SP 800-38A vectors.
//------------------------------------------------------------------------------
// F.1.1/F.2.1: AES-128 key and IV.
const char kKey128[] = "2b7e151628aed2a6abf7158809cf4f3c";
// F.1.3/F.2.5: AES-256 key (same IV).
const char kKey256[] = "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4";
const char kIv[]     = "000102030405060708090a0b0c0d0e0f";

const char kPt1[] = "6bc1bee22e409f96e93d7e117393172a";
const char kPt2[] = "ae2d8a571e03ac9c9eb76fac45af8e51";
const char kPt3[] = "30c81c46a35ce411e5fbc1191a0a52ef";

const char kCbc128C1[] = "7649abac8119b246cee98e9b12e9197d";
const char kCbc128C2[] = "5086cb9b507219ee95db113a917678b2";
const char kCbc128C3[] = "73bed6b8e3c1743b7116e69e22229516";

const char kCbc256C1[] = "f58c4c04d6e5f1ba779eabfb5f7bfbd6";
const char kCbc256C2[] = "9cfc4e967edb808d679f777bc6702c7d";
const char kCbc256C3[] = "39f23369a9d9bacfa530e26304231461";

//------------------------------------------------------------------------------
// net48 oracle mode-layer vectors (live .NET Framework 4.8 Aes run):
// key = "0123456789abcdef" x1 (AES-128) / x2 (AES-256), iv = "0123456789abcdef",
// message = "The quick brown fox jumps over the lazy dog" (43 bytes) ->
// 48-byte CBC+PKCS7 ciphertext; the empty plaintext encrypts to the 16-byte
// pad block below. These assert byte-exact parity with net48.
//------------------------------------------------------------------------------
const char kNet48Msg[] = "The quick brown fox jumps over the lazy dog";
const char kKeyAes128Str[] = "0123456789abcdef";
const char kKeyAes256Str[] = "0123456789abcdef0123456789abcdef";
const char kIvStr[] = "0123456789abcdef";
const char kNet48Ct128[] =
    "2df981387725776104b757ef44908a6481e62d669539ff84f8b6042805eeed2b3148f59d9fa19d39d5f671e410f351ee";
const char kNet48Ct256[] =
    "3fb71aa5df5cb45088bb79102524e2abf3be6098d39db8996f4ea605bb14a64e1fd1c082c754f3d51fd48d4406574af6";
const char kNet48EmptyCt[] = "ed47fee0545c3fa7dd070d44b86e98d9";
// AES-256 empty plaintext -> its own pad block (net48 capture).
const char kNet48EmptyCt256[] = "38ae83a3edd22a71ecf296539ddf7320";

} // namespace

int main()
{
    uint8_t key128[16], key256[32], iv[16];
    uint8_t pt1[16], pt2[16], pt3[16];
    HexToBytes(kKey128, key128);
    HexToBytes(kKey256, key256);
    HexToBytes(kIv, iv);
    HexToBytes(kPt1, pt1);
    HexToBytes(kPt2, pt2);
    HexToBytes(kPt3, pt3);

    // --- 1. Block KATs (F.1.1 / F.1.3), both directions + in-place ---
    {
        il2cpp::runtime::Il2CppAesContext ctx;
        il2cpp::runtime::il2cpp_aes_create(&ctx, key128, 16);
        uint8_t ct[16], pt[16];
        il2cpp::runtime::il2cpp_aes_encrypt_block(&ctx, pt1, ct);
        ExpectHex(ct, 16, "3ad77bb40d7a3660a89ecaf32466ef97",
                  "ECB-AES128 F.1.1: 6bc1bee2... encrypts to 3ad77bb4...");
        il2cpp::runtime::il2cpp_aes_decrypt_block(&ctx, ct, pt);
        Expect(std::memcmp(pt, pt1, 16) == 0, "ECB-AES128 F.1.1 decrypt round trip");

        il2cpp::runtime::il2cpp_aes_create(&ctx, key256, 32);
        il2cpp::runtime::il2cpp_aes_encrypt_block(&ctx, pt1, ct);
        ExpectHex(ct, 16, "f3eed1bdb5d2a03c064b5a7e3db181f8",
                  "ECB-AES256 F.1.3: 6bc1bee2... encrypts to f3eed1bd...");
        il2cpp::runtime::il2cpp_aes_decrypt_block(&ctx, ct, pt);
        Expect(std::memcmp(pt, pt1, 16) == 0, "ECB-AES256 F.1.3 decrypt round trip");

        uint8_t inPlace[16];
        std::memcpy(inPlace, pt1, 16);
        il2cpp::runtime::il2cpp_aes_encrypt_block(&ctx, inPlace, inPlace);
        ExpectHex(inPlace, 16, "f3eed1bdb5d2a03c064b5a7e3db181f8",
                  "ECB-AES256 in-place encrypt (in == out buffer)");
    }

    // --- 2. CBC chaining KATs (F.2.1 AES-128 / F.2.5 AES-256) ---
    {
        uint8_t ctAll[48], ptAll[48];
        uint8_t pt48[48];
        std::memcpy(pt48, pt1, 16);
        std::memcpy(pt48 + 16, pt2, 16);
        std::memcpy(pt48 + 32, pt3, 16);
        CbcEncryptManual(key128, 16, iv, pt48, 48, ctAll);
        ExpectHex(ctAll, 16, kCbc128C1, "CBC-AES128 F.2.1 block 1 ciphertext");
        ExpectHex(ctAll + 16, 16, kCbc128C2, "CBC-AES128 F.2.1 block 2 ciphertext");
        ExpectHex(ctAll + 32, 16, kCbc128C3, "CBC-AES128 F.2.1 block 3 ciphertext");
        CbcDecryptManual(key128, 16, iv, ctAll, 48, ptAll);
        Expect(std::memcmp(ptAll, pt48, 48) == 0, "CBC-AES128 F.2.1 decrypt round trip");

        CbcEncryptManual(key256, 32, iv, pt48, 48, ctAll);
        ExpectHex(ctAll, 16, kCbc256C1, "CBC-AES256 F.2.5 block 1 ciphertext");
        ExpectHex(ctAll + 16, 16, kCbc256C2, "CBC-AES256 F.2.5 block 2 ciphertext");
        ExpectHex(ctAll + 32, 16, kCbc256C3, "CBC-AES256 F.2.5 block 3 ciphertext");
        CbcDecryptManual(key256, 32, iv, ctAll, 48, ptAll);
        Expect(std::memcmp(ptAll, pt48, 48) == 0, "CBC-AES256 F.2.5 decrypt round trip");
    }

        // --- 3. Mode round trips (CBC + PKCS7) across lengths ---
    {
        uint8_t plain[100];
        for (int i = 0; i < 100; ++i)
            plain[i] = static_cast<uint8_t>(i * 7 + 3);
        const int32_t lengths[] = { 0, 1, 15, 16, 17, 31, 32, 33, 47, 48, 100 };
        for (size_t t = 0; t < sizeof(lengths) / sizeof(lengths[0]); ++t)
        {
            const int32_t len = lengths[t];
            char what[64];
            std::snprintf(what, sizeof(what), "round trip of %d-byte plaintext (AES-128)", len);
            RoundTrip(key128, 16, iv, plain, len, what);
            std::snprintf(what, sizeof(what), "round trip of %d-byte plaintext (AES-256)", len);
            RoundTrip(key256, 32, iv, plain, len, what);
        }
    }

        // --- 4. PKCS7 padding shape ---
    {
        uint8_t ct[64];
        // Empty input: exactly one 16-byte padding block.
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key128, 16, iv, nullptr, 0, ct);
        uint8_t pt[32];
        Expect(il2cpp::runtime::il2cpp_aes_decrypt_cbc(key128, 16, iv, ct, 16, pt) == 0,
               "PKCS7: empty plaintext -> one pad block, decrypt -> 0 bytes");
        // 15-byte input: padLen 1.
        const uint8_t data15[15] = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key128, 16, iv, data15, 15, ct);
        uint8_t back15[15];
        Expect(il2cpp::runtime::il2cpp_aes_decrypt_cbc(key128, 16, iv, ct, 16, back15) == 15,
               "PKCS7: 15-byte plaintext -> 16-byte ciphertext -> 15-byte plaintext");
        // 16-byte input: a full 0x10 pad block is appended (32-byte output).
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key128, 16, iv, pt1, 16, ct);
        uint8_t back16[32];
        Expect(il2cpp::runtime::il2cpp_aes_decrypt_cbc(key128, 16, iv, ct, 32, back16) == 16,
               "PKCS7: block-aligned plaintext gains a full 0x10 block");
    }

        // --- 5. Mode-layer CBC KAT: first output block == F.2.1 C1 ---
    {
        uint8_t ct[32];
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key128, 16, iv, pt1, 16, ct);
        ExpectHex(ct, 16, kCbc128C1,
                  "mode encrypt_cbc: first 16 output bytes match F.2.1 block 1");
        uint8_t pt[16];
        Expect(il2cpp::runtime::il2cpp_aes_decrypt_cbc(key128, 16, iv, ct, 32, pt) == 16,
               "mode decrypt_cbc of the F.2.1 first block returns 16 bytes");
        Expect(std::memcmp(pt, pt1, 16) == 0,
               "mode decrypt_cbc of the F.2.1 first block recovers the plaintext");
    }

        // --- 6. Error paths ---
    {
        uint8_t ct[64];
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key128, 16, iv, pt1, 16, ct);

        // Tamper the last ciphertext byte: the decrypted pad byte changes ->
        // CryptographicException ("Padding is invalid and cannot be removed.").
        uint8_t tampered[32];
        std::memcpy(tampered, ct, 32);
        tampered[31] ^= 0x01;
        uint8_t pt[32];
        bool cryptoThrown = false;
        try
        {
            il2cpp::runtime::il2cpp_aes_decrypt_cbc(key128, 16, iv, tampered, 32, pt);
        }
        catch (const il2cpp::DotNetException& e)
        {
            cryptoThrown = std::strstr(e.type_name(), "System.Security.Cryptography.CryptographicException")
                           != nullptr;
        }
        Expect(cryptoThrown, "tampered last ciphertext byte -> CryptographicException");

        // Tamper a pad byte (byte 30 of a 32-byte output whose pad block is
        // all 0x10): the pad-bytes-must-equal-padLen check fails ->
        // CryptographicException.
        Expect(ThrowsType([]() {
                    uint8_t k[16], ivBuf[16], cipher[32], ptBuf[32];
                    HexToBytes(kKey128, k);
                    HexToBytes(kIv, ivBuf);
                    uint8_t plain[16];
                    HexToBytes(kPt1, plain);
                    il2cpp::runtime::il2cpp_aes_encrypt_cbc(k, 16, ivBuf, plain, 16, cipher);
                    cipher[30] ^= 0x01;
                    il2cpp::runtime::il2cpp_aes_decrypt_cbc(k, 16, ivBuf, cipher, 32, ptBuf);
                }, "System.Security.Cryptography.CryptographicException"),
               "tampered pad byte -> CryptographicException");

        // Ciphertext length must be a multiple of 16 (0 is legal).
        Expect(ThrowsType([]() {
                    uint8_t ptBuf[32];
                    uint8_t k[16], ivBuf[16];
                    HexToBytes(kKey128, k);
                    HexToBytes(kIv, ivBuf);
                    const uint8_t in15[15] = { 0 };
                    il2cpp::runtime::il2cpp_aes_decrypt_cbc(k, 16, ivBuf, in15, 15, ptBuf);
                }, "System.Security.Cryptography.CryptographicException"),
               "decrypt of 15-byte ciphertext -> CryptographicException");
        Expect(ThrowsType([]() {
                    uint8_t ptBuf[32];
                    uint8_t k[16], ivBuf[16];
                    HexToBytes(kKey128, k);
                    HexToBytes(kIv, ivBuf);
                    il2cpp::runtime::il2cpp_aes_decrypt_cbc(k, 16, ivBuf, ptBuf, -16, ptBuf);
                }, "System.Security.Cryptography.CryptographicException"),
               "decrypt of negative-length ciphertext -> CryptographicException");
        // net48 accepts a 0-length decrypt input and returns an empty
        // plaintext (TransformFinalBlock, live-probe verified) -- no
        // exception, return value 0.
        {
            uint8_t ptBuf[16];
            uint8_t k[16], ivBuf[16];
            HexToBytes(kKey128, k);
            HexToBytes(kIv, ivBuf);
            const int32_t back = il2cpp::runtime::il2cpp_aes_decrypt_cbc(
                k, 16, ivBuf, ptBuf, 0, ptBuf);
            Expect(back == 0, "decrypt of 0-byte ciphertext returns 0 (net48)");
        }

        // AES-192 is out of scope -> NotSupportedException.
        Expect(ThrowsType([]() {
                    il2cpp::runtime::Il2CppAesContext ctx;
                    uint8_t key24[24] = { 0 };
                    il2cpp::runtime::il2cpp_aes_create(&ctx, key24, 24);
                }, "System.NotSupportedException"),
               "AES-192 (24-byte key) -> NotSupportedException");

        // Other bad key sizes -> CryptographicException (net48
        // SymmetricAlgorithm.Key setter semantics, live-probe verified:
        // invalid key size throws Crypto, not ArgumentException).
        Expect(ThrowsType([]() {
                    il2cpp::runtime::Il2CppAesContext ctx;
                    uint8_t key8[8] = { 0 };
                    il2cpp::runtime::il2cpp_aes_create(&ctx, key8, 8);
                }, "System.Security.Cryptography.CryptographicException"),
               "8-byte key -> CryptographicException");

        // Null key -> ArgumentNullException.
        Expect(ThrowsType([]() {
                    il2cpp::runtime::Il2CppAesContext ctx;
                    il2cpp::runtime::il2cpp_aes_create(&ctx, nullptr, 16);
                }, "System.ArgumentNullException"),
               "null key -> ArgumentNullException");

        // Null iv / data / out -> ArgumentNullException.
        Expect(ThrowsType([]() {
                    uint8_t buf[64];
                    il2cpp::runtime::il2cpp_aes_encrypt_cbc(buf, 16, nullptr, buf, 16, buf);
                }, "System.ArgumentNullException"),
               "null iv -> ArgumentNullException");
        Expect(ThrowsType([]() {
                    uint8_t buf[64];
                    il2cpp::runtime::il2cpp_aes_encrypt_cbc(buf, 16, buf, nullptr, 16, buf);
                }, "System.ArgumentNullException"),
               "null data -> ArgumentNullException");
        Expect(ThrowsType([]() {
                    uint8_t buf[64];
                    il2cpp::runtime::il2cpp_aes_encrypt_cbc(buf, 16, buf, buf, 16, nullptr);
                }, "System.ArgumentNullException"),
               "null out -> ArgumentNullException");
    }

        // --- 7. net48 oracle mode-layer KATs ---
    {
        uint8_t key128[16], key256[32], iv[16];
        std::memcpy(key128, kKeyAes128Str, 16);
        std::memcpy(key256, kKeyAes256Str, 32);
        std::memcpy(iv, kIvStr, 16);
        const uint8_t* msg = reinterpret_cast<const uint8_t*>(kNet48Msg);
        const int32_t msgLen = 43;

        uint8_t ct128[48];
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key128, 16, iv, msg, msgLen, ct128);
        ExpectHex(ct128, 48, kNet48Ct128,
                  "net48 oracle: AES-128 CBC+PKCS7 ciphertext matches");
        uint8_t back128[48];
        const int32_t pt128 = il2cpp::runtime::il2cpp_aes_decrypt_cbc(
            key128, 16, iv, ct128, 48, back128);
        Expect(pt128 == msgLen, "net48 oracle: AES-128 decrypt returns 43");
        Expect(std::memcmp(back128, msg, 43) == 0,
               "net48 oracle: AES-128 decrypt recovers the message");

        uint8_t ct256[48];
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key256, 32, iv, msg, msgLen, ct256);
        ExpectHex(ct256, 48, kNet48Ct256,
                  "net48 oracle: AES-256 CBC+PKCS7 ciphertext matches");
        uint8_t back256[48];
        const int32_t pt256 = il2cpp::runtime::il2cpp_aes_decrypt_cbc(
            key256, 32, iv, ct256, 48, back256);
        Expect(pt256 == msgLen, "net48 oracle: AES-256 decrypt returns 43");
        Expect(std::memcmp(back256, msg, 43) == 0,
               "net48 oracle: AES-256 decrypt recovers the message");

        // Empty plaintext -> the net48-captured 16-byte pad block; decrypting
        // it (both key sizes) yields an empty plaintext.
        uint8_t emptyCt[16];
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key128, 16, iv, nullptr, 0, emptyCt);
        ExpectHex(emptyCt, 16, kNet48EmptyCt,
                  "net48 oracle: empty plaintext encrypts to the pad block (AES-128)");
        uint8_t emptyBack[16];
        Expect(il2cpp::runtime::il2cpp_aes_decrypt_cbc(
                   key128, 16, iv, emptyCt, 16, emptyBack) == 0,
               "net48 oracle: pad block decrypts to 0 bytes (AES-128)");
        uint8_t emptyCt256[16];
        il2cpp::runtime::il2cpp_aes_encrypt_cbc(key256, 32, iv, nullptr, 0, emptyCt256);
        ExpectHex(emptyCt256, 16, kNet48EmptyCt256,
                  "net48 oracle: empty plaintext encrypts to the pad block (AES-256)");
        Expect(il2cpp::runtime::il2cpp_aes_decrypt_cbc(
                   key256, 32, iv, emptyCt256, 16, emptyBack) == 0,
               "net48 oracle: pad block decrypts to 0 bytes (AES-256)");
    }

    if (g_failures != 0)
    {
        std::printf("%d aes assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime aes tests passed\n");
    return 0;
}
