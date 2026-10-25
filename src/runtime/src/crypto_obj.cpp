//------------------------------------------------------------------------------
// crypto_obj.cpp -- System.Security.Cryptography OBJECT layer (see
// crypto_obj.h). Pure T4 wiring: class descriptors + object icalls that
// delegate to the T1/T2/T3 crypto cores (md5/sha1/sha256/sha512/hmac/aes/
// rng). NO algorithm logic lives here -- every path funnels into the
// existing pure contexts, so the object layer adds no behavioural
// divergence beyond the section 1.4 documented ones.
//------------------------------------------------------------------------------

#include "crypto_obj.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_* / not_supported / throw_new
#include "il2cpp_runtime.h"       // il2cpp_object_new / il2cpp_class_register
#include "il2cpp_streamio.h"      // icall_stream_read_bytes (ComputeHash(Stream))
#include "rng.h"                  // il2cpp_rng_fill (Aes.GenerateKey/IV + RNG icalls)

#include <cstring>

namespace il2cpp { namespace runtime {

namespace {

// Exception type-name chain for System.Security.Cryptography.CryptographicException
// (il2cpp_exception.h carrier format -- rng.cpp/aes.cpp carry the same
// constant; each module keeps its own copy because the shared factory
// header is out of scope).
const char kCryptoChain[] =
    "System.Security.Cryptography.CryptographicException,"
    "System.Security.Cryptography.CryptographicException,"
    "System.SystemException,System.Exception";

//------------------------------------------------------------------------------
// Hash algorithm adapters: normalize the per-module create shapes (md5
// returns the context by value, sha1/sha256/sha512 reset in place) onto one
// (create/update/final/digestSize) table so the object icalls share one
// generic implementation (C6 DRY -- one body serves all five algorithms).
//------------------------------------------------------------------------------
template <typename TCtx>
struct HashAdapter
{
    void (*create)(TCtx*);
    void (*update)(TCtx*, const void*, size_t);
    void (*final)(TCtx*, uint8_t*);
    int32_t digestSize;
};

void Md5CreatePtr(Il2CppMd5Context* c) { *c = il2cpp_md5_create(); }

const HashAdapter<Il2CppMd5Context> kMd5 =
    { Md5CreatePtr, il2cpp_md5_update, il2cpp_md5_final, 16 };
const HashAdapter<il2cpp::Il2CppSha1Context> kSha1 =
    { il2cpp_sha1_create, il2cpp_sha1_update, il2cpp_sha1_final, 20 };
const HashAdapter<Il2CppSha256Ctx> kSha256 =
    { il2cpp_sha256_create, il2cpp_sha256_update, il2cpp_sha256_final, 32 };
const HashAdapter<Il2CppSha512Ctx> kSha384 =
    { il2cpp_sha384_create, il2cpp_sha512_update, il2cpp_sha384_final, 48 };
const HashAdapter<Il2CppSha512Ctx> kSha512 =
    { il2cpp_sha512_create, il2cpp_sha512_update, il2cpp_sha512_final, 64 };

// net48 HashAlgorithm exception chains (hashalgorithm.cs): ObjectDisposedException
// (Clear()/Dispose() sets m_bDisposed) and CryptographicUnexpectedOperationException
// (the Hash getter with State != 0).
[[noreturn]] void ThrowHashDisposed()
{
    il2cpp_throw_new(
        "System.ObjectDisposedException,System.ObjectDisposedException,"
        "System.InvalidOperationException,System.SystemException,System.Exception",
        "The hash algorithm has been disposed.");
}

[[noreturn]] void ThrowHashNotYetFinalized()
{
    il2cpp_throw_new(
        "System.Security.Cryptography.CryptographicUnexpectedOperationException,"
        "System.Security.Cryptography.CryptographicUnexpectedOperationException,"
        "System.SystemException,System.Exception",
        "Hash must be finalized before it can be read.");
}

// net48 AesCryptoServiceProvider.Dispose closes the underlying CSP handle, so
// a later Key/IV access, CreateEncryptor()/CreateDecryptor() or GenerateKey/
// GenerateIV throws ObjectDisposedException (the oracle-pinned behaviour).
[[noreturn]] void ThrowAesDisposed()
{
    il2cpp_throw_new(
        "System.ObjectDisposedException,System.ObjectDisposedException,"
        "System.InvalidOperationException,System.SystemException,System.Exception",
        "Safe handle has been closed");
}

// Forward declaration (defined later in this namespace): clone a managed
// byte[] (net48's `(byte[]) HashValue.Clone()` / key clone).
Il2CppArray* CloneByteArray(const Il2CppArray* src);

// Allocate a hash object through 'klass' and prime its inline context.
template <typename TObj, typename TCtx>
Il2CppObject* HashCreateObject(il2cpp::Il2CppClass* klass, const HashAdapter<TCtx>* adapter)
{
    il2cpp::il2cpp_crypto_register_classes();
    auto* obj = reinterpret_cast<TObj*>(il2cpp_object_new(klass));
    adapter->create(&obj->ctx);
    return reinterpret_cast<Il2CppObject*>(obj);
}

// net48 HashAlgorithm.ComputeHash(byte[]): the disposed check runs BEFORE the
// null check (hashalgorithm.cs). The digest is cached in the object's hashValue
// (a GC reference) and a CLONE is returned -- the cached array is never handed
// out. Initialize() (adapter->create) resets the inline context so the next
// ComputeHash starts fresh (net48 Initialize() at the tail of ComputeHash).
template <typename TObj, typename TCtx>
Il2CppObject* HashComputeArray(Il2CppObject* self, Il2CppArray* data, const HashAdapter<TCtx>* adapter)
{
    auto* obj = reinterpret_cast<TObj*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
    }
    adapter->update(&obj->ctx, data->data, static_cast<size_t>(data->length));
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, adapter->digestSize);
    adapter->final(&obj->ctx, reinterpret_cast<uint8_t*>(result->data));
    obj->hs.hashValue = result;
    Il2CppArray* tmp = CloneByteArray(obj->hs.hashValue);
    adapter->create(&obj->ctx); // Initialize()
    return reinterpret_cast<Il2CppObject*>(tmp);
}

// net48 HashAlgorithm.ComputeHash(byte[], int, int): the (offset, count)
// validation runs BEFORE the disposed check, and mirrors hashalgorithm.cs
// exactly (verified against a live net48 probe):
//   null data          -> ArgumentNullException
//   offset < 0         -> ArgumentOutOfRangeException
//   count < 0 or count > length - offset -> ArgumentException
// The overflow case must be ArgumentException, NOT ArgumentOutOfRangeException
// (net48 throws the base type there -- a catch(ArgumentOutOfRangeException)
// around ComputeHash must NOT swallow it).
template <typename TObj, typename TCtx>
Il2CppObject* HashComputeRange(Il2CppObject* self, Il2CppArray* data,
                               int32_t offset, int32_t count, const HashAdapter<TCtx>* adapter)
{
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
    }
    if (offset < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("offset",
            "offset and count must specify a range within the data array");
    }
    if (count < 0 || count > data->length - offset)
    {
        il2cpp_throw_argument_exception(
            "offset and count must specify a range within the data array");
    }
    auto* obj = reinterpret_cast<TObj*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    if (count > 0)
    {
        adapter->update(&obj->ctx, data->data + offset, static_cast<size_t>(count));
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, adapter->digestSize);
    adapter->final(&obj->ctx, reinterpret_cast<uint8_t*>(result->data));
    obj->hs.hashValue = result;
    Il2CppArray* tmp = CloneByteArray(obj->hs.hashValue);
    adapter->create(&obj->ctx); // Initialize()
    return reinterpret_cast<Il2CppObject*>(tmp);
}

// Prime an already-allocated hash object's context (the legacy
// *CryptoServiceProvider / *Managed newobj path). The object is zero-
// initialized, so hs.state/hs.disposed start false and hashValue null.
template <typename TObj, typename TCtx>
void HashCtor(Il2CppObject* self, const HashAdapter<TCtx>* adapter)
{
    auto* obj = reinterpret_cast<TObj*>(self);
    adapter->create(&obj->ctx);
}

// net48 HashAlgorithm.Initialize: reset the inline context (the abstract
// algorithm Initialize). The object stays usable (Initialize does NOT clear the
// cached digest or the disposed flag -- it only resets the hash state).
template <typename TObj, typename TCtx>
void HashInitialize(Il2CppObject* self, const HashAdapter<TCtx>* adapter)
{
    auto* obj = reinterpret_cast<TObj*>(self);
    adapter->create(&obj->ctx);
}

// net48 HashAlgorithm.Hash getter:
//   disposed          -> ObjectDisposedException
//   state != 0        -> CryptographicUnexpectedOperationException
//   hashValue == null -> NullReferenceException (HashValue.Clone() on null)
//   otherwise         -> a CLONE of the cached digest
template <typename TObj>
Il2CppObject* HashGet(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<TObj*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    if (obj->hs.state != 0)
    {
        ThrowHashNotYetFinalized();
    }
    if (obj->hs.hashValue == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    return reinterpret_cast<Il2CppObject*>(CloneByteArray(obj->hs.hashValue));
}

// net48 HashAlgorithm.Clear(): (this as IDisposable).Dispose() -- clears the
// cached digest and sets the disposed flag (a later Hash/ComputeHash/Transform*
// throws ObjectDisposedException).
template <typename TObj>
void HashClear(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<TObj*>(self);
    if (obj->hs.hashValue != nullptr)
    {
        std::memset(obj->hs.hashValue->data, 0, static_cast<size_t>(obj->hs.hashValue->length));
        obj->hs.hashValue = nullptr;
    }
    obj->hs.disposed = true;
}

// net48 HashAlgorithm.ComputeHash(Stream): the 4096-byte chunked read loop
// (hashalgorithm.cs). The disposed check runs first.
template <typename TObj, typename TCtx>
Il2CppObject* HashComputeStream(Il2CppObject* self, Il2CppObject* stream, const HashAdapter<TCtx>* adapter)
{
    auto* obj = reinterpret_cast<TObj*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    Il2CppArray* buffer = il2cpp_array_new(nullptr, 1, false, 4096);
    int32_t bytesRead;
    do
    {
        bytesRead = il2cpp::runtime::icall_stream_read_bytes(stream, buffer, 0, 4096);
        if (bytesRead > 0)
        {
            adapter->update(&obj->ctx, buffer->data, static_cast<size_t>(bytesRead));
        }
    } while (bytesRead > 0);
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, adapter->digestSize);
    adapter->final(&obj->ctx, reinterpret_cast<uint8_t*>(result->data));
    obj->hs.hashValue = result;
    Il2CppArray* tmp = CloneByteArray(obj->hs.hashValue);
    adapter->create(&obj->ctx); // Initialize()
    return reinterpret_cast<Il2CppObject*>(tmp);
}

// net48 HashAlgorithm.TransformBlock(input, inOff, inCount, output, outOff):
// validate, disposed check, State = 1, HashCore(input), copy the input to the
// output buffer when it differs, return inCount. A null output buffer skips the
// copy (the net48 `(outputBuffer != null)` guard).
template <typename TObj, typename TCtx>
int32_t HashTransformBlock(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                           int32_t inCount, Il2CppArray* output, int32_t outOffset,
                           const HashAdapter<TCtx>* adapter)
{
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("inputBuffer");
    }
    if (inOffset < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("inputOffset",
            "Value must be greater than or equal to 0.");
    }
    if (inCount < 0 || inCount > input->length)
    {
        il2cpp_throw_argument_exception("Value does not fall within the expected range.");
    }
    if ((input->length - inCount) < inOffset)
    {
        il2cpp_throw_argument_exception("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
    }
    auto* obj = reinterpret_cast<TObj*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    obj->hs.state = 1;
    if (inCount > 0)
    {
        adapter->update(&obj->ctx, input->data + inOffset, static_cast<size_t>(inCount));
    }
    if (output != nullptr && (output != input || outOffset != inOffset))
    {
        if (outOffset < 0 || (outOffset + inCount) > output->length)
        {
            il2cpp_throw_argument_exception("Destination is too short.");
        }
        if (inCount > 0)
        {
            std::memcpy(output->data + outOffset, input->data + inOffset, static_cast<size_t>(inCount));
        }
    }
    return inCount;
}

// net48 HashAlgorithm.TransformFinalBlock(input, inOff, inCount): validate,
// disposed check, HashCore(input), cache the digest, State = 0, return a COPY
// of the input (the net48 output is the input bytes, not the digest).
template <typename TObj, typename TCtx>
Il2CppObject* HashTransformFinalBlock(Il2CppObject* self, Il2CppArray* input,
                                      int32_t inOffset, int32_t inCount,
                                      const HashAdapter<TCtx>* adapter)
{
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("inputBuffer");
    }
    if (inOffset < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("inputOffset",
            "Value must be greater than or equal to 0.");
    }
    if (inCount < 0 || inCount > input->length)
    {
        il2cpp_throw_argument_exception("Value does not fall within the expected range.");
    }
    if ((input->length - inCount) < inOffset)
    {
        il2cpp_throw_argument_exception("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
    }
    auto* obj = reinterpret_cast<TObj*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    if (inCount > 0)
    {
        adapter->update(&obj->ctx, input->data + inOffset, static_cast<size_t>(inCount));
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, adapter->digestSize);
    adapter->final(&obj->ctx, reinterpret_cast<uint8_t*>(result->data));
    obj->hs.hashValue = result;
    Il2CppArray* output = il2cpp_array_new(nullptr, 1, false, inCount);
    if (inCount > 0)
    {
        std::memcpy(output->data, input->data + inOffset, static_cast<size_t>(inCount));
    }
    obj->hs.state = 0;
    return reinterpret_cast<Il2CppObject*>(output);
}

//------------------------------------------------------------------------------
// AES key/IV validation (net48 Aes semantics with the section 1.4 AES-192 cut).
//------------------------------------------------------------------------------
void ValidateAesKey(Il2CppArray* key)
{
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    if (key->length == 24)
    {
        il2cpp_throw_not_supported_exception("AES-192 keys are not supported.");
    }
    if (key->length != 16 && key->length != 32)
    {
        il2cpp_throw_new(kCryptoChain,
                         "Specified key is not a valid size for this algorithm.");
    }
}

void ValidateAesIv(Il2CppArray* iv)
{
    if (iv == nullptr)
    {
        il2cpp_throw_argument_null_exception("iv");
    }
    if (iv->length != 16)
    {
        il2cpp_throw_new(kCryptoChain,
                         "Specified initialization vector (IV) does not match the block size for this algorithm.");
    }
}

// net48 Aes.GenerateKey()/GenerateIV(): a fresh CSPRNG key of KeySizeValue bits
// (256 -> 32 bytes) / a fresh 16-byte IV. The generated arrays are stored in
// the object's Key/IV slots (the net48 Key/IV getters generate on demand).
Il2CppArray* GenerateAesKeyBytes()
{
    Il2CppArray* key = il2cpp_array_new(nullptr, 1, false, 32);
    il2cpp_rng_fill(reinterpret_cast<uint8_t*>(key->data), 32);
    return key;
}

Il2CppArray* GenerateAesIvBytes()
{
    Il2CppArray* iv = il2cpp_array_new(nullptr, 1, false, 16);
    il2cpp_rng_fill(reinterpret_cast<uint8_t*>(iv->data), 16);
    return iv;
}

// Build an AesTransform object (CBC + PKCS7) from an explicit key/IV. Both are
// validated (ValidateAesKey/ValidateAesIv) before the object is created.
Il2CppObject* CreateAesTransform(Il2CppArray* key, Il2CppArray* iv, bool decrypt)
{
    ValidateAesKey(key);
    ValidateAesIv(iv);
    il2cpp::il2cpp_crypto_register_classes();
    auto* t = reinterpret_cast<il2cpp::Il2CppAesTransformObject*>(
        il2cpp_object_new(&System_Security_Cryptography_AesTransform_class));
    t->key = key;
    t->iv = iv;
    t->decrypt = decrypt;
    il2cpp_aes_create(&t->ctx, key->data, key->length);
    return reinterpret_cast<Il2CppObject*>(t);
}

// net48 SymmetricAlgorithm.CreateEncryptor()/CreateDecryptor() (parameterless):
// CreateEncryptor(Key, IV) -- the Key/IV getters GENERATE a fresh key/IV when
// the object has none, so the stored slots are populated before the transform is
// built.
Il2CppObject* AesCreateTransformFromStored(Il2CppObject* self, bool decrypt)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppAesObject*>(self);
    if (obj->disposed)
    {
        ThrowAesDisposed();
    }
    if (obj->key == nullptr)
    {
        obj->key = GenerateAesKeyBytes();
    }
    if (obj->iv == nullptr)
    {
        obj->iv = GenerateAesIvBytes();
    }
    return CreateAesTransform(obj->key, obj->iv, decrypt);
}

// The HMAC algorithm descriptor for a receiver: dispatched from the object's
// class pointer (one icall surface serves HMACSHA1/256/384/512).
const il2cpp::runtime::Il2CppHmacHash* HmacDescriptorOf(const il2cpp::Il2CppHmacObject* obj)
{
    if (obj->obj.klass == &System_Security_Cryptography_HMACSHA1_class) return &kIl2CppHmacSha1;
    if (obj->obj.klass == &System_Security_Cryptography_HMACSHA256_class) return &kIl2CppHmacSha256;
    if (obj->obj.klass == &System_Security_Cryptography_HMACSHA384_class) return &kIl2CppHmacSha384;
    return &kIl2CppHmacSha512;
}

// Clone a managed byte[] (net48's `(byte[]) KeyValue.Clone()` / InitializeKey
// key clone). A fresh byte[] of the same length with the same payload.
Il2CppArray* CloneByteArray(const Il2CppArray* src)
{
    Il2CppArray* clone = il2cpp_array_new(nullptr, 1, false, src->length);
    if (src->length > 0)
    {
        std::memcpy(clone->data, src->data, static_cast<size_t>(src->length));
    }
    return clone;
}

// net48 HMAC.InitializeKey: a key longer than the algorithm's block size is
// compressed first (KeyValue = H(key)); otherwise the key is cloned into the
// slot. The result is stored (a GC reference).
Il2CppArray* HmacNormalizeKey(const il2cpp::Il2CppHmacObject* obj, const Il2CppArray* key)
{
    const il2cpp::runtime::Il2CppHmacHash* desc = HmacDescriptorOf(obj);
    if (key->length > static_cast<int32_t>(desc->blockSize))
    {
        il2cpp::runtime::Il2CppHmacCtx ctx;
        desc->create(&ctx);
        desc->update(&ctx, key->data, static_cast<size_t>(key->length));
        Il2CppArray* hashed = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(desc->digestSize));
        desc->final(&ctx, reinterpret_cast<uint8_t*>(hashed->data));
        return hashed;
    }
    return CloneByteArray(key);
}

} // namespace

//------------------------------------------------------------------------------
// HMAC algorithm descriptors for SHA-384 / SHA-512 (SHA-256 and SHA-1 ship
// in hmac.cpp -- kIl2CppHmacSha256/kIl2CppHmacSha1; the RFC 2104 core is
// shared -- one code path, four tables).
//------------------------------------------------------------------------------
const il2cpp::runtime::Il2CppHmacHash kIl2CppHmacSha384 = {
    128, // SHA-384/512 block size B
    48,  // SHA-384 digest size (truncated exit)
    [](void* ctx) { il2cpp_sha384_create(static_cast<Il2CppSha512Ctx*>(ctx)); },
    [](void* ctx, const void* data, size_t len) { il2cpp_sha512_update(static_cast<Il2CppSha512Ctx*>(ctx), data, len); },
    [](void* ctx, uint8_t* out) { il2cpp_sha384_final(static_cast<Il2CppSha512Ctx*>(ctx), out); },
};

const il2cpp::runtime::Il2CppHmacHash kIl2CppHmacSha512 = {
    128, // SHA-384/512 block size B
    64,  // SHA-512 digest size
    [](void* ctx) { il2cpp_sha512_create(static_cast<Il2CppSha512Ctx*>(ctx)); },
    [](void* ctx, const void* data, size_t len) { il2cpp_sha512_update(static_cast<Il2CppSha512Ctx*>(ctx), data, len); },
    [](void* ctx, uint8_t* out) { il2cpp_sha512_final(static_cast<Il2CppSha512Ctx*>(ctx), out); },
};

}} // namespace il2cpp::runtime

//------------------------------------------------------------------------------
// GLOBAL-scope class descriptors (matching the crypto_obj.h extern
// declarations and the transpiler's &System_Security_Cryptography_*_class
// emission -- the versionuri.cpp System_Version_class precedent). One macro
// row per descriptor; the struct layouts are pinned below (the collector
// indexes refFieldBitmap by pointer slot). instanceSize covers header +
// context; NO descriptor has a managed base beyond System.Object.
//------------------------------------------------------------------------------
#define CRYPTO_CLASS(SYM, DOTNAME, STRUCT, REFBITS, REFCOUNT) \
    ::il2cpp::Il2CppClass SYM = {                              \
        DOTNAME,                                               \
        static_cast<int32_t>(sizeof(il2cpp::STRUCT)),          \
        REFBITS,                                               \
        REFCOUNT,                                              \
        &System_Object_class,                                  \
        nullptr, 0, nullptr, 0,                                \
    }

CRYPTO_CLASS(System_Security_Cryptography_MD5_class, "System.Security.Cryptography.MD5", Il2CppMd5Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA1_class, "System.Security.Cryptography.SHA1", Il2CppSha1Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA256_class, "System.Security.Cryptography.SHA256", Il2CppSha256Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA384_class, "System.Security.Cryptography.SHA384", Il2CppSha512Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA512_class, "System.Security.Cryptography.SHA512", Il2CppSha512Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_MD5CryptoServiceProvider_class, "System.Security.Cryptography.MD5CryptoServiceProvider", Il2CppMd5Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA1CryptoServiceProvider_class, "System.Security.Cryptography.SHA1CryptoServiceProvider", Il2CppSha1Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA256CryptoServiceProvider_class, "System.Security.Cryptography.SHA256CryptoServiceProvider", Il2CppSha256Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA384CryptoServiceProvider_class, "System.Security.Cryptography.SHA384CryptoServiceProvider", Il2CppSha512Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA512CryptoServiceProvider_class, "System.Security.Cryptography.SHA512CryptoServiceProvider", Il2CppSha512Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA1Managed_class, "System.Security.Cryptography.SHA1Managed", Il2CppSha1Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA256Managed_class, "System.Security.Cryptography.SHA256Managed", Il2CppSha256Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA384Managed_class, "System.Security.Cryptography.SHA384Managed", Il2CppSha512Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_SHA512Managed_class, "System.Security.Cryptography.SHA512Managed", Il2CppSha512Object, 0b00010u, 1);
CRYPTO_CLASS(System_Security_Cryptography_HMACSHA1_class, "System.Security.Cryptography.HMACSHA1", Il2CppHmacObject, 0b01010u, 2);
CRYPTO_CLASS(System_Security_Cryptography_HMACSHA256_class, "System.Security.Cryptography.HMACSHA256", Il2CppHmacObject, 0b01010u, 2);
CRYPTO_CLASS(System_Security_Cryptography_HMACSHA384_class, "System.Security.Cryptography.HMACSHA384", Il2CppHmacObject, 0b01010u, 2);
CRYPTO_CLASS(System_Security_Cryptography_HMACSHA512_class, "System.Security.Cryptography.HMACSHA512", Il2CppHmacObject, 0b01010u, 2);
CRYPTO_CLASS(System_Security_Cryptography_Aes_class, "System.Security.Cryptography.Aes", Il2CppAesObject, 0b00110u, 2);
CRYPTO_CLASS(System_Security_Cryptography_AesTransform_class, "System.Security.Cryptography.AesTransform", Il2CppAesTransformObject, 0b00110u, 2);
CRYPTO_CLASS(System_Security_Cryptography_RNGCryptoServiceProvider_class, "System.Security.Cryptography.RNGCryptoServiceProvider", Il2CppRngObject, 0u, 0);

#undef CRYPTO_CLASS

namespace il2cpp {

namespace {

// Layout pins: the refFieldBitmap bits must line up with the GC-reference
// slots. The hash contexts (MD5/SHA-1/SHA-256/SHA-512) are 8-aligned on every
// supported target, so they sit at offset 8 right after the pointer header on
// both 4- and 8-byte pointer widths (NOT at one pointer slot on x86).
constexpr size_t AlignUp(size_t value, size_t alignment)
{
    return (value + alignment - 1) & ~(alignment - 1);
}
static_assert(offsetof(Il2CppMd5Object, hs) == sizeof(il2cpp::Il2CppObject),
              "Il2CppMd5Object: the hash state must sit right after the header");
static_assert(offsetof(Il2CppMd5Object, ctx) ==
                  AlignUp(sizeof(il2cpp::Il2CppObject) + sizeof(Il2CppHashState), alignof(runtime::Il2CppMd5Context)),
              "Il2CppMd5Object: ctx must sit in the first 8-aligned slot after the hash state");
static_assert(offsetof(Il2CppSha1Object, hs) == sizeof(il2cpp::Il2CppObject),
              "Il2CppSha1Object: the hash state must sit right after the header");
static_assert(offsetof(Il2CppSha1Object, ctx) ==
                  AlignUp(sizeof(il2cpp::Il2CppObject) + sizeof(Il2CppHashState), alignof(Il2CppSha1Context)),
              "Il2CppSha1Object: ctx must sit in the first 8-aligned slot after the hash state");
static_assert(offsetof(Il2CppSha256Object, hs) == sizeof(il2cpp::Il2CppObject),
              "Il2CppSha256Object: the hash state must sit right after the header");
static_assert(offsetof(Il2CppSha256Object, ctx) ==
                  AlignUp(sizeof(il2cpp::Il2CppObject) + sizeof(Il2CppHashState), alignof(runtime::Il2CppSha256Ctx)),
              "Il2CppSha256Object: ctx must sit in the first 8-aligned slot after the hash state");
static_assert(offsetof(Il2CppSha512Object, hs) == sizeof(il2cpp::Il2CppObject),
              "Il2CppSha512Object: the hash state must sit right after the header");
static_assert(offsetof(Il2CppSha512Object, ctx) ==
                  AlignUp(sizeof(il2cpp::Il2CppObject) + sizeof(Il2CppHashState), alignof(runtime::Il2CppSha512Ctx)),
              "Il2CppSha512Object: ctx must sit in the first 8-aligned slot after the hash state");
static_assert(offsetof(Il2CppHmacObject, hs) == sizeof(il2cpp::Il2CppObject),
              "Il2CppHmacObject: the hash state must sit right after the header");
static_assert(offsetof(Il2CppHmacObject, key) == 3 * sizeof(void*),
              "Il2CppHmacObject: key must sit at slot 3 (bitmap bit 3)");
static_assert(offsetof(Il2CppHmacObject, ctx) == 4 * sizeof(void*),
              "Il2CppHmacObject: ctx must sit at slot 4 (offset 32)");
static_assert(offsetof(Il2CppAesObject, key) == 1 * sizeof(void*),
              "Il2CppAesObject: key must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppAesObject, iv) == 2 * sizeof(void*),
              "Il2CppAesObject: iv must sit at slot 2 (bitmap bit 2)");
static_assert(offsetof(Il2CppAesObject, disposed) == 3 * sizeof(void*),
              "Il2CppAesObject: disposed must follow iv (offset 24)");
static_assert(offsetof(Il2CppAesTransformObject, key) == 1 * sizeof(void*),
              "Il2CppAesTransformObject: key must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppAesTransformObject, iv) == 2 * sizeof(void*),
              "Il2CppAesTransformObject: iv must sit at slot 2 (bitmap bit 2)");
static_assert(offsetof(Il2CppAesTransformObject, ctx) == 3 * sizeof(void*),
              "Il2CppAesTransformObject: ctx must sit at slot 3 (offset 24)");

bool g_cryptoClassesRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_crypto_register_classes -- register every crypto descriptor once
// (idempotent -- il2cpp_class_register dedupes by pointer; called by the
// creation icalls before the first allocation).
//------------------------------------------------------------------------------
void il2cpp_crypto_register_classes()
{
    if (g_cryptoClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Security_Cryptography_MD5_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA1_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA256_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA384_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA512_class);
    il2cpp_class_register(&System_Security_Cryptography_MD5CryptoServiceProvider_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA1CryptoServiceProvider_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA256CryptoServiceProvider_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA384CryptoServiceProvider_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA512CryptoServiceProvider_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA1Managed_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA256Managed_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA384Managed_class);
    il2cpp_class_register(&System_Security_Cryptography_SHA512Managed_class);
    il2cpp_class_register(&System_Security_Cryptography_HMACSHA1_class);
    il2cpp_class_register(&System_Security_Cryptography_HMACSHA256_class);
    il2cpp_class_register(&System_Security_Cryptography_HMACSHA384_class);
    il2cpp_class_register(&System_Security_Cryptography_HMACSHA512_class);
    il2cpp_class_register(&System_Security_Cryptography_Aes_class);
    il2cpp_class_register(&System_Security_Cryptography_AesTransform_class);
    il2cpp_class_register(&System_Security_Cryptography_RNGCryptoServiceProvider_class);
    g_cryptoClassesRegistered = true;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// Hash object icalls (one generic body per shape; the per-algorithm entry
// points bind the object type + adapter).
//------------------------------------------------------------------------------
Il2CppObject* icall_md5_create()
{
    return HashCreateObject<il2cpp::Il2CppMd5Object>(&System_Security_Cryptography_MD5_class, &kMd5);
}
Il2CppObject* icall_sha1_create()
{
    return HashCreateObject<il2cpp::Il2CppSha1Object>(&System_Security_Cryptography_SHA1_class, &kSha1);
}
Il2CppObject* icall_sha256_create()
{
    return HashCreateObject<il2cpp::Il2CppSha256Object>(&System_Security_Cryptography_SHA256_class, &kSha256);
}
Il2CppObject* icall_sha384_create()
{
    return HashCreateObject<il2cpp::Il2CppSha512Object>(&System_Security_Cryptography_SHA384_class, &kSha384);
}
Il2CppObject* icall_sha512_create()
{
    return HashCreateObject<il2cpp::Il2CppSha512Object>(&System_Security_Cryptography_SHA512_class, &kSha512);
}

void icall_md5_ctor(Il2CppObject* self) { HashCtor<il2cpp::Il2CppMd5Object>(self, &kMd5); }
void icall_sha1_ctor(Il2CppObject* self) { HashCtor<il2cpp::Il2CppSha1Object>(self, &kSha1); }
void icall_sha256_ctor(Il2CppObject* self) { HashCtor<il2cpp::Il2CppSha256Object>(self, &kSha256); }
void icall_sha384_ctor(Il2CppObject* self) { HashCtor<il2cpp::Il2CppSha512Object>(self, &kSha384); }
void icall_sha512_ctor(Il2CppObject* self) { HashCtor<il2cpp::Il2CppSha512Object>(self, &kSha512); }

Il2CppObject* icall_md5_compute_hash(Il2CppObject* self, Il2CppArray* data)
{
    return HashComputeArray<il2cpp::Il2CppMd5Object>(self, data, &kMd5);
}
Il2CppObject* icall_sha1_compute_hash(Il2CppObject* self, Il2CppArray* data)
{
    return HashComputeArray<il2cpp::Il2CppSha1Object>(self, data, &kSha1);
}
Il2CppObject* icall_sha256_compute_hash(Il2CppObject* self, Il2CppArray* data)
{
    return HashComputeArray<il2cpp::Il2CppSha256Object>(self, data, &kSha256);
}
Il2CppObject* icall_sha384_compute_hash(Il2CppObject* self, Il2CppArray* data)
{
    return HashComputeArray<il2cpp::Il2CppSha512Object>(self, data, &kSha384);
}
Il2CppObject* icall_sha512_compute_hash(Il2CppObject* self, Il2CppArray* data)
{
    return HashComputeArray<il2cpp::Il2CppSha512Object>(self, data, &kSha512);
}

Il2CppObject* icall_md5_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count)
{
    return HashComputeRange<il2cpp::Il2CppMd5Object>(self, data, offset, count, &kMd5);
}
Il2CppObject* icall_sha1_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count)
{
    return HashComputeRange<il2cpp::Il2CppSha1Object>(self, data, offset, count, &kSha1);
}
Il2CppObject* icall_sha256_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count)
{
    return HashComputeRange<il2cpp::Il2CppSha256Object>(self, data, offset, count, &kSha256);
}
Il2CppObject* icall_sha384_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count)
{
    return HashComputeRange<il2cpp::Il2CppSha512Object>(self, data, offset, count, &kSha384);
}
Il2CppObject* icall_sha512_compute_hash_range(Il2CppObject* self, Il2CppArray* data, int32_t offset, int32_t count)
{
    return HashComputeRange<il2cpp::Il2CppSha512Object>(self, data, offset, count, &kSha512);
}

void icall_md5_dispose(Il2CppObject* self) { HashClear<il2cpp::Il2CppMd5Object>(self); }
void icall_sha1_dispose(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha1Object>(self); }
void icall_sha256_dispose(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha256Object>(self); }
void icall_sha384_dispose(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha512Object>(self); }
void icall_sha512_dispose(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha512Object>(self); }

//-----------------------------------------------------------------------------
// HashAlgorithm object surface: HashSize (the digest size in BITS -- net48
// HashSizeValue), Initialize (reset the inline context), the Hash getter
// (the cached digest, cloned), Clear/Dispose (the disposed flag) and the
// ICryptoTransform streaming surface (TransformBlock/TransformFinalBlock) plus
// ComputeHash(Stream).
//-----------------------------------------------------------------------------
int32_t icall_md5_get_hash_size(Il2CppObject*) { return kMd5.digestSize * 8; }
int32_t icall_sha1_get_hash_size(Il2CppObject*) { return kSha1.digestSize * 8; }
int32_t icall_sha256_get_hash_size(Il2CppObject*) { return kSha256.digestSize * 8; }
int32_t icall_sha384_get_hash_size(Il2CppObject*) { return kSha384.digestSize * 8; }
int32_t icall_sha512_get_hash_size(Il2CppObject*) { return kSha512.digestSize * 8; }

void icall_md5_initialize(Il2CppObject* self) { HashInitialize<il2cpp::Il2CppMd5Object>(self, &kMd5); }
void icall_sha1_initialize(Il2CppObject* self) { HashInitialize<il2cpp::Il2CppSha1Object>(self, &kSha1); }
void icall_sha256_initialize(Il2CppObject* self) { HashInitialize<il2cpp::Il2CppSha256Object>(self, &kSha256); }
void icall_sha384_initialize(Il2CppObject* self) { HashInitialize<il2cpp::Il2CppSha512Object>(self, &kSha384); }
void icall_sha512_initialize(Il2CppObject* self) { HashInitialize<il2cpp::Il2CppSha512Object>(self, &kSha512); }

Il2CppObject* icall_md5_get_hash(Il2CppObject* self) { return HashGet<il2cpp::Il2CppMd5Object>(self); }
Il2CppObject* icall_sha1_get_hash(Il2CppObject* self) { return HashGet<il2cpp::Il2CppSha1Object>(self); }
Il2CppObject* icall_sha256_get_hash(Il2CppObject* self) { return HashGet<il2cpp::Il2CppSha256Object>(self); }
Il2CppObject* icall_sha384_get_hash(Il2CppObject* self) { return HashGet<il2cpp::Il2CppSha512Object>(self); }
Il2CppObject* icall_sha512_get_hash(Il2CppObject* self) { return HashGet<il2cpp::Il2CppSha512Object>(self); }

void icall_md5_clear(Il2CppObject* self) { HashClear<il2cpp::Il2CppMd5Object>(self); }
void icall_sha1_clear(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha1Object>(self); }
void icall_sha256_clear(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha256Object>(self); }
void icall_sha384_clear(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha512Object>(self); }
void icall_sha512_clear(Il2CppObject* self) { HashClear<il2cpp::Il2CppSha512Object>(self); }

Il2CppObject* icall_md5_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream)
{
    return HashComputeStream<il2cpp::Il2CppMd5Object>(self, stream, &kMd5);
}
Il2CppObject* icall_sha1_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream)
{
    return HashComputeStream<il2cpp::Il2CppSha1Object>(self, stream, &kSha1);
}
Il2CppObject* icall_sha256_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream)
{
    return HashComputeStream<il2cpp::Il2CppSha256Object>(self, stream, &kSha256);
}
Il2CppObject* icall_sha384_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream)
{
    return HashComputeStream<il2cpp::Il2CppSha512Object>(self, stream, &kSha384);
}
Il2CppObject* icall_sha512_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream)
{
    return HashComputeStream<il2cpp::Il2CppSha512Object>(self, stream, &kSha512);
}

int32_t icall_md5_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                  int32_t inCount, Il2CppArray* output, int32_t outOffset)
{
    return HashTransformBlock<il2cpp::Il2CppMd5Object>(self, input, inOffset, inCount, output, outOffset, &kMd5);
}
int32_t icall_sha1_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                   int32_t inCount, Il2CppArray* output, int32_t outOffset)
{
    return HashTransformBlock<il2cpp::Il2CppSha1Object>(self, input, inOffset, inCount, output, outOffset, &kSha1);
}
int32_t icall_sha256_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                     int32_t inCount, Il2CppArray* output, int32_t outOffset)
{
    return HashTransformBlock<il2cpp::Il2CppSha256Object>(self, input, inOffset, inCount, output, outOffset, &kSha256);
}
int32_t icall_sha384_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                     int32_t inCount, Il2CppArray* output, int32_t outOffset)
{
    return HashTransformBlock<il2cpp::Il2CppSha512Object>(self, input, inOffset, inCount, output, outOffset, &kSha384);
}
int32_t icall_sha512_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                     int32_t inCount, Il2CppArray* output, int32_t outOffset)
{
    return HashTransformBlock<il2cpp::Il2CppSha512Object>(self, input, inOffset, inCount, output, outOffset, &kSha512);
}

Il2CppObject* icall_md5_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                              int32_t inOffset, int32_t inCount)
{
    return HashTransformFinalBlock<il2cpp::Il2CppMd5Object>(self, input, inOffset, inCount, &kMd5);
}
Il2CppObject* icall_sha1_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                               int32_t inOffset, int32_t inCount)
{
    return HashTransformFinalBlock<il2cpp::Il2CppSha1Object>(self, input, inOffset, inCount, &kSha1);
}
Il2CppObject* icall_sha256_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                                 int32_t inOffset, int32_t inCount)
{
    return HashTransformFinalBlock<il2cpp::Il2CppSha256Object>(self, input, inOffset, inCount, &kSha256);
}
Il2CppObject* icall_sha384_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                                 int32_t inOffset, int32_t inCount)
{
    return HashTransformFinalBlock<il2cpp::Il2CppSha512Object>(self, input, inOffset, inCount, &kSha384);
}
Il2CppObject* icall_sha512_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                                 int32_t inOffset, int32_t inCount)
{
    return HashTransformFinalBlock<il2cpp::Il2CppSha512Object>(self, input, inOffset, inCount, &kSha512);
}

//------------------------------------------------------------------------------
// HMAC object icalls.
//------------------------------------------------------------------------------
void icall_hmac_ctor(Il2CppObject* self, Il2CppArray* key)
{
    il2cpp::il2cpp_crypto_register_classes();
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    obj->key = HmacNormalizeKey(obj, key);
    // The context is (re)built on every ComputeHash from the stored key --
    // no state survives between calls (crypto-domain.md section 2.1 HMAC contract).
}

Il2CppObject* icall_hmac_compute_hash(Il2CppObject* self, Il2CppArray* data)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    // net48 HashAlgorithm.ComputeHash(byte[]): disposed check before null check.
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    if (data == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
    }
    const il2cpp::runtime::Il2CppHmacHash* desc = HmacDescriptorOf(obj);
    il2cpp_hmac_create(&obj->ctx, desc, obj->key->data, static_cast<size_t>(obj->key->length));
    il2cpp_hmac_update(&obj->ctx, data->data, static_cast<size_t>(data->length));
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(desc->digestSize));
    il2cpp_hmac_final(&obj->ctx, reinterpret_cast<uint8_t*>(result->data));
    obj->hs.hashValue = result;
    return reinterpret_cast<Il2CppObject*>(CloneByteArray(obj->hs.hashValue));
}

void icall_hmac_dispose(Il2CppObject* self)
{
    // net48 key clearing + the HashAlgorithm disposed flag: clear the cached
    // digest, null the stored key (collectable) and mark the object disposed.
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    if (obj->hs.hashValue != nullptr)
    {
        std::memset(obj->hs.hashValue->data, 0, static_cast<size_t>(obj->hs.hashValue->length));
        obj->hs.hashValue = nullptr;
    }
    obj->key = nullptr;
    obj->hs.disposed = true;
}

//------------------------------------------------------------------------------
// HMAC Key property + the keyless ctor (net48 HMAC.Key / InitializeKey and
// HMACSHAx() contract).
//
//   icall_hmac_get_key(self)      -- the Key getter returns a CLONE of the
//     stored key (net48 HMAC.Key: `(byte[]) KeyValue.Clone()` -- the identity
//     is never the stored array).
//   icall_hmac_set_key(self, key) -- InitializeKey: a null key throws
//     NullReferenceException (net48 reads value.Length before any guard); a key
//     longer than the algorithm's block size is compressed first
//     (KeyValue = H(key)), otherwise it is cloned into the stored slot.
//   icall_hmac_ctor_default(self) -- the keyless HMACSHAx() ctor: a fresh
//     CSPRNG key of the algorithm's block size (net48 Utils.GenerateRandom).
//------------------------------------------------------------------------------
Il2CppObject* icall_hmac_get_key(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    if (obj->key == nullptr)
    {
        return nullptr;
    }
    return reinterpret_cast<Il2CppObject*>(CloneByteArray(obj->key));
}

void icall_hmac_set_key(Il2CppObject* self, Il2CppArray* key)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    // net48 InitializeKey dereferences key.Length before any guard -> NRE.
    if (key == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    obj->key = HmacNormalizeKey(obj, key);
}

void icall_hmac_ctor_default(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    il2cpp::il2cpp_crypto_register_classes();
    const il2cpp::runtime::Il2CppHmacHash* desc = HmacDescriptorOf(obj);
    obj->key = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(desc->blockSize));
    il2cpp_rng_fill(reinterpret_cast<uint8_t*>(obj->key->data), desc->blockSize);
}

//-----------------------------------------------------------------------------
// HMAC HashAlgorithm surface (HMACSHAx : KeyedHashAlgorithm : HashAlgorithm):
// the same cached-digest / disposed / streaming contract as the hash family.
// The HMAC context is created from the stored key on the first block of a
// streaming session (state 0 -> create) and reused for the following blocks.
//-----------------------------------------------------------------------------
Il2CppObject* icall_hmac_get_hash(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    if (obj->hs.state != 0)
    {
        ThrowHashNotYetFinalized();
    }
    if (obj->hs.hashValue == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    return reinterpret_cast<Il2CppObject*>(CloneByteArray(obj->hs.hashValue));
}

void icall_hmac_clear(Il2CppObject* self) { icall_hmac_dispose(self); }

Il2CppObject* icall_hmac_compute_hash_stream(Il2CppObject* self, Il2CppObject* stream)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    const il2cpp::runtime::Il2CppHmacHash* desc = HmacDescriptorOf(obj);
    il2cpp_hmac_create(&obj->ctx, desc, obj->key->data, static_cast<size_t>(obj->key->length));
    Il2CppArray* buffer = il2cpp_array_new(nullptr, 1, false, 4096);
    int32_t bytesRead;
    do
    {
        bytesRead = il2cpp::runtime::icall_stream_read_bytes(stream, buffer, 0, 4096);
        if (bytesRead > 0)
        {
            il2cpp_hmac_update(&obj->ctx, buffer->data, static_cast<size_t>(bytesRead));
        }
    } while (bytesRead > 0);
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(desc->digestSize));
    il2cpp_hmac_final(&obj->ctx, reinterpret_cast<uint8_t*>(result->data));
    obj->hs.hashValue = result;
    return reinterpret_cast<Il2CppObject*>(CloneByteArray(obj->hs.hashValue));
}

int32_t icall_hmac_transform_block(Il2CppObject* self, Il2CppArray* input, int32_t inOffset,
                                   int32_t inCount, Il2CppArray* output, int32_t outOffset)
{
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("inputBuffer");
    }
    if (inOffset < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("inputOffset",
            "Value must be greater than or equal to 0.");
    }
    if (inCount < 0 || inCount > input->length)
    {
        il2cpp_throw_argument_exception("Value does not fall within the expected range.");
    }
    if ((input->length - inCount) < inOffset)
    {
        il2cpp_throw_argument_exception("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
    }
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    const il2cpp::runtime::Il2CppHmacHash* desc = HmacDescriptorOf(obj);
    if (obj->hs.state == 0)
    {
        il2cpp_hmac_create(&obj->ctx, desc, obj->key->data, static_cast<size_t>(obj->key->length));
    }
    obj->hs.state = 1;
    if (inCount > 0)
    {
        il2cpp_hmac_update(&obj->ctx, input->data + inOffset, static_cast<size_t>(inCount));
    }
    if (output != nullptr && (output != input || outOffset != inOffset))
    {
        if (outOffset < 0 || (outOffset + inCount) > output->length)
        {
            il2cpp_throw_argument_exception("Destination is too short.");
        }
        if (inCount > 0)
        {
            std::memcpy(output->data + outOffset, input->data + inOffset, static_cast<size_t>(inCount));
        }
    }
    return inCount;
}

Il2CppObject* icall_hmac_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                               int32_t inOffset, int32_t inCount)
{
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("inputBuffer");
    }
    if (inOffset < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("inputOffset",
            "Value must be greater than or equal to 0.");
    }
    if (inCount < 0 || inCount > input->length)
    {
        il2cpp_throw_argument_exception("Value does not fall within the expected range.");
    }
    if ((input->length - inCount) < inOffset)
    {
        il2cpp_throw_argument_exception("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
    }
    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(self);
    if (obj->hs.disposed)
    {
        ThrowHashDisposed();
    }
    const il2cpp::runtime::Il2CppHmacHash* desc = HmacDescriptorOf(obj);
    if (obj->hs.state == 0)
    {
        il2cpp_hmac_create(&obj->ctx, desc, obj->key->data, static_cast<size_t>(obj->key->length));
    }
    if (inCount > 0)
    {
        il2cpp_hmac_update(&obj->ctx, input->data + inOffset, static_cast<size_t>(inCount));
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(desc->digestSize));
    il2cpp_hmac_final(&obj->ctx, reinterpret_cast<uint8_t*>(result->data));
    obj->hs.hashValue = result;
    obj->hs.state = 0;
    Il2CppArray* output = il2cpp_array_new(nullptr, 1, false, inCount);
    if (inCount > 0)
    {
        std::memcpy(output->data, input->data + inOffset, static_cast<size_t>(inCount));
    }
    return reinterpret_cast<Il2CppObject*>(output);
}

//------------------------------------------------------------------------------
// Aes object icalls.
//------------------------------------------------------------------------------
Il2CppObject* icall_aes_create()
{
    il2cpp::il2cpp_crypto_register_classes();
    return il2cpp_object_new(&System_Security_Cryptography_Aes_class);
}

void icall_aes_set_key(Il2CppObject* self, Il2CppArray* key)
{
    ValidateAesKey(key);
    reinterpret_cast<il2cpp::Il2CppAesObject*>(self)->key = key;
}

Il2CppObject* icall_aes_get_key(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppAesObject*>(self);
    if (obj->disposed)
    {
        ThrowAesDisposed();
    }
    return reinterpret_cast<Il2CppObject*>(obj->key);
}

void icall_aes_set_iv(Il2CppObject* self, Il2CppArray* iv)
{
    ValidateAesIv(iv);
    reinterpret_cast<il2cpp::Il2CppAesObject*>(self)->iv = iv;
}

Il2CppObject* icall_aes_get_iv(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppAesObject*>(self);
    if (obj->disposed)
    {
        ThrowAesDisposed();
    }
    return reinterpret_cast<Il2CppObject*>(obj->iv);
}

Il2CppObject* icall_aes_create_encryptor(Il2CppObject* self, Il2CppArray* key, Il2CppArray* iv)
{
    (void)self;
    return CreateAesTransform(key, iv, false);
}

Il2CppObject* icall_aes_create_decryptor(Il2CppObject* self, Il2CppArray* key, Il2CppArray* iv)
{
    (void)self;
    return CreateAesTransform(key, iv, true);
}

Il2CppObject* icall_aes_create_encryptor_self(Il2CppObject* self)
{
    return AesCreateTransformFromStored(self, false);
}

Il2CppObject* icall_aes_create_decryptor_self(Il2CppObject* self)
{
    return AesCreateTransformFromStored(self, true);
}

Il2CppObject* icall_aes_transform_final_block(Il2CppObject* self, Il2CppArray* input,
                                              int32_t offset, int32_t count)
{
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("inputBuffer");
    }
    if (offset < 0 || count < 0 || offset > input->length || count > input->length - offset)
    {
        il2cpp_throw_argument_out_of_range_exception("inputOffset",
            "offset and count must specify a range within the input array");
    }
    auto* t = reinterpret_cast<il2cpp::Il2CppAesTransformObject*>(self);
    const uint8_t* src = reinterpret_cast<const uint8_t*>(input->data) + offset;
    const uint8_t* iv = reinterpret_cast<const uint8_t*>(t->iv->data);
    const uint8_t* key = reinterpret_cast<const uint8_t*>(t->key->data);
    const int32_t keyLen = t->key->length;

    if (!t->decrypt)
    {
        // CBC encrypt + PKCS7 pad: output length is count rounded up to a
        // block (block-aligned inputs gain a full 0x10 pad block).
        const int32_t outLen = count + (16 - count % 16);
        Il2CppArray* out = il2cpp_array_new(nullptr, 1, false, outLen);
        il2cpp_aes_encrypt_cbc(key, keyLen, iv, src, count, out->data);
        return reinterpret_cast<Il2CppObject*>(out);
    }

    // CBC decrypt + PKCS7 strip: decrypt in place (the array holds count
    // bytes >= the plaintext length), then trim to the plaintext length.
    // Invalid padding -> CryptographicException (aes.h).
    Il2CppArray* out = il2cpp_array_new(nullptr, 1, false, count);
    const int32_t plain = il2cpp_aes_decrypt_cbc(key, keyLen, iv, src, count, out->data);
    if (plain != count)
    {
        Il2CppArray* trimmed = il2cpp_array_new(nullptr, 1, false, plain);
        std::memcpy(trimmed->data, out->data, static_cast<size_t>(plain));
        return reinterpret_cast<Il2CppObject*>(trimmed);
    }
    return reinterpret_cast<Il2CppObject*>(out);
}

Il2CppObject* icall_aes_generate_key(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppAesObject*>(self);
    if (obj->disposed)
    {
        ThrowAesDisposed();
    }
    Il2CppArray* key = GenerateAesKeyBytes();
    obj->key = key;
    return reinterpret_cast<Il2CppObject*>(key);
}

Il2CppObject* icall_aes_generate_iv(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppAesObject*>(self);
    if (obj->disposed)
    {
        ThrowAesDisposed();
    }
    Il2CppArray* iv = GenerateAesIvBytes();
    obj->iv = iv;
    return reinterpret_cast<Il2CppObject*>(iv);
}

void icall_aes_dispose(Il2CppObject* self)
{
    auto* obj = reinterpret_cast<il2cpp::Il2CppAesObject*>(self);
    obj->key = nullptr;
    obj->iv = nullptr;
    obj->disposed = true;
}

//------------------------------------------------------------------------------
// RNG object icalls.
//------------------------------------------------------------------------------
void icall_rng_ctor(Il2CppObject*) {}  // stateless no-op (net48)

void icall_rng_get_bytes(Il2CppObject*, Il2CppArray* bytes)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    il2cpp_rng_fill(reinterpret_cast<uint8_t*>(bytes->data), static_cast<size_t>(bytes->length));
}

void icall_rng_get_nonzero_bytes(Il2CppObject*, Il2CppArray* bytes)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    // Rejection sampling: fill, then re-roll every zero byte until non-zero
    // (net48 GetNonZeroBytes contract; expected re-rolls ~1 byte per 256).
    uint8_t* p = reinterpret_cast<uint8_t*>(bytes->data);
    for (int32_t i = 0; i < bytes->length; ++i)
    {
        il2cpp_rng_fill(p + i, 1);
        while (p[i] == 0)
        {
            il2cpp_rng_fill(p + i, 1);
        }
    }
}

void icall_rng_fill_static(Il2CppArray* bytes)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("data");
    }
    il2cpp_rng_fill(reinterpret_cast<uint8_t*>(bytes->data), static_cast<size_t>(bytes->length));
}

void icall_rng_dispose(Il2CppObject*) {}  // stateless -- nothing to release

}} // namespace il2cpp::runtime
