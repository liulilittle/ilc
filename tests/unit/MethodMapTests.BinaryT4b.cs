using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// T4b parity: BinaryWriter/Reader decimal + byte[]/char[] + char arms over the
// landed A2 icalls. Narrow/char ride the int32 carrier ("i"); decimal rides
// the Il2CppDecimal* slot ("dec" value cast / "dec2" out-last result).
// BaseStream stays LOUD (GC slot pending); byte/sbyte/short/ushort/ulong
// writes and sbyte/int16/uint16/uint64 reads stay LOUD (no runtime icall).
public class MethodMapTestsBinaryT4b
{
    [Fact]
    public void Resolve_BinaryWriter_WriteDecimal_MapsToWriteDecimalIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
            new byte[] { 0x00, 0x01, 0x01, 0x11, 0x05 }), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_decimal", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteChar_MapsToWriteCharIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
            new byte[] { 0x00, 0x01, 0x01, 0x03 }), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_char", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("System.Byte[]", "il2cpp::runtime::icall_binary_write_bytes_full")]
    [InlineData("System.Char[]", "il2cpp::runtime::icall_binary_write_chars_full")]
    public void Resolve_BinaryWriter_WriteWholeArray_MapsToFullIcall(string arg0Type, string expected)
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x08 }), "Write", arg0Type);

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { "", "a" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("System.Byte[]", "il2cpp::runtime::icall_binary_write_bytes")]
    [InlineData("System.Char[]", "il2cpp::runtime::icall_binary_write_chars")]
    public void Resolve_BinaryWriter_WriteArrayRange_MapsToRangedIcall(string arg0Type, string expected)
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
            new byte[] { 0x00, 0x03, 0x01, 0x1D, 0x08, 0x08, 0x08 }), "Write", arg0Type);

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { "", "a", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteArray_UntrackedElement_ThrowsLoud()
    {
        // 0x1D alone cannot prove byte[] vs char[] -- LOUD, never guess
        // (protocol corruption either way).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
                new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x08 }), "Write"));

        Assert.Contains("System.IO.BinaryWriter::Write", ex.Message);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteByteArray_SignatureElementDecides()
    {
        // Real ECMA-335: byte[] = SZARRAY(0x1D)+BYTE(0x05), no tracking needed.
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x05 }), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_bytes_full", resolved.CppName);
    }

    [Fact]
    public void Resolve_BinaryWriter_WriteCharArray_SignatureElementDecides()
    {
        // Real ECMA-335: char[] = SZARRAY(0x1D)+CHAR(0x03), no tracking needed.
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryWriter", "Write",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x03 }), "Write");

        Assert.Equal("il2cpp::runtime::icall_binary_write_chars_full", resolved.CppName);
    }

    [Fact]
    public void Resolve_BinaryReader_ReadDecimal_MapsToReadDecimalIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadDecimal",
            new byte[] { 0x00, 0x00, 0x11, 0x05 }), "ReadDecimal");

        Assert.Equal("il2cpp::runtime::icall_binary_read_decimal", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("dec2", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_BinaryReader_ReadChar_MapsToReadCharIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "BinaryReader", "ReadChar",
            new byte[] { 0x00, 0x00, 0x03 }), "ReadChar");

        Assert.Equal("il2cpp::runtime::icall_binary_read_char", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? arg0Type = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, arg0Type, null);
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
