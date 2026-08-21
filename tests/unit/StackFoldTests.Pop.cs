using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// sk6 (delegate2 e2e root cause): a discarded NON-VOID delegate Invoke in
// statement position (`try { ex(); }` -- IntFn returns int) was silently
// dropped by the IL `pop`. StackFold's Pop case popped the DelegateInvokeExpr
// off the stack without emitting any statement, so the whole call vanished
// (delegate2 gen showed an empty try body). The void-delegate path emits a
// dinvoke_ CallStmt directly at the EmitCall site; the value-returning path
// pushes the DelegateInvokeExpr LAZILY (Push materializes only
// CallExpr/VirtualCallExpr/NewObjExpr into temps), so the pop must now
// materialize it as the same dinvoke_ CallStmt -- preserving the call + its
// exception semantics. Pure value expressions (constants, arithmetic) remain
// dead and droppable.
//------------------------------------------------------------------------------
public class StackFoldTestsPop
{
    [Fact]
    public void Fold_DiscardedValueReturningDelegateInvoke_EmitsDinvokeCallStmt()
    {
        // Main: ldnull; callvirt IntFn::Invoke (returns int32); pop; ret
        using var pe = SynthesizeDelegateMain();
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // The invoke must be preserved as a statement (call + exception
        // semantics), not silently dropped by the pop.
        var dinvoke = Assert.Single(stmts.OfType<CallStmt>());
        Assert.StartsWith("dinvoke_", dinvoke.Function);
        Assert.IsType<NullExpr>(Assert.Single(dinvoke.Args)); // the receiver
        Assert.IsType<RetStmt>(stmts[^1]);
        // The result must NOT be stored into a temp (it is discarded).
        Assert.DoesNotContain(stmts, s => s is AssignStmt a && a.Value is DelegateInvokeExpr);
    }

    [Fact]
    public void Fold_DiscardedPureValueArithmetic_EmitsNoStatement()
    {
        // ldc.i4.3; ldc.i4.4; add; pop; ret -- the constant arithmetic result
        // is a PURE value, so the pop must drop it without emitting anything.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x18, 0x1A, 0x58, 0x26, 0x2A }),
            new MethodContext { ArgCount = 0 });

        Assert.DoesNotContain(stmts, s => s is CallStmt);
        Assert.DoesNotContain(stmts, s => s is AssignStmt);
        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        Assert.Null(ret.Value); // nothing left on the stack to return
    }

    [Fact]
    public void Fold_DiscardedPureConstant_EmitsNoStatement()
    {
        // ldc.i4.7; pop; ret -- a bare constant discard.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x1C, 0x26, 0x2A }),
            new MethodContext { ArgCount = 0 });

        Assert.DoesNotContain(stmts, s => s is CallStmt);
        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        Assert.Null(ret.Value);
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
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, DelegateTypes());
    }

    // The model TypeDef list must declare the custom delegate so
    // StackFold.IsDelegateType's base-class chain reaches
    // System.MulticastDelegate (the TypeRef-only metadata has no TypeDef for
    // it, mirroring the real assembly load of a user delegate).
    private static List<TypeDef> DelegateTypes() => new()
    {
        new TypeDef { Name = "IntFn", Namespace = "Cases", BaseTypeName = "System.MulticastDelegate" },
    };

    // Cases.IntFn : System.MulticastDelegate (runtime-provided .ctor + int32
    // Invoke() MethodDefs, the exact Roslyn delegate shape) + Cases.Program
    // with a Main that invokes a null delegate in statement position
    // (ldnull; callvirt Invoke; pop; ret).
    private static PEReader SynthesizeDelegateMain()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var mcDelRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("MulticastDelegate"));

        // IntFn's runtime-provided methods: .ctor(object, native int) +
        // int32 Invoke() -- both abstract (RVA=0), skipped by emission.
        var ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x02, 0x01, 0x12, 0x05, 0x1C }),
            0, default(ParameterHandle));
        var invoke = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString("Invoke"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x08 }), // int32 Invoke()
            0, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("IntFn"), mcDelRef, default, ctor);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ldnull);
        Callvirt(encoder, MetadataTokens.GetToken(invoke));
        encoder.OpCode(ILOpCode.Pop);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), default,
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

    private static void Callvirt(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Callvirt);
        e.Token(token);
    }
}
