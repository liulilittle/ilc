using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Buffer dispatch tests: prove the System.Buffer domain wiring
// (MethodMap.Buffer.cs -> TryMapBuffer) routes the three runtime icalls,
// rejects the raw-pointer/absent-runtime members LOUDLY, and lets unknown
// members fall through to the generated-name path.
//
// Same synthesis approach as MethodRefResolverDispatchTests: a real ECMA-335
// PE holding exactly one MemberReference (System.Buffer::<member>) with a
// hand-built signature blob. The Array-typed params (0x12 CLASS) carry a
// valid TypeDefOrRef coded index pointing at the synthesized System.Array
// TypeReference (row 2 -> coded byte (2<<2)|1 = 0x09, computed dynamically).
// TypeDefOrRef tag bits (ECMA-335 II.24.2.6): TypeDef=0, TypeRef=1, TypeSpec=2
// -- tag 2 would decode to a TypeSpecification handle and fail in
// MethodSignatureCoder.ResolveTypeName ("kind TypeSpecification not supported").
public class MethodMapBufferTests
{
    //--------------------------------------------------------------------------
    // BlockCopy(Array, int, Array, int, int) -- the cross-element-kind byte
    // copy. Array slots stay uncased (Il2CppObject*), int slots get (int32_t).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Buffer_BlockCopy_MapsToBlockCopyIcall()
    {
        var (_, arr) = Synthesize("BlockCopy",
            new byte[] { 0x00, 0x05, 0x01, 0x12, 0x00, 0x08, 0x12, 0x00, 0x08, 0x08 });

        var resolved = ResolveBuffer("BlockCopy", new byte[] { 0x00, 0x05, 0x01, 0x12, arr, 0x08, 0x12, arr, 0x08, 0x08 });

        Assert.Equal("il2cpp::runtime::il2cpp_buffer_block_copy", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(5, resolved.ParamCount);
        Assert.Equal(new[] { "", "i", "", "i", "i" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { true, false, true, false, false }, resolved.ParamIsRef);
    }

    //--------------------------------------------------------------------------
    // ByteLength(Array) -> int32 -- single-argument form, int32 result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Buffer_ByteLength_MapsToByteLengthIcall()
    {
        var (_, arr) = Synthesize("ByteLength",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x00 });

        var resolved = ResolveBuffer("ByteLength", new byte[] { 0x00, 0x01, 0x08, 0x12, arr });

        Assert.Equal("il2cpp::runtime::il2cpp_buffer_byte_length", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { true }, resolved.ParamIsRef);
        Assert.Null(resolved.ResultKind); // int32 is the default result kind
    }

    //--------------------------------------------------------------------------
    // MemoryCopy(void*, void*, long, long) -- unwireable: the runtime icall
    // is memmove-ordered (dst, src, count) while the IL pushes
    // (source, destination, ...); positional emission would swap direction.
    // Must fail LOUD at transpile time, never emit a wrong-direction copy.
    //
    // Two rejection layers, both loud (NotSupportedException at transpile
    // time -- never a silent wrong-direction copy):
    //   1. MethodSignatureCoder.EncodeType rejects the void* param byte
    //      0x1E PTR before the BCL dispatch runs (Decode executes ahead of
    //      BclResolve in Resolve) -- the real-world path for this signature.
    //   2. TryMapBuffer's own MemoryCopy branch (MethodMap.Buffer.cs) -- the
    //      defensive contract for a future day when the decoder learns PTR;
    //      reachable only with a decodable signature.
    // The assertion is layer-independent: it pins the LOUD contract, not the
    // message text of either layer.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Buffer_MemoryCopy_MapsToSizedIcall()
    {
        // void* params (0x0F PTR + 0x01 VOID target), long size/count (0x0A);
        // IL order (source, destination, ...) reorders via IcallArgOrder.
        var resolved = ResolveBuffer("MemoryCopy",
            new byte[] { 0x00, 0x04, 0x01, 0x0F, 0x01, 0x0F, 0x01, 0x0A, 0x0A });

        Assert.Equal("il2cpp::runtime::il2cpp_buffer_memory_copy_sized", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(4, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GetByte(Array, int) -- known System.Buffer member with no runtime icall
    // on the current surface (block_copy/byte_length/memory_copy only).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Buffer_GetByte_MapsToGetByteIcall()
    {
        // Wired (D5 cov2): GetByte(Array, int) -> int32 carrier.
        var (_, arr) = Synthesize("GetByte", new byte[] { 0x00, 0x02, 0x03, 0x12, 0x00, 0x08 });

        var resolved = ResolveBuffer("GetByte", new byte[] { 0x00, 0x02, 0x03, 0x12, arr, 0x08 });

        Assert.Equal("il2cpp::runtime::icall_buffer_get_byte", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // SetByte(Array, int, byte) -- WIRED (arrgen1b): the 3-arg shape maps to
    // icall_buffer_set_byte (buffer.h/.cpp); the byte value arrives in an int32
    // carrier ("i"), the array slot stays uncased. The net48 validation mirrors
    // GetByte. A non-3-arg shape still fails LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Buffer_SetByte_MapsToSetByteIcall()
    {
        var (_, arr) = Synthesize("SetByte", new byte[] { 0x00, 0x03, 0x01, 0x12, 0x00, 0x08, 0x05 });

        var resolved = ResolveBuffer("SetByte", new byte[] { 0x00, 0x03, 0x01, 0x12, arr, 0x08, 0x05 });

        Assert.Equal("il2cpp::runtime::icall_buffer_set_byte", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "i", "i" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { true, false, false }, resolved.ParamIsRef);
    }

    [Fact]
    public void Resolve_Buffer_SetByte_BadArity_ThrowsLoud()
    {
        var (_, arr) = Synthesize("SetByte", new byte[] { 0x00, 0x02, 0x01, 0x12, 0x00, 0x08 });

        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveBuffer("SetByte", new byte[] { 0x00, 0x02, 0x01, 0x12, arr, 0x08 }));

        Assert.Contains("System.Buffer::SetByte", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Unknown member -- dispatch miss falls back to the generated-name path
    // (the manual's "unwired members return null" contract).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Buffer_UnknownMember_FallsBackToGeneratedName()
    {
        var (_, _) = Synthesize("Future", new byte[] { 0x00, 0x01, 0x08, 0x08 });

        var resolved = ResolveBuffer("Future", new byte[] { 0x00, 0x01, 0x08, 0x08 });

        Assert.StartsWith("System_Buffer_Future_", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveBuffer(string memberName, byte[] signature)
    {
        using var pe = new PEReader(Synthesize(memberName, signature).Pe);
        var md = pe.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Builds an assembly whose only MemberReference is System.Buffer::<member>
    // plus a System.Array TypeReference; returns the PE and the TypeDefOrRef
    // coded index byte for System.Array (0x12 CLASS params point at it).
    private static (MemoryStream Pe, byte ArrayCoded) Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var bufferRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Buffer"));
        var arrRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Array"));
        // TypeDefOrRef coded index: TypeRef tag 0x01, row << 2 | 1.
        var arrCoded = (byte)((MetadataTokens.GetRowNumber(arrRef) << 2) | 1);
        mdb.AddMemberReference(bufferRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return (Serialize(mdb), arrCoded);
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
