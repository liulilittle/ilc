#pragma once
//------------------------------------------------------------------------------
// sha1.h -- SHA-1 message digest runtime support (FIPS 180-4, secure hash
// standard). Crypto domain.
//
// SHA-1 produces a fixed 20-byte digest for an arbitrary byte sequence. The
// algorithm is the FIPS 180-4 specification: 512-bit blocks, 80 rounds in
// four 20-round groups, message words big-endian, padding = 0x80 + zeros to
// the 448-bit boundary + a 64-bit big-endian bit length (padding never feeds
// back into the length counter).
//
// SECURITY NOTE: SHA-1 is broken for collision resistance (SHAttered, 2017)
// and MUST NOT be used for signatures/commitments. It is implemented here
// only because .NET still exposes System.Security.Cryptography.SHA1 and its
// one-way property (preimage resistance) is unaffected.
//
// SEMANTIC BASELINE: FIPS 180-4 -- byte-exact digests, no platform
// variation; the test vectors ("", "abc", the 56-byte two-block string)
// are published in FIPS 180-4 and the standard test suite.
//
// API shape: streaming context (OpenSSL EVP style -- the shape the managed
// SHA1.TransformBlock/TransformFinalBlock/ComputeHash surface maps onto):
//   create -> zero or more update -> final  (digest 20 bytes, big-endian)
//   hash_bytes = create + update(one shot) + final.
//
// All functions are defensive about null arguments (no-op, matching the
// domain TryParse convention) and NEVER throw.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp {

// Streaming SHA-1 context (112 bytes). Stack-allocatable, no heap, no
// dynamic allocation. After final() the context holds the finished digest
// state and MUST be passed to create() again before reuse (create resets
// state, length counter and buffer).
struct Il2CppSha1Context
{
    uint32_t state[5];   // H0..H4 (FIPS 180-4 6.1.1 initial hash values)
    uint64_t count;      // message bytes fed via update (bit length = count*8)
    uint8_t buffer[64];  // pending tail of the current block
    size_t bufferLen;    // bytes pending in 'buffer' (always 0..63)
};

// Initialize/reset 'ctx' to the FIPS 180-4 initial state (H0..H4 = the
// fractional parts of sqrt(2), sqrt(3), sqrt(5), sqrt(7), sqrt(11)).
// Null 'ctx' -> no-op.
void il2cpp_sha1_create(Il2CppSha1Context* ctx);

// Feed 'len' bytes from 'data' into the hash. Any byte sequence is legal;
// len 0 or null 'data' is a no-op. Null 'ctx' -> no-op. May be called any
// number of times between create() and final().
void il2cpp_sha1_update(Il2CppSha1Context* ctx, const void* data, size_t len);

// Apply FIPS 180-4 5.1.1 padding (0x80, zeros, 64-bit big-endian bit
// length), produce the final digest: digest[0..19] big-endian, exactly the
// 40-hex-digit string SHA-1 is known by. Null 'ctx' or 'digest' -> no-op.
// 'ctx' must not be updated afterwards without create() first.
void il2cpp_sha1_final(Il2CppSha1Context* ctx, uint8_t digest[20]);

// One-shot: hash 'len' bytes at 'data' into 'digest' (20 bytes).
// Equivalent to create + update + final on a private context; a null 'data'
// with len 0 hashes the empty message. Null 'digest' -> no-op.
void il2cpp_sha1_hash_bytes(const void* data, size_t len, uint8_t digest[20]);

// Managed byte[] overload -- the transpiler's SHA1.ComputeHash(byte[])
// target (MethodMap.Hash.cs): hashes the array's PAYLOAD (data/length
// extracted here -- the emitter has no cast code for an array data pointer
// + length, so the array-taking form is the icall shape). Null array ->
// ArgumentNullException (the .NET ComputeHash contract). Returns a NEW
// managed byte[20]. The raw-buffer overload above keeps its documented
// NEVER-throw contract.
Il2CppArray* il2cpp_sha1_hash_bytes(Il2CppArray* data);

} // namespace il2cpp
