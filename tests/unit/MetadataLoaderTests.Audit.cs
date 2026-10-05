using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// MetadataLoader audit (b23) -- the metadata-loading surface beyond the M2
// top-level shape:
//
//   * static-field SIGNATURE decoding (the decimal sf_Euler gap): static
//     FieldDefs now carry TypeKind/FieldTypeName from their field signature --
//     the model the emitter classifies storage from (System.Decimal -> 16-byte
//     Il2CppDecimal, reference -> Il2CppObject*, everything else -> int32_t).
//   * TypeSpec forms (generic instantiations) as base/interface types.
//   * entry point from the CLI header (GetEntryPointFullName).
//   * CustomAttribute parsing (ATTRIBUTE domain): fixed ctor args + named
//     field/property args.
//
// All PEs are synthesized in-memory with MetadataBuilder (no file fixtures
// beyond the entry-point assertions, which use the existing calc fixture).
//------------------------------------------------------------------------------
public class MetadataLoaderAuditTests
{
    //--------------------------------------------------------------------------
    // Static-field signature decoding.
    //--------------------------------------------------------------------------
    [Fact]
    public void StaticField_DecimalSignature_LoadsSystemDecimalTypeName()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        var types = MetadataLoader.LoadAssembly(tmp.Path);
        var util = types.Single(t => t.FullName == "Meta.Util");

        // sf_Euler gap closure: the loader decodes the STATIC field signature
        // into the model -- a System.Decimal static field carries the value
        // kind (16-byte value slot) plus the System.Decimal type name, which is
        // exactly what the static-storage emitter classifies from (the 16-byte
        // Il2CppDecimal emission lives in the TypeEmitter domain).
        var rate = util.StaticFields.Single(f => f.Name == "Rate");
        Assert.Equal(FieldTypeKind.Int32, rate.TypeKind);   // value slot
        Assert.Equal("System.Decimal", rate.FieldTypeName); // the 16-byte driver
    }

    [Fact]
    public void StaticField_ReferenceSignature_LoadsReferenceKind()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        var types = MetadataLoader.LoadAssembly(tmp.Path);
        var util = types.Single(t => t.FullName == "Meta.Util");

        var label = util.StaticFields.Single(f => f.Name == "Label");
        Assert.Equal(FieldTypeKind.Reference, label.TypeKind);
        Assert.Equal("System.String", label.FieldTypeName);

        var boxRef = util.StaticFields.Single(f => f.Name == "Ref");
        Assert.Equal(FieldTypeKind.Reference, boxRef.TypeKind);
        Assert.Equal("Meta.Box", boxRef.FieldTypeName);

        var tags = util.StaticFields.Single(f => f.Name == "Tags");
        Assert.Equal(FieldTypeKind.Reference, tags.TypeKind);
        Assert.Equal("System.Array", tags.FieldTypeName);
    }

    [Fact]
    public void StaticField_PrimitiveWidths_KeepValueSlotWithSystemName()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        var types = MetadataLoader.LoadAssembly(tmp.Path);
        var util = types.Single(t => t.FullName == "Meta.Util");

        var count = util.StaticFields.Single(f => f.Name == "Count");
        Assert.Equal(FieldTypeKind.Int32, count.TypeKind);
        Assert.Equal("System.Int32", count.FieldTypeName);

        var big = util.StaticFields.Single(f => f.Name == "Big");
        Assert.Equal(FieldTypeKind.Int32, big.TypeKind);   // int64 still a value slot
        Assert.Equal("System.Int64", big.FieldTypeName);
    }

    [Fact]
    public void InstanceField_ValueType_StillThrows()
    {
        // The M2 instance-field contract is UNCHANGED: a value-type instance
        // field must still fail loudly (no silent mis-layout).
        using var tmp = WriteTempAssembly(SynthesizeValueTypeFieldAssembly());
        Assert.Throws<NotSupportedException>(() => MetadataLoader.LoadAssembly(tmp.Path));
    }

    [Fact]
    public void InstanceField_Int32_LoadsInt32Kind()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        var types = MetadataLoader.LoadAssembly(tmp.Path);
        var util = types.Single(t => t.FullName == "Meta.Util");

        var x = util.InstanceFields.Single(f => f.Name == "X");
        Assert.Equal(FieldTypeKind.Int32, x.TypeKind);
        Assert.Equal("", x.FieldTypeName); // M2 value fields carry the empty name
    }

    //--------------------------------------------------------------------------
    // TypeSpec forms (generic instantiations) as base / interface types.
    //--------------------------------------------------------------------------
    [Fact]
    public void TypeSpec_GenericBase_ResolvesInstantiationName()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        var types = MetadataLoader.LoadAssembly(tmp.Path);
        // The generic DEFINITION TypeRef carries the arity suffix ("Base`1");
        // the TypeSpec instantiation name appends the decoded arguments.
        var util2 = types.Single(t => t.FullName == "Meta.Util2");
        Assert.Equal("Meta.Base`1<System.Int32>", util2.BaseTypeName);
        // The plain (non-generic) base still resolves through the classic path.
        var util = types.Single(t => t.FullName == "Meta.Util");
        Assert.Equal("System.Object", util.BaseTypeName);
    }

    [Fact]
    public void TypeSpec_GenericInterface_ResolvesInstantiationName()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        var types = MetadataLoader.LoadAssembly(tmp.Path);
        var util = types.Single(t => t.FullName == "Meta.Util");
        Assert.Contains("Meta.Iface`1<System.Int32>", util.InterfaceNames);
    }

    //--------------------------------------------------------------------------
    // Entry point (CLI header token).
    //--------------------------------------------------------------------------
    [Fact]
    public void GetEntryPointFullName_ExeWithMain()
    {
        // calc.exe is the console variant of the calc fixture: Main is the
        // CLI entry point, exposed as "Namespace.Type::Method".
        Assert.Equal("Calc.Program::Main", MetadataLoader.GetEntryPointFullName(FixtureExePath("calc")));
    }

    [Fact]
    public void GetEntryPointFullName_LibraryReturnsNull()
    {
        // The synthetic Meta assembly is a DLL (no entry point token).
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        Assert.Null(MetadataLoader.GetEntryPointFullName(tmp.Path));
    }

    //--------------------------------------------------------------------------
    // CustomAttribute parsing (ATTRIBUTE domain).
    //--------------------------------------------------------------------------
    [Fact]
    public void LoadCustomAttributes_DecodesFixedAndNamedArgs()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        var attrs = MetadataLoader.LoadCustomAttributes(tmp.Path, "Meta.Util");

        var ca = Assert.Single(attrs);
        Assert.Equal("Meta.Marker", ca.AttributeType);
        Assert.Equal(".ctor", ca.CtorName);

        var fixedArg = Assert.Single(ca.FixedArgs);
        Assert.Equal("hello", fixedArg);

        Assert.Equal(2, ca.NamedArgs.Count);
        Assert.True(ca.NamedArgs[0].IsField);
        Assert.Equal("Extra", ca.NamedArgs[0].Name);
        Assert.Equal(42, ca.NamedArgs[0].Value);
        Assert.False(ca.NamedArgs[1].IsField); // property named arg (0x54)
        Assert.Equal("Enabled", ca.NamedArgs[1].Name);
        Assert.Equal(true, ca.NamedArgs[1].Value);
    }

    [Fact]
    public void LoadCustomAttributes_UnknownType_ReturnsEmpty()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        Assert.Empty(MetadataLoader.LoadCustomAttributes(tmp.Path, "No.Such.Type"));
    }

    //--------------------------------------------------------------------------
    // D-ATTR-1 item c: the BATCH surface (LoadAllCustomAttributes) the code
    // generator consumes -- every annotated type keyed by its metadata full
    // name, with the ctor TOKEN the emitter resolves the generated ctor C++
    // name from.
    //--------------------------------------------------------------------------
    [Fact]
    public void LoadAllCustomAttributes_KeysEveryAnnotatedTypeAndCarriesCtorToken()
    {
        using var tmp = WriteTempAssembly(SynthesizeMeta());
        using var stream = File.OpenRead(tmp.Path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var all = MetadataLoader.LoadAllCustomAttributes(md);

        var (typeName, attrs) = Assert.Single(all);
        Assert.Equal("Meta.Util", typeName);
        var ca = Assert.Single(attrs);
        Assert.Equal("Meta.Marker", ca.AttributeType);
        Assert.Equal(".ctor", ca.CtorName);
        // The ctor token: this synthetic assembly references the ctor via a
        // MemberReference whose parent is the Meta.Marker TypeDef (a real
        // Roslyn same-assembly attribute ctor is a MethodDefinition -- both
        // kinds resolve to the same generated C++ name).
        var handle = MetadataTokens.Handle(ca.ConstructorToken);
        Assert.Equal(HandleKind.MemberReference, handle.Kind);
        var ctor = md.GetMemberReference((MemberReferenceHandle)handle);
        Assert.Equal(".ctor", md.GetString(ctor.Name));
        Assert.Equal(HandleKind.TypeDefinition, ctor.Parent.Kind);
        Assert.Equal("Meta.Marker", md.GetFullName((TypeDefinitionHandle)ctor.Parent));
    }

    //--------------------------------------------------------------------------
    // Synthetic assembly: the full TypeDef/TypeRef/TypeSpec + field + attribute
    // surface the audit covers.
    //--------------------------------------------------------------------------
    private static MemoryStream SynthesizeMeta()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("meta.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));

        // System type refs.
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var attrRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Attribute"));
        var stringRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("String"));
        var decimalRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));

        // Generic DEFINITION refs for the TypeSpec tests (Meta.Base / Meta.Iface).
        var baseDefRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Base`1"));
        var ifaceDefRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Iface`1"));
        var boxRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Box"));

        // Generic instantiations: Meta.Base<int> / Meta.Iface<int> (0x15 GENERICINST,
        // CLASS marker + coded TypeDefOrRef + arg count + one I4 argument).
        var baseIntSpec = mdb.AddTypeSpecification(GenericInstBlob(mdb, baseDefRef, 0x08));
        var ifaceIntSpec = mdb.AddTypeSpecification(GenericInstBlob(mdb, ifaceDefRef, 0x08));

        // Meta.Marker -- the attribute class (extends System.Attribute).
        var markerCtor0Sig = mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }); // instance void .ctor()
        var markerCtor1Sig = mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x01, 0x0E }); // instance void .ctor(string)
        var markerCtor0 = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), markerCtor0Sig, 0, default(ParameterHandle));
        var markerCtor1 = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), markerCtor1Sig, 0, default(ParameterHandle));
        var markerTd = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Marker"),
            attrRef, default, markerCtor0);

        // Meta.Box -- a plain class for the CLASS-typed reference field.
        var boxTd = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Box"),
            objRef, default, default);

        // Meta.Util -- the audited field/interface/attribute surface.
        var intSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 });              // int32
        var longSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x0A });             // int64
        var stringSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x0E });           // string
        var decimalSig = ValueTypeFieldBlob(mdb, decimalRef);                  // System.Decimal
        var classSig = ClassFieldBlob(mdb, boxRef);                            // Meta.Box
        var stringArraySig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x1D, 0x0E }); // string[]

        var fieldX = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("X"), intSig);
        var fieldCount = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Count"), intSig);
        var fieldBig = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Big"), longSig);
        var fieldLabel = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Label"), stringSig);
        var fieldRate = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Rate"), decimalSig);
        var fieldRef = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Ref"), classSig);
        var fieldTags = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Tags"), stringArraySig);

        var utilCtor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }), 0, default(ParameterHandle));
        var utilGet = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString("Get"), mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }), 0, default(ParameterHandle));
        // Meta.Util2 -- base type is a generic instantiation (TypeSpec extends).
        // NOTE: added BEFORE Meta.Util -- the metadata fieldList range of a
        // type runs [fieldList, nextType.fieldList); a later type with a nil
        // fieldList would truncate Util's field rows (Util must be the LAST
        // type so its range extends to the end of the Field table).
        mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Util2"),
            baseIntSpec, default, default);

        var utilTd = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Util"),
            objRef, fieldX, utilCtor);
        mdb.AddInterfaceImplementation(utilTd, ifaceIntSpec);

        // Custom attribute on Meta.Util: Marker .ctor(string) with fixed arg
        // "hello" + named field Extra=42 + named property Enabled=true.
        var ctorRef = mdb.AddMemberReference(markerTd, mdb.GetOrAddString(".ctor"), markerCtor1Sig);
        var attrBlob = AttributeBlob();
        mdb.AddCustomAttribute(utilTd, ctorRef, mdb.GetOrAddBlob(attrBlob));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    // A one-type assembly whose instance field is a value type -- the M2-rejected
    // shape (the loader must throw at load time, preserving the fail-loudly
    // contract for INSTANCE fields even though static value-type fields now
    // load type-aware).
    private static MemoryStream SynthesizeValueTypeFieldAssembly()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("vt.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var decimalRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));

        var badFieldSig = ValueTypeInstanceField(mdb, decimalRef);
        var badField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("Value"), badFieldSig);
        mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class, mdb.GetOrAddString("Meta"), mdb.GetOrAddString("Bad"),
            objRef, badField, default);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    // FIELD(0x06) + VALUETYPE(0x11) + TypeDefOrRef coded index of a TypeRef.
    private static BlobHandle ValueTypeFieldBlob(MetadataBuilder mdb, TypeReferenceHandle typeRef)
    {
        var bb = new BlobBuilder();
        bb.WriteByte(0x06);
        bb.WriteByte(0x11);
        bb.WriteCompressedInteger(TypeDefOrRefIndex(typeRef, tag: 1));
        return mdb.GetOrAddBlob(bb);
    }

    // FIELD(0x06) + CLASS(0x12) + TypeDefOrRef coded index of a TypeRef.
    private static BlobHandle ClassFieldBlob(MetadataBuilder mdb, TypeReferenceHandle typeRef)
    {
        var bb = new BlobBuilder();
        bb.WriteByte(0x06);
        bb.WriteByte(0x12);
        bb.WriteCompressedInteger(TypeDefOrRefIndex(typeRef, tag: 1));
        return mdb.GetOrAddBlob(bb);
    }

    // An INSTANCE value-type field (0x06 0x11 + coded ref) -- the M2-rejected shape.
    private static BlobHandle ValueTypeInstanceField(MetadataBuilder mdb, TypeReferenceHandle typeRef)
    {
        var bb = new BlobBuilder();
        bb.WriteByte(0x06);
        bb.WriteByte(0x11);
        bb.WriteCompressedInteger(TypeDefOrRefIndex(typeRef, tag: 1));
        return mdb.GetOrAddBlob(bb);
    }

    // GENERICINST(0x15) + CLASS(0x12) + TypeDefOrRef + arg count + one type byte.
    private static BlobHandle GenericInstBlob(MetadataBuilder mdb, TypeReferenceHandle defRef, byte argType)
    {
        var bb = new BlobBuilder();
        bb.WriteByte(0x15);
        bb.WriteByte(0x12);
        bb.WriteCompressedInteger(TypeDefOrRefIndex(defRef, tag: 1));
        bb.WriteCompressedInteger(1);
        bb.WriteByte(argType);
        return mdb.GetOrAddBlob(bb);
    }

    // TypeDefOrRef coded index: (rowId << 2) | tag (0 TypeDef, 1 TypeRef, 2 TypeSpec).
    private static int TypeDefOrRefIndex(EntityHandle h, int tag) =>
        (MetadataTokens.GetRowNumber(h) << 2) | tag;

    // CustomAttribute blob: prolog 0x0001 + fixed SerString "hello" + two named
    // args (field Extra int32 42, property Enabled bool true).
    private static byte[] AttributeBlob()
    {
        var bb = new BlobBuilder();
        bb.WriteUInt16(0x0001);
        bb.WriteSerializedString("hello");
        bb.WriteCompressedInteger(2);
        // field int Extra = 42
        bb.WriteByte(0x53);
        bb.WriteByte(0x08);
        bb.WriteSerializedString("Extra");
        bb.WriteInt32(42);
        // property bool Enabled = true
        bb.WriteByte(0x54);
        bb.WriteByte(0x02);
        bb.WriteSerializedString("Enabled");
        bb.WriteByte(0x01);
        return bb.ToArray();
    }

    //--------------------------------------------------------------------------
    // Fixture / temp helpers (same repo-root-walk convention as
    // MetadataLoaderTests.FixturePath).
    //--------------------------------------------------------------------------
    private sealed class TempFile : IDisposable
    {
        public TempFile(string path) => Path = path;
        public string Path { get; }
        public void Dispose() => File.Delete(Path);
    }

    private static TempFile WriteTempAssembly(MemoryStream ms)
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "il2cpp_mdl1_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, ms.ToArray());
        return new TempFile(tmp);
    }

    private static string FixtureExePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "tests", "fixtures", "out", name + ".exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"fixture {name}.exe not found; run scripts/build-fixtures.ps1 first");
    }
}
