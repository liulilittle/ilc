using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.BinaryIO dispatch tests: prove the System.IO.BinaryReader /
// BinaryWriter domain (TryMapBinaryIO in MethodMap.BinaryIO.cs) routes each
// member to the matching binaryio.h icall, that the Write overload family is
// distinguished by the RawSignature parameter byte (int32 0x08 vs char 0x03
// vs bool 0x02 -- the signature encoder maps them to the same int32 slot),
// and that known-but-unwireable members reject LOUDLY (NotSupportedException
// with the "{owner}::{name}" prefix -- see BclResolve's wrap).
//
// Coverage contract:
//   * every wired Write overload (int32/int64/uint32/double/single/bool/
//     string) -- icall name + per-value cast codes (receiver index 0 is ""
//     per the managed-reference-slot convention, value cast follows)
//   * the wired Read family (Int32/Int64/UInt32/Double/Single/Boolean/
//     String) -- result kinds and ref-ness
//   * LOUD rejection of close-but-wrong mappings (Write(byte) must NOT route
//     through write_int32; ReadByte must NOT route through read_bool)
//   * LOUD rejection of the unwired surface (char/array writes, BaseStream,
//     the Stream-taking ctor) and the generated-name fallback for
//     out-of-scope members (Read7BitEncodedInt)
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (MetadataBuilder + ManagedPEBuilder): a real ECMA-335 assembly whose only
// MemberReference is the member under test. Signatures are built with
// Sig(ret, params...) -- the raw bytes RawSignature consumes; class-typed
// return/params use 0x12 followed by a compressed TypeDefOrRef coded index
// (the synthesized PE holds exactly one TypeReference, so 0x05).
public class MethodMapBinaryIOTests
{
    //--------------------------------------------------------------------------
    // BinaryWriter.Write overload family -- RawSignature byte dispatch
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BinaryWriter_WriteInt32_MapsToWriteInt32Icall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, 0x08)), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_int32", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteInt64_MapsToWriteInt64Icall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, 0x0A)), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_int64", resolved.CppName);
        Assert.Equal(new[] { "", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteUInt32_MapsToWriteUInt32Icall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, 0x09)), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_uint32", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteDouble_MapsToWriteDoubleIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, 0x0D)), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_double", resolved.CppName);
        Assert.Equal(new[] { "", "d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteSingle_MapsToWriteSingleIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, 0x0C)), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_single", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteBool_MapsToWriteBoolIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, 0x02)), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_bool", resolved.CppName);
        Assert.Equal(new[] { "", "b" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteString_MapsToWriteStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, 0x0E)), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // BinaryWriter.Write -- LOUD rejection of close-but-wrong overloads
    // (routing Write(byte) through write_int32 would emit 4 bytes instead
    // of 1: protocol corruption, so the mapping must refuse, not guess).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(0x04)] // sbyte
    [InlineData(0x05)] // byte
    [InlineData(0x06)] // short
    [InlineData(0x07)] // ushort
    [InlineData(0x03)] // char
    [InlineData(0x0B)] // ulong
    public void Resolve_BinaryWriter_WriteNarrowPrimitive_ThrowsLoud(byte paramByte)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write", Sig(0x01, paramByte)), "Write"));

        Assert.Contains("System.IO.BinaryWriter::Write", ex.Message);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteCharArray_ThrowsLoud()
    {
        // char[] = 0x1D SZARRAY whose element byte (0x03) is consumed by the
        // signature decoder; RawSignature sees 0x1D.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
                new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x03 }), "Write"));

        Assert.Contains("System.IO.BinaryWriter::Write", ex.Message);
    }

    //--------------------------------------------------------------------------
    // BinaryReader Read family
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BinaryReader_ReadInt32_MapsToReadInt32Icall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadInt32", Sig(0x08)), "ReadInt32");

        Assert.Equal("il2cpp::runtime::icall_binary_read_int32", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_BinaryReader_ReadInt64_MapsToReadInt64Icall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadInt64", Sig(0x0A)), "ReadInt64");

        Assert.Equal("il2cpp::runtime::icall_binary_read_int64", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_BinaryReader_ReadUInt32_MapsToReadUInt32Icall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadUInt32", Sig(0x09)), "ReadUInt32");

        Assert.Equal("il2cpp::runtime::icall_binary_read_uint32", resolved.CppName);
        Assert.Null(resolved.ResultKind); // int32 carrier slot, bit-pattern preserved
    }

    [Fact]
    public void Resolve_BinaryReader_ReadDouble_MapsToReadDoubleIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadDouble", Sig(0x0D)), "ReadDouble");

        Assert.Equal("il2cpp::runtime::icall_binary_read_double", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_BinaryReader_ReadSingle_MapsToReadSingleIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadSingle", Sig(0x0C)), "ReadSingle");

        Assert.Equal("il2cpp::runtime::icall_binary_read_single", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind); // singles widen into double slots
    }

    [Fact]
    public void Resolve_BinaryReader_ReadBoolean_MapsToReadBoolIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadBoolean", Sig(0x02)), "ReadBoolean");

        Assert.Equal("il2cpp::runtime::icall_binary_read_bool", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_BinaryReader_ReadString_MapsToReadStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadString", Sig(0x0E)), "ReadString");

        Assert.Equal("il2cpp::runtime::icall_binary_read_string", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // BinaryReader -- LOUD rejection of close-but-wrong / unwired members
    // (ReadByte routes to the WAVE2 RAW-byte icall below, NOT through
    // read_bool: that icall normalizes to 0/1 while .NET ReadByte returns the
    // raw byte 0..255).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BinaryReader_ReadByte_MapsToReadByteIcall()
    {
        // WAVE2: ReadByte() is wired to the RAW-byte icall; byte return 0x05.
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadByte", Sig(0x05)), "ReadByte");

        Assert.Equal("il2cpp::runtime::icall_binary_read_byte", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_BinaryReader_ReadInt16_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadInt16", Sig(0x06)), "ReadInt16"));

        Assert.Contains("System.IO.BinaryReader::ReadInt16", ex.Message);
    }

    [Fact]
    public void Resolve_BinaryReader_GetBaseStream_ThrowsLoud()
    {
        // get_BaseStream(): System.IO.Stream class return -- 0x12 + the
        // compressed TypeDefOrRef coded index (TypeRef row 1 == 0x05).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "BinaryReader", "get_BaseStream",
                new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_BaseStream"));

        Assert.Contains("System.IO.BinaryReader::get_BaseStream", ex.Message);
    }

    [Fact]
    public void Resolve_BinaryWriter_CtorStream_MapsToWriterCtor()
    {
        // .ctor(System.IO.Stream): 1 param, void -- class param 0x12 + the
        // TypeDefOrRef coded index (TypeRef row 1 == 0x05). Batch 150d: the
        // runtime object surface is wired (icall_binary_writer_ctor).
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", ".ctor",
            new byte[] { 0x00, 0x01, 0x01, 0x12, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_binary_writer_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Out-of-scope members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BinaryReader_Read7BitEncodedInt_FallsBackToGeneratedName()
    {
        // Out-of-scope member -> generated-name path. Header 0x20 (HAS_THIS)
        // models the real instance MemberRef (Read7BitEncodedInt is instance
        // in net48) -- the #44 header-based classification.
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "Read7BitEncodedInt",
            new byte[] { 0x20, 0x00, 0x08 }), "Read7BitEncodedInt");

        Assert.StartsWith("System_IO_BinaryReader_Read7BitEncodedInt_", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodRefResolverDispatchTests)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then one byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
    }

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
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
