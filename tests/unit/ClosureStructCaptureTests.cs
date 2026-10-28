using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// ClosureStructCaptureTests -- closure display-class fields of a USER STRUCT
// or a Nullable<T>.
//
// The defect this pins: TypeEmitter.ValueTypeNestedField rejected every nested
// VALUETYPE instance field that was not one of the five BCL runtime value
// structs or an enum, so a captured user struct was LOUD at transpile time:
//     nested type '<>c__DisplayClass0_0' field 'p': value-type field of type
//     'CapturedPt' has no runtime value slot ...
// A user struct HAS a layout in the model (LayoutValueTypeFields) and a
// descriptor (P25b), so the capture must materialize real slots:
//   * the display-class member is the generated struct VALUE, placed at its
//     natural alignment and spanning ceil(sizeof(T)/pointerSize) slots;
//   * every GC reference field INSIDE the struct is marked in the display
//     class refFieldBitmap at its ABSOLUTE slot (the collector scans the
//     object, not the struct's own descriptor);
//   * a Nullable<T> field expands to the underlying T value slot plus a
//     hasValue(bool) slot (each nullable field occupies its value slots + one
//     whole pointer slot for the flag).
//
// The assertions are on the generated TEXT + the laid-out model (slot indices,
// byte offsets, instance size, refFieldBitmap) so they fail RED on the rejected
// capture and on an int32/one-slot truncation.
//------------------------------------------------------------------------------
public class ClosureStructCaptureTests
{
    // The metadata reader borrows its backing blocks from the PEReader; the
    // reader is used long after Load returns. Retain the handles for the
    // process lifetime (same guard as ClosureValueSlotTests).
    private static readonly List<PEReader> s_retainedReaders = new();

    private const string PtFullName = "Cases.CapturedPt";

    //--------------------------------------------------------------------------
    // Metadata syntheses
    //--------------------------------------------------------------------------

    // row1 Cases.Recurse (top-level static class)
    // row2 CapturedPt   (top-level struct : System.ValueType)
    //        X : int32 / S : string / Y : int32   -> net48 sequential 24 bytes
    // row3 + <>c__DisplayClass0_0 (nested closure display class)
    //        p : CapturedPt   (user struct value field)
    //        q : int32        (neighbour after the struct)
    //        d : Nullable<DateTime>
    private static MemoryStream SynthesizeUserStructAndNullable()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("closurestruct.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var vtRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("ValueType"));
        var dtRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var nullableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Nullable`1"));

        // Field rows: 1 X, 2 S, 3 Y, 4 p, 5 q, 6 d.
        var xField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("X"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var sField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("S"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x0E }));
        var yField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("Y"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));

        // TypeDefOrRef coded index: TypeDef tag = 0, row 2.
        var codedPt = (byte)((2 << 2) | 0);
        var pField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("p"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, codedPt }));
        var qField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("q"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));

        static byte Coded(TypeReferenceHandle h) => (byte)((MetadataTokens.GetRowNumber(h) << 2) | 1);
        // FIELD(0x06) + GENERICINST(0x15) + VALUETYPE(0x11) Nullable`1 + arity 1
        // + VALUETYPE(0x11) DateTime -- the exact inline encoding csc emits.
        var dField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("d"),
            mdb.GetOrAddBlob(new byte[]
            {
                0x06, 0x15, 0x11, Coded(nullableRef), 0x01, 0x11, Coded(dtRef),
            }));

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef,
            MetadataTokens.FieldDefinitionHandle(1), default);
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("CapturedPt"), vtRef,
            MetadataTokens.FieldDefinitionHandle(1), default);
        var display = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("<>c__DisplayClass0_0"), objRef,
            MetadataTokens.FieldDefinitionHandle(4), default);
        mdb.AddNestedType(display, MetadataTokens.TypeDefinitionHandle(1));
        return Serialize(mdb);
    }

    // row1 Cases.Recurse
    // row2 CapturedPt (X int32 / S string / Y int32)
    // row3 + <>c__DisplayClass0_0 -- n : Nullable<CapturedPt>
    private static MemoryStream SynthesizeNullableOfUserStruct()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("closurestructn.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var vtRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("ValueType"));
        var nullableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Nullable`1"));

        var xField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("X"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var sField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("S"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x0E }));
        var yField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("Y"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));

        var codedPt = (byte)((2 << 2) | 0);
        static byte Coded(TypeReferenceHandle h) => (byte)((MetadataTokens.GetRowNumber(h) << 2) | 1);
        var nField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("n"),
            mdb.GetOrAddBlob(new byte[]
            {
                0x06, 0x15, 0x11, Coded(nullableRef), 0x01, 0x11, codedPt,
            }));

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef,
            MetadataTokens.FieldDefinitionHandle(1), default);
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("CapturedPt"), vtRef,
            MetadataTokens.FieldDefinitionHandle(1), default);
        var display = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("<>c__DisplayClass0_0"), objRef,
            MetadataTokens.FieldDefinitionHandle(4), default);
        mdb.AddNestedType(display, MetadataTokens.TypeDefinitionHandle(1));
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

    private static (List<TypeDef> Types, MetadataReader Md) Load(string tmpPath, MemoryStream pe)
    {
        File.WriteAllBytes(tmpPath, pe.ToArray());
        var types = MetadataLoader.LoadAssembly(tmpPath);
        var per = new PEReader(new MemoryStream(File.ReadAllBytes(tmpPath), writable: false));
        var md = per.GetMetadataReader();
        TypeEmitter.ExpandNestedTypes(md, types);
        lock (s_retainedReaders)
        {
            s_retainedReaders.Add(per);
        }
        return (types, md);
    }

    private static TypeDef Display(List<TypeDef> types) => types.Single(t => t.FullName == "<>c__DisplayClass0_0");

    //--------------------------------------------------------------------------
    // User struct
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitClassStruct_UserStructField_IsEmbeddedStructValue()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeUserStructAndNullable());
            var pt = types.Single(t => t.FullName == PtFullName);
            var code = TypeEmitter.EmitClassStruct(Display(types), types);

            // The display-class member IS the generated struct value.
            Assert.Contains($"{TypeEmitter.TypeCppName(pt)} p;", code);
            Assert.DoesNotContain("int32_t p;", code);
            // The Nullable field is split into the value + hasValue slots.
            Assert.Contains("il2cpp::runtime::Il2CppDateTime d__value;", code);
            Assert.Contains("bool d__hasValue;", code);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Layout_UserStructField_SpanAndReferenceBitmap()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeUserStructAndNullable());
            var display = Display(types);
            TypeEmitter.LayoutInstanceFields(display, types);

            var p = display.InstanceFields.Single(f => f.Name == "p");
            var q = display.InstanceFields.Single(f => f.Name == "q");
            var d = display.InstanceFields.Single(f => f.Name == "d");

            // CapturedPt = { int X; string S; int Y } -> 24 bytes / align 8.
            Assert.Equal(FieldTypeKind.UserStruct, p.TypeKind);
            Assert.Equal(1, p.SlotIndex);
            Assert.Equal(8, p.ByteOffset);
            // p spans slots 1..3; q continues at slot 4.
            Assert.Equal(4, q.SlotIndex);
            Assert.Equal(32, q.ByteOffset);
            // Nullable<DateTime> starts at its 8-byte-aligned offset (slot 5):
            // 16-byte value (slots 5,6) + the hasValue slot (slot 7).
            Assert.Equal(FieldTypeKind.Nullable, d.TypeKind);
            Assert.Equal(5, d.SlotIndex);
            Assert.Equal(40, d.ByteOffset);
            Assert.Equal(56, d.NullableHasValueOffset);
            Assert.Equal(64, display.InstanceSize);
            // The string field inside the captured struct is at absolute
            // offset 8 (p) + 8 (S) = 16 -> slot 2.
            Assert.Equal(1u << 2, display.RefFieldBitmap);
            Assert.Equal(1, display.RefFieldCount);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // Nullable<T>
    //--------------------------------------------------------------------------
    [Fact]
    public void Layout_NullableOfUserStruct_SpanAndInnerReferences()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNullableOfUserStruct());
            var display = Display(types);
            TypeEmitter.LayoutInstanceFields(display, types);

            var n = display.InstanceFields.Single(f => f.Name == "n");
            Assert.Equal(FieldTypeKind.Nullable, n.TypeKind);
            Assert.Equal(PtFullName, n.NullableUnderlyingName);
            Assert.Equal(1, n.SlotIndex);
            Assert.Equal(8, n.ByteOffset);
            // CapturedPt value is 24 bytes (slots 1..3); hasValue occupies the
            // following whole slot (slot 4) -> the object ends at slot 5.
            Assert.Equal(32, n.NullableHasValueOffset);
            Assert.Equal(40, display.InstanceSize);
            // Inner S at relative offset 8 -> absolute 16 -> slot 2.
            Assert.Equal(1u << 2, display.RefFieldBitmap);
            Assert.Equal(1, display.RefFieldCount);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void EmitClassStruct_NullableOfUserStruct_EmitsValueAndFlag()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNullableOfUserStruct());
            var pt = types.Single(t => t.FullName == PtFullName);
            var code = TypeEmitter.EmitClassStruct(Display(types), types);
            Assert.Contains($"{TypeEmitter.TypeCppName(pt)} n__value;", code);
            Assert.Contains("bool n__hasValue;", code);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // The old contract (LOUD rejection of a captured user struct) is replaced:
    // the field is modeled and laid out.
    //--------------------------------------------------------------------------
    [Fact]
    public void Expand_NestedUserStructField_IsModeled()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeUserStructAndNullable());
            var display = Display(types);
            var p = display.InstanceFields.Single(f => f.Name == "p");
            Assert.Equal(FieldTypeKind.UserStruct, p.TypeKind);
            Assert.Equal(PtFullName, p.FieldTypeName);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}

