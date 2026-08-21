using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// rv=false fold stack balance (collchain G4). .NET call semantics ALWAYS
// push the result of a non-void signature onto the evaluation stack,
// regardless of whether the caller uses it -- the caller's `pop` discards
// it. A mapping with ReturnsValue=false (the C API returns void, e.g.
// ArrayList::Add -> il2cpp_arraylist_add which returns nothing) must still
// mirror that IL stack slot: the fold pops args + this and emits the
// CallStmt, then pushes a DiscardedValueExpr marker standing for the
// dropped result. Without it the shadow stack desyncs by one slot:
//   newobj; dup; ldstr "a"; callvirt Add; pop; dup   (collchain ildump
//   0512/0517/0518/051D/0522/0523)
// the `pop` discards the fold's LIVE list copy and the following `dup`
// throws "dup on empty stack at offset 0x0523".
//------------------------------------------------------------------------------
public class StackFoldTestsRvFalse
{
    [Fact]
    public void Fold_NewobjDupAddPopDup_StackBalanced()
    {
        // newobj ArrayList::.ctor; dup; ldstr "a"; callvirt Add; pop; dup; pop; pop; ret
        using var pe = Synthesize(BodyKind.NewobjDupAddPopDup);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        Assert.Equal(4, stmts.Count); // newobj assign + ctor call + add call + ret
        Assert.IsType<AssignStmt>(stmts[0]);
        Assert.Equal(2, stmts.OfType<CallStmt>().Count());
        Assert.IsType<RetStmt>(stmts[^1]);
        Assert.DoesNotContain(stmts, s => ContainsMarker(s));
    }

    [Fact]
    public void Fold_NewobjDupVoidCallPop_NoDiscardSlot()
    {
        // control: a VOID IL signature (ArrayList::Clear) must NOT push a
        // discard slot -- IL leaves no value after the call.
        // newobj; dup; callvirt Clear; pop; ret
        using var pe = Synthesize(BodyKind.NewobjDupClearPop);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        Assert.Equal(4, stmts.Count); // newobj assign + ctor call + clear call + ret
        Assert.DoesNotContain(stmts, s => ContainsMarker(s));
    }

    [Fact]
    public void Fold_MultiAddChain_CollchainShape_Balanced()
    {
        // the collchain ArrayList section shape:
        // newobj; dup; ldstr "a"; Add; pop; dup; ldstr "b"; Add; pop; dup;
        // ldc.i4.3; box; Add; pop; pop; ret
        using var pe = Synthesize(BodyKind.MultiAddChain);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        Assert.Equal(7, stmts.Count); // newobj + ctor + 3 adds + box assign + ret
        Assert.Equal(4, stmts.OfType<CallStmt>().Count());
        Assert.DoesNotContain(stmts, s => ContainsMarker(s));
    }

    [Fact]
    public void Fold_StoreOfDiscardedAddResult_CarriesDiscardMarker()
    {
        // contract: `int n = list.Add(x)` -- the caller USES the value the
        // rv=false mapping cannot produce. The fold must NOT silently emit a
        // wrong value: the DiscardedValueExpr marker lands in the store and
        // the emitter rejects it loudly (unknown IR expr, CodeGenerator) --
        // the map's "callers must discard" contract, loud instead of silent.
        // newobj; dup; ldstr "a"; callvirt Add; stloc.0; ret
        using var pe = Synthesize(BodyKind.StoreAddResult);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var store = Assert.IsType<AssignStmt>(stmts[3]);
        Assert.IsType<StackFold.DiscardedValueExpr>(store.Value);
    }

    //--------------------------------------------------------------------------
    // Test harness.
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

    private static bool ContainsMarker(Stmt s) => s switch
    {
        AssignStmt a => a.Value is StackFold.DiscardedValueExpr,
        CallStmt c => c.Args.Any(a => a is StackFold.DiscardedValueExpr),
        _ => false,
    };

    private enum BodyKind
    {
        NewobjDupAddPopDup,  // newobj; dup; ldstr; callvirt Add; pop; dup; pop; pop; ret
        NewobjDupClearPop,   // newobj; dup; callvirt Clear; pop; ret
        MultiAddChain,       // 3-add chain with box (collchain shape)
        StoreAddResult,      // ...; callvirt Add; stloc.0; ret
    }

    private sealed record RvFalseTokens
    {
        public required int ArrayListCtor { get; init; }
        public required int ArrayListAdd { get; init; }
        public required int ArrayListClear { get; init; }
        public required int Int32Box { get; init; }
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var int32Ref = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int32"));
        var arrRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System.Collections"),
            mdb.GetOrAddString("ArrayList"));

        // ArrayList::.ctor() : void -- instance, 0 params (sig: inst cc 0x20,
        // count 0, ret void 0x01)
        var ctorSig = new BlobBuilder();
        ctorSig.WriteByte(0x20);
        ctorSig.WriteCompressedInteger(0);
        ctorSig.WriteByte(0x01);
        var ctor = mdb.AddMemberReference(arrRef, mdb.GetOrAddString(".ctor"), mdb.GetOrAddBlob(ctorSig));
        // ArrayList::Add(object) : int32 -- instance, 1 param (sig: inst cc
        // 0x20, count 1, ret i4 0x08, param CLASS 0x12 + coded index)
        var addSig = new BlobBuilder();
        addSig.WriteByte(0x20);
        addSig.WriteCompressedInteger(1);
        addSig.WriteByte(0x08);
        addSig.WriteByte(0x12);
        addSig.WriteCompressedInteger(CodedIndex(objRef));
        var add = mdb.AddMemberReference(arrRef, mdb.GetOrAddString("Add"), mdb.GetOrAddBlob(addSig));
        // ArrayList::Clear() : void -- instance, 0 params
        var clearSig = new BlobBuilder();
        clearSig.WriteByte(0x20);
        clearSig.WriteCompressedInteger(0);
        clearSig.WriteByte(0x01);
        var clear = mdb.AddMemberReference(arrRef, mdb.GetOrAddString("Clear"), mdb.GetOrAddBlob(clearSig));

        var a = mdb.GetOrAddUserString("a");
        var b = mdb.GetOrAddUserString("b");

        var tokens = new RvFalseTokens
        {
            ArrayListCtor = MetadataTokens.GetToken(ctor),
            ArrayListAdd = MetadataTokens.GetToken(add),
            ArrayListClear = MetadataTokens.GetToken(clear),
            Int32Box = MetadataTokens.GetToken(int32Ref),
        };

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.NewobjDupAddPopDup:
                Newobj(encoder, tokens.ArrayListCtor);
                encoder.OpCode(ILOpCode.Dup);
                encoder.LoadString(a);
                Callvirt(encoder, tokens.ArrayListAdd);
                encoder.OpCode(ILOpCode.Pop);
                encoder.OpCode(ILOpCode.Dup);
                encoder.OpCode(ILOpCode.Pop);
                encoder.OpCode(ILOpCode.Pop);
                break;
            case BodyKind.NewobjDupClearPop:
                Newobj(encoder, tokens.ArrayListCtor);
                encoder.OpCode(ILOpCode.Dup);
                Callvirt(encoder, tokens.ArrayListClear);
                encoder.OpCode(ILOpCode.Pop);
                break;
            case BodyKind.MultiAddChain:
                Newobj(encoder, tokens.ArrayListCtor);
                encoder.OpCode(ILOpCode.Dup);
                encoder.LoadString(a);
                Callvirt(encoder, tokens.ArrayListAdd);
                encoder.OpCode(ILOpCode.Pop);
                encoder.OpCode(ILOpCode.Dup);
                encoder.LoadString(b);
                Callvirt(encoder, tokens.ArrayListAdd);
                encoder.OpCode(ILOpCode.Pop);
                encoder.OpCode(ILOpCode.Dup);
                encoder.OpCode(ILOpCode.Ldc_i4_3);
                encoder.OpCode(ILOpCode.Box);
                encoder.Token(tokens.Int32Box);
                Callvirt(encoder, tokens.ArrayListAdd);
                encoder.OpCode(ILOpCode.Pop);
                encoder.OpCode(ILOpCode.Pop);
                break;
            case BodyKind.StoreAddResult:
                Newobj(encoder, tokens.ArrayListCtor);
                encoder.OpCode(ILOpCode.Dup);
                encoder.LoadString(a);
                Callvirt(encoder, tokens.ArrayListAdd);
                encoder.OpCode(ILOpCode.Stloc_0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        // the single TypeDef anchors Main's method row (methodList = Main) --
        // GetMethods on it must return Main (Ldtoken harness pattern).
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Holder"), objRef,
            default(FieldDefinitionHandle), main);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static void Newobj(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Newobj);
        e.Token(token);
    }

    private static void Callvirt(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Callvirt);
        e.Token(token);
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
