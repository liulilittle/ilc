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
// Ldflda decode + INSTANCE-field-address fold (D-NEST-2 -- nesteddeep GAPS.md).
// `Tag + ":" + V` (V an int instance FIELD) compiles to
//   ldarg.0; ldflda int32 Cases.Inner::V; call instance string Int32::ToString()
// -- the byref receiver is the FIELD's ADDRESS (&obj->V). StackFold routes the
// address receiver through the resolver's receiverIsAddress flag, which
// recognized LocAddrExpr/StaticFieldAddrExpr but NOT FieldAddrExpr -- so the
// address fell into the "unbox:i" arm:
//   il2cpp_unbox_int32(static_cast<Il2CppObject*>(&obj->V))
// -- an ill-formed static_cast from int32_t* to Il2CppObject* (the field
// address treated as a boxed object). The fix folds FieldAddrExpr into the
// "deref:i" arm:
//   (*reinterpret_cast<int32_t*>(&obj->V))
// -- a LOAD of the field VALUE, the exact Ldflda/Ldsflda receiver contract.
// These tests pin the fold; the control test keeps the VALUE-receiver
// (ldfld, not ldflda) on the existing "unbox:i" boxed-receiver routing.
//------------------------------------------------------------------------------
public class StackFoldTestsLdflda
{
    [Fact]
    public void Fold_Ldflda_ThenInt32ToString_ResolvesDerefReceiver()
    {
        // The D-NEST-2 shape: ldarg.0; ldflda <int instance field>;
        // call instance string Int32::ToString(). The ADDRESS receiver must
        // resolve the parameterless ToString through the "deref:i" arm -- a
        // LOAD of the field value (NOT "unbox:i", which would pointer-cast
        // the int32_t* address to Il2CppObject* -- an ill-formed static_cast).
        using var pe = Synthesize(useLdflda: true);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // the call result is materialized into a temp: t0 = icall_...; ret t0
        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(assign.Value);
        Assert.Equal("il2cpp::runtime::icall_int32_to_string", call.Resolved!.CppName);
        Assert.Equal("deref:i", call.Resolved.ParamTypeNames![0]);
        var recv = Assert.IsType<FieldAddrExpr>(Assert.Single(call.Args));
        Assert.Equal("V", recv.FieldCppName);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.IsType<LocExpr>(ret.Value);
    }

    [Fact]
    public void Fold_Ldfld_ThenInt32ToString_ResolvesUnboxReceiver_Control()
    {
        // control: a VALUE receiver (ldfld, not ldflda) keeps the existing
        // boxed-receiver routing ("unbox:i") -- the address fold must be the
        // ONLY thing that flips the cast arm.
        using var pe = Synthesize(useLdflda: false);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // same materialized-temp shape as the ldflda test
        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(assign.Value);
        Assert.Equal("il2cpp::runtime::icall_int32_to_string", call.Resolved!.CppName);
        Assert.Equal("unbox:i", call.Resolved.ParamTypeNames![0]);
        Assert.IsType<FieldExpr>(Assert.Single(call.Args));
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.IsType<LocExpr>(ret.Value);
    }

    //--------------------------------------------------------------------------
    // Test harness: a synthetic PE with an instance int field "V" on
    // Cases.Base and a MemberRef System.Int32::ToString() (instance, string
    // return) -- the minimal D-NEST-2 ldflda shape (Describe reads this via
    // ldarg.0, ArgCount=1).
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Describe");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 1 }, md, Array.Empty<TypeDef>());
    }

    private static PEReader Synthesize(bool useLdflda)
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

        // Cases.Base with an INSTANCE int32 field "V" (sig: FIELD 0x06, I4 0x08)
        var field = mdb.AddFieldDefinition(FieldAttributes.Private,
            mdb.GetOrAddString("V"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));

        var fieldToken = MetadataTokens.GetToken(field);
        var toStrToken = MetadataTokens.GetToken(toStr);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ldarg_0);
        encoder.OpCode(useLdflda ? ILOpCode.Ldflda : ILOpCode.Ldfld);
        encoder.Token(fieldToken);
        encoder.OpCode(ILOpCode.Call);
        encoder.Token(toStrToken);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        // Describe must be registered BEFORE the type so it lands inside the
        // type's method range (a TypeDef's method list = first row of its
        // range; an empty method list makes GetMethods() return nothing).
        var describe = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Describe"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, field, describe);

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
