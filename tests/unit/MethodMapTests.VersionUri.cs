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
// descriptor (Il2CppUriObject, versionuri.h -- scheme/host/path/opaque/
// original string slots, refFieldBitmap bits 1..5, plus the isAbsolute flag)
// and the object icalls, so every mapped member resolves to its icall --
// the .ctor(string) / .ctor(string, UriKind) / get_Scheme / get_Host /
// get_AbsolutePath / get_OriginalString / get_AbsoluteUri /
// get_IsAbsoluteUri / Equals / ToString mappings assert the exact C++ name +
// instance/static + return shape:
//
//   Uri.ctor(string)          -> icall_uri_ctor (instance, void)
//   Uri.ctor(string, UriKind) -> icall_uri_ctor_2 (instance, void; P1)
//   Uri.get_Scheme/Host       -> icall_uri_get_scheme/host (instance, string)
//   Uri.get_AbsolutePath      -> icall_uri_get_path (instance, string)
//   Uri.get_OriginalString    -> icall_uri_get_originalstring (P1)
//   Uri.get_AbsoluteUri       -> icall_uri_get_absoluteuri (P1)
//   Uri.get_IsAbsoluteUri     -> icall_uri_get_is_absolute_uri (P1, bool)
//   Uri.Equals(object)        -> icall_uri_equals (P1, bool; also reached
//                                via Object::Equals on a Uri receiver)
//   Uri.ToString()            -> icall_uri_to_string (instance, string)
//   Uri.GetHashCode           -> icall_uri_get_hash_code (P3: deterministic
//                                ordinal contract hash -- accepted deviation
//                                from the net48 NLS integer; also reached
//                                via Object::GetHashCode on a Uri receiver)
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
    // Version.ToString(int fieldCount) -> icall_version_to_string_field
    // (net48 version.cs:284). The field count selects how many components are
    // rebuilt ("" / major / major.minor / major.minor.build /
    // major.minor.build.revision) and throws ArgumentException when a missing
    // component is requested (build absent -> fieldCount >= 3; revision absent
    // -> fieldCount >= 4) or the count is out of range (> 4).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_ToStringInt_MapsToFieldIcall()
    {
        // instance string ToString(int fieldCount): ret string (0x0E), one
        // int32 param (0x08).
        var m = ResolveMember(Synthesize("System", "Version", "ToString", Sig(0x0E, 0x08)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_version_to_string_field", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        // Receiver passes through (the Version object) + the int32 field count.
        Assert.Equal(new[] { "", "i" }, m.ParamTypeNames);
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
    // URI-FIX P4b: Uri.get_AbsolutePath -> icall_uri_get_path (instance,
    // returns string). Given the get_AbsolutePath member reference on
    // System.Uri, when resolved, then it reaches the normalizing path icall
    // (instance, 0 params, string ref) -- the runtime folds dot-segments
    // (C3) and maps empty->"/" (C6) for hierarchical absolute URIs while
    // keeping "%XX" escapes verbatim and opaque/relative paths untouched
    // (net48 oracle: diffuri004 -- "/a", "/b/c", "/a%20b", "/", "/bar",
    // "a/../b", "/x"). The text behavior itself is proven by the
    // diffuri004 byte-EQ e2e; this fact locks the dispatch half of P4b.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_AbsolutePath_P4b_MapsToNormalizedPathIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", "get_AbsolutePath", Sig(0x0E)), "get_AbsolutePath");

        Assert.Equal("il2cpp::runtime::icall_uri_get_path", m.CppName);
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
    // URI-FIX P1: Uri .ctor(string, UriKind) -> icall_uri_ctor_2 (instance,
    // void). UriKind is a valuetype enum int32 carrier: 0x11 + TypeDefOrRef
    // row (0x05), mirroring the Version op_Equality blob shape below.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_CtorStringUriKind_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", ".ctor",
            new byte[] { 0x00, 0x02, 0x01, 0x0E, 0x11, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_uri_ctor_2", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // URI-BASE: Uri(Uri baseUri, string relativeUri) -> icall_uri_ctor_base_s
    // (instance, void). Both operands are CLASS signatures: the base Uri is
    // 0x12 + TypeDefOrRef (0x05), the relative is 0x0E (string). net48
    // System_net_System_URI.cs:452.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_CtorBaseUriRelativeString_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", ".ctor",
            new byte[] { 0x00, 0x02, 0x01, 0x12, 0x05, 0x0E }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_uri_ctor_base_s", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "", "", "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // URI-BASE: Uri(Uri baseUri, Uri relativeUri) -> icall_uri_ctor_base_u
    // (instance, void). Both operands are Uri CLASS values (0x12 + 0x05).
    // net48 System_net_System_URI.cs:504.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_CtorBaseUriRelativeUri_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", ".ctor",
            new byte[] { 0x00, 0x02, 0x01, 0x12, 0x05, 0x12, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_uri_ctor_base_u", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "", "", "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // URI-FIX P1: new getters -> icall_uri_get_* (instance, returns string)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_OriginalString", "il2cpp::runtime::icall_uri_get_originalstring")]
    [InlineData("get_AbsoluteUri", "il2cpp::runtime::icall_uri_get_absoluteuri")]
    public void Resolve_Uri_NewGetter_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System", "Uri", name, Sig(0x0E)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // URI-FIX P1: Uri.get_IsAbsoluteUri -> bool icall (instance, value)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_IsAbsoluteUri_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", "get_IsAbsoluteUri", Sig(0x02)), "get_IsAbsoluteUri");

        Assert.Equal("il2cpp::runtime::icall_uri_get_is_absolute_uri", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // URI-FIX P1: Uri.Equals(object) -> icall_uri_equals (instance, bool)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Uri_EqualsObject_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "Uri", "Equals",
            new byte[] { 0x00, 0x01, 0x02, 0x12, 0x05 }), "Equals");

        Assert.Equal("il2cpp::runtime::icall_uri_equals", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // URI-FIX P1: Object::Equals on a tracked Uri receiver routes to the Uri
    // value icall (evidence: gen Cases_Prog.cpp:379 takes the boxed path
    // today, yielding reference equality for uri.equals.same).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Object_Equals_UriReceiver_MapsToUriEquals()
    {
        var m = ResolveMember(Synthesize("System", "Object", "Equals",
            new byte[] { 0x00, 0x01, 0x02, 0x12, 0x05 }), "Equals", receiverType: "System.Uri");

        Assert.Equal("il2cpp::runtime::icall_uri_equals", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Members without a mapping hit the default -- LOUD "member not wired"
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Version_GetHashCode_MapsToComponentFold()
    {
        // icallwide: Version.GetHashCode wires to the deterministic
        // component fold (same-value -> same-hash contract).
        var resolved = ResolveMember(Synthesize("System", "Version", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.Equal("il2cpp::runtime::icall_version_get_hash_code", resolved.CppName);
    }

    [Fact]
    public void Resolve_Uri_GetHashCode_MapsToContractHash()
    {
        // URI-FIX P3 (accepted deviation, parent ruling 2026-09-19): exact
        // net48 NLS-int replication is infeasible (native sort tables); the
        // runtime hash is a deterministic ordinal fold over the normalized
        // absolute text (relative: OriginalString), paired with the ordinal
        // Equals -- contract equality only, never the oracle integer.
        var m = ResolveMember(Synthesize("System", "Uri", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.Equal("il2cpp::runtime::icall_uri_get_hash_code", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    [Fact]
    public void Resolve_Object_GetHashCode_UriReceiver_MapsToContractHash()
    {
        // Roslyn emits callvirt Object::GetHashCode for the override, like
        // Equals (P1b) -- a tracked Uri receiver must reach the contract
        // hash, not the identity default arm below.
        var m = ResolveMember(Synthesize("System", "Object", "GetHashCode", Sig(0x08)), "GetHashCode",
            receiverType: "System.Uri");

        Assert.Equal("il2cpp::runtime::icall_uri_get_hash_code", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Guid.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
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
