using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// ClosureValueSlotTests -- a CLOSURE-CAPTURED VALUE TYPE keeps its real
// runtime value slot in the nested display class.
//
// The defect this pins: TypeEmitter.DecodeNestedFieldSignature classified
// every nested VALUETYPE (0x11) instance field as an int32 slot (the Guid
// exception aside), so a captured DateTime became
//     int32_t d; // slot 1 at offset 8 (value)
// while the IL value is the 16-byte il2cpp::runtime::Il2CppDateTime. The
// store (->d = t1) then failed to compile (int32_t = Il2CppDateTime) and the
// read (icall_datetime_get_year(*reinterpret_cast<Il2CppDateTime*>(&->d)))
// silently read 8 bytes out of a 4-byte slot.
//
// The contract under test:
//   * System.Guid / System.DateTime / System.TimeSpan / System.Decimal /
//     System.DateTimeOffset -> the runtime value struct member (never int32);
//   * an ENUM -> the int32 carrier (the established D5 / com17type doctrine:
//     the enum rides its underlying int32 slot);
//   * any OTHER value type (a user struct, ...) -> LOUD rejection with a
//     self-identifying message, never a silently mis-typed slot.
//
// The assertions are on the GENERATED TEXT (not on the model enums) so the
// test compiles against the pre-fix tree and fails RED on the int32 member.
//------------------------------------------------------------------------------
public class ClosureValueSlotTests
{
    // The metadata reader borrows its backing blocks from the PEReader; the
    // reader is used long after Load returns. Retain the handles for the
    // process lifetime (same guard as TypeEmitterNestedTests).
    private static readonly List<PEReader> s_retainedReaders = new();

    //--------------------------------------------------------------------------
    // Syntheses
    //--------------------------------------------------------------------------

    // Cases.Recurse (top-level static class)
    //   + <>c__DisplayClass0_0 (nested class -- the closure display class)
    //       d  : System.DateTime
    //       ts : System.TimeSpan
    //       m  : System.Decimal
    //       g  : System.Guid
    private static MemoryStream SynthesizeClosureValueFields()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("closureval.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var dtRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var tsRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("TimeSpan"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));
        var guidRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Guid"));

        // FIELD(0x06) + VALUETYPE(0x11) + TypeDefOrRef coded index.
        // TypeRef tag = 1; every coded value here is < 0x80 -> one byte.
        static byte Coded(TypeReferenceHandle h) => (byte)((MetadataTokens.GetRowNumber(h) << 2) | 1);
        var dField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("d"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, Coded(dtRef) }));
        var tsField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("ts"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, Coded(tsRef) }));
        var mField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("m"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, Coded(decRef) }));
        var gField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("g"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, Coded(guidRef) }));

        var recurse = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef, dField, default);
        var display = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("<>c__DisplayClass0_0"), objRef, dField, default);
        mdb.AddNestedType(display, recurse);
        return Serialize(mdb);
    }

    // Cases.Recurse (top-level static class)
    //   + Col   (nested enum : System.Enum -- value__ : Col)
    //   + Holder(nested class -- c : Col)
    // The enum rides the int32 carrier slot (D5 / com17type doctrine).
    private static MemoryStream SynthesizeNestedEnumField()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("closureenum.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var enumRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Enum"));

        // TypeDef row of Col = 2 -> TypeDefOrRef coded index (row << 2) | 0.
        var codedCol = (byte)((2 << 2) | 0);
        var valueField = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.SpecialName,
            mdb.GetOrAddString("value__"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, codedCol }));
        var cField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("c"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, codedCol }));

        var recurse = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef, valueField, default);
        var col = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Col"), enumRef, valueField, default);
        mdb.AddNestedType(col, recurse);
        var holder = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Holder"), objRef,
            MetadataTokens.FieldDefinitionHandle(2), default);
        mdb.AddNestedType(holder, recurse);
        return Serialize(mdb);
    }

    // Cases.Recurse (top-level static class)
    //   + Pt     (nested struct : System.ValueType -- X, Y : int32)
    //   + Holder (nested class -- p : Pt)
    // A user struct has no runtime value slot shape on the nested path ->
    // LOUD rejection, never a silently mis-typed int32 member.
    private static MemoryStream SynthesizeNestedUserStructField()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("closurept.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var vtRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("ValueType"));

        // TypeDef row of Pt = 2 -> TypeDefOrRef coded index (row << 2) | 0.
        var codedPt = (byte)((2 << 2) | 0);
        var xField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("X"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var yField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("Y"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var pField = mdb.AddFieldDefinition(FieldAttributes.Public, mdb.GetOrAddString("p"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x11, codedPt }));

        var recurse = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef, xField, default);
        var pt = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Pt"), vtRef, xField, default);
        mdb.AddNestedType(pt, recurse);
        var holder = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Holder"), objRef,
            MetadataTokens.FieldDefinitionHandle(3), default);
        mdb.AddNestedType(holder, recurse);
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

    //--------------------------------------------------------------------------
    // RED: the captured value types keep their runtime value shape.
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitClassStruct_NestedClosureValueFields_KeepRuntimeValueShape()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeClosureValueFields());
            var display = types.Single(t => t.FullName == "<>c__DisplayClass0_0");
            var code = TypeEmitter.EmitClassStruct(display);

            // The 16-byte / 8-byte runtime value structs, never an int32 slot.
            Assert.Contains("il2cpp::runtime::Il2CppDateTime d;", code);
            Assert.Contains("il2cpp::runtime::Il2CppTimeSpan ts;", code);
            Assert.Contains("il2cpp::runtime::Il2CppDecimal m;", code);
            Assert.Contains("il2cpp::Il2CppGuid g;", code);

            // The pre-fix shape (int32 slot for a value type) must be gone.
            Assert.DoesNotContain("int32_t d;", code);
            Assert.DoesNotContain("int32_t ts;", code);
            Assert.DoesNotContain("int32_t m;", code);
            Assert.DoesNotContain("int32_t g;", code);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // The value slot must also ADVANCE the class slot grid: a 16-byte value
    // struct occupies two pointer-size slots, so the following field must not
    // land on the same slot (the GC refFieldBitmap scans slot-wise).
    [Fact]
    public void Layout_NestedClosureValueFields_AdvanceTheSlotGrid()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeClosureValueFields());
            var display = types.Single(t => t.FullName == "<>c__DisplayClass0_0");
            TypeEmitter.LayoutInstanceFields(display);

            var d = display.InstanceFields.Single(f => f.Name == "d");
            var ts = display.InstanceFields.Single(f => f.Name == "ts");
            var m = display.InstanceFields.Single(f => f.Name == "m");
            var g = display.InstanceFields.Single(f => f.Name == "g");

            Assert.Equal(1, d.SlotIndex);
            Assert.Equal(8, d.ByteOffset);
            // DateTime is 16 bytes -> two slots -> TimeSpan starts at slot 3.
            Assert.Equal(3, ts.SlotIndex);
            Assert.Equal(24, ts.ByteOffset);
            // TimeSpan is 8 bytes -> one slot -> Decimal starts at slot 4.
            Assert.Equal(4, m.SlotIndex);
            Assert.Equal(32, m.ByteOffset);
            // Decimal is 16 bytes -> two slots -> Guid starts at slot 6.
            Assert.Equal(6, g.SlotIndex);
            Assert.Equal(48, g.ByteOffset);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // GREEN contract: an ENUM keeps the int32 carrier slot.
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitClassStruct_NestedEnumField_KeepsInt32Carrier()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var (types, _) = Load(tmp, SynthesizeNestedEnumField());
            var holder = types.Single(t => t.FullName == "Holder");
            var code = TypeEmitter.EmitClassStruct(holder);
            Assert.Contains("int32_t c;", code);
            Assert.DoesNotContain("Col c;", code);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // LOUD contract: an unsupported value type is rejected with a
    // self-identifying message (never a silently mis-typed int32 slot).
    //--------------------------------------------------------------------------
    [Fact]
    public void Expand_NestedUserStructField_IsRejectedLoudly()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var ex = Assert.Throws<NotSupportedException>(() => Load(tmp, SynthesizeNestedUserStructField()));
            Assert.Contains("Pt", ex.Message);
            Assert.Contains("Holder", ex.Message);
            Assert.Contains("p", ex.Message);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
