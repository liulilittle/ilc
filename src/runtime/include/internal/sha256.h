#pragma once
//------------------------------------------------------------------------------
// sha256.h -- SHA-256 (FIPS 180-4) streaming digest support.
//
// Pure C++ computation -- no managed object / icall ABI involved: the
// streaming context is a plain struct, so the helpers can back BCL hashing
// (e.g. System.Security.Cryptography.SHA256) later without object interop.
//
// SEMANTIC BASELINE: FIPS 180-4 SHA-256.
//   * Output is the canonical 32-byte big-endian digest.
//   * Streaming contract: create -> (update)* -> final. final() pads the
//     message per FIPS 180-4 (0x80, zeros to 56 mod 64, 64-bit bit length)
//     and writes the digest; call create() again to restart the context.
//   * update() with len == 0 is a no-op; create() is idempotent (reset).
//   * The context is reentrant per-context: distinct contexts share no state.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp { namespace runtime {

// Streaming SHA-256 context (FIPS 180-4). Pass to create() before the first
// update(); the struct is fully owned by the caller (stack or heap).
struct Il2CppSha256Ctx
{
    uint32_t state[8];    // H0..H7 working hash state (big-endian on output)
    uint8_t  buffer[64];  // unprocessed input block
    uint64_t totalBits;   // total message length in bits (incl. buffered)
    size_t   buffered;    // bytes currently buffered (0..63)
};

// Reset a context for a fresh digest (idempotent). Null 'ctx' -> no-op.
void il2cpp_sha256_create(Il2CppSha256Ctx* ctx);

// Feed 'len' bytes of data into the digest. Null 'ctx', null 'data' or
// len == 0 is a no-op (the sha1.h defensive no-op convention).
void il2cpp_sha256_update(Il2CppSha256Ctx* ctx, const void* data, size_t len);

// Produce the 32-byte digest into 'out' (must hold at least 32 bytes).
// Pads the remaining message per FIPS 180-4. The context is left consumed;
// call create() to restart. Null 'ctx' or 'out' -> no-op.
void il2cpp_sha256_final(Il2CppSha256Ctx* ctx, uint8_t out[32]);

// One-shot convenience: digest of 'data'/'len' into 'out' (32 bytes).
// Null 'out' -> no-op; a null 'data' with len 0 hashes the empty message.
void il2cpp_sha256_hash_bytes(const void* data, size_t len, uint8_t out[32]);

// Managed byte[] overload -- the transpiler's SHA256.ComputeHash(byte[])
// target (MethodMap.Hash.cs): hashes the array's PAYLOAD (data/length
// extracted here -- the emitter has no cast code for an array data pointer
// + length, so the array-taking form is the icall shape). Null array ->
// ArgumentNullException (the .NET ComputeHash contract). Returns a NEW
// managed byte[32]. The raw-buffer overload above keeps its documented
// no-throw contract.
Il2CppArray* il2cpp_sha256_hash_bytes(Il2CppArray* data);

}} // namespace il2cpp::runtime
