using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// BATCH 9 Convert arity/param-family residuals: the 2-arg IFormatProvider
// forms (object/string source) and the radix forms ToString(value, int) /
// ToXxx(string, int). Every arm maps to the runtime icall that reproduces the
// net48 oracle (conv-arity difftest).
public class MethodMapTestsConvertArity
{
    [Theory]
    // 2-arg IFormatProvider forms: the provider is dropped ("drop" cast) and
    // the 1-arg icall serves the value (net48 ignores the provider).
    [InlineData("ToInt32", 0x08, 0x1C, "il2cpp::runtime::icall_convert_to_int32_obj", "", null)]
    [InlineData("ToInt32", 0x08, 0x0E, "il2cpp::runtime::icall_convert_to_int32_string", "s", null)]
    [InlineData("ToInt64", 0x0A, 0x1C, "il2cpp::runtime::icall_convert_to_int64_obj", "", "l")]
    [InlineData("ToInt64", 0x0A, 0x0E, "il2cpp::runtime::icall_convert_to_int64_string", "s", "l")]
    [InlineData("ToDouble", 0x0D, 0x1C, "il2cpp::runtime::icall_convert_to_double_obj", "", "d")]
    [InlineData("ToDouble", 0x0D, 0x0E, "il2cpp::runtime::icall_convert_to_double_string", "s", "d")]
    [InlineData("ToBoolean", 0x02, 0x1C, "il2cpp::runtime::icall_convert_to_bool_obj", "", null)]
    [InlineData("ToBoolean", 0x02, 0x0E, "il2cpp::runtime::icall_convert_to_boolean_string", "s", null)]
    [InlineData("ToChar", 0x03, 0x1C, "il2cpp::runtime::icall_convert_to_char_obj", "", null)]
    [InlineData("ToChar", 0x03, 0x0E, "il2cpp::runtime::icall_convert_to_char_string", "s", null)]
    [InlineData("ToByte", 0x05, 0x1C, "il2cpp::runtime::icall_convert_to_byte_obj", "", null)]
    [InlineData("ToByte", 0x05, 0x0E, "il2cpp::runtime::icall_convert_to_byte_string", "s", null)]
    [InlineData("ToSByte", 0x04, 0x1C, "il2cpp::runtime::icall_convert_to_sbyte_obj", "", null)]
    [InlineData("ToSByte", 0x04, 0x0E, "il2cpp::runtime::icall_convert_to_sbyte_string", "s", null)]
    [InlineData("ToInt16", 0x06, 0x1C, "il2cpp::runtime::icall_convert_to_int16_obj", "", null)]
    [InlineData("ToInt16", 0x06, 0x0E, "il2cpp::runtime::icall_convert_to_int16_string", "s", null)]
    [InlineData("ToUInt16", 0x07, 0x1C, "il2cpp::runtime::icall_convert_to_uint16_obj", "", null)]
    [InlineData("ToUInt16", 0x07, 0x0E, "il2cpp::runtime::icall_convert_to_uint16_string", "s", null)]
    [InlineData("ToUInt32", 0x09, 0x1C, "il2cpp::runtime::icall_convert_to_uint32_obj", "", null)]
    [InlineData("ToUInt32", 0x09, 0x0E, "il2cpp::runtime::icall_convert_to_uint32_string", "s", null)]
    [InlineData("ToUInt64", 0x0B, 0x1C, "il2cpp::runtime::icall_convert_to_uint64_obj", "", "l")]
    [InlineData("ToUInt64", 0x0B, 0x0E, "il2cpp::runtime::icall_convert_to_uint64_string", "s", "l")]
    [InlineData("ToSingle", 0x0C, 0x1C, "il2cpp::runtime::icall_convert_to_single_obj", "", "d")]
    [InlineData("ToSingle", 0x0C, 0x0E, "il2cpp::runtime::icall_convert_to_single_string", "s", "d")]
    public void Resolve_Convert_TwoArgProvider_MapsToIcall(
        string name, byte ret, byte src, string expected, string cast, string? resultKind)
    {
        // The CLASS (0x12) provider param carries a TypeDefOrRef coded index:
        // TypeRef row 2 (System.IFormatProvider) -> (2 << 2) | 1 = 9.
        var sig = new byte[] { 0x00, 0x02, ret, src, 0x12, 0x09 };
        var resolved = ResolveMember(SynthesizeWithProvider(name, sig), name);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { cast, "drop" }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    [Theory]
    // Convert.ToXxx(string, int fromBase): the batch-9 radix icalls.
    [InlineData("ToInt64", 0x0A, "il2cpp::runtime::icall_convert_to_int64_string_base", "l")]
    [InlineData("ToByte", 0x05, "il2cpp::runtime::icall_convert_to_byte_string_base", null)]
    [InlineData("ToSByte", 0x04, "il2cpp::runtime::icall_convert_to_sbyte_string_base", null)]
    [InlineData("ToInt16", 0x06, "il2cpp::runtime::icall_convert_to_int16_string_base", null)]
    [InlineData("ToUInt16", 0x07, "il2cpp::runtime::icall_convert_to_uint16_string_base", null)]
    [InlineData("ToUInt32", 0x09, "il2cpp::runtime::icall_convert_to_uint32_string_base", null)]
    [InlineData("ToUInt64", 0x0B, "il2cpp::runtime::icall_convert_to_uint64_string_base", "l")]
    public void Resolve_Convert_FromBase_MapsToIcall(string name, byte ret, string expected, string? resultKind)
    {
        var sig = new byte[] { 0x00, 0x02, ret, 0x0E, 0x08 };
        var resolved = ResolveMember(Synthesize(name, sig), name);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { "s", "i" }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    [Theory]
    // Convert.ToString(value, int toBase): byte/short/int/long.
    [InlineData(0x05, "il2cpp::runtime::icall_convert_to_string_u1_base", "i")]
    [InlineData(0x06, "il2cpp::runtime::icall_convert_to_string_i2_base", "i")]
    [InlineData(0x08, "il2cpp::runtime::icall_convert_to_string_i4_base", "i")]
    [InlineData(0x0A, "il2cpp::runtime::icall_convert_to_string_i8_base", "l")]
    public void Resolve_Convert_ToStringToBase_MapsToIcall(byte src, string expected, string cast0)
    {
        var sig = new byte[] { 0x00, 0x02, 0x0E, src, 0x08 };
        var resolved = ResolveMember(Synthesize("ToString", sig), "ToString");
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { cast0, "i" }, resolved.ParamTypeNames);
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
        return SynthesizeCore(memberName, signature, addProviderRef: false);
    }

    private static MemoryStream SynthesizeWithProvider(string memberName, byte[] signature)
    {
        return SynthesizeCore(memberName, signature, addProviderRef: true);
    }

    private static MemoryStream SynthesizeCore(string memberName, byte[] signature, bool addProviderRef)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Convert"));
        if (addProviderRef)
        {
            // The IFormatProvider TypeRef must be row 2 so the CLASS param's
            // TypeDefOrRef coded index (2 << 2) | 1 = 9 resolves to it.
            mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("IFormatProvider"));
        }
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
