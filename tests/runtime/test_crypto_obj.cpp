//------------------------------------------------------------------------------
// test_crypto_obj.cpp -- System.Security.Cryptography OBJECT-layer runtime
// tests (crypto_obj.h / crypto_obj.cpp -- the T4 object icalls).
//
// Coverage (normal + edge + error paths):
//   1. Hash OBJECT STREAMING (the T4 contract -- the object carries the
//      inline create/update/final context):
//        - icall_<algo>_create() allocates through the algorithm's GLOBAL
//          class descriptor (klass identity asserted) and primes the ctx
//        - ComputeHash("abc") / ("") match the RFC 1321 / FIPS 180-4
//          vectors; repeated ComputeHash on the SAME instance returns the
//          same digest (each call re-creates the context -- net48)
//        - ComputeHash(byte[], int, int): range(0, n) == full digest;
//          range(1, 2) on "abc" == the raw one-shot of "bc" (offset/count
//          handling proven against the pure cores)
//        - legacy newobj path: il2cpp_object_new(&...CryptoServiceProvider_
//          class) + icall_<algo>_ctor hashes identically (aliased class)
//        - error paths: null data -> ArgumentNullException; out-of-range
//          offset/count -> ArgumentOutOfRangeException; dispose no-crash
//   2. HMAC objects (RFC 2104; RFC 4231/2202 case-1 vectors): klass-based
//      algorithm dispatch for all four HMACSHAx classes, repeated ComputeHash
//      stability, null key/data -> ArgumentNullException, dispose nulls the
//      stored key reference.
//   3. AES objects (SP 800-38A F.2.1 + CBC/PKCS7 round trips):
//        - Aes.Create() -> set Key/IV -> get Key/IV round trip
//        - CreateEncryptor(key, iv).TransformFinalBlock(plain) == the
//          F.2.1 first ciphertext block (mode-layer CBC KAT)
//        - CreateEncryptor/CreateDecryptor round trips over lengths
//          {0, 1, 15, 16, 17, 100} (AES-128 and AES-256 keys)
//        - GenerateKey/GenerateIV output lengths 32/16
//        - error paths: bad key/iv sizes -> CryptographicException (24 ->
//          NotSupportedException); null key/iv -> ArgumentNullException;
//          null input / bad range -> ArgumentNull/OutOfRange; tampered
//          ciphertext -> CryptographicException
//   4. RNG objects: GetBytes honors the length, two fills differ,
//      GetNonZeroBytes has no zero bytes, Fill(byte[]) static fills,
//      null array -> ArgumentNullException, dispose no-crash.
//
// Plain executable + minimal assertion harness (see test_sha256.cpp).
//------------------------------------------------------------------------------

#include "crypto_obj.h"
#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"
#include "il2cpp_streamio.h" // il2cpp_stream_memory_from_bytes (ComputeHash(Stream))
#include "stubs.h" // crypto/x509 LOUD stubs (parity-T7 section below)

#include <cstdint>
#include <cstdio>
#include <cstring>

// The runtime types live in namespace il2cpp (arrays/objects/classes) and
// il2cpp::runtime (pure hash cores); the test helpers below are global.
using il2cpp::Il2CppArray;
using il2cpp::Il2CppClass;
using il2cpp::Il2CppObject;
using il2cpp::il2cpp_array_new;
using il2cpp::il2cpp_object_new;
using il2cpp::il2cpp_sha1_hash_bytes;
using il2cpp::runtime::il2cpp_sha256_hash_bytes;
using il2cpp::runtime::il2cpp_sha384_hash_bytes;
using il2cpp::runtime::il2cpp_sha512_hash_bytes;

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

void ExpectHex(const uint8_t* bytes, int count, const char* expected, const char* what)
{
    char hex[64 * 2 + 1];
    for (int i = 0; i < count && i < 64; ++i)
        std::snprintf(hex + i * 2, sizeof(hex) - static_cast<size_t>(i) * 2, "%02x", bytes[i]);
    hex[count * 2] = '\0';
    Expect(std::strcmp(hex, expected) == 0, what);
}

void HexToBytes(const char* hex, uint8_t* out)
{
    for (const char* p = hex; *p != '\0'; p += 2)
    {
        const char hi = p[0], lo = p[1];
        const auto nib = [](char c) -> uint8_t {
            return static_cast<uint8_t>(c <= '9' ? c - '0' : c - 'a' + 10);
        };
        *out++ = static_cast<uint8_t>((nib(hi) << 4) | nib(lo));
    }
}

// A managed byte[] from a hex string (the icall argument shape).
Il2CppArray* MakeBytes(const char* hex)
{
    const size_t len = std::strlen(hex) / 2;
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(len));
    HexToBytes(hex, reinterpret_cast<uint8_t*>(arr->data));
    return arr;
}

// A managed byte[] from a raw byte buffer.
Il2CppArray* MakeBytes(const uint8_t* data, int32_t len)
{
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, len);
    std::memcpy(arr->data, data, static_cast<size_t>(len));
    return arr;
}

// The icall result (Il2CppObject*) viewed as a byte[]; asserts the length.
Il2CppArray* AsBytes(Il2CppObject* obj, int32_t expectedLen, const char* what)
{
    auto* arr = reinterpret_cast<Il2CppArray*>(obj);
    Expect(arr->length == expectedLen, what);
    return arr;
}

// Template: accepts non-capturing function pointers and capturing lambdas
// alike (avoids <functional>, whose libc++ headers are broken on this
// mingw-w64 snapshot).
template <typename Fn>
bool ThrowsType(Fn fn, const char* typeName)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeName) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// EXACT top-of-chain type match. The .NET type chain is
// "{Type},{Type},{Base1},{Base2},..." (the first element repeats the thrown
// type); strstr-based matching would accept ArgumentOutOfRangeException for a
// "System.ArgumentException" assertion because the AORE chain lists
// ArgumentException as a base. net48's ComputeHash(byte[], int, int) throws
// the BASE ArgumentException for count<0 / overflow -- that distinction is
// observable (catch(ArgumentOutOfRangeException) must NOT swallow it), so the
// test must pin the thrown type exactly.
template <typename Fn>
bool ThrowsExactType(Fn fn, const char* typeName)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* t = e.type_name();
        const size_t n = std::strlen(typeName);
        return std::strncmp(t, typeName, n) == 0 && (t[n] == ',' || t[n] == '\0');
    }
    catch (...)
    {
        return false;
    }
    return false;
}

//------------------------------------------------------------------------------
// Hash object test spec: one row per algorithm (class + icall family +
// vectors + a raw one-shot adapter for the range cross-check).
//------------------------------------------------------------------------------
struct HashObjSpec
{
    il2cpp::Il2CppClass* klass;
    Il2CppObject* (*create)();
    Il2CppObject* (*compute)(Il2CppObject*, Il2CppArray*);
    Il2CppObject* (*computeRange)(Il2CppObject*, Il2CppArray*, int32_t, int32_t);
    void (*oneShotRaw)(const void*, size_t, uint8_t*);
    int32_t digestLen;
    const char* abcHex;
    const char* emptyHex;
};

void Md5OneShotRaw(const void* data, size_t len, uint8_t* out)
{
    Il2CppArray* a = il2cpp::runtime::il2cpp_md5_hash_bytes(data, static_cast<int32_t>(len));
    std::memcpy(out, a->data, 16);
}

void RunHashObjTests(const HashObjSpec& s, const char* what)
{
    // --- object streaming: create -> compute (twice) ---
    Il2CppObject* obj = s.create();
    Expect(obj != nullptr, what);
    Expect(obj->klass == s.klass, what);

    Il2CppArray* abc = MakeBytes("616263"); // "abc"
    Il2CppArray* empty = MakeBytes("");     // ""
    Il2CppArray* d1 = AsBytes(s.compute(obj, abc), s.digestLen, what);
    ExpectHex(reinterpret_cast<uint8_t*>(d1->data), s.digestLen, s.abcHex, what);
    // Repeated ComputeHash on the SAME instance: independent per call.
    Il2CppArray* d2 = AsBytes(s.compute(obj, abc), s.digestLen, what);
    Expect(std::memcmp(d1->data, d2->data, static_cast<size_t>(s.digestLen)) == 0, what);
    Il2CppArray* dEmpty = AsBytes(s.compute(obj, empty), s.digestLen, what);
    ExpectHex(reinterpret_cast<uint8_t*>(dEmpty->data), s.digestLen, s.emptyHex, what);

    // --- range overload: (0, n) == full digest; (1, 2) == raw one-shot of "bc" ---
    Il2CppArray* full = AsBytes(s.computeRange(obj, abc, 0, 3), s.digestLen, what);
    Expect(std::memcmp(full->data, d1->data, static_cast<size_t>(s.digestLen)) == 0, what);
    uint8_t bcExpected[64];
    s.oneShotRaw("bc", 2, bcExpected);
    Il2CppArray* bc = AsBytes(s.computeRange(obj, abc, 1, 2), s.digestLen, what);
    Expect(std::memcmp(bc->data, bcExpected, static_cast<size_t>(s.digestLen)) == 0, what);

    // --- error paths (net48 ComputeHash(byte[], int, int) semantics) ---
    Expect(ThrowsType([&]() { s.compute(obj, nullptr); }, "System.ArgumentNullException"), what);
    // offset < 0 -> ArgumentOutOfRangeException (net48)
    Expect(ThrowsExactType([&]() { s.computeRange(obj, abc, -1, 2); }, "System.ArgumentOutOfRangeException"), what);
    // count < 0 -> ArgumentException (net48, NOT ArgumentOutOfRangeException)
    Expect(ThrowsExactType([&]() { s.computeRange(obj, abc, 0, -1); }, "System.ArgumentException"), what);
    // offset+count > length -> ArgumentException (net48, NOT AORE)
    Expect(ThrowsExactType([&]() { s.computeRange(obj, abc, 0, 4); }, "System.ArgumentException"), what);
    Expect(ThrowsExactType([&]() { s.computeRange(obj, abc, 3, 1); }, "System.ArgumentException"), what);
    // offset > length (even with count==0) -> ArgumentException (net48)
    Expect(ThrowsExactType([&]() { s.computeRange(obj, abc, 4, 0); }, "System.ArgumentException"), what);
    // A zero-length range at the end boundary is legal.
    Il2CppArray* zero = AsBytes(s.computeRange(obj, abc, 3, 0), s.digestLen, what);
    ExpectHex(reinterpret_cast<uint8_t*>(zero->data), s.digestLen, s.emptyHex, what);
}

//------------------------------------------------------------------------------
// HMAC object test spec.
//------------------------------------------------------------------------------
struct HmacObjSpec
{
    il2cpp::Il2CppClass* klass;
    const char* abcHex; // RFC 2202/4231 case 1 ("Hi There", key 0x0b x20)
    int32_t digestLen;
};

void RunHmacObjTests(const HmacObjSpec& s, const char* what)
{
    il2cpp::il2cpp_crypto_register_classes();
    uint8_t keyBuf[20];
    std::memset(keyBuf, 0x0b, 20);
    Il2CppArray* key = MakeBytes(keyBuf, 20);
    Il2CppArray* hiThere = MakeBytes("4869205468657265"); // "Hi There"

    auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(
        il2cpp_object_new(s.klass));
    il2cpp::runtime::icall_hmac_ctor(reinterpret_cast<Il2CppObject*>(obj), key);
    // net48 HMAC.InitializeKey clones the key (the stored slot is never the
    // caller's array) -- batch 5 introduced the clone; the assertion now pins
    // the clone (identity) + the payload.
    Expect(obj->key != key, what);
    Expect(obj->key->length == key->length &&
           std::memcmp(obj->key->data, key->data, static_cast<size_t>(key->length)) == 0, what);

    Il2CppArray* m1 = AsBytes(il2cpp::runtime::icall_hmac_compute_hash(
        reinterpret_cast<Il2CppObject*>(obj), hiThere), s.digestLen, what);
    ExpectHex(reinterpret_cast<uint8_t*>(m1->data), s.digestLen, s.abcHex, what);
    // Repeated ComputeHash on the same object: stable (context rebuilt
    // from the stored key -- zero cross-call state).
    Il2CppArray* m2 = AsBytes(il2cpp::runtime::icall_hmac_compute_hash(
        reinterpret_cast<Il2CppObject*>(obj), hiThere), s.digestLen, what);
    Expect(std::memcmp(m1->data, m2->data, static_cast<size_t>(s.digestLen)) == 0, what);

    Expect(ThrowsType([]() { il2cpp::runtime::icall_hmac_ctor(nullptr, nullptr); },
                      "System.ArgumentNullException"), what);
    Expect(ThrowsType([&]() {
                il2cpp::runtime::icall_hmac_compute_hash(reinterpret_cast<Il2CppObject*>(obj), nullptr);
            }, "System.ArgumentNullException"), what);

    il2cpp::runtime::icall_hmac_dispose(reinterpret_cast<Il2CppObject*>(obj));
    Expect(obj->key == nullptr, what); // key reference cleared (collectable)
}

//------------------------------------------------------------------------------
// AES object round-trip helper.
//------------------------------------------------------------------------------
void AesRoundTrip(const char* keyHex, const char* ivHex, const uint8_t* plain,
                  int32_t plainLen, const char* what)
{
    Il2CppArray* key = MakeBytes(keyHex);
    Il2CppArray* iv = MakeBytes(ivHex);
    Il2CppArray* input = MakeBytes(plain, plainLen);

    Il2CppObject* aes = il2cpp::runtime::icall_aes_create();
    il2cpp::runtime::icall_aes_set_key(aes, key);
    il2cpp::runtime::icall_aes_set_iv(aes, iv);
    Expect(il2cpp::runtime::icall_aes_get_key(aes) == reinterpret_cast<Il2CppObject*>(key), what);
    Expect(il2cpp::runtime::icall_aes_get_iv(aes) == reinterpret_cast<Il2CppObject*>(iv), what);

    Il2CppObject* enc = il2cpp::runtime::icall_aes_create_encryptor(aes, key, iv);
    Il2CppArray* ct = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(
        enc, input, 0, plainLen), plainLen + (16 - plainLen % 16), what);

    Il2CppObject* dec = il2cpp::runtime::icall_aes_create_decryptor(aes, key, iv);
    Il2CppArray* back = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(
        dec, ct, 0, ct->length), plainLen, what);
    Expect(std::memcmp(back->data, plain, static_cast<size_t>(plainLen)) == 0, what);

    // Offset/count forms: encrypt the subrange [1, plainLen-1) only.
    if (plainLen >= 2)
    {
        Il2CppArray* sub = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(
            enc, input, 1, plainLen - 1), (plainLen - 1) + (16 - (plainLen - 1) % 16), what);
        Il2CppArray* subBack = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(
            dec, sub, 0, sub->length), plainLen - 1, what);
        Expect(std::memcmp(subBack->data, plain + 1, static_cast<size_t>(plainLen - 1)) == 0, what);
    }

    il2cpp::runtime::icall_aes_dispose(aes);
}

//------------------------------------------------------------------------------
// HashAlgorithm.Hash / Clear / Transform* / ComputeHash(Stream) surface spec
// (batch 6 -- the net48 HashAlgorithm object contract).
//------------------------------------------------------------------------------
struct HashSurfaceSpec
{
    Il2CppObject* (*create)();
    Il2CppObject* (*compute)(Il2CppObject*, Il2CppArray*);
    Il2CppObject* (*getHash)(Il2CppObject*);
    void (*clear)(Il2CppObject*);
    int32_t (*transformBlock)(Il2CppObject*, Il2CppArray*, int32_t, int32_t, Il2CppArray*, int32_t);
    Il2CppObject* (*transformFinalBlock)(Il2CppObject*, Il2CppArray*, int32_t, int32_t);
    Il2CppObject* (*computeStream)(Il2CppObject*, Il2CppObject*);
    int32_t digestLen;
    const char* abcHex;
};

void RunHashSurfaceTests(const HashSurfaceSpec& s, const char* what)
{
    Il2CppObject* obj = s.create();
    Expect(obj != nullptr, what);

    // Hash before any ComputeHash -> NullReferenceException (HashValue null).
    Expect(ThrowsExactType([&]() { s.getHash(obj); }, "System.NullReferenceException"), what);

    // ComputeHash caches the digest; Hash returns a CLONE (a fresh array).
    Il2CppArray* abc = MakeBytes("616263"); // "abc"
    Il2CppArray* d = AsBytes(s.compute(obj, abc), s.digestLen, what);
    ExpectHex(reinterpret_cast<uint8_t*>(d->data), s.digestLen, s.abcHex, what);
    Il2CppObject* h1 = s.getHash(obj);
    Il2CppObject* h2 = s.getHash(obj);
    Expect(h1 != h2, what); // a clone each time (never the cached identity)
    Il2CppArray* ha = AsBytes(h1, s.digestLen, what);
    ExpectHex(reinterpret_cast<uint8_t*>(ha->data), s.digestLen, s.abcHex, what);

    // TransformBlock -> state != 0 -> Hash throws CryptographicUnexpectedOperationException.
    Il2CppArray* part1 = MakeBytes("6162"); // "ab"
    uint8_t outBuf[2] = {0, 0};
    Il2CppArray* out1 = MakeBytes(outBuf, 2);
    int32_t n = s.transformBlock(obj, part1, 0, 2, out1, 0);
    Expect(n == 2, what);
    ExpectHex(reinterpret_cast<uint8_t*>(out1->data), 2, "6162", what); // input copied to output
    Expect(ThrowsExactType([&]() { s.getHash(obj); },
                           "System.Security.Cryptography.CryptographicUnexpectedOperationException"), what);

    // TransformFinalBlock finalizes: the digest of "abc", and the returned
    // array is a COPY of the input (not the digest).
    Il2CppArray* part2 = MakeBytes("63"); // "c"
    Il2CppArray* fin = AsBytes(s.transformFinalBlock(obj, part2, 0, 1), 1, what);
    ExpectHex(reinterpret_cast<uint8_t*>(fin->data), 1, "63", what);
    Il2CppArray* h3 = AsBytes(s.getHash(obj), s.digestLen, what);
    ExpectHex(reinterpret_cast<uint8_t*>(h3->data), s.digestLen, s.abcHex, what);

    // Clear -> disposed: Hash/ComputeHash/Transform* all throw ODE.
    s.clear(obj);
    Expect(ThrowsExactType([&]() { s.getHash(obj); }, "System.ObjectDisposedException"), what);
    Expect(ThrowsExactType([&]() { s.compute(obj, abc); }, "System.ObjectDisposedException"), what);
    Expect(ThrowsExactType([&]() { s.transformFinalBlock(obj, part2, 0, 1); },
                           "System.ObjectDisposedException"), what);
}

//------------------------------------------------------------------------------
// Aes parameterless CreateEncryptor/Decryptor + the disposed flag (batch 6).
//------------------------------------------------------------------------------
void RunAesSelfCtorTests()
{
    il2cpp::Il2CppClass* aesClass = &System_Security_Cryptography_Aes_class;
    Il2CppObject* aes = il2cpp::runtime::icall_aes_create();
    Expect(aes->klass == aesClass, "Aes.Create klass");

    // The parameterless CreateEncryptor/Decryptor auto-generate Key/IV.
    Il2CppObject* enc = il2cpp::runtime::icall_aes_create_encryptor_self(aes);
    Il2CppObject* dec = il2cpp::runtime::icall_aes_create_decryptor_self(aes);
    Expect(enc != nullptr && dec != nullptr, "Aes self CreateEncryptor/Decryptor");

    uint8_t plain[14];
    for (int i = 0; i < 14; ++i) plain[i] = static_cast<uint8_t>('a' + i);
    Il2CppArray* input = MakeBytes(plain, 14);
            Il2CppArray* ct = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(enc, input, 0, 14), 16,
                                      "Aes self encrypt length");
    Il2CppArray* back = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(dec, ct, 0, 16), 14,
                                "Aes self decrypt length");
    Expect(std::memcmp(back->data, plain, 14) == 0, "Aes self round trip");

    // Clear -> disposed: CreateEncryptor/Key throw ODE.
    il2cpp::runtime::icall_aes_dispose(aes);
    Expect(ThrowsType([&]() { il2cpp::runtime::icall_aes_create_encryptor_self(aes); },
                      "System.ObjectDisposedException"),
           "Aes disposed CreateEncryptor -> ODE");
    Expect(ThrowsType([&]() { il2cpp::runtime::icall_aes_get_key(aes); },
                      "System.ObjectDisposedException"),
           "Aes disposed Key -> ODE");
}

} // namespace

int main()
{
    il2cpp::il2cpp_crypto_register_classes();

    // --- 1. Hash object streaming (RFC 1321 / FIPS 180-4 vectors) ---
    {
        const HashObjSpec md5 = {
            &System_Security_Cryptography_MD5_class,
            &il2cpp::runtime::icall_md5_create,
            &il2cpp::runtime::icall_md5_compute_hash,
            &il2cpp::runtime::icall_md5_compute_hash_range,
            &Md5OneShotRaw,
            16,
            "900150983cd24fb0d6963f7d28e17f72",
            "d41d8cd98f00b204e9800998ecf8427e",
        };
        RunHashObjTests(md5, "MD5 object streaming");

        const HashObjSpec sha1 = {
            &System_Security_Cryptography_SHA1_class,
            &il2cpp::runtime::icall_sha1_create,
            &il2cpp::runtime::icall_sha1_compute_hash,
            &il2cpp::runtime::icall_sha1_compute_hash_range,
            &il2cpp_sha1_hash_bytes,
            20,
            "a9993e364706816aba3e25717850c26c9cd0d89d",
            "da39a3ee5e6b4b0d3255bfef95601890afd80709",
        };
        RunHashObjTests(sha1, "SHA1 object streaming");

        const HashObjSpec sha256 = {
            &System_Security_Cryptography_SHA256_class,
            &il2cpp::runtime::icall_sha256_create,
            &il2cpp::runtime::icall_sha256_compute_hash,
            &il2cpp::runtime::icall_sha256_compute_hash_range,
            &il2cpp_sha256_hash_bytes,
            32,
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        };
        RunHashObjTests(sha256, "SHA256 object streaming");

        const HashObjSpec sha384 = {
            &System_Security_Cryptography_SHA384_class,
            &il2cpp::runtime::icall_sha384_create,
            &il2cpp::runtime::icall_sha384_compute_hash,
            &il2cpp::runtime::icall_sha384_compute_hash_range,
            &il2cpp_sha384_hash_bytes,
            48,
            "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed"
            "8086072ba1e7cc2358baeca134c825a7",
            "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da"
            "274edebfe76f65fbd51ad2f14898b95b",
        };
        RunHashObjTests(sha384, "SHA384 object streaming");

        const HashObjSpec sha512 = {
            &System_Security_Cryptography_SHA512_class,
            &il2cpp::runtime::icall_sha512_create,
            &il2cpp::runtime::icall_sha512_compute_hash,
            &il2cpp::runtime::icall_sha512_compute_hash_range,
            &il2cpp_sha512_hash_bytes,
            64,
            "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2"
            "192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
            "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce"
            "47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
        };
        RunHashObjTests(sha512, "SHA512 object streaming");
    }

    // --- 1b. Legacy newobj path: *CryptoServiceProvider class + ctor icall
    //          hashes identically (aliased descriptor, same layout) ---
    {
        il2cpp::Il2CppClass* legacy = &System_Security_Cryptography_MD5CryptoServiceProvider_class;
        auto* obj = reinterpret_cast<il2cpp::Il2CppMd5Object*>(il2cpp_object_new(legacy));
        il2cpp::runtime::icall_md5_ctor(reinterpret_cast<Il2CppObject*>(obj));
        Expect(obj->obj.klass == legacy, "MD5CryptoServiceProvider newobj klass");
        Il2CppArray* abc = MakeBytes("616263");
        Il2CppArray* d = AsBytes(il2cpp::runtime::icall_md5_compute_hash(
            reinterpret_cast<Il2CppObject*>(obj), abc), 16, "legacy MD5 CSP ComputeHash");
        ExpectHex(reinterpret_cast<uint8_t*>(d->data), 16,
                  "900150983cd24fb0d6963f7d28e17f72", "legacy MD5 CSP digest matches");
        il2cpp::runtime::icall_md5_dispose(reinterpret_cast<Il2CppObject*>(obj));

        auto* sha1obj = reinterpret_cast<il2cpp::Il2CppSha1Object*>(
            il2cpp_object_new(&System_Security_Cryptography_SHA1Managed_class));
        il2cpp::runtime::icall_sha1_ctor(reinterpret_cast<Il2CppObject*>(sha1obj));
        Il2CppArray* d1 = AsBytes(il2cpp::runtime::icall_sha1_compute_hash(
            reinterpret_cast<Il2CppObject*>(sha1obj), abc), 20, "legacy SHA1Managed ComputeHash");
        ExpectHex(reinterpret_cast<uint8_t*>(d1->data), 20,
                  "a9993e364706816aba3e25717850c26c9cd0d89d", "legacy SHA1Managed digest matches");
    }

    // --- 2. HMAC objects (RFC 2202/4231 case 1: key 0x0b x20, "Hi There") ---
    {
        const HmacObjSpec sha1 = { &System_Security_Cryptography_HMACSHA1_class,
            "b617318655057264e28bc0b6fb378c8ef146be00", 20 };
        RunHmacObjTests(sha1, "HMACSHA1 object");

        const HmacObjSpec sha256 = { &System_Security_Cryptography_HMACSHA256_class,
            "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7", 32 };
        RunHmacObjTests(sha256, "HMACSHA256 object");

        const HmacObjSpec sha384 = { &System_Security_Cryptography_HMACSHA384_class,
            "afd03944d84895626b0825f4ab46907f15f9dadbe4101ec682aa034c7cebc59c"
            "faea9ea9076ede7f4af152e8b2fa9cb6", 48 };
        RunHmacObjTests(sha384, "HMACSHA384 object");

        const HmacObjSpec sha512 = { &System_Security_Cryptography_HMACSHA512_class,
            "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cde"
            "daa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854", 64 };
        RunHmacObjTests(sha512, "HMACSHA512 object");
    }

    // --- 3. AES objects (SP 800-38A F.2.1 key/iv + CBC round trips) ---
    {
        const char* kKey128 = "2b7e151628aed2a6abf7158809cf4f3c";
        const char* kKey256 = "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4";
        const char* kIv = "000102030405060708090a0b0c0d0e0f";

        // KAT: mode-layer CBC of the F.2.1 first plaintext block must start
        // with the F.2.1 first ciphertext block (pad block follows).
        {
            Il2CppArray* key = MakeBytes(kKey128);
            Il2CppArray* iv = MakeBytes(kIv);
            Il2CppArray* pt = MakeBytes("6bc1bee22e409f96e93d7e117393172a");
            Il2CppObject* aes = il2cpp::runtime::icall_aes_create();
            il2cpp::runtime::icall_aes_set_key(aes, key);
            il2cpp::runtime::icall_aes_set_iv(aes, iv);
            Il2CppObject* enc = il2cpp::runtime::icall_aes_create_encryptor(aes, key, iv);
            Il2CppArray* ct = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(
                enc, pt, 0, 16), 32, "AES CBC KAT length");
            ExpectHex(reinterpret_cast<uint8_t*>(ct->data), 16,
                      "7649abac8119b246cee98e9b12e9197d",
                      "AES-CBC F.2.1 first ciphertext block via the object layer");
        }

        // Round trips across lengths, AES-128 and AES-256.
        uint8_t plain[100];
        for (int i = 0; i < 100; ++i)
            plain[i] = static_cast<uint8_t>(i * 7 + 3);
        const int32_t lengths[] = { 0, 1, 15, 16, 17, 100 };
        for (size_t t = 0; t < sizeof(lengths) / sizeof(lengths[0]); ++t)
        {
            char what[64];
            std::snprintf(what, sizeof(what), "AES-128 object round trip of %d bytes", lengths[t]);
            AesRoundTrip(kKey128, kIv, plain, lengths[t], what);
            std::snprintf(what, sizeof(what), "AES-256 object round trip of %d bytes", lengths[t]);
            AesRoundTrip(kKey256, kIv, plain, lengths[t], what);
        }

        // GenerateKey/GenerateIV: lengths 32/16, stored + returned.
        {
            Il2CppObject* aes = il2cpp::runtime::icall_aes_create();
            Il2CppArray* gk = AsBytes(il2cpp::runtime::icall_aes_generate_key(aes), 32,
                                      "Aes.GenerateKey length");
            Expect(il2cpp::runtime::icall_aes_get_key(aes) == reinterpret_cast<Il2CppObject*>(gk),
                   "Aes.GenerateKey stored");
            Il2CppArray* giv = AsBytes(il2cpp::runtime::icall_aes_generate_iv(aes), 16,
                                       "Aes.GenerateIV length");
            Expect(il2cpp::runtime::icall_aes_get_iv(aes) == reinterpret_cast<Il2CppObject*>(giv),
                   "Aes.GenerateIV stored");
        }

        // Error paths.
        Expect(ThrowsType([]() { il2cpp::runtime::icall_aes_set_key(il2cpp::runtime::icall_aes_create(), nullptr); },
                          "System.ArgumentNullException"),
               "Aes.set_Key(null) -> ArgumentNullException");
        Expect(ThrowsType([]() {
                    uint8_t k24[24] = { 0 };
                    Il2CppArray* key = MakeBytes(k24, 24);
                    il2cpp::runtime::icall_aes_set_key(il2cpp::runtime::icall_aes_create(), key);
                }, "System.NotSupportedException"),
               "AES-192 key (24 bytes) -> NotSupportedException");
        Expect(ThrowsType([]() {
                    uint8_t k8[8] = { 0 };
                    Il2CppArray* key = MakeBytes(k8, 8);
                    il2cpp::runtime::icall_aes_set_key(il2cpp::runtime::icall_aes_create(), key);
                }, "System.Security.Cryptography.CryptographicException"),
               "8-byte key -> CryptographicException");
        Expect(ThrowsType([]() {
                    uint8_t iv8[8] = { 0 };
                    Il2CppArray* iv = MakeBytes(iv8, 8);
                    il2cpp::runtime::icall_aes_set_iv(il2cpp::runtime::icall_aes_create(), iv);
                }, "System.Security.Cryptography.CryptographicException"),
               "8-byte IV -> CryptographicException");
        Expect(ThrowsType([&]() {
                    Il2CppArray* key = MakeBytes(kKey128);
                    Il2CppArray* iv = MakeBytes(kIv);
                    Il2CppObject* aes = il2cpp::runtime::icall_aes_create();
                    Il2CppObject* enc = il2cpp::runtime::icall_aes_create_encryptor(aes, key, iv);
                    il2cpp::runtime::icall_aes_transform_final_block(enc, nullptr, 0, 4);
                }, "System.ArgumentNullException"),
               "TransformFinalBlock(null input) -> ArgumentNullException");
        Expect(ThrowsType([&]() {
                    Il2CppArray* key = MakeBytes(kKey128);
                    Il2CppArray* iv = MakeBytes(kIv);
                    Il2CppArray* in3 = MakeBytes("616263");
                    Il2CppObject* aes = il2cpp::runtime::icall_aes_create();
                    Il2CppObject* enc = il2cpp::runtime::icall_aes_create_encryptor(aes, key, iv);
                    il2cpp::runtime::icall_aes_transform_final_block(enc, in3, 1, 3);
                }, "System.ArgumentOutOfRangeException"),
               "TransformFinalBlock out-of-range count -> ArgumentOutOfRangeException");
        Expect(ThrowsType([&]() {
                    Il2CppArray* key = MakeBytes(kKey128);
                    Il2CppArray* iv = MakeBytes(kIv);
                    Il2CppArray* pt = MakeBytes("6bc1bee22e409f96e93d7e117393172a");
                    Il2CppObject* aes = il2cpp::runtime::icall_aes_create();
                    Il2CppObject* enc = il2cpp::runtime::icall_aes_create_encryptor(aes, key, iv);
                    Il2CppArray* ct = AsBytes(il2cpp::runtime::icall_aes_transform_final_block(
                        enc, pt, 0, 16), 32, "tamper setup");
                    reinterpret_cast<uint8_t*>(ct->data)[31] ^= 0x01;
                    Il2CppObject* dec = il2cpp::runtime::icall_aes_create_decryptor(aes, key, iv);
                    il2cpp::runtime::icall_aes_transform_final_block(dec, ct, 0, 32);
                }, "System.Security.Cryptography.CryptographicException"),
               "tampered last ciphertext byte -> CryptographicException");
    }

    // --- 4. RNG objects ---
    {
        il2cpp::il2cpp_crypto_register_classes();
        auto* rng = reinterpret_cast<il2cpp::Il2CppRngObject*>(
            il2cpp_object_new(&System_Security_Cryptography_RNGCryptoServiceProvider_class));
        il2cpp::runtime::icall_rng_ctor(reinterpret_cast<Il2CppObject*>(rng));

        uint8_t buf[64];
        Il2CppArray* a = MakeBytes(buf, 64);
        il2cpp::runtime::icall_rng_get_bytes(reinterpret_cast<Il2CppObject*>(rng), a);
        Il2CppArray* b = MakeBytes(buf, 64);
        il2cpp::runtime::icall_rng_get_bytes(reinterpret_cast<Il2CppObject*>(rng), b);
        Expect(a->length == 64 && b->length == 64, "RNG GetBytes length honored");
        Expect(std::memcmp(a->data, b->data, 64) != 0, "RNG two fills differ");
        Expect(std::memcmp(a->data, buf, 64) != 0 || std::memcmp(b->data, buf, 64) != 0,
               "RNG fills are not the zeroed input");

        Il2CppArray* nz = MakeBytes(buf, 256);
        il2cpp::runtime::icall_rng_get_nonzero_bytes(reinterpret_cast<Il2CppObject*>(rng), nz);
        bool anyZero = false;
        for (int32_t i = 0; i < nz->length; ++i)
            anyZero = anyZero || reinterpret_cast<uint8_t*>(nz->data)[i] == 0;
        Expect(!anyZero, "RNG GetNonZeroBytes has no zero bytes");

        Il2CppArray* fill = MakeBytes(buf, 16);
        il2cpp::runtime::icall_rng_fill_static(fill);
        Expect(fill->length == 16, "RNG Fill(byte[]) static fills the array");
        Expect(std::memcmp(fill->data, buf, 16) != 0, "RNG Fill changed the payload");

        Expect(ThrowsType([]() { il2cpp::runtime::icall_rng_get_bytes(nullptr, nullptr); },
                          "System.ArgumentNullException"),
               "RNG GetBytes(null) -> ArgumentNullException");
        Expect(ThrowsType([]() { il2cpp::runtime::icall_rng_get_nonzero_bytes(nullptr, nullptr); },
                          "System.ArgumentNullException"),
               "RNG GetNonZeroBytes(null) -> ArgumentNullException");
        Expect(ThrowsType([]() { il2cpp::runtime::icall_rng_fill_static(nullptr); },
                          "System.ArgumentNullException"),
               "RNG Fill(null) -> ArgumentNullException");
        il2cpp::runtime::icall_rng_dispose(reinterpret_cast<Il2CppObject*>(rng));
    }

    // --- 5. HashAlgorithm.Hash / Clear / Transform* / ComputeHash(Stream) ---
    {
        const HashSurfaceSpec md5 = {
            &il2cpp::runtime::icall_md5_create,
            &il2cpp::runtime::icall_md5_compute_hash,
            &il2cpp::runtime::icall_md5_get_hash,
            &il2cpp::runtime::icall_md5_clear,
            &il2cpp::runtime::icall_md5_transform_block,
            &il2cpp::runtime::icall_md5_transform_final_block,
            &il2cpp::runtime::icall_md5_compute_hash_stream,
            16,
            "900150983cd24fb0d6963f7d28e17f72",
        };
        RunHashSurfaceTests(md5, "MD5 Hash surface");

        const HashSurfaceSpec sha1 = {
            &il2cpp::runtime::icall_sha1_create,
            &il2cpp::runtime::icall_sha1_compute_hash,
            &il2cpp::runtime::icall_sha1_get_hash,
            &il2cpp::runtime::icall_sha1_clear,
            &il2cpp::runtime::icall_sha1_transform_block,
            &il2cpp::runtime::icall_sha1_transform_final_block,
            &il2cpp::runtime::icall_sha1_compute_hash_stream,
            20,
            "a9993e364706816aba3e25717850c26c9cd0d89d",
        };
        RunHashSurfaceTests(sha1, "SHA1 Hash surface");

        const HashSurfaceSpec sha256 = {
            &il2cpp::runtime::icall_sha256_create,
            &il2cpp::runtime::icall_sha256_compute_hash,
            &il2cpp::runtime::icall_sha256_get_hash,
            &il2cpp::runtime::icall_sha256_clear,
            &il2cpp::runtime::icall_sha256_transform_block,
            &il2cpp::runtime::icall_sha256_transform_final_block,
            &il2cpp::runtime::icall_sha256_compute_hash_stream,
            32,
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
        };
        RunHashSurfaceTests(sha256, "SHA256 Hash surface");

        const HashSurfaceSpec sha384 = {
            &il2cpp::runtime::icall_sha384_create,
            &il2cpp::runtime::icall_sha384_compute_hash,
            &il2cpp::runtime::icall_sha384_get_hash,
            &il2cpp::runtime::icall_sha384_clear,
            &il2cpp::runtime::icall_sha384_transform_block,
            &il2cpp::runtime::icall_sha384_transform_final_block,
            &il2cpp::runtime::icall_sha384_compute_hash_stream,
            48,
            "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed"
            "8086072ba1e7cc2358baeca134c825a7",
        };
        RunHashSurfaceTests(sha384, "SHA384 Hash surface");

        const HashSurfaceSpec sha512 = {
            &il2cpp::runtime::icall_sha512_create,
            &il2cpp::runtime::icall_sha512_compute_hash,
            &il2cpp::runtime::icall_sha512_get_hash,
            &il2cpp::runtime::icall_sha512_clear,
            &il2cpp::runtime::icall_sha512_transform_block,
            &il2cpp::runtime::icall_sha512_transform_final_block,
            &il2cpp::runtime::icall_sha512_compute_hash_stream,
            64,
            "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2"
            "192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
        };
        RunHashSurfaceTests(sha512, "SHA512 Hash surface");

        // ComputeHash(Stream): the 4096-byte chunked read loop.
        {
            const uint8_t streamData[] = "hello world";
            Il2CppObject* stream = il2cpp::runtime::il2cpp_stream_memory_from_bytes(
                streamData, static_cast<int32_t>(sizeof(streamData) - 1));
            Il2CppObject* sha = il2cpp::runtime::icall_sha1_create();
            Il2CppArray* digest = AsBytes(
                il2cpp::runtime::icall_sha1_compute_hash_stream(sha, stream), 20,
                "SHA1 ComputeHash(Stream) length");
            ExpectHex(reinterpret_cast<uint8_t*>(digest->data), 20,
                      "2aae6c35c94fcfb415dbe95f408b9ce91ee846ed",
                      "SHA1 ComputeHash(Stream) digest");
        }
    }

    // --- 6. HMAC Hash getter + Clear (KeyedHashAlgorithm) ---
    {
        il2cpp::Il2CppClass* hmacClass = &System_Security_Cryptography_HMACSHA256_class;
        uint8_t keyBuf[3] = {'k', 'e', 'y'};
        Il2CppArray* key = MakeBytes(keyBuf, 3);
        auto* obj = reinterpret_cast<il2cpp::Il2CppHmacObject*>(il2cpp_object_new(hmacClass));
        il2cpp::runtime::icall_hmac_ctor(reinterpret_cast<Il2CppObject*>(obj), key);

        Expect(ThrowsExactType([&]() {
                    il2cpp::runtime::icall_hmac_get_hash(reinterpret_cast<Il2CppObject*>(obj));
                }, "System.NullReferenceException"),
               "HMAC Hash before compute -> NRE");

        Il2CppArray* abc = MakeBytes("616263");
        il2cpp::runtime::icall_hmac_compute_hash(reinterpret_cast<Il2CppObject*>(obj), abc);
        Il2CppArray* h1 = AsBytes(
            il2cpp::runtime::icall_hmac_get_hash(reinterpret_cast<Il2CppObject*>(obj)), 32,
            "HMAC Hash length");
        Il2CppArray* h2 = AsBytes(
            il2cpp::runtime::icall_hmac_get_hash(reinterpret_cast<Il2CppObject*>(obj)), 32,
            "HMAC Hash clone length");
        Expect(h1 != h2, "HMAC Hash returns a clone");
        Expect(std::memcmp(h1->data, h2->data, 32) == 0, "HMAC Hash clone payload");

        il2cpp::runtime::icall_hmac_clear(reinterpret_cast<Il2CppObject*>(obj));
        Expect(ThrowsExactType([&]() {
                    il2cpp::runtime::icall_hmac_get_hash(reinterpret_cast<Il2CppObject*>(obj));
                }, "System.ObjectDisposedException"),
               "HMAC Hash after Clear -> ODE");
    }

    // --- 7. Aes parameterless CreateEncryptor/Decryptor + disposed ---
    RunAesSelfCtorTests();

    // --- T7. Crypto/X509 object stubs are LOUD (parity-T7): the CSP/X509
    // models do not exist, so every object-returning helper throws
    // NotSupportedException instead of returning a silent nullptr.
    {
        Expect(ThrowsType([]() { il2cpp::runtime::icall_crypto_utils_import_csp_blob(nullptr, nullptr); },
                          "System.NotSupportedException"),
               "ImportCspBlob stub is LOUD (NotSupportedException, not nullptr)");
        Expect(ThrowsType([]() { il2cpp::runtime::icall_crypto_utils_create_csp(nullptr); },
                          "System.NotSupportedException"),
               "CreateCsp stub is LOUD (NotSupportedException, not nullptr)");
        Expect(ThrowsType([]() { il2cpp::runtime::icall_x509_utils_get_issuer_name(nullptr); },
                          "System.NotSupportedException"),
               "GetIssuerName stub is LOUD (NotSupportedException, not nullptr)");
        Expect(ThrowsType([]() { il2cpp::runtime::icall_x509_utils_get_thumbprint(nullptr); },
                          "System.NotSupportedException"),
               "GetThumbprint stub is LOUD (NotSupportedException, not nullptr)");
    }

    if (g_failures != 0)
    {
        std::printf("%d crypto_obj assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime crypto_obj tests passed\n");
    return 0;
}
