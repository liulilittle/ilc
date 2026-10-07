//------------------------------------------------------------------------------
// aes.cpp -- AES block cipher + CBC/PKCS7 mode (FIPS 197 + SP 800-38A).
//
// Implementation notes:
//   * The S-box and inverse S-box are the FIPS 197 section 5.1.1 / section 5.3.2 tables;
//     the round constants follow FIPS 197 section 5.2 (Rcon[i] = x^(i-1) in GF(2^8)).
//   * Key expansion: FIPS 197 section 5.2 -- Nk = 4 (AES-128) or 8 (AES-256),
//     Nr = 10 or 14, w[] of 4*(Nr+1) words. The Nk == 8 && i % Nk == 4
//     SubWord step is the AES-256-only extra substitution.
//   * Encryption: FIPS 197 section 5.1 (AddRoundKey -> Nr-1 rounds of
//     SubBytes/ShiftRows/MixColumns/AddRoundKey -> final SubBytes/ShiftRows/
//     AddRoundKey). Decryption: the FIPS 197 section 5.3 Inverse Cipher with the
//     SAME schedule (InvShiftRows/InvSubBytes/AddRoundKey/InvMixColumns
//     applied in reverse), not the equivalent-inverse-cipher variant -- the
//     straight inverse needs no schedule transform and is verified directly
//     against the SP 800-38A decrypt vectors.
//   * State layout is column-major (state[r][c], byte i -> r = i % 4,
//     c = i / 4) exactly as FIPS 197 section 3.4 defines the cipher state.
//   * CBC: SP 800-38A section 6.2 -- each plaintext block XORs the previous
//     ciphertext block (the IV for the first); decrypt reverses. PKCS #7
//     padding: pad bytes all equal padLen (1..16) so block-aligned inputs
//     gain a full 0x10 block.
//   * Error semantics follow net48 (see aes.h): AES-192 -> NotSupported,
//     other bad key sizes -> CryptographicException (verified against a live
//     net48 probe -- SymmetricAlgorithm.Key throws Crypto, not Argument),
//     bad ciphertext length or invalid padding -> CryptographicException.
//     A 0-length decrypt input is legal and yields an empty plaintext
//     (net48 TransformFinalBlock returns an empty array for an empty
//     ciphertext -- no exception). All thrown through the
//     il2cpp::DotNetException carrier so generated try/catch matches.
//------------------------------------------------------------------------------

#include "aes.h"

#include "il2cpp_exception_ext.h"

#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// FIPS 197 section 5.1.1, Table S (S-box).
//------------------------------------------------------------------------------
const uint8_t kSBox[256] = {
    0x63, 0x7c, 0x77, 0x7b, 0xf2, 0x6b, 0x6f, 0xc5, 0x30, 0x01, 0x67, 0x2b, 0xfe, 0xd7, 0xab, 0x76,
    0xca, 0x82, 0xc9, 0x7d, 0xfa, 0x59, 0x47, 0xf0, 0xad, 0xd4, 0xa2, 0xaf, 0x9c, 0xa4, 0x72, 0xc0,
    0xb7, 0xfd, 0x93, 0x26, 0x36, 0x3f, 0xf7, 0xcc, 0x34, 0xa5, 0xe5, 0xf1, 0x71, 0xd8, 0x31, 0x15,
    0x04, 0xc7, 0x23, 0xc3, 0x18, 0x96, 0x05, 0x9a, 0x07, 0x12, 0x80, 0xe2, 0xeb, 0x27, 0xb2, 0x75,
    0x09, 0x83, 0x2c, 0x1a, 0x1b, 0x6e, 0x5a, 0xa0, 0x52, 0x3b, 0xd6, 0xb3, 0x29, 0xe3, 0x2f, 0x84,
    0x53, 0xd1, 0x00, 0xed, 0x20, 0xfc, 0xb1, 0x5b, 0x6a, 0xcb, 0xbe, 0x39, 0x4a, 0x4c, 0x58, 0xcf,
    0xd0, 0xef, 0xaa, 0xfb, 0x43, 0x4d, 0x33, 0x85, 0x45, 0xf9, 0x02, 0x7f, 0x50, 0x3c, 0x9f, 0xa8,
    0x51, 0xa3, 0x40, 0x8f, 0x92, 0x9d, 0x38, 0xf5, 0xbc, 0xb6, 0xda, 0x21, 0x10, 0xff, 0xf3, 0xd2,
    0xcd, 0x0c, 0x13, 0xec, 0x5f, 0x97, 0x44, 0x17, 0xc4, 0xa7, 0x7e, 0x3d, 0x64, 0x5d, 0x19, 0x73,
    0x60, 0x81, 0x4f, 0xdc, 0x22, 0x2a, 0x90, 0x88, 0x46, 0xee, 0xb8, 0x14, 0xde, 0x5e, 0x0b, 0xdb,
    0xe0, 0x32, 0x3a, 0x0a, 0x49, 0x06, 0x24, 0x5c, 0xc2, 0xd3, 0xac, 0x62, 0x91, 0x95, 0xe4, 0x79,
    0xe7, 0xc8, 0x37, 0x6d, 0x8d, 0xd5, 0x4e, 0xa9, 0x6c, 0x56, 0xf4, 0xea, 0x65, 0x7a, 0xae, 0x08,
    0xba, 0x78, 0x25, 0x2e, 0x1c, 0xa6, 0xb4, 0xc6, 0xe8, 0xdd, 0x74, 0x1f, 0x4b, 0xbd, 0x8b, 0x8a,
    0x70, 0x3e, 0xb5, 0x66, 0x48, 0x03, 0xf6, 0x0e, 0x61, 0x35, 0x57, 0xb9, 0x86, 0xc1, 0x1d, 0x9e,
    0xe1, 0xf8, 0x98, 0x11, 0x69, 0xd9, 0x8e, 0x94, 0x9b, 0x1e, 0x87, 0xe9, 0xce, 0x55, 0x28, 0xdf,
    0x8c, 0xa1, 0x89, 0x0d, 0xbf, 0xe6, 0x42, 0x68, 0x41, 0x99, 0x2d, 0x0f, 0xb0, 0x54, 0xbb, 0x16,
};

//------------------------------------------------------------------------------
// FIPS 197 section 5.3.2, Table S^-1 (inverse S-box).
//------------------------------------------------------------------------------
const uint8_t kInvSBox[256] = {
    0x52, 0x09, 0x6a, 0xd5, 0x30, 0x36, 0xa5, 0x38, 0xbf, 0x40, 0xa3, 0x9e, 0x81, 0xf3, 0xd7, 0xfb,
    0x7c, 0xe3, 0x39, 0x82, 0x9b, 0x2f, 0xff, 0x87, 0x34, 0x8e, 0x43, 0x44, 0xc4, 0xde, 0xe9, 0xcb,
    0x54, 0x7b, 0x94, 0x32, 0xa6, 0xc2, 0x23, 0x3d, 0xee, 0x4c, 0x95, 0x0b, 0x42, 0xfa, 0xc3, 0x4e,
    0x08, 0x2e, 0xa1, 0x66, 0x28, 0xd9, 0x24, 0xb2, 0x76, 0x5b, 0xa2, 0x49, 0x6d, 0x8b, 0xd1, 0x25,
    0x72, 0xf8, 0xf6, 0x64, 0x86, 0x68, 0x98, 0x16, 0xd4, 0xa4, 0x5c, 0xcc, 0x5d, 0x65, 0xb6, 0x92,
    0x6c, 0x70, 0x48, 0x50, 0xfd, 0xed, 0xb9, 0xda, 0x5e, 0x15, 0x46, 0x57, 0xa7, 0x8d, 0x9d, 0x84,
    0x90, 0xd8, 0xab, 0x00, 0x8c, 0xbc, 0xd3, 0x0a, 0xf7, 0xe4, 0x58, 0x05, 0xb8, 0xb3, 0x45, 0x06,
    0xd0, 0x2c, 0x1e, 0x8f, 0xca, 0x3f, 0x0f, 0x02, 0xc1, 0xaf, 0xbd, 0x03, 0x01, 0x13, 0x8a, 0x6b,
    0x3a, 0x91, 0x11, 0x41, 0x4f, 0x67, 0xdc, 0xea, 0x97, 0xf2, 0xcf, 0xce, 0xf0, 0xb4, 0xe6, 0x73,
    0x96, 0xac, 0x74, 0x22, 0xe7, 0xad, 0x35, 0x85, 0xe2, 0xf9, 0x37, 0xe8, 0x1c, 0x75, 0xdf, 0x6e,
    0x47, 0xf1, 0x1a, 0x71, 0x1d, 0x29, 0xc5, 0x89, 0x6f, 0xb7, 0x62, 0x0e, 0xaa, 0x18, 0xbe, 0x1b,
    0xfc, 0x56, 0x3e, 0x4b, 0xc6, 0xd2, 0x79, 0x20, 0x9a, 0xdb, 0xc0, 0xfe, 0x78, 0xcd, 0x5a, 0xf4,
    0x1f, 0xdd, 0xa8, 0x33, 0x88, 0x07, 0xc7, 0x31, 0xb1, 0x12, 0x10, 0x59, 0x27, 0x80, 0xec, 0x5f,
    0x60, 0x51, 0x7f, 0xa9, 0x19, 0xb5, 0x4a, 0x0d, 0x2d, 0xe5, 0x7a, 0x9f, 0x93, 0xc9, 0x9c, 0xef,
    0xa0, 0xe0, 0x3b, 0x4d, 0xae, 0x2a, 0xf5, 0xb0, 0xc8, 0xeb, 0xbb, 0x3c, 0x83, 0x53, 0x99, 0x61,
    0x17, 0x2b, 0x04, 0x7e, 0xba, 0x77, 0xd6, 0x26, 0xe1, 0x69, 0x14, 0x63, 0x55, 0x21, 0x0c, 0x7d,
};

//------------------------------------------------------------------------------
// Round constants: Rcon[i] = x^(i-1) in GF(2^8) (FIPS 197 section 5.2). 15 entries
// cover AES-256 (needs Rcon[1..7]).
//------------------------------------------------------------------------------
const uint32_t kRcon[15] = {
    0x01000000u, 0x02000000u, 0x04000000u, 0x08000000u, 0x10000000u,
    0x20000000u, 0x40000000u, 0x80000000u, 0x1b000000u, 0x36000000u,
    0x6c000000u, 0xd8000000u, 0xab000000u, 0x4d000000u, 0x01000000u,
};

//------------------------------------------------------------------------------
// GF(2^8) helpers (polynomial modulus 0x11b).
//------------------------------------------------------------------------------
uint8_t GfMul(uint8_t a, uint8_t b)
{
    uint8_t r = 0;
    for (uint32_t i = 0; i < 8; ++i)
    {
        if (b & 1u)
        {
            r ^= a;
        }
        b >>= 1;
        a = static_cast<uint8_t>((a << 1) ^ ((a & 0x80u) ? 0x1bu : 0u));
    }
    return r;
}

uint8_t GfMul2(uint8_t a)
{
    return static_cast<uint8_t>((a << 1) ^ ((a & 0x80u) ? 0x1bu : 0u));
}

//------------------------------------------------------------------------------
// Word helpers (words are the big-endian image of 4 state bytes).
//------------------------------------------------------------------------------
uint32_t LoadBE32(const uint8_t* p)
{
    return (static_cast<uint32_t>(p[0]) << 24) |
           (static_cast<uint32_t>(p[1]) << 16) |
           (static_cast<uint32_t>(p[2]) << 8) |
           static_cast<uint32_t>(p[3]);
}

// S-box applied to each of the four bytes of a word.
uint32_t SubWord(uint32_t w)
{
    const uint32_t b0 = kSBox[(w >> 24) & 0xff];
    const uint32_t b1 = kSBox[(w >> 16) & 0xff];
    const uint32_t b2 = kSBox[(w >> 8) & 0xff];
    const uint32_t b3 = kSBox[w & 0xff];
    return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
}

// Cyclic left rotation of the four bytes of a word (FIPS 197 section 5.2 RotWord).
uint32_t RotWord(uint32_t w)
{
    return (w << 8) | (w >> 24);
}

//------------------------------------------------------------------------------
// FIPS 197 section 5.2 key expansion into ctx->roundKeys (4*(Nr+1) words).
//------------------------------------------------------------------------------
void ExpandKey(Il2CppAesContext* ctx, const uint8_t* key)
{
    const uint32_t nk = ctx->nk;
    const uint32_t total = 4u * (ctx->nr + 1u);
    for (uint32_t i = 0; i < nk; ++i)
    {
        ctx->roundKeys[i] = LoadBE32(key + i * 4);
    }
    for (uint32_t i = nk; i < total; ++i)
    {
        uint32_t temp = ctx->roundKeys[i - 1];
        if (i % nk == 0)
        {
            // i/nk is the round number (1-based), and kRcon holds
            // Rcon[1]..Rcon[15], so index i/nk - 1.
            temp = SubWord(RotWord(temp)) ^ kRcon[i / nk - 1u];
        }
        else if (nk == 8u && i % nk == 4u)
        {
            // AES-256 extra substitution step (FIPS 197 section 5.2).
            temp = SubWord(temp);
        }
        ctx->roundKeys[i] = ctx->roundKeys[i - nk] ^ temp;
    }
}

//------------------------------------------------------------------------------
// Cipher state (column-major, FIPS 197 section 3.4): state[r][c], byte i ->
// r = i % 4, c = i / 4.
//------------------------------------------------------------------------------
typedef uint8_t State[4][4];

void LoadState(State s, const uint8_t* in)
{
    for (uint32_t i = 0; i < 16; ++i)
    {
        s[i % 4][i / 4] = in[i];
    }
}

void StoreState(uint8_t* out, const State s)
{
    for (uint32_t i = 0; i < 16; ++i)
    {
        out[i] = s[i % 4][i / 4];
    }
}

void AddRoundKey(State s, const uint32_t* w, uint32_t round)
{
    for (uint32_t c = 0; c < 4; ++c)
    {
        const uint32_t word = w[4u * round + c];
        for (uint32_t r = 0; r < 4; ++r)
        {
            s[r][c] ^= static_cast<uint8_t>(word >> (24u - 8u * r));
        }
    }
}

void SubBytes(State s)
{
    for (uint32_t c = 0; c < 4; ++c)
    {
        for (uint32_t r = 0; r < 4; ++r)
        {
            s[r][c] = kSBox[s[r][c]];
        }
    }
}

void InvSubBytes(State s)
{
    for (uint32_t c = 0; c < 4; ++c)
    {
        for (uint32_t r = 0; r < 4; ++r)
        {
            s[r][c] = kInvSBox[s[r][c]];
        }
    }
}

// ShiftRows: row r cyclically shifted left by r (FIPS 197 section 5.1.2).
void ShiftRows(State s)
{
    for (uint32_t r = 1; r < 4; ++r)
    {
        const uint8_t keep[4] = { s[r][0], s[r][1], s[r][2], s[r][3] };
        for (uint32_t c = 0; c < 4; ++c)
        {
            s[r][c] = keep[(c + r) % 4];
        }
    }
}

// InvShiftRows: row r cyclically shifted right by r (FIPS 197 section 5.3.1).
void InvShiftRows(State s)
{
    for (uint32_t r = 1; r < 4; ++r)
    {
        const uint8_t keep[4] = { s[r][0], s[r][1], s[r][2], s[r][3] };
        for (uint32_t c = 0; c < 4; ++c)
        {
            s[r][c] = keep[(c - r + 4) % 4];
        }
    }
}

// MixColumns: each column is a polynomial multiply by 3x^3+x^2+x+2 over
// GF(2^8) (FIPS 197 section 5.1.3).
void MixColumns(State s)
{
    for (uint32_t c = 0; c < 4; ++c)
    {
        const uint8_t a0 = s[0][c], a1 = s[1][c], a2 = s[2][c], a3 = s[3][c];
        s[0][c] = GfMul2(a0) ^ GfMul(a1, 3u) ^ a2 ^ a3;
        s[1][c] = a0 ^ GfMul2(a1) ^ GfMul(a2, 3u) ^ a3;
        s[2][c] = a0 ^ a1 ^ GfMul2(a2) ^ GfMul(a3, 3u);
        s[3][c] = GfMul(a0, 3u) ^ a1 ^ a2 ^ GfMul2(a3);
    }
}

// InvMixColumns: each column is a polynomial multiply by
// 11x^3+13x^2+9x+14 over GF(2^8) (FIPS 197 section 5.3.3).
void InvMixColumns(State s)
{
    for (uint32_t c = 0; c < 4; ++c)
    {
        const uint8_t a0 = s[0][c], a1 = s[1][c], a2 = s[2][c], a3 = s[3][c];
        s[0][c] = GfMul(a0, 14u) ^ GfMul(a1, 11u) ^ GfMul(a2, 13u) ^ GfMul(a3, 9u);
        s[1][c] = GfMul(a0, 9u) ^ GfMul(a1, 14u) ^ GfMul(a2, 11u) ^ GfMul(a3, 13u);
        s[2][c] = GfMul(a0, 13u) ^ GfMul(a1, 9u) ^ GfMul(a2, 14u) ^ GfMul(a3, 11u);
        s[3][c] = GfMul(a0, 11u) ^ GfMul(a1, 13u) ^ GfMul(a2, 9u) ^ GfMul(a3, 14u);
    }
}

//------------------------------------------------------------------------------
// Exception type-name chains (il2cpp_exception.h carrier format, see
// il2cpp_exception_ext.h for the layout rule).
//------------------------------------------------------------------------------
const char kCryptoChain[] =
    "System.Security.Cryptography.CryptographicException,"
    "System.Security.Cryptography.CryptographicException,"
    "System.SystemException,System.Exception";

} // namespace

//------------------------------------------------------------------------------
// il2cpp_aes_create -- key validation + key schedule expansion.
//------------------------------------------------------------------------------
void il2cpp_aes_create(Il2CppAesContext* ctx, const void* key, int32_t keyLen)
{
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    if (keyLen == 24)
    {
        // AES-192 is out of scope (docs/design/crypto-domain.md section 1.2).
        il2cpp_throw_not_supported_exception(
            "AES-192 is not supported by this build (128/256-bit keys only).");
    }
    if (keyLen != 16 && keyLen != 32)
    {
        // net48 SymmetricAlgorithm.Key setter semantics (live-probe verified:
        // invalid key size throws CryptographicException, not ArgumentException).
        il2cpp_throw_new(kCryptoChain,
                         "The specified key is not a valid size for this algorithm.");
    }
    ctx->nk = static_cast<uint32_t>(keyLen / 4);
    ctx->nr = (ctx->nk == 4u) ? 10u : 14u;
    ExpandKey(ctx, static_cast<const uint8_t*>(key));
}

//------------------------------------------------------------------------------
// il2cpp_aes_encrypt_block -- FIPS 197 section 5.1 forward cipher.
//------------------------------------------------------------------------------
void il2cpp_aes_encrypt_block(const Il2CppAesContext* ctx, const void* in16, void* out16)
{
    State s;
    LoadState(s, static_cast<const uint8_t*>(in16));
    AddRoundKey(s, ctx->roundKeys, 0);
    for (uint32_t round = 1; round < ctx->nr; ++round)
    {
        SubBytes(s);
        ShiftRows(s);
        MixColumns(s);
        AddRoundKey(s, ctx->roundKeys, round);
    }
    SubBytes(s);
    ShiftRows(s);
    AddRoundKey(s, ctx->roundKeys, ctx->nr);
    StoreState(static_cast<uint8_t*>(out16), s);
}

//------------------------------------------------------------------------------
// il2cpp_aes_decrypt_block -- FIPS 197 section 5.3 inverse cipher (same schedule).
//------------------------------------------------------------------------------
void il2cpp_aes_decrypt_block(const Il2CppAesContext* ctx, const void* in16, void* out16)
{
    State s;
    LoadState(s, static_cast<const uint8_t*>(in16));
    AddRoundKey(s, ctx->roundKeys, ctx->nr);
    for (uint32_t round = ctx->nr; round > 1; --round)
    {
        InvShiftRows(s);
        InvSubBytes(s);
        AddRoundKey(s, ctx->roundKeys, round - 1u);
        InvMixColumns(s);
    }
    InvShiftRows(s);
    InvSubBytes(s);
    AddRoundKey(s, ctx->roundKeys, 0);
    StoreState(static_cast<uint8_t*>(out16), s);
}

//------------------------------------------------------------------------------
// il2cpp_aes_encrypt_cbc -- CBC + PKCS7 (SP 800-38A section 6.2 + RFC 5652 section 6.3).
// Output length == len + 16 (the padding block always exists).
//------------------------------------------------------------------------------
void il2cpp_aes_encrypt_cbc(const void* key, int32_t keyLen, const void* iv,
                            const void* data, int32_t len, void* out)
{
    if (iv == nullptr)
    {
        il2cpp_throw_argument_null_exception("iv");
    }
    if (len > 0 && data == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    if (len > 0 && out == nullptr)
    {
        il2cpp_throw_argument_null_exception("out");
    }

    Il2CppAesContext ctx;
    il2cpp_aes_create(&ctx, key, keyLen);

    const uint8_t* src = static_cast<const uint8_t*>(data);
    uint8_t* dst = static_cast<uint8_t*>(out);
    const uint8_t* ivb = static_cast<const uint8_t*>(iv);

    uint8_t prev[16];
    uint8_t block[16];
    std::memcpy(prev, ivb, 16);

    // Whole input blocks only -- the tail below (0..15 bytes) belongs to
    // the padding block, never to a short standalone block.
    const int32_t whole = len & ~15;
    for (int32_t i = 0; i < whole; i += 16)
    {
        for (uint32_t j = 0; j < 16; ++j)
        {
            block[j] = src[i + j] ^ prev[j];
        }
        il2cpp_aes_encrypt_block(&ctx, block, dst + i);
        std::memcpy(prev, dst + i, 16);
    }

    // PKCS7 padding block (always present): the remaining 'rem' plaintext
    // bytes followed by (16 - rem) pad bytes of value (16 - rem); for
    // block-aligned input this is a full block of 0x10. Total output
    // length == whole + 16 == len + padLen (1..16 pad bytes).
    const uint32_t rem = static_cast<uint32_t>(len & 15);
    const uint32_t padLen = 16u - rem;
    for (uint32_t j = 0; j < rem; ++j)
    {
        block[j] = src[whole + j] ^ prev[j];
    }
    for (uint32_t j = rem; j < 16; ++j)
    {
        block[j] = static_cast<uint8_t>(padLen) ^ prev[j];
    }
    il2cpp_aes_encrypt_block(&ctx, block, dst + whole);
}

//------------------------------------------------------------------------------
// il2cpp_aes_decrypt_cbc -- CBC decrypt + PKCS7 validation/stripping.
// Returns the plaintext length. The last block is decrypted into a local
// buffer so the padding is validated BEFORE the final plaintext bytes are
// committed to 'out' (verify-then-mutate, D8): on invalid padding the
// exception is thrown and the caller's buffer is never given pad bytes.
//------------------------------------------------------------------------------
int32_t il2cpp_aes_decrypt_cbc(const void* key, int32_t keyLen, const void* iv,
                               const void* data, int32_t len, void* out)
{
    if (iv == nullptr)
    {
        il2cpp_throw_argument_null_exception("iv");
    }
    if (len > 0 && data == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    if (len > 0 && out == nullptr)
    {
        il2cpp_throw_argument_null_exception("out");
    }
    if (len < 0 || (len % 16) != 0)
    {
        // net48 TransformFinalBlock semantics (live-probe verified message):
        // a non-block-multiple ciphertext throws CryptographicException.
        il2cpp_throw_new(kCryptoChain, "The input data is not a complete block.");
    }
    if (len == 0)
    {
        // net48 TransformFinalBlock accepts a 0-length decrypt input and
        // returns an empty plaintext (verified against a live net48 probe) --
        // no padding to validate, no block to process.
        return 0;
    }

    Il2CppAesContext ctx;
    il2cpp_aes_create(&ctx, key, keyLen);

    const uint8_t* src = static_cast<const uint8_t*>(data);
    uint8_t* dst = static_cast<uint8_t*>(out);
    const uint8_t* ivb = static_cast<const uint8_t*>(iv);

    uint8_t prev[16];
    std::memcpy(prev, ivb, 16);

    const int32_t blockCount = len / 16;
    for (int32_t i = 0; i < blockCount - 1; ++i)
    {
        uint8_t block[16];
        il2cpp_aes_decrypt_block(&ctx, src + i * 16, block);
        for (uint32_t j = 0; j < 16; ++j)
        {
            dst[i * 16 + j] = block[j] ^ prev[j];
        }
        std::memcpy(prev, src + i * 16, 16);
    }

    // Last block: decrypt locally, validate the pad, then commit.
    uint8_t last[16];
    il2cpp_aes_decrypt_block(&ctx, src + len - 16, last);
    for (uint32_t j = 0; j < 16; ++j)
    {
        last[j] ^= prev[j];
    }
    const uint32_t padLen = last[15];
    if (padLen < 1u || padLen > 16u)
    {
        il2cpp_throw_new(kCryptoChain, "Padding is invalid and cannot be removed.");
    }
    for (uint32_t j = 0; j < padLen; ++j)
    {
        if (last[15u - j] != padLen)
        {
            il2cpp_throw_new(kCryptoChain, "Padding is invalid and cannot be removed.");
        }
    }
    std::memcpy(dst + len - 16, last, 16u - padLen);
    return len - static_cast<int32_t>(padLen);
}

}} // namespace il2cpp::runtime
