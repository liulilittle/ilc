using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// b5-gaps section 13: the multidim allocation layer (newobj ctor fold) must
// accept VALUE-TYPE elements. `new DateTime[2,2]` lowers to
//   ldc.i4.2; ldc.i4.2; newobj DateTime[0...,0...]::.ctor(int32,int32)
// where the ctor's declaring TypeSpec blob opens ELEMENT_TYPE_ARRAY (0x14)
// and the element byte is VALUETYPE (0x11). ReadMultidimElemSuffix must map
// the value element onto the mdnew value channel (the arrayinit-vt width
// conventions: DateTime/TimeSpan 8-byte cells, Guid/Decimal/DateTimeOffset
// 16-byte cells, an enum the UNDERLYING type's width -- the raw value__
// signature decides, the int32 carrier model does not). A user STRUCT keeps
// the LOUD reject (no GC reference-field bitmap channel yet); a nested value
// generic (0x15 marker 0x11) keeps the LOUD too.
//------------------------------------------------------------------------------
public class StackFoldTestsMultidimVt
{
    public enum SynthElem
    {
        DateTimeRef, TimeSpanRef, GuidRef, DateTimeOffsetRef, DecimalRef,
        EnumInt, EnumByte, EnumLong, UserStruct,
    }

    [Theory]
    [InlineData(SynthElem.DateTimeRef, "mdnew_dt2")]
    [InlineData(SynthElem.TimeSpanRef, "mdnew_ts2")]
    [InlineData(SynthElem.GuidRef, "mdnew_guid2")]
    [InlineData(SynthElem.DateTimeOffsetRef, "mdnew_dto2")]
    [InlineData(SynthElem.DecimalRef, "mdnew_dec2")]
    [InlineData(SynthElem.EnumInt, "mdnew_int2")]
    [InlineData(SynthElem.EnumByte, "mdnew_byte2")]
    [InlineData(SynthElem.EnumLong, "mdnew_long2")]
    public void Fold_MultidimCtor_ValueElement_FoldsMdnew(SynthElem kind, string expectedAlloc)
    {
        using var pe = SynthMultidim(kind, rank: 2);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);

        var alloc = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(alloc.Value);
        Assert.Equal(expectedAlloc, call.Function);
        Assert.True(call.ReturnsRef, "a multidim array is a reference (Il2CppArray*)");
        Assert.Equal(2, call.Args.Count);
        Assert.All(call.Args, a => Assert.Equal(2, Assert.IsType<IntConstExpr>(a).Value));
    }

    [Fact]
    public void Fold_MultidimCtor_ThreeRank_KeepsRankInName()
    {
        // `new DateTime[2,2,2]` -- the rank is read AFTER the element payload;
        // the value-element decode must consume exactly the type handle so the
        // rank/sizes/lobounds parse stays aligned.
        using var pe = SynthMultidim(SynthElem.DateTimeRef, rank: 3);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);

        var alloc = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(alloc.Value);
        Assert.Equal("mdnew_dt3", call.Function);
        Assert.Equal(3, call.Args.Count);
    }

    [Fact]
    public void Fold_MultidimCtor_UserStruct_KeepsLoud()
    {
        // A translated user struct has no GC reference-field bitmap channel
        // yet -- the allocation layer must keep rejecting it loudly (b5-gaps
        // residual), never silently mis-allocating.
        using var pe = SynthMultidim(SynthElem.UserStruct, rank: 2);
        var md = pe.GetMetadataReader();

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md));
        Assert.Contains("0x11", ex.Message);
        Assert.Contains("M3", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Harness: a synthetic PE whose Main is
    //   ldc.i4.2; [ldc.i4.2; ldc.i4.2 for rank 3]; newobj <T[0......]::.ctor>;
    //   pop; ret
    // with the ctor MemberRef parented on the multidim TypeSpec
    // (0x14 0x11 <elem handle> <rank> 00 00).
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var insns = IlDecoder.DecodeMethod(MainBody(pe, md), md);
        return StackFold.Fold(insns, new MethodContext { ArgCount = 0 }, md,
            Array.Empty<TypeDef>(), null, pe);
    }

    private static byte[] MainBody(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var body = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        return body!.GetILBytes()!;
    }

    private static PEReader SynthMultidim(SynthElem kind, int rank)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        EntityHandle elemHandle;
        switch (kind)
        {
            case SynthElem.DateTimeRef:
                elemHandle = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
                break;
            case SynthElem.TimeSpanRef:
                elemHandle = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("TimeSpan"));
                break;
            case SynthElem.GuidRef:
                elemHandle = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Guid"));
                break;
            case SynthElem.DateTimeOffsetRef:
                elemHandle = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTimeOffset"));
                break;
            case SynthElem.DecimalRef:
                elemHandle = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
                break;
            case SynthElem.EnumInt:
            case SynthElem.EnumByte:
            case SynthElem.EnumLong:
            {
                var enumRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Enum"));
                var underlying = kind switch
                {
                    SynthElem.EnumByte => (byte)0x05, // byte
                    SynthElem.EnumLong => (byte)0x0A, // long
                    _ => (byte)0x08,                  // int
                };
                var valueField = mdb.AddFieldDefinition(
                    FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                    mdb.GetOrAddString("value__"), mdb.GetOrAddBlob(new byte[] { 0x06, underlying }));
                elemHandle = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                    mdb.GetOrAddString("Cases"), mdb.GetOrAddString("SynthEnum"), enumRef, valueField,
                    default(MethodDefinitionHandle));
                break;
            }
            case SynthElem.UserStruct:
            {
                var vtRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("ValueType"));
                var dummyField = mdb.AddFieldDefinition(FieldAttributes.Public,
                    mdb.GetOrAddString("X"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
                elemHandle = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                    mdb.GetOrAddString("Cases"), mdb.GetOrAddString("SynthPt"), vtRef, dummyField,
                    default(MethodDefinitionHandle));
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        // The multidim ARRAY TypeSpec: 0x14 VALUETYPE(0x11) <elem> <rank> 00 00.
        var arrSig = new BlobBuilder();
        arrSig.WriteByte(0x14);
        arrSig.WriteByte(0x11);
        arrSig.WriteCompressedInteger(CodedIndex(elemHandle));
        arrSig.WriteCompressedInteger(rank);
        arrSig.WriteCompressedInteger(0); // numSizes
        arrSig.WriteCompressedInteger(0); // numLoBounds
        var arrSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(arrSig));

        // .ctor(int32, int32[, int32]) : void -- the multidim array ctor shape.
        var ctorSig = new BlobBuilder();
        ctorSig.WriteByte(0x00); // default calling convention, has this
        ctorSig.WriteCompressedInteger(rank);
        ctorSig.WriteByte(0x01); // void return
        for (var i = 0; i < rank; i++) ctorSig.WriteByte(0x08);
        var ctor = mdb.AddMemberReference(arrSpec, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(ctorSig));

        var field = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("s_dummy"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var nope = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Nope"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }), 0, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, field, nope);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        for (var i = 0; i < rank; i++) encoder.OpCode(ILOpCode.Ldc_i4_2);
        encoder.OpCode(ILOpCode.Newobj);
        encoder.Token(MetadataTokens.GetToken(ctor));
        encoder.OpCode(ILOpCode.Pop);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        _ = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static int CodedIndex(EntityHandle h)
    {
        var row = MetadataTokens.GetToken(h) & 0xFFFFFF;
        var tag = h.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentException($"handle kind {h.Kind}"),
        };
        return (row << 2) | tag;
    }
}
