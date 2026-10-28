using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// `ldloca <slot>; ldstr "s"; call System.Guid::.ctor(string)` (the Roslyn
// lowering of `g = new Guid("s")` on a LOCAL -- a plain call, NOT newobj).
// The mapping (MethodMap.Guid.cs, commit 4f1cde64) carried rv=true, but the
// C wrapper guid.h:107 is `inline void il2cpp_guid_ctor_string(uint8_t[16],
// Il2CppString*)` -- a VOID output-slot writer (the same ABI as
// il2cpp_guid_ctor_bytes, which maps rv=false). The fold's generic call arm
// (StackFold: `resolved.ReturnsValue -> Push(call)`) then materializes a
// stray temp onto the shadow stack: the IL ctor call is pop2/push0, so the
// stack depth drifts +1 and any later join (SpillAtBranch -> RegisterJoin)
// dies with "branch stack height != registered" -- the b5-gaps 13 residual
// that forced multidim-vt to bypass new Guid(string) with Guid.Parse.
//
// GREEN contract: rv=false. The ctor folds to a VOID CallStmt (statement
// form, no temp), PushDiscardSlot consults IlCallReturnsValue (the ctor
// signature is ELEMENT_TYPE_VOID) and mirrors NOTHING -- the stack stays
// pop2/push0, exactly the IL contract.
//------------------------------------------------------------------------------
public class StackFoldTestsGuidCtorString
{
    [Fact]
    public void Fold_GuidCtorStringCall_EmitsVoidCallStmt_NoTemp()
    {
        // ldloca.s 0; ldstr "x"; call instance void System.Guid::.ctor(string); ret
        using var pe = Synthesize(GuidCtorBody.Linear);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // The ctor call rides STATEMENT form (void -- no value to keep).
        var callStmts = stmts.OfType<CallStmt>()
            .Where(s => s.Resolved?.CppName == "il2cpp::il2cpp_guid_ctor_string").ToList();
        Assert.Single(callStmts);
        // The receiver is the ldloca ADDRESS; the string arg is slot 1.
        Assert.IsType<LocAddrExpr>(callStmts[0].Args[0]);
        // NO temp materialization: the old rv=true shape assigned the call to
        // a stray tN and pushed it (`tN = il2cpp::il2cpp_guid_ctor_string(...)`)
        // -- exactly the +1 the IL stack never had.
        Assert.DoesNotContain(stmts, st => st is AssignStmt a
            && a.Value is CallExpr c && c.Resolved?.CppName == "il2cpp::il2cpp_guid_ctor_string");
    }

    [Fact]
    public void Fold_GuidCtorStringCall_IfElseJoin_StackStaysBalanced()
    {
        // The join-height reproduction (the real b5-gaps 13 shape):
        //   if (b) { g = new Guid("x"); ... } else { ... }
        // The then-branch's br.s END spills the STRAY ctor value the old
        // rv=true fold left on the stack (join height 1); the else branch
        // reaches END empty (height 0) -> RegisterJoin throws
        // "branch stack height 0 != registered 1". rv=false keeps both
        // branches at 0.
        //
        //   ldc.i4.1; brfalse.s ELSE
        //   ldloca.s 0; ldstr "x"; call Guid::.ctor(string); (stack must be EMPTY here)
        //   ldc.i4.4; stloc.1; br.s END
        //   ELSE: ldc.i4.5; stloc.1
        //   END: ldloc.1; ret
        using var pe = Synthesize(GuidCtorBody.IfElseJoin);
        var md = pe.GetMetadataReader();

        // RED (rv=true): NotSupportedException "join ...: branch stack height 0 != registered 1".
        // GREEN (rv=false): the fold completes with both join heights at 0.
        var stmts = Fold(pe, md);
        Assert.NotEmpty(stmts);
    }

    [Fact]
    public void Fold_GuidCtorBytesCall_Control_VoidCallStmtUnchanged()
    {
        // Control: Guid(byte[]) already mapped rv=false (MethodMap.Guid.cs:144)
        // -- the shape the fix must NOT disturb. ldloca.s 0; ldnull; call
        // Guid::.ctor(byte[]); ret folds to the same void CallStmt statement
        // form and no temp.
        using var pe = Synthesize(GuidCtorBody.BytesControl);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var callStmts = stmts.OfType<CallStmt>()
            .Where(s => s.Resolved?.CppName == "il2cpp::il2cpp_guid_ctor_bytes").ToList();
        Assert.Single(callStmts);
        Assert.DoesNotContain(stmts, st => st is AssignStmt a
            && a.Value is CallExpr c && c.Resolved?.CppName == "il2cpp::il2cpp_guid_ctor_bytes");
    }

    //--------------------------------------------------------------------------
    // Test harness.
    //--------------------------------------------------------------------------
    private enum GuidCtorBody
    {
        Linear,       // ldloca.s 0; ldstr; call .ctor(string); ret
        IfElseJoin,   // the join-height shape above
        BytesControl, // ldloca.s 0; ldnull; call .ctor(byte[]); ret
    }

    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());
    }

    private static PEReader Synthesize(GuidCtorBody kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var guidRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Guid"));

        // instance void Guid::.ctor(string) -- HASTHIS, 1 param, void, string.
        var ctorStr = mdb.AddMemberReference(guidRef, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x01, 0x0E }));
        // instance void Guid::.ctor(uint8[]) -- HASTHIS, 1 param, void,
        // SZArray(u1) (0x1D is a modifier -- the byte element 0x05 follows).
        var ctorBytes = mdb.AddMemberReference(guidRef, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x01, 0x1D, 0x05 }));
        var us = mdb.GetOrAddUserString("x");

        // local sigs: (Guid) and (Guid, int32). VALUETYPE = 0x11 + the Guid
        // TypeRef coded index (row 1, TypeRef tag 1 -> 0x05); int32 = 0x08.
        var localsGuid = mdb.AddStandaloneSignature(
            mdb.GetOrAddBlob(new byte[] { 0x07, 0x01, 0x11, 0x05 }));
        var localsGuidInt = mdb.AddStandaloneSignature(
            mdb.GetOrAddBlob(new byte[] { 0x07, 0x02, 0x11, 0x05, 0x08 }));

        var dummyField = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("s_dummy"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));

        var il = new BlobBuilder();
        var stream = new MethodBodyStreamEncoder(il);
        var ce = new InstructionEncoder(il, new ControlFlowBuilder());
        var maxStack = 4;
        switch (kind)
        {
            case GuidCtorBody.Linear:
                ce.LoadLocalAddress(0);
                ce.LoadString(us);
                ce.OpCode(ILOpCode.Call);
                ce.Token(MetadataTokens.GetToken(ctorStr));
                break;
            case GuidCtorBody.BytesControl:
                ce.LoadLocalAddress(0);
                ce.OpCode(ILOpCode.Ldnull);
                ce.OpCode(ILOpCode.Call);
                ce.Token(MetadataTokens.GetToken(ctorBytes));
                break;
            case GuidCtorBody.IfElseJoin:
                {
                    var lElse = ce.DefineLabel();
                    var lEnd = ce.DefineLabel();
                    ce.OpCode(ILOpCode.Ldc_i4_1);
                    ce.Branch(ILOpCode.Brfalse_s, lElse);
                    ce.LoadLocalAddress(0);
                    ce.LoadString(us);
                    ce.OpCode(ILOpCode.Call);
                    ce.Token(MetadataTokens.GetToken(ctorStr));
                    ce.OpCode(ILOpCode.Ldc_i4_4);
                    ce.OpCode(ILOpCode.Stloc_1);
                    ce.Branch(ILOpCode.Br_s, lEnd);
                    ce.MarkLabel(lElse);
                    ce.OpCode(ILOpCode.Ldc_i4_5);
                    ce.OpCode(ILOpCode.Stloc_1);
                    ce.MarkLabel(lEnd);
                    ce.OpCode(ILOpCode.Ldloc_1);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ce.OpCode(ILOpCode.Ret);
        il.Align(4);
        var localsHandle = kind == GuidCtorBody.IfElseJoin ? localsGuidInt : localsGuid;
        var bodyOffset = stream.AddMethodBody(ce, maxStack, localsHandle, MethodBodyAttributes.InitLocals);
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("GuidCtorStr"), objRef, dummyField,
            mdb.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
                mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle)));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }
}
