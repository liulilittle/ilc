//------------------------------------------------------------------------------
// sha1.cpp -- SHA-1 message digest runtime support (see sha1.h).
//
// FIPS 180-4 implementation notes:
//   * Message words and the final digest are BIG-ENDIAN (3.2: the message is
//     read most-significant byte first) -- no platform-endianness branching.
//   * The 80 rounds split into four groups, each with its own round function
//     f and constant K (4.1.1):
//       0..19  f = Ch(b,c,d) = (b & c) | (~b & d)        K = 0x5A827999
//       20..39 f = Parity    = b ^ c ^ d                 K = 0x6ED9EBA1
//       40..59 f = Maj(b,c,d) = (b & c)|(b & d)|(c & d)  K = 0x8F1BBCDC
//       60..79 f = Parity    = b ^ c ^ d                 K = 0xCA62C1D6
//   * The length counter counts MESSAGE bytes only -- the padding (0x80,
//     zeros, bit-length) is appended by final() directly into a local block
//     and never routed through update()/count.
//------------------------------------------------------------------------------

#include "sha1.h"
#include "il2cpp_exception_ext.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

// Rotate 'x' left by 'n' bits. SHA-1 only rotates by 1, 5 and 30, so no
// shift-by-0/32 edge case exists.
inline uint32_t Rotl(uint32_t x, int n)
{
    return (x << n) | (x >> (32 - n));
}

// Big-endian word load (FIPS 180-4 3.2).
uint32_t LoadBe32(const uint8_t* p)
{
    return (static_cast<uint32_t>(p[0]) << 24) |
           (static_cast<uint32_t>(p[1]) << 16) |
           (static_cast<uint32_t>(p[2]) << 8) |
           static_cast<uint32_t>(p[3]);
}

// Big-endian word store.
void StoreBe32(uint8_t* p, uint32_t v)
{
    p[0] = static_cast<uint8_t>(v >> 24);
    p[1] = static_cast<uint8_t>(v >> 16);
    p[2] = static_cast<uint8_t>(v >> 8);
    p[3] = static_cast<uint8_t>(v);
}

// Round function for round 't' (FIPS 180-4 4.1.1).
uint32_t RoundF(uint32_t t, uint32_t b, uint32_t c, uint32_t d)
{
    if (t < 20)
    {
        return (b & c) | (~b & d);
    }
    if (t < 40)
    {
        return b ^ c ^ d;
    }
    if (t < 60)
    {
        return (b & c) | (b & d) | (c & d);
    }
    return b ^ c ^ d;
}

// Round constant for round 't' (FIPS 180-4 4.1.1).
uint32_t RoundK(uint32_t t)
{
    if (t < 20)
    {
        return 0x5A827999u;
    }
    if (t < 40)
    {
        return 0x6ED9EBA1u;
    }
    if (t < 60)
    {
        return 0x8F1BBCDCu;
    }
    return 0xCA62C1D6u;
}

// Compress one 64-byte block into 'state' (FIPS 180-4 6.1.2 steps 1-4).
void Compress(uint32_t state[5], const uint8_t block[64])
{
    uint32_t w[80];

    for (int i = 0; i < 16; ++i)
    {
        w[i] = LoadBe32(block + 4 * i);
    }
    for (int i = 16; i < 80; ++i)
    {
        w[i] = Rotl(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
    }

    uint32_t a = state[0];
    uint32_t b = state[1];
    uint32_t c = state[2];
    uint32_t d = state[3];
    uint32_t e = state[4];

    for (uint32_t t = 0; t < 80; ++t)
    {
        uint32_t temp = Rotl(a, 5) + RoundF(t, b, c, d) + e + w[t] + RoundK(t);
        e = d;
        d = c;
        c = Rotl(b, 30);
        b = a;
        a = temp;
    }

    state[0] += a;
    state[1] += b;
    state[2] += c;
    state[3] += d;
    state[4] += e;
}

} // anonymous namespace

void il2cpp_sha1_create(Il2CppSha1Context* ctx)
{
    if (ctx == nullptr)
    {
        return;
    }

    ctx->state[0] = 0x67452301u;
    ctx->state[1] = 0xEFCDAB89u;
    ctx->state[2] = 0x98BADCFEu;
    ctx->state[3] = 0x10325476u;
    ctx->state[4] = 0xC3D2E1F0u;
    ctx->count = 0;
    ctx->bufferLen = 0;
}

void il2cpp_sha1_update(Il2CppSha1Context* ctx, const void* data, size_t len)
{
    if (ctx == nullptr)
    {
        return;
    }
    if (data == nullptr || len == 0)
    {
        return;
    }

    ctx->count += len;
    const uint8_t* p = static_cast<const uint8_t*>(data);

    // Top up the pending block first, then whole blocks straight from the
    // caller's buffer, then keep the tail for the next call.
    if (ctx->bufferLen > 0)
    {
        size_t take = 64 - ctx->bufferLen;
        if (take > len)
        {
            take = len;
        }
        std::memcpy(ctx->buffer + ctx->bufferLen, p, take);
        ctx->bufferLen += take;
        p += take;
        len -= take;
        if (ctx->bufferLen == 64)
        {
            Compress(ctx->state, ctx->buffer);
            ctx->bufferLen = 0;
        }
    }

    while (len >= 64)
    {
        Compress(ctx->state, p);
        p += 64;
        len -= 64;
    }

    if (len > 0)
    {
        std::memcpy(ctx->buffer, p, len);
        ctx->bufferLen = len;
    }
}

void il2cpp_sha1_final(Il2CppSha1Context* ctx, uint8_t digest[20])
{
    if (ctx == nullptr)
    {
        return;
    }
    if (digest == nullptr)
    {
        return;
    }

    // FIPS 180-4 5.1.1: append a single 1 bit (0x80), pad zeros to the
    // 448-bit (56-byte) boundary, then the 64-bit big-endian bit length of
    // the ORIGINAL message. The padding is built in a local block -- it is
    // never routed through update(), so 'count' stays message-only. A full
    // pending block forces a second padding block.
    const uint64_t bitLen = ctx->count * 8;

    uint8_t block[64];
    size_t pos = ctx->bufferLen;
    std::memcpy(block, ctx->buffer, pos);
    block[pos] = 0x80;
    ++pos;

    if (pos <= 56)
    {
        std::memset(block + pos, 0, 56 - pos);
    }
    else
    {
        std::memset(block + pos, 0, 64 - pos);
        Compress(ctx->state, block);
        std::memset(block, 0, 56);
    }
    // The 64-bit big-endian bit length always occupies the LAST 8 bytes of
    // the final padding block (FIPS 180-4 5.1.1) -- NOT block[pos..].
    for (int i = 0; i < 8; ++i)
    {
        block[56 + i] = static_cast<uint8_t>(bitLen >> (56 - 8 * i));
    }
    Compress(ctx->state, block);

    for (int i = 0; i < 5; ++i)
    {
        StoreBe32(digest + 4 * i, ctx->state[i]);
    }

    // Finalized. create() must be called before the context is reused.
}

void il2cpp_sha1_hash_bytes(const void* data, size_t len, uint8_t digest[20])
{
    if (digest == nullptr)
    {
        return;
    }

    Il2CppSha1Context ctx;
    il2cpp_sha1_create(&ctx);
    if (data != nullptr && len > 0)
    {
        il2cpp_sha1_update(&ctx, data, len);
    }
    il2cpp_sha1_final(&ctx, digest);
}

Il2CppArray* il2cpp_sha1_hash_bytes(Il2CppArray* data)
{
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, 20);
    il2cpp_sha1_hash_bytes(data->data, static_cast<size_t>(data->length),
        reinterpret_cast<uint8_t*>(result->data));
    return result;
}

} // namespace il2cpp
