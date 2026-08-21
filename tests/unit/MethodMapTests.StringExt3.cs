using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.StringExt3 dispatch tests: prove the BCL dispatch chain
// (MethodRefResolver.cs:329 -- TryMapString ?? TryMapStringExt3) routes the
// System.String POSITIONAL-SEARCH / COMPARISON-MODE overloads to the
// il2cpp_string_ext3.h icalls, distinguished by RawSignature shape:
//
//   IndexOf(char, int32) / (char, int32, int32)
//   IndexOf(string, int32) / (string, int32, int32)
//   LastIndexOf(string) / (char, int32) / (char, int32, int32)
//   LastIndexOf(string, int32) / (string, int32, int32)
//   StartsWith(string, StringComparison) / EndsWith(string, StringComparison)
//
// The single-position forms stay with MethodMap.String.cs (they must NOT be
// re-mapped here). The StringComparison argument is the StringComparison
// ENUM -- a valuetype byte 0x11 plus its TypeHandle in the signature blob
// (RawSignature consumes the handle, so ps[1] == 0x11).
public class MethodMapTestsStringExt3
{
    //--------------------------------------------------------------------------
    // IndexOf position/range overloads
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IndexOfCharStart_MapsToFromIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "IndexOf", Sig(0x08, 0x03, 0x08)), "IndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_index_of_c_from", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_IndexOfCharStartCount_MapsToRangeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "IndexOf", Sig(0x08, 0x03, 0x08, 0x08)), "IndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_index_of_c_range", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_IndexOfStringStart_MapsToFromIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "IndexOf", Sig(0x08, 0x0E, 0x08)), "IndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_index_of_s_from", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_IndexOfStringStartCount_MapsToRangeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "IndexOf", Sig(0x08, 0x0E, 0x08, 0x08)), "IndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_index_of_s_range", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // LastIndexOf family (including the string single-arg form)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_LastIndexOfString_MapsToStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "LastIndexOf", Sig(0x08, 0x0E)), "LastIndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_last_index_of_s", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_LastIndexOfCharStart_MapsToFromIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "LastIndexOf", Sig(0x08, 0x03, 0x08)), "LastIndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_last_index_of_c_from", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_LastIndexOfCharStartCount_MapsToRangeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "LastIndexOf", Sig(0x08, 0x03, 0x08, 0x08)), "LastIndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_last_index_of_c_range", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_LastIndexOfStringStart_MapsToFromIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "LastIndexOf", Sig(0x08, 0x0E, 0x08)), "LastIndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_last_index_of_s_from", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_LastIndexOfStringStartCount_MapsToRangeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "LastIndexOf", Sig(0x08, 0x0E, 0x08, 0x08)), "LastIndexOf");

        Assert.Equal("il2cpp::runtime::icall_string_last_index_of_s_range", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // StartsWith / EndsWith comparison-mode overloads (StringComparison enum:
    // valuetype byte 0x11 + TypeHandle in the signature)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StartsWithStringComparison_MapsToCultureIcall()
    {
        var resolved = ResolveMember(
            Synthesize("System", "String", "StartsWith", EnumSig(0x02, 0x0E)), "StartsWith");

        Assert.Equal("il2cpp::runtime::icall_string_startswith_culture", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_EndsWithStringComparison_MapsToCultureIcall()
    {
        var resolved = ResolveMember(
            Synthesize("System", "String", "EndsWith", EnumSig(0x02, 0x0E)), "EndsWith");

        Assert.Equal("il2cpp::runtime::icall_string_endswith_culture", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
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

    // Signature blob: header 0x00 (DEFAULT), param count, return byte, then
    // one byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
    }

    // Signature ending in an ENUM (StringComparison) parameter: the valuetype
    // byte 0x11 followed by its TypeHandle -- a compressed TypeDefOrRef coded
    // index. The synthesized PE holds exactly ONE TypeRef (row 1, the type
    // being declared -- "System.String"), so the coded index is (1 << 2) | 1
    // = 0x05 (TypeRef tag). A zero handle would resolve to TypeDef row 0 and
    // blow up in GetFullName (Guid test precedent, MethodMapTests.Guid.cs).
    private static byte[] EnumSig(byte ret, params byte[] leading)
    {
        var bytes = new byte[3 + leading.Length + 2];
        bytes[0] = 0x00;
        bytes[1] = (byte)(leading.Length + 1);
        bytes[2] = ret;
        Array.Copy(leading, 0, bytes, 3, leading.Length);
        bytes[3 + leading.Length] = 0x11;
        bytes[3 + leading.Length + 1] = 0x05; // TypeRef row 1 coded index
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
