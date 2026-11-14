using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// W11 F4-a TDD probes (J1): a reference instance field landing on slot >= 32
// must be REJECTED at transpile time with NotSupportedException, not silently
// omitted from the 32-bit refFieldBitmap (gc.cpp:705/:844 scan slot < 32 only).
// Covered trigger paths: A single class, B deep-inheritance slot accumulation
// (:1074-1079), C embedded user struct absSlot (:1555-1570). Path D (array
// isRefElem, gc.cpp:691/:827) is a separate GC branch and is EXCLUDED.
// Value fields beyond slot 31 stay legal (no over-tightening guard).
public class TypeEmitterF4BitmapTests
{
    private const string RefType = "Cases.Node";

    private static TypeDef NewClass(string name) =>
        new() { Name = name, Namespace = "Cases" };

    private static void AddInts(TypeDef t, int n)
    {
        for (var i = 0; i < n; i++)
            t.InstanceFields.Add(new FieldDef
            { Name = "V" + i, Owner = t, TypeKind = FieldTypeKind.Int32 });
    }

    private static void AddRef(TypeDef t, string name = "Ref") =>
        t.InstanceFields.Add(new FieldDef
        { Name = name, Owner = t, TypeKind = FieldTypeKind.Reference, FieldTypeName = RefType });

    // Path A: header slot 0 + 31 value slots -> reference lands on slot 32.
    [Fact]
    public void A_SingleClass_RefAtSlot32_ThrowsNotSupported()
    {
        var t = NewClass("WideA");
        AddInts(t, 31);
        AddRef(t);
        Assert.Throws<NotSupportedException>(() => TypeEmitter.LayoutInstanceFields(t));
    }

    // Path B: base chain accumulates 31 value slots; the derived class's OWN
    // reference continues the base slot grid and lands on slot 32.
    [Fact]
    public void B_DeepInheritance_OwnRefAtSlot32_ThrowsNotSupported()
    {
        var b = NewClass("WideBase");
        AddInts(b, 31);
        // BaseTypeName is init-only (:296) AND the base-chain branch (:1074)
        // requires it != "System.Object", so set it in the initializer.
        var d = new TypeDef
        { Name = "WideDerived", Namespace = "Cases", BaseTypeName = "Cases.WideBase", BaseType = b };
        AddRef(d, "OwnRef");
        Assert.Throws<NotSupportedException>(() => TypeEmitter.LayoutInstanceFields(d));
    }

    // Path C: an embedded user struct carries an inner GC reference. When the
    // struct is placed at owner slot 32, MarkEmbeddedReferences sees absSlot>=32.
    [Fact]
    public void C_EmbeddedStruct_InnerRefAbsSlot32_ThrowsNotSupported()
    {
        var s = new TypeDef { Name = "Inner", Namespace = "Cases", IsValueType = true };
        AddRef(s, "InnerRef"); // value-type layout -> inner bitmap bit 0
        var owner = NewClass("WideOwner");
        AddInts(owner, 31); // next own slot = 32
        owner.InstanceFields.Add(new FieldDef
        {
            Name = "Embedded", Owner = owner,
            TypeKind = FieldTypeKind.UserStruct,
            FieldTypeName = "Cases.Inner", UserStructType = s
        });
        Assert.Throws<NotSupportedException>(() => TypeEmitter.LayoutInstanceFields(owner));
    }

    // Site :1269 (LayoutValueTypeFields): a reference field of a VALUE TYPE
    // whose compact byte offset reaches slot >= 32 (64 int32 = 256 bytes @ ptr 8).
    [Fact]
    public void ValueType_RefAtSlot32_ThrowsNotSupported()
    {
        var vt = new TypeDef { Name = "WideStruct", Namespace = "Cases", IsValueType = true };
        AddInts(vt, 64);
        AddRef(vt);
        Assert.Throws<NotSupportedException>(() => TypeEmitter.LayoutInstanceFields(vt));
    }

    // Guard (no over-tightening): VALUE fields beyond slot 31 remain legal --
    // only REFERENCE fields need a bitmap bit and must be rejected.
    [Fact]
    public void ValueFieldsBeyondSlot32_StillAllowed()
    {
        var t = NewClass("WideValues");
        AddInts(t, 64);
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(0u, t.RefFieldBitmap);
        Assert.Equal(0, t.RefFieldCount);
    }

    // Guard (no regression): a reference BELOW slot 32 is still recorded.
    [Fact]
    public void RefBelowSlot32_StillRecordedInBitmap()
    {
        var t = NewClass("Small");
        AddInts(t, 2); // slots 1,2
        AddRef(t);     // slot 3
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(1u << 3, t.RefFieldBitmap);
        Assert.Equal(1, t.RefFieldCount);
    }
}
