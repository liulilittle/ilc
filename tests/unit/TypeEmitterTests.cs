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

    // O-1 (RF-N4 audit A): a DERIVED class's own instance fields continue the
    // base chain's slot grid -- a derived class with its OWN reference field
    // must lay it out AFTER the base region (slot = base.InstanceSize/ptr),
    // and EmitClassStruct must emit the base chain's fields INLINE so the C++
    // member offsets match the slot offsets the GC bitmap scans.
    private static (TypeDef BaseT, TypeDef Derived) MakeDerivedWithOwnRefField()
    {
        var baseT = new TypeDef { Name = "GcBase", Namespace = "Cases" };
        baseT.InstanceFields.Add(new FieldDef { Name = "name", Owner = baseT, TypeKind = FieldTypeKind.Reference, FieldTypeName = "System.String" });
        var derived = new TypeDef { Name = "GcLeaf", Namespace = "Cases", BaseTypeName = "Cases.GcBase" };
        derived.BaseType = baseT;
        derived.InstanceFields.Add(new FieldDef { Name = "extra", Owner = derived, TypeKind = FieldTypeKind.Reference, FieldTypeName = "System.String" });
        return (baseT, derived);
    }

    [Fact]
    public void Layout_DerivedOwnRefField_ContinuesBaseSlotGrid()
    {
        var (baseT, derived) = MakeDerivedWithOwnRefField();
        TypeEmitter.LayoutInstanceFields(baseT);
        TypeEmitter.LayoutInstanceFields(derived);
        // base: header slot 0, name slot 1 (ref). instanceSize = 2*ptr.
        Assert.Equal(1, baseT.InstanceFields[0].SlotIndex);
        Assert.Equal(0b10u, baseT.RefFieldBitmap);
        Assert.Equal(2 * IntPtr.Size, baseT.InstanceSize);
        // derived: own extra slot starts AFTER the base region (slot 2).
        Assert.Equal(2, derived.InstanceFields[0].SlotIndex);
        Assert.Equal(0b110u, derived.RefFieldBitmap); // base bit1 + own bit2
        Assert.Equal(2, derived.RefFieldCount);       // inherited (1) + own (1)
        Assert.Equal(3 * IntPtr.Size, derived.InstanceSize);
    }

    [Fact]
    public void EmitClassStruct_DerivedOwnRefField_EmbedsBaseFieldsInline()
    {
        var (baseT, derived) = MakeDerivedWithOwnRefField();
        var code = TypeEmitter.EmitClassStruct(derived);
        // The base field member must appear (inline, at its base offset) so the
        // C++ struct offsets match the slot grid: extra at offset 2*ptr.
        Assert.Contains("Il2CppObject* name;", code);
        Assert.Contains("Il2CppObject* extra;", code);
        Assert.Contains($"slot 2 at offset {2 * IntPtr.Size}", code);
        Assert.Contains("Il2CppObject _obj;", code);
    }

    [Fact]
    public void EmitClassStruct_DerivedNoOwnRefField_StillEmbedsBaseFields()
    {
        // An EMPTY derived class (rf-props PropHost shape) must also embed the
        // base chain fields inline -- otherwise the struct sizeof undercounts
        // the object and any subsequent own-field layout would collide.
        var baseT = new TypeDef { Name = "GcBase", Namespace = "Cases" };
        baseT.InstanceFields.Add(new FieldDef { Name = "name", Owner = baseT, TypeKind = FieldTypeKind.Reference, FieldTypeName = "System.String" });
        var derived = new TypeDef { Name = "GcLeaf", Namespace = "Cases", BaseTypeName = "Cases.GcBase" };
        derived.BaseType = baseT;
        TypeEmitter.LayoutInstanceFields(baseT);
        TypeEmitter.LayoutInstanceFields(derived);
        Assert.Equal(2 * IntPtr.Size, derived.InstanceSize); // base region
        var code = TypeEmitter.EmitClassStruct(derived);
        Assert.Contains("Il2CppObject* name;", code);
    }

    [Fact]
    public void EmitClassStruct_DerivedShadowsBaseField_DisambiguatesMemberNames()
    {
        // reflprop2 shape: PropBase2.BaseProp (auto-property backing field)
        // and PropDerived2's `new int BaseProp` both sanitize to
        // _BaseProp_k__BackingField. The derived struct must keep its OWN
        // field at the canonical name (access sites emit it by name) while
        // the INLINE base copy (layout-only) is disambiguated -- the C++
        // struct cannot carry two same-named members.
        var baseT = new TypeDef { Name = "PropBase2", Namespace = "Cases" };
        baseT.InstanceFields.Add(new FieldDef { Name = "_BaseProp_k__BackingField", Owner = baseT, TypeKind = FieldTypeKind.Int32 });
        var derived = new TypeDef { Name = "PropDerived2", Namespace = "Cases", BaseTypeName = "Cases.PropBase2" };
        derived.BaseType = baseT;
        derived.InstanceFields.Add(new FieldDef { Name = "_BaseProp_k__BackingField", Owner = derived, TypeKind = FieldTypeKind.Int32 });
        TypeEmitter.LayoutInstanceFields(baseT);
        TypeEmitter.LayoutInstanceFields(derived);
        Assert.Equal(3 * IntPtr.Size, derived.InstanceSize); // base slot1 + own slot2
        var code = TypeEmitter.EmitClassStruct(derived);
        // own field keeps the canonical member name; the base copy is
        // disambiguated with its slot index.
        Assert.Contains("int32_t _BaseProp_k__BackingField; // slot 2 at offset", code);
        Assert.Contains("_BaseProp_k__BackingField_slot1; // slot 1 at offset", code);
        Assert.Contains("Il2CppObject _obj;", code);
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

    //--------------------------------------------------------------------------
    // GN-20.4 F1 -- VALUE TYPES take the net48 sequential (compact) layout.
    // A struct is handed to native code BY POINTER (P/Invoke out/ref parameter,
    // pointer return, marshal buffer), so the field offsets and instanceSize
    // must be the compact byte layout the native callee writes through -- NOT
    // the whole-pointer slot grid. The gdifont TEXTMETRIC probe (15 int fields,
    // [StructLayout(Sequential)]) was advertised as instanceSize 120 with
    // 8-byte strides while the real C++ object is 60 bytes: the metadata and
    // the struct disagreed, and reflection read the wrong slots.
    //--------------------------------------------------------------------------

    // Fixture: the gdifont TEXTMETRIC shape -- 15 sequential int fields.
    private static TypeDef MakeWideIntStruct()
    {
        var t = new TypeDef { Name = "TEXTMETRIC", Namespace = "Cases.Gdifont", IsValueType = true };
        for (var i = 0; i < 15; i++)
            t.InstanceFields.Add(new FieldDef { Name = "F" + i, Owner = t, TypeKind = FieldTypeKind.Int32 });
        return t;
    }

    [Fact]
    public void Layout_ValueType_IntFields_PackCompact()
    {
        var t = MakeWideIntStruct();
        TypeEmitter.LayoutInstanceFields(t);
        // compact 4-byte strides, exactly the net48 sequential layout
        Assert.Equal(60, t.InstanceSize);
        for (var i = 0; i < 15; i++)
        {
            Assert.Equal(4 * i, t.InstanceFields[i].ByteOffset);
            Assert.Equal((4 * i) / IntPtr.Size, t.InstanceFields[i].SlotIndex);
        }
        Assert.Equal(0u, t.RefFieldBitmap);
        Assert.Equal(0, t.RefFieldCount);
    }

    [Fact]
    public void Layout_ValueType_IsIdempotent()
    {
        var t = MakeWideIntStruct();
        TypeEmitter.LayoutInstanceFields(t);
        TypeEmitter.LayoutInstanceFields(t); // second call must not shift anything
        Assert.Equal(60, t.InstanceSize);
        Assert.Equal(44, t.InstanceFields[11].ByteOffset);
    }

    [Fact]
    public void Layout_ValueType_ReferenceField_LandsOnWholeSlot()
    {
        // { int a; int b; Node c; } -> C++ packs c at byte 8 (its natural
        // alignment), NOT at slot 2 / offset 16. The bitmap must follow the
        // real offset so the collector's slot scan finds the reference.
        var t = new TypeDef { Name = "Bag", Namespace = "Cases", IsValueType = true };
        t.InstanceFields.Add(new FieldDef { Name = "a", Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "b", Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "c", Owner = t, TypeKind = FieldTypeKind.Reference, FieldTypeName = "Cases.Node" });
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(0, t.InstanceFields[0].ByteOffset);
        Assert.Equal(4, t.InstanceFields[1].ByteOffset);
        Assert.Equal(IntPtr.Size, t.InstanceFields[2].ByteOffset);
        Assert.Equal(0b10u, t.RefFieldBitmap); // bit 1 = the reference at byte 8
        Assert.Equal(1, t.RefFieldCount);
        Assert.Equal(2 * IntPtr.Size, t.InstanceSize); // 8 + 8 bytes of payload
    }

    [Fact]
    public void Layout_ValueType_PointerCarrierField_KeepsFullWidth()
    {
        // { IntPtr h; int n; } -> h at 0 (8), n at 8 (4), size 16 (aligned to 8).
        var t = new TypeDef { Name = "HandleAndCount", Namespace = "Cases", IsValueType = true };
        t.InstanceFields.Add(new FieldDef { Name = "h", Owner = t, TypeKind = FieldTypeKind.NativeInt, FieldTypeName = "System.IntPtr" });
        t.InstanceFields.Add(new FieldDef { Name = "n", Owner = t, TypeKind = FieldTypeKind.Int32 });
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(0, t.InstanceFields[0].ByteOffset);
        Assert.Equal(IntPtr.Size, t.InstanceFields[1].ByteOffset);
        Assert.Equal(2 * IntPtr.Size, t.InstanceSize);
        Assert.Equal(0u, t.RefFieldBitmap);
    }

    [Fact]
    public void Layout_ValueType_AloneUsesSequentialLayout_NotSlotGrid()
    {
        // The two int fields of a struct must NOT be reported 8 bytes apart
        // (that is the class/reference-type slot grid, wrong for a struct).
        var t = new TypeDef { Name = "Point", Namespace = "Calc", IsValueType = true };
        t.InstanceFields.Add(new FieldDef { Name = "X", Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "Y", Owner = t, TypeKind = FieldTypeKind.Int32 });
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(8, t.InstanceSize);
        Assert.Equal(4, t.InstanceFields[1].ByteOffset);
    }

    [Fact]
    public void EmitValueTypeStruct_OffsetCommentsMatchRealOffsets()
    {
        var t = MakeWideIntStruct();
        var code = TypeEmitter.EmitClassStruct(t);
        // no object header for a value type; offsets are the compact ones
        Assert.Contains("// Sequential value layout (net48): compact byte offsets", code);
        Assert.Contains("int32_t F0; // slot 0 at offset 0 (value)", code);
        Assert.Contains("int32_t F1; // slot 0 at offset 4 (value)", code);
        Assert.Contains("int32_t F14; // slot 7 at offset 56 (value)", code);
        Assert.DoesNotContain("Il2CppObject _obj;", code);
    }

    [Fact]
    public void EmitValueTypeDescriptor_ReportsCompactInstanceSize()
    {
        var t = MakeWideIntStruct();
        var code = TypeEmitter.EmitClassDescriptor(t);
        Assert.Contains("    60,                                            // instanceSize", code);
        Assert.DoesNotContain("    120,", code);
    }

    [Fact]
    public void EmitValueTypeTypeInfo_FieldOffsetsAreCompact()
    {
        var t = MakeWideIntStruct();
        var code = TypeEmitter.EmitTypeInfo(t, null);
        Assert.Contains("\"F0\", \"System.Int32\", 0,", code);
        Assert.Contains("\"F1\", \"System.Int32\", 4,", code);
        Assert.Contains("\"F14\", \"System.Int32\", 56,", code);
        Assert.DoesNotContain("\"F1\", \"System.Int32\", 8,", code);
    }

    // B3 (LOUD P0-05/06/08/09): System.Guid is a 16-byte VALUE (guid.h
    // Il2CppGuid), never Il2CppObject* and never a 4-byte int slot. The
    // emitted member keeps the value shape and the following field keeps the
    // net48 offset (16).
    [Fact]
    public void EmitClassStruct_GuidValueField_UsesIl2CppGuid()
    {
        var t = new TypeDef { Name = "GuidHolder", Namespace = "Cases", IsValueType = true };
        t.InstanceFields.Add(new FieldDef
        {
            Name = "G", Owner = t, TypeKind = FieldTypeKind.Guid, FieldTypeName = "System.Guid",
        });
        t.InstanceFields.Add(new FieldDef { Name = "N", Owner = t, TypeKind = FieldTypeKind.Int32 });
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(0, t.InstanceFields[0].ByteOffset);
        Assert.Equal(16, t.InstanceFields[1].ByteOffset);
        Assert.Equal(20, t.InstanceSize);
        Assert.Equal(0u, t.RefFieldBitmap); // a Guid is NOT a GC reference
        var code = TypeEmitter.EmitClassStruct(t);
        Assert.Contains("il2cpp::Il2CppGuid G;", code);
        Assert.Contains("int32_t N;", code);
        Assert.DoesNotContain("Il2CppObject* G;", code);
    }

    // A reference-type Guid field occupies TWO pointer-size slots (16 bytes on
    // the x64 slot grid), so the next field's slot must follow the value.
    [Fact]
    public void Layout_RefTypeGuidField_AdvancesTwoSlots()
    {
        var t = new TypeDef { Name = "Holder", Namespace = "Cases" };
        t.InstanceFields.Add(new FieldDef
        {
            Name = "G", Owner = t, TypeKind = FieldTypeKind.Guid, FieldTypeName = "System.Guid",
        });
        t.InstanceFields.Add(new FieldDef { Name = "N", Owner = t, TypeKind = FieldTypeKind.Int32 });
        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(1, t.InstanceFields[0].SlotIndex);
        Assert.Equal(3, t.InstanceFields[1].SlotIndex);
        Assert.Equal(4 * IntPtr.Size, t.InstanceSize);
    }
}
