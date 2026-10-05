//------------------------------------------------------------------------------
// hmac.cpp -- HMAC (RFC 2104 / FIPS 198-1) implementation (see hmac.h).
//
// HMAC(K, m) = H((K' xor opad) || H((K' xor ipad) || m)) with
// K' = K for |K| <= B and K' = H(K) otherwise. The underlying hash arrives
// as an Il2CppHmacHash descriptor, so this one core serves SHA-256, SHA-1
// and MD5 (the .NET KeyedHashAlgorithm set) -- no per-algorithm copies.
//
// Streaming structure (classic two-layer construction):
//   create()  primes the INNER hash with (K' xor ipad) and caches
//             (K' xor opad) in outerKey.
//   update()  feeds the inner hash only.
//   final()   completes the inner digest, then rebuilds a fresh OUTER hash
//             context (create + update(outerKey) + update(innerDigest)) and
//             serializes its digest. Nothing but outerKey survives between
//             create and final, so contexts share no mutable state.
//------------------------------------------------------------------------------

#include "hmac.h"

#include "md5.h"
#include "sha1.h"

#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

void HmacSha256Create(void* ctx)
{
    il2cpp_sha256_create(static_cast<Il2CppSha256Ctx*>(ctx));
}

void HmacSha256Update(void* ctx, const void* data, size_t len)
{
    il2cpp_sha256_update(static_cast<Il2CppSha256Ctx*>(ctx), data, len);
}

void HmacSha256Final(void* ctx, uint8_t* out)
{
    il2cpp_sha256_final(static_cast<Il2CppSha256Ctx*>(ctx), out);
}

// SHA-1 lives in namespace il2cpp (not il2cpp::runtime) -- sha1.h's
// original namespace -- so qualify every call explicitly.
void HmacSha1Create(void* ctx)
{
    ::il2cpp::il2cpp_sha1_create(static_cast<Il2CppSha1Context*>(ctx));
}

void HmacSha1Update(void* ctx, const void* data, size_t len)
{
    ::il2cpp::il2cpp_sha1_update(static_cast<Il2CppSha1Context*>(ctx), data, len);
}

void HmacSha1Final(void* ctx, uint8_t* out)
{
    ::il2cpp::il2cpp_sha1_final(static_cast<Il2CppSha1Context*>(ctx), out);
}

// MD5's create() returns a fresh context by value (md5.h) -- materialize it
// into the caller's storage.
void HmacMd5Create(void* ctx)
{
    *static_cast<Il2CppMd5Context*>(ctx) = il2cpp_md5_create();
}

void HmacMd5Update(void* ctx, const void* data, size_t len)
{
    il2cpp_md5_update(static_cast<Il2CppMd5Context*>(ctx), data, len);
}

void HmacMd5Final(void* ctx, uint8_t* out)
{
    il2cpp_md5_final(static_cast<Il2CppMd5Context*>(ctx), out);
}

} // namespace

const Il2CppHmacHash kIl2CppHmacSha256 = {
    64, // SHA-256 block size B
    32, // SHA-256 digest size
    &HmacSha256Create,
    &HmacSha256Update,
    &HmacSha256Final,
};

const Il2CppHmacHash kIl2CppHmacSha1 = {
    64, // SHA-1 block size B
    20, // SHA-1 digest size
    &HmacSha1Create,
    &HmacSha1Update,
    &HmacSha1Final,
};

const Il2CppHmacHash kIl2CppHmacMd5 = {
    64, // MD5 block size B
    16, // MD5 digest size
    &HmacMd5Create,
    &HmacMd5Update,
    &HmacMd5Final,
};

void il2cpp_hmac_create(Il2CppHmacCtx* ctx, const Il2CppHmacHash* hash,
                        const void* key, size_t keyLen)
{
    ctx->hash = hash;

    uint8_t keyBuf[128]; // padded K' (blockSize <= 128 for all current hashes)
    if (keyLen > hash->blockSize)
    {
        // K' = H(K): compress an over-long key down to the digest size.
        hash->create(ctx->innerCtx);
        hash->update(ctx->innerCtx, key, keyLen);
        hash->final(ctx->innerCtx, keyBuf);
        std::memset(keyBuf + hash->digestSize, 0, hash->blockSize - hash->digestSize);
    }
    else
    {
        std::memset(keyBuf, 0, hash->blockSize);
        std::memcpy(keyBuf, key, keyLen);
    }

    // Prime the inner hash with (K' xor ipad); cache (K' xor opad).
    hash->create(ctx->innerCtx);
    for (size_t i = 0; i < hash->blockSize; ++i)
        keyBuf[i] ^= 0x36u;
    hash->update(ctx->innerCtx, keyBuf, hash->blockSize);
    for (size_t i = 0; i < hash->blockSize; ++i)
        ctx->outerKey[i] = keyBuf[i] ^ 0x36u ^ 0x5cu;
}

void il2cpp_hmac_update(Il2CppHmacCtx* ctx, const void* data, size_t len)
{
    ctx->hash->update(ctx->innerCtx, data, len);
}

void il2cpp_hmac_final(Il2CppHmacCtx* ctx, uint8_t* out)
{
    uint8_t innerDigest[64]; // digestSize <= 64 for all current hashes
    ctx->hash->final(ctx->innerCtx, innerDigest);

    // Rebuild a fresh outer hash: H((K' xor opad) || innerDigest).
    uint8_t outerCtx[kIl2CppHmacCtxCapacity];
    ctx->hash->create(outerCtx);
    ctx->hash->update(outerCtx, ctx->outerKey, ctx->hash->blockSize);
    ctx->hash->update(outerCtx, innerDigest, ctx->hash->digestSize);
    ctx->hash->final(outerCtx, out);
}

void il2cpp_hmac_sha256(const void* key, size_t keyLen,
                        const void* data, size_t dataLen, uint8_t out[32])
{
    Il2CppHmacCtx ctx;
    il2cpp_hmac_create(&ctx, &kIl2CppHmacSha256, key, keyLen);
    il2cpp_hmac_update(&ctx, data, dataLen);
    il2cpp_hmac_final(&ctx, out);
}

void il2cpp_hmac_sha1(const void* key, size_t keyLen,
                      const void* data, size_t dataLen, uint8_t out[20])
{
    Il2CppHmacCtx ctx;
    il2cpp_hmac_create(&ctx, &kIl2CppHmacSha1, key, keyLen);
    il2cpp_hmac_update(&ctx, data, dataLen);
    il2cpp_hmac_final(&ctx, out);
}

void il2cpp_hmac_md5(const void* key, size_t keyLen,
                     const void* data, size_t dataLen, uint8_t out[16])
{
    Il2CppHmacCtx ctx;
    il2cpp_hmac_create(&ctx, &kIl2CppHmacMd5, key, keyLen);
    il2cpp_hmac_update(&ctx, data, dataLen);
    il2cpp_hmac_final(&ctx, out);
}

}} // namespace il2cpp::runtime
