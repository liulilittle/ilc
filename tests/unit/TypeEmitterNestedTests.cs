using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterNestedTests -- nested types expand to independent top-level
// emission entries (recurse/exdeep gap: Cases.Recurse+Node / MyException were
// referenced by the generated C++ but never emitted).
//
// Naming contract (the invariant under test):
//   * every reference point resolves nested types from metadata by their
//     SHORT name -- MetadataExtensions.GetFullName on a TypeDefinitionHandle
//     returns "Node" for "Cases.Recurse+Node" (nested types carry an empty
//     namespace and no declaring-type prefix);
//   * the expanded TypeDef models the SAME identity (Name="Node",
//     Namespace="") so TypeCppName/Sanitize produce identical C++ names on
//     the reference side and the definition side:
//       il2cpp_object_new(&Node_class)          (CodeGenerator newobj)
//       ((Node*)...)->Id                        (field access cast)
//       Node__ctor_b3740a9592788e34             (SigHash("Node..ctor()"),
//                                                verified against the real
//                                                recurse gen output);
//   * base chains resolve across the WHOLE list (nested->nested,
//     top-level->nested); BCL bases stay null (System.Exception root).
//------------------------------------------------------------------------------
public class TypeEmitterNestedTests
{
    // Synthesize a PE with nested types, mirroring the recurse/exdeep shapes:
    //   Cases.Recurse (top-level static class)
    //     + Node         (nested class, int field Id, .ctor())
    //     + MyException  (nested : System.Exception, .ctor())
    //     + MyException2 (nested : MyException, .ctor())
    //   Cases.Derived (top-level class : Node -- a TOP-LEVEL type whose base
    //                  is a NESTED type; MetadataLoader misses the resolution
    //                  at load time, ExpandNestedTypes must fix it)
    private static MemoryStream SynthesizeNested()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("nested.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var excRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Exception"));
        var ctorSig = mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }); // instance, 0 params, void

        // Field/method LIST threading (ECMA-335 II.22.37): every type's
        // fieldList/methodList marks the FIRST row it owns; a type with NO
        // fields/methods carries the next type's first row (the range
        // [mine, next) is then empty). A nil (0) list mid-assembly makes the
        // reader compute a broken range (Node's field would vanish and the
        // ctor's declaring-type lookup would misattribute). Field rows:
        // Id=1. Method rows: Node..ctor=1, MyException..ctor=2,
        // MyException2..ctor=3, total=3.
        var nodeField = mdb.AddFieldDefinition(FieldAttributes.Public,
            mdb.GetOrAddString("Id"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 })); // int32
        var nodeCtor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), ctorSig, 0, default(ParameterHandle));
        var myExcCtor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), ctorSig, 0, default(ParameterHandle));
        var myExc2Ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), ctorSig, 0, default(ParameterHandle));

        // --- Cases.Recurse (static class, no members) ---
        var recurse = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef, nodeField, nodeCtor);

        // --- Node: field Id + .ctor() ---
        var node = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Node"), objRef, nodeField, nodeCtor);
        mdb.AddNestedType(node, recurse);

        // --- MyException : System.Exception + .ctor() ---
        // fieldList = 2 (field row count + 1): no fields of its own.
        var myExc = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("MyException"), excRef,
            MetadataTokens.FieldDefinitionHandle(2), myExcCtor);
        mdb.AddNestedType(myExc, recurse);

        // --- MyException2 : MyException (nested base, TypeDef handle) + .ctor() ---
        var myExc2 = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("MyException2"), myExc,
            MetadataTokens.FieldDefinitionHandle(2), myExc2Ctor);
        mdb.AddNestedType(myExc2, recurse);

        // --- Cases.Derived (top-level) : Node (nested base) ---
        // methodList = 4 (method row count + 1): no methods of its own.
        mdb.AddTypeDefinition(TypeAttributes.Public,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Derived"), node,
            MetadataTokens.FieldDefinitionHandle(2), MetadataTokens.MethodDefinitionHandle(4));

        return Serialize(mdb);
    }

    // Two nested types with the SAME short name in different outer types
    // (Ns1.A+Dup / Ns2.B+Dup) -- both expand to "Dup" -> duplicate C++ symbols.
    private static MemoryStream SynthesizeCollision()
    {        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("collision.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        var a = mdb.AddTypeDefinition(TypeAttributes.Public,
            mdb.GetOrAddString("Ns1"), mdb.GetOrAddString("A"), objRef, default, default);
        var dup1 = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Dup"), objRef, default, default);
        mdb.AddNestedType(dup1, a);

        var b = mdb.AddTypeDefinition(TypeAttributes.Public,
            mdb.GetOrAddString("Ns2"), mdb.GetOrAddString("B"), objRef, default, default);
        var dup2 = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Dup"), objRef, default, default);
        mdb.AddNestedType(dup2, b);

        return Serialize(mdb);
    }

    // Nested type with an INSTANCE ARRAY field (D-GC-1, newarrdeep): the
    // nested class's field decode (TypeEmitter.DecodeNestedFieldSignature --
    // the field-TYPE emission path, distinct from D-GC-2's newarr ELEMENT
    // classification in CodeGenerator) must accept SZARRAY (0x1D) and model
    // the field as a GC reference to the array object. Shape:
    //   Cases.Recurse (static class)
    //     + Bag        (nested class, Outer.Inner[]-style field kids)
    // The array element is a TypeRef to System.Object so the synthesized PE is
    // well-formed; the SZARRAY decode returns before reading the element.
    private static MemoryStream SynthesizeNestedArrayField()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("nestedarr.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var ctorSig = mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }); // instance, 0 params, void

        // kids: FIELD(0x06) + SZARRAY(0x1D) + CLASS(0x12) + TypeDefOrRef(Object).
        var codedObj = (MetadataTokens.GetRowNumber(objRef) << 2) | 1; // TypeRef tag 1
        var kidsField = mdb.AddFieldDefinition(FieldAttributes.Public,
            mdb.GetOrAddString("kids"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x1D, 0x12, (byte)codedObj }));
        var bagCtor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), ctorSig, 0, default(ParameterHandle));

        var recurse = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef, kidsField, bagCtor);
        var bag = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Bag"), objRef, kidsField, bagCtor);
        mdb.AddNestedType(bag, recurse);

        return Serialize(mdb);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    // The metadata reader borrows its backing blocks from the PEReader; the
    // reader is USED LONG AFTER Load returns (CtorMethodToken/typeinfo checks).
    // Disposing the PEReader here leaves `md` dangling -- the block pool then
    // hands the memory to a parallel test and StringHeap reads run out of
    // bounds (flaky BadImageFormatException under the full parallel suite).
    // Retain both handles for the process lifetime (test-only helper).
    private static readonly List<PEReader> s_retainedReaders = new();

    private static (List<TypeDef> Types, MetadataReader Md) Load(string tmpPath, MemoryStream pe)
    {
        File.WriteAllBytes(tmpPath, pe.ToArray());
        var types = MetadataLoader.LoadAssembly(tmpPath);
        // Read the bytes into memory FIRST: the caller deletes tmpPath, and a
        // live FileStream would lock the file (and the metadata blocks must
        // outlive the disposed REParse because md escapes this helper).
        var per = new PEReader(new MemoryStream(File.ReadAllBytes(tmpPath), writable: false));
        var md = per.GetMetadataReader();
        TypeEmitter.ExpandNestedTypes(md, types);
        lock (s_retainedReaders)
        {
            s_retainedReaders.Add(per);
        }
        return (types, md);
    }

    private static int CtorMethodToken(MetadataReader md, string ownerFullName)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            if (md.GetFullName(tdh) != ownerFullName) continue;
            foreach (var mh in md.GetTypeDefinition(tdh).GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (md.GetString(m.Name) == ".ctor") return MetadataTokens.GetToken(mh);
            }
        }
        throw new Xunit.Sdk.XunitException($"ctor of '{ownerFullName}' not found");
    }

    //--------------------------------------------------------------------------
    // Expansion -- nested types join the list as independent top-level
    // entries under their SHORT metadata name (the reference-point identity).
    //--------------------------------------------------------------------------
    [Fact]
    public void Expand_AddsNestedTypesWithShortNameIdentity()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNested());
            Assert.Equal(5, types.Count); // Recurse + Node + MyException + MyException2 + Derived
            var node = types.Single(t => t.FullName == "Node");
            Assert.Equal("", node.Namespace);
            Assert.Equal("Node", node.Name);
            Assert.Equal("System.Object", node.BaseTypeName);
            Assert.False(node.IsInterface);
            Assert.False(node.IsValueType);
            var id = Assert.Single(node.InstanceFields);
            Assert.Equal("Id", id.Name);
            Assert.Equal(FieldTypeKind.Int32, id.TypeKind);
            var ctor = Assert.Single(node.Methods);
            Assert.Equal(".ctor", ctor.Name);
            Assert.False(ctor.IsStatic);
            Assert.Equal(0, ctor.ParamCount);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Expand_NestedBaseChain_IsComplete()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNested());
            var myExc = types.Single(t => t.FullName == "MyException");
            var myExc2 = types.Single(t => t.FullName == "MyException2");
            // nested -> nested base resolves (the ExceptionChain walk depends
            // on BaseType pointers; CodeGenerator.ExceptionChain then emits
            // "MyException2,MyException2,MyException,System.Exception").
            Assert.NotNull(myExc2.BaseType);
            Assert.Equal("MyException", myExc2.BaseType!.FullName);
            // BCL base stays null with the terminal name recorded (the
            // exception-chain terminal segment comes from BaseTypeName).
            Assert.Null(myExc.BaseType);
            Assert.Equal("System.Exception", myExc.BaseTypeName);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Expand_TopLevelTypeWithNestedBase_Resolves()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNested());
            // Cases.Derived : Node was loaded by MetadataLoader BEFORE the
            // nested Node existed -- the expansion's whole-list second pass
            // must fix the resolution.
            var derived = types.Single(t => t.FullName == "Cases.Derived");
            Assert.NotNull(derived.BaseType);
            Assert.Equal("Node", derived.BaseType!.FullName);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // TypeInfo registry key (refl2 gt_nested / m1) -- the nested type's
    // TypeInfo table must register under the net48 '+' FULL name, not the
    // short C++ symbol name. Type.GetType("Cases.Outer+Inner") normalizes to
    // "Cases.Outer+Inner" (NormalizeTypeName rule 3 keeps '+'), and the
    // registry lookup compares info->name against that key -- a table named
    // "Inner" would never answer the query. The expansion records the nesting
    // identity (IsNested + DeclaringTypeName) so ReflectionFullName can emit
    // the '+' key while TypeCppName/Sanitize keep the short C++ symbols.
    //--------------------------------------------------------------------------
    [Fact]
    public void Expand_NestedType_RecordsNestingIdentity()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNested());
            var node = types.Single(t => t.FullName == "Node");
            Assert.True(node.IsNested);
            Assert.Equal("Cases.Recurse", node.DeclaringTypeName);
            // The net48 reflection identity: declaring chain + "+" + short name.
            Assert.Equal("Cases.Recurse+Node", TypeEmitter.ReflectionFullName(node));
            // The C++ symbol now carries the declaring-type prefix (r78 fix:
            // multiple enclosing types can have nested types with the same short
            // name -- the prefix disambiguates them).
            Assert.Equal("Cases_Recurse_Node", TypeEmitter.TypeCppName(node));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void EmitTypeInfo_NestedType_RegistryKeyIsPlusFullName()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNested());
            var node = types.Single(t => t.FullName == "Node");
            var code = TypeEmitter.EmitTypeInfo(node);
            // The table NAME field (the registry key) is the '+' full name --
            // Type.GetType("Cases.Recurse+Node") resolves against it.
            Assert.Contains("il2cpp::Il2CppTypeInfo Cases_Recurse_Node_typeinfo = {", code);
            Assert.Contains("\"Cases.Recurse+Node\",", code);
            Assert.DoesNotContain("\"Node\",", code);
            // Methods carry the same declaringType identity (net48 reflection).
            Assert.Contains("\"Cases.Recurse+Node\", reinterpret_cast", code);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // Emission -- the nested type emits as a FLAT top-level struct/descriptor
    // (struct Node, Il2CppClass Node_class), exactly as the reference points
    // name it.
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitClassStruct_NestedType_FlatTopLevelStruct()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNested());
            var node = types.Single(t => t.FullName == "Node");
            var code = TypeEmitter.EmitClassStruct(node);
            Assert.Contains("struct Cases_Recurse_Node", code);
            Assert.Contains("Il2CppObject _obj;", code); // reference type: header first
            Assert.Contains("int32_t Id;", code);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void EmitClassDescriptor_NestedType_WiresNestedBaseClass()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNested());
            var myExc = types.Single(t => t.FullName == "MyException");
            var myExc2 = types.Single(t => t.FullName == "MyException2");
            // BCL base -> nullptr (System.Exception is a runtime type).
            var d1 = TypeEmitter.EmitClassDescriptor(myExc);
            Assert.Contains("Il2CppClass Cases_Recurse_MyException_class", d1);
            Assert.Contains("\"Cases.Recurse+MyException\"", d1); // '+' canonical: matches typeof/enum registry
            Assert.Contains("nullptr", d1);         // baseClass (BCL root)
            // Nested user base -> the nested descriptor (same short-name mapping).
            var d2 = TypeEmitter.EmitClassDescriptor(myExc2);
            Assert.Contains("Il2CppClass Cases_Recurse_MyException2_class", d2);
            Assert.Contains("&Cases_Recurse_MyException_class", d2);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // Ctor name consistency -- the definition side (CodeGenerator.MethodCppName
    // over the expanded model) and the call side (MethodRefResolver.Resolve on
    // the metadata ctor token) must produce the SAME C++ name. The hash is
    // verified against the real recurse gen output:
    //   Node__ctor_b55523c5f2fc0cf3   (SigHash("Cases.Recurse+Node..ctor()") --
    //   '+' canonical owner key, same input the resolver hashes)
    //--------------------------------------------------------------------------
    [Fact]
    public void CtorCppName_MatchesReferencePointNaming()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, md) = Load(tmp, SynthesizeNested());
            var node = types.Single(t => t.FullName == "Node");
            var ctor = node.Methods.Single(m => m.Name == ".ctor");
            // Definition side: the MethodCppName formula CodeGenerator uses
            // when emitting the ctor function of an expanded nested type
            // (hash input is the '+' canonical owner key "Cases.Recurse+Node").
            var defName = $"{TypeEmitter.TypeCppName(node)}_{TypeEmitter.Sanitize(ctor.Name)}_{SigHash.Hash(TypeEmitter.ReflectionFullName(node) + "." + ctor.Name + "(" + ctor.ParamTypes + ")")}";
            Assert.Equal("Cases_Recurse_Node__ctor_b55523c5f2fc0cf3", defName);
            // Call side: the resolver names the ctor call from metadata
            // (owner '+' canonical name) -- must be byte-identical.
            var resolved = MethodRefResolver.Resolve(md, CtorMethodToken(md, "Cases.Recurse+Node"), types);
            Assert.Equal(defName, resolved.CppName);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // Collision guard -- two nested types with the same short name in
    // different outer types both expand to the same FullName -> duplicate
    // C++ symbols. Reject loudly at transpile time (the reference-point
    // naming cannot disambiguate them).
    //--------------------------------------------------------------------------


    //--------------------------------------------------------------------------
    // D-GC-1 (newarrdeep): a NESTED type's INSTANCE ARRAY FIELD -- the
    // field-TYPE emission path. MetadataLoader rejects top-level instance
    // array fields (read-only domain); the nested-class field decode lives in
    // TypeEmitter.DecodeNestedFieldSignature, which previously threw on
    // SZARRAY (0x1D) -- so a nested class with an array field never
    // transpiled. D-GC-2 fixed the NEWARR ELEMENT classification (allocation
    // stride / GC scan); this closes the FIELD TYPE gap: the field is a GC
    // reference to the array object, modeled as Reference/System.Array and
    // emitted as Il2CppObject* (the uniform reference shape).
    //--------------------------------------------------------------------------
    [Fact]
    public void Expand_NestedType_ArrayField_DecodesAsReferenceSystemArray()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNestedArrayField());
            var bag = types.Single(t => t.FullName == "Bag");
            var kids = Assert.Single(bag.InstanceFields);
            Assert.Equal("kids", kids.Name);
            Assert.Equal(FieldTypeKind.Reference, kids.TypeKind);
            Assert.Equal("System.Array", kids.FieldTypeName);
            // The reference bitmap marks the slot a GC reference (the field
            // holds an array object the collector must scan). Slot 1 (after
            // the object header at slot 0) -> bit 1. LayoutInstanceFields is
            // the layout pass (idempotent); EmitClassStruct calls it too.
            TypeEmitter.LayoutInstanceFields(bag);
            Assert.Equal(0b10u, bag.RefFieldBitmap);
            Assert.Equal(1, bag.RefFieldCount);
            Assert.Equal(2 * IntPtr.Size, bag.InstanceSize);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void EmitClassStruct_NestedType_ArrayField_EmitsIl2CppObjectPtr()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNestedArrayField());
            var bag = types.Single(t => t.FullName == "Bag");
            var code = TypeEmitter.EmitClassStruct(bag);
            Assert.Contains("struct Cases_Recurse_Bag", code);
            Assert.Contains("Il2CppObject* kids;", code);
            Assert.Contains("slot 1 at offset 8 (GC reference)", code);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
