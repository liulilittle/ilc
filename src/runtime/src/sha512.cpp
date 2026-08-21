//------------------------------------------------------------------------------
// sha512.cpp -- SHA-384/SHA-512 (FIPS 180-4) implementation (see sha512.h).
//
// Standard NIST SHA-512: 1024-bit blocks, 80 rounds over the 64-bit words of
// the message schedule. The round constants K[0..79] are the first 64 bits
// of the fractional parts of the cube roots of the first 80 primes; the
// initial hash state H0..H7 is the first 64 bits of the fractional parts of
// the square roots of the first 8 primes. SHA-384 (FIPS 180-4 5.3.4) reuses
// the exact same core with the first 64 bits of the square roots of primes
// 9..16 as its initial state and truncates the final output to 48 bytes --
// one shared context + update path, two final exits (C6 DRY).
//
// All endianness handling is explicit (big-endian loads/stores via shifts),
// so the code is portable to little-endian hosts without memcpy tricks.
// The bit-length counter is 128-bit (totalBitsHi/Lo): update() accumulates
// len * 8 so the final padding carries the true message length for any
// feasible input (FIPS 180-4 5.1.2 requires a 128-bit length field).
//------------------------------------------------------------------------------

#include "sha512.h"
#include "il2cpp_exception_ext.h"

#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

const uint64_t kRoundConstants[80] = {
    0x428a2f98d728ae22ULL, 0x7137449123ef65cdULL, 0xb5c0fbcfec4d3b2fULL, 0xe9b5dba58189dbbcULL,
    0x3956c25bf348b538ULL, 0x59f111f1b605d019ULL, 0x923f82a4af194f9bULL, 0xab1c5ed5da6d8118ULL,
    0xd807aa98a3030242ULL, 0x12835b0145706fbeULL, 0x243185be4ee4b28cULL, 0x550c7dc3d5ffb4e2ULL,
    0x72be5d74f27b896fULL, 0x80deb1fe3b1696b1ULL, 0x9bdc06a725c71235ULL, 0xc19bf174cf692694ULL,
    0xe49b69c19ef14ad2ULL, 0xefbe4786384f25e3ULL, 0x0fc19dc68b8cd5b5ULL, 0x240ca1cc77ac9c65ULL,
    0x2de92c6f592b0275ULL, 0x4a7484aa6ea6e483ULL, 0x5cb0a9dcbd41fbd4ULL, 0x76f988da831153b5ULL,
    0x983e5152ee66dfabULL, 0xa831c66d2db43210ULL, 0xb00327c898fb213fULL, 0xbf597fc7beef0ee4ULL,
    0xc6e00bf33da88fc2ULL, 0xd5a79147930aa725ULL, 0x06ca6351e003826fULL, 0x142929670a0e6e70ULL,
    0x27b70a8546d22ffcULL, 0x2e1b21385c26c926ULL, 0x4d2c6dfc5ac42aedULL, 0x53380d139d95b3dfULL,
    0x650a73548baf63deULL, 0x766a0abb3c77b2a8ULL, 0x81c2c92e47edaee6ULL, 0x92722c851482353bULL,
    0xa2bfe8a14cf10364ULL, 0xa81a664bbc423001ULL, 0xc24b8b70d0f89791ULL, 0xc76c51a30654be30ULL,
    0xd192e819d6ef5218ULL, 0xd69906245565a910ULL, 0xf40e35855771202aULL, 0x106aa07032bbd1b8ULL,
    0x19a4c116b8d2d0c8ULL, 0x1e376c085141ab53ULL, 0x2748774cdf8eeb99ULL, 0x34b0bcb5e19b48a8ULL,
    0x391c0cb3c5c95a63ULL, 0x4ed8aa4ae3418acbULL, 0x5b9cca4f7763e373ULL, 0x682e6ff3d6b2b8a3ULL,
    0x748f82ee5defb2fcULL, 0x78a5636f43172f60ULL, 0x84c87814a1f0ab72ULL, 0x8cc702081a6439ecULL,
    0x90befffa23631e28ULL, 0xa4506cebde82bde9ULL, 0xbef9a3f7b2c67915ULL, 0xc67178f2e372532bULL,
    0xca273eceea26619cULL, 0xd186b8c721c0c207ULL, 0xeada7dd6cde0eb1eULL, 0xf57d4f7fee6ed178ULL,
    0x06f067aa72176fbaULL, 0x0a637dc5a2c898a6ULL, 0x113f9804bef90daeULL, 0x1b710b35131c471bULL,
    0x28db77f523047d84ULL, 0x32caab7b40c72493ULL, 0x3c9ebe0a15c9bebcULL, 0x431d67c49c100d4cULL,
    0x4cc5d4becb3e42b6ULL, 0x597f299cfc657e2aULL, 0x5fcb6fab3ad6faecULL, 0x6c44198c4a475817ULL,
};

inline uint64_t Ror(uint64_t x, uint32_t n)
{
    return (x >> n) | (x << (64u - n));
}

// Big-endian load of a 64-bit word (explicit shifts -> endian-neutral).
uint64_t LoadBE64(const uint8_t* p)
{
    return (static_cast<uint64_t>(p[0]) << 56) |
           (static_cast<uint64_t>(p[1]) << 48) |
           (static_cast<uint64_t>(p[2]) << 40) |
           (static_cast<uint64_t>(p[3]) << 32) |
           (static_cast<uint64_t>(p[4]) << 24) |
           (static_cast<uint64_t>(p[5]) << 16) |
           (static_cast<uint64_t>(p[6]) << 8) |
           static_cast<uint64_t>(p[7]);
}

void StoreBE64(uint8_t* p, uint64_t v)
{
    p[0] = static_cast<uint8_t>(v >> 56);
    p[1] = static_cast<uint8_t>(v >> 48);
    p[2] = static_cast<uint8_t>(v >> 40);
    p[3] = static_cast<uint8_t>(v >> 32);
    p[4] = static_cast<uint8_t>(v >> 24);
    p[5] = static_cast<uint8_t>(v >> 16);
    p[6] = static_cast<uint8_t>(v >> 8);
    p[7] = static_cast<uint8_t>(v);
}

// Compress one 1024-bit block into the working state h[8].
void Compress(uint64_t h[8], const uint8_t block[128])
{
    uint64_t w[80];
    for (uint32_t i = 0; i < 16; ++i)
        w[i] = LoadBE64(block + i * 8);
    for (uint32_t i = 16; i < 80; ++i)
    {
        const uint64_t s0 = Ror(w[i - 15], 1) ^ Ror(w[i - 15], 8) ^ (w[i - 15] >> 7);
        const uint64_t s1 = Ror(w[i - 2], 19) ^ Ror(w[i - 2], 61) ^ (w[i - 2] >> 6);
        w[i] = w[i - 16] + w[i - 7] + s0 + s1;
    }

    uint64_t a = h[0], b = h[1], c = h[2], d = h[3];
    uint64_t e = h[4], f = h[5], g = h[6], hh = h[7];

    for (uint32_t i = 0; i < 80; ++i)
    {
        const uint64_t bigSigma1 = Ror(e, 14) ^ Ror(e, 18) ^ Ror(e, 41);
        const uint64_t ch = (e & f) ^ (~e & g);
        const uint64_t t1 = hh + bigSigma1 + ch + kRoundConstants[i] + w[i];
        const uint64_t bigSigma0 = Ror(a, 28) ^ Ror(a, 34) ^ Ror(a, 39);
        const uint64_t maj = (a & b) ^ (a & c) ^ (b & c);
        const uint64_t t2 = bigSigma0 + maj;

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

// FIPS 180-4 5.1.2 padding + final compression; leaves the digest in
// ctx->state, which both final exits (sha512/sha384) serialize out.
void FinalCore(Il2CppSha512Ctx* ctx)
{
    const uint64_t hi = ctx->totalBitsHi;
    const uint64_t lo = ctx->totalBitsLo;
    const size_t remaining = 128u - ctx->buffered;

    if (remaining >= 17u)
    {
        // Room for 0x80 + zeros + 16-byte length in this block.
        ctx->buffer[ctx->buffered] = 0x80u;
        std::memset(ctx->buffer + ctx->buffered + 1, 0, remaining - 17u);
    }
    else
    {
        // Not enough room for the length: finish this block with 0x80 +
        // zeros, compress, then emit a fresh all-zero padding block.
        ctx->buffer[ctx->buffered] = 0x80u;
        std::memset(ctx->buffer + ctx->buffered + 1, 0, remaining - 1u);
        Compress(ctx->state, ctx->buffer);
        std::memset(ctx->buffer, 0, 112u);
    }

    // 16-byte big-endian message length in bits (last 16 bytes of the block).
    for (uint32_t i = 0; i < 8; ++i)
        ctx->buffer[112u + i] = static_cast<uint8_t>(hi >> (56u - i * 8u));
    for (uint32_t i = 0; i < 8; ++i)
        ctx->buffer[120u + i] = static_cast<uint8_t>(lo >> (56u - i * 8u));
    Compress(ctx->state, ctx->buffer);
}

} // namespace

void il2cpp_sha512_create(Il2CppSha512Ctx* ctx)
{
    if (ctx == nullptr)
    {
        return;
    }
    ctx->state[0] = 0x6a09e667f3bcc908ULL;
    ctx->state[1] = 0xbb67ae8584caa73bULL;
    ctx->state[2] = 0x3c6ef372fe94f82bULL;
    ctx->state[3] = 0xa54ff53a5f1d36f1ULL;
    ctx->state[4] = 0x510e527fade682d1ULL;
    ctx->state[5] = 0x9b05688c2b3e6c1fULL;
    ctx->state[6] = 0x1f83d9abfb41bd6bULL;
    ctx->state[7] = 0x5be0cd19137e2179ULL;
    ctx->totalBitsHi = 0;
    ctx->totalBitsLo = 0;
    ctx->buffered = 0;
}

void il2cpp_sha384_create(Il2CppSha512Ctx* ctx)
{
    if (ctx == nullptr)
    {
        return;
    }
    // FIPS 180-4 5.3.4: first 64 bits of the fractional parts of the square
    // roots of the 9th through 16th primes.
    ctx->state[0] = 0xcbbb9d5dc1059ed8ULL;
    ctx->state[1] = 0x629a292a367cd507ULL;
    ctx->state[2] = 0x9159015a3070dd17ULL;
    ctx->state[3] = 0x152fecd8f70e5939ULL;
    ctx->state[4] = 0x67332667ffc00b31ULL;
    ctx->state[5] = 0x8eb44a8768581511ULL;
    ctx->state[6] = 0xdb0c2e0d64f98fa7ULL;
    ctx->state[7] = 0x47b5481dbefa4fa4ULL;
    ctx->totalBitsHi = 0;
    ctx->totalBitsLo = 0;
    ctx->buffered = 0;
}

void il2cpp_sha512_update(Il2CppSha512Ctx* ctx, const void* data, size_t len)
{
    if (ctx == nullptr)
    {
        return;
    }
    if (data == nullptr || len == 0)
    {
        return;
    }
    const uint64_t bits = static_cast<uint64_t>(len) * 8u;
    ctx->totalBitsLo += bits;
    if (ctx->totalBitsLo < bits)
        ++ctx->totalBitsHi;

    const uint8_t* src = static_cast<const uint8_t*>(data);

    // Top up a partially filled block first (keeps the tail-block invariant:
    // after each update, buffered < 128 unless a whole block was just taken).
    if (ctx->buffered != 0)
    {
        const size_t need = 128u - ctx->buffered;
        const size_t take = len < need ? len : need;
        std::memcpy(ctx->buffer + ctx->buffered, src, take);
        ctx->buffered += take;
        src += take;
        len -= take;
        if (ctx->buffered == 128u)
        {
            Compress(ctx->state, ctx->buffer);
            ctx->buffered = 0;
        }
    }

    // Whole blocks straight from the caller's buffer.
    while (len >= 128u)
    {
        Compress(ctx->state, src);
        src += 128u;
        len -= 128u;
    }

    if (len != 0)
    {
        std::memcpy(ctx->buffer, src, len);
        ctx->buffered = len;
    }
}

void il2cpp_sha512_final(Il2CppSha512Ctx* ctx, uint8_t out[64])
{
    if (ctx == nullptr || out == nullptr)
    {
        return;
    }
    FinalCore(ctx);
    for (uint32_t i = 0; i < 8; ++i)
        StoreBE64(out + i * 8u, ctx->state[i]);
}

void il2cpp_sha384_final(Il2CppSha512Ctx* ctx, uint8_t out[48])
{
    if (ctx == nullptr || out == nullptr)
    {
        return;
    }
    FinalCore(ctx);
    for (uint32_t i = 0; i < 6; ++i)
        StoreBE64(out + i * 8u, ctx->state[i]);
}

void il2cpp_sha512_hash_bytes(const void* data, size_t len, uint8_t out[64])
{
    if (out == nullptr)
    {
        return;
    }
    Il2CppSha512Ctx ctx;
    il2cpp_sha512_create(&ctx);
    il2cpp_sha512_update(&ctx, data, len);
    il2cpp_sha512_final(&ctx, out);
}

void il2cpp_sha384_hash_bytes(const void* data, size_t len, uint8_t out[48])
{
    if (out == nullptr)
    {
        return;
    }
    Il2CppSha512Ctx ctx;
    il2cpp_sha384_create(&ctx);
    il2cpp_sha512_update(&ctx, data, len);
    il2cpp_sha384_final(&ctx, out);
}

Il2CppArray* il2cpp_sha512_hash_bytes(Il2CppArray* data)
{
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, 64);
    il2cpp_sha512_hash_bytes(data->data, static_cast<size_t>(data->length),
        reinterpret_cast<uint8_t*>(result->data));
    return result;
}

Il2CppArray* il2cpp_sha384_hash_bytes(Il2CppArray* data)
{
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, 48);
    il2cpp_sha384_hash_bytes(data->data, static_cast<size_t>(data->length),
        reinterpret_cast<uint8_t*>(result->data));
    return result;
}

}} // namespace il2cpp::runtime
