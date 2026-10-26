using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Crypto dispatch tests: prove the T4 OBJECT-layer wiring
// (MethodMap.Crypto.cs -> TryMapCrypto, crypto_obj.h/cpp) for the owners
// beyond the T1 MD5/SHA1/SHA256 trio:
//
//   SHA384/SHA512.Create() / ComputeHash(byte[]) / ComputeHash(byte[],int,int)
//                          -> icall_sha384/512_* object icalls
//   new HMACSHA1/256/384/512(byte[] key) -> icall_hmac_ctor (newobj allocates
//                          through the runtime GLOBAL class descriptor via
//                          the BclAllocExpr default fallback)
//   HMACSHAx.ComputeHash(byte[])  -> icall_hmac_compute_hash (the algorithm
//                          is dispatched from the receiver's class; arrives
//                          under the KeyedHashAlgorithm parent owner too)
//   Aes.Create() / set_Key/get_Key/set_IV/get_IV / CreateEncryptor/
//                          CreateDecryptor(key, iv) / GenerateKey/GenerateIV
//                          -> icall_aes_* family
//   ICryptoTransform.TransformFinalBlock(byte[],int,int) -> the AES transform
//                          final-block icall
//   new RNGCryptoServiceProvider() / GetBytes / GetNonZeroBytes /
//                          RandomNumberGenerator.Fill(byte[]) -> icall_rng_*
//   Legacy *CryptoServiceProvider / *Managed owners normalize onto the same
//                          algorithm icalls (crypto-domain.md §4.4)
//
// Known-unwireable members MUST be LOUD (NotSupportedException):
// HashAlgorithm.Create(string) (CryptoConfig). The hash TransformBlock/
// TransformFinalBlock, ComputeHash(Stream), the Aes parameterless
// CreateEncryptor/CreateDecryptor, the Aes TransformBlock/TransformFinalBlock
// streaming surface and the RNG named-CSP / CspParameters ctors are wired
// (batch 6 + batch 7).
public class MethodMapTestsCrypto
{
    [Fact]
    public void Resolve_Create_SHA384_MapsToObjectIcall()
    {
        var resolved = ResolveMember(Synthesize("SHA384", "Create", Sig(B(0x12, 0x05))), "Create");

        Assert.Equal("il2cpp::runtime::icall_sha384_create", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal("System.Security.Cryptography.SHA384", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Create_SHA512_MapsToObjectIcall()
    {
        var resolved = ResolveMember(Synthesize("SHA512", "Create", Sig(B(0x12, 0x05))), "Create");

        Assert.Equal("il2cpp::runtime::icall_sha512_create", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal("System.Security.Cryptography.SHA512", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ComputeHash_ReceiverHintSha512_MapsToStreamingIcall()
    {
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08))), "ComputeHash",
            receiverType: "System.Security.Cryptography.SHA512");

        Assert.Equal("il2cpp::runtime::icall_sha512_compute_hash", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Ctor_HMACSHA256_MapsToHmacCtor()
    {
        // new HMACSHA256(byte[] key): ret void, param byte[].
        var resolved = ResolveMember(Synthesize("HMACSHA256", ".ctor",
            Sig(B(0x00), B(0x1D, 0x08))), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_hmac_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ComputeHash_HmacOwner_MapsToHmacCompute()
    {
        var resolved = ResolveMember(Synthesize("HMACSHA256", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08))), "ComputeHash",
            receiverType: "System.Security.Cryptography.HMACSHA256");

        Assert.Equal("il2cpp::runtime::icall_hmac_compute_hash", resolved.CppName);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ComputeHash_KeyedHashAlgorithmParent_DispatchesFromReceiver()
    {
        // callvirt KeyedHashAlgorithm::ComputeHash with an HMACSHA512 receiver.
        var resolved = ResolveMember(Synthesize("KeyedHashAlgorithm", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08))), "ComputeHash",
            receiverType: "System.Security.Cryptography.HMACSHA512");

        Assert.Equal("il2cpp::runtime::icall_hmac_compute_hash", resolved.CppName);
    }

    [Fact]
    public void Resolve_Dispose_Hmac_MapsToHmacDispose()
    {
        var resolved = ResolveMember(Synthesize("HMACSHA256", "Dispose", Sig(B(0x00))), "Dispose");

        Assert.Equal("il2cpp::runtime::icall_hmac_dispose", resolved.CppName);
    }

    [Fact]
    public void Resolve_Create_Aes_MapsToObjectIcall()
    {
        var resolved = ResolveMember(Synthesize("Aes", "Create", Sig(B(0x12, 0x05))), "Create");

        Assert.Equal("il2cpp::runtime::icall_aes_create", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal("System.Security.Cryptography.Aes", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SetKey_Aes_MapsToAesSetKey()
    {
        var resolved = ResolveMember(Synthesize("Aes", "set_Key",
            Sig(B(0x00), B(0x1D, 0x08))), "set_Key");

        Assert.Equal("il2cpp::runtime::icall_aes_set_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_CreateEncryptor_Aes_MapsToAesEncryptor()
    {
        var resolved = ResolveMember(Synthesize("Aes", "CreateEncryptor",
            Sig(B(0x12, 0x05), B(0x1D, 0x08), B(0x1D, 0x08))), "CreateEncryptor");

        Assert.Equal("il2cpp::runtime::icall_aes_create_encryptor", resolved.CppName);
        Assert.Equal(new[] { "", "a", "a" }, resolved.ParamTypeNames);
        Assert.Equal("System.Security.Cryptography.AesTransform", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_TransformFinalBlock_ICryptoTransform_MapsToAesFinalBlock()
    {
        // aes.CreateEncryptor(k, iv).TransformFinalBlock(data, 0, len) --
        // the callvirt owner is the ICryptoTransform INTERFACE.
        var resolved = ResolveMember(Synthesize("ICryptoTransform", "TransformFinalBlock",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08), B(0x08), B(0x08))), "TransformFinalBlock");

        Assert.Equal("il2cpp::runtime::icall_aes_transform_final_block", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "a", "i", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_GenerateKey_Aes_MapsToAesGenerateKey()
    {
        // net48 GenerateKey() is VOID (mutates the object context; read back
        // via get_Key). The runtime icall returns the value as a convenience,
        // but the mapping must match the IL signature -- a value-returning
        // mapping would push a phantom temp on the void call and desync the
        // fold stack (aescase receiver-kept-on-stack pattern).
        var resolved = ResolveMember(Synthesize("Aes", "GenerateKey", Sig(B(0x00))), "GenerateKey");

        Assert.Equal("il2cpp::runtime::icall_aes_generate_key", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Null(resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SetKey_SymmetricAlgorithmParent_DispatchesFromReceiver()
    {
        // csc declares Aes.set_Key on the BCL PARENT SymmetricAlgorithm; the
        // algorithm comes from the tracked receiver (the Aes.Create() result).
        var resolved = ResolveMember(Synthesize("SymmetricAlgorithm", "set_Key",
            Sig(B(0x00), B(0x1D, 0x08))), "set_Key",
            receiverType: "System.Security.Cryptography.Aes");

        Assert.Equal("il2cpp::runtime::icall_aes_set_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_GetKey_SymmetricAlgorithmParent_DispatchesFromReceiver()
    {
        var resolved = ResolveMember(Synthesize("SymmetricAlgorithm", "get_Key",
            Sig(B(0x1D, 0x08))), "get_Key",
            receiverType: "System.Security.Cryptography.Aes");

        Assert.Equal("il2cpp::runtime::icall_aes_get_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SetIv_GetIv_SymmetricAlgorithmParent_DispatchesFromReceiver()
    {
        var set = ResolveMember(Synthesize("SymmetricAlgorithm", "set_IV",
            Sig(B(0x00), B(0x1D, 0x08))), "set_IV",
            receiverType: "System.Security.Cryptography.Aes");
        var get = ResolveMember(Synthesize("SymmetricAlgorithm", "get_IV",
            Sig(B(0x1D, 0x08))), "get_IV",
            receiverType: "System.Security.Cryptography.Aes");

        Assert.Equal("il2cpp::runtime::icall_aes_set_iv", set.CppName);
        Assert.Equal("il2cpp::runtime::icall_aes_get_iv", get.CppName);
        Assert.Equal("System.Byte[]", get.ReturnTypeName);
    }

    [Fact]
    public void Resolve_CreateEncryptorDecryptor_SymmetricAlgorithmParent_DispatchesFromReceiver()
    {
        var enc = ResolveMember(Synthesize("SymmetricAlgorithm", "CreateEncryptor",
            Sig(B(0x12, 0x05), B(0x1D, 0x08), B(0x1D, 0x08))), "CreateEncryptor",
            receiverType: "System.Security.Cryptography.Aes");
        var dec = ResolveMember(Synthesize("SymmetricAlgorithm", "CreateDecryptor",
            Sig(B(0x12, 0x05), B(0x1D, 0x08), B(0x1D, 0x08))), "CreateDecryptor",
            receiverType: "System.Security.Cryptography.Aes");

        Assert.Equal("il2cpp::runtime::icall_aes_create_encryptor", enc.CppName);
        Assert.Equal("il2cpp::runtime::icall_aes_create_decryptor", dec.CppName);
        Assert.Equal(new[] { "", "a", "a" }, enc.ParamTypeNames);
        Assert.Equal("System.Security.Cryptography.AesTransform", enc.ReturnTypeName);
    }

    [Fact]
    public void Resolve_GenerateKeyIv_SymmetricAlgorithmParent_DispatchesFromReceiver()
    {
        var key = ResolveMember(Synthesize("SymmetricAlgorithm", "GenerateKey",
            Sig(B(0x00))), "GenerateKey",
            receiverType: "System.Security.Cryptography.Aes");
        var iv = ResolveMember(Synthesize("SymmetricAlgorithm", "GenerateIV",
            Sig(B(0x00))), "GenerateIV",
            receiverType: "System.Security.Cryptography.Aes");

        Assert.Equal("il2cpp::runtime::icall_aes_generate_key", key.CppName);
        Assert.Equal("il2cpp::runtime::icall_aes_generate_iv", iv.CppName);
        Assert.False(key.ReturnsValue);
        Assert.False(key.ReturnsRef);
    }

    [Fact]
    public void Resolve_Ctor_RNGCryptoServiceProvider_MapsToRngCtor()
    {
        var resolved = ResolveMember(Synthesize("RNGCryptoServiceProvider", ".ctor", Sig(B(0x00))), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_rng_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_GetBytes_RNGCryptoServiceProvider_MapsToRngGetBytes()
    {
        var resolved = ResolveMember(Synthesize("RNGCryptoServiceProvider", "GetBytes",
            Sig(B(0x00), B(0x1D, 0x08))), "GetBytes");

        Assert.Equal("il2cpp::runtime::icall_rng_get_bytes", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GetNonZeroBytes_RNGCryptoServiceProvider_MapsToRngNonzero()
    {
        var resolved = ResolveMember(Synthesize("RNGCryptoServiceProvider", "GetNonZeroBytes",
            Sig(B(0x00), B(0x1D, 0x08))), "GetNonZeroBytes");

        Assert.Equal("il2cpp::runtime::icall_rng_get_nonzero_bytes", resolved.CppName);
    }

    [Fact]
    public void Resolve_Fill_RandomNumberGenerator_MapsToStaticFill()
    {
        // RandomNumberGenerator.Fill(byte[]) -- the static fill shape (no
        // receiver slot: casts cover the parameter only).
        var resolved = ResolveMember(Synthesize("RandomNumberGenerator", "Fill",
            Sig(B(0x00), B(0x1D, 0x08))), "Fill");

        Assert.Equal("il2cpp::runtime::icall_rng_fill_static", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Create_MD5CryptoServiceProvider_LegacyOwner_NormalizesToMd5()
    {
        // The *CryptoServiceProvider legacy owners map onto the SAME icall
        // family (crypto-domain.md §4.4); the ReturnTypeName is the
        // canonical algorithm type so ComputeHash dispatches by MD5.
        var resolved = ResolveMember(Synthesize("MD5CryptoServiceProvider", "Create",
            Sig(B(0x12, 0x05))), "Create");

        Assert.Equal("il2cpp::runtime::icall_md5_create", resolved.CppName);
        Assert.Equal("System.Security.Cryptography.MD5", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Ctor_SHA256Managed_LegacyOwner_MapsToCtorIcall()
    {
        var resolved = ResolveMember(Synthesize("SHA256Managed", ".ctor", Sig(B(0x00))), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_sha256_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_TransformBlock_Hash_MapsToTransformBlockIcall()
    {
        // batch 6: the hash family implements ICryptoTransform on HashAlgorithm
        // (hashalgorithm.cs) -- TransformBlock feeds the inline context.
        var resolved = ResolveMember(Synthesize("MD5", "TransformBlock",
                Sig(B(0x08), B(0x1D, 0x08), B(0x08), B(0x08), B(0x1D, 0x08), B(0x08))), "TransformBlock");

        Assert.Equal("il2cpp::runtime::icall_md5_transform_block", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "a", "i", "i", "a", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_TransformBlock_ICryptoTransform_MapsToAesTransformBlock()
    {
        // batch 7: aes.CreateEncryptor(k, iv).TransformBlock(...) -- the
        // callvirt owner is the ICryptoTransform INTERFACE; net48 v2
        // RijndaelManagedTransform streaming semantics.
        var resolved = ResolveMember(Synthesize("ICryptoTransform", "TransformBlock",
                Sig(B(0x08), B(0x1D, 0x08), B(0x08), B(0x08), B(0x1D, 0x08), B(0x08))), "TransformBlock");

        Assert.Equal("il2cpp::runtime::icall_aes_transform_block", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "a", "i", "i", "a", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ComputeHash_StreamOverload_MapsToStreamIcall()
    {
        // batch 6: ComputeHash(Stream) is the 4096-byte chunked read loop; the
        // concrete algorithm comes from the receiver hint.
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "ComputeHash",
                Sig(B(0x1D, 0x08), B(0x12, 0x05))), "ComputeHash",
            receiverType: "System.Security.Cryptography.SHA512");

        Assert.Equal("il2cpp::runtime::icall_sha512_compute_hash_stream", resolved.CppName);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_CreateEncryptor_Parameterless_MapsToSelfIcall()
    {
        // batch 6: the parameterless SymmetricAlgorithm.CreateEncryptor() uses
        // the stored Key/IV (generated on demand).
        var resolved = ResolveMember(Synthesize("Aes", "CreateEncryptor", Sig(B(0x12, 0x05))), "CreateEncryptor");

        Assert.Equal("il2cpp::runtime::icall_aes_create_encryptor_self", resolved.CppName);
        Assert.Equal("System.Security.Cryptography.AesTransform", resolved.ReturnTypeName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_GetHash_HashAlgorithmParent_DispatchesFromReceiver()
    {
        // batch 6: HashAlgorithm.Hash -- the cached digest (a clone).
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "get_Hash",
            Sig(B(0x1D, 0x08))), "get_Hash",
            receiverType: "System.Security.Cryptography.MD5");

        Assert.Equal("il2cpp::runtime::icall_md5_get_hash", resolved.CppName);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Clear_HashAlgorithmParent_DispatchesFromReceiver()
    {
        // batch 6: HashAlgorithm.Clear() -- the net48 disposed flag.
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "Clear",
            Sig(B(0x00))), "Clear",
            receiverType: "System.Security.Cryptography.MD5");

        Assert.Equal("il2cpp::runtime::icall_md5_clear", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Clear_Aes_MapsToAesDispose()
    {
        // batch 6: SymmetricAlgorithm.Clear() (Aes) -- the CSP-handle Dispose.
        var resolved = ResolveMember(Synthesize("SymmetricAlgorithm", "Clear",
            Sig(B(0x00))), "Clear",
            receiverType: "System.Security.Cryptography.Aes");

        Assert.Equal("il2cpp::runtime::icall_aes_dispose", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_GetKey_Hmac_MapsToHmacGetKey()
    {
        var resolved = ResolveMember(Synthesize("HMACSHA256", "get_Key", Sig(B(0x1D, 0x08))), "get_Key");

        Assert.Equal("il2cpp::runtime::icall_hmac_get_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SetKey_Hmac_MapsToHmacSetKey()
    {
        var resolved = ResolveMember(Synthesize("HMACSHA256", "set_Key",
            Sig(B(0x00), B(0x1D, 0x08))), "set_Key");

        Assert.Equal("il2cpp::runtime::icall_hmac_set_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Ctor_HmacKeyless_MapsToHmacCtorDefault()
    {
        var resolved = ResolveMember(Synthesize("HMACSHA1", ".ctor", Sig(B(0x00))), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_hmac_ctor_default", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_GetHashSize_MD5_MapsToHashSizeIcall()
    {
        var resolved = ResolveMember(Synthesize("MD5", "get_HashSize", Sig(B(0x08))), "get_HashSize");

        Assert.Equal("il2cpp::runtime::icall_md5_get_hash_size", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_GetHashSize_HashAlgorithmParent_DispatchesFromReceiver()
    {
        // callvirt HashAlgorithm::get_HashSize with an SHA512 receiver.
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "get_HashSize",
            Sig(B(0x08))), "get_HashSize",
            receiverType: "System.Security.Cryptography.SHA512");

        Assert.Equal("il2cpp::runtime::icall_sha512_get_hash_size", resolved.CppName);
    }

    [Fact]
    public void Resolve_Initialize_SHA256_MapsToInitializeIcall()
    {
        var resolved = ResolveMember(Synthesize("SHA256", "Initialize", Sig(B(0x00))), "Initialize");

        Assert.Equal("il2cpp::runtime::icall_sha256_initialize", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Ctor_RngNamedCsp_MapsToRngCtor()
    {
        // batch 6: RNGCryptoServiceProvider(string) / (byte[]) delegate to
        // this((CspParameters) null) -- a no-op (the OS CSPRNG is used).
        var resolved = ResolveMember(Synthesize("RNGCryptoServiceProvider", ".ctor",
                Sig(B(0x00), B(0x0E))), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_rng_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Ctor_RngCspParameters_MapsToRngCtor()
    {
        // batch 7: RNGCryptoServiceProvider(CspParameters) -- the CLASS param
        // (0x12) delegates to this((CspParameters) null) too: the OS CSPRNG is
        // used regardless of the container name, so the argument is dropped.
        var resolved = ResolveMember(Synthesize("RNGCryptoServiceProvider", ".ctor",
                Sig(B(0x00), B(0x12, 0x05))), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_rng_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // net48 HashAlgorithm.ComputeHash(byte[], int, int) is inherited by the
    // KeyedHashAlgorithm/HMAC family (hashalgorithm.cs:91 -- HMAC only overrides
    // HashCore/HashFinal/Initialize, hmac.cs:128-157). The range overload is
    // therefore compile-time analyzable for HMAC and must wire to the shared
    // HMAC range icall, not a LOUD rejection.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ComputeHash_HmacRangeOverload_MapsToHmacRangeIcall()
    {
        var resolved = ResolveMember(Synthesize("HMACSHA256", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08), B(0x08), B(0x08))), "ComputeHash",
            receiverType: "System.Security.Cryptography.HMACSHA256");

        Assert.Equal("il2cpp::runtime::icall_hmac_compute_hash_range", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "a", "i", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ComputeHash_HmacRangeOverload_ParentOwner_DispatchesFromReceiver()
    {
        // callvirt KeyedHashAlgorithm::ComputeHash(byte[], int, int) with an
        // HMACSHA512 receiver (the parent-owner callvirt shape).
        var resolved = ResolveMember(Synthesize("KeyedHashAlgorithm", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08), B(0x08), B(0x08))), "ComputeHash",
            receiverType: "System.Security.Cryptography.HMACSHA512");

        Assert.Equal("il2cpp::runtime::icall_hmac_compute_hash_range", resolved.CppName);
        Assert.Equal(new[] { "", "a", "i", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // net48 RandomNumberGenerator.GetBytes(byte[], int, int) is a public virtual
    // method on the parent (randomnumbergenerator.cs:53 -- RNGCryptoServiceProvider
    // does not override it on desktop net48). Compile-time analyzable: wire the
    // range fill.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GetBytes_RangeOverload_MapsToRngGetBytesRange()
    {
        var resolved = ResolveMember(Synthesize("RNGCryptoServiceProvider", "GetBytes",
            Sig(B(0x00), B(0x1D, 0x08), B(0x08), B(0x08))), "GetBytes");

        Assert.Equal("il2cpp::runtime::icall_rng_get_bytes_range", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "a", "i", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // net48 HMACSHA1(byte[] key, bool useManagedSha1) is a public ctor
    // (hmacsha1.cs:24). The flag only chooses SHA1Managed vs
    // SHA1CryptoServiceProvider -- both produce standard SHA1, so the flag is
    // semantically irrelevant and is dropped at the call site.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Ctor_HMACSHA1_UseManagedSha1_MapsToHmacCtor()
    {
        var resolved = ResolveMember(Synthesize("HMACSHA1", ".ctor",
            Sig(B(0x00), B(0x1D, 0x08), B(0x02))), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_hmac_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "a", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Whitelist 4 (net48 has no such overload): GetNonZeroBytes is byte[]-only
    // (randomnumbergenerator.cs:67), so any other arity must stay LOUD with a
    // self-identifying whitelist message (never silent, never a guessed map).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GetNonZeroBytes_NonByteArrayArity_ThrowsWhitelistLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("RNGCryptoServiceProvider", "GetNonZeroBytes",
                Sig(B(0x00), B(0x1D, 0x08), B(0x08))), "GetNonZeroBytes"));

        Assert.Contains("whitelist", ex.Message);
        Assert.Contains("Reference Source", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
    }

    // One full parameter byte sequence (e.g. B(0x1D, 0x08) for a byte[]
    // SZARRAY -- element byte included; B(0x12, 0x05) for a class;
    // B(0x0E) for a string).
    private static byte[] B(params byte[] bytes) => bytes;

    // Signature blob: header 0x00 (DEFAULT, non-generic), LOGICAL parameter
    // count, return byte sequence (a class return keeps its TypeHandle
    // byte), then each parameter's full byte sequence -- decodable by
    // MethodSignatureCoder (runs before the resolver's dispatch) and by
    // RawSignature (yields the leading byte per slot).
    private static byte[] Sig(byte[] ret, params byte[][] ps)
    {
        var bytes = new byte[3 + ret.Length + ps.Sum(p => p.Length)];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret[0];
        var at = 3;
        for (var i = 1; i < ret.Length; i++) bytes[at++] = ret[i];
        foreach (var p in ps)
        {
            Array.Copy(p, 0, bytes, at, p.Length);
            at += p.Length;
        }
        return bytes;
    }

    private static MemoryStream Synthesize(string type, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Security.Cryptography"), mdb.GetOrAddString(type));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
