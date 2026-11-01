using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// impl-tz2 (TimeZone/DateTime domain LOUD governance): every
// `throw new NotSupportedException` in the 8 MethodMap files must be either
// (a) truly implemented (compile-time analyzable), or (b) carry the
// self-identifying whitelist message
//     "LOUD whitelist (reason) [Reference Source file:line]"
// for one of the 4 sanctioned categories: machine state/culture, dynamic
// dispatch, no runtime descriptor, net48 missing overload/arity.
//
// Part 1 pins the NEW wirings of the analyzable gaps (MethodMap.DateTimeExtra:
// Add/op_Addition ride the existing il2cpp_datetime_add_ticks with the
// "ts_ticks" TimeSpan cast; TryParse is the no-throw parse twin; Today /
// ToOADate / FromOADate / ToBinary / FromBinary / IsDaylightSavingTime /
// GetTypeCode get dedicated icalls).
// Part 2 pins the whitelist message format on every surviving LOUD throw.
//------------------------------------------------------------------------------
public class MethodMapTestsDateTimeLoud
{
    //--------------------------------------------------------------------------
    // Part 1 -- the analyzable gaps get real wirings (DateTimeExtra domain)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DateTime_AddTimeSpan_MapsToAddTicksIcall()
    {
        // DateTime.Add(TimeSpan) -> DateTime: net48 Add(TimeSpan) IS
        // AddTicks(value._ticks) (datetime.cs:456-461), so the existing
        // il2cpp_datetime_add_ticks entry serves it with the "ts_ticks"
        // TimeSpan cast (the Subtract(TimeSpan) precedent).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeTimeSpanRefs("System", "DateTime", "Add",
                new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x09 }), "Add");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_add_ticks", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt", "ts_ticks" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_OpAddition_MapsToAddTicksIcall()
    {
        // DateTime + TimeSpan (static op_Addition, the only net48 form).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeTimeSpanRefs("System", "DateTime", "op_Addition",
                new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x09 }), "op_Addition");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_add_ticks", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "ts_ticks" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_OpSubtractionTimeSpan_MapsToSubtractTicks()
    {
        // impl-tz2 e2e find (dtextra2): DateTime - TimeSpan (static
        // op_Subtraction) was mapped to the (DateTime, DateTime) -> TimeSpan
        // icall -- the TimeSpan struct landed in an Il2CppDateTime param and
        // the generated C++ failed to compile. The operand-type text
        // disambiguates (the instance Subtract(TimeSpan) precedent) and the
        // result is a DateTime ("dt", datetime.cs:1336-1343).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeTimeSpanRefs("System", "DateTime", "op_Subtraction",
                new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x09 }), "op_Subtraction");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_subtract_ticks", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "ts_ticks" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_TryParse_MapsToTryParseIcall()
    {
        // DateTime.TryParse(string, out DateTime): the BYREF out slot folds
        // to the pointee byte (0x10 0x11 + handle -> ps 0x11, the
        // Int32.TryParse precedent) and rides the "" passthrough (the
        // TryParseExact out-slot precedent).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "TryParse",
                new byte[] { 0x00, 0x02, 0x02, 0x0E, 0x10, 0x11, 0x05 }), "TryParse");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_try_parse", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_TryParseStyles_MapsToStylesIcall()
    {
        // DateTime.TryParse(string, IFormatProvider, DateTimeStyles, out
        // DateTime): the provider is dropped, the styles enum rides "i".
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "TryParse",
                new byte[] { 0x00, 0x04, 0x02, 0x0E, 0x12, 0x05, 0x11, 0x05, 0x10, 0x11, 0x05 }), "TryParse");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_try_parse_styles", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "s", "drop", "i", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_GetToday_MapsToTodayIcall()
    {
        // DateTime.Today == Now.Date (net48 datetime.cs:1262-1268).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "get_Today",
                new byte[] { 0x00, 0x00, 0x11, 0x05 }), "get_Today");

        Assert.Equal("il2cpp::runtime::icall_datetime_today", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.Equal("System.DateTime", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_DateTime_ToOADate_MapsToOaIcall()
    {
        // DateTime.ToOADate() -> double (net48 TicksToOADate, datetime.cs:1346).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "ToOADate",
                new byte[] { 0x00, 0x00, 0x0D }), "ToOADate");

        Assert.Equal("il2cpp::runtime::icall_datetime_to_oa_date", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_FromOADate_MapsToOaIcall()
    {
        // DateTime.FromOADate(double) -> DateTime, Kind=Unspecified
        // (net48 datetime.cs:779-782 via DoubleDateToTicks).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "FromOADate",
                new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0D }), "FromOADate");

        Assert.Equal("il2cpp::runtime::icall_datetime_from_oa_date", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "d" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_ToBinary_MapsToBinaryIcall()
    {
        // DateTime.ToBinary() -> long (net48 datetime.cs:808-831).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "ToBinary",
                new byte[] { 0x00, 0x00, 0x0A }), "ToBinary");

        Assert.Equal("il2cpp::runtime::icall_datetime_to_binary", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
        Assert.Equal("l", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_FromBinary_MapsToBinaryIcall()
    {
        // DateTime.FromBinary(long) -> DateTime (net48 datetime.cs:700-742).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "FromBinary",
                new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0A }), "FromBinary");

        Assert.Equal("il2cpp::runtime::icall_datetime_from_binary", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "l" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_InstanceIsDaylightSavingTime_MapsToKindAwareIcall()
    {
        // DateTime.IsDaylightSavingTime() (net48 datetime.cs:797-802): a
        // Kind=Utc input is false, anything else evaluates the per-date
        // machine probe (timezone.h).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "IsDaylightSavingTime",
                new byte[] { 0x00, 0x00, 0x02 }), "IsDaylightSavingTime");

        Assert.Equal("il2cpp::runtime::icall_datetime_is_daylight_saving", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_GetTypeCode_MapsToConstantIcall()
    {
        // DateTime.GetTypeCode() -> TypeCode.DateTime == 16
        // (referencesource mscorlib/system/typecode.cs:44).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "GetTypeCode",
                new byte[] { 0x00, 0x00, 0x08 }), "GetTypeCode");

        Assert.Equal("il2cpp::runtime::icall_datetime_get_type_code", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Part 2 -- whitelist message format on every surviving LOUD throw
    //--------------------------------------------------------------------------
    private static void AssertLoudWhitelist(Action action, string reasonFragment)
    {
        var ex = Assert.Throws<NotSupportedException>(action);
        Assert.Contains("LOUD whitelist", ex.Message);
        Assert.Contains(reasonFragment, ex.Message);
        Assert.Contains("[Reference Source", ex.Message);
    }

    // --- MethodMap.DateTime.cs ---
    [Fact]
    public void Loud_DateTime_CtorCalendar_DynamicDispatch()
    {
        // ctor(y, m, d, Calendar): the Calendar argument is virtually
        // dispatched (GetYear/GetMonth/... on the concrete calendar).
        AssertLoudWhitelist(() => ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", ".ctor",
                new byte[] { 0x00, 0x04, 0x11, 0x05, 0x08, 0x08, 0x08, 0x12, 0x05 }), ".ctor"),
            "dynamic dispatch");
    }

    [Fact]
    public void Loud_DateTime_SubtractArity_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            SynthesizeWithDateTimeTimeSpanRefs("System", "DateTime", "Subtract",
                new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "Subtract"),
            "net48");
    }

    [Fact]
    public void Loud_DateTime_ToStringChar_Net48Arity()
    {
        // ToString(char): no net48 overload takes a single char.
        AssertLoudWhitelist(() => ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "ToString",
                new byte[] { 0x00, 0x01, 0x0E, 0x03 }), "ToString"),
            "net48");
    }

    [Fact]
    public void Loud_DateTime_ToStringThreeArgs_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "ToString",
                new byte[] { 0x00, 0x03, 0x0E, 0x0E, 0x0E, 0x12, 0x05 }), "ToString"),
            "net48");
    }

    // --- MethodMap.TimeSpan.cs ---
    [Fact]
    public void Loud_TimeSpan_CtorArity_Net48Arity()
    {
        // TimeSpan ctor arities are exactly {1, 3, 4, 5} in net48.
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeSpan", ".ctor",
                new byte[] { 0x00, 0x02, 0x00, 0x08, 0x08 }), ".ctor"),
            "net48");
    }

    // --- MethodMap.TimeExt2.cs ---
    [Fact]
    public void Loud_DateTime_ParseZeroArgs_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "DateTime", "Parse", Sig(0x0A)), "Parse"),
            "net48");
    }

    [Fact]
    public void Loud_DateTime_ParseExactOneArg_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "DateTime", "ParseExact", Sig(0x0A, 0x0E)), "ParseExact"),
            "net48");
    }

    [Fact]
    public void Loud_DateTime_TryParseExactFourArgs_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "DateTime", "TryParseExact",
                new byte[] { 0x00, 0x04, 0x02, 0x0E, 0x0E, 0x12, 0x05, 0x11, 0x05 }), "TryParseExact"),
            "net48");
    }

    // --- MethodMap.TimeZone.cs (arity guards) ---
    [Fact]
    public void Loud_TimeZoneInfo_GetUtcOffsetArity_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", "get_UtcOffset", Sig(0x0A, 0x0A, 0x0A)), "get_UtcOffset"),
            "net48");
    }

    [Fact]
    public void Loud_TimeZoneInfo_ToLocalTimeArity_Net48Arity()
    {
        // net48 has NO TimeZoneInfo.ToLocalTime member at all (only the
        // obsolete TimeZone.ToLocalTime(DateTime), timezone.cs:112).
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", "ToLocalTime",
                new byte[] { 0x00, 0x02, 0x0A, 0x11, 0x05, 0x02 }), "ToLocalTime"),
            "net48");
    }

    [Fact]
    public void Loud_TimeZoneInfo_ToUniversalTimeArity_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", "ToUniversalTime",
                new byte[] { 0x00, 0x02, 0x0A, 0x11, 0x05, 0x02 }), "ToUniversalTime"),
            "net48");
    }

    [Fact]
    public void Loud_TimeZoneInfo_IsDaylightSavingTimeArity_Net48Arity()
    {
        // TimeZoneInfo owner: net48 exposes only the 1-arg DateTime /
        // DateTimeOffset forms (timezoneinfo.cs:542,549).
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", "IsDaylightSavingTime",
                new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x12, 0x05 }), "IsDaylightSavingTime"),
            "net48");
    }

    [Fact]
    public void Loud_TimeZone_IsDaylightSavingTimeDaylightTime_NoRuntimeDescriptor()
    {
        // The obsolete TimeZone.IsDaylightSavingTime(DateTime, DaylightTime)
        // (timezone.cs:132) takes a DaylightTime value that GetDaylightChanges
        // would produce -- no runtime descriptor exists for it.
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZone", "IsDaylightSavingTime",
                new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x12, 0x05 }), "IsDaylightSavingTime"),
            "runtime descriptor");
    }

    [Fact]
    public void Loud_TimeZoneInfo_IsInvalidTimeArity_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", "IsInvalidTime",
                new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x02 }), "IsInvalidTime"),
            "net48");
    }

    [Fact]
    public void Loud_TimeZoneInfo_FindByIdArity_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", "FindSystemTimeZoneById", Sig(0x1C, 0x0E, 0x0E)), "FindSystemTimeZoneById"),
            "net48");
    }

    [Fact]
    public void Loud_TimeZoneInfo_ConvertFromUtcArity_Net48Arity()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", "ConvertTimeFromUtc",
                new byte[] { 0x00, 0x03, 0x0A, 0x11, 0x05, 0x1C, 0x1C }), "ConvertTimeFromUtc"),
            "net48");
    }

    // --- MethodMap.TimeZone.cs (registry/object surface) ---
    [Theory]
    [InlineData("get_Id")]
    [InlineData("get_DisplayName")]
    [InlineData("get_StandardName")]
    [InlineData("get_DaylightName")]
    [InlineData("get_BaseUtcOffset")]
    [InlineData("get_SupportsDaylightSavingTime")]
    [InlineData("GetSystemTimeZones")]
    [InlineData("ClearCachedData")]
    [InlineData("ConvertTimeBySystemTimeZoneId")]
    [InlineData("GetAmbiguousTimeOffsets")]
    public void Loud_TimeZoneInfo_RegistrySurface_MachineState(string member)
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", member, Sig(0x1C)), member),
            "machine");
    }

    [Theory]
    [InlineData("CreateCustomTimeZone")]
    [InlineData("ConvertTime")]
    public void Loud_TimeZoneInfo_ObjectSurface_NoRuntimeDescriptor(string member)
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZoneInfo", member, Sig(0x1C)), member),
            "runtime descriptor");
    }

    [Fact]
    public void Loud_TimeZone_GetDaylightChanges_NoRuntimeDescriptor()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "TimeZone", "GetDaylightChanges", Sig(0x1C, 0x08)), "GetDaylightChanges"),
            "runtime descriptor");
    }

    // --- MethodMap.Legacy.cs (no runtime object model) ---
    // p0c D31-D35 wired the five public-face state accessors, so the family
    // LOUD sample is a member that still needs the missing object model.
    [Fact]
    public void Loud_RemotingFamily_NoRuntimeDescriptor()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System.Runtime.Remoting", "RemotingServices", "CreateTransparentProxy",
                new byte[] { 0x00, 0x01, 0x02, 0x1C }), "CreateTransparentProxy"),
            "remoting object model");
    }

    [Fact]
    public void Loud_AppDomain_UnclosedMember_NoRuntimeDescriptor()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "AppDomain", "CreateDomain", Sig(0x1C, 0x0E)), "CreateDomain"),
            "runtime descriptor");
    }

    [Fact]
    public void Loud_AppDomainSetup_UnclosedMember_NoRuntimeDescriptor()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "AppDomainSetup", "get_ApplicationName", Sig(0x0E)), "get_ApplicationName"),
            "runtime descriptor");
    }

    [Fact]
    public void Loud_ObjectHandle_UnclosedMember_NoRuntimeDescriptor()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System.Runtime.Remoting", "ObjectHandle", "GetLifetimeService", Sig(0x1C)), "GetLifetimeService"),
            "runtime descriptor");
    }

    [Fact]
    public void Loud_ArgIterator_NoRuntimeDescriptor()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System", "ArgIterator", ".ctor", Sig(0x01, 0x1C)), ".ctor"),
            "runtime descriptor");
    }

    // p0c D37 wired the lock object model; the LockCookie members are the
    // remaining LOUD strain.
    [Fact]
    public void Loud_ReaderWriterLock_NoRuntimeDescriptor()
    {
        AssertLoudWhitelist(() => ResolveMember(
            Synthesize("System.Threading", "ReaderWriterLock", "DowngradeFromWriterLock", Sig(0x00, 0x1C)),
            "DowngradeFromWriterLock"),
            "LockCookie");
    }

    // --- MethodMap.Dtoffset.cs ---
    // The DateTimeOffset Equals(object) / CompareTo(object) LOUD rows that used
    // to live here are GONE: both overloads are wired now, so asserting a throw
    // would assert the defect. Their coverage moved to
    // MethodMapTests.Dtoffset.cs (the mapping pin) and to the
    // tests/cases/dtoobjcmp1 oracle (the net48 behaviour).

    // --- MethodMap.DateTimeExtra.cs (culture-dependent + arity) ---
    [Theory]
    [InlineData("GetDateTimeFormats")]
    [InlineData("ToShortDateString")]
    [InlineData("ToLongDateString")]
    [InlineData("ToShortTimeString")]
    [InlineData("ToLongTimeString")]
    public void Loud_DateTime_CultureSurface_MachineState(string member)
    {
        AssertLoudWhitelist(() => ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", member, Sig(0x0E)), member),
            "culture");
    }

    [Fact]
    public void Loud_DateTime_ParseFourArgs_Net48Arity()
    {
        // A 4-arg Parse shape cannot come from net48 (the 3-arg styles form
        // is the largest); it reaches the Extra domain's arity guard.
        AssertLoudWhitelist(() => ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "Parse",
                new byte[] { 0x00, 0x04, 0x0A, 0x0E, 0x12, 0x05, 0x11, 0x05, 0x11, 0x05 }), "Parse"),
            "net48");
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same harness as
    // MethodMapTests.DateTime / MethodMapTests.TimeExt2)
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

    private static MemoryStream SynthesizeWithDateTimeRef(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        // DateTime TypeRef FIRST (row 1 -> coded 0x05, matching the blob).
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static MemoryStream SynthesizeWithDateTimeTimeSpanRefs(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        // Row 1 = DateTime (coded 0x05), row 2 = TimeSpan (coded 0x09).
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("TimeSpan"));
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
