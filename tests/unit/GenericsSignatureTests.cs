using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using Xunit;

// Generics signature support (genlist work order A+B). The byte shapes come
// from real genlist.exe dumps:
//
//   A -- VAR (0x13) / MVAR (0x1E) index bytes: the byte after either element
//   type is the compressed generic-parameter index and MUST be consumed.
//     * Id<T> MethodDef  : 10 01 01 1E 00 1E 00 (ret MVAR+0x00, param MVAR+0x00)
//     * get_Item MemberRef: 20 01 13 00 08    (ret VAR+0x00, param int32)
//     * Add MemberRef    : 20 01 01 13 00     (param VAR+0x00 -- survived
//       only because VAR was the last param)
//   Without the consume, the index byte falls into the parameter loop and
//   EncodeType throws -- ANY assembly with a generic method fails at load
//   (MetadataLoader.cs -> MethodSignatureCoder.Decode).
//
//   B -- GenericInst (0x15): in-signature form is 0x15 + TypeDefOrRef +
//   count + args. The TypeSpec TABLE blob additionally carries the
//   ELEMENT_TYPE_CLASS (0x12) marker before the coded index
//   (List<int> TypeSpec: 15 12 35 01 08) -- the isinst/castclass gate must
//   skip it, else ReadTypeHandle misreads and the reader crashes instead of
//   rejecting loudly.
public class GenericsSignatureTests
{
    //--------------------------------------------------------------------------
    // A -- VAR/MVAR index-byte consumption
    //--------------------------------------------------------------------------

    [Fact]
    public void Decode_GenericMethod_MvarIndexBytesConsumed()
    {
        // static T Id<T>(T x): header 0x10 (static|generic), genParamCount 1,
        // paramCount 1, ret MVAR(0x1E)+0x00, param MVAR(0x1E)+0x00.
        var (pc, ret, pt) = DecodeMember(SynthesizeMember("Cases", "GenList", "Id",
            new byte[] { 0x10, 0x01, 0x01, 0x1E, 0x00, 0x1E, 0x00 }), "Id");

        Assert.Equal(1, pc);
        Assert.Equal((SignatureTypeCode)0x1E, ret);
        Assert.Equal("i", pt);
    }

    [Fact]
    public void Decode_VarReturn_IndexByteConsumed()
    {
        // T get_Item(int32): ret VAR(0x13)+0x00, param int32 -- the VAR
        // return's index byte must not leak into the parameter loop.
        var (pc, ret, pt) = DecodeMember(SynthesizeMember("System.Collections.Generic", "List`1", "get_Item",
            new byte[] { 0x20, 0x01, 0x13, 0x00, 0x08 }), "get_Item");

        Assert.Equal(1, pc);
        Assert.Equal((SignatureTypeCode)0x13, ret);
        Assert.Equal("i", pt);
    }

    [Fact]
    public void Decode_VarParam_TrailingParamsAligned()
    {
        // void Set(T t, int i): params VAR(0x13)+0x00, int32 -- if the VAR
        // index byte is not consumed, the int32 param is misread as 0x00.
        var (pc, _, pt) = DecodeMember(SynthesizeMember("Cases", "Box`1", "Set",
            new byte[] { 0x20, 0x02, 0x01, 0x13, 0x00, 0x08 }), "Set");

        Assert.Equal(2, pc);
        Assert.Equal("i,i", pt);
    }

    [Fact]
    public void LoadAssembly_GenericMethodAssembly_LoadsWithoutCrash()
    {
        // A real assembly containing `static T Id<T>(T x)` must LOAD (the
        // load-time MVAR decode is the blocking failure before this fix)
        // and TRANSPILE (the declaration's MVAR return maps to int32_t --
        // the call site still rejects loudly in the resolver).
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_genlist_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeGenericMethod().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            var genList = Assert.Single(types);
            var id = Assert.Single(genList.Methods, m => m.Name == "Id");
            Assert.Equal(1, id.ParamCount);
            Assert.Equal("i", id.ParamTypes);

            CodeGenerator.Generate(types, dir, tmp);
            Assert.Contains("Cases_GenList_Id_", File.ReadAllText(Path.Combine(dir, "src", "Cases_GenList.cpp")));
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // B -- GenericInst decoding + loud (not crashing) rejection
    //--------------------------------------------------------------------------

    [Fact]
    public void Decode_GenericInstParam_Decodes()
    {
        // void M(List<int> x): param GenericInst(0x15) + ELEMENT_TYPE_CLASS
        // (0x12) marker + TypeRef coded index (row 1 -> 0x05) + count 1 +
        // int32 arg -- the real delechain add_Changed shape
        // (15 12 35 02 08 08: EventHandler`1 with two int args).
        var (pc, _, pt) = DecodeMember(SynthesizeMember("System.Collections.Generic", "List`1", "M",
            new byte[] { 0x20, 0x01, 0x01, 0x15, 0x12, 0x05, 0x01, 0x08 }), "M");

        Assert.Equal(1, pc);
        Assert.Contains("System.Collections.Generic.List`1", pt);
    }

    [Fact]
    public void Fold_CallvirtOnGenericInstanceMember_ResolvesToListI4GetCount()
    {
        // List<int>::get_Count MemberRef (TypeSpec parent, sig: header 0x20
        // HAS_THIS -- an INSTANCE getter, count 0, ret int32). The generic-
        // instance MemberRef must NOT crash the fold: the List<int32> AOT
        // expansion IS wired (MethodMap.List.cs), so get_Count resolves to
        // the il2cpp_list_i4_get_count icall. This is the correct-expansion
        // path, not the loud rejection the pre-wiring era demanded (the
        // unwired generic-instance rejection contract now lives where a
        // non-int32 instantiation reaches -- TryMapList's i4Receiver Loud()).
        using var pe = new PEReader(SynthesizeGenericMember("get_Count", new byte[] { 0x20, 0x00, 0x08 }));
        var md = pe.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "get_Count");
        var token = MetadataTokens.GetToken(handle);
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[]
            {
                0x02, // ldarg.0 (this)
                0x6F, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
                (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF), // callvirt <token>
                0x26, 0x2A, // pop; ret
            }),
            new MethodContext { ArgCount = 1 }, md, Array.Empty<TypeDef>());

        var call = Assert.Single(stmts.OfType<AssignStmt>().Select(a => a.Value).OfType<CallExpr>());
        Assert.Equal("il2cpp::il2cpp_list_i4_get_count", call.Resolved!.CppName);
        Assert.True(call.Resolved.IsInstance);
    }

    [Fact]
    public void Generate_IsinstGenericTypeSpec_RejectsLoudlyNotCrash()
    {
        // isinst <TypeSpec List<int>>: the TypeSpec blob carries the
        // ELEMENT_TYPE_CLASS (0x12) marker before the TypeDefOrRef coded
        // index. The M5 cast gate must skip it -- otherwise ReadTypeHandle
        // misreads the marker and the metadata reader crashes (out of
        // bounds) instead of the loud "not wired in M5" rejection.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_genlist_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeIsinst().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            var ex = Assert.Throws<NotSupportedException>(() => CodeGenerator.Generate(types, dir, tmp));
            Assert.Contains("not wired in M5", ex.Message);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the MethodMap
    // tests: MetadataBuilder + ManagedPEBuilder)
    //--------------------------------------------------------------------------

    private static (int ParamCount, SignatureTypeCode ReturnType, string ParamTypes) DecodeMember(
        MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        var (pc, ret, pt, _) = MethodSignatureCoder.Decode(md, md.GetMemberReference(handle).Signature);
        return (pc, ret, pt);
    }

    // One TypeRef (row 1 -> coded index 0x05) + one MemberRef carrying the
    // signature blob.
    private static MemoryStream SynthesizeMember(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = NewBuilder();
        var typeRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    // A TypeDef `Cases.GenList` with a MethodDef `static T Id<T>(T x)` -- the
    // load-level generic-method shape.
    private static MemoryStream SynthesizeGenericMethod()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ldarg_0); // T x
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var id = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Id"),
            mdb.GetOrAddBlob(new byte[] { 0x10, 0x01, 0x01, 0x1E, 0x00, 0x1E, 0x00 }),
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("GenList"), objRef, default, id);
        return Serialize(mdb, il);
    }

    // TypeSpec List<int> (Object TypeRef row 1 -> 0x05, List`1 TypeRef row 2
    // -> 0x09) + a MemberRef whose parent is the TypeSpec.
    private static MemoryStream SynthesizeGenericMember(string memberName, byte[] signature)
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var listRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb),
            mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("List`1"));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x08 }));
        mdb.AddMemberReference(typeSpec, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    // A TypeDef with a Main body `ldnull; isinst <TypeSpec>; ret` -- the
    // isinst/castclass TypeSpec emission gate.
    private static MemoryStream SynthesizeIsinst()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var listRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb),
            mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("List`1"));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x08 }));
        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ldnull);
        encoder.OpCode(ILOpCode.Isinst);
        encoder.Token(0x1B000001); // isinst <TypeSpec 1B000001>
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("GenList"), objRef, default, main);
        return Serialize(mdb, il);
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

    private static MemoryStream Serialize(MetadataBuilder mdb, BlobBuilder? ilStream = null)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilStream ?? new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
