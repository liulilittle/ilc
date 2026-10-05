using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.List dispatch tests: prove the System.Collections.Generic.List`1
// domain (MethodMap.List.cs -> TryMapList) maps the int32 AOT expansion to
// the runtime icalls declared in src/runtime/include/internal/listi4.h
// (namespace il2cpp -- note: NOT il2cpp::runtime):
//
//   get_Count()        -> il2cpp::il2cpp_list_i4_get_count
//   Clear()            -> il2cpp::il2cpp_list_i4_clear
//   Add(int32)         -> il2cpp::il2cpp_list_i4_add
//   get_Item(int32)    -> il2cpp::il2cpp_list_i4_get_item
//   set_Item(int32, T) -> il2cpp::il2cpp_list_i4_set_item
//   Insert(int32, T)   -> il2cpp::il2cpp_list_i4_insert
//   Remove(T)          -> il2cpp::il2cpp_list_i4_remove
//   ToArray()          -> il2cpp::il2cpp_list_i4_to_array
//   RemoveAt(int32)    -> il2cpp::il2cpp_list_i4_remove_at
//   IndexOf(int32)     -> il2cpp::il2cpp_list_i4_index_of
//   Contains(int32)    -> il2cpp::il2cpp_list_i4_contains
//   .ctor(int32)       -> il2cpp::il2cpp_list_i4_create_with_capacity
//   .ctor()            -> il2cpp::il2cpp_list_i4_create
//
// The signatures mirror REAL genlist.exe blobs (probe-verified): instance
// methods carry the 0x20 (HAS_THIS) header, and T-bearing parameters appear
// as ELEMENT_TYPE_VAR (0x13 <index>) -- the int32 instantiation lives in the
// TypeSpec parent, which the mapping never sees. get_Item's RETURN type is T
// (0x13): its VAR index leaks into ps (RawSignature's ret handling does not
// consume a VAR-return index), so get_Item guards on count only.
//
// Receiver handling: a List<T> instance rides in a managed reference slot
// (Il2CppObject*), so the instance receiver cast is "" (pass-through) and
// value arguments cast with "i".
//
// Instantiation guard: the int32 AOT expansion ("List`1_int") maps to the
// il2cpp_list_i4_* value domain; the reference instantiations ("_class",
// "_string", "_object", "_array" -- every reference metadata form) map to
// the il2cpp_list_obj_* reference domain (listobj.h). Any OTHER
// instantiation (a value-type element -- "_long" etc.) is known-unwireable
// and must fail LOUDLY (NotSupportedException), never silently fall to the
// generated-name path (and the .ctor must not silently take the i4 create).
// A T-shaped parameter that is not VAR (0x13) is not the List`1 definition
// signature at all and must also be LOUD.
public class MethodMapTestsList
{
    //--------------------------------------------------------------------------
    // int32 AOT expansion -- instance members map to il2cpp_list_i4_*
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_List_GetCount_MapsToI4GetCount()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Count", Sig(0x20,0x00,0x08)),
            "get_Count", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_List_Clear_MapsToI4Clear()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Clear", Sig(0x20,0x00,0x01)),
            "Clear", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_clear", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_List_Add_MapsToI4Add()
    {
        // instance void Add(T): the real blob is 20 01 01 13 00 (param VAR 0)
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x13,0x00)),
            "Add", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);   // receiver: managed ref, no cast
        Assert.Equal("i", resolved.ParamTypeNames![1]);  // int32 value
    }

    [Fact]
    public void Resolve_List_GetItem_MapsToI4GetItem()
    {
        // instance T get_Item(int32): real blob 20 01 13 00 08 (ret VAR 0).
        // The VAR-return index leaks into ps (RawSignature does not consume
        // it) -- the mapping guards on count, which is decoded correctly.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_get_item", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_List_RemoveAt_MapsToI4RemoveAt()
    {
        // instance void RemoveAt(int32): real blob 20 01 01 08 (plain int)
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "RemoveAt", Sig(0x20,0x01,0x01,0x08)),
            "RemoveAt", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_remove_at", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_List_IndexOf_MapsToI4IndexOf()
    {
        // instance int32 IndexOf(T): real blob 20 01 08 13 00 (param VAR 0)
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "IndexOf", Sig(0x20,0x01,0x08,0x13,0x00)),
            "IndexOf", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_index_of", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_List_Contains_MapsToI4Contains()
    {
        // instance bool Contains(T): real blob 20 01 02 13 00 (param VAR 0)
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Contains", Sig(0x20,0x01,0x02,0x13,0x00)),
            "Contains", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_contains", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    //--------------------------------------------------------------------------
    // Reference-element instantiations -- the listobj reference domain
    // (src/runtime/include/internal/listobj.h). The TypeSpec decoder names the
    // element by its metadata form: CLASS (0x12) -> "_class", STRING (0x0E) ->
    // "_string", OBJECT (0x1C) -> "_object", SZARRAY (0x1D) -> "_array". All
    // four are managed references sharing the Il2CppObject* slot ABI, so every
    // one of them must route to il2cpp_list_obj_*.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_List_GetCount_StringReceiver_MapsToObjGetCount()
    {
        // get_Count has no T-bearing parameter, so the domain selection falls
        // back to the folded receiver instance name.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Count", Sig(0x20,0x00,0x08)),
            "get_Count", receiverType: "System.Collections.Generic.List`1_string");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_List_Add_StringReceiver_MapsToObjAdd()
    {
        // Add(T) with a List`1_string receiver: the element crosses as a
        // managed reference ("" passthrough -- no int32 cast).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x13,0x00)),
            "Add", receiverType: "System.Collections.Generic.List`1_string");

        Assert.Equal("il2cpp::il2cpp_list_obj_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_List_GetItem_ObjectReceiver_MapsToObjGetItem()
    {
        // get_Item on a List`1_object receiver returns the element as a
        // managed reference (ReturnsRef true).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_object");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_item", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_List_Add_ArrayReceiver_MapsToObjAdd()
    {
        // SZARRAY element (0x1D -> "_array"), e.g. List<string[]>: still a
        // managed reference slot.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x13,0x00)),
            "Add", receiverType: "System.Collections.Generic.List`1_array");

        Assert.Equal("il2cpp::il2cpp_list_obj_add", resolved.CppName);
        Assert.Equal("", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_List_Ctor_StringReceiver_MapsToObjCreate()
    {
        // Parameterless .ctor on a reference instantiation creates through the
        // listobj domain (the newobj temp is dropped; the create icall returns
        // the new object).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", ".ctor", Sig(0x20,0x00,0x01)),
            ".ctor", receiverType: "System.Collections.Generic.List`1_string");

        Assert.Equal("il2cpp::il2cpp_list_obj_create", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("drop", resolved.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // Instantiation guard -- an instantiation that is neither the int32 AOT
    // expansion nor a reference element is LOUD, not silent
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_List_Ctor_LongReceiver_MapsToObjCreate()
    {
        // G4: List<long> rides the listobj storage domain -- the parameterless
        // ctor creates through the object-domain create (the *_long value
        // icalls box/unbox at the member boundary).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", ".ctor", Sig(0x20,0x00,0x01)),
            ".ctor", receiverType: "System.Collections.Generic.List`1_long");

        Assert.Equal("il2cpp::il2cpp_list_obj_create", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_List_Ctor_DoubleReceiver_MapsToObjCreate()
    {
        // G4: List<double> rides the listobj storage domain -- the parameterless
        // ctor creates through the object-domain create (the *_double value
        // icalls box/unbox at the member boundary).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", ".ctor", Sig(0x20,0x00,0x01)),
            ".ctor", receiverType: "System.Collections.Generic.List`1_double");

        Assert.Equal("il2cpp::il2cpp_list_obj_create", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_List_Ctor_NestedListReceiver_MapsToObjCreate()
    {
        // G4: List<List<int>> -- the nested GenericInst element is a managed
        // reference, so the instantiation rides the listobj domain (its
        // instance name carries the fully nested suffix).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", ".ctor", Sig(0x20,0x00,0x01)),
            ".ctor", receiverType: "System.Collections.Generic.List`1_System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_obj_create", resolved.CppName);
    }

    [Fact]
    public void Resolve_List_GetItem_NestedListReceiver_TracksInnerList()
    {
        // The get_Item result tracks the INNER instantiation name so member
        // calls on the returned list resolve against List<int>.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_item", resolved.CppName);
        Assert.Equal("System.Collections.Generic.List`1_int", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_List_Add_DoubleReceiver_MapsToAddDouble()
    {
        // instance void Add(T): real blob 20 01 01 13 00; the double domain
        // boxes on entry (icall_box_double), so the value cast is "d".
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x13,0x00)),
            "Add", receiverType: "System.Collections.Generic.List`1_double");

        Assert.Equal("il2cpp::il2cpp_list_obj_add_double", resolved.CppName);
        Assert.Equal(new[] { "", "d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_List_GetItem_DoubleReceiver_ResultKindDouble()
    {
        // instance T get_Item(int32): real blob 20 01 13 00 08; the element
        // leaves as the double carrier (ResultKind "d" -- the emitter declares
        // a double local and unboxes on exit).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_double");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_item_double", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_List_Add_NonVarTShape_ThrowsLoud()
    {
        // A string-shaped parameter (0x0E) is NOT the List`1 definition
        // signature (T always arrives as VAR 0x13) -- LOUD, not mapped.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x0E)), "Add",
                receiverType: "System.Collections.Generic.List`1_int"));

        Assert.Contains("System.Collections.Generic.List`1::Add", ex.Message);
    }

    //--------------------------------------------------------------------------
    // D-LISTQ-1..5: the previously-unwired members now map (2026-08-12)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_List_SetItem_MapsToI4SetItem()
    {
        // instance void set_Item(int32 index, T value): index is plain int32
        // (0x08), the value is T and arrives as VAR (0x13 <0>) -- the same
        // T-bearing convention as Add's value.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "set_Item", Sig(0x20,0x02,0x01,0x08,0x13,0x00)),
            "set_Item", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_set_item", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
        Assert.Equal("i", resolved.ParamTypeNames![2]);
    }

    [Fact]
    public void Resolve_List_Insert_MapsToI4Insert()
    {
        // instance void Insert(int32 index, T value): same 08-13 shape as
        // set_Item (index is plain int32, value is T).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Insert", Sig(0x20,0x02,0x01,0x08,0x13,0x00)),
            "Insert", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_insert", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("i", resolved.ParamTypeNames![2]);
    }

    [Fact]
    public void Resolve_List_Remove_MapsToI4Remove()
    {
        // instance bool Remove(T value): the value is T (VAR 0x13), like
        // Add/IndexOf/Contains.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Remove", Sig(0x20,0x01,0x02,0x13,0x00)),
            "Remove", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_remove", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_List_ToArray_MapsToI4ToArray()
    {
        // instance T[] ToArray(): zero params; the SZARRAY return's element
        // byte is consumed by RawSignature, so ps is empty. The result is
        // tracked as System.Int32[] (downstream Length/indexer resolution).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "ToArray", Sig(0x20,0x00,0x1D,0x13,0x00)),
            "ToArray", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_to_array", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("System.Int32[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_List_CapacityCtor_MapsToI4CreateWithCapacity()
    {
        // instance void .ctor(int32 capacity): the capacity parameter is
        // T-free int32 (0x08); the newobj temp is dropped, the capacity is
        // cast "i", and the runtime icall takes the capacity only.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", ".ctor", Sig(0x20,0x01,0x01,0x08)),
            ".ctor", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_create_with_capacity", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("drop", resolved.ParamTypeNames![0]); // newobj temp dropped
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    //--------------------------------------------------------------------------
    // Enumerator dispatch over a reference List receiver: the tracked
    // pseudo-type carries the element form ("...List`1.Enumerator_string"),
    // so get_Current returns a managed reference and the listobj enumerator
    // icalls are selected (listrefdeep foreach).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ListEnumerator_StringReceiver_GetCurrent_MapsToObj()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1+Enumerator", "get_Current", Sig(0x20,0x00,0x13,0x00)),
            "get_Current", receiverType: "System.Collections.Generic.List`1.Enumerator_string");

        Assert.Equal("il2cpp::il2cpp_list_obj_enumerator_get_current", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ListEnumerator_StringReceiver_MoveNext_MapsToObj()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1+Enumerator", "MoveNext", Sig(0x20,0x00,0x02)),
            "MoveNext", receiverType: "System.Collections.Generic.List`1.Enumerator_string");

        Assert.Equal("il2cpp::il2cpp_list_obj_enumerator_move_next", resolved.CppName);
        Assert.False(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_IDisposable_StringEnumeratorDispose_MapsToObj()
    {
        var resolved = ResolveMember(Synthesize("System", "IDisposable", "Dispose", Sig(0x20,0x00,0x01)),
            "Dispose", receiverType: "System.Collections.Generic.List`1.Enumerator_string");

        Assert.Equal("il2cpp::il2cpp_list_obj_enumerator_dispose", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Generic interface owners over a reference List receiver
    // (TryMapListInterface): `ilist[i]` / `ilist.Count` on IList`1<T> where T
    // is a reference type. The tracked element type follows the receiver
    // instance suffix so downstream member resolution sees the right type.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IListGetItem_StringListReceiver_MapsToObjGetItem()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_string");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_item", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ICollectionGetCount_ClassListReceiver_MapsToObjGetCount()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "ICollection`1", "get_Count", Sig(0x20,0x00,0x08)),
            "get_Count", receiverType: "System.Collections.Generic.List`1_class");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_count", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_IListGetItem_ForeignReceiver_ThrowsLoud()
    {
        // A receiver-typed attempt that still has no mapping is a REAL gap:
        // BclResolve rejects it LOUDLY (MethodRefResolver.cs:721-740) instead
        // of emitting a phantom generated name. "_long" is neither the int32
        // expansion nor a reference List instance.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
                "get_Item", receiverType: "System.Collections.Generic.List`1_long"));

        Assert.Contains("IList`1::get_Item", ex.Message);
        Assert.Contains("System.Collections.Generic.List`1_long", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dotted user-class instantiations (A5/B1/B6 myIList/baudit2/
    // ilistException): "List`1_System.Exception" / "List`1_Cases.Baudit2.P"
    // ride the SHARED listobj reference domain (the Queue dotted-tag
    // precedent) -- every reference element shares the Il2CppObject* slot
    // ABI. BCL struct full names stay LOUD (no per-type box domain).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_List_Add_DottedClassReceiver_MapsToObjAdd()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x13,0x00)),
            "Add", receiverType: "System.Collections.Generic.List`1_System.Exception");

        Assert.Equal("il2cpp::il2cpp_list_obj_add", resolved.CppName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_List_Ctor_DottedClassReceiver_MapsToObjCreate()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", ".ctor", Sig(0x20,0x00,0x01)),
            ".ctor", receiverType: "System.Collections.Generic.List`1_Cases.Baudit2.P");

        Assert.Equal("il2cpp::il2cpp_list_obj_create", resolved.CppName);
    }

    [Fact]
    public void Resolve_List_GetCount_DottedClassReceiver_MapsToObjGetCount()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Count", Sig(0x20,0x00,0x08)),
            "get_Count", receiverType: "System.Collections.Generic.List`1_System.Exception");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_count", resolved.CppName);
    }

    [Fact]
    public void Resolve_List_GetItem_DottedClassReceiver_ReturnsRefTracked()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_System.Exception");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_item", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Exception", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_IListGetItem_DottedClassReceiver_MapsToObjGetItem()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Sig(0x20,0x01,0x13,0x00,0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_System.Exception");

        Assert.Equal("il2cpp::il2cpp_list_obj_get_item", resolved.CppName);
        Assert.Equal("System.Exception", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_List_Add_DottedStructReceiver_StaysLoud()
    {
        // BCL struct full names are excluded from the dotted rule (no
        // per-type box domain exists) -- they stay LOUD, never silently
        // take the reference domain.
        Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x13,0x00)),
                "Add", receiverType: "System.Collections.Generic.List`1_System.DateTime"));
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
    }

    // Signature blob -- the raw bytes verbatim (the real genlist.exe blobs,
    // probe-verified): header 0x20 (HAS_THIS), param count, return byte,
    // params. T-bearing parameters carry the VAR (0x13) byte AND its
    // compressed index (0x00) -- MethodSignatureCoder.Decode consumes both
    // before BclResolve runs. get_Item's RETURN is T (0x13 0x00) -- that VAR
    // index leaks into RawSignature's ps (it is not consumed for returns).
    private static byte[] Sig(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
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
}

