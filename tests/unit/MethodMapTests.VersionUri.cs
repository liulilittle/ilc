using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Version + MethodMap.Uri dispatch tests: prove the System.Version
// / System.Uri domains (TryMapVersion/TryMapUri in MethodMap.Version.cs /
// MethodMap.Uri.cs). System.Version is CLOSED (M6 G6): the runtime owns the
// System_Version_class descriptor (Il2CppVersionObject, versionuri.h) and the
// object icalls, so every mapped member resolves to its icall -- the .ctor /
// getter / ToString / CompareTo / Equals / Parse / op_* mappings assert the
// exact C++ name + instance/static + return shape:
//
//   Version.ctor(string)        -> icall_version_ctor (instance, void)
//   Version.ctor(int,...)/( )   -> icall_version_ctor_2/3/4 / _default
//   Version.get_Major/Minor/... -> icall_version_get_* (instance, int32)
//   Version.ToString()          -> icall_version_to_string (instance, string)
//   Version.CompareTo/Equals    -> icall_version_compare/equals
//   Version.Parse(string)       -> icall_version_parse (static, returns Version)
//   Version.op_Equality/Inequality -> icall_version_op_equality/inequality
//   Version.GetHashCode         -> throws (default case: member not wired)
//
// System.Uri is CLOSED too (M6 G6): the runtime owns the System_Uri_class
// descriptor (Il2CppUriObject, versionuri.h -- scheme/host/path string
// slots, refFieldBitmap bits 1..3) and the object icalls, so every mapped
// member resolves to its icall -- the .ctor(string) / get_Scheme / get_Host
// / get_AbsolutePath / ToString mappings assert the exact C++ name +
// instance/static + return shape:
//
//   Uri.ctor(string)          -> icall_uri_ctor (instance, void)
//   Uri.get_Scheme/Host       -> icall_uri_get_scheme/host (instance, string)
//   Uri.get_AbsolutePath      -> icall_uri_get_path (instance, string)
//   Uri.ToString()            -> icall_uri_to_string (instance, string)
//   Uri.GetHashCode           -> throws (default case: member not wired)
//
// Signature blobs are the raw bytes RawSignature consumes (0x08=i4, 0x0E=
// string, 0x01=void, 0x02=bool, 0x12=class); CLASS (0x12) return/params carry
// the TypeDefOrRef coded index of the owner TypeRef (row 1 -> 0x05) after the
// 0x12 byte, exactly as RawSignature reads them.
public class MethodMapVersionUriTests
{
    //--------------------------------------------------------------------------
    // Version .ctor(string) -> icall_version_ctor (instance, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_CtorString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", ".ctor", Sig(0x01, 0x0E)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_version_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // Version int-component ctors -> icall_version_ctor_2/3/4; Version() ->
    // icall_version_ctor_default
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_CtorIntInt_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", ".ctor", Sig(0x01, 0x08, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_version_ctor_2", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
    }

    [Fact]
    public void Resolve_Version_CtorIntIntInt_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", ".ctor", Sig(0x01, 0x08, 0x08, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_version_ctor_3", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(3, m.ParamCount);
    }

    [Fact]
    public void Resolve_Version_CtorIntIntIntInt_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", ".ctor", Sig(0x01, 0x08, 0x08, 0x08, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_version_ctor_4", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(4, m.ParamCount);
    }

    [Fact]
    public void Resolve_Version_CtorDefault_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_version_ctor_default", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Version component getters -> icall_version_get_* (instance, int32)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_Major", "il2cpp::runtime::icall_version_get_major")]
    [InlineData("get_Minor", "il2cpp::runtime::icall_version_get_minor")]
    [InlineData("get_Build", "il2cpp::runtime::icall_version_get_build")]
    [InlineData("get_Revision", "il2cpp::runtime::icall_version_get_revision")]
    public void Resolve_Version_ComponentGetter_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System", "Version", name, Sig(0x08)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Version.ToString() -> icall_version_to_string (instance, returns string)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_ToString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_version_to_string", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // Version.CompareTo(object) / Equals(object) -> component icalls
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_CompareToVersion_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", "CompareTo",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05 }), "CompareTo");

        Assert.Equal("il2cpp::runtime::icall_version_compare", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Version_EqualsVersion_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", "Equals",
            new byte[] { 0x00, 0x01, 0x02, 0x12, 0x05 }), "Equals");

        Assert.Equal("il2cpp::runtime::icall_version_equals", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Version.Parse(string) -> icall_version_parse (static, returns Version)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_ParseString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", "Parse",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "Parse");

        Assert.Equal("il2cpp::runtime::icall_version_parse", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsRef);
        Assert.Equal("System.Version", m.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Version.op_Equality / op_Inequality (static) -> op icalls
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_OpEquality_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", "op_Equality",
            new byte[] { 0x00, 0x02, 0x02, 0x12, 0x05, 0x12, 0x05 }), "op_Equality");

        Assert.Equal("il2cpp::runtime::icall_version_op_equality", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Version_OpInequality_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Version", "op_Inequality",
            new byte[] { 0x00, 0x02, 0x02, 0x12, 0x05, 0x12, 0x05 }), "op_Inequality");

        Assert.Equal("il2cpp::runtime::icall_version_op_inequality", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Uri .ctor(string) -> icall_uri_ctor (instance, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_CtorString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", ".ctor", Sig(0x01, 0x0E)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_uri_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // Uri getters -> icall_uri_get_* (instance, returns string)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_Scheme", "il2cpp::runtime::icall_uri_get_scheme")]
    [InlineData("get_Host", "il2cpp::runtime::icall_uri_get_host")]
    [InlineData("get_AbsolutePath", "il2cpp::runtime::icall_uri_get_path")]
    public void Resolve_Uri_Getter_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System", "Uri", name, Sig(0x0E)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // Uri.ToString() -> icall_uri_to_string (instance, returns string)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_ToString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_uri_to_string", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // Members without a mapping hit the default -- LOUD "member not wired"
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_GetHashCode_ThrowsLoudNotWired()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Version", "GetHashCode", Sig(0x08)), "GetHashCode"));

        Assert.StartsWith("System.Version::GetHashCode: ", ex.Message);
        Assert.Contains("member not wired", ex.Message);
    }

    [Fact]
    public void Resolve_Uri_GetHashCode_ThrowsLoudNotWired()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Uri", "GetHashCode", Sig(0x08)), "GetHashCode"));

        Assert.StartsWith("System.Uri::GetHashCode: ", ex.Message);
        Assert.Contains("member not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Guid.cs)
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
