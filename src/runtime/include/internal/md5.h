#pragma once
//------------------------------------------------------------------------------
// md5.h -- MD5 hash runtime support (RFC 1321).
//
// The transpiler maps System.Security.Cryptography.MD5 calls to these runtime
// helpers (InternalCall-style intrinsics delegating to the C++ implementation).
//
// SOURCE OF ALGORITHM: RFC 1321 (The MD5 Message-Digest Algorithm,
// R. Rivest, April 1992). The project's reference tree was checked for a
// .NET Core MD5 implementation first: reference/netcore sparse checkout does
// NOT contain System.Security.Cryptography (only System.Console,
// System.Private.CoreLib, System.Private.Uri, System.Text.RegularExpressions
// are present), so per the work-order rule the public standard algorithm is
// implemented directly. MD5 output is byte-for-byte identical across .NET
// Framework / .NET Core / RFC 1321 -- the digest is fully specified by the
// standard, so there is no behavioural divergence to reconcile.
//
// INTERFACE (three-phase + one-shot):
//   * il2cpp_md5_create()    -> fresh zeroed context
//   * il2cpp_md5_update(ctx, data, len) -> feed bytes (any chunking)
//   * il2cpp_md5_final(ctx, out16) -> write the 16-byte digest, context
//     is reset afterwards so it can be reused
//   * il2cpp_md5_hash_bytes(data, len) -> one-shot: returns a NEW managed
//     byte[16] (matches .NET MD5.ComputeHash(byte[])), null data ->
//     ArgumentNullException
//
// TEST VECTORS (RFC 1321 §A.5, also the .NET framework test vectors):
//   MD5("")    = d41d8cd98f00b204e9800998ecf8427e
//   MD5("abc") = 900150983cd24fb0d6963f7d28e17f72
//   MD5(1M 'a')= 7707d6ae4e027c70eea2a935c2296f21
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp { namespace runtime {

// MD5 computation context. Opaque to callers: create with
// il2cpp_md5_create, feed with il2cpp_md5_update, finish with
// il2cpp_md5_final. Contents are implementation details.
struct Il2CppMd5Context
{
    uint32_t h[4];          // running state (A/B/C/D)
    uint64_t bitCount;      // total bits processed
    uint8_t buffer[64];     // pending partial block
    size_t bufferLength;    // bytes pending in buffer (0..63)
};

// Create a zeroed MD5 context (equivalent to a fresh .NET MD5 instance).
Il2CppMd5Context il2cpp_md5_create();

// Feed 'length' bytes of 'data' into the context. 'data' may be null only
// when 'length' is zero. May be called any number of times.
void il2cpp_md5_update(Il2CppMd5Context* ctx, const void* data, size_t length);

// Finish the digest: writes 16 bytes to 'out16' (must be non-null). The
// context is reset to the fresh state afterwards, so it may be reused.
void il2cpp_md5_final(Il2CppMd5Context* ctx, uint8_t out16[16]);

// One-shot hash: computes MD5 of 'length' bytes at 'data' and returns a NEW
// managed byte[16]. 'data' null with length > 0 -> ArgumentNullException
// (matches .NET MD5.ComputeHash(byte[])). Equivalent to create+update+final
// with a single update.
Il2CppArray* il2cpp_md5_hash_bytes(const void* data, int32_t length);

// Managed byte[] overload -- the transpiler's MD5.ComputeHash(byte[]) target
// (MethodMap.Hash.cs): hashes the array's PAYLOAD (data/length extracted
// here -- the emitter has no cast code for an array data pointer + length,
// so the array-taking form is the icall shape). Null array ->
// ArgumentNullException (the .NET ComputeHash contract; the raw-buffer
// overload above keeps its documented semantics). Returns a NEW byte[16].
Il2CppArray* il2cpp_md5_hash_bytes(Il2CppArray* data);

}} // namespace il2cpp::runtime
