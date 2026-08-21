using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Ir;
using Xunit;

// Delegate-support gaps (delechain work order):
//
//   A -- custom (non-generic) delegate Invoke: IsDelegateInvoke recognized
//   System.Func`N only, so callvirt on a user MulticastDelegate-derived
//   delegate's Invoke fell through to "callvirt owner X not in translated
//   types". A custom delegate's Invoke is a MemberRef whose parent is the
//   delegate TypeDefinition (Roslyn shape); the fold must recognize any
//   MulticastDelegate-derived delegate (base-class chain) AND construct it
//   through il2cpp_delegate_new (the Func`N-only newobj gate).
//
//   B -- delegate == / != (System.Delegate::op_Equality / op_Inequality):
//   the default resolver classifies op_* methods as INSTANCE (its heuristic
//   only knows get_/.ctor/.cctor) and the Delegate map has no op_* entries
//   -> the fold threw "pops this but the stack is empty". Operators are
//   ALWAYS static; == / != fold to the null-safe structural
//   icall_delegate_equals (a==b -> true, one-null -> false, else list
//   compare -- exactly the .NET op_Equality contract).
public class DelegateSupportTests
{
    [Fact]
    public void Generate_CustomDelegateInvoke_Int32Signature_RoutesThroughMulticastIcall()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeCustomDelegate().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // the custom delegate construction goes through il2cpp_delegate_new
            Assert.Contains("il2cpp_delegate_new", cpp);
            // int32-returning int32-arg invokes route through the multicast
            // icall: raw methodPtr dispatch would read a combine container's
            // `first` field (same offset as Il2CppDelegate::methodPtr) and
            // call a data pointer as code (0xC0000005 -- delegatecase crash).
            Assert.Contains("icall_multicast_invoke_i4", cpp);
            Assert.DoesNotContain("->methodPtr", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_CustomDelegateInvoke_RefArgSignature_KeepsMethodPtrDispatch()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeCustomDelegate(refArg: true).ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // ref-arg delegates have no runtime multicast icall -- the direct
            // methodPtr dispatch stays (single-cast only; multicast of those
            // signatures is a documented gap in il2cpp_delegate.h).
            Assert.Contains("->methodPtr", cpp);
            Assert.DoesNotContain("icall_multicast_invoke_i4", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_DelegateInstanceEquals_EmitsStructuralEqualsIcall()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeDelegateEqualsCall().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // `delegate.Equals(x)` is emitted by csc as callvirt
            // System.Object::Equals -- on a delegate-typed receiver the fold
            // must route it to the structural icall_delegate_equals (boxed
            // equality would compare identity/klass -- wrong: net48 compares
            // invocation lists).
            Assert.Contains("il2cpp::runtime::icall_delegate_equals", cpp);
            Assert.DoesNotContain("icall_boxed_equals_ext2", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_DelegateOpEquality_EmitsStructuralEqualsIcall()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeDelegateEquality("op_Equality").ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            Assert.Contains("il2cpp::runtime::icall_delegate_equals", cpp);
            // no bogus generated C++ name for the un-mapped operator
            Assert.DoesNotContain("System_Delegate_op_Equality", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_DelegateOpInequality_EmitsNegatedStructuralEquals()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeDelegateEquality("op_Inequality").ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            Assert.Contains("il2cpp::runtime::icall_delegate_equals", cpp);
            Assert.Contains("== 0", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_InstanceBoundDelegateNewobj_EmitsTwoArgWithTarget()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeInstanceBoundDelegate().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // instance binding: the delegate ctor's non-ldftn argument (the
            // bound instance) must reach the runtime as the second arg of
            // il2cpp_delegate_new -- dropping it (the pre-M3.5 1-arg shape)
            // makes the runtime invoke the instance ABI {ret} M(Il2CppObject*
            // this, ...) as if static (delegateinst e2e 0xC0000005).
            Assert.Contains("il2cpp_delegate_new(reinterpret_cast<void*>(&", cpp);
            // batch 177b: the type-id arg follows the target (il2cpp_delegate_new
            // (methodPtr, target, typeId) -- the heterogeneous-Combine guard).
            Assert.Matches("il2cpp_delegate_new\\(reinterpret_cast<void\\*>\\(&Cases_Counter_Inc[^)]*\\), loc0, [0-9]+ULL\\)", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_StaticBoundDelegateNewobj_EmitsExplicitNullTarget()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeCustomDelegate().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // static binding: the ldnull target still flows through the shape
            // as an explicit nullptr (runtime default; the shape is uniform so
            // the fold never distinguishes static vs instance). batch 177b:
            // the type-id arg follows (il2cpp_delegate_new(methodPtr, target,
            // typeId) -- the heterogeneous-Combine guard).
            Assert.Contains("il2cpp_delegate_new(reinterpret_cast<void*>(&", cpp);
            Assert.Matches("il2cpp_delegate_new\\(reinterpret_cast<void\\*>\\(&[^)]*\\), nullptr, [0-9]+ULL\\)", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_BclGenericPredicateNewobj_EmitsDelegateNewNotUndefinedSymbols()
    {
        // findidx D-findidx-2: `newobj System.Predicate<int>` is NOT folded by
        // StackFold.IsDelegateType (read-only Ir/StackFold.cs lists Func`/
        // Action`/ThreadStart/WaitCallback, NOT Predicate`1) -- the construction
        // falls to the generic alloc path. The emitter must STILL route it
        // through il2cpp_delegate_new (the ctor interception) with a discarded
        // nullptr placeholder -- never the undefined symbols
        // &System_Predicate_1_int_class / System_Predicate_1_int__ctor_<hash>.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeBclPredicateDelegate().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // the BCL generic delegate construction goes through
            // il2cpp_delegate_new -- never the generic alloc path.
            Assert.Contains("il2cpp_delegate_new", cpp);
            // no undefined descriptor symbol for the discarded placeholder
            Assert.DoesNotContain("System_Predicate_1_int_class", cpp);
            // no undefined ctor symbol
            Assert.DoesNotContain("System_Predicate_1_int__ctor_", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_BclGenericComparisonNewobj_EmitsDelegateNewNotUndefinedSymbols()
    {
        // cg8-b23 (icomparedeep): `newobj System.Comparison`1<int>` is NOT
        // folded by StackFold.IsDelegateType (read-only Ir/StackFold.cs lists
        // Func`/Action`/ThreadStart/WaitCallback, NOT Comparison`1) -- the
        // construction falls to the generic alloc path. The emitter must
        // STILL route it through il2cpp_delegate_new (the ctor interception
        // extended to System_Comparison_) with a discarded nullptr placeholder
        // -- never the undefined symbols &System_Comparison_1_int_class /
        // System_Comparison_1_int__ctor_<hash>. The delegate feeds the runtime
        // delegate-sort entries (arraysort.h il2cpp_array_sort_int32_comparison
        // / _ref_comparison).
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_del_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeBclComparisonDelegate().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // the BCL generic delegate construction goes through
            // il2cpp_delegate_new -- never the generic alloc path.
            Assert.Contains("il2cpp_delegate_new", cpp);
            // no undefined descriptor symbol for the discarded placeholder
            Assert.DoesNotContain("System_Comparison_1_int_class", cpp);
            // no undefined ctor symbol
            Assert.DoesNotContain("System_Comparison_1_int__ctor_", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Synthesis -- same pipeline as the GenericsSignatureTests: MetadataBuilder
    // + ManagedPEBuilder. Row 1 of the TypeRef table is System.Object
    // (coded index 0x05); System.Delegate/MulticastDelegate follow.
    //--------------------------------------------------------------------------

    // Cases.MyDelegate : System.MulticastDelegate + Cases.Program with a Main
    // that binds Inc to a MyDelegate (ldftn + newobj) and invokes it
    // (callvirt Invoke). The delegate's .ctor/Invoke are RUNTIME-provided
    // MethodDefs (MethodImplAttributes.Runtime, no IL body) -- the exact
    // Roslyn shape (ildasm: "runtime managed"); the transpiler must skip them
    // (no vtable/descriptor/method bodies) and route construction/Invoke
    // through the runtime delegate machinery.
    //
    // refArg: the Invoke takes System.Object instead of int32 (int32
    // Invoke(System.Object)) -- the ref-arg fallback emission path (raw
    // methodPtr dispatch, no runtime multicast icall for that signature).
    private static MemoryStream SynthesizeCustomDelegate(bool refArg = false)
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var mcDelRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("MulticastDelegate"));

        // MyDelegate's runtime-provided methods FIRST (the MethodDef table is
        // contiguous per type): .ctor(object, native int) + int32 Invoke(int32)
        // / int32 Invoke(System.Object).
        var ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x02, 0x01, 0x12, 0x05, 0x1C }),
            0, default(ParameterHandle));
        var invokeSig = refArg
            ? new byte[] { 0x20, 0x01, 0x08, 0x12, 0x05 } // int32 Invoke(System.Object)
            : new byte[] { 0x20, 0x01, 0x08, 0x08 };      // int32 Invoke(int32)
        var invoke = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString("Invoke"),
            mdb.GetOrAddBlob(invokeSig),
            0, default(ParameterHandle));
        var myDel = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("MyDelegate"), mcDelRef, default, ctor);

        // Program's methods: static int Inc(int x) => x; / static int
        // Ignore(System.Object) => 0; + Main.
        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var incEnc = new InstructionEncoder(new BlobBuilder());
        if (refArg)
        {
            incEnc.OpCode(ILOpCode.Ldc_i4_0); // Ignore(object) => 0
        }
        else
        {
            incEnc.OpCode(ILOpCode.Ldarg_0);  // Inc(int) => x
        }
        incEnc.OpCode(ILOpCode.Ret);
        var incBodyOffset = stream.AddMethodBody(incEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var incSig = refArg
            ? new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05 } // static int Ignore(System.Object)
            : new byte[] { 0x00, 0x01, 0x08, 0x08 };      // static int Inc(int32)
        var inc = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            refArg ? mdb.GetOrAddString("Ignore") : mdb.GetOrAddString("Inc"),
            mdb.GetOrAddBlob(incSig),
            incBodyOffset, default(ParameterHandle));

        // Main: ldnull; ldftn Inc; newobj MyDelegate::.ctor; stloc.0;
        //       ldloc.0; ldc.i4.1 / ldnull; callvirt Invoke; stloc.1; ret
        var mainEnc = new InstructionEncoder(new BlobBuilder());
        mainEnc.OpCode(ILOpCode.Ldnull);
        mainEnc.OpCode(ILOpCode.Ldftn);
        mainEnc.Token(MetadataTokens.GetToken(inc));
        mainEnc.OpCode(ILOpCode.Newobj);
        mainEnc.Token(MetadataTokens.GetToken(ctor));
        mainEnc.OpCode(ILOpCode.Stloc_0);
        mainEnc.OpCode(ILOpCode.Ldloc_0);
        if (refArg)
        {
            mainEnc.OpCode(ILOpCode.Ldnull);
        }
        else
        {
            mainEnc.OpCode(ILOpCode.Ldc_i4_1);
        }
        mainEnc.OpCode(ILOpCode.Callvirt);
        mainEnc.Token(MetadataTokens.GetToken(invoke));
        mainEnc.OpCode(ILOpCode.Stloc_1);
        mainEnc.OpCode(ILOpCode.Ret);
        var mainBodyOffset = stream.AddMethodBody(mainEnc, 4, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            mainBodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, inc);
        return Serialize(mdb, stream.Builder);
    }

        // Cases.Program.Main with a delegate receiver + `callvirt
    // System.Object::Equals(object)` (csc's shape for `delegate.Equals(x)`):
    //   ldnull; ldftn Inc; newobj MyDelegate::.ctor; stloc.0; ldloc.0;
    //   ldloc.0; callvirt Object::Equals; stloc.1; ret
    private static MemoryStream SynthesizeDelegateEqualsCall()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var mcDelRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("MulticastDelegate"));
        var equals = mdb.AddMemberReference(objRef, mdb.GetOrAddString("Equals"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x02, 0x12, 0x05 })); // instance bool Equals(System.Object)

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
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x08, 0x08 }),
            0, default(ParameterHandle));
        mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("MyDelegate"), mcDelRef, default, ctor);

        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var incEnc = new InstructionEncoder(new BlobBuilder());
        incEnc.OpCode(ILOpCode.Ldarg_0);
        incEnc.OpCode(ILOpCode.Ret);
        var incBodyOffset = stream.AddMethodBody(incEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var inc = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Inc"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x08, 0x08 }),
            incBodyOffset, default(ParameterHandle));

        var mainEnc = new InstructionEncoder(new BlobBuilder());
        mainEnc.OpCode(ILOpCode.Ldnull);
        mainEnc.OpCode(ILOpCode.Ldftn);
        mainEnc.Token(MetadataTokens.GetToken(inc));
        mainEnc.OpCode(ILOpCode.Newobj);
        mainEnc.Token(MetadataTokens.GetToken(ctor));
        mainEnc.OpCode(ILOpCode.Stloc_0);
        mainEnc.OpCode(ILOpCode.Ldloc_0);
        mainEnc.OpCode(ILOpCode.Ldloc_0);
        mainEnc.OpCode(ILOpCode.Callvirt);
        mainEnc.Token(MetadataTokens.GetToken(equals));
        mainEnc.OpCode(ILOpCode.Stloc_1);
        mainEnc.OpCode(ILOpCode.Ret);
        var mainBodyOffset = stream.AddMethodBody(mainEnc, 4, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            mainBodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, inc);
        return Serialize(mdb, stream.Builder);
    }

    // Cases.Program.Main: ldnull; ldnull; call <op>; stloc.0; ret -- the
        // exact shape of C# `delegate == delegate` / `delegate != delegate`
        // (the operator is ALWAYS declared on System.Delegate, never the
        // derived type). The result is STORED so the dead-temp optimizer
        // cannot eliminate the call.
        private static MemoryStream SynthesizeDelegateEquality(string opName)
        {
            var mdb = NewBuilder();
            var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
            var delRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Delegate"));
            var op = mdb.AddMemberReference(delRef, mdb.GetOrAddString(opName),
                mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x02, 0x12, 0x05, 0x12, 0x05 })); // static bool op(Delegate, Delegate)

            var stream = new MethodBodyStreamEncoder(new BlobBuilder());
            var mainEnc = new InstructionEncoder(new BlobBuilder());
            mainEnc.OpCode(ILOpCode.Ldnull);
            mainEnc.OpCode(ILOpCode.Ldnull);
            mainEnc.OpCode(ILOpCode.Call);
            mainEnc.Token(MetadataTokens.GetToken(op));
            mainEnc.OpCode(ILOpCode.Stloc_0);
            mainEnc.OpCode(ILOpCode.Ret);
        var bodyOffset = stream.AddMethodBody(mainEnc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, main);
        return Serialize(mdb, stream.Builder);
    }

    // Cases.Counter (ctor + instance int Inc(int)) + Cases.MyDelegate
    // (runtime-provided ctor(object, native int)/Invoke) + Cases.Program.Main:
    //   newobj Counter::.ctor; stloc.0; ldloc.0; ldftn Counter::Inc;
    //   newobj MyDelegate::.ctor; stloc.1; ret
    // -- the exact INSTANCE-method binding shape (`Counter c = new Counter();
    // MyDelegate d = c.Inc;`): the delegate ctor's first argument is the
    // bound instance, not ldnull.
    private static MemoryStream SynthesizeInstanceBoundDelegate()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var mcDelRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("MulticastDelegate"));

        var delCtor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x02, 0x01, 0x12, 0x05, 0x1C }),
            0, default(ParameterHandle));
        var invoke = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString("Invoke"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x08, 0x08 }),
            0, default(ParameterHandle));
        mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("MyDelegate"), mcDelRef, default, delCtor);

        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var ctorEnc = new InstructionEncoder(new BlobBuilder());
        ctorEnc.OpCode(ILOpCode.Ret);
        var ctorBodyOffset = stream.AddMethodBody(ctorEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }),
            ctorBodyOffset, default(ParameterHandle));
        var incEnc = new InstructionEncoder(new BlobBuilder());
        incEnc.OpCode(ILOpCode.Ldarg_1);
        incEnc.OpCode(ILOpCode.Ret);
        var incBodyOffset = stream.AddMethodBody(incEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var inc = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Inc"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x08, 0x08 }),
            incBodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Counter"), objRef, default, ctor);

        var mainEnc = new InstructionEncoder(new BlobBuilder());
        mainEnc.OpCode(ILOpCode.Newobj);
        mainEnc.Token(MetadataTokens.GetToken(ctor));
        mainEnc.OpCode(ILOpCode.Stloc_0);
        mainEnc.OpCode(ILOpCode.Ldloc_0);
        mainEnc.OpCode(ILOpCode.Ldftn);
        mainEnc.Token(MetadataTokens.GetToken(inc));
        mainEnc.OpCode(ILOpCode.Newobj);
        mainEnc.Token(MetadataTokens.GetToken(delCtor));
        mainEnc.OpCode(ILOpCode.Stloc_1);
        mainEnc.OpCode(ILOpCode.Ret);
        var mainBodyOffset = stream.AddMethodBody(mainEnc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            mainBodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, main);
        return Serialize(mdb, stream.Builder);
    }

    // Cases.Program.Inc(int) + Cases.Program.Main:
    //   ldnull; ldftn Inc; newobj <System.Predicate`1<int>::.ctor>; stloc.0; ret
    // -- the EXACT BCL generic delegate shape findidx exercises (ldftn + newobj
    // of a Predicate`1<int>, whose ctor MemberRef parent is the Predicate<int>
    // TypeSpec). StackFold.IsDelegateType does NOT fold this one, so the
    // emitter-side ctor interception must construct it via il2cpp_delegate_new.
    private static MemoryStream SynthesizeBclPredicateDelegate()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var predRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Predicate`1"));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x08 }));
        var ctor = mdb.AddMemberReference(typeSpec, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x02, 0x01, 0x12, 0x05, 0x1C })); // instance .ctor(object, native int)

        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var incEnc = new InstructionEncoder(new BlobBuilder());
        incEnc.OpCode(ILOpCode.Ldarg_0);
        incEnc.OpCode(ILOpCode.Ret);
        var incBodyOffset = stream.AddMethodBody(incEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var inc = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Inc"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x08, 0x08 }),
            incBodyOffset, default(ParameterHandle));

        var mainEnc = new InstructionEncoder(new BlobBuilder());
        mainEnc.OpCode(ILOpCode.Ldnull);
        mainEnc.OpCode(ILOpCode.Ldftn);
        mainEnc.Token(MetadataTokens.GetToken(inc));
        mainEnc.OpCode(ILOpCode.Newobj);
        mainEnc.Token(MetadataTokens.GetToken(ctor));
        mainEnc.OpCode(ILOpCode.Stloc_0);
        mainEnc.OpCode(ILOpCode.Ret);
        var mainBodyOffset = stream.AddMethodBody(mainEnc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            mainBodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, inc);
        return Serialize(mdb, stream.Builder);
    }

    // Cases.Program.Inc(int) + Cases.Program.Main:
    //   ldnull; ldftn Inc; newobj <System.Comparison`1<int>::.ctor>; stloc.0; ret
    // -- the EXACT BCL generic delegate shape icomparedeep exercises (ldftn +
    // newobj of a Comparison`1<int>, whose ctor MemberRef parent is the
    // Comparison<int> TypeSpec). StackFold.IsDelegateType does NOT fold this
    // one, so the emitter-side ctor interception must construct it via
    // il2cpp_delegate_new (the cg8-b23 extension to System_Comparison_).
    private static MemoryStream SynthesizeBclComparisonDelegate()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var cmpRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Comparison`1"));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x08 }));
        var ctor = mdb.AddMemberReference(typeSpec, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x02, 0x01, 0x12, 0x05, 0x1C })); // instance .ctor(object, native int)

        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var incEnc = new InstructionEncoder(new BlobBuilder());
        incEnc.OpCode(ILOpCode.Ldarg_0);
        incEnc.OpCode(ILOpCode.Ret);
        var incBodyOffset = stream.AddMethodBody(incEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var inc = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Inc"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x08, 0x08 }),
            incBodyOffset, default(ParameterHandle));

        var mainEnc = new InstructionEncoder(new BlobBuilder());
        mainEnc.OpCode(ILOpCode.Ldnull);
        mainEnc.OpCode(ILOpCode.Ldftn);
        mainEnc.Token(MetadataTokens.GetToken(inc));
        mainEnc.OpCode(ILOpCode.Newobj);
        mainEnc.Token(MetadataTokens.GetToken(ctor));
        mainEnc.OpCode(ILOpCode.Stloc_0);
        mainEnc.OpCode(ILOpCode.Ret);
        var mainBodyOffset = stream.AddMethodBody(mainEnc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            mainBodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, inc);
        return Serialize(mdb, stream.Builder);
    }

    private static MetadataBuilder NewBuilder()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        return mdb;
    }

    private static AssemblyReferenceHandle GetOrAddMscorlib(MetadataBuilder mdb) =>
        mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));

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
}
