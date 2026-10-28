using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// Decimal PURE-VALUE comparison (and the arithmetic family) takes the
// `const Il2CppDecimal*` icall parameters, so the emitter ADDRESSES each
// operand slot. A decimal slot is a 16-byte Il2CppDecimal STRUCT: an lvalue
// slot can be addressed directly (`&(loc)`), but an un-materialized call
// result (here a delegate Invoke -- the only decimal-producing expression the
// fold leaves inline) is a PRVALUE and `&(prvalue)` is ill-formed C++
// ("taking address of rvalue", gcc -fpermissive).
//
// Contract (the GuidValueBytePtr precedent): a NON-lvalue decimal operand is
// bound to a const reference first, which materializes a temporary whose
// lifetime covers the full icall expression and IS addressable:
//   (&static_cast<const il2cpp::runtime::Il2CppDecimal&>(<prvalue>))
// An lvalue operand keeps the plain `(&(<lvalue>))` form, and an ADDRESS
// operand (ldloca / ldsflda) keeps passing through UNCAST (its rendering is
// already `&x` -- addressing it again would be a double indirection).
public class DecimalPrvalueArgTests
{
    [Fact]
    public void Generate_DecimalEquality_DelegateInvokeOperands_BindsAddressableReference()
    {
        // decimal f() == decimal f() -- both operands are delegate Invoke
        // results, i.e. prvalues reaching the "dec" argument cast.
        var cpp = Generate(DecimalOpEquality);
        Assert.Contains("il2cpp::runtime::icall_decimal_equals(", cpp);
        // Both operands bind through the const reference (materialized temp).
        Assert.Contains("icall_decimal_equals((&static_cast<const il2cpp::runtime::Il2CppDecimal&>(", cpp);
        // Never the ill-formed address of the invoke expression itself.
        Assert.DoesNotContain("(&(il2cpp_delegate", cpp);
    }

    [Fact]
    public void Generate_DecimalAddition_DelegateInvokeOperands_BindsAddressableReference()
    {
        // decimal f() + decimal f(): the decimal-producing icall takes the
        // RESULT slot first, then the two operand ADDRESSES -- both prvalues
        // bind through the const reference.
        var cpp = Generate(DecimalOpAddition);
        Assert.Contains("il2cpp::runtime::icall_decimal_add((&", cpp);
        Assert.Contains("static_cast<const il2cpp::runtime::Il2CppDecimal&>(", cpp);
        Assert.DoesNotContain("(&(il2cpp_delegate", cpp);
    }

    [Fact]
    public void Generate_DecimalEquality_LocalOperands_KeepsDirectSlotAddress()
    {
        // Control (no over-application): two decimal LOCALS are lvalues -- the
        // plain slot address stays, with no const-reference temporary.
        var cpp = Generate(DecimalOpLocalsEquality);
        Assert.Contains("icall_decimal_equals((&(loc1)), (&(loc2)))", cpp);
        Assert.DoesNotContain("static_cast<const il2cpp::runtime::Il2CppDecimal&>", cpp);
    }

    [Fact]
    public void Generate_DecimalCompareTo_CapturedFieldAddress_PassesThroughWithoutDoubleAddress()
    {
        // A closure-captured decimal lives in the display class field, so an
        // instance CompareTo on it arrives as ldflda -- an ADDRESS expression
        // that already renders `(&x->d)`. Wrapping it in another address-of is
        // `&(&x->d)`: address of an rvalue pointer, ill-formed C++
        // ("lvalue required as unary '&' operand").
        var cpp = GenerateCapturedField();
        Assert.Contains("icall_decimal_compare((&((reinterpret_cast<", cpp);
        Assert.DoesNotContain("(&(&", cpp);
    }

    // The decimal operator the synthetic Main applies to its two operands.
    private const string DecimalOpEquality = "op_Equality";
    private const string DecimalOpAddition = "op_Addition";
    private const string DecimalOpLocalsEquality = "locals";

    // Synthesized assembly:
    //   Cases.MyDecimalFunc : System.MulticastDelegate  (decimal Invoke())
    //   Cases.Program.Get() : decimal  (decimal 1m)
    //   Cases.Program.Main():
    //     ldnull; ldftn Get; newobj MyDecimalFunc::.ctor; stloc.0
    //     <operands>; call/hold; ret
    // The delegate Invoke result is used DIRECTLY as the operator operand
    // (never stored into a local), which is exactly the un-materialized
    // prvalue shape.
    private static string Generate(string op)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_decprv_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, Synthesize(op).ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            return File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static MemoryStream Synthesize(string op)
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var mcDelRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("MulticastDelegate"));
        // TypeRef row 3 -> TypeDefOrRef coded index (3 << 2 | 1) = 0x0D.
        var decRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        const byte dec = 0x0D;

        // MyDecimalFunc's runtime-provided methods FIRST (MethodDef rows are
        // contiguous per type): .ctor(object, native int) + decimal Invoke().
        var delCtor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x02, 0x01, 0x12, 0x05, 0x1C }),
            0, default(ParameterHandle));
        var delInvoke = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString("Invoke"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x11, dec }),
            0, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("MyDecimalFunc"), mcDelRef, default, delCtor);

        var stream = new MethodBodyStreamEncoder(new BlobBuilder());

        // decimal Get() => 1m;  ldc.i4.1; newobj Decimal::.ctor(int32); ret
        var decCtor = mdb.AddMemberReference(decRef, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x01, 0x08 }));
        var getEnc = new InstructionEncoder(new BlobBuilder());
        getEnc.OpCode(ILOpCode.Ldc_i4_1);
        getEnc.OpCode(ILOpCode.Newobj);
        getEnc.Token(MetadataTokens.GetToken(decCtor));
        getEnc.OpCode(ILOpCode.Ret);
        var getBody = stream.AddMethodBody(getEnc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var get = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Get"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x11, dec }),
            getBody, default(ParameterHandle));

        // The operator under test.
        var mainEnc = new InstructionEncoder(new BlobBuilder());
        mainEnc.OpCode(ILOpCode.Ldnull);
        mainEnc.OpCode(ILOpCode.Ldftn);
        mainEnc.Token(MetadataTokens.GetToken(get));
        mainEnc.OpCode(ILOpCode.Newobj);
        mainEnc.Token(MetadataTokens.GetToken(delCtor));
        mainEnc.OpCode(ILOpCode.Stloc_0);

        if (op == DecimalOpLocalsEquality)
        {
            // Control: materialized decimal LOCALS (lvalues).
            mainEnc.OpCode(ILOpCode.Ldloc_0);
            mainEnc.OpCode(ILOpCode.Callvirt);
            mainEnc.Token(MetadataTokens.GetToken(delInvoke));
            mainEnc.OpCode(ILOpCode.Stloc_1);
            mainEnc.OpCode(ILOpCode.Ldloc_0);
            mainEnc.OpCode(ILOpCode.Callvirt);
            mainEnc.Token(MetadataTokens.GetToken(delInvoke));
            mainEnc.OpCode(ILOpCode.Stloc_2);
            mainEnc.OpCode(ILOpCode.Ldloc_1);
            mainEnc.OpCode(ILOpCode.Ldloc_2);
        }
        else
        {
            // Prvalue operands: the Invoke results feed the operator directly.
            mainEnc.OpCode(ILOpCode.Ldloc_0);
            mainEnc.OpCode(ILOpCode.Callvirt);
            mainEnc.Token(MetadataTokens.GetToken(delInvoke));
            mainEnc.OpCode(ILOpCode.Ldloc_0);
            mainEnc.OpCode(ILOpCode.Callvirt);
            mainEnc.Token(MetadataTokens.GetToken(delInvoke));
        }

        var decOp = mdb.AddMemberReference(decRef,
            mdb.GetOrAddString(op == DecimalOpAddition ? DecimalOpAddition : DecimalOpEquality),
            mdb.GetOrAddBlob(op == DecimalOpAddition
                ? new byte[] { 0x00, 0x02, 0x11, dec, 0x11, dec, 0x11, dec }
                : new byte[] { 0x00, 0x02, 0x08, 0x11, dec, 0x11, dec }));
        mainEnc.OpCode(ILOpCode.Call);
        mainEnc.Token(MetadataTokens.GetToken(decOp));
        mainEnc.OpCode(ILOpCode.Stloc_3);
        mainEnc.OpCode(ILOpCode.Ret);
        var mainBody = stream.AddMethodBody(mainEnc, 4, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            mainBody, default(ParameterHandle));

        _ = objRef;
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, get);
        return Serialize(mdb, stream.Builder);
    }

// Synthesized assembly (the closure-captured decimal shape):
    //   Cases.Program                      (CompareCaptured(DisplayClass))
    //     + DisplayClass { decimal d }     (nested -- the display-class layout)
    //   CompareCaptured: ldarg.0; ldflda DisplayClass::d; ldc.i4.5;
    //                    newobj Decimal::.ctor(int32);
    //                    call instance int32 Decimal::CompareTo(decimal); ret
    private static string GenerateCapturedField()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_decprv_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeCapturedField().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            return File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static MemoryStream SynthesizeCapturedField()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        // TypeRef row 2 -> coded index (2 << 2 | 1) = 0x09.
        var decRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        const byte dec = 0x09;

        var decCtor = mdb.AddMemberReference(decRef, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x01, 0x08 }));
        var cmpTo = mdb.AddMemberReference(decRef, mdb.GetOrAddString("CompareTo"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x08, 0x11, dec }));

        // The display-class field (Field row 1, a DECIMAL value slot).
        var field = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("d"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, dec }));
        // DisplayClass is TypeDef row 2 -> TypeDef coded index (2 << 2) = 0x08.
        var fieldRef = mdb.AddMemberReference(MetadataTokens.TypeDefinitionHandle(2),
            mdb.GetOrAddString("d"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, dec }));

        var enc = new InstructionEncoder(new BlobBuilder());
        enc.OpCode(ILOpCode.Ldarg_0);
        enc.OpCode(ILOpCode.Ldflda);
        enc.Token(MetadataTokens.GetToken(fieldRef));
        enc.OpCode(ILOpCode.Ldc_i4_5);
        enc.OpCode(ILOpCode.Newobj);
        enc.Token(MetadataTokens.GetToken(decCtor));
        enc.OpCode(ILOpCode.Call);
        enc.Token(MetadataTokens.GetToken(cmpTo));
        enc.OpCode(ILOpCode.Ret);
        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var body = stream.AddMethodBody(enc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var cmp = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("CompareCaptured"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x08, 0x12, 0x08 }),
            body, default(ParameterHandle));

        // TypeDef rows: <Module> 1, DisplayClass 2 (one field, no methods),
        // Program 3 (one method, no fields). The Field/Method lists must stay
        // non-decreasing across the rows, so DisplayClass (field row 1) is
        // declared before Program (which owns no fields).
        var display = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("DisplayClass"), objRef,
            field, MetadataTokens.MethodDefinitionHandle(1));
        var program = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef,
            MetadataTokens.FieldDefinitionHandle(2), cmp);
        mdb.AddNestedType(display, program);
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

