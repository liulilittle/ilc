using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.DecimalMissing dispatch tests: cover the decimal mappings absent
// from MethodMapTests.Decimal.cs -- FCallToInt32, the op_Explicit primitive
// family (int32/int64/uint32/uint64/float/double), ToSingle, ToInt32, and
// GetHashCode. These exercise the MethodMap.Decimal.cs branches that the
// original test file never reached.
public class MethodMapDecimalMissingTests
{
    //--------------------------------------------------------------------------
    // FCallToInt32 -- internal truncating conversion (the BCL op_Explicit
    // wrapper's FCall entry). Maps to icall_decimal_fcall_to_int32.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_FCallToInt32_MapsToFCallIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "FCallToInt32",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "FCallToInt32");

        Assert.Equal("il2cpp::runtime::icall_decimal_fcall_to_int32", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // op_Explicit(decimal -> int32) -- the 0x08 return byte routes to the
    // truncating icall_decimal_to_int32.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpExplicitToInt32_MapsToTruncatingIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Explicit",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "op_Explicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_int32", resolved.CppName);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // op_Explicit(decimal -> int64) -- the 0x0A return byte routes to
    // icall_decimal_to_int64 (ResultKind "l" for the 64-bit result).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpExplicitToInt64_MapsToLongIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Explicit",
            new byte[] { 0x00, 0x01, 0x0A, 0x11, 0x05 }), "op_Explicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_int64", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // op_Explicit(decimal -> uint32) -- the 0x09 return byte routes to
    // icall_decimal_to_uint32.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpExplicitToUInt32_MapsToUnsignedIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Explicit",
            new byte[] { 0x00, 0x01, 0x09, 0x11, 0x05 }), "op_Explicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_uint32", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // op_Explicit(decimal -> uint64) -- the 0x0B return byte routes to
    // icall_decimal_to_uint64 (ResultKind "l").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpExplicitToUInt64_MapsToULongIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Explicit",
            new byte[] { 0x00, 0x01, 0x0B, 0x11, 0x05 }), "op_Explicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_uint64", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // op_Explicit(decimal -> float) -- the 0x0C return byte routes to
    // icall_decimal_to_float (float rides the double carrier).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpExplicitToFloat_MapsToFloatIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Explicit",
            new byte[] { 0x00, 0x01, 0x0C, 0x11, 0x05 }), "op_Explicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_float", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // op_Explicit(decimal -> double) -- the 0x0D return byte routes to
    // icall_decimal_to_double.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpExplicitToDouble_MapsToDoubleIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Explicit",
            new byte[] { 0x00, 0x01, 0x0D, 0x11, 0x05 }), "op_Explicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_double", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToSingle -- instance, one decimal arg, rides the double carrier.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_ToSingle_MapsToFloatIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "ToSingle",
            new byte[] { 0x00, 0x01, 0x0C, 0x11, 0x05 }), "ToSingle");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_float", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal("d", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToInt32 -- instance, one decimal arg, truncating conversion.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_ToInt32_MapsToTruncatingIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "ToInt32",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "ToInt32");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_int32", resolved.CppName);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // GetHashCode -- instance, no args, the "dec" receiver cast.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_GetHashCode_MapsToHashCodeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "GetHashCode",
            new byte[] { 0x00, 0x00, 0x08 }), "GetHashCode");

        Assert.Equal("il2cpp::runtime::icall_decimal_get_hash_code", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as MethodMapDecimalTests)
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
