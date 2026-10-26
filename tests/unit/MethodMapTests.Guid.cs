using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Guid dispatch tests: prove the System.Guid domain (TryMapGuid in
// MethodMap.Guid.cs) routes each member to the matching guid.h icall with
// the runtime's POINTER-shaped value-type passing:
//
//   Guid.NewGuid()                 -> il2cpp::il2cpp_guid_new_guid
//   Guid.ToString()                -> il2cpp::il2cpp_guid_to_string
//   Guid.TryParse(string, out G)   -> il2cpp::il2cpp_guid_parse
//   Guid.Equals(Guid)              -> il2cpp::il2cpp_guid_equals
//   Guid.CompareTo(Guid)           -> il2cpp::il2cpp_guid_compare
//
// The runtime treats a Guid as an opaque 16-byte value and every icall takes
// RAW POINTERS to those bytes (const uint8_t[16] decays to const uint8_t*).
// A Guid value slot is `uint8_t loc[16]` and its ldloca ADDRESS is a
// pointer-to-array -- it does NOT decay to uint8_t* -- so every byte-slot
// param carries the "g" cast code (the emitter renders it as
// `(uint8_t*)(...)`; see CodeGenerator.EmitIcallCallArgs). Plain array
// values and addresses both survive the cast, so "g" is correct for either
// shape. The parse input string gets the "s" cast (Il2CppString* -- the IR
// carries it as Il2CppObject*). NewGuid's value result carries the "guid"
// kind marker (the generated-code slot type for Guid does not exist yet;
// see the MethodMap.Guid.cs header).
//
// NOTE (interlockedcase regression, 2026-08-05): the "" (no-cast) code MUST
// stay a raw passthrough in the emitter -- Interlocked's int32_t*/int64_t*
// location params and value-type ctor receivers rely on it. Guid is the ONLY
// domain whose ""-cast slots were blanket-cast to (uint8_t*) by commit
// b59c6d9; that blanket is what broke Interlocked/valuetypes. "g" replaced
// it.
//
// Known-unwireable overloads reject LOUDLY (NotSupportedException with the
// "{owner}::{name}" prefix -- see BclResolve's wrap):
//   * ToString(string, IFormatProvider) -- the provider is dropped
//     (invariant-culture only); guid.h implements the N/D/B/P/X form only.
//   * Equals(object)/CompareTo(object) -- boxed Guids have no box class.
//   * .ctor(byte[])          -- guid.h has no from-bytes icall.
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (MetadataBuilder + ManagedPEBuilder -- net8.0 shared framework, no new
// packages), the same reader pipeline the transpiler uses. Signature blobs
// are the raw bytes RawSignature consumes (0x08=i4, 0x0E=string, 0x02=bool,
// 0x01=void, 0x11=valuetype, 0x1C=object, 0x1D=array, 0x10=byref);
// valuetype (0x11) return/params carry the TypeDefOrRef coded index of the
// owner TypeRef (row 1 -> 0x05) after the 0x11 byte, exactly as
// RawSignature reads them; SZARRAY (0x1D) params carry the element byte
// (byte = 0x05).
public class MethodMapGuidTests
{
    //--------------------------------------------------------------------------
    // NewGuid -- static, value result with the "guid" kind marker
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Guid_NewGuid_MapsToNewGuidIcall()
    {
        // static Guid NewGuid(): ret valuetype + the TypeRef coded index.
        var resolved = ResolveMember(Synthesize("System", "Guid", "NewGuid",
            new byte[] { 0x00, 0x00, 0x11, 0x05 }), "NewGuid");

        Assert.Equal("il2cpp::il2cpp_guid_new_guid", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("guid", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToString -- instance, string result; the receiver address passes
    // through to the icall's const uint8_t[16] parameter
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Guid_ToString_MapsToStringifyIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Guid", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::il2cpp_guid_to_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("g", resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_Guid_ToStringFormat_MapsToFormatFamily()
    {
        // icallwide: ToString(string format) wires to the full N/D/B/P/X
        // family (il2cpp_guid_to_string_format).
        var resolved = ResolveMember(Synthesize("System", "Guid", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::il2cpp_guid_to_string_format", resolved.CppName);
    }

    [Fact]
    public void Resolve_Guid_ToStringFormatProvider_MapsToFormatIcallDroppingProvider()
    {
        // net48 Guid.ToString(string format, IFormatProvider provider): the
        // provider is dropped at emission (invariant-culture only). The
        // IFormatProvider param is CLASS (0x12) + coded-index byte.
        var resolved = ResolveMember(Synthesize("System", "Guid", "ToString",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::il2cpp_guid_to_string_format", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "g", "s", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Parse / TryParse -- Parse maps to the strict wrapper icall
    // (il2cpp_guid_parse_strict: null -> ArgumentNullException, invalid ->
    // FormatException -- net48 semantics on top of the TryParse-shaped
    // core); TryParse(string, out Guid) maps to the plain parse icall with
    // the out-address as the output buffer
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Guid_Parse_MapsToStrictParseIcall()
    {
        // static Guid Parse(string): ret valuetype + TypeRef coded index,
        // param string.
        var resolved = ResolveMember(Synthesize("System", "Guid", "Parse",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0E }), "Parse");

        Assert.Equal("il2cpp::il2cpp_guid_parse_strict", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
        Assert.Equal("guid", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Guid_TryParse_MapsToParseIcall()
    {
        // static bool TryParse(string, out Guid): bool ret, string param,
        // BYREF (0x10) out param -- the out-address is the icall's buffer.
        // Blob: header 0x00, count 2, ret bool (0x02), param string (0x0E),
        // param byref (0x10) + valuetype (0x11) + TypeRef coded index (0x05).
        var resolved = ResolveMember(Synthesize("System", "Guid", "TryParse",
            new byte[] { 0x00, 0x02, 0x02, 0x0E, 0x10, 0x11, 0x05 }), "TryParse");

        Assert.Equal("il2cpp::il2cpp_guid_parse", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "g" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Equals / CompareTo -- instance, one Guid argument; both sides pass
    // through as raw byte pointers
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Guid_EqualsGuid_MapsToEqualsIcall()
    {
        // bool Equals(Guid): bool ret, valuetype (0x11) param + TypeRef coded
        // index (0x05) -- a multi-byte param, so a hand-built blob (the Sig
        // helper emits one byte per param and cannot carry the coded index).
        var resolved = ResolveMember(Synthesize("System", "Guid", "Equals",
            new byte[] { 0x00, 0x01, 0x02, 0x11, 0x05 }), "Equals");

        Assert.Equal("il2cpp::il2cpp_guid_equals", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "g", "g" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Guid_EqualsObject_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Guid", "Equals", Sig(0x02, 0x1C)), "Equals"));

        Assert.Contains("System.Guid::Equals", ex.Message);
    }

    [Fact]
    public void Resolve_Guid_CompareToGuid_MapsToCompareIcall()
    {
        // int32 CompareTo(Guid): int32 ret, valuetype (0x11) param + TypeRef
        // coded index (0x05) -- multi-byte param, hand-built blob.
        var resolved = ResolveMember(Synthesize("System", "Guid", "CompareTo",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "CompareTo");

        Assert.Equal("il2cpp::il2cpp_guid_compare", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "g", "g" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Guid_CompareToObject_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Guid", "CompareTo", Sig(0x08, 0x1C)), "CompareTo"));

        Assert.Contains("System.Guid::CompareTo", ex.Message);
    }

    //--------------------------------------------------------------------------
    // op_Equality / op_Inequality -- static, two Guid arguments; both pass
    // through as raw byte pointers (==/!= must not fall back to the
    // generated-name path, which would misclassify them as INSTANCE -- the
    // M1 heuristic in MethodRefResolver) and the result is a plain bool
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Guid_OpEquality_MapsToEqualsIcall()
    {
        // static bool op_Equality(Guid, Guid): bool ret, two valuetype (0x11)
        // params each + TypeRef coded index (0x05) -- hand-built blob (the
        // Sig helper emits one byte per param and cannot carry the coded
        // index).
        var resolved = ResolveMember(Synthesize("System", "Guid", "op_Equality",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_Equality");

        Assert.Equal("il2cpp::il2cpp_guid_equals", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "g", "g" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Guid_OpInequality_MapsToNegatedEqualsIcall()
    {
        // static bool op_Inequality(Guid, Guid): same shape as op_Equality;
        // the mapping emits the equality icall under a C++ logical-NOT (no
        // inequality icall exists on the guid.h surface).
        var resolved = ResolveMember(Synthesize("System", "Guid", "op_Inequality",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_Inequality");

        Assert.Equal("!il2cpp::il2cpp_guid_equals", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "g", "g" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // .ctor(byte[]) -- the net48 from-bytes ctor (il2cpp_guid_ctor_bytes).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Guid_CtorByteArray_MapsToCtorBytesIcall()
    {
        // void .ctor(byte[]): void ret, SZARRAY (0x1D) param + element byte
        // (0x05) -- multi-byte param, hand-built blob. The receiver slot carries
        // the "g" byte-pointer cast; the byte[] argument the "a" array cast.
        var resolved = ResolveMember(Synthesize("System", "Guid", ".ctor",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::il2cpp_guid_ctor_bytes", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "g", "a" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // GetHashCode -- boxwire: the value hash icall (guid.h). The unit test
    // used to pin the FALLBACK because the runtime had no Guid hash helper;
    // il2cpp_guid_get_hash_code now exists (the ORACLE-probed net48 fold).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Guid_GetHashCode_MapsToValueHashIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Guid", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.Equal("il2cpp::il2cpp_guid_get_hash_code", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "g" }, resolved.ParamTypeNames);
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
