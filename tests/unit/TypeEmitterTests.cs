using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterTests -- class layout + descriptor emission invariants.
//
// M2 slot-grid layout rule (see TypeEmitter.LayoutInstanceFields):
//   slot 0 = object header (klass pointer); each instance field gets the next
//   slot in declaration order; slot i lives at byte offset i*IntPtr.Size;
//   refFieldBitmap bit i is set exactly when slot i is a GC reference;
//   instanceSize = slotCount * IntPtr.Size (whole 8-byte grid on x64).
//------------------------------------------------------------------------------
public class TypeEmitterTests
{
    // Fixture: Calc.Point { int X; int Y; }  -> slots 1,2 (values), bitmap 0, size 24
    private static TypeDef MakePoint()
    {
        var t = new TypeDef { Name = "Point", Namespace = "Calc" };
        t.InstanceFields.Add(new FieldDef { Name = "X", Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "Y", Owner = t, TypeKind = FieldTypeKind.Int32 });
        return t;
    }

    // Fixture: Calc.Node { int V; Node Next; } -> slots 1 (value), 2 (ref), bitmap 0b100, size 24
    private static TypeDef MakeNode()
    {
        var t = new TypeDef { Name = "Node", Namespace = "Calc" };
        t.InstanceFields.Add(new FieldDef { Name = "V", Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "Next", Owner = t, TypeKind = FieldTypeKind.Reference, FieldTypeName = "Calc.Node" });
        return t;
    }

    [Fact]
    public void Layout_ValueFields_ZeroBitmapAndPackedSlots()
    {
        var t = MakePoint();
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(1, t.InstanceFields[0].SlotIndex);
        Assert.Equal(2, t.InstanceFields[1].SlotIndex);
        Assert.Equal(0u, t.RefFieldBitmap);
        Assert.Equal(0, t.RefFieldCount);
        Assert.Equal(3 * IntPtr.Size, t.InstanceSize);
    }

    [Fact]
    public void Layout_RefField_SetsBitmapBitAtItsSlot()
    {
        var t = MakeNode();
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(2, t.InstanceFields[1].SlotIndex);
        Assert.Equal(0b100u, t.RefFieldBitmap); // bit 2 = slot 2 (Next is a GC ref)
        Assert.Equal(1, t.RefFieldCount);
        Assert.Equal(3 * IntPtr.Size, t.InstanceSize);
    }

    [Fact]
    public void EmitClassStruct_EmbedsObjectHeaderFirst()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitClassStruct(t);
        Assert.Contains("struct Calc_Node", code);
        Assert.Contains("Il2CppObject _obj;", code);   // header is the FIRST member
        Assert.Contains("int32_t V;", code);
        // Without the types list, ReferenceCppType falls back to Il2CppObject*
        // (the uniform reference shape) for user reference fields.
        Assert.Contains("Il2CppObject* Next;", code);
    }

    [Fact]
    public void EmitClassDescriptor_ContainsLayoutNumbers()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitClassDescriptor(t);
        Assert.Contains("\"Calc.Node\"", code);
        Assert.Contains("0b100u", code);
        Assert.Contains("1,", code); // refFieldCount
        Assert.Contains("nullptr", code); // baseClass (M2: no user inheritance)
    }

    [Fact]
    public void Layout_IsIdempotent()
    {
        var t = MakeNode();
        TypeEmitter.LayoutInstanceFields(t);
        var size1 = t.InstanceSize;
        var bitmap1 = t.RefFieldBitmap;
        TypeEmitter.LayoutInstanceFields(t); // second call must not shift slots
        Assert.Equal(size1, t.InstanceSize);
        Assert.Equal(bitmap1, t.RefFieldBitmap);
        Assert.Equal(1, t.InstanceFields[0].SlotIndex);
    }

    [Fact]
    public void Layout_NoFields_HeaderOnly()
    {
        var t = new TypeDef { Name = "Empty", Namespace = "Calc" };
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(0u, t.RefFieldBitmap);
        Assert.Equal(0, t.RefFieldCount);
        Assert.Equal(IntPtr.Size, t.InstanceSize); // just the object header
    }

    //--------------------------------------------------------------------------
    // Audit H6 -- the 32-bit refFieldBitmap can only express slots 0..31.
    // A reference field landing on slot >= 32 would be silently omitted from
    // the GC scan (bitmap shift UB; gc.cpp scans slot < 32) -- objects reachable
    // only through such fields would be collected. Warn and skip instead of
    // rejecting (the field is not added to the bitmap).
    //--------------------------------------------------------------------------
    [Fact]
    public void Layout_RefFieldAtSlot32_WarnsAndSkips()
    {
        var t = new TypeDef { Name = "Wide", Namespace = "Calc" };
        // 31 value fields -> slots 1..31, then a reference field -> slot 32.
        for (var i = 0; i < 31; i++)
            t.InstanceFields.Add(new FieldDef { Name = "V" + i, Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "Ref", Owner = t, TypeKind = FieldTypeKind.Reference, FieldTypeName = "Calc.Node" });
        // Audit H6: warn and skip instead of rejecting (the field is not added to the bitmap).
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(0u, t.RefFieldBitmap); // reference field at slot 32 is skipped
        Assert.Equal(0, t.RefFieldCount);
    }

    [Fact]
    public void Layout_ValueFieldAtSlot32_IsAllowed()
    {
        // Value fields beyond slot 31 are FINE (no bitmap bit is needed) --
        // only reference fields must be rejected.
        var t = new TypeDef { Name = "Wide", Namespace = "Calc" };
        for (var i = 0; i < 32; i++)
            t.InstanceFields.Add(new FieldDef { Name = "V" + i, Owner = t, TypeKind = FieldTypeKind.Int32 });
        TypeEmitter.LayoutInstanceFields(t); // must not throw
        Assert.Equal(0u, t.RefFieldBitmap);
        Assert.Equal(33 * IntPtr.Size, t.InstanceSize);
    }
}
