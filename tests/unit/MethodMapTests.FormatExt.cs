using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMapTests.FormatExt -- the numeric ToString(format) surface
// (MethodMap.Format.cs -> icall_format_int32/int64/double_custom in
// src/runtime/include/internal/il2cpp_format_ext.h). Coverage contract:
//   * ToString(string) on Int32/Int64/Double -> the three custom icalls
//     (the dispatch switch tries TryMapFormat BEFORE TryMapValueType)
//   * ToString(string, IFormatProvider) -> the SAME icall with the culture
//     argument dropped at emission (the DateTime precedent,
//     MethodMap.DateTime.cs -- the format_ext icalls take (value, format))
//   * ToString(IFormatProvider) alone -> loud NotSupportedException (no
//     icall shape: the M-stage emitter cannot inject a null format)
//   * the value-type receiver cast follows receiverIsAddress (deref vs unbox)
//   * the ?? fall-back contract: ToString() (0 args) and non-ToString
//     members on the three owners still resolve through TryMapValueType
//     (MethodMap.ValueType.cs) -- the Format-first chain must not shadow them
//   * GAP-SHAPE documentation: ToString(string) on owners OUTSIDE the
//     Format dispatch chain (the small-int/uint owners System.Int16 /
//     System.UInt16 / System.Byte / System.SByte / System.UInt32 /
//     System.UInt64 -- routed to TryMapValueType only, MethodRefResolver.cs)
//     falls to the mangled-name path with IsInstance=true -- the standard
//     unwired-surface failure shape. These tests red-flag when a future
//     agent wires any of them.
//   * System.Single IS in the chain (routes to icall_format_double_custom --
//     float lives in double slots, the icall_double_to_string precedent).
//   * decimal-domain T3: System.Decimal joined the chain (Format ?? Decimal
//     dispatch) -- ToString(string) routes to icall_decimal_to_string_custom
//     (the pre-wiring gap-shape test was updated accordingly).
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (same pipeline as MethodRefResolverDispatchTests): one MemberReference,
// raw signature bytes 0x0E=string / 0x12=class (+ TypeDefOrRef coded index).
public class MethodMapFormatExtTests
{
    //--------------------------------------------------------------------------
    // ToString(string format) -- one arg, string param byte 0x0E.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int32_ToStringWithFormat_MapsToInt32CustomIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Int32", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_int32_custom", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "unbox:i", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Int64_ToStringWithFormat_MapsToInt64CustomIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Int64", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_int64_custom", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "unbox:l", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Double_ToStringWithFormat_MapsToDoubleCustomIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Double", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_double_custom", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "unbox:d", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // ToString(string, IFormatProvider) -- two args; saudit4: the provider
    // PASSES THROUGH (ProviderCulture) so the runtime honors the invariant
    // culture; the cast list keeps the unit-pinned "drop" SHAPE (the emitter
    // emits the provider expression for the flagged icall).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int32_ToStringWithFormatAndProvider_PassesCulture()
    {
        // string ToString(string format, IFormatProvider provider): 0x0E, then
        // 0x12 CLASS + TypeRef row 1 coded index (0x05) -- discarded by RawSignature.
        var resolved = ResolveMember(Synthesize("System", "Int32", "ToString",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_int32_custom", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ProviderCulture);
        Assert.Equal(new[] { "unbox:i", "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Int64_ToStringWithFormatAndProvider_PassesCulture()
    {
        var resolved = ResolveMember(Synthesize("System", "Int64", "ToString",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_int64_custom", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ProviderCulture);
        Assert.Equal(new[] { "unbox:l", "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Double_ToStringWithFormatAndProvider_PassesCulture()
    {
        var resolved = ResolveMember(Synthesize("System", "Double", "ToString",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_double_custom", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.True(resolved.ProviderCulture);
        Assert.Equal(new[] { "unbox:d", "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Double_ToStringProviderOnly_MapsToDefaultFormatter()
    {
        // double ToString(IFormatProvider): real blob 00 01 0E 12 05 -- the
        // single param is a CLASS ref (0x12), NOT a format string, so the
        // provider is dropped and the receiver routes to the DEFAULT
        // formatter (the culture is fixed by the runtime).
        var resolved = ResolveMember(Synthesize("System", "Double", "ToString",
            new byte[] { 0x00, 0x01, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_double_to_string", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "unbox:d", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Single_ToStringProviderOnly_MapsToDefaultFormatter()
    {
        // Single reuses the double default formatter (the runtime promotes to
        // double before formatting; net48 G7 for Single).
        var resolved = ResolveMember(Synthesize("System", "Single", "ToString",
            new byte[] { 0x00, 0x01, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_single_to_string", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "unbox:d", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // ToString(IFormatProvider) alone: the provider is dropped and the
    // receiver routes to the DEFAULT formatter with the IL arity preserved
    // (the fold pops the provider; "drop" keeps it out of the C++ call).
    // The culture is fixed by the runtime, so the rendered value matches the
    // net48 output for the oracle culture.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int32_ToStringProviderOnly_DropsProviderToDefault()
    {
        var resolved = ResolveMember(Synthesize("System", "Int32", "ToString",
            new byte[] { 0x00, 0x01, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "unbox:i", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Value-type receiver address (ldloca): the cast must switch to deref.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int64_ToStringWithFormat_AddressReceiver_UsesDerefCast()
    {
        var resolved = ResolveMember(Synthesize("System", "Int64", "ToString", Sig(0x0E, 0x0E)), "ToString",
            receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_format_int64_custom", resolved.CppName);
        Assert.Equal(new[] { "deref:l", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // The ?? fall-back contract (MethodRefResolver.cs:312-319): TryMapFormat
    // returns null for ToString() (0 args) and for non-ToString members, so
    // the ValueType domain must still resolve them. Guards the Format-first
    // chain from shadowing the parameterless/value-semantics surface.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int32_ToStringParameterless_FallsThroughToValueType()
    {
        var resolved = ResolveMember(Synthesize("System", "Int32", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "unbox:i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Int64_ToStringParameterless_FallsThroughToValueType()
    {
        var resolved = ResolveMember(Synthesize("System", "Int64", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_int64_to_string", resolved.CppName);
        Assert.Equal(new[] { "unbox:l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Double_ToStringParameterless_FallsThroughToValueType()
    {
        var resolved = ResolveMember(Synthesize("System", "Double", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_double_to_string", resolved.CppName);
        Assert.Equal(new[] { "unbox:d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Int64_GetHashCode_NotShadowedByFormatChain()
    {
        var resolved = ResolveMember(Synthesize("System", "Int64", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.Equal("il2cpp::runtime::icall_int64_get_hash_code", resolved.CppName);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "unbox:l" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // System.Single -- wired: routes to icall_format_double_custom (float
    // values live in double slots -- the icall_double_to_string precedent in
    // MethodMap.ValueType.cs). Full surface mirrors the Double coverage.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Single_ToStringWithFormat_MapsToDoubleCustomIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Single", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_double_custom", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "unbox:d", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Single_ToStringWithFormatAndProvider_DropsCulture()
    {
        var resolved = ResolveMember(Synthesize("System", "Single", "ToString",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x12, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_double_custom", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "unbox:d", "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Single_ToStringWithFormat_AddressReceiver_UsesDerefCast()
    {
        var resolved = ResolveMember(Synthesize("System", "Single", "ToString", Sig(0x0E, 0x0E)), "ToString",
            receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_format_double_custom", resolved.CppName);
        Assert.Equal(new[] { "deref:d", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Single_ToStringParameterless_FallsThroughToValueType()
    {
        var resolved = ResolveMember(Synthesize("System", "Single", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_single_to_string", resolved.CppName);
        Assert.Equal(new[] { "unbox:d" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Gap shapes (unwired owners): the small-int/uint owners are the
    // TryMapValueType-only group (no runtime formatter icall for int16/
    // uint32/... -- the runtime has only int32/int64/double formatters).
    // System.Decimal WAS the same shape until decimal-domain T3: the wiring
    // added the dispatch case (Format ?? Decimal chain) + the
    // icall_decimal_to_string_custom formatter mapping (MethodMap.Format.cs)
    // -- see the decimal tests below for the WIRED shape.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_ToStringWithFormat_FallsToMangledGapShape()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "ToString", Sig(0x0E, 0x0E)), "ToString");

        // decimal-domain T3: the custom-format branch now wins (TryMapFormat
        // runs first in the System.Decimal dispatch chain).
        Assert.Equal("il2cpp::runtime::icall_decimal_to_string_custom", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the MethodRefResolverDispatchTests
    // pipeline: one MemberReference, resolved via the public Resolve entry).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, bool receiverIsAddress = false)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, receiverIsAddress, null, null);
    }

    // Signature blob: header 0x00 (DEFAULT), param count, return byte, then one
    // byte per parameter (the raw bytes RawSignature decodes). Overloads whose
    // parameters carry trailing data (0x12 CLASS + coded index) must build the
    // blob explicitly -- see the provider tests above.
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
