#pragma once
//------------------------------------------------------------------------------
// crypto_obj.h -- System.Security.Cryptography OBJECT layer (T4).
//
// The T1/T2/T3 crypto modules (md5.h/sha1.h/sha256.h/sha512.h/hmac.h/aes.h/
// rng.h) expose PLAIN C++ computation: streaming contexts and one-shot
// helpers, no managed objects. This header adds the MANAGED OBJECT layer
// (crypto-domain.md section 2.1 object model, path 1 -- ctx-inline objects) that
// the transpiler's MethodMap.Crypto.cs wires MD5.Create()/ComputeHash(),
// HMACSHAx, Aes.Create()/CreateEncryptor()/TransformFinalBlock and
// RNGCryptoServiceProvider.GetBytes onto:
//
//   * Each hash algorithm has a GLOBAL-scope class descriptor
//     (System_Security_Cryptography_<Algo>_class, the mangled name the
//     transpiler emits for newobj/isinst/castclass -- TypeEmitter.Sanitize
//     of the full .NET name, the System.Version precedent in versionuri.h)
//     whose instance layout is the object header + the algorithm's inline
//     STREAMING CONTEXT (create/update/final live on the object: the
//     "streaming" contract the task requires).
//   * The legacy *CryptoServiceProvider / *Managed types (net48 aliases,
//     crypto-domain.md section 4.4) share the SAME layout and icalls -- their
//     descriptors are aliases, so `new MD5CryptoServiceProvider()` links
//     through the GLOBAL symbol while ComputeHash behaves identically.
//   * HMAC objects carry the key as a GC reference field (refFieldBitmap
//     bit 1 -- the stringbuilder/arraylist ref-field precedent); the
//     embedded Il2CppHmacCtx is rebuilt from the stored key on every
//     ComputeHash (zero cross-call shared state, crypto-domain.md section 2.1).
//   * Aes objects carry Key/IV as GC reference fields (bits 1,2);
//     AesTransform objects carry key/iv refs + the inline AES key schedule
//     (no native pointers, GC-safe) + a decrypt flag.
//   * RNGCryptoServiceProvider is a stateless instance (header only).
//
// SEMANTIC BASELINE: net48 with the crypto-domain.md section 1.4 documented
// divergences (runtime klass identity, v1 scope). All icalls validate at
// the boundary and throw the .NET exception types (ArgumentNullException /
// ArgumentOutOfRangeException / NotSupportedException /
// CryptographicException) -- the pure cores never throw.
//
// Object lifetime: every object is allocated via il2cpp_object_new through
// its GLOBAL descriptor (zero-initialized -- GC-safe before the ctor
// icall); the create/ctor icalls stamp the context. Dispose now carries the
// net48 disposed flag: it clears the cached digest (hashValue), sets
// disposed=true and nulls the HMAC/AES ref fields so the key/iv become
// collectable (matching net48 key-clearing + Clear()/Dispose() semantics).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "il2cpp_metadata.h"
#include "md5.h"
#include "sha1.h"
#include "sha256.h"
#include "sha512.h"
#include "hmac.h"
#include "aes.h"

#include <cstdint>

//------------------------------------------------------------------------------
// GLOBAL-scope runtime descriptors (the transpiler emits
// &System_Security_Cryptography_*_class for newobj/isinst/castclass --
// TypeEmitter.Sanitize("System.Security.Cryptography.X") ==
// "System_Security_Cryptography_X"; defined in crypto_obj.cpp, registered
// lazily through il2cpp_crypto_register_classes()).
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Security_Cryptography_MD5_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA1_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA256_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA384_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA512_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_MD5CryptoServiceProvider_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA1CryptoServiceProvider_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA256CryptoServiceProvider_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA384CryptoServiceProvider_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA512CryptoServiceProvider_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA1Managed_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA256Managed_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA384Managed_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_SHA512Managed_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_HMACSHA1_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_HMACSHA256_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_HMACSHA384_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_HMACSHA512_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_Aes_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_AesTransform_class;
extern il2cpp::Il2CppClass System_Security_Cryptography_RNGCryptoServiceProvider_class;

namespace il2cpp {

//------------------------------------------------------------------------------
// Managed object layouts. Every hash object carries the Il2CppHashState
// directly after the object header (hashValue = slot 1, bitmap bit 1); the
// algorithm contexts follow at the first 8-aligned offset after the state
// (offset 24 on both 4- and 8-byte pointer widths) -- the static_assert pins
// in crypto_obj.cpp enforce this for the collector's refFieldBitmap indexing.
//------------------------------------------------------------------------------

// Common per-instance hash state appended to every hash object (net48
// HashAlgorithm contract, hashalgorithm.cs):
//   hashValue -- the cached last ComputeHash/TransformFinalBlock digest (a GC
//                reference; refFieldBitmap bit 1 -- slot 1). The Hash getter
//                returns a CLONE; before the first compute it is null (-> NRE)
//                and after Clear()/Dispose() it is null AND disposed (-> ODE).
//   state     -- 0 = idle/finalized, 1 = mid-stream (TransformBlock). The Hash
//                getter throws CryptographicUnexpectedOperationException when it
//                is not 0 (HashNotYetFinalized).
//   disposed  -- true after Clear()/Dispose(); the Hash getter, ComputeHash,
//                TransformBlock and TransformFinalBlock throw ODE.
// The struct sits directly after the object header so hashValue always occupies
// slot 1 (bitmap bit 1); the algorithm contexts follow at the first aligned
// offset (the static_asserts in crypto_obj.cpp pin them).
struct Il2CppHashState
{
    Il2CppArray* hashValue; // slot 1 -- GC reference (bitmap bit 1)
    int32_t state;          // 0 = idle/finalized, 1 = mid-stream
    bool disposed;          // true after Clear()/Dispose()
};

// MD5: header + inline RFC 1321 context.
struct Il2CppMd5Object
{
    Il2CppObject obj;      // object header (klass pointer, offset 0)
    Il2CppHashState hs;    // cached digest + streaming state (hashValue slot 1)
    runtime::Il2CppMd5Context ctx;  // streaming context
};

// SHA-1: header + inline FIPS 180-4 context.
struct Il2CppSha1Object
{
    Il2CppObject obj;
    Il2CppHashState hs;
    Il2CppSha1Context ctx;
};

// SHA-256: header + inline FIPS 180-4 context.
struct Il2CppSha256Object
{
    Il2CppObject obj;
    Il2CppHashState hs;
    runtime::Il2CppSha256Ctx ctx;
};

// SHA-384/SHA-512 SHARE this layout (FIPS 180-4 5.3.4: same 64-bit-word
// core, different initial state + final exit). The per-class icalls select
// the create/final pair -- SHA-384 -> il2cpp_sha384_create/final,
// SHA-512 -> il2cpp_sha512_create/final.
struct Il2CppSha512Object
{
    Il2CppObject obj;
    Il2CppHashState hs;
    runtime::Il2CppSha512Ctx ctx;
};

// HMAC: header + the hash state (slot 1) + the key as a GC reference (slot 3)
// + the embedded HMAC context (slot 4). The context is rebuilt from the stored
// key on every ComputeHash (RFC 2104 ipad/opad), so no state survives between
// calls.
struct Il2CppHmacObject
{
    Il2CppObject obj;              // object header
    Il2CppHashState hs;            // slot 1 -- hashValue GC ref (bitmap bit 1)
    Il2CppArray* key;              // slot 3 -- GC reference (bitmap bit 3)
    runtime::Il2CppHmacCtx ctx;    // slot 4 -- inline HMAC context
};

// Aes: header + Key/IV as GC references (slots 1,2 -- bitmap bits 1,2) + the
// disposed flag (net48 Dispose closes the CSP handle: a later Key/IV/Create*/
// Generate* throws ObjectDisposedException).
struct Il2CppAesObject
{
    Il2CppObject obj;  // object header
    Il2CppArray* key;  // slot 1 -- GC reference (bitmap bit 1)
    Il2CppArray* iv;   // slot 2 -- GC reference (bitmap bit 2)
    bool disposed;     // net48 AesCryptoServiceProvider.Dispose
};

// AesTransform (CreateEncryptor/CreateDecryptor result): header + key/iv
// refs (bits 1,2) + the INLINE expanded key schedule (opaque to callers;
// no native pointers -- GC-safe) + a decrypt flag. The schedule is built by
// il2cpp_aes_create at the transform's creation icall.
struct Il2CppAesTransformObject
{
    Il2CppObject obj;            // object header
    Il2CppArray* key;            // slot 1 -- GC reference (bitmap bit 1)
    Il2CppArray* iv;             // slot 2 -- GC reference (bitmap bit 2)
    runtime::Il2CppAesContext ctx; // slot 3 -- inline key schedule
    bool decrypt;                // false: encrypt (CBC+PKCS7), true: decrypt
};

// RNGCryptoServiceProvider: stateless instance (header only -- net48's
// parameterless ctor has no side effects).
struct Il2CppRngObject
{
    Il2CppObject obj; // object header
};

// Register all crypto descriptors (idempotent; called by the creation
// icalls before the first il2cpp_object_new).
void il2cpp_crypto_register_classes();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// HMAC algorithm descriptors beyond SHA-256 (hmac.cpp ships only
// kIl2CppHmacSha256; the SHA-1/384/512 descriptors are defined in
// crypto_obj.cpp -- the same Il2CppHmacHash descriptor table, one HMAC core
// serves all four).
extern const Il2CppHmacHash kIl2CppHmacSha1;
extern const Il2CppHmacHash kIl2CppHmacSha384;
extern const Il2CppHmacHash kIl2CppHmacSha512;

//------------------------------------------------------------------------------
// Hash object icalls (MD5/SHA1/SHA256/SHA384/SHA512 -- the same surface per
// algorithm; the algorithm is a property of the receiver's class).
//
//   icall_<algo>_create()   -- static MD5.Create() etc.: allocates the
//                              object through the algorithm's GLOBAL class
//                              descriptor and primes the inline context.
//   icall_<algo>_ctor(self) -- newobj of the legacy *CryptoServiceProvider
//                              / *Managed types: primes the context on an
//                              already-allocated instance.
//   icall_<algo>_compute_hash(self, byte[]) / _range(self, byte[], int, int)
//                            -- streaming ComputeHash: re-creates the
//                              context, feeds the (sub)range, finalizes.
//                              Range validation mirrors net48
//                              HashAlgorithm.ComputeHash(byte[], int, int):
//                              null -> ArgumentNullException; offset < 0 ->
//                              ArgumentOutOfRangeException; count < 0 or
//                              offset+count > length -> ArgumentException.
//   icall_<algo>_dispose(self) -- no-op (nothing native to release).
//------------------------------------------------------------------------------
Il2CppObject* icall_md5_create();
Il2CppObject* icall_sha1_create();
Il2CppObject* icall_sha256_create();
Il2CppObject* icall_sha384_create();
Il2CppObject* icall_sha512_create();

void icall_md5_ctor(Il2CppObject* self);
void icall_sha1_ctor(Il2CppObject* self);
void icall_sha256_ctor(Il2CppObject* self);
void icall_sha384_ctor(Il2CppObject* self);
void icall_sha512_ctor(Il2CppObject* self);

Il2CppObject* icall_md5_compute_hash(Il2CppObject* self, Il2CppArray* data);
Il2CppObject* icall_sha1_compute_hash(Il2CppObject* self, Il2CppArray* data);
Il2CppObject* icall_sha256_compute_hash(Il2CppObject* self, Il2CppArray* data);
Il2CppObject* icall_sha384_compute_hash(Il2CppObject* self, Il2CppArray* data);
Il2CppObject* icall_sha512_compute_hash(Il2CppObject* self, Il2CppArray* data);

Il2CppObject* icall_md5_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count);
Il2CppObject* icall_sha1_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count);
Il2CppObject* icall_sha256_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count);
Il2CppObject* icall_sha384_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count);
Il2CppObject* icall_sha512_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count);

void icall_md5_dispose(Il2CppObject* self);
void icall_sha1_dispose(Il2CppObject* self);
void icall_sha256_dispose(Il2CppObject* self);
void icall_sha384_dispose(Il2CppObject* self);
void icall_sha512_dispose(Il2CppObject* self);

//   icall_<algo>_dispose(self) -- HashAlgorithm.Dispose: clears the cached
//                              digest (hashValue) and sets the disposed flag
//                              (a later Hash/ComputeHash/Transform* throws ODE).
//   icall_<algo>_get_hash(self) -- HashAlgorithm.Hash: ODE when disposed,
//                              CryptographicUnexpectedOperationException when
//                              state != 0, else a CLONE of the cached digest
//                              (NRE before the first ComputeHash).
//   icall_<algo>_clear(self) -- HashAlgorithm.Clear(): Dispose() (the net48
//                              Clear()/Dispose() contract).
//   icall_<algo>_initialize(self) -- HashAlgorithm.Initialize: resets the
//                              inline context (net48 Initialize).
//   icall_<algo>_compute_hash_stream(self, stream) -- ComputeHash(Stream):
//                              the 4096-byte chunked streaming read loop.
//   icall_<algo>_transform_block(self, input, inOff, inCount, output,
//                              outOff) -- ICryptoTransform.TransformBlock:
//                              sets state=1, hashes the input, copies to the
//                              output buffer when it differs, returns inCount.
//   icall_<algo>_transform_final_block(self, input, inOff, inCount) --
//                              TransformFinalBlock: hashes the input, caches the
//                              digest, state=0, returns a copy of the input.
//-----------------------------------------------------------------------------
int32_t icall_md5_get_hash_size(Il2CppObject* self);
int32_t icall_sha1_get_hash_size(Il2CppObject* self);
int32_t icall_sha256_get_hash_size(Il2CppObject* self);
int32_t icall_sha384_get_hash_size(Il2CppObject* self);
int32_t icall_sha512_get_hash_size(Il2CppObject* self);

void icall_md5_initialize(Il2CppObject* self);
void icall_sha1_initialize(Il2CppObject* self);
void icall_sha256_initialize(Il2CppObject* self);
void icall_sha384_initialize(Il2CppObject* self);
void icall_sha512_initialize(Il2CppObject* self);

Il2CppObject* icall_md5_get_hash(Il2CppObject* self);
Il2CppObject* icall_sha1_get_hash(Il2CppObject* self);
Il2CppObject* icall_sha256_get_hash(Il2CppObject* self);
Il2CppObject* icall_sha384_get_hash(Il2CppObject* self);
Il2CppObject* icall_sha512_get_hash(Il2CppObject* self);

void icall_md5_clear(Il2CppObject* self);
void icall_sha1_clear(Il2CppObject* self);
void icall_sha256_clear(Il2CppObject* self);
void icall_sha384_clear(Il2CppObject* self);
void icall_sha512_clear(Il2CppObject* self);

Il2CppObject* icall_md5_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream);
Il2CppObject* icall_sha1_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream);
Il2CppObject* icall_sha256_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream);
Il2CppObject* icall_sha384_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream);
Il2CppObject* icall_sha512_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream);

int32_t icall_md5_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                  int32_t inCount, Il2CppArray* output, int32_t outOffset);
int32_t icall_sha1_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                   int32_t inCount, Il2CppArray* output, int32_t outOffset);
int32_t icall_sha256_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                     int32_t inCount, Il2CppArray* output, int32_t outOffset);
int32_t icall_sha384_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                     int32_t inCount, Il2CppArray* output, int32_t outOffset);
int32_t icall_sha512_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                     int32_t inCount, Il2CppArray* output, int32_t outOffset);

Il2CppObject* icall_md5_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                              int32_t inOffset, int32_t inCount);
Il2CppObject* icall_sha1_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                               int32_t inOffset, int32_t inCount);
Il2CppObject* icall_sha256_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                                 int32_t inOffset, int32_t inCount);
Il2CppObject* icall_sha384_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                                 int32_t inOffset, int32_t inCount);
Il2CppObject* icall_sha512_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                                 int32_t inOffset, int32_t inCount);

//------------------------------------------------------------------------------
// HMAC object icalls (one shared surface; the algorithm is dispatched from
// the receiver's class -- HMACSHA1/256/384/512 share the RFC 2104 core).
//
//   icall_hmac_ctor(self, byte[] key) -- new HMACSHAx(key): null key ->
//     ArgumentNullException; the key is stored as a GC reference field.
//   icall_hmac_ctor_default(self) -- the keyless HMACSHAx() ctor: a fresh
//     CSPRNG key of the algorithm's block size (net48 Utils.GenerateRandom).
//   icall_hmac_get_key(self) / icall_hmac_set_key(self, byte[]) -- the
//     Key property: the getter returns a CLONE of the stored key (net48
//     HMAC.Key clones); the setter runs InitializeKey (null ->
//     NullReferenceException, a key longer than the block size is hashed
//     first, otherwise it is cloned).
//   icall_hmac_compute_hash(self, byte[]) -- rebuilds the HMAC context from
//     the stored key (ipad/opad), feeds the data, caches the digest, returns a
//     clone.
//   icall_hmac_dispose(self) -- clears the cached digest, nulls the stored key
//     (net48 key clearing) and sets the disposed flag.
//   icall_hmac_get_hash(self) / icall_hmac_clear(self) / 
//     icall_hmac_compute_hash_stream(self, stream) /
//     icall_hmac_transform_block(self, input, inOff, inCount, output,
//     outOff) / icall_hmac_transform_final_block(self, input, inOff, inCount)
//     -- the same HashAlgorithm contract the *CryptoServiceProvider family
//     implements (HMAC is a KeyedHashAlgorithm : HashAlgorithm).
//-----------------------------------------------------------------------------
void icall_hmac_ctor(Il2CppObject* self, Il2CppArray* key);
void icall_hmac_ctor_default(Il2CppObject* self);
Il2CppObject* icall_hmac_get_key(Il2CppObject* self);
void icall_hmac_set_key(Il2CppObject* self, Il2CppArray* key);
Il2CppObject* icall_hmac_compute_hash(Il2CppObject* self, Il2CppArray* data);
void icall_hmac_dispose(Il2CppObject* self);
Il2CppObject* icall_hmac_get_hash(Il2CppObject* self);
void icall_hmac_clear(Il2CppObject* self);
Il2CppObject* icall_hmac_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream);
int32_t icall_hmac_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                   int32_t inCount, Il2CppArray* output, int32_t outOffset);
Il2CppObject* icall_hmac_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                               int32_t inOffset, int32_t inCount);

//------------------------------------------------------------------------------
// Aes object icalls (crypto-domain.md section 2.4 -- CBC + PKCS7 only).
//
//   icall_aes_create()                -- static Aes.Create(): a Key/IV-less
//                                       instance (net48 default).
//   icall_aes_set_key/iv(self, byte[])-- Key must be 16/32 bytes (24 ->
//     NotSupportedException; other -> CryptographicException), IV must be
//     16 bytes (else CryptographicException); null -> ArgumentNullException.
//   icall_aes_get_key/iv(self)        -- the stored arrays (byte[]).
//   icall_aes_create_encryptor/decryptor(self, byte[] key, byte[] iv)
//                                     -- validates (as set_Key/IV) and
//                                       returns a System_AesTransform_class
//                                       object with the inline key schedule.
//   icall_aes_create_encryptor_self/decryptor_self(self)
//                                     -- the parameterless net48
//                                       SymmetricAlgorithm.CreateEncryptor()/
//                                       CreateDecryptor() (Key/IV are
//                                       generated on demand when unset).
//   icall_aes_transform_final_block(self, byte[] input, int offset, int count)
//                                     -- CBC+PKCS7 of the subrange: encrypt
//                                       appends the PKCS7 pad; decrypt
//                                       validates/strips it (invalid pad ->
//                                       CryptographicException, aes.h).
//   icall_aes_generate_key/iv(self)   -- fresh CSPRNG bytes (16/32 / 16),
//                                       stored + returned.
//   icall_aes_dispose(self)           -- nulls key/iv refs.
//------------------------------------------------------------------------------
Il2CppObject* icall_aes_create();
void icall_aes_set_key(Il2CppObject* self, Il2CppArray* key);
Il2CppObject* icall_aes_get_key(Il2CppObject* self);
void icall_aes_set_iv(Il2CppObject* self, Il2CppArray* iv);
Il2CppObject* icall_aes_get_iv(Il2CppObject* self);
Il2CppObject* icall_aes_create_encryptor(Il2CppObject* self, Il2CppArray* key, Il2CppArray* iv);
Il2CppObject* icall_aes_create_decryptor(Il2CppObject* self, Il2CppArray* key, Il2CppArray* iv);
Il2CppObject* icall_aes_create_encryptor_self(Il2CppObject* self);
Il2CppObject* icall_aes_create_decryptor_self(Il2CppObject* self);
Il2CppObject* icall_aes_transform_final_block(Il2CppObject* self, Il2CppArray* input, int32_t offset, int32_t count);
Il2CppObject* icall_aes_generate_key(Il2CppObject* self);
Il2CppObject* icall_aes_generate_iv(Il2CppObject* self);
void icall_aes_dispose(Il2CppObject* self);

//------------------------------------------------------------------------------
// RNG object icalls (RNGCryptoServiceProvider + RandomNumberGenerator.Fill).
//
//   icall_rng_ctor(self)      -- new RNGCryptoServiceProvider(): no-op
//                                (net48's parameterless ctor has no effect).
//   icall_rng_get_bytes(self, byte[]) / get_nonzero_bytes -- fill the array
//                                payload from the OS CSPRNG (rng.h);
//                                GetNonZeroBytes resamples zeros (net48).
//   icall_rng_fill_static(byte[]) -- RandomNumberGenerator.Fill(byte[]):
//                                the static fill shape.
//   Null array -> ArgumentNullException in all three fillers.
//------------------------------------------------------------------------------
void icall_rng_ctor(Il2CppObject* self);
void icall_rng_get_bytes(Il2CppObject* self, Il2CppArray* bytes);
void icall_rng_get_nonzero_bytes(Il2CppObject* self, Il2CppArray* bytes);
void icall_rng_fill_static(Il2CppArray* bytes);
void icall_rng_dispose(Il2CppObject* self);

}} // namespace il2cpp::runtime
