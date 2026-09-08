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
// fnptr domain b23: C# 9.0 function pointers (`delegate*`) -- ELEMENT_TYPE_FNPTR
// (0x1B) decode + calli (0x29) fold + emission.
//
// Verified against real Roslyn output (fnptr probe):
//   `static int Call(delegate*<int,int,int> p) => p(3, 4);`
//     Call sig = 00 01 08 1B 00 02 08 08 08   (1 param: FNPTR int32(int32,int32))
//     IL      = ldarg.0; stloc.0; ldc.i4.3; ldc.i4.4; ldloc.0; calli <0x11 tok>; ret
//     StandAloneSig blob = 00 02 08 08 08     (default cc, 2 params, ret i4)
// The fnptr is pushed LAST (stack top); the args sit below it.
//------------------------------------------------------------------------------
public class FnPtrTests
{
    //--------------------------------------------------------------------------
    // IlDecoder: calli opcode (0x29) with its 4-byte StandAloneSig token.
    //--------------------------------------------------------------------------
    [Fact]
    public void Decode_Calli_TokenRead()
    {
        // 0x29 <tok: 0x11000002> 0x2A(ret)
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x29, 0x02, 0x00, 0x00, 0x11, 0x2A });
        var calli = Assert.Single(insns.Where(i => i.OpCode == ILOpCode.Calli));
        Assert.Equal(0, calli.Offset);
        Assert.Equal(0x11000002L, calli.Operand);
        Assert.Equal(IlOperandKind.MethodToken, calli.OperandKind);
        Assert.Equal(5, insns[1].Offset); // the trailing ret sits at 1 + 4 operand bytes
    }

    //--------------------------------------------------------------------------
    // MethodSignatureCoder: ELEMENT_TYPE_FNPTR (0x1B) in a METHOD signature
    // (a `delegate*` parameter) and the calli StandAloneSig decode.
    //--------------------------------------------------------------------------
    [Fact]
    public void Signature_FnPtrParameter_EncodesEmbeddedSig()
    {
        // Call sig: default cc, 1 param, ret i4, param FNPTR(int32(int32,int32))
        // (inner fnptr params joined with ';' -- ',' is the outer ParamTypes
        // separator consumed by the read-only MethodBodyReader/ParamTypesCpp
        // comma-splits, so a comma inside the fnptr encoding would fragment).
        var enc = EncodeMethodParamTypes(new byte[] { 0x00, 0x01, 0x08, 0x1B, 0x00, 0x02, 0x08, 0x08, 0x08 });
        Assert.Equal("F:i(i;i)", enc);
    }

    [Fact]
    public void Signature_StandaloneCalli_Decodes()
    {
        // calli StandAloneSig: default cc, 2 params, ret i4, params i4,i4
        var (conv, hasThis, paramCount, retType, paramTypes) =
            DecodeStandalone(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 });
        Assert.Equal(0, conv);
        Assert.False(hasThis);
        Assert.Equal(2, paramCount);
        Assert.Equal((byte)SignatureTypeCode.Int32, (byte)retType);
        Assert.Equal("i,i", paramTypes);
    }

    [Fact]
    public void Signature_StandaloneCalli_UnmanagedCdecl_ReportsConvention()
    {
        // Cdecl (0x01) calli sig: ret void, 1 param i4
        var (conv, hasThis, paramCount, _, _) =
            DecodeStandalone(new byte[] { 0x01, 0x01, 0x01, 0x08 });
        Assert.Equal(1, conv);
        Assert.False(hasThis);
        Assert.Equal(1, paramCount);
    }



    //--------------------------------------------------------------------------
    // StackFold: calli -> CallExpr{Function="calli_{token}", Args=[fnptr, 3, 4]}.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_Calli_PopsFnptrAndArgs()
    {
        // ldftn <Add>; ldc.i4.3; ldc.i4.4; calli <sigsig>; ret
        using var pe = SynthesizeMain(BodyKind.ValueReturn);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // The calli result is single-use (returned) -> OptimizeTemps inlines
        // the temp into the return: the CallExpr is either the AssignStmt's
        // value or the RetStmt's value.
        var call = (stmts.OfType<AssignStmt>().FirstOrDefault()?.Value
            ?? stmts.OfType<RetStmt>().FirstOrDefault()?.Value) as CallExpr;
        Assert.NotNull(call);
        Assert.StartsWith("calli_", call.Function);
        // Args: [fnptr(FuncPtrExpr), 3, 4]
        Assert.IsType<FuncPtrExpr>(call.Args[0]);
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(call.Args[1]).Value);
        Assert.Equal(4L, Assert.IsType<IntConstExpr>(call.Args[2]).Value);
    }

    [Fact]
    public void Fold_Calli_NullFnptr_PushesNullExpr()
    {
        // ldnull; ldc.i4.3; ldc.i4.4; calli <sigsig>; ret
        using var pe = SynthesizeMain(BodyKind.NullFnptr);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var call = (stmts.OfType<AssignStmt>().FirstOrDefault()?.Value
            ?? stmts.OfType<RetStmt>().FirstOrDefault()?.Value) as CallExpr;
        Assert.NotNull(call);
        Assert.IsType<NullExpr>(call.Args[0]);
    }

    [Fact]
    public void Fold_Calli_VoidReturn_EmitsCallStmt()
    {
        // ldftn <Add>; ldc.i4.3; ldc.i4.4; calli <void sig>; ret
        using var pe = SynthesizeMain(BodyKind.VoidReturn);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var callStmt = Assert.Single(stmts.OfType<CallStmt>());
        Assert.StartsWith("calli_", callStmt.Function);
        Assert.Equal(3, callStmt.Args.Count);
    }

    //--------------------------------------------------------------------------
    // CodeGenerator full pipeline: emitted C++ renders the indirect call with
    // the target function-pointer type and the null guard.
    //--------------------------------------------------------------------------
    [Fact]
    public void Emit_Calli_RendersIndirectCallAndNullGuard()
    {
        using var pe = SynthesizeMain(BodyKind.ValueReturn);
        var cpp = GenerateMain(pe);

        // ((int32_t(*)(int32_t, int32_t))fnptr)(3, 4) with a null guard
        Assert.Contains("(*)(int32_t, int32_t)", cpp);
        Assert.Contains("calli", cpp);
        Assert.Contains("NullReferenceException", cpp);
        Assert.Contains("il2cpp_throw_new", cpp);
        // no vtable/delegate route
        Assert.DoesNotContain("methodPtr", cpp);
        Assert.DoesNotContain("->klass->vtable", cpp);
    }

    [Fact]
    public void Emit_Calli_VoidReturn_NoResultTemp()
    {
        using var pe = SynthesizeMain(BodyKind.VoidReturn);
        var cpp = GenerateMain(pe);

        Assert.Contains("(*)(int32_t, int32_t)", cpp);
        Assert.Contains("il2cpp_throw_new", cpp);
    }

    //--------------------------------------------------------------------------
    // Test harness.
    //--------------------------------------------------------------------------
    private enum BodyKind
    {
        ValueReturn,  // ldftn Add; ldc.i4.3; ldc.i4.4; calli int32(int32,int32); ret
        NullFnptr,    // ldnull; ldc.i4.3; ldc.i4.4; calli int32(int32,int32); ret
        VoidReturn,   // ldftn Add; ldc.i4.3; ldc.i4.4; calli void(int32,int32); ret
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

    private static PEReader SynthesizeMain(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("fnptr.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        // Both Add and Main bodies must live in ONE MethodBodyStreamEncoder
        // (the PE image carries a single method-body stream; offsets from a
        // second builder would point outside it -> MethodBodyBlock.Create
        // throws when CodeGenerator reads Add's body).
        var bodyStream = new MethodBodyStreamEncoder(new BlobBuilder());

        // Add(int,int):int -- the ldftn target (user method with a body).
        var addEnc = new InstructionEncoder(new BlobBuilder());
        addEnc.OpCode(ILOpCode.Ldarg_0);
        addEnc.OpCode(ILOpCode.Ldarg_1);
        addEnc.OpCode(ILOpCode.Add);
        addEnc.OpCode(ILOpCode.Ret);
        var addBodyOffset = bodyStream.AddMethodBody(addEnc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var add = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Add"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 }),
            addBodyOffset, default(ParameterHandle));

        // calli StandAloneSigs: int32(int32,int32) and void(int32,int32).
        var valueSig = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 }));
        var voidSig = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x01, 0x08, 0x08 }));
        var addToken = MetadataTokens.GetToken(add);
        var valueSigToken = MetadataTokens.GetToken(valueSig);
        var voidSigToken = MetadataTokens.GetToken(voidSig);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.ValueReturn:
                // ECMA-335 III.3.19 calli: stack bottom->top = [args..., fnptr]
                // -- the fnptr is pushed LAST (on top). Push args first.
                encoder.OpCode(ILOpCode.Ldc_i4_3);
                encoder.OpCode(ILOpCode.Ldc_i4_4);
                LdFtn(encoder, addToken);
                Calli(encoder, valueSigToken);
                break;
            case BodyKind.NullFnptr:
                encoder.OpCode(ILOpCode.Ldc_i4_3);
                encoder.OpCode(ILOpCode.Ldc_i4_4);
                encoder.OpCode(ILOpCode.Ldnull);
                Calli(encoder, valueSigToken);
                break;
            case BodyKind.VoidReturn:
                encoder.OpCode(ILOpCode.Ldc_i4_3);
                encoder.OpCode(ILOpCode.Ldc_i4_4);
                LdFtn(encoder, addToken);
                Calli(encoder, voidSigToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        var bodyOffset = bodyStream.AddMethodBody(encoder, 4, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = main;

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default(FieldDefinitionHandle), add);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, bodyStream.Builder);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static string GenerateMain(PEReader pe)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_fnptr_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, pe.GetEntireImage().GetContent(0, pe.GetEntireImage().Length).ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_fnptrgen_" + Guid.NewGuid().ToString("N"));
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

    // MethodSignatureCoder.Decode is (paramCount, returnType, paramTypes); we
    // want the encoded PARAM TYPES only -- decode through a synthesized
    // MemberRef signature blob via a tiny PE.
    private static string EncodeMethodParamTypes(byte[] methodSig)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("sig.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString("M"), mdb.GetOrAddBlob(methodSig));
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        using var pe = new PEReader(new MemoryStream(blob.ToArray()));
        var md = pe.GetMetadataReader();
        var mr = md.GetMemberReference(md.MemberReferences.Single());
        var (_, _, paramTypes, _) = MethodSignatureCoder.Decode(md, mr.Signature);
        return paramTypes;
    }

    private static (int, bool, int, SignatureTypeCode, string) DecodeStandalone(byte[] sig)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("sig.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var ss = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(sig));
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        using var pe = new PEReader(new MemoryStream(blob.ToArray()));
        var md = pe.GetMetadataReader();
        var handle = md.GetTableRowCount(TableIndex.StandAloneSig) > 0
            ? MetadataTokens.StandaloneSignatureHandle(1)
            : default(StandaloneSignatureHandle);
        var result = MethodSignatureCoder.DecodeStandalone(md, md.GetStandaloneSignature(handle).Signature);
        return (result.CallingConvention, result.HasThis, result.ParamCount, result.ReturnType, result.ParamTypes);
    }

    private static void LdFtn(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Ldftn);
        e.Token(token);
    }

    private static void Calli(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Calli);
        e.Token(token);
    }
}
