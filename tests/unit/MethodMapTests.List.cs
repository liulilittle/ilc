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
// Instantiation guard: a folded receiver instance name other than
// "System.Collections.Generic.List`1_int" is a known-unwireable instantiation
// and must fail LOUDLY (NotSupportedException), never silently fall to the
// generated-name path. A T-shaped parameter that is not VAR (0x13) is not
// the List`1 definition signature at all and must also be LOUD.
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
    // Instantiation guard -- a non-int32 instantiation is LOUD, not silent
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_List_GetCount_StringReceiver_ThrowsLoud()
    {
        // get_Count has no T-bearing parameter, so the guard falls back to the
        // folded receiver instance name: a List`1_string receiver is LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "List`1", "get_Count", Sig(0x20,0x00,0x08)),
                "get_Count", receiverType: "System.Collections.Generic.List`1_string"));

        Assert.Contains("System.Collections.Generic.List`1::get_Count", ex.Message);
    }

    [Fact]
    public void Resolve_List_Add_StringReceiver_ThrowsLoud()
    {
        // Add(T) with a folded List`1_string receiver -- LOUD (the ps VAR
        // shape cannot distinguish instantiations; the receiver name can).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x13,0x00)),
                "Add", receiverType: "System.Collections.Generic.List`1_string"));

        Assert.Contains("System.Collections.Generic.List`1::Add", ex.Message);
        Assert.Contains("il2cpp_list_i4", ex.Message);
    }

    [Fact]
    public void Resolve_List_Add_NonVarTShape_ThrowsLoud()
    {
        // A string-shaped parameter (0x0E) is NOT the List`1 definition
        // signature (T always arrives as VAR 0x13) -- LOUD, not mapped.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "List`1", "Add", Sig(0x20,0x01,0x01,0x0E)), "Add"));

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

