using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// P2-HARDEN #3: DelegateInvokeExpr ResultKind width channel.
//
// A delegate Invoke whose result is a long/double/float is emitted through the
// FULL-WIDTH multicast helper (P-D2 #1: the helper returns int64_t / double /
// float). When that result lands on a COMPILER temp (not an IL-declared
// local -- e.g. the branch-merge join temp below), the temp's declaration is
// typed by ExprKind/TempSlotPass. Before this hardening, DelegateInvokeExpr
// only carried ReturnsRef, so a long/double/float result temp classified
// "val" and was declared int32_t -- truncating the widened helper result. The
// ResultKind ("l"/"d"/"float") makes the temp wide. Theoretical residual
// (measured no truncation -- named locals are IL-typed), but the contract is
// now held.
//
// The synthetic Main/Compute shape forces the residual: a ternary delegate
// invoke `flag ? d(5) : d(6)` crosses an unconditional branch with the invoke
// result ON the stack -> StackFold's join spill materializes
// `t0 = DelegateInvokeExpr` (a compiler temp). Delegate = long Invoke(long).
//------------------------------------------------------------------------------
public class DelegateInvokeResultKindTests
{
    [Fact]
    public void Fold_DelegateInvokeResultOnJoinTemp_CarriesLongResultKind()
    {
        using var pe = new PEReader(Synthesize());
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // At least one join temp holds the long-returning delegate invoke and
        // it MUST carry the "l" width (before the fix: null -> "val").
        var joinInvokes = stmts.OfType<AssignStmt>()
            .Where(a => a.Value is DelegateInvokeExpr)
            .Select(a => (DelegateInvokeExpr)a.Value)
            .ToList();
        Assert.NotEmpty(joinInvokes);
        Assert.All(joinInvokes, d => Assert.Equal("l", d.ResultKind));
    }

    [Fact]
    public void Generate_DelegateBranchMergeLongInvoke_EmitsInt64JoinTemp()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_drk_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, Synthesize().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // the long-returning delegate invoke routes through the full-width
            // multicast helper...
            Assert.Contains("il2cpp_delegate_multicast_", cpp);
            // ...and its result temp is declared int64_t, NOT int32_t (the
            // branch-merge join slot holds the helper's int64_t result).
            Assert.Contains("int64_t t0;", cpp);
            Assert.DoesNotContain("int32_t t0;", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Test harness
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var compute = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Compute");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(compute).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 2 }, md, DelegateTypes());
    }

    private static List<TypeDef> DelegateTypes() => new()
    {
        new TypeDef { Name = "MyDel", Namespace = "Cases", BaseTypeName = "System.MulticastDelegate" },
    };

    // Cases.MyDel : System.MulticastDelegate (runtime .ctor + long Invoke(long))
    // + Cases.Program with:
    //   static long Compute(MyDel d, bool flag) => flag ? d(5) : d(6);
    //   static void Main() {}
    private static MemoryStream Synthesize()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var mcDelRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("MulticastDelegate"));

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
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x0A, 0x0A }), // long Invoke(long)
            0, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("MyDel"), mcDelRef, default, ctor);

        // Compute IL: ldarg.1; brfalse.s L1; ldarg.0; ldc.i4.5; conv.i8;
        // callvirt Invoke; br.s L2; L1: ldarg.0; ldc.i4.6; conv.i8;
        // callvirt Invoke; L2: ret
        var il = new BlobBuilder();
        var stream = new MethodBodyStreamEncoder(il);
        var ce = new InstructionEncoder(il, new ControlFlowBuilder());
        var l1 = ce.DefineLabel();
        var l2 = ce.DefineLabel();
        ce.OpCode(ILOpCode.Ldarg_1);
        ce.Branch(ILOpCode.Brfalse_s, l1);
        ce.OpCode(ILOpCode.Ldarg_0);
        ce.OpCode(ILOpCode.Ldc_i4_5);
        ce.OpCode(ILOpCode.Conv_i8);
        Callvirt(ce, MetadataTokens.GetToken(invoke));
        ce.Branch(ILOpCode.Br_s, l2);
        ce.MarkLabel(l1);
        ce.OpCode(ILOpCode.Ldarg_0);
        ce.OpCode(ILOpCode.Ldc_i4_6);
        ce.OpCode(ILOpCode.Conv_i8);
        Callvirt(ce, MetadataTokens.GetToken(invoke));
        ce.MarkLabel(l2);
        ce.OpCode(ILOpCode.Ret);
        il.Align(4);
        var computeBodyOffset = stream.AddMethodBody(ce, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        // static long Compute(MyDel, bool): param types 0x12 + MyDel-coded-index, 0x02 (bool).
        var compute = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Compute"),
            mdb.GetOrAddBlob(ComputeSig()),
            computeBodyOffset, default(ParameterHandle));

        var me = new InstructionEncoder(il);
        me.OpCode(ILOpCode.Ret);
        il.Align(4);
        var mainBodyOffset = stream.AddMethodBody(me, 0, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            mainBodyOffset, default(ParameterHandle));

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, compute);

        return Serialize(mdb, il);
    }

    private static byte[] ComputeSig()
    {
        // static long Compute(MyDel, bool): header 0x00, 2 params, ret I8(0x0A),
        // param0 CLASS MyDel (TypeDefOrRef coded index = row 1 << 2 = 4 -- MyDel
        // is the first user TypeDef row), param1 bool (0x02).
        return new byte[] { 0x00, 0x02, 0x0A, 0x12, 0x04, 0x02 };
    }

    private static MemoryStream Serialize(MetadataBuilder mdb, BlobBuilder ilStream)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilStream);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    private static void Callvirt(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Callvirt);
        e.Token(token);
    }
}