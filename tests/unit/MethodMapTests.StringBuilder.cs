using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.StringBuilder dispatch tests: prove the StringBuilder domain
// routes the AppendLine / AppendFormat / Replace families to their runtime
// icalls (il2cpp_stringbuilder.h), that the chain contract holds (every
// member returns the SAME builder: ReturnsValue + ReturnsRef with
// ReturnTypeName "System.Text.StringBuilder"), and that close-but-wrong
// signatures reject LOUDLY (NotSupportedException -- see BclResolve's wrap).
//
// Signature blobs are the raw bytes RawSignature consumes; ret byte 0x1C
// (object) avoids the 0x11/0x12 ret-handle consumption (the dispatch ignores
// the ret byte). CLASS params (0x12) carry a TypeDefOrRef coded-index byte
// after them.
public class MethodMapStringBuilderTests
{
    //--------------------------------------------------------------------------
    // AppendLine -- () and (string) both chain
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendLine_NoArgs_MapsToAppendLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendLine", Sig(0x1C)), "AppendLine");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_line", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendLine_String_MapsToAppendLineStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendLine", Sig(0x1C, 0x0E)), "AppendLine");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_line_s", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { true }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    //--------------------------------------------------------------------------
    // AppendFormat -- the (string, object[, object[, object]]) family
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendFormat_OneArg_MapsToAppendFormat1Icall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            Sig(0x1C, 0x0E, 0x1C)), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_1", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { true, true }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_TwoArgs_MapsToAppendFormat2Icall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            Sig(0x1C, 0x0E, 0x1C, 0x1C)), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_2", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_ThreeArgs_MapsToAppendFormat3Icall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            Sig(0x1C, 0x0E, 0x1C, 0x1C, 0x1C)), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_3", resolved.CppName);
        Assert.Equal(4, resolved.ParamCount);
        AssertChains(resolved);
    }

    //--------------------------------------------------------------------------
    // AppendFormat -- close-but-wrong signatures reject LOUDLY
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendFormat_NonObjectSecondArg_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
                Sig(0x1C, 0x0E, 0x08)), "AppendFormat")); // (string, int) does not exist

        Assert.Contains("System.Text.StringBuilder::AppendFormat", ex.Message);
    }

    [Fact]
    public void Resolve_AppendFormat_FormatProviderOverload_ThrowsLoud()
    {
        // (IFormatProvider, string, object) -- ps[0] is a CLASS, not string.
        // Hand-built blob: the CLASS param (0x12) consumes its TypeRef coded
        // index (0x05 = the synthetic PE's single TypeRef row), so the param
        // count is 3, not 4 (the Sig helper cannot express this).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
                new byte[] { 0x00, 0x03, 0x1C, 0x12, 0x05, 0x0E, 0x1C }), "AppendFormat"));

        Assert.Contains("System.Text.StringBuilder::AppendFormat", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Replace -- (string, string) and (char, char)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Replace_StringString_MapsToReplaceStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Replace",
            Sig(0x1C, 0x0E, 0x0E)), "Replace");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_replace_s_s", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { true, true }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_Replace_CharChar_MapsToReplaceCharIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Replace",
            Sig(0x1C, 0x03, 0x03)), "Replace");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_replace_c_c", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "c" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { true, false, false }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_Replace_MixedTypes_ThrowsLoud()
    {
        // (string, char) -- no such net48 overload
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "Replace",
                Sig(0x1C, 0x0E, 0x03)), "Replace"));

        Assert.Contains("System.Text.StringBuilder::Replace", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss within the domain -- falls back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_UnwiredMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Frobnicate", Sig(0x1C, 0x0E)), "Frobnicate");

        Assert.StartsWith("System_Text_StringBuilder_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same harness as the other
    // MethodMapTests: real ECMA-335 assembly whose only MemberReference is
    // the member under test)
    //--------------------------------------------------------------------------
    private static void AssertChains(ResolvedMethod resolved)
    {
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Text.StringBuilder", resolved.ReturnTypeName);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count (=
    // ps.Length), return byte, then one byte per parameter (the raw bytes
    // RawSignature decodes; CLASS params 0x12 carry a trailing coded-index
    // byte the decoder consumes).
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
