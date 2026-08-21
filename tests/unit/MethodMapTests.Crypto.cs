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
// Known-unwireable members MUST be LOUD (NotSupportedException): hash
// TransformBlock/TransformFinalBlock (v2), ComputeHash(Stream) (v2), HMAC
// Key property access, Aes parameterless CreateEncryptor/CreateDecryptor,
// RNG named-CSP ctors, Aes.TransformBlock (v2).
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
    public void Resolve_TransformBlock_Hash_ThrowsLoud()
    {
        // v2 scope: incremental block streaming must NOT be silent.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("MD5", "TransformBlock",
                Sig(B(0x08), B(0x1D, 0x08), B(0x08), B(0x08), B(0x1D, 0x08), B(0x08))), "TransformBlock"));

        Assert.Contains("TransformBlock", ex.Message);
    }

    [Fact]
    public void Resolve_TransformBlock_ICryptoTransform_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("ICryptoTransform", "TransformBlock",
                Sig(B(0x08), B(0x1D, 0x08), B(0x08), B(0x08), B(0x1D, 0x08), B(0x08))), "TransformBlock"));

        Assert.Contains("TransformBlock", ex.Message);
    }

    [Fact]
    public void Resolve_ComputeHash_StreamOverload_ThrowsLoud()
    {
        // ComputeHash(Stream) is unwireable (v2); T4 defers when the algorithm
        // is unpinned (null receiver), so the LOUD path is exercised with the
        // receiver hint -- the parent-owner call with a concrete receiver.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("HashAlgorithm", "ComputeHash",
                Sig(B(0x1D, 0x08), B(0x12, 0x05))), "ComputeHash",
                receiverType: "System.Security.Cryptography.SHA512"));

        Assert.Contains("Stream", ex.Message);
    }

    [Fact]
    public void Resolve_CreateEncryptor_Parameterless_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Aes", "CreateEncryptor", Sig(B(0x12, 0x05))), "CreateEncryptor"));

        Assert.Contains("CreateEncryptor", ex.Message);
    }

    [Fact]
    public void Resolve_GetKey_Hmac_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("HMACSHA256", "get_Key", Sig(B(0x1D, 0x08))), "get_Key"));

        Assert.Contains("Key", ex.Message);
    }

    [Fact]
    public void Resolve_Ctor_RngNamedCsp_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("RNGCryptoServiceProvider", ".ctor",
                Sig(B(0x00), B(0x0E))), ".ctor"));

        Assert.Contains("RNGCryptoServiceProvider", ex.Message);
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
