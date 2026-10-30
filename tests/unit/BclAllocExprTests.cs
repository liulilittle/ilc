using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// BclAllocExpr wiring: a List<int> newobj must allocate through the runtime
// listi4 descriptor (il2cpp::il2cpp_list_i4_class). Before the entry was
// added, the table fallthrough emitted
// il2cpp_object_new(&System_Collections_Generic_List_1_int_class) -- an
// undefined symbol that broke the generated C++ link (genlist probe: the
// transpiler emits no descriptor for BCL types).
public class BclAllocExprTests
{
    [Fact]
    public void Generate_NewListInt_AllocatesThroughListI4Class()
    {
        // Cases.GenList.Main: newobj <List`1<int> ::.ctor>; pop; ret -- the
        // MemberRef ctor's parent is the List<int> TypeSpec, so the fold
        // names the instantiation "System.Collections.Generic.List`1_int".
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_bclalloc_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeNewListInt().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var generated = File.ReadAllText(Path.Combine(dir, "src", "Cases_GenList.cpp"));
            Assert.Contains("il2cpp_object_new(il2cpp::il2cpp_list_i4_class())", generated);
            Assert.DoesNotContain("System_Collections_Generic_List_1_int_class", generated);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_NewObject_AllocatesThroughSystemObjectClass()
    {
        // Cases.GenList.Main: newobj <System.Object::.ctor>; pop; ret --
        // TypeCppName maps "System.Object" to "Il2CppObject", so WITHOUT the
        // BclAllocExpr entry the fallback emits il2cpp_object_new(&Il2CppObject_class)
        // -- an undefined symbol (the runtime descriptor is System_Object_class,
        // extern in il2cpp_metadata.h). gccase/valuesem `new object()` probes
        // hit this.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_bclalloc_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeNewObject().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var generated = File.ReadAllText(Path.Combine(dir, "src", "Cases_GenList.cpp"));
            Assert.Contains("il2cpp_object_new(&System_Object_class)", generated);
            Assert.DoesNotContain("Il2CppObject_class", generated);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("System", "Decimal")]
    [InlineData("System", "DateTime")]
    [InlineData("System", "TimeSpan")]
    [InlineData("System", "Guid")]
    [InlineData("System", "DateTimeOffset")]
    public void Generate_NewListOfBclValueStruct_AllocatesThroughListObjClass(string ns, string name)
    {
        // p0linq4 (D1) registered the listobj *_dec/_dt/_ts/_guid/_dtoffset
        // member forwarders for these closed value-struct element
        // instantiations, but the ALLOCATION table was never updated:
        // DottedRefArg excludes the closed BCL value-type names, so `new
        // List<decimal>()` fell through to the generated-descriptor fallback
        // il2cpp_object_new(&System_Collections_Generic_List_1_System_Decimal_class)
        // -- an undefined symbol (probe-listvtalloc: gcc "was not declared in
        // this scope" for all five families). Every value element is BOXED
        // into the shared listobj reference array, so ONE descriptor serves
        // them all.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_bclalloc_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeNewListOfValueStruct(ns, name).ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var generated = File.ReadAllText(Path.Combine(dir, "src", "Cases_GenList.cpp"));
            Assert.Contains("il2cpp_object_new(il2cpp::il2cpp_list_obj_class())", generated);
            Assert.DoesNotContain("System_Collections_Generic_List_1_" + ns + "_" + name + "_class", generated);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // TypeRefs: System.Object (row 1 -> coded 0x05), List`1 (row 2 -> 0x09),
    // the ELEMENT type (row 3 -> coded 0x0D); TypeSpec List<elem>
    // [15 12 09 01 11 0D] (0x11 = VALUETYPE element); ctor MemberRef
    // (parent = TypeSpec, sig 20 00 01).
    private static MemoryStream SynthesizeNewListOfValueStruct(string elemNamespace, string elemName)
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var listRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb),
            mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("List`1"));
        var elemRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb),
            mdb.GetOrAddString(elemNamespace), mdb.GetOrAddString(elemName));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x11, 0x0D }));
        var ctor = mdb.AddMemberReference(typeSpec, mdb.GetOrAddString(".ctor"), mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));
        return BuildMainBody(mdb, ctor);
    }

    // TypeRefs: System.Object (row 1 -> coded 0x05), List`1 (row 2 -> 0x09);
    // TypeSpec List<int> [15 12 09 01 08]; ctor MemberRef (parent = TypeSpec,
    // sig 20 00 01); a TypeDef whose Main body does newobj <0A000001>; pop; ret.
    private static MemoryStream SynthesizeNewListInt()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var listRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb),
            mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("List`1"));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x08 }));
        var ctor = mdb.AddMemberReference(typeSpec, mdb.GetOrAddString(".ctor"), mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));
        return BuildMainBody(mdb, ctor);
    }

    [Fact]
    public void Generate_NewListException_AllocatesThroughListObjClass()
    {
        // new List<System.Exception> (parent TypeSpec List`1 + 0x12
        // System.Exception): the dotted user-class instantiation rides the
        // shared listobj descriptor (A5/B1/B6).
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_bclalloc_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeNewListException().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var generated = File.ReadAllText(Path.Combine(dir, "src", "Cases_GenList.cpp"));
            Assert.Contains("il2cpp_object_new(il2cpp::il2cpp_list_obj_class())", generated);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // TypeRefs: Object (row 1), List`1 (row 2 -> coded 0x09), Exception
    // (row 3 -> coded 0x0D); TypeSpec List<Exception>
    // [15 12 09 01 12 0D]; ctor MemberRef (parent = TypeSpec, sig 20 00 01).
    private static MemoryStream SynthesizeNewListException()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var listRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb),
            mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("List`1"));
        var excRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Exception"));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x12, 0x0D }));
        var ctor = mdb.AddMemberReference(typeSpec, mdb.GetOrAddString(".ctor"), mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));
        return BuildMainBody(mdb, ctor);
    }

    // System.Object::.ctor MemberRef (parent = the Object TypeRef, sig
    // 20 00 01); Main body does newobj <0A000001>; pop; ret.
    private static MemoryStream SynthesizeNewObject()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var ctor = mdb.AddMemberReference(objRef, mdb.GetOrAddString(".ctor"), mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));
        return BuildMainBody(mdb, ctor);
    }

    private static MemoryStream BuildMainBody(MetadataBuilder mdb, MemberReferenceHandle ctor)
    {
        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Newobj);
        encoder.Token(MetadataTokens.GetToken(ctor));
        encoder.OpCode(ILOpCode.Pop);
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
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
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
