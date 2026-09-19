using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Base64 dispatch tests: prove the System.Convert Base64 wiring
// (MethodMap.Base64.cs -> TryMapBase64) routes the two runtime icalls
// (il2cpp_convertbase64.h), rejects the char[]-array variants and the
// offset/options overloads LOUDLY, and lets unknown members fall through to
// the generated-name path. Also covers the ConvertExtra surface
// (MethodMap.ConvertExtra.cs -> TryMapConvertExtra): integer-source CHECKED
// narrowings wire to the conv.ovf family (batch 152e/154b) -- while the
// float/double sources (net48 rounds half-to-even; existing helpers
// truncate) and ToDecimal (no decimal-target icall at all) stay LOUD --
// never silently mapped to a wrong-semantics helper.
//
// Same synthesis approach as MethodMapTests.Buffer: a real ECMA-335 PE
// holding exactly one MemberReference (System.Convert::<member>) with a
// hand-built signature blob. byte[] params are 0x1D SZARRAY + 0x05 U1
// element byte (consumed by RawSignature); string is 0x0E; int is 0x08.
public class MethodMapBase64Tests
{
    //--------------------------------------------------------------------------
    // ToBase64String(byte[]) -> string: the byte[] slot is uncased
    // (Il2CppObject*), the result is a managed reference.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_ToBase64String_MapsToBase64EncodeIcall()
    {
        // string ToBase64String(byte[]): 0x0E ret, 0x1D 0x05 param (SZARRAY U1).
        var resolved = ResolveConvert("ToBase64String",
            new byte[] { 0x00, 0x01, 0x0E, 0x1D, 0x05 });

        Assert.Equal("il2cpp::runtime::il2cpp_convert_to_base64_string", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        // The byte[] slot carries the "a" cast (Il2CppObject* -> Il2CppArray*:
        // the runtime icall declares the concrete array type -- MethodMap.cs
        // conventions; Il2CppArray embeds Il2CppObject first, exact-address
        // reinterpret like the "s" string cast).
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { false }, resolved.ParamIsRef);
        Assert.Null(resolved.ResultKind); // ref results carry no result kind
    }

    //--------------------------------------------------------------------------
    // FromBase64String(string) -> byte[]: the string param carries the "s"
    // cast (Il2CppString*, the existing Convert.cs string-icall pattern), the
    // result is a managed reference (byte[]).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_FromBase64String_MapsToBase64DecodeIcall()
    {
        // byte[] FromBase64String(string): 0x1D 0x05 ret (SZARRAY U1), 0x0E param.
        var resolved = ResolveConvert("FromBase64String",
            new byte[] { 0x00, 0x01, 0x1D, 0x05, 0x0E });

        Assert.Equal("il2cpp::runtime::il2cpp_convert_from_base64_string", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { false }, resolved.ParamIsRef);
        Assert.Null(resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToBase64String(byte[], Base64FormattingOptions) -- the options overload
    // has no runtime helper: LOUD, never a silently-wrong emission.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_ToBase64StringOptionsOverload_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveConvert("ToBase64String",
                new byte[] { 0x00, 0x02, 0x0E, 0x1D, 0x05, 0x08 }));

        Assert.Contains("System.Convert::ToBase64String", ex.Message);
        Assert.Contains("overload", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ToBase64CharArray / FromBase64CharArray -- the char[]-buffer forms of
    // the same Base64 domain, no runtime helper: LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_ToBase64CharArray_ThrowsLoud()
    {
        // int ToBase64CharArray(byte[], int, int, char[], int): 0x08 ret.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveConvert("ToBase64CharArray",
                new byte[] { 0x00, 0x05, 0x08, 0x1D, 0x05, 0x08, 0x08, 0x1D, 0x03, 0x08 }));

        Assert.Contains("System.Convert::ToBase64CharArray", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ConvertExtra: ToByte(int) -- the CHECKED narrowing (0..255) wires to
    // the conv.ovf narrow family (il2cpp_conv.h batch 152e/154b): the int32
    // slot sign-extends ("l") into the checked int64 range test -- exact
    // net48 semantics (a value negative under extension is precisely the
    // OverflowException net48 throws).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_ToByte_IntSource_MapsToCheckedNarrowIcall()
    {
        var resolved = ResolveConvert("ToByte", new byte[] { 0x00, 0x01, 0x05, 0x08 });

        Assert.Equal("il2cpp::runtime::icall_conv_ovf_u1_i8", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "l" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { false }, resolved.ParamIsRef);
        Assert.Null(resolved.ResultKind); // int32-carried result
    }

    //--------------------------------------------------------------------------
    // The rest of the checked-narrowing family with an int source: all WIRE
    // to the checked conv family now. u4/u8 targets take the int32 slot
    // directly ("i"); ToUInt64's result rides an int64 slot ("l"), ToSingle's
    // a double slot ("d" -- the MathF float-return precedent).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ToSByte", "icall_conv_ovf_i1_i8", "l", null)]
    [InlineData("ToInt16", "icall_conv_ovf_i2_i8", "l", null)]
    [InlineData("ToUInt16", "icall_conv_ovf_u2_i8", "l", null)]
    [InlineData("ToUInt32", "icall_conv_ovf_u4_i4", "i", null)]
    [InlineData("ToUInt64", "icall_conv_ovf_u8_i4", "i", "l")]
    [InlineData("ToSingle", "icall_conv_r4_i4", "i", "d")]
    public void Resolve_Convert_NarrowingFamily_IntSource_Wires(
        string member, string icall, string cast, string? resultKind)
    {
        var resolved = ResolveConvert(member, new byte[] { 0x00, 0x01, 0x05, 0x08 });

        Assert.Equal($"il2cpp::runtime::{icall}", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { cast }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // Float/double SOURCES of the integer narrowings stay LOUD: net48 rounds
    // half-to-even BEFORE the range check; the existing ovf_*_d helpers
    // truncate first (Convert.ToByte(3.5) == 4 vs 3) -- silently different,
    // so never mapped (rounding-aware icalls are on the edit_convert.md
    // needs-new list).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ToByte")]
    [InlineData("ToSByte")]
    [InlineData("ToInt16")]
    [InlineData("ToUInt16")]
    [InlineData("ToUInt32")]
    [InlineData("ToUInt64")]
    public void Resolve_Convert_NarrowingFamily_FloatDoubleSource_ThrowsLoud(string member)
    {
        var floatEx = Assert.Throws<NotSupportedException>(() =>
            ResolveConvert(member, new byte[] { 0x00, 0x01, 0x05, 0x0C }));
        Assert.Contains($"System.Convert::{member}", floatEx.Message);

        var doubleEx = Assert.Throws<NotSupportedException>(() =>
            ResolveConvert(member, new byte[] { 0x00, 0x01, 0x05, 0x0D }));
        Assert.Contains($"System.Convert::{member}", doubleEx.Message);
    }

    //--------------------------------------------------------------------------
    // ToDecimal: STILL LOUD -- no decimal-target runtime icall exists at all
    // (ctor-based paths need the dec-result-slot protocol; see edit_convert.md).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_ToDecimal_IntSource_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveConvert("ToDecimal", new byte[] { 0x00, 0x01, 0x05, 0x08 }));

        Assert.Contains("System.Convert::ToDecimal", ex.Message);
        Assert.Contains("no decimal-target runtime icalls", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Unknown member -- dispatch miss falls back to the generated-name path
    // (the manual's "unwired members return null" contract).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_UnknownMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveConvert("Future", new byte[] { 0x00, 0x01, 0x08, 0x08 });

        Assert.StartsWith("System_Convert_Future_", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveConvert(string memberName, byte[] signature)
    {
        using var pe = new PEReader(Synthesize(memberName, signature));
        var md = pe.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Builds an assembly whose only MemberReference is System.Convert::<member>.
    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var convertRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Convert"));
        mdb.AddMemberReference(convertRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
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
