using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Hash dispatch tests: prove the System.Security.Cryptography
// MD5/SHA1/SHA256 domain (MethodMap.Crypto.cs -> TryMapCrypto, the T4
// OBJECT layer that supersedes the T1 one-shot mapping in MethodMap.Hash.cs)
// wires Create()/ComputeHash()/Dispose() to the runtime object icalls
// (crypto_obj.h -- ctx-inline objects):
//
//   MD5/SHA1/SHA256.Create()      -> icall_<algo>_create() (static, allocates
//     the object + primes the inline context; ReturnTypeName carries the
//     concrete algorithm for the next callvirt's receiver hint)
//   <Algo>.ComputeHash(byte[])     -> icall_<algo>_compute_hash(self, byte[])
//     with the RECEIVER PASSED ("" cast) and the byte[] as Il2CppArray*
//     ("a" cast) -- streaming through the object's context
//   <Algo>.ComputeHash(byte[],int,int) -> icall_<algo>_compute_hash_range
//     (wired by T4 -- the T1 LOUD gap is closed by the object layer)
//   <Algo>.Dispose()               -> icall_<algo>_dispose (no-op)
//
// Callvirt shape: csc declares ComputeHash on HashAlgorithm (virtual
// inherited), so the algorithm arrives in receiverType -- the receiver-less
// first resolution must defer (return null -> generated-name fallback).
public class MethodMapTestsHash
{
    [Fact]
    public void Resolve_Create_MD5_MapsToObjectIcall_Static()
    {
        // static MD5 Create() -- ret is a CLASS (0x12 + TypeHandle)
        var resolved = ResolveMember(Synthesize("MD5", "Create", Sig(B(0x12, 0x05))), "Create");

        Assert.Equal("il2cpp::runtime::icall_md5_create", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Security.Cryptography.MD5", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Create_SHA256_MapsToObjectIcall()
    {
        var resolved = ResolveMember(Synthesize("SHA256", "Create", Sig(B(0x12, 0x05))), "Create");

        Assert.Equal("il2cpp::runtime::icall_sha256_create", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal("System.Security.Cryptography.SHA256", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ComputeHash_ReceiverHintMd5_MapsToStreamingIcall()
    {
        // callvirt HashAlgorithm::ComputeHash(byte[]) with a receiver hint
        // derived from MD5.Create()'s ReturnTypeName.
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08))), "ComputeHash", receiverType: "System.Security.Cryptography.MD5");

        Assert.Equal("il2cpp::runtime::icall_md5_compute_hash", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ComputeHash_ReceiverHintSha1_MapsToSha1StreamingIcall()
    {
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08))), "ComputeHash", receiverType: "System.Security.Cryptography.SHA1");

        Assert.Equal("il2cpp::runtime::icall_sha1_compute_hash", resolved.CppName);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ComputeHash_ConcreteOwner_MapsWithoutReceiverHint()
    {
        // A call on a MD5-typed receiver that resolves under the concrete
        // owner still dispatches the algorithm from the owner itself.
        var resolved = ResolveMember(Synthesize("MD5", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08))), "ComputeHash", receiverType: "System.Security.Cryptography.MD5");

        Assert.Equal("il2cpp::runtime::icall_md5_compute_hash", resolved.CppName);
    }

    [Fact]
    public void Resolve_ComputeHash_NoReceiverHint_DefersToReceiverAwareResolve()
    {
        // The fold's FIRST resolution has no receiver hint yet: the map
        // defers (null) and the call falls to the generated-name path until
        // the receiver-aware re-resolve dispatches (Encoding precedent).
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08))), "ComputeHash");

        Assert.StartsWith("System_Security_Cryptography_HashAlgorithm_", resolved.CppName);
    }

    [Fact]
    public void Resolve_ComputeHash_RangeOverload_MapsToRangeStreamingIcall()
    {
        // byte[] ComputeHash(byte[], int, int) -- closed by the T4 object
        // layer (the T1 one-shot kept it LOUD).
        var resolved = ResolveMember(Synthesize("HashAlgorithm", "ComputeHash",
            Sig(B(0x1D, 0x08), B(0x1D, 0x08), B(0x08), B(0x08))), "ComputeHash",
            receiverType: "System.Security.Cryptography.MD5");

        Assert.Equal("il2cpp::runtime::icall_md5_compute_hash_range", resolved.CppName);
        Assert.Equal(new[] { "", "a", "i", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Byte[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Dispose_MapsToNoopDisposeIcall()
    {
        var resolved = ResolveMember(Synthesize("MD5", "Dispose", Sig(B(0x00))), "Dispose");

        Assert.Equal("il2cpp::runtime::icall_md5_dispose", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_UnknownMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("MD5", "GetHashCode", Sig(B(0x08))), "GetHashCode");

        Assert.StartsWith("System_Security_Cryptography_MD5_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // MethodMap.Hash.cs is the SUPERSEDED T1 mapper (TryMapCrypto owns every
    // hash owner in the dispatch). It must nevertheless not LOUD-reject the
    // net48 HashAlgorithm overloads that the T4 object layer implements --
    // the file delegates to TryMapCrypto so the two mappings can never drift
    // (range: hashalgorithm.cs:91; stream: hashalgorithm.cs:57).
    //--------------------------------------------------------------------------
    [Fact]
    public void TryMapHash_DelegatesToCryptoObjectLayer_ForRangeAndStreamOverloads()
    {
        var mapper = typeof(MethodRefResolver).GetMethod("TryMapHash",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mapper);

        // byte[] ComputeHash(byte[], int, int) on an SHA512 receiver.
        var range = (ResolvedMethod?)mapper!.Invoke(null, new object?[]
        {
            "System.Security.Cryptography.HashAlgorithm", "ComputeHash",
            new byte[] { 0x1D, 0x08, 0x08 }, 3, null,
            "System.Security.Cryptography.SHA512", false, null, null
        });
        Assert.NotNull(range);
        Assert.Equal("il2cpp::runtime::icall_sha512_compute_hash_range", range!.CppName);

        // byte[] ComputeHash(Stream) on an SHA384 receiver.
        var stream = (ResolvedMethod?)mapper.Invoke(null, new object?[]
        {
            "System.Security.Cryptography.HashAlgorithm", "ComputeHash",
            new byte[] { 0x12 }, 1, null,
            "System.Security.Cryptography.SHA384", false, null, null
        });
        Assert.NotNull(stream);
        Assert.Equal("il2cpp::runtime::icall_sha384_compute_hash_stream", stream!.CppName);
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
    // SZARRAY -- element byte included; B(0x12, 0x05) for a class).
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
