using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// BATCH 8 Convert residuals: the OBJECT-source overloads (Convert.ToXxx(object))
// and the value-source arms the batch-5 sweep LOUD-rejected with a wrong
// "InvalidCastException" note. Every arm maps to the runtime icall that
// reproduces the net48 oracle (conv-obj difftest, sha 0f55648f...).
public class MethodMapTestsConvertObj
{
    [Theory]
    // object source (0x1C) -> the boxed-dispatch icall, the object passes uncast.
    [InlineData("ToInt32", 0x08, "il2cpp::runtime::icall_convert_to_int32_obj", "", null)]
    [InlineData("ToInt64", 0x0A, "il2cpp::runtime::icall_convert_to_int64_obj", "", "l")]
    [InlineData("ToUInt32", 0x09, "il2cpp::runtime::icall_convert_to_uint32_obj", "", null)]
    [InlineData("ToUInt64", 0x0B, "il2cpp::runtime::icall_convert_to_uint64_obj", "", "l")]
    [InlineData("ToDouble", 0x0D, "il2cpp::runtime::icall_convert_to_double_obj", "", "d")]
    [InlineData("ToSingle", 0x0C, "il2cpp::runtime::icall_convert_to_single_obj", "", "d")]
    [InlineData("ToBoolean", 0x02, "il2cpp::runtime::icall_convert_to_bool_obj", "", null)]
    [InlineData("ToChar", 0x03, "il2cpp::runtime::icall_convert_to_char_obj", "", null)]
    [InlineData("ToByte", 0x05, "il2cpp::runtime::icall_convert_to_byte_obj", "", null)]
    [InlineData("ToSByte", 0x04, "il2cpp::runtime::icall_convert_to_sbyte_obj", "", null)]
    [InlineData("ToInt16", 0x06, "il2cpp::runtime::icall_convert_to_int16_obj", "", null)]
    [InlineData("ToUInt16", 0x07, "il2cpp::runtime::icall_convert_to_uint16_obj", "", null)]
    // NOTE: the ToDecimal object-source arm is pinned by the conv-obj difftest
    // (its 0x11 VALUETYPE return needs a TypeDefOrRef token this minimal
    // synthesizer does not emit).
    public void Resolve_Convert_ObjectSource_MapsToBoxedIcall(
        string name, byte ret, string expected, string cast, string? resultKind)
    {
        var resolved = ResolveMember(Synthesize(name, new byte[] { 0x00, 0x01, ret, 0x1C }), name);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { cast }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    [Theory]
    // batch 8: the ToString sources the batch-5 sweep left LOUD (uint/ulong
    // render the full unsigned range; the float source keeps the "d" carrier
    // but formats at FLOAT width -- floatfmt; decimal).
    [InlineData("ToString", 0x0E, 0x09, "il2cpp::runtime::icall_uint32_to_string", "u4", null)]
    [InlineData("ToString", 0x0E, 0x0B, "il2cpp::runtime::icall_uint64_to_string", "q", null)]
    // floatfmt: the Single source must NOT ride the double G15 engine --
    // net48 Convert.ToString(0.1f) is "0.1", the FLOAT-width default text.
    [InlineData("ToString", 0x0E, 0x0C, "il2cpp::runtime::icall_single_to_string", "d", null)]
    // NOTE: ToString(decimal) is pinned by the conv-obj difftest (the 0x11
    // VALUETYPE source needs a TypeDefOrRef token this synthesizer lacks).
    public void Resolve_Convert_ToStringSource_MapsToIcall(
        string name, byte ret, byte src, string expected, string cast, string? resultKind)
    {
        var resolved = ResolveMember(Synthesize(name, new byte[] { 0x00, 0x01, ret, src }), name);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { cast }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    [Theory]
    // batch 8: the InvalidCastException residuals (net48 always throws).
    [InlineData("ToDouble", 0x0D, 0x03, "il2cpp::runtime::icall_convert_to_double_char", "i", "d")]
    [InlineData("ToBoolean", 0x02, 0x03, "il2cpp::runtime::icall_convert_to_bool_char", "i", null)]
    [InlineData("ToChar", 0x03, 0x02, "il2cpp::runtime::icall_convert_to_char_bool", "i", null)]
    [InlineData("ToChar", 0x03, 0x0C, "il2cpp::runtime::icall_convert_to_char_d", "d", null)]
    public void Resolve_Convert_IceResidual_MapsToIcall(
        string name, byte ret, byte src, string expected, string cast, string? resultKind)
    {
        var resolved = ResolveMember(Synthesize(name, new byte[] { 0x00, 0x01, ret, src }), name);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { cast }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    [Theory]
    // batch 8: the Base64 offset/length overloads + FromBase64CharArray. The
    // SZARRAY element byte follows 0x1D (0x05 byte[] / 0x03 char[]).
    // sigTail = the return type followed by the parameter types (SZARRAY types
    // carry their element byte).
    [InlineData("ToBase64String", 3, new byte[] { 0x0E, 0x1D, 0x05, 0x08, 0x08 },
        "il2cpp::runtime::il2cpp_convert_to_base64_string_range", new[] { "a", "i", "i" }, null)]
    [InlineData("FromBase64CharArray", 3, new byte[] { 0x1D, 0x05, 0x1D, 0x03, 0x08, 0x08 },
        "il2cpp::runtime::il2cpp_convert_from_base64_char_array", new[] { "a", "i", "i" }, null)]
    // NOTE: the Base64FormattingOptions overloads (the 0x11 VALUETYPE enum
    // param) are pinned by the conv-obj difftest (the enum token cannot be
    // synthesized by this minimal harness).
    public void Resolve_Convert_Base64Arity_MapsToIcall(
        string name, int paramCount, byte[] sigTail, string expected, string[] casts, string? resultKind)
    {
        var sig = new byte[2 + sigTail.Length];
        sig[0] = 0x00; sig[1] = (byte)paramCount;
        Array.Copy(sigTail, 0, sig, 2, sigTail.Length);
        var resolved = ResolveMember(Synthesize(name, sig), name);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(casts, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    [Theory]
    // value-source residuals: net48 performs these (the batch-5 sweep wrongly
    // claimed InvalidCastException).
    [InlineData("ToDouble", 0x0D, 0x02, "il2cpp::runtime::icall_conv_d_i4", "i", "d")]          // bool
    [InlineData("ToBoolean", 0x02, 0x0A, "il2cpp::runtime::icall_convert_to_bool_i8", "l", null)] // long
    [InlineData("ToBoolean", 0x02, 0x0B, "il2cpp::runtime::icall_convert_to_bool_i8", "q", null)] // ulong
    [InlineData("ToBoolean", 0x02, 0x0C, "il2cpp::runtime::icall_convert_to_bool_d", "d", null)]  // float
    [InlineData("ToBoolean", 0x02, 0x0D, "il2cpp::runtime::icall_convert_to_bool_d", "d", null)]  // double
    // NOTE: ToBoolean(decimal) is pinned by the conv-obj difftest (the 0x11
    // source needs a TypeDefOrRef token this synthesizer does not emit).
    [InlineData("ToChar", 0x03, 0x0A, "il2cpp::runtime::icall_convert_to_char_i8", "l", null)]    // long
    [InlineData("ToChar", 0x03, 0x0B, "il2cpp::runtime::icall_convert_to_char_u8", "q", null)]    // ulong
    public void Resolve_Convert_ValueResidual_MapsToIcall(
        string name, byte ret, byte src, string expected, string cast, string? resultKind)
    {
        var resolved = ResolveMember(Synthesize(name, new byte[] { 0x00, 0x01, ret, src }), name);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { cast }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);
    }

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Convert"));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
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
