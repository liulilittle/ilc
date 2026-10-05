using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// MethodSignatureCoder full ELEMENT_TYPE audit (b23): every ECMA-335 element
// type byte that can appear in a method signature (0x01-0x1B + 0x40-0x55) is
// pinned either to its compact encoding or to a loud reject. Wire bytes are
// verified against real Roslyn/net48 csc output (probe sigs):
//   nint=0x18, nuint=0x19, PTR=0x0F(+pointee), TYPEDBYREF=0x16,
//   SZARRAY=0x1D(+element), FNPTR=0x1B(embedded sig), ARRAY=0x14.
// The compact encoding is the method-name hash input ({owner}.{name}({params}))
// AND the ABI slot descriptor -- both must stay byte-exact per element type.
public class MethodSignatureCoderMatrixTests
{
    //--------------------------------------------------------------------------
    // Leaf/primitive params (single byte, no trailing data).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(0x01, "v")]  // VOID
    [InlineData(0x02, "b")]  // BOOLEAN (int32 slot)
    [InlineData(0x03, "c")]  // CHAR (int32 slot)
    [InlineData(0x04, "i1")] // I1 (int32 slot)
    [InlineData(0x05, "u1")] // U1 (int32 slot)
    [InlineData(0x06, "i2")] // I2 (int32 slot)
    [InlineData(0x07, "u2")] // U2 (int32 slot)
    [InlineData(0x08, "i")]  // I4
    [InlineData(0x09, "i")]  // U4 (int32 slot, unsigned semantics live at the compare site)
    [InlineData(0x0A, "l")]  // I8 (int64 slot)
    [InlineData(0x0B, "q")]  // U8 (uint64 slot)
    [InlineData(0x0C, "f")]  // R4 (double slot)
    [InlineData(0x0D, "d")]  // R8 (double slot)
    [InlineData(0x0E, "r:System.String")]
    [InlineData(0x16, "tr")] // TYPEDBYREF (no trailing)
    [InlineData(0x18, "n")]  // ELEMENT_TYPE_I (native int, raw 0x18 -- int32 slot)
    [InlineData(0x19, "i")]  // ELEMENT_TYPE_U (native uint, raw 0x19 -- as int32)
    [InlineData(0x1C, "r:System.Object")]
    public void Param_LeafTypes_Encode(int typeByte, string expected)
    {
        Assert.Equal(expected, EncodeParams(new byte[] { (byte)typeByte }));
    }

    //--------------------------------------------------------------------------
    // Structural params (trailing payload).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(new byte[] { 0x0F, 0x08 }, "P:i")]                 // int* p
    [InlineData(new byte[] { 0x0F, 0x01 }, "P:v")]                 // void* p
    [InlineData(new byte[] { 0x0F, 0x0E }, "P:r:System.String")]   // string* p
    [InlineData(new byte[] { 0x10, 0x08 }, "B:i")]                 // ref/out int
    [InlineData(new byte[] { 0x11, 0x05 }, "V:System.Object")]     // VALUETYPE System.Object (TypeRef row 1)
    [InlineData(new byte[] { 0x12, 0x05 }, "r:System.Object")]     // CLASS System.Object
    [InlineData(new byte[] { 0x13, 0x00 }, "i")]                   // VAR T (class-level generic param, index 0)
    [InlineData(new byte[] { 0x14, 0x08, 0x02, 0x00, 0x00 }, "r:i[2]")] // int[,]
    [InlineData(new byte[] { 0x15, 0x12, 0x05, 0x01, 0x08 }, "r:System.Object<i>")] // List<int>-shaped GenericInst
    [InlineData(new byte[] { 0x1B, 0x00, 0x02, 0x08, 0x08, 0x08 }, "F:i(i;i)")]     // delegate*<int,int,int>
    [InlineData(new byte[] { 0x1D, 0x08 }, "r:i[]")]               // int[]
    [InlineData(new byte[] { 0x1E, 0x00 }, "i")]                   // MVAR !!0 (method-level generic param, index 0)
    // ECMA-335 II.23.1.16 custom modifiers (0x1F CMOD_REQD / 0x20 CMOD_OPT):
    // a coded index followed by the modified type -- both are consumed and the
    // underlying type encodes (volatile's IsVolatile modreq is the real-world
    // shape: `static volatile int` field sig = 06 1F 09 08; the old codebase
    // treated 0x1F as an SZARRAY variant, which is wrong).
    [InlineData(new byte[] { 0x1F, 0x05, 0x08 }, "i")]             // CMOD_REQD + I4
    [InlineData(new byte[] { 0x20, 0x05, 0x08 }, "i")]             // CMOD_OPT + I4
    [InlineData(new byte[] { 0x40, 0x05 }, "T:System.Object")]     // ELEMENT_TYPE type-handle (nested)
    public void Param_StructuralTypes_Encode(byte[] paramBytes, string expected)
    {
        Assert.Equal(expected, EncodeParams(paramBytes));
    }

    // Vararg sentinel (0x41) is a separator, not a real param: consumed, encoded
    // as a "..." marker, and the loop keeps decoding the following (optional) args.
    [Fact]
    public void Param_Sentinel_ConsumedAndMarked()
    {
        // header 0x00, paramCount 2, ret void, params: SENTINEL(0x41), int32
        Assert.Equal("...,i", EncodeParams(new byte[] { 0x41, 0x08 }, paramCount: 2));
    }

    //--------------------------------------------------------------------------
    // Reserved / unsupported bytes reject LOUDLY (never a silent misparse).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(0x17)] // reserved (ECMA-335 PRINTED I=0x17 is an erratum; real nint=0x18)
    [InlineData(0x1A)] // reserved
    [InlineData(0x20)] // CMOD_OPT with no payload = malformed -> loud (well-formed cmods are consumed, see StructuralTypes)
    [InlineData(0x21)] // INTERNAL
    [InlineData(0x42)] // undefined
    [InlineData(0x45)] // PINNED (local-signature modifier, never a method param)
    [InlineData(0x53)] // ELEMENT_TYPE_TYPE (TypeSpec-arg context, not a method param)
    [InlineData(0x55)] // TAGGED_OBJECT (deprecated)
    public void Param_ReservedOrUnsupported_RejectLoud(int typeByte)
    {
        var ex = Assert.Throws<NotSupportedException>(() => EncodeParams(new byte[] { (byte)typeByte }));
        Assert.Contains($"0x{typeByte:X2}", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Return-type trailing-data boundaries (ConsumeReturnTrailing): a return
    // byte with trailing payload must be fully consumed BEFORE the param loop,
    // or the first param decodes the element/pointee byte instead.
    //--------------------------------------------------------------------------

    // string[] Get(int x) -- return 0x1D SZARRAY + element 0x0E; without the
    // consume the param loop reads 0x0E ("r:System.String") and drops the int.
    [Fact]
    public void Return_SzArray_ConsumesElementByte()
    {
        var (pc, ret, pt) = DecodeMethod(new byte[] { 0x00, 0x01, 0x1D, 0x0E, 0x08 });
        Assert.Equal(1, pc);
        Assert.Equal(SignatureTypeCode.SZArray, ret);
        Assert.Equal("i", pt);
    }

    // int[] Get(int a, string b) -- two params after the element byte.
    [Fact]
    public void Return_SzArray_MultiParam_Aligned()
    {
        var (pc, _, pt) = DecodeMethod(new byte[] { 0x00, 0x02, 0x1D, 0x08, 0x08, 0x0E });
        Assert.Equal(2, pc);
        Assert.Equal("i,r:System.String", pt);
    }

    // ref int Get(int x) -- return 0x10 BYREF + element 0x08.
    [Fact]
    public void Return_ByRef_ConsumesElementByte()
    {
        var (pc, ret, pt) = DecodeMethod(new byte[] { 0x00, 0x01, 0x10, 0x08, 0x08 });
        Assert.Equal(1, pc);
        Assert.Equal(SignatureTypeCode.ByReference, ret);
        Assert.Equal("i", pt);
    }

    // int* Get(int x) -- return 0x0F PTR + pointee 0x08.
    [Fact]
    public void Return_Ptr_ConsumesPointeeByte()
    {
        var (pc, ret, pt) = DecodeMethod(new byte[] { 0x00, 0x01, 0x0F, 0x08, 0x08 });
        Assert.Equal(1, pc);
        Assert.Equal(SignatureTypeCode.Pointer, ret);
        Assert.Equal("i", pt);
    }

    // Native-int/uint returns surface the RIGHT SignatureTypeCode via the raw
    // byte cast ((SignatureTypeCode)0x18 == IntPtr, (SignatureTypeCode)0x19 ==
    // UIntPtr) -- the enum values match the wire bytes (pinvokeext 00-01-18-0E).
    [Theory]
    [InlineData(0x18, SignatureTypeCode.IntPtr)]
    [InlineData(0x19, SignatureTypeCode.UIntPtr)]
    public void Return_NativeInt_ValueTypeSurfaces(int returnByte, SignatureTypeCode expected)
    {
        var (_, ret, _) = DecodeMethod(new byte[] { 0x00, 0x00, (byte)returnByte });
        Assert.Equal(expected, ret);
    }

    // 0x20 CMOD_OPT return carries TypeDefOrRef + underlying -- reject loudly
    // (symmetric with the param contract), never silently misalign the loop.
    [Fact]
    public void Return_CmodOpt_RejectsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            DecodeMethod(new byte[] { 0x00, 0x00, 0x20 }));
        Assert.Contains("0x20", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Harness -- one TypeRef (System.Object, row 1 -> coded index 0x05) + one
    // MemberRef carrying the raw signature blob (same pipeline as the
    // GenericsSignatureTests / FnPtrTests synthesizers).
    //--------------------------------------------------------------------------
    private static (int ParamCount, SignatureTypeCode ReturnType, string ParamTypes) DecodeMethod(byte[] signature)
    {
        using var pe = new PEReader(SynthesizeMember(signature));
        var md = pe.GetMetadataReader();
        var (pc, ret, pt, _) = MethodSignatureCoder.Decode(md, md.GetMemberReference(md.MemberReferences.Single()).Signature);
        return (pc, ret, pt);
    }

    private static string EncodeParams(byte[] paramBytes, int paramCount = 1)
    {
        var sig = new byte[3 + paramBytes.Length];
        sig[0] = 0x00;                    // default calling convention
        sig[1] = (byte)paramCount;
        sig[2] = 0x01;                    // return type void (no trailing)
        paramBytes.CopyTo(sig, 3);
        return DecodeMethod(sig).ParamTypes;
    }

    private static MemoryStream SynthesizeMember(byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("matrix.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        // ONE TypeRef (System.Object, row 1 -> TypeDefOrRef coded index 0x05) so
        // the VALUETYPE/CLASS/GENERICINST/type-handle tests resolve cleanly.
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        mdb.AddMemberReference(objRef, mdb.GetOrAddString("M"), mdb.GetOrAddBlob(signature));
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
