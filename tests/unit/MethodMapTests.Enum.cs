using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Enum dispatch tests: prove the System.Enum domain (MethodMap.Enum.cs
// -> TryMapEnum, reached through the existing `case "System.Enum"` line in
// BclResolveCore) maps GetName / GetUnderlyingType to the enumext.h runtime
// primitives, maps the parameterless instance ToString to il2cpp_enum_to_string
// with the null-descriptor interim (PrependZeroArg), maps Parse to the
// value-returning boxed primitive (enumface D-STREN-2), wires GetNames /
// HasFlag (the name-string ABI), LOUDLY rejects TryParse (the runtime icall
// is TryParse-shaped with an int64_t* out result -- no ref/out support in
// the M-stage transpiler) and the known-but-unwireable members
// (Format/IsDefined/GetValues/ToObject/CompareTo/ToString(string)), and lets
// unknown members fall back to the generated-name path.
//
// PE construction follows MethodRefResolverDispatchTests.cs: a synthetic
// ECMA-335 assembly whose only MemberReference is the member under test.
// Signature blobs are built with Sig(ret, params...) -- the raw bytes
// RawSignature consumes (0x08=i4, 0x0E=string, 0x12=class/Type, 0x1C=object).
// NOTE: 0x12 CLASS params REQUIRE a TypeDefOrRef coded-index byte after them
// (the decoders consume it with ReadTypeHandle) -- Synthesize injects the
// System.Type coded index after every 0x12 automatically.
public class MethodMapEnumTests
{
    //--------------------------------------------------------------------------
    // GetName(Type, object) -- both arguments pass through uncast (name-string
    // ABI, enumface D-STREN-2, 2026-08-12): the folded typeof FULL-NAME
    // STRING + the BOXED VALUE (the enum box carries its own klass -- the
    // int64-carrier "unbox:l" cast would fail the Int64-box klass check).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_GetName_MapsToGetNameIcall()
    {
        var resolved = ResolveMember("GetName", Sig(0x0E, 0x12, 0x1C));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_get_name_by_type_name", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // GetUnderlyingType(Type) -- the underlying primitive's name string.
    // raudit4 (2026-09-13): the mapping routes the folded typeof(T) FULL-NAME
    // STRING to il2cpp_enum_get_underlying_type_by_type_name (the runtime
    // resolves the registered enum metadata by name and returns "System.Int64"
    // for a long-backed enum). The name string is the ONE runtime argument --
    // no dropped arg, no injected null descriptor (VERIFY-FIX: prependZero is
    // false).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_GetUnderlyingType_MapsToUnderlyingIcall()
    {
        var resolved = ResolveMember("GetUnderlyingType", Sig(0x0E, 0x12));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_get_underlying_type_by_type_name", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
        Assert.False(resolved.PrependZeroArg);
    }

    //--------------------------------------------------------------------------
    // Parse(Type, string) -- WIRED (enumface, D-STREN-2, 2026-08-12): the
    // TryParse-shaped runtime icall (il2cpp_enum_parse) has an int64_t* out
    // result (no ref/out in the M-stage transpiler), so Parse maps to the
    // VALUE-RETURNING boxed variant (il2cpp_enum_parse_boxed): both args are
    // managed refs (folded type-name string + text string), the result is a
    // boxed enum object, ArgumentException on failure. TryParse stays LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_Parse_MapsToParseBoxed()
    {
        var resolved = ResolveMember("Parse", Sig(0x1C, 0x12, 0x0E));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_parse_boxed", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Parse(Type, string, bool ignoreCase) -- WIRED (batch 148): the
    // il2cpp_enum_parse_boxed_ic primitive does ASCII case-insensitive NAME
    // matching; the bool rides the "b" shape.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_Parse_ThreeArg_MapsToParseBoxedIc()
    {
        var resolved = ResolveMember("Parse", Sig(0x1C, 0x12, 0x0E, 0x02));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_parse_boxed_ic", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "b" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // GetNames(Type) -- WIRED (enumface): the name-string primitive returns
    // a string[] (Il2CppArray*) of the member names in declaration order.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_GetNames_MapsToGetNamesIcall()
    {
        var resolved = ResolveMember("GetNames", Sig(0x0E, 0x12));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_get_names_by_type_name", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // HasFlag(Enum) -- WIRED (enumface): the boxed-receiver flags bit test.
    // Receiver and argument are both boxed enum references; the bool result
    // rides the int32 slot.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_HasFlag_MapsToHasFlagIcall()
    {
        var resolved = ResolveMember("HasFlag", Sig(0x02, 0x1C));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_has_flag", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // instance string ToString() -- the System.Enum-owner shape (Enum-typed
    // references). The BOX path (batch 148) maps to
    // il2cpp_enum_boxed_to_string -- the klass identity rides on the box
    // itself (the old "unbox:l" shape hit icall_unbox_long, which rejected
    // enum boxes). The ADDRESS path (ldloca + constrained, StackFold fold)
    // keeps il2cpp_enum_to_string with PrependTypeClass.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_ToString_MapsToBoxedToStringIcall()
    {
        var resolved = ResolveMember("ToString", Sig(0x0E));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_boxed_to_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "boxed" }, resolved.ParamTypeNames);
        Assert.False(resolved.PrependZeroArg);
    }

    //--------------------------------------------------------------------------
    // ToString(string format) -- WIRED (batch 148): the boxed-format
    // primitive (D/G/F/X, case-insensitive; null/empty -> "G").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_ToStringWithFormat_MapsToBoxedFormatIcall()
    {
        var resolved = ResolveMember("ToString", Sig(0x0E, 0x0E));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_boxed_to_string_format", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "boxed", "" }, resolved.ParamTypeNames);
        Assert.False(resolved.PrependZeroArg);
    }

    //--------------------------------------------------------------------------
    // ToString(IFormatProvider) / ToString(string, IFormatProvider) -- the
    // net48 provider overloads (enum.cs:871 / :808). The provider is IGNORED
    // by net48 (the plain ToString / ToString(format) text is returned), so
    // the provider slot is DROPPED at emission.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_ToStringWithProvider_MapsToBoxedToStringDroppingProvider()
    {
        var resolved = ResolveMember("ToString", Sig(0x0E, 0x12));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_boxed_to_string", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "boxed", "drop" }, resolved.ParamTypeNames);
        Assert.False(resolved.PrependZeroArg);
    }

    [Fact]
    public void Resolve_Enum_ToStringWithFormatProvider_MapsToBoxedFormatDroppingProvider()
    {
        var resolved = ResolveMember("ToString", Sig(0x0E, 0x0E, 0x12));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_boxed_to_string_format", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "boxed", "", "drop" }, resolved.ParamTypeNames);
        Assert.False(resolved.PrependZeroArg);
    }

    [Fact]
    public void Resolve_Enum_ToStringBadArity_ThrowsWhitelistLoud()
    {
        // An arity outside the four net48 ToString forms -> LOUD whitelist.
        var ex = Assert.Throws<NotSupportedException>(
            () => ResolveMember("ToString", Sig(0x0E, 0x0E, 0x0E, 0x0E)));

        Assert.Contains("System.Enum::ToString", ex.Message);
        Assert.Contains("LOUD whitelist", ex.Message);
        Assert.Contains("enum.cs", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Equals(object) -- ICALL-C (2026-08-07): the boxed-enum equality icall
    // (il2cpp_enum_equals, enumext.h:200). bool result rides the int32 slot
    // (rv=true); the receiver and the argument both pass through uncast (the
    // boxed-enum-reference forward ABI -- the enum VALUE ldloca+constrained
    // shape stays unreachable until the enum box helper lands).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_Equals_MapsToEqualsIcall()
    {
        var resolved = ResolveMember("Equals", Sig(0x02, 0x1C));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_equals", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // bool TryParse<T>(string, out T) -- WIRED (batch 148e) when the call
    // site carries a MethodSpec with a resolvable VALUETYPE generic-argument
    // name (the e2e enumparseic pins the full path). WITHOUT the MethodSpec
    // the resolver cannot inject the klass and stays LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_TryParse_WithoutGenericArgs_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember("TryParse", Sig(0x02, 0x12, 0x12)));

        Assert.Contains("TryParse", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Array GetValues(Type) -- WIRED (batch 148d): the int32[] member values
    // (return type name steers array consumption).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_GetValues_MapsToGetValuesIcall()
    {
        var resolved = ResolveMember("GetValues", Sig(0x1C));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_get_values_by_type_name", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // object ToObject(Type, object) -- WIRED (batch 148d): the boxed enum
    // (int64 carrier value argument).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_ToObject_MapsToToObjectIcall()
    {
        var resolved = ResolveMember("ToObject", Sig(0x1C, 0x1C));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_to_object", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // int CompareTo(object) -- WIRED (batch 148d): the constrained receiver
    // arrives as its int64 carrier ("enumval"); argument is a boxed enum.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_CompareTo_MapsToCompareToIcall()
    {
        var resolved = ResolveMember("CompareTo", Sig(0x1C));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_compare_to", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "enumval", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // bool IsDefined(Type, object) -- WIRED (batch 148): the name/numeric
    // membership query (bool rides the int32 slot).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_IsDefined_MapsToIsDefinedIcall()
    {
        var resolved = ResolveMember("IsDefined", Sig(0x1C, 0x1C));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_is_defined", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // string Format(Type, object, string) -- WIRED (batch 148): the static
    // D/G/F/X formatter (net48 rejects a member-NAME string value).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_Format_MapsToFormatIcall()
    {
        var resolved = ResolveMember("Format", Sig(0x1C, 0x1C, 0x0E));

        Assert.Equal("il2cpp::runtime::il2cpp_enum_format", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Unknown member -> the generated-name path (dispatch miss).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Enum_UnknownMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember("Future", Sig(0x08, 0x08));

        Assert.StartsWith("System_Enum_Future_", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodRefResolverDispatchTests.cs; the helpers there are private).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(string memberName, byte[] signature)
    {
        using var reader = new PEReader(Synthesize("System", "Enum", memberName, signature));
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
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
        var sysTypeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Type"));
        // TypeDefOrRef coded index for the System.Type TypeRef: tag 0x02,
        // row << 2 | 2. Injected after every 0x12 CLASS param byte -- the
        // decoders (RawSignature / MethodSignatureCoder) consume it with
        // ReadTypeHandle, so a bare 0x12 would read the NEXT param as the
        // handle and go out of range.
        var typeCoded = (byte)((MetadataTokens.GetRowNumber(sysTypeRef) << 2) | 1);
        var fixedSig = signature.SelectMany(b => b == 0x12
            ? new[] { b, typeCoded }
            : new[] { b }).ToArray();
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(fixedSig));
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

//------------------------------------------------------------------------------
// Enum REGISTER wiring tests (timecase work order 3, MethodMap.Enum.cs a-e):
// prove the transpiler-side register-emission helpers produce the
// static-init shape -- caller-owned statics (names/values/Il2CppEnumInfo)
// + the il2cpp_enum_register call -- for BCL enums (the transpiler-side
// table; the runtime ALSO ships DayOfWeek built-in, enumext.cpp, so the
// two sources must agree) and user enum TypeDefs (literal static fields).
// The helpers live in MethodRefResolver's MethodMap.Enum.cs domain.
//------------------------------------------------------------------------------
public class EnumRegisterWiringTests
{
    //--------------------------------------------------------------------------
    // BCL table: System.DayOfWeek -- Sunday=0..Saturday=6, underlying Int32
    // (TypeCode 9). Must agree with the runtime's built-in table (enumext.cpp
    // kDayOfWeekNames/kDayOfWeekValues -- the runtime table is the authority
    // for the null-descriptor fallback; the transpiler table is the authority
    // for the name-keyed register emission).
    //--------------------------------------------------------------------------
    [Fact]
    public void BclTable_DayOfWeek_Sunday0ToSaturday6()
    {
        Assert.True(MethodRefResolver.TryGetBclEnumRegisterInfo("System.DayOfWeek", out var info));
        Assert.Equal("System.DayOfWeek", info.TypeName);
        Assert.Equal(9, info.UnderlyingTypeCode); // kEnumUnderlyingInt32
        Assert.Equal(new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            info.Names);
        Assert.Equal(new long[] { 0, 1, 2, 3, 4, 5, 6 }, info.Values);
        Assert.Equal(7, info.Names.Length);
    }

    [Fact]
    public void BclTable_UnknownEnum_ReturnsFalse()
    {
        Assert.False(MethodRefResolver.TryGetBclEnumRegisterInfo("System.NotAnEnum", out _));
    }

    //--------------------------------------------------------------------------
    // User enum TypeDef: literal static fields (declaration order) become the
    // member table; the underlying TypeCode is the enum's base type's
    // value__ field -- M3 loads value types through the Int32 slot only, so
    // the register info defaults to Int32 (TypeCode 9) for user enums.
    //--------------------------------------------------------------------------
    [Fact]
    public void UserEnum_RegisterInfo_FromLiteralFields()
    {
        var t = new TypeDef
        {
            Name = "MyColor",
            Namespace = "AuditFix",
            BaseTypeName = "System.Enum",
        };
        t.StaticFields.Add(new FieldDef { Name = "Red", Owner = t, IsStatic = true, IsLiteral = true, ConstantValue = "0" });
        t.StaticFields.Add(new FieldDef { Name = "Green", Owner = t, IsStatic = true, IsLiteral = true, ConstantValue = "1" });
        t.StaticFields.Add(new FieldDef { Name = "NonLiteral", Owner = t, IsStatic = true, IsLiteral = false, ConstantValue = "5" });

        Assert.True(MethodRefResolver.TryGetUserEnumRegisterInfo(t, out var info));
        Assert.Equal("AuditFix.MyColor", info.TypeName);
        Assert.Equal(9, info.UnderlyingTypeCode);
        Assert.Equal(new[] { "Red", "Green" }, info.Names); // literals only, declaration order
        Assert.Equal(new long[] { 0, 1 }, info.Values);
    }

    [Fact]
    public void UserEnum_NonEnumType_ReturnsFalse()
    {
        var t = new TypeDef { Name = "Plain", Namespace = "AuditFix" };
        Assert.False(MethodRefResolver.TryGetUserEnumRegisterInfo(t, out _));
    }

    //--------------------------------------------------------------------------
    // Emission: the static-init shape -- caller-owned statics (the registry
    // stores pointers, so the statics must outlive it) + the register call.
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitRegisterBlock_StaticInitShape()
    {
        var block = MethodRefResolver.EmitEnumRegisterBlock("System.DayOfWeek", 9,
            new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            new long[] { 0, 1, 2, 3, 4, 5, 6 }, isFlags: false);

        Assert.Contains("static const char* const s_System_DayOfWeek_names[] = { \"Sunday\", \"Monday\", \"Tuesday\", \"Wednesday\", \"Thursday\", \"Friday\", \"Saturday\" };", block);
        Assert.Contains("static const int64_t s_System_DayOfWeek_values[] = { 0, 1, 2, 3, 4, 5, 6 };", block);
        Assert.Contains("static const il2cpp::runtime::Il2CppEnumInfo s_System_DayOfWeek_enumInfo = { 9, 7, s_System_DayOfWeek_names, s_System_DayOfWeek_values, 0 };", block);
        Assert.Contains("il2cpp::runtime::il2cpp_enum_register(\"System.DayOfWeek\", &s_System_DayOfWeek_enumInfo);", block);
    }

    //--------------------------------------------------------------------------
    // flagsenum: the register block's LAST column is the REAL [Flags]
    // attribute bit (Il2CppEnumInfo::isFlags). The runtime's ToString
    // decomposition reads it -- never a member-shape guess -- so a [Flags]
    // enum with composite members (AB=3) still decomposes.
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitRegisterBlock_FlagsBitIsEmitted()
    {
        var flags = MethodRefResolver.EmitEnumRegisterBlock("Cases.Flags",
            9, new[] { "None", "A", "B", "C", "AB" }, new long[] { 0, 1, 2, 4, 3 },
            isFlags: true);
        Assert.Contains("Il2CppEnumInfo s_Cases_Flags_enumInfo = { 9, 5, s_Cases_Flags_names, s_Cases_Flags_values, 1 };", flags);

        var plain = MethodRefResolver.EmitEnumRegisterBlock("Cases.Plain",
            9, new[] { "Zero", "One", "Two" }, new long[] { 0, 1, 2 }, isFlags: false);
        Assert.Contains("Il2CppEnumInfo s_Cases_Plain_enumInfo = { 9, 3, s_Cases_Plain_names, s_Cases_Plain_values, 0 };", plain);
    }

    [Fact]
    public void EmitRegisterBlock_UserEnumTypeName_Sanitized()
    {
        var block = MethodRefResolver.EmitEnumRegisterBlock("AuditFix.MyColor", 9,
            new[] { "Red", "Green" }, new long[] { 0, 1 }, isFlags: false);

        Assert.Contains("s_AuditFix_MyColor_names", block);
        Assert.Contains("s_AuditFix_MyColor_values", block);
        Assert.Contains("s_AuditFix_MyColor_enumInfo", block);
        Assert.Contains("il2cpp::runtime::il2cpp_enum_register(\"AuditFix.MyColor\", &s_AuditFix_MyColor_enumInfo);", block);
    }

    //--------------------------------------------------------------------------
    // Capacity: kMaxRegisteredEnums = 32 (enumext.cpp) -- the compile-time
    // count must LOUD-reject beyond 32 (item d).
    //--------------------------------------------------------------------------
    [Fact]
    public void Capacity_Within32_Ok()
    {
        MethodRefResolver.EnsureEnumRegisterCapacity(32);
    }

}
