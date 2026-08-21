using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.BitConverter dispatch tests: prove the System.BitConverter domain
// (TryMapBitConverter in MethodMap.BitConverter.cs) routes each member to the
// matching bitconverter.h entry point:
//
//   ToBoolean/ToChar/ToInt16/ToUInt16/ToInt32/ToUInt32 -> int32 slot (null
//     result kind)
//   ToInt64/ToUInt64                                -> int64 slot ("l")
//   ToSingle/ToDouble                               -> double slot ("d")
//   ToT(byte[]) / ToT(byte[], int)                  -> same icall; the byte[]
//     param carries the "a" array cast ((Il2CppArray*), the concrete type the
//     bitconverter.h icalls declare); the startIndex is DROPPED ("drop" cast;
//     the runtime simplified form reads from index 0 -- exact for
//     startIndex = 0)
//   SingleToInt32Bits / Int32BitsToSingle           -> runtime bits bridges
//   DoubleToInt64Bits / Int64BitsToDouble           -> STAY on the Convert
//     bridge (il2cpp_double_bits): TryMapConvert runs first in the ??
//     fallback chain, so the BitConverter file must NOT duplicate them
//
// LOUD rejections (known-unwireable, never a silent wrong emission):
//   * GetBytes -- the byte[] RETURN's element byte leaks into ps[0]
//     (RawSignature does not consume the SZARRAY ret's 0x05), so EVERY
//     overload resolves identically (ps=[0x05], count=1); overload dispatch
//     is impossible without a RawSignature fix. The two GetBytes tests below
//     (int32 vs single param) prove the inputs are indistinguishable.
//   * ToString(byte[], int) / (byte[], int, int) -- the offset/length
//     overloads have no runtime helper (single-arity form only).
//   * get_IsLittleEndian -- no runtime entry (net48 exposes it as a FIELD,
//     so real code never reaches the method mapper anyway).
//
// Signature blobs are the raw bytes RawSignature consumes; byte[] params are
// 0x1D SZARRAY + 0x05 U1 element byte (consumed by ReadParamByte); the byte[]
// RETURN is 0x1D + 0x05 where the element byte is NOT consumed (the leak).
public class MethodMapBitConverterTests
{
    //--------------------------------------------------------------------------
    // ToT family -- offset-free (byte[]) forms, int32 result slot
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ToBoolean", 0x02, "il2cpp::runtime::il2cpp_bitconverter_to_bool")]
    [InlineData("ToChar", 0x03, "il2cpp::runtime::il2cpp_bitconverter_to_char")]
    [InlineData("ToInt16", 0x06, "il2cpp::runtime::il2cpp_bitconverter_to_int16")]
    [InlineData("ToUInt16", 0x07, "il2cpp::runtime::il2cpp_bitconverter_to_uint16")]
    [InlineData("ToInt32", 0x08, "il2cpp::runtime::il2cpp_bitconverter_to_int32")]
    [InlineData("ToUInt32", 0x09, "il2cpp::runtime::il2cpp_bitconverter_to_uint32")]
    public void Resolve_BitConverter_ToTByteArray_MapsToIcall(string member, byte ret, string icall)
    {
        // <ret> ToX(byte[]): ret byte, SZARRAY (0x1D) param + element byte 0x05.
        var resolved = ResolveMember(Synthesize("System", "BitConverter", member,
            new byte[] { 0x00, 0x01, ret, 0x1D, 0x05 }), member);

        Assert.Equal(icall, resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        // "a" cast: the byte[] arg arrives as Il2CppObject* (the GetBytes
        // result temp) but the bitconverter.h icalls declare Il2CppArray* --
        // without the cast the generated C++ fails to compile
        // (bitconvcase build error "cannot initialize a parameter of type
        // 'Il2CppArray *'"). Same cast as the ToString hex formatter below.
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { false }, resolved.ParamIsRef);
        Assert.Null(resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToT family -- 8-byte results carry the int64/double result kinds
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ToInt64", 0x0A, "il2cpp::runtime::il2cpp_bitconverter_to_int64", "l")]
    [InlineData("ToUInt64", 0x0B, "il2cpp::runtime::il2cpp_bitconverter_to_uint64", "l")]
    [InlineData("ToSingle", 0x0C, "il2cpp::runtime::il2cpp_bitconverter_to_single", "d")]
    [InlineData("ToDouble", 0x0D, "il2cpp::runtime::il2cpp_bitconverter_to_double", "d")]
    public void Resolve_BitConverter_ToTWideByteArray_MapsToIcallWithResultKind(string member, byte ret, string icall, string kind)
    {
        var resolved = ResolveMember(Synthesize("System", "BitConverter", member,
            new byte[] { 0x00, 0x01, ret, 0x1D, 0x05 }), member);

        Assert.Equal(icall, resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(kind, resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToT(byte[], int) -- the startIndex form: same icall, arg DROPPED
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("ToBoolean", 0x02, "il2cpp::runtime::il2cpp_bitconverter_to_bool")]
    [InlineData("ToInt32", 0x08, "il2cpp::runtime::il2cpp_bitconverter_to_int32")]
    [InlineData("ToDouble", 0x0D, "il2cpp::runtime::il2cpp_bitconverter_to_double")]
    public void Resolve_BitConverter_ToTWithStartIndex_DropsTheIndex(string member, byte ret, string icall)
    {
        // <ret> ToX(byte[], int): SZARRAY param + element, int32 param.
        var resolved = ResolveMember(Synthesize("System", "BitConverter", member,
            new byte[] { 0x00, 0x02, ret, 0x1D, 0x05, 0x08 }), member);

        Assert.Equal(icall, resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BitConverter_ToInt32ThreeArity_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "BitConverter", "ToInt32",
                new byte[] { 0x00, 0x03, 0x08, 0x1D, 0x05, 0x08, 0x08 }), "ToInt32"));

        Assert.Contains("arity 3", ex.Message);
    }

    [Fact]
    public void Resolve_BitConverter_ToInt32LongStartIndex_ThrowsLoud()
    {
        // ToInt32(byte[], long) -- the startIndex must be int32.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "BitConverter", "ToInt32",
                new byte[] { 0x00, 0x02, 0x08, 0x1D, 0x05, 0x0A }), "ToInt32"));

        Assert.Contains("startIndex", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Bits bridges -- SingleToInt32Bits / Int32BitsToSingle
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BitConverter_SingleToInt32Bits_MapsToBitsBridge()
    {
        // int32 SingleToInt32Bits(float32): int32 ret, 0x0C param.
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "SingleToInt32Bits",
            new byte[] { 0x00, 0x01, 0x08, 0x0C }), "SingleToInt32Bits");

        Assert.Equal("il2cpp::runtime::il2cpp_bitconverter_single_to_int32_bits", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "d" }, resolved.ParamTypeNames);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_BitConverter_Int32BitsToSingle_MapsToBitsBridge()
    {
        // float32 Int32BitsToSingle(int32): 0x0C ret, 0x08 param.
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "Int32BitsToSingle",
            new byte[] { 0x00, 0x01, 0x0C, 0x08 }), "Int32BitsToSingle");

        Assert.Equal("il2cpp::runtime::il2cpp_bitconverter_int32_bits_to_single", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "i" }, resolved.ParamTypeNames);
        Assert.Equal("d", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // The DOUBLE bits bridges stay on the Convert bridge (?? chain: Convert
    // runs first) -- the BitConverter file must not duplicate them.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BitConverter_DoubleToInt64Bits_StaysOnConvertBridge()
    {
        // int64 DoubleToInt64Bits(double): 0x0A ret, 0x0D param.
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "DoubleToInt64Bits",
            new byte[] { 0x00, 0x01, 0x0A, 0x0D }), "DoubleToInt64Bits");

        Assert.Equal("il2cpp_double_bits", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_BitConverter_Int64BitsToDouble_StaysOnConvertBridge()
    {
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "Int64BitsToDouble",
            new byte[] { 0x00, 0x01, 0x0D, 0x0A }), "Int64BitsToDouble");

        Assert.Equal("il2cpp_double_from_bits", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // GetBytes -- G2: RawSignature consumes the SZARRAY-return element byte,
    // so ps[0] carries the VALUE param type (0x08=i4 / 0x0A=i8 / 0x0C=r4 ...)
    // and every overload dispatches to its exact get_bytes_* icall -- the
    // byte[] return is a managed reference (ReturnsRef, like FromBase64String).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BitConverter_GetBytesInt32_MapsToInt32Icall()
    {
        // byte[] GetBytes(int32): 0x1D 0x05 ret, 0x08 param.
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "GetBytes",
            new byte[] { 0x00, 0x01, 0x1D, 0x05, 0x08 }), "GetBytes");

        Assert.Equal("il2cpp::runtime::il2cpp_bitconverter_get_bytes_int32", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BitConverter_GetBytesInt64_MapsToInt64Icall()
    {
        // byte[] GetBytes(int64): 0x1D 0x05 ret, 0x0A param.
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "GetBytes",
            new byte[] { 0x00, 0x01, 0x1D, 0x05, 0x0A }), "GetBytes");

        Assert.Equal("il2cpp::runtime::il2cpp_bitconverter_get_bytes_int64", resolved.CppName);
        Assert.Equal(new[] { "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BitConverter_GetBytesSingle_MapsToSingleIcall()
    {
        // byte[] GetBytes(float32): 0x1D 0x05 ret, 0x0C param -- the same blob
        // as GetBytes(int32) EXCEPT the param byte: the overloads now
        // distinguish at dispatch (the old leak erased this).
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "GetBytes",
            new byte[] { 0x00, 0x01, 0x1D, 0x05, 0x0C }), "GetBytes");

        Assert.Equal("il2cpp::runtime::il2cpp_bitconverter_get_bytes_single", resolved.CppName);
        Assert.Equal(new[] { "d" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // ToString(byte[]) -> string hex formatter: the "a" cast byte[] slot, a
    // managed string result (ReturnsRef, the ToBase64String precedent); the
    // offset/length overloads have no runtime helper -- LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BitConverter_ToStringByteArray_MapsToHexFormatterIcall()
    {
        // string ToString(byte[]): 0x0E ret, 0x1D 0x05 param (SZARRAY U1).
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "ToString",
            new byte[] { 0x00, 0x01, 0x0E, 0x1D, 0x05 }), "ToString");

        Assert.Equal("il2cpp::runtime::il2cpp_bitconverter_to_hex_string", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "a" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { false }, resolved.ParamIsRef);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_BitConverter_ToStringWithStartIndex_ThrowsLoud()
    {
        // string ToString(byte[], int): 0x0E ret, 0x1D 0x05 0x08 params.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "BitConverter", "ToString",
                new byte[] { 0x00, 0x02, 0x0E, 0x1D, 0x05, 0x08 }), "ToString"));

        Assert.Contains("arity 2", ex.Message);
    }

    [Fact]
    public void Resolve_BitConverter_GetIsLittleEndian_ThrowsLoud()
    {
        // bool get_IsLittleEndian(): bool ret, 0 params.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "BitConverter", "get_IsLittleEndian",
                new byte[] { 0x00, 0x00, 0x02 }), "get_IsLittleEndian"));

        Assert.Contains("little-endian is the platform contract", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Unknown member -- dispatch miss falls back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BitConverter_UnknownMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "Future",
            new byte[] { 0x00, 0x01, 0x08, 0x08 }), "Future");

        Assert.StartsWith("System_BitConverter_Future_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Base64.cs / MethodMapTests.VersionUri.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
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
