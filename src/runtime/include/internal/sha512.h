#pragma once
//------------------------------------------------------------------------------
// sha512.h -- SHA-384/SHA-512 (FIPS 180-4) streaming digest support.
//
// Pure C++ computation -- no managed object / icall ABI involved: the
// streaming context is a plain struct, so the helpers can back BCL hashing
// (e.g. System.Security.Cryptography.SHA512) later without object interop.
//
// SEMANTIC BASELINE: FIPS 180-4 SHA-512 / SHA-384.
//   * SHA-512 output is the canonical 64-byte big-endian digest; SHA-384 is
//     the SAME 64-bit-word core with a different initial state and a 48-byte
//     truncated output (FIPS 180-4 5.3.4) -- both share Il2CppSha512Ctx and
//     the update path (one core, two final exits, C6 DRY).
//   * Streaming contract: create -> (update)* -> final. final() pads the
//     message per FIPS 180-4 (0x80, zeros to 112 mod 128, 128-bit bit length)
//     and writes the digest; call create() again to restart the context.
//   * update() with len == 0 is a no-op; create() is idempotent (reset).
//   * The context is reentrant per-context: distinct contexts share no state.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp { namespace runtime {

// Streaming SHA-384/SHA-512 context (FIPS 180-4). Pass to create() before the
// first update(); the struct is fully owned by the caller (stack or heap).
// SHA-384 uses the same context -- create it with il2cpp_sha384_create and
// finalize with il2cpp_sha384_final.
struct Il2CppSha512Ctx
{
    uint64_t state[8];    // H0..H7 working hash state (big-endian on output)
    uint8_t  buffer[128]; // unprocessed input block
    uint64_t totalBitsHi; // total message length in bits, high 64 bits
    uint64_t totalBitsLo; // total message length in bits, low 64 bits
    size_t   buffered;    // bytes currently buffered (0..127)
};

// Reset a context for a fresh SHA-512 digest (idempotent). Null 'ctx' ->
// no-op.
void il2cpp_sha512_create(Il2CppSha512Ctx* ctx);

// Reset a context for a fresh SHA-384 digest (FIPS 180-4 5.3.4 initial
// state; the update path is shared with SHA-512). Null 'ctx' -> no-op.
void il2cpp_sha384_create(Il2CppSha512Ctx* ctx);

// Feed 'len' bytes of data into the digest. Null 'ctx', null 'data' or
// len == 0 is a no-op (the sha1.h defensive no-op convention). Shared by
// SHA-384 and SHA-512 (identical block/update semantics).
void il2cpp_sha512_update(Il2CppSha512Ctx* ctx, const void* data, size_t len);

// Produce the 64-byte SHA-512 digest into 'out' (must hold at least 64
// bytes). Pads the remaining message per FIPS 180-4. The context is left
// consumed; call create() to restart. Null 'ctx' or 'out' -> no-op.
void il2cpp_sha512_final(Il2CppSha512Ctx* ctx, uint8_t out[64]);

// Produce the 48-byte SHA-384 digest into 'out' (must hold at least 48
// bytes): same padding/compression as SHA-512, truncated to the first six
// state words (FIPS 180-4 5.3.4). Context contract as il2cpp_sha512_final.
// Null 'ctx' or 'out' -> no-op.
void il2cpp_sha384_final(Il2CppSha512Ctx* ctx, uint8_t out[48]);

// One-shot convenience: SHA-512 digest of 'data'/'len' into 'out' (64 bytes).
// Null 'out' -> no-op; a null 'data' with len 0 hashes the empty message.
void il2cpp_sha512_hash_bytes(const void* data, size_t len, uint8_t out[64]);

// One-shot convenience: SHA-384 digest of 'data'/'len' into 'out' (48 bytes).
// Null 'out' -> no-op; a null 'data' with len 0 hashes the empty message.
void il2cpp_sha384_hash_bytes(const void* data, size_t len, uint8_t out[48]);

// Managed byte[] overloads -- the transpiler's SHA512/SHA384.ComputeHash
// (byte[]) target, same shape as the T1 md5/sha1/sha256 overloads: hashes
// the array's PAYLOAD, throws ArgumentNullException on a null array (the
// .NET ComputeHash contract), returns a NEW managed byte[] (64 / 48 bytes).
Il2CppArray* il2cpp_sha512_hash_bytes(Il2CppArray* data);
Il2CppArray* il2cpp_sha384_hash_bytes(Il2CppArray* data);

}} // namespace il2cpp::runtime
