using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// G4 spec (RED until the generic SortedList`2<string,int> domain lands): the
// instantiated SortedList<string,int> must ride the non-generic sorted-array
// storage through a string/int wrapper layer (icall_sortedlist_si_*), exactly
// like Queue`1<int> rides queue_class (icall_queue_int_*) and Dictionary`2
// <string,string> rides hashtable_class (icall_dict_ss_*).
//
// Contract under test (instName = System.Collections.Generic.SortedList`2
// _string_int, VAR0 = TKey=string ref, VAR1 = TValue=int32 -- real net48
// MemberRef signatures carry VAR bytes 0x13 00 / 0x13 01):
//
//   .ctor()             -> il2cpp_sortedlist_create(self, 0) (AppendZeroArg)
//   .ctor(int32)        -> il2cpp_sortedlist_create(self, capacity)
//   Add(string,int)     -> icall_sortedlist_si_add          ("" "" "i": wrapper boxes)
//   set_Item(string,int)-> icall_sortedlist_si_set_by_key   ("" "" "i")
//   get_Item(string)    -> icall_sortedlist_si_get_by_key   (int32 result, wrapper unboxes)
//   ContainsKey(string) -> icall_sortedlist_si_contains_key (bool result)
//   Remove(string)      -> icall_sortedlist_si_remove       (bool result -- wrapper maps void->bool)
//   get_Count()         -> il2cpp_sortedlist_get_count      (shared, no boxing involved)
//   get_Keys()          -> icall_sortedlist_si_get_keys     (string[] snapshot)
//
// A different instantiation (e.g. SortedList<int,int>) must NOT mis-map: it
// falls back to the generated-name path (loud build failure until wired).
public class MethodMapTestsSortedListGeneric
{
    public const string SiReceiver = "System.Collections.Generic.SortedList`2_string_int";

    [Fact]
    public void Resolve_SortedListSi_CtorParameterless_AppendsZeroArgument()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_SortedListSi_CtorCapacity_MapsToCreate()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x01, 0x01, 0x08)), ".ctor",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_Add_BoxesIntValueIntoNonGenericAdd()
    {
        // instance void Add(TKey, TValue): 20 02 01 13 00 13 01
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_SetItem_BoxesIntValueIntoSetByKey()
    {
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_set_by_key", resolved.CppName);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_GetItem_UnboxesIntResult()
    {
        // int32 get_Item(TKey): the net48 OPEN signature -- ret VAR1 (13 01)
        // leaks its index byte, param VAR0 (13 00).
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_by_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal("i", resolved.ResultKind);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_ContainsKey_MapsToSiContainsKey()
    {
        // bool ContainsKey(TKey): 20 01 02 13 00
        var resolved = ResolveMember(Synthesize("ContainsKey", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "ContainsKey",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_contains_key", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_Remove_MapsToSiRemoveBool()
    {
        // bool Remove(TKey): 20 01 02 13 00 -- the non-generic remove returns
        // void, so the wrapper must surface the removal flag; bool marshals
        // as int32 with no ResultKind (the HashSet Remove precedent).
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Remove",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_remove", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Null(resolved.ResultKind);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_GetCount_ReusesNonGenericCount()
    {
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_get_count", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_GetKeys_SnapshotsStringArray()
    {
        // IList<TKey> get_Keys(): 20 00 12 05 (the interface-typed result is
        // tracked as the snapshot array the wrapper actually returns, the
        // dictss Values precedent).
        var resolved = ResolveMember(Synthesize("get_Keys", Blob(0x20, 0x00, 0x12, 0x05)), "get_Keys",
            receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_keys", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_IndexSurface_MapsToUnboxedAndRefArms()
    {
        // GetByIndex/GetValueAt(int): TValue -> unboxed wrapper; GetKey(int):
        // TKey -> the core key slot (ref); IndexOfKey: core int32;
        // RemoveAt: core void; get_Capacity: core int32.
        var byIndex = ResolveMember(Synthesize("GetByIndex", Blob(0x20, 0x01, 0x13, 0x01, 0x08)), "GetByIndex", receiverType: SiReceiver);
        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_by_index", byIndex.CppName);
        Assert.Equal("i", byIndex.ResultKind);

        var valueAt = ResolveMember(Synthesize("GetValueAt", Blob(0x20, 0x01, 0x13, 0x01, 0x08)), "GetValueAt", receiverType: SiReceiver);
        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_by_index", valueAt.CppName);

        var key = ResolveMember(Synthesize("GetKey", Blob(0x20, 0x01, 0x13, 0x00, 0x08)), "GetKey", receiverType: SiReceiver);
        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_get_key_at", key.CppName);
        Assert.True(key.ReturnsRef);

        var indexOf = ResolveMember(Synthesize("IndexOfKey", Blob(0x20, 0x01, 0x08, 0x13, 0x00)), "IndexOfKey", receiverType: SiReceiver);
        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_index_of_key", indexOf.CppName);

        var removeAt = ResolveMember(Synthesize("RemoveAt", Blob(0x20, 0x01, 0x01, 0x08)), "RemoveAt", receiverType: SiReceiver);
        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_remove_at", removeAt.CppName);

        var cap = ResolveMember(Synthesize("get_Capacity", Blob(0x20, 0x00, 0x08)), "get_Capacity", receiverType: SiReceiver);
        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_get_capacity", cap.CppName);
    }

    [Fact]
    public void Resolve_SortedListSi_UnwiredInstantiation_RejectsLoudly()
    {
        // SortedList<int,int> has no wrapper layer yet -- a receiver-typed
        // attempt that still has no mapping is a LOUD gap (the
        // MethodMapTests.List.cs ForeignReceiver precedent).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
                receiverType: "System.Collections.Generic.SortedList`2_int_int"));

        Assert.Contains("SortedList`2::Add", ex.Message);
        Assert.Contains("System.Collections.Generic.SortedList`2_int_int", ex.Message);
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

    private static byte[] Blob(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("SortedList`2"));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
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
