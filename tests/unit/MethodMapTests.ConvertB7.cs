using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// B7 Convert residuals: the static-analyzable arms that were LOUD before batch 7.
//
//   * Convert.ToSingle(bool)  -- net48 `value ? 1f : 0f`; the bool slot carries
//                                int32 0/1, so icall_conv_r4_i4 is exact.
//   * Convert.ToSingle(char)  -- net48 throws InvalidCastException (char has no
//                                IConvertible.ToSingle; the dedicated icall is
//                                the exact contract, probe-pinned for 'A').
//   * Convert.ToSingle(uint)  -- icall_conv_r4_u4 over the "u4" reinterpret
//                                (zero-extension: uint >= 2^31 stays positive).
//   * Convert.ToUInt32(uint)  -- the identity conversion: icall_conv_u4_i4
//                                returns the same 32-bit pattern (a range test
//                                would wrongly throw for uint >= 2^31).
//   * Convert.ToDouble(uint)  -- exact (uint32 fits the 53-bit mantissa).
//   * Convert.ToDouble(float) -- the bit-exact identity (the float source rides
//                                an already-widened double slot).
public class MethodMapTestsConvertB7
{
    [Theory]
    [InlineData("ToSingle", 0x02, "il2cpp::runtime::icall_conv_r4_i4", "i", "d")]                  // bool
    [InlineData("ToSingle", 0x03, "il2cpp::runtime::icall_convert_to_single_char", "i", "d")]      // char -> ICE
    [InlineData("ToSingle", 0x09, "il2cpp::runtime::icall_conv_r4_u4", "u4", "d")]                 // uint
    [InlineData("ToUInt32", 0x09, "il2cpp::runtime::icall_conv_u4_i4", "i", null)]                 // uint identity
    [InlineData("ToDouble", 0x09, "il2cpp::runtime::icall_conv_d_u4", "u4", "d")]                 // uint
    [InlineData("ToDouble", 0x0C, "il2cpp::runtime::icall_conv_d_d", "d", "d")]                   // float
    public void Resolve_Convert_B7Arm_MapsToIcall(string name, byte src, string expected, string cast, string? resultKind)
    {
        var ret = name == "ToSingle" ? (byte)0x0C : name == "ToDouble" ? (byte)0x0D : (byte)0x09;
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
