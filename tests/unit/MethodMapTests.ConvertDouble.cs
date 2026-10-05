using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// T1 parity: Convert double/float-source arms over the rounding _d icalls
// (net48 rounds half-to-even then range-checks; the old ovf_*_d truncate).
// Precedent: ToInt32 0x0C/0x0D -> icall_convert_to_int32_d ("d" cast widens
// the float slot; the helper rounds). ResultKind "l" for the 64-bit targets.
public class MethodMapTestsConvertDouble
{
    [Theory]
    [InlineData("ToInt64", 0x0A, "il2cpp::runtime::icall_convert_to_int64_d", "l")]
    [InlineData("ToUInt32", 0x09, "il2cpp::runtime::icall_convert_to_uint32_d", null)]
    [InlineData("ToUInt64", 0x0B, "il2cpp::runtime::icall_convert_to_uint64_d", "l")]
    [InlineData("ToByte", 0x05, "il2cpp::runtime::icall_convert_to_byte_d", null)]
    [InlineData("ToSByte", 0x04, "il2cpp::runtime::icall_convert_to_sbyte_d", null)]
    [InlineData("ToUInt16", 0x07, "il2cpp::runtime::icall_convert_to_uint16_d", null)]
    [InlineData("ToInt16", 0x06, "il2cpp::runtime::icall_convert_to_int16_d", null)]
    public void Resolve_Convert_DoubleSource_MapsToRoundingIcall(string name, byte ret, string expected, string? resultKind)
    {
        var resolved = ResolveMember(Synthesize(name, new byte[] { 0x00, 0x01, ret, 0x0D }), name);

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { "d" }, resolved.ParamTypeNames);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    [Theory]
    [InlineData("ToInt64", 0x0A, "il2cpp::runtime::icall_convert_to_int64_d", "l")]
    [InlineData("ToUInt64", 0x0B, "il2cpp::runtime::icall_convert_to_uint64_d", "l")]
    [InlineData("ToByte", 0x05, "il2cpp::runtime::icall_convert_to_byte_d", null)]
    public void Resolve_Convert_FloatSource_WidensToRoundingIcall(string name, byte ret, string expected, string? resultKind)
    {
        var resolved = ResolveMember(Synthesize(name, new byte[] { 0x00, 0x01, ret, 0x0C }), name);

        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(new[] { "d" }, resolved.ParamTypeNames);
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
