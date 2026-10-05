#pragma once
//------------------------------------------------------------------------------
// hmac.h -- HMAC (RFC 2104 / FIPS 198-1) keyed-hash runtime support.
//
// Pure C++ computation -- no managed object / icall ABI involved: the HMAC
// context is a plain struct, so the helpers can back BCL keyed hashing
// (e.g. System.Security.Cryptography.HMACSHA256) later without object
// interop.
//
// SEMANTIC BASELINE: RFC 2104.
//   * HMAC(K, m) = H((K' xor opad) || H((K' xor ipad) || m)), where K' is K
//     itself when |K| <= B and H(K) otherwise (key longer than the block
//     size is compressed first -- the .NET KeyedHashAlgorithm contract).
//     B is the underlying hash's block size (64 for SHA-256).
//   * The underlying hash is passed in as a descriptor (block/digest size +
//     create/update/final function pointers): one HMAC core serves every
//     hash the runtime provides (C6 DRY -- no per-algorithm HMAC copies).
//   * Streaming contract: create -> (update)* -> final. final() outputs the
//     underlying hash's digest size in bytes. Call create() to restart.
//   * update() with len == 0 is a no-op. The context is reentrant
//     per-context: distinct contexts share no state.
//------------------------------------------------------------------------------

#include "sha256.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp { namespace runtime {

// Descriptor for one underlying hash function. All functions take the hash's
// own streaming context as an opaque void* (the caller never touches it).
struct Il2CppHmacHash
{
    size_t blockSize;  // B in RFC 2104 terms (e.g. 64 for SHA-256)
    size_t digestSize; // output size in bytes (e.g. 32 for SHA-256)
    void (*create)(void* ctx);
    void (*update)(void* ctx, const void* data, size_t len);
    void (*final)(void* ctx, uint8_t* out);
};

// SHA-256 / SHA-1 / MD5 descriptors for use with il2cpp_hmac_create (each
// backs the matching .NET KeyedHashAlgorithm: HMACSHA256 / HMACSHA1 /
// HMACMD5). One HMAC core, three hash descriptors -- no per-algorithm
// copies (C6 DRY).
extern const Il2CppHmacHash kIl2CppHmacSha256;
extern const Il2CppHmacHash kIl2CppHmacSha1;
extern const Il2CppHmacHash kIl2CppHmacMd5;

// Streaming HMAC context. Fully owned by the caller (stack or heap);
// innerCtx is the underlying hash's streaming context storage -- sized for
// the largest hash context the runtime currently has (SHA-384/512 = 216
// bytes); a descriptor whose context exceeds kIl2CppHmacCtxCapacity will
// overflow and must not be used.
const size_t kIl2CppHmacCtxCapacity = 256;

struct Il2CppHmacCtx
{
    const Il2CppHmacHash* hash;      // underlying algorithm descriptor
    uint8_t outerKey[128];           // (K' xor opad), blockSize bytes used
    alignas(8) uint8_t innerCtx[kIl2CppHmacCtxCapacity]; // inner hash ctx
};

// Initialize an HMAC context from 'key' (RFC 2104: derive K' = H(K) when
// keyLen > blockSize, zero-pad to the block size, XOR with ipad/opad). The
// inner hash is primed with (K' xor ipad) so update() can be called next.
// Any key length is legal (including 0 and > blockSize).
void il2cpp_hmac_create(Il2CppHmacCtx* ctx, const Il2CppHmacHash* hash,
                        const void* key, size_t keyLen);

// Feed 'len' bytes of data into the inner hash. Null data with len == 0 is
// fine. May be called any number of times between create() and final().
void il2cpp_hmac_update(Il2CppHmacCtx* ctx, const void* data, size_t len);

// Complete the MAC: finish the inner hash, then H((K' xor opad) || inner
// digest). Writes hash->digestSize bytes into 'out' (must hold at least
// that many). The context is left consumed; call create() to restart.
void il2cpp_hmac_final(Il2CppHmacCtx* ctx, uint8_t* out);

// One-shot convenience: HMAC-SHA-256 of 'data'/'len' under 'key'/'keyLen'
// into 'out' (32 bytes). Equals create + update + final on a private
// context.
void il2cpp_hmac_sha256(const void* key, size_t keyLen,
                        const void* data, size_t dataLen, uint8_t out[32]);

// One-shot convenience: HMAC-SHA-1 of 'data'/'len' under 'key'/'keyLen'
// into 'out' (20 bytes).
void il2cpp_hmac_sha1(const void* key, size_t keyLen,
                      const void* data, size_t dataLen, uint8_t out[20]);

// One-shot convenience: HMAC-MD5 of 'data'/'len' under 'key'/'keyLen'
// into 'out' (16 bytes).
void il2cpp_hmac_md5(const void* key, size_t keyLen,
                     const void* data, size_t dataLen, uint8_t out[16]);

}} // namespace il2cpp::runtime
