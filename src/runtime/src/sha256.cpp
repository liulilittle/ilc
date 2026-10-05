//------------------------------------------------------------------------------
// sha256.cpp -- SHA-256 (FIPS 180-4) implementation (see sha256.h).
//
// Standard NIST SHA-256: 512-bit blocks, 64 rounds over the 32-bit words of
// the message schedule. The round constants K[0..63] are the first 32 bits
// of the fractional parts of the cube roots of the first 64 primes; the
// initial hash state H0..H7 is the first 32 bits of the fractional parts of
// the square roots of the first 8 primes.
//
// All endianness handling is explicit (big-endian loads/stores via shifts),
// so the code is portable to little-endian hosts without memcpy tricks.
// The bit-length counter is 64-bit: update() accumulates len * 8 so the
// final padding carries the true message length for any feasible input.
//------------------------------------------------------------------------------

#include "sha256.h"
#include "il2cpp_exception_ext.h"

#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

const uint32_t kRoundConstants[64] = {
    0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u,
    0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
    0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u,
    0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
    0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu,
    0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
    0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u,
    0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
    0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u,
    0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
    0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u,
    0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
    0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u,
    0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
    0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u,
    0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u,
};

inline uint32_t Ror(uint32_t x, uint32_t n)
{
    return (x >> n) | (x << (32u - n));
}

// Big-endian load of a 32-bit word (explicit shifts -> endian-neutral).
uint32_t LoadBE32(const uint8_t* p)
{
    return (static_cast<uint32_t>(p[0]) << 24) |
           (static_cast<uint32_t>(p[1]) << 16) |
           (static_cast<uint32_t>(p[2]) << 8) |
           static_cast<uint32_t>(p[3]);
}

void StoreBE32(uint8_t* p, uint32_t v)
{
    p[0] = static_cast<uint8_t>(v >> 24);
    p[1] = static_cast<uint8_t>(v >> 16);
    p[2] = static_cast<uint8_t>(v >> 8);
    p[3] = static_cast<uint8_t>(v);
}

// Compress one 512-bit block into the working state h[8].
void Compress(uint32_t h[8], const uint8_t block[64])
{
    uint32_t w[64];
    for (uint32_t i = 0; i < 16; ++i)
        w[i] = LoadBE32(block + i * 4);
    for (uint32_t i = 16; i < 64; ++i)
    {
        const uint32_t sigma0 = Ror(w[i - 15], 7) ^ Ror(w[i - 15], 18) ^ (w[i - 15] >> 3);
        const uint32_t sigma1 = Ror(w[i - 2], 17) ^ Ror(w[i - 2], 19) ^ (w[i - 2] >> 10);
        w[i] = w[i - 16] + w[i - 7] + sigma0 + sigma1;
    }

    uint32_t a = h[0], b = h[1], c = h[2], d = h[3];
    uint32_t e = h[4], f = h[5], g = h[6], hh = h[7];

    for (uint32_t i = 0; i < 64; ++i)
    {
        const uint32_t bigSigma1 = Ror(e, 6) ^ Ror(e, 11) ^ Ror(e, 25);
        const uint32_t ch = (e & f) ^ (~e & g);
        const uint32_t t1 = hh + bigSigma1 + ch + kRoundConstants[i] + w[i];
        const uint32_t bigSigma0 = Ror(a, 2) ^ Ror(a, 13) ^ Ror(a, 22);
        const uint32_t maj = (a & b) ^ (a & c) ^ (b & c);
        const uint32_t t2 = bigSigma0 + maj;

        hh = g;
        g = f;
        f = e;
        e = d + t1;
        d = c;
        c = b;
        b = a;
        a = t1 + t2;
    }

    h[0] += a; h[1] += b; h[2] += c; h[3] += d;
    h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
}

} // namespace

void il2cpp_sha256_create(Il2CppSha256Ctx* ctx)
{
    if (ctx == nullptr)
    {
        return;
    }
    ctx->state[0] = 0x6a09e667u;
    ctx->state[1] = 0xbb67ae85u;
    ctx->state[2] = 0x3c6ef372u;
    ctx->state[3] = 0xa54ff53au;
    ctx->state[4] = 0x510e527fu;
    ctx->state[5] = 0x9b05688cu;
    ctx->state[6] = 0x1f83d9abu;
    ctx->state[7] = 0x5be0cd19u;
    ctx->totalBits = 0;
    ctx->buffered = 0;
}

void il2cpp_sha256_update(Il2CppSha256Ctx* ctx, const void* data, size_t len)
{
    if (ctx == nullptr)
    {
        return;
    }
    if (data == nullptr || len == 0)
    {
        return;
    }
    const uint8_t* src = static_cast<const uint8_t*>(data);
    ctx->totalBits += static_cast<uint64_t>(len) * 8u;

    // Top up a partially filled block first (keeps the tail-block invariant:
    // after each update, buffered < 64 unless a whole block was just taken).
    if (ctx->buffered != 0)
    {
        const size_t need = 64u - ctx->buffered;
        const size_t take = len < need ? len : need;
        std::memcpy(ctx->buffer + ctx->buffered, src, take);
        ctx->buffered += take;
        src += take;
        len -= take;
        if (ctx->buffered == 64u)
        {
            Compress(ctx->state, ctx->buffer);
            ctx->buffered = 0;
        }
    }

    // Whole blocks straight from the caller's buffer.
    while (len >= 64u)
    {
        Compress(ctx->state, src);
        src += 64u;
        len -= 64u;
    }

    if (len != 0)
    {
        std::memcpy(ctx->buffer, src, len);
        ctx->buffered = len;
    }
}

void il2cpp_sha256_final(Il2CppSha256Ctx* ctx, uint8_t out[32])
{
    if (ctx == nullptr || out == nullptr)
    {
        return;
    }
    const uint64_t bitLen = ctx->totalBits;
    const size_t remaining = 64u - ctx->buffered;

    if (remaining >= 9u)
    {
        // Room for 0x80 + zeros + 8-byte length in this block.
        ctx->buffer[ctx->buffered] = 0x80u;
        std::memset(ctx->buffer + ctx->buffered + 1, 0, remaining - 9u);
    }
    else
    {
        // Not enough room for the length: finish this block with 0x80 +
        // zeros, compress, then emit a fresh all-zero padding block.
        ctx->buffer[ctx->buffered] = 0x80u;
        std::memset(ctx->buffer + ctx->buffered + 1, 0, remaining - 1u);
        Compress(ctx->state, ctx->buffer);
        std::memset(ctx->buffer, 0, 56u);
    }

    // 8-byte big-endian message length in bits (last 8 bytes of the block).
    for (uint32_t i = 0; i < 8; ++i)
        ctx->buffer[56u + i] = static_cast<uint8_t>(bitLen >> (56u - i * 8u));
    Compress(ctx->state, ctx->buffer);

    for (uint32_t i = 0; i < 8; ++i)
        StoreBE32(out + i * 4u, ctx->state[i]);
}

void il2cpp_sha256_hash_bytes(const void* data, size_t len, uint8_t out[32])
{
    if (out == nullptr)
    {
        return;
    }
    Il2CppSha256Ctx ctx;
    il2cpp_sha256_create(&ctx);
    il2cpp_sha256_update(&ctx, data, len);
    il2cpp_sha256_final(&ctx, out);
}

Il2CppArray* il2cpp_sha256_hash_bytes(Il2CppArray* data)
{
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, 32);
    il2cpp_sha256_hash_bytes(data->data, static_cast<size_t>(data->length),
        reinterpret_cast<uint8_t*>(result->data));
    return result;
}

}} // namespace il2cpp::runtime
