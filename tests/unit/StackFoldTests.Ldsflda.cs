using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// Ldsflda decode + static-field-address fold (work order #10 -- the 0x7F gap).
// `"A:" + order` (order a static int) compiles to
//   ldsflda int32 Program::order; call instance string Int32::ToString()
// -- the byref receiver is the static field's ADDRESS. StackFold had Ldflda
// (FieldAddrExpr) but no Ldsflda, so every such method died with
// "fold: opcode Ldsflda not supported in M3" (delegate2: 5 sites).
// The fold mirrors Ldflda's ADDRESS semantics (StaticFieldAddrExpr -> &storage):
// the resolver then routes the receiver through the "deref:i" arm
// (icall_int32_to_string((*(int32_t*)(&storage)))) -- a LOAD of the field
// value -- exactly the Ldflda/Ldfld receiver-shape contract. Folding to a
// plain value instead would misroute to "unbox:i" (a pointer cast of the
// int32 value -- garbage).
//------------------------------------------------------------------------------
public class StackFoldTestsLdsflda
{
    [Fact]
    public void Decode_Ldsflda_ReadsFieldToken()
    {
        // ldsflda <0x04000001>; ret -- 0x7F + 4-byte field token. The decoder
        // reads the token LITTLE-endian (the InstructionEncoder writes the
        // token bytes in the same order), so the byte sequence is
        // 01 00 00 04.
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x7F, 0x01, 0x00, 0x00, 0x04, 0x2A });

        var ldsflda = Assert.Single(insns, i => i.OpCode == ILOpCode.Ldsflda);
        Assert.Equal(IlOperandKind.FieldToken, ldsflda.OperandKind);
        Assert.Equal(0x04000001, ldsflda.Operand);
    }

    [Fact]
    public void Fold_Ldsflda_ProducesStaticFieldAddrExpr()
    {
        // pure-IR fold (no metadata): the token operand becomes the
        // FieldName placeholder -- the fold must NOT hit the LOUD default.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x7F, 0x01, 0x00, 0x00, 0x04, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var addr = Assert.IsType<StaticFieldAddrExpr>(ret.Value);
        Assert.Equal("field_67108865", addr.FieldName);
    }

    [Fact]
    public void Fold_Ldsflda_ThenInt32ToString_ResolvesDerefReceiver()
    {
        // The delegate2 shape: ldsflda <static int order>; call instance
        // string Int32::ToString(). The address receiver must resolve the
        // parameterless ToString through the "deref:i" arm -- a LOAD of the
        // static field value (not "unbox:i", which would pointer-cast the
        // int32 value).
        using var pe = Synthesize(useLdsflda: true);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // the call result is materialized into a temp: t0 = icall_...; ret t0
        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(assign.Value);
        Assert.Equal("il2cpp::runtime::icall_int32_to_string", call.Resolved!.CppName);
        Assert.Equal("deref:i", call.Resolved.ParamTypeNames![0]);
        var recv = Assert.IsType<StaticFieldAddrExpr>(Assert.Single(call.Args));
        Assert.Equal("Cases.Base.order", recv.FieldName);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.IsType<LocExpr>(ret.Value);
    }

    [Fact]
    public void Fold_Ldsfld_ThenInt32ToString_ResolvesUnboxReceiver_Control()
    {
        // control: a VALUE receiver (ldsfld, not ldsflda) keeps the existing
        // boxed-receiver routing ("unbox:i") -- the address fold must be the
        // ONLY thing that flips the cast arm.
        using var pe = Synthesize(useLdsflda: false);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // same materialized-temp shape as the ldsflda test
        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(assign.Value);
        Assert.Equal("il2cpp::runtime::icall_int32_to_string", call.Resolved!.CppName);
        Assert.Equal("unbox:i", call.Resolved.ParamTypeNames![0]);
        Assert.IsType<StaticFieldExpr>(Assert.Single(call.Args));
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.IsType<LocExpr>(ret.Value);
    }

    //--------------------------------------------------------------------------
    // Test harness: a synthetic PE with a static int field "order" on
    // Cases.Base and a MemberRef System.Int32::ToString() (instance, string
    // return) -- the minimal delegate2 ldsflda shape.
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());
    }

    private static PEReader Synthesize(bool useLdsflda)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var stringRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("String"));
        var int32Ref = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int32"));

        // instance string Int32::ToString() -- sig: 0x20 instance, 0 params,
        // ret CLASS<System.String>
        var toStrSig = new BlobBuilder();
        toStrSig.WriteByte(0x20);
        toStrSig.WriteCompressedInteger(0);
        toStrSig.WriteByte(0x12);
        toStrSig.WriteCompressedInteger(CodedIndex(stringRef));
        var toStr = mdb.AddMemberReference(int32Ref, mdb.GetOrAddString("ToString"),
            mdb.GetOrAddBlob(toStrSig));

        // Cases.Base with a static int32 field "order" (sig: FIELD 0x06, I4 0x08)
        var field = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("order"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));

        var fieldToken = MetadataTokens.GetToken(field);
        var toStrToken = MetadataTokens.GetToken(toStr);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(useLdsflda ? ILOpCode.Ldsflda : ILOpCode.Ldsfld);
        encoder.Token(fieldToken);
        encoder.OpCode(ILOpCode.Call);
        encoder.Token(toStrToken);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        // Main must be registered BEFORE the type so it lands inside the
        // type's method range (a TypeDef's method list = first row of its
        // range; an empty method list makes GetMethods() return nothing).
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, field, main);

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
            _ => throw new ArgumentException($"handle kind {h.Kind}"),
        };
        return (row << 2) | tag;
    }
}
