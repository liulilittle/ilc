using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.TimeZone dispatch tests: prove the BCL domain dispatch routes
// the System.TimeZoneInfo / System.TimeZone surface to TryMapTimeZone
// (MethodMap.TimeZone.cs) and lock the timezone.h contract
// (timezone.h -- the per-date evaluation family + the current-instant
// offset family used by the converters).
//
// Coverage contract (timezone.h surface):
//   * get_Local / get_Utc / get_CurrentTimeZone fold to null (NullMarker)
//     with the zone TAG on ReturnTypeName ("System.TimeZoneInfo+Local" /
//     "System.TimeZoneInfo+Utc") -- the folded-null receiver re-resolve
//     (StackFold NullExpr.TypeName) dispatches the instance members.
//   * get_UtcOffset(DateTime) -> TimeSpan maps to
//     icall_timespan_from_ticks with the PER-DATE SECONDS -> TICKS
//     conversion carried by the "tzsec_at" receiver cast (the
//     System.TimeZoneInfo Local branch -- the offset is evaluated at the
//     DateTime argument) or the "tzsec_legacy" cast (the obsolete
//     System.TimeZone branch -- Kind-aware: a Kind=Utc input is Zero); the
//     Utc branch injects a pre-appended 0 (PrependZeroArg -- never the
//     machine offset).
//   * ToLocalTime / ToUniversalTime -> il2cpp_datetime_to_local /
//     il2cpp_datetime_from_local ("dt" result kind + "System.DateTime"
//     ReturnTypeName for chained member resolution).
//   * IsDaylightSavingTime -> il2cpp_timezone_get_is_daylight_saving_at
//     (per-date; the DateTime argument passes by value).
//   * IsInvalidTime -> il2cpp_timezone_get_is_invalid_time (per-date).
//   * The name/registry/zone-table surface (get_Id, FindSystemTimeZoneById
//     ...) MUST be LOUD (NotSupportedException) -- no silent fallback.
//   * Members NOT in the domain return null -> the generated-name path.
//
// Same synthetic-PE pipeline as MethodMapTests.Stopwatch: a real ECMA-335
// assembly whose only MemberReference is the member under test. Signature
// blobs use ret 0x0A (int64) / 0x1C (object) -- the domains under test only
// check ps.Length (the DateTime overloads are arity-1).
public class MethodMapTimeZoneTests
{
    private const string LocalTag = "System.TimeZoneInfo+Local";
    private const string UtcTag = "System.TimeZoneInfo+Utc";

    //--------------------------------------------------------------------------
    // Singletons -- fold to null with the zone tag (NullExpr.TypeName)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_TimeZoneInfo_GetLocal_FoldsToNullWithLocalTag()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "get_Local", Sig(0x1C, 0)), "get_Local");

        Assert.Equal(ResolvedMethod.NullMarker, resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(LocalTag, resolved.ReturnTypeName); // the receiver-hint tag
    }

    [Fact]
    public void Resolve_TimeZoneInfo_GetUtc_FoldsToNullWithUtcTag()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "get_Utc", Sig(0x1C, 0)), "get_Utc");

        Assert.Equal(ResolvedMethod.NullMarker, resolved.CppName);
        Assert.Equal(UtcTag, resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_TimeZone_GetCurrentTimeZone_FoldsToNullWithLocalTag()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZone", "get_CurrentTimeZone", Sig(0x1C, 0)), "get_CurrentTimeZone");

        Assert.Equal(ResolvedMethod.NullMarker, resolved.CppName);
        Assert.Equal(LocalTag, resolved.ReturnTypeName); // net48 CurrentTimeZone IS TimeZoneInfo.Local
    }

    //--------------------------------------------------------------------------
    // get_UtcOffset(DateTime) -> TimeSpan -- the SECONDS -> TICKS conversion
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_TimeZoneInfo_GetUtcOffset_NoReceiver_DefersToReceiverResolve()
    {
        // receiverType == null is the fold's FIRST resolution: the domain
        // DEFERS (TryMapTimeZone returns null) and the resolver falls back
        // to the generated-name path -- the fold's callvirt re-resolve
        // (StackFold.cs:1355) then routes the receiver's tag through the
        // domain again (the Encoding deferral precedent -- the members are
        // VIRTUAL, the tag rides the folded null's NullExpr.TypeName).
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "get_UtcOffset", Sig(0x0A, 1, 0x0A)), "get_UtcOffset");

        Assert.StartsWith("System_TimeZoneInfo_get_UtcOffset_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    [Fact]
    public void Resolve_TimeZoneInfo_GetUtcOffset_LocalReceiver_MapsToPerDateIcallWithTzsecAtCast()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "get_UtcOffset", Sig(0x0A, 1, 0x0A)), "get_UtcOffset",
            LocalTag);

        Assert.Equal("il2cpp::runtime::icall_timespan_from_ticks", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        // The Local receiver slot carries the "tzsec_at" cast: the emitter
        // replaces the folded-null receiver with the runtime's PER-DATE
        // offset at the DateTime argument in SECONDS scaled to TICKS
        // (10^7/1s). The DateTime argument is PASSED (the per-date domain
        // reads its civil face + Kind).
        Assert.Equal(new[] { "tzsec_at", "drop" }, resolved.ParamTypeNames);
        Assert.False(resolved.PrependZeroArg);
        Assert.Equal("ts", resolved.ResultKind);
        Assert.Equal("System.TimeSpan", resolved.ReturnTypeName); // chains (.TotalMinutes) resolve
    }

    [Fact]
    public void Resolve_TimeZoneInfo_GetUtcOffset_UtcReceiver_PrependsZeroArg()
    {
        // The Utc singleton re-resolves with the Utc tag: ZERO offset,
        // never the machine offset -- icall_timespan_from_ticks(0).
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "get_UtcOffset", Sig(0x0A, 1, 0x0A)), "get_UtcOffset",
            UtcTag);

        Assert.Equal("il2cpp::runtime::icall_timespan_from_ticks", resolved.CppName);
        Assert.Equal(new[] { "drop", "drop" }, resolved.ParamTypeNames);
        Assert.True(resolved.PrependZeroArg); // the injected 0 (the zero-offset contract)
        Assert.Equal("ts", resolved.ResultKind);
        Assert.Equal("System.TimeSpan", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_TimeZone_GetUtcOffset_OwnerTimeZone_MapsToLegacyKindAwareIcall()
    {
        // The net48 TimeZone.GetUtcOffset(DateTime) old API is the
        // TimeZoneInfoWrapper: KIND-aware -- the "tzsec_legacy" runtime arm
        // zeros a Kind==Utc input, otherwise the per-date offset (probe-
        // pinned: TimeZone.CurrentTimeZone.GetUtcOffset(utcKind) = 0 on
        // net48 while the current-instant shape returned the machine
        // offset -- the GN-20 per-date fix).
        var resolved = ResolveMember(Synthesize("System", "TimeZone", "GetUtcOffset", Sig(0x0A, 1, 0x0A)), "GetUtcOffset",
            LocalTag);

        Assert.Equal("il2cpp::runtime::icall_timespan_from_ticks", resolved.CppName);
        Assert.Equal(new[] { "tzsec_legacy", "drop" }, resolved.ParamTypeNames);
        Assert.Equal("System.TimeSpan", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_TimeZoneInfo_GetUtcOffset_NonArity1Overload_IsLoud()
    {
        // The DateTimeOffset overload shares the 0x11 arity-1 shape -- the
        // arity guard is the wire's only defense (documented in the header);
        // a 2-arg form must never resolve silently.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "TimeZoneInfo", "get_UtcOffset", Sig(0x0A, 2, 0x0A, 0x0A)), "get_UtcOffset"));

        Assert.Contains("not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // The DateTime converters (current-offset application)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_TimeZoneInfo_ToLocalTime_MapsToLocalConverter()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "ToLocalTime", Sig(0x0A, 1, 0x0A)), "ToLocalTime");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_to_local", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "drop", "" }, resolved.ParamTypeNames); // receiver dropped, dt passes by value
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal("System.DateTime", resolved.ReturnTypeName); // chained .Year etc. resolve
    }

    [Fact]
    public void Resolve_TimeZoneInfo_ToUniversalTime_MapsToUtcConverter()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "ToUniversalTime", Sig(0x0A, 1, 0x0A)), "ToUniversalTime");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_from_local", resolved.CppName);
        Assert.Equal(new[] { "drop", "" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal("System.DateTime", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_TimeZoneInfo_IsDaylightSavingTime_MapsToPerDateProbe()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "IsDaylightSavingTime", Sig(0x01, 1, 0x0A)), "IsDaylightSavingTime");

        Assert.Equal("il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at", resolved.CppName);
        Assert.True(resolved.IsInstance);
        // per-date probe: receiver dropped, the DateTime passes by value
        Assert.Equal(new[] { "drop", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_TimeZoneInfo_IsInvalidTime_MapsToPerDateInvalidProbe()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "IsInvalidTime", Sig(0x01, 1, 0x0A)), "IsInvalidTime");

        Assert.Equal("il2cpp::runtime::il2cpp_timezone_get_is_invalid_time", resolved.CppName);
        Assert.True(resolved.IsInstance);
        // per-date invalid-hour probe: receiver dropped, the DateTime passes
        // by value (net48 IsInvalidTime(DateTime) -- the spring-forward gap).
        Assert.Equal(new[] { "drop", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // LOUD rejections -- the name/registry/zone-table surface (timezone.h
    // ships only the offset/conversion/daylight icalls)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_Id")]
    [InlineData("get_DisplayName")]
    [InlineData("get_StandardName")]
    [InlineData("get_DaylightName")]
    [InlineData("get_BaseUtcOffset")]
    [InlineData("get_SupportsDaylightSavingTime")]
    [InlineData("FindSystemTimeZoneById")]
    [InlineData("GetSystemTimeZones")]
    [InlineData("CreateCustomTimeZone")]
    [InlineData("ClearCachedData")]
    [InlineData("ConvertTime")]
    [InlineData("ConvertTimeBySystemTimeZoneId")]
    [InlineData("GetAmbiguousTimeOffsets")]
    [InlineData("GetDaylightChanges")]
    public void Resolve_TimeZone_RegistrySurface_IsLoud(string member)
    {
        var owner = member == "GetDaylightChanges" ? "System.TimeZone" : "System.TimeZoneInfo";
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", owner[7..], member, Sig(0x1C, 0)), member));

        Assert.Contains("not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss within the domain -- falls back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_TimeZoneInfo_UnwiredMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeZoneInfo", "Frobnicate", Sig(0x1C, 0)), "Frobnicate");

        Assert.StartsWith("System_TimeZoneInfo_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same harness as
    // MethodMapTests.Stopwatch)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null);
    }

    // Signature blob: header 0x00 (DEFAULT), param COUNT, return byte, then
    // the raw param bytes RawSignature decodes.
    private static byte[] Sig(byte ret, int paramCount, params byte[] rawParamBytes)
    {
        var bytes = new byte[3 + rawParamBytes.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)paramCount;
        bytes[2] = ret;
        Array.Copy(rawParamBytes, 0, bytes, 3, rawParamBytes.Length);
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
