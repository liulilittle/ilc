using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.TimeExt2 / MethodMap.DateTimeExtra dispatch tests: prove the
// timeext2.h surface (Parse / ParseExact / AddTicks / SubtractTicks /
// Compare) maps to the il2cpp_datetime_* icalls, that the DateTime ->
// TimeExt2 -> DateTimeExtra chain does NOT shadow the existing
// MethodMap.DateTime.cs wiring (ToString(format) still lands on the
// canonical formatter; get_Ticks maps through the PRIMARY DateTime domain
// to icall_datetime_get_ticks -- wired in batch 151b -- and DaysInMonth to
// icall_datetime_days_in_month -- wired in the net9/10 campaign -- so neither
// is a loud-rejection gap anymore), and that the remaining unwired DateTime
// surface is rejected LOUDLY (the "{owner}::{name}" prefix -- see BclResolve's wrap).
//
// PEs are synthesized with System.Reflection.Metadata's own writer, exactly
// like MethodRefResolverDispatchTests.cs. Signature bytes: 0x0A = i8 /
// DateTime value (8-byte), 0x0E = string, 0x08 = i4, 0x11 = valuetype.
public class MethodMapTestsTimeExt2
{
    //--------------------------------------------------------------------------
    // TimeExt2 domain (MethodMap.TimeExt2.cs -- timeext2.h il2cpp_datetime_*)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DateTime_ParseExact_MapsToParseExactIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTime", "ParseExact", Sig(0x0A, 0x0E, 0x0E)), "ParseExact");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_parse_exact", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_DateTime_AddTicks_MapsToAddTicksIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTime", "AddTicks", Sig(0x0A, 0x0A)), "AddTicks");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_add_ticks", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_SubtractTicks_MapsToSubtractTicksIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTime", "SubtractTicks", Sig(0x0A, 0x0A)), "SubtractTicks");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_subtract_ticks", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt", "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_Compare_MapsToCompareIcall()
    {
        // static Compare(DateTime, DateTime) -> int32: two valuetype (0x11)
        // params, each followed by its TypeHandle byte (0x05) -- the Sig()
        // helper cannot encode valuetype params (RawSignature consumes the
        // trailing TypeHandle, Guid/IntPtr test precedent).
        var resolved = ResolveMember(Synthesize("System", "DateTime", "Compare",
            new byte[] { 0x00, 0x02, 0x08, 0x11, 0x05, 0x11, 0x05 }), "Compare");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_compare", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Null(resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Chain order: the DateTime -> TimeExt2 -> DateTimeExtra fallback must NOT
    // shadow the existing MethodMap.DateTime.cs wiring.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DateTime_ToStringFormat_StillMapsThroughDateTimeDomain()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTime", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_datetime_to_string_format", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_DateTime_GetTicks_MapsToTicksIcall()
    {
        // get_Ticks was originally a loud-rejection gap (it still sits in the
        // DateTimeExtra rejection list), but batch 151b wired it in the PRIMARY
        // DateTime domain (MethodMap.DateTime.cs -> icall_datetime_get_ticks,
        // the runtime's real Ticks accessor at time.cpp:339). The dispatch
        // chain tries DateTime.cs FIRST (MethodRefResolver.cs:694-696), so this
        // member now maps to the icall and never reaches the loud-rejection
        // path. The wired mapping is the correct end state -- Ticks is a core
        // DateTime accessor the runtime implements, so a loud "not wired"
        // throw would be wrong -- hence this test pins the mapping.
        var resolved = ResolveMember(Synthesize("System", "DateTime", "get_Ticks", Sig(0x0A)), "get_Ticks");

        Assert.Equal("il2cpp::runtime::icall_datetime_get_ticks", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // DateTimeExtra domain (MethodMap.DateTimeExtra.cs -- loud rejections)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DateTime_ParseExactWithProvider_DropsProviderArg()
    {
        // ParseExact(string, string, IFormatProvider): 3 explicit args, the
        // provider a CLASS (0x12) followed by its TypeHandle byte (0x05) --
        // the only 3-arg ParseExact overload net48 C# can emit. The runtime
        // has only the 2-param icall, so the provider is DROPPED at emission
        // (ParamCount = the IL arity, "drop" cast precedent: String.Compare
        // 3-arg, DateTime.ToString(string, IFormatProvider)).
        var resolved = ResolveMember(Synthesize("System", "DateTime", "ParseExact",
            new byte[] { 0x00, 0x03, 0x0A, 0x0E, 0x0E, 0x12, 0x05 }), "ParseExact");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_parse_exact", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_Parse_MapsToParseIcall()
    {
        // DateTime.Parse(string) -> DateTime: the general invariant-culture
        // subset (timeext2.h). 1 explicit string arg, DateTime return (0x0A).
        var resolved = ResolveMember(Synthesize("System", "DateTime", "Parse", Sig(0x0A, 0x0E)), "Parse");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_parse", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_ParseWithProvider_DropsProviderArg()
    {
        // DateTime.Parse(string, IFormatProvider): 2 explicit args, the
        // provider a CLASS (0x12) followed by its TypeHandle byte (0x05) --
        // the shape every net48 DateTime.Parse call site emits. The runtime
        // has only the 1-param icall, so the provider is DROPPED at emission
        // (ParamCount = the IL arity, "drop" cast precedent: ParseExact
        // 3-arg, DateTime.ToString(string, IFormatProvider)).
        var resolved = ResolveMember(Synthesize("System", "DateTime", "Parse",
            new byte[] { 0x00, 0x02, 0x0A, 0x0E, 0x12, 0x05 }), "Parse");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_parse", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_OpEquality_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "DateTime", "op_Equality",
                new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_Equality"));

        Assert.Contains("System.DateTime::op_Equality", ex.Message);
    }

    [Fact]
    public void Resolve_DateTime_GetToday_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "DateTime", "get_Today", Sig(0x0A)), "get_Today"));

        Assert.Contains("System.DateTime::get_Today", ex.Message);
    }

    [Fact]
    public void Resolve_DateTime_DaysInMonth_MapsToDaysInMonthIcall()
    {
        // DaysInMonth was originally a loud-rejection gap, but the net9/10
        // campaign wired it in the PRIMARY DateTime domain (MethodMap.DateTime.cs
        // -> icall_datetime_days_in_month, time.cpp proleptic Gregorian). Like
        // get_Ticks above, the wired mapping is the correct end state -- a loud
        // "not wired" throw would be wrong for a core calendar accessor the
        // runtime implements -- hence this test pins the mapping (static,
        // 2 x int32 "i" casts, int32 result).
        var resolved = ResolveMember(Synthesize("System", "DateTime", "DaysInMonth", Sig(0x08, 0x08, 0x08)), "DaysInMonth");

        Assert.Equal("il2cpp::runtime::icall_datetime_days_in_month", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Null(resolved.ResultKind);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "i", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        string? receiverType = null, string? arg0Type = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, arg0Type, null);
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
