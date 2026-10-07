#pragma once
//------------------------------------------------------------------------------
// aes.h -- AES block cipher + CBC/PKCS7 mode runtime support (FIPS 197).
//
// Pure C++ computation -- no managed object / icall ABI involved: the key
// schedule is a plain struct, so the helpers can back BCL symmetric
// encryption (e.g. System.Security.Cryptography.Aes) later without object
// interop (the same pattern sha256.h uses for the hash family).
//
// SOURCE OF ALGORITHM: FIPS 197 (Advanced Encryption Standard) for the block
// cipher and key expansion; NIST SP 800-38A for the CBC mode chaining; PKCS #7
// (RFC 5652 section 6.3, "padding to the block size") for the padding. The project's
// reference tree was checked for a .NET AES implementation first:
// reference/netcore sparse checkout does NOT contain
// System.Security.Cryptography, so per the no-external-dependency rule
// (docs/design/crypto-domain.md section 2.2) the public standard algorithm is
// implemented directly. AES output is fully specified by the standard, so
// there is no behavioural divergence to reconcile with net48.
//
// SEMANTIC BASELINE: FIPS 197 + SP 800-38A + net48 Aes defaults.
//   * Key sizes: 128-bit (16 bytes) and 256-bit (32 bytes) only. AES-192
//     is out of scope (docs/design/crypto-domain.md section 1.2) -> the 24-byte
//     key size throws NotSupportedException (loud). Any other size throws
//     CryptographicException (net48 SymmetricAlgorithm.Key setter semantics,
//     live-probe verified -- invalid key size throws Crypto, not Argument).
//   * Block size is fixed at 16 bytes.
//   * Mode: CBC encrypt/decrypt with PKCS7 padding (net48 default).
//     - encrypt: output length == 'len' rounded up to a block boundary
//       ('len' + padLen, padLen in 1..16 -- block-aligned inputs gain a
//       full 0x10 pad block).
//     - decrypt: input must be a multiple of 16 (0 is legal and yields an
//       empty plaintext -- net48 TransformFinalBlock returns an empty array
//       for an empty ciphertext, no exception); any other non-multiple or
//       negative length throws CryptographicException (net48 message "The
//       input data is not a complete block."); the PKCS7 pad is validated
//       (each of the last padLen bytes == padLen, padLen in 1..16); invalid
//       padding -> CryptographicException ("Padding is invalid and cannot be
//       removed."). Returns the plaintext length (input length minus the pad).
//   * The context (il2cpp_aes_create) is a plain struct, fully owned by the
//     caller (stack or heap); no allocation, no dynamic state (D9/D13).
//   * All functions are exception-free except the documented parameter and
//     padding errors, which throw catchable managed exceptions through the
//     il2cpp::DotNetException carrier (il2cpp_exception.h).
//
// TEST VECTORS (verified in tests/runtime/test_aes.cpp):
//   SP 800-38A F.1.1/F.1.3 (ECB single-block via the block API) and
//   F.2.1/F.2.5 (CBC, chained manually over the block API -- the mode
//   layer's padding keeps the raw CBC vectors out of the padded entry
//   points, so the F.2.x ciphertexts are asserted with explicit chaining).
//------------------------------------------------------------------------------

#include <cstddef>
#include <cstdint>

namespace il2cpp { namespace runtime {

// AES context: the expanded key schedule. Create with il2cpp_aes_create
// before encrypting/decrypting; the struct is opaque to callers.
struct Il2CppAesContext
{
    uint32_t roundKeys[60]; // 4 * (Nr + 1) words: 44 for AES-128, 60 for AES-256
    uint32_t nk;            // key length in words (4 or 8)
    uint32_t nr;            // number of rounds (10 for AES-128, 14 for AES-256)
};

// Initialize a context for the key at 'key'/'keyLen' (16 or 32 bytes).
// Null key -> ArgumentNullException; keyLen == 24 (AES-192) ->
// NotSupportedException (out of scope); any other keyLen ->
// CryptographicException (net48 SymmetricAlgorithm.Key setter semantics).
void il2cpp_aes_create(Il2CppAesContext* ctx, const void* key, int32_t keyLen);

// Encrypt/decrypt a single 16-byte block (FIPS 197). 'in16'/'out16' must
// hold 16 bytes each; may be the same buffer. No padding, no chaining --
// this is the raw block cipher (SP 800-38A ECB vectors test exactly this).
void il2cpp_aes_encrypt_block(const Il2CppAesContext* ctx, const void* in16, void* out16);
void il2cpp_aes_decrypt_block(const Il2CppAesContext* ctx, const void* in16, void* out16);

// CBC-encrypt 'len' bytes at 'data' with PKCS7 padding into 'out' (which
// must hold at least 'len' rounded up to a block boundary, i.e. len +
// (16 - len % 16) bytes). 'iv' must hold 16 bytes. Null iv/data/out with
// non-zero sizes -> ArgumentNullException.
void il2cpp_aes_encrypt_cbc(const void* key, int32_t keyLen, const void* iv,
                            const void* data, int32_t len, void* out);

// CBC-decrypt 'len' bytes at 'data' (a multiple of 16; 0 is legal and
// yields an empty plaintext, any other non-multiple or negative length
// throws CryptographicException with net48's "The input data is not a
// complete block." message), validate and strip the PKCS7 padding, and
// write the plaintext into 'out' (which must hold at least len bytes).
// Returns the plaintext length (len minus the pad). Invalid padding ->
// CryptographicException ("Padding is invalid and cannot be removed.").
int32_t il2cpp_aes_decrypt_cbc(const void* key, int32_t keyLen, const void* iv,
                               const void* data, int32_t len, void* out);

}} // namespace il2cpp::runtime
