#include "md5.h"
#include "il2cpp_exception_ext.h"

#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// RFC 1321 MD5 core.
//
// Constants: the 64-entry sine table K[i] = floor(2^32 * |sin(i+1)|),
// computed with the standard double-rounding expression so the values are
// bit-exact on every compiler.
//------------------------------------------------------------------------------

uint32_t RotateLeft(uint32_t value, int32_t shift)
{
    return (value << shift) | (value >> (32 - shift));
}

uint32_t F(uint32_t x, uint32_t y, uint32_t z) { return (x & y) | (~x & z); }
uint32_t G(uint32_t x, uint32_t y, uint32_t z) { return (x & z) | (y & ~z); }
uint32_t H(uint32_t x, uint32_t y, uint32_t z) { return x ^ y ^ z; }
uint32_t I(uint32_t x, uint32_t y, uint32_t z) { return y ^ (x | ~z); }

// One 64-byte block transform (RFC 1321 §3.4).
void Transform(uint32_t state[4], const uint8_t block[64])
{
    static const uint32_t K[64] = {
        0xd76aa478u, 0xe8c7b756u, 0x242070dbu, 0xc1bdceeeu,
        0xf57c0fafu, 0x4787c62au, 0xa8304613u, 0xfd469501u,
        0x698098d8u, 0x8b44f7afu, 0xffff5bb1u, 0x895cd7beu,
        0x6b901122u, 0xfd987193u, 0xa679438eu, 0x49b40821u,
        0xf61e2562u, 0xc040b340u, 0x265e5a51u, 0xe9b6c7aau,
        0xd62f105du, 0x02441453u, 0xd8a1e681u, 0xe7d3fbc8u,
        0x21e1cde6u, 0xc33707d6u, 0xf4d50d87u, 0x455a14edu,
        0xa9e3e905u, 0xfcefa3f8u, 0x676f02d9u, 0x8d2a4c8au,
        0xfffa3942u, 0x8771f681u, 0x6d9d6122u, 0xfde5380cu,
        0xa4beea44u, 0x4bdecfa9u, 0xf6bb4b60u, 0xbebfbc70u,
        0x289b7ec6u, 0xeaa127fau, 0xd4ef3085u, 0x04881d05u,
        0xd9d4d039u, 0xe6db99e5u, 0x1fa27cf8u, 0xc4ac5665u,
        0xf4292244u, 0x432aff97u, 0xab9423a7u, 0xfc93a039u,
        0x655b59c3u, 0x8f0ccc92u, 0xffeff47du, 0x85845dd1u,
        0x6fa87e4fu, 0xfe2ce6e0u, 0xa3014314u, 0x4e0811a1u,
        0xf7537e82u, 0xbd3af235u, 0x2ad7d2bbu, 0xeb86d391u,
    };

    static const uint8_t S[64] = {
         7, 12, 17, 22,  7, 12, 17, 22,  7, 12, 17, 22,  7, 12, 17, 22,
         5,  9, 14, 20,  5,  9, 14, 20,  5,  9, 14, 20,  5,  9, 14, 20,
         4, 11, 16, 23,  4, 11, 16, 23,  4, 11, 16, 23,  4, 11, 16, 23,
         6, 10, 15, 21,  6, 10, 15, 21,  6, 10, 15, 21,  6, 10, 15, 21,
    };

    uint32_t m[16];
    for (int32_t i = 0; i < 16; ++i)
    {
        m[i] = static_cast<uint32_t>(block[i * 4])
             | (static_cast<uint32_t>(block[i * 4 + 1]) << 8)
             | (static_cast<uint32_t>(block[i * 4 + 2]) << 16)
             | (static_cast<uint32_t>(block[i * 4 + 3]) << 24);
    }

    uint32_t a = state[0];
    uint32_t b = state[1];
    uint32_t c = state[2];
    uint32_t d = state[3];

    for (int32_t i = 0; i < 64; ++i)
    {
        uint32_t f;
        int32_t g;
        if (i < 16)
        {
            f = F(b, c, d);
            g = i;
        }
        else if (i < 32)
        {
            f = G(b, c, d);
            g = (5 * i + 1) & 15;
        }
        else if (i < 48)
        {
            f = H(b, c, d);
            g = (3 * i + 5) & 15;
        }
        else
        {
            f = I(b, c, d);
            g = (7 * i) & 15;
        }

        uint32_t next = d;
        d = c;
        c = b;
        b = b + RotateLeft(a + f + K[i] + m[g], S[i]);
        a = next;
    }

    state[0] += a;
    state[1] += b;
    state[2] += c;
    state[3] += d;
}

// Process whole 64-byte blocks from 'data'; returns the first byte of the
// trailing partial block (or 'end' if no partial data remains).
const uint8_t* ConsumeBlocks(Il2CppMd5Context* ctx, const uint8_t* data, size_t length)
{
    const uint8_t* p = data;
    const uint8_t* end = data + length;
    while (end - p >= 64)
    {
        Transform(ctx->h, p);
        p += 64;
    }
    return p;
}

} // namespace

//------------------------------------------------------------------------------
// Three-phase interface (create / update / final).
//------------------------------------------------------------------------------

Il2CppMd5Context il2cpp_md5_create()
{
    Il2CppMd5Context ctx{};
    ctx.h[0] = 0x67452301u;
    ctx.h[1] = 0xefcdab89u;
    ctx.h[2] = 0x98badcfeu;
    ctx.h[3] = 0x10325476u;
    return ctx;
}

void il2cpp_md5_update(Il2CppMd5Context* ctx, const void* data, size_t length)
{
    if (length == 0)
    {
        return;
    }
    const uint8_t* bytes = static_cast<const uint8_t*>(data);

    ctx->bitCount += static_cast<uint64_t>(length) * 8;

    if (ctx->bufferLength > 0)
    {
        const size_t need = 64 - ctx->bufferLength;
        const size_t take = (length < need) ? length : need;
        std::memcpy(ctx->buffer + ctx->bufferLength, bytes, take);
        ctx->bufferLength += take;
        bytes += take;
        length -= take;
        if (ctx->bufferLength == 64)
        {
            Transform(ctx->h, ctx->buffer);
            ctx->bufferLength = 0;
        }
    }

    const uint8_t* p = ConsumeBlocks(ctx, bytes, length);
    const size_t remaining = static_cast<size_t>(bytes + length - p);
    if (remaining > 0)
    {
        std::memcpy(ctx->buffer, p, remaining);
        ctx->bufferLength = remaining;
    }
}

void il2cpp_md5_final(Il2CppMd5Context* ctx, uint8_t out16[16])
{
    // Padding per RFC 1321 §3.1: 0x80, zeros, then the 64-bit bit count
    // (little-endian). The bit count has already been accumulated across
    // all updates, so no arithmetic is needed here.
    const uint64_t bitCount = ctx->bitCount;

    static const uint8_t padZero = 0x00;
    const uint8_t padOne = 0x80;

    uint8_t byte = padOne;
    il2cpp_md5_update(ctx, &byte, 1);
    while (ctx->bufferLength != 56)
    {
        il2cpp_md5_update(ctx, &padZero, 1);
    }

    uint8_t countBytes[8];
    for (int32_t i = 0; i < 8; ++i)
    {
        countBytes[i] = static_cast<uint8_t>(bitCount >> (8 * i));
    }
    il2cpp_md5_update(ctx, countBytes, 8);

    for (int32_t i = 0; i < 4; ++i)
    {
        out16[i * 4]     = static_cast<uint8_t>(ctx->h[i]);
        out16[i * 4 + 1] = static_cast<uint8_t>(ctx->h[i] >> 8);
        out16[i * 4 + 2] = static_cast<uint8_t>(ctx->h[i] >> 16);
        out16[i * 4 + 3] = static_cast<uint8_t>(ctx->h[i] >> 24);
    }

    *ctx = il2cpp_md5_create();
}

//------------------------------------------------------------------------------
// One-shot byte[] form.
//------------------------------------------------------------------------------

Il2CppArray* il2cpp_md5_hash_bytes(const void* data, int32_t length)
{
    if (data == nullptr && length > 0)
    {
        il2cpp_throw_argument_null_exception("data");
    }

    Il2CppMd5Context ctx = il2cpp_md5_create();
    if (length > 0)
    {
        il2cpp_md5_update(&ctx, data, static_cast<size_t>(length));
    }

    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, 16);
    il2cpp_md5_final(&ctx, reinterpret_cast<uint8_t*>(result->data));
    return result;
}

Il2CppArray* il2cpp_md5_hash_bytes(Il2CppArray* data)
{
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    return il2cpp_md5_hash_bytes(data->data, static_cast<int32_t>(data->length));
}

}} // namespace il2cpp::runtime
