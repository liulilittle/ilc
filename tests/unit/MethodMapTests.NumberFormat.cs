using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.NumberFormat dispatch tests: prove the NumberFormatInfo/TextInfo
// mappings (MethodMap.NumberFormat.cs) resolve through the BCL domain dispatch
// (MethodRefResolver.cs:389-390 -- the ONLY owners routed to TryMapNumberFormat).
//
// Semantics under test (invariant-culture baseline, numberformat.h):
//   * NumberFormatInfo static culture factories fold to null (::null::);
//   * the separator/sign/percent getters fold to invariant literal strings
//     (::lit:: -- '.' decimal, ',' thousands, '%' percent, '+'/'-' signs);
//   * the special spellings pinned by numberformat.h (NaN/Infinity/-Infinity)
//     fold to their ASCII literal strings;
//   * TextInfo.ToLower/ToUpper reuse the string case icalls with the TextInfo
//     receiver dropped (cast slot 0 = "drop");
//   * get_CurrencySymbol (D-CUL-1) maps DIRECTLY to the runtime icall
//     il2cpp_number_format_currency_symbol (the zh-CN U+00A5 '¥' symbol --
//     the same spelling formatext.cpp's C format emits) with the null-folded
//     receiver dropped (cast slot 0 = "drop").
public class MethodMapTestsNumberFormat
{
    //--------------------------------------------------------------------------
    // NumberFormatInfo static culture factories -- fold to null (the runtime
    // has no culture object; consumers drop it or fold the getters).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_NumberFormatInfo_CurrentInfo_FoldsToNull()
    {
        var resolved = ResolveMember(Synthesize("System.Globalization", "NumberFormatInfo", "get_CurrentInfo", Sig(0x1C)),
            "get_CurrentInfo");

        Assert.Equal("il2cpp::runtime::il2cpp_culture_get_current", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_NumberFormatInfo_InvariantInfo_FoldsToNull()
    {
        var resolved = ResolveMember(Synthesize("System.Globalization", "NumberFormatInfo", "get_InvariantInfo", Sig(0x1C)),
            "get_InvariantInfo");

        Assert.Equal("il2cpp::runtime::il2cpp_culture_get_invariant", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // NumberFormatInfo invariant-literal getters -- the fold pushes the fixed
    // literal string (numberformat.h invariant baseline) instead of a call.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_NumberDecimalSeparator", ".")]
    [InlineData("get_NumberGroupSeparator", ",")]
    [InlineData("get_PercentSymbol", "%")]
    [InlineData("get_PercentDecimalSeparator", ".")]
    [InlineData("get_PercentGroupSeparator", ",")]
    [InlineData("get_PositiveSign", "+")]
    [InlineData("get_NegativeSign", "-")]
    [InlineData("get_NaNSymbol", "NaN")]
    public void Resolve_NumberFormatInfo_SeparatorGetters_FoldToInvariantLiteral(string member, string expected)
    {
        var resolved = ResolveMember(Synthesize("System.Globalization", "NumberFormatInfo", member, Sig(0x0E)), member);

        Assert.Equal(ResolvedMethod.LiteralMarker + expected, resolved.CppName);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // D-CUL-1 (culturefmt, 2026-08-12): get_CurrencySymbol is wired DIRECTLY to
    // the runtime icall il2cpp_number_format_currency_symbol -- the zh-CN U+00A5
    // '¥' symbol, the SAME spelling formatext.cpp's C format emits (so the
    // getter and ToString("C") agree byte-for-byte). The null-folded receiver
    // (get_CurrentInfo/get_InvariantInfo) is dropped by cast slot 0 ("drop"),
    // the TextInfo.ToLower/ToUpper precedent.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_NumberFormatInfo_CurrencySymbol_MapsToRuntimeIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Globalization", "NumberFormatInfo", "get_CurrencySymbol", Sig(0x0E)),
            "get_CurrencySymbol");

        Assert.Equal("il2cpp::runtime::il2cpp_number_format_currency_symbol", resolved.CppName);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]); // culture-name receiver
    }

    [Theory]
    [InlineData("get_PositiveInfinitySymbol", "il2cpp::runtime::il2cpp_number_format_positive_infinity_symbol")]
    [InlineData("get_NegativeInfinitySymbol", "il2cpp::runtime::il2cpp_number_format_negative_infinity_symbol")]
    public void Resolve_NumberFormatInfo_InfinitySymbol_MapsToRuntimeIcall(string member, string expected)
    {
        var resolved = ResolveMember(Synthesize("System.Globalization", "NumberFormatInfo", member, Sig(0x0E)), member);

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // TextInfo -- ToLower/ToUpper drop the TextInfo receiver and reuse the
    // string case icalls (invariant-culture case mapping).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ToLower", "il2cpp::runtime::icall_string_to_lower")]
    [InlineData("ToUpper", "il2cpp::runtime::icall_string_to_upper")]
    public void Resolve_TextInfo_CaseMethods_MapsToStringCaseIcall(string member, string expectedIcall)
    {
        var resolved = ResolveMember(Synthesize("System.Globalization", "TextInfo", member, Sig(0x0E, 0x0E)), member);

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("drop", resolved.ParamTypeNames![0]); // TextInfo receiver dropped
        Assert.Equal("", resolved.ParamTypeNames![1]);     // the string passes through
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
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
