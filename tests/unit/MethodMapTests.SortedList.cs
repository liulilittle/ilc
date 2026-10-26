using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.SortedList dispatch tests: prove the System.Collections.SortedList
// domain (MethodMap.SortedList.cs -> TryMapSortedList) maps the non-generic
// members to the sorted-array runtime icalls declared in
// src/runtime/include/internal/sortedlist.h (namespace il2cpp::runtime):
//
//   .ctor()                   -> il2cpp_sortedlist_create(self, 0) (AppendZeroArg)
//   .ctor(int32)              -> il2cpp_sortedlist_create(self, capacity)
//   Add(object,object)        -> il2cpp_sortedlist_add
//   set_Item(object,object)   -> il2cpp_sortedlist_set_by_key (insert-or-replace)
//   get_Item(object)          -> il2cpp_sortedlist_get_by_key (null on miss)
//   GetByIndex(int32)         -> il2cpp_sortedlist_get_by_index
//   GetKey(int32)             -> il2cpp_sortedlist_get_key_at
//   GetValueAt(int32)         -> il2cpp_sortedlist_get_value_at
//   IndexOfKey(object)        -> il2cpp_sortedlist_index_of_key
//   ContainsKey(object)       -> il2cpp_sortedlist_contains_key
//   Remove(object)            -> il2cpp_sortedlist_remove_by_key
//   RemoveAt(int32)           -> il2cpp_sortedlist_remove_at
//   get_Count()               -> il2cpp_sortedlist_get_count
//   get_Capacity()            -> il2cpp_sortedlist_get_capacity
//   get_Keys()                -> il2cpp_sortedlist_get_keys  (snapshot)
//   get_Values()              -> il2cpp_sortedlist_get_values (snapshot)
//   Clear()                   -> il2cpp_sortedlist_clear
//
// The MemberRef signatures are the CONCRETE net48 SortedList signatures (the
// object/key+value params arrive as ELEMENT_TYPE_OBJECT 0x1C, int32 as 0x08).
// Keys/values are Il2CppObject* slots (the IL boxes value keys before the
// call), so object args carry "" and int32 args carry "i".
public class MethodMapTestsSortedList
{
    //--------------------------------------------------------------------------
    // SortedList -- ctor (parameterless appends the capacity 0)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SortedList_CtorParameterless_AppendsZeroArgument()
    {
        // real dictcase blob: 20 00 01 (HAS_THIS, 0 params, void)
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.True(resolved.AppendZeroArg);
        Assert.False(resolved.PrependZeroArg);
    }

    [Fact]
    public void Resolve_SortedList_CtorCapacity_MapsToCreate()
    {
        // capacity ctor: the int32 param is 0x08
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x01, 0x01, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_CtorTwoArgs_ThrowsLoud()
    {
        // .ctor(object, object) is not a net48 SortedList overload -- LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize(".ctor", Blob(0x20, 0x02, 0x01, 0x1C, 0x1C)), ".ctor"));

        Assert.Contains("SortedList ctor", ex.Message);
    }

    //--------------------------------------------------------------------------
    // SL-CTOR: the net48 (IComparer) / (IComparer,int) / (IDictionary) /
    // (IDictionary,IComparer) ctor overloads. Both interface kinds carry the
    // CLASS byte 0x12, so the DECODED parameter-type text discriminates (the
    // synthesized PE adds the IComparer / IDictionary TypeRefs as rows 2 / 3).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SortedList_CtorIComparer_MapsToCreateWithComparer()
    {
        // .ctor(IComparer): 20 01 01 12 09 (TypeRef row 2 = IComparer)
        var resolved = ResolveMember(SynthesizeCtor(".ctor", Blob(0x20, 0x01, 0x01, 0x12, 0x09)), ".ctor");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_SortedList_CtorIComparerCapacity_MapsToCreateWithComparer()
    {
        // .ctor(IComparer, int): 20 02 01 12 09 08
        var resolved = ResolveMember(SynthesizeCtor(".ctor", Blob(0x20, 0x02, 0x01, 0x12, 0x09, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create_with_comparer", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_CtorIDictionary_MapsToCreateFromDict()
    {
        // .ctor(IDictionary): 20 01 01 12 0D (TypeRef row 3 = IDictionary)
        var resolved = ResolveMember(SynthesizeCtor(".ctor", Blob(0x20, 0x01, 0x01, 0x12, 0x0D)), ".ctor");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create_from_dict", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "nullptr" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_SortedList_CtorIDictionaryIComparer_MapsToCreateFromDict()
    {
        // .ctor(IDictionary, IComparer): 20 02 01 12 0D 12 09
        var resolved = ResolveMember(SynthesizeCtor(".ctor", Blob(0x20, 0x02, 0x01, 0x12, 0x0D, 0x12, 0x09)), ".ctor");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create_from_dict", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // SortedList -- key/value pair members (both args object refs "")
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SortedList_Add_MapsToSortedListAdd()
    {
        // instance void Add(object, object): 20 02 01 1C 1C
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x1C, 0x1C)), "Add");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_SetItem_MapsToSortedListSetByKey()
    {
        // indexer SET (insert-or-replace): 20 02 01 1C 1C
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x1C, 0x1C)), "set_Item");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_set_by_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_GetItem_MapsToSortedListGetByKey()
    {
        // indexer GET by key: object get_Item(object) -> 20 01 1C 1C
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x1C, 0x1C)), "get_Item");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_get_by_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_IndexOfKey_MapsToSortedListIndexOfKey()
    {
        // int32 IndexOfKey(object): 20 01 08 1C
        var resolved = ResolveMember(Synthesize("IndexOfKey", Blob(0x20, 0x01, 0x08, 0x1C)), "IndexOfKey");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_index_of_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // SortedList -- one-arg object members (ContainsKey / Remove)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SortedList_ContainsKey_MapsToSortedListContainsKey()
    {
        // bool ContainsKey(object): 20 01 02 1C
        var resolved = ResolveMember(Synthesize("ContainsKey", Blob(0x20, 0x01, 0x02, 0x1C)), "ContainsKey");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_contains_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_Remove_MapsToSortedListRemoveByKey()
    {
        // void Remove(object): 20 01 01 1C
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x01, 0x1C)), "Remove");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_remove_by_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // SortedList -- index-based members (int32 arg, object result / void)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("GetByIndex", "il2cpp::runtime::il2cpp_sortedlist_get_by_index")]
    [InlineData("GetKey", "il2cpp::runtime::il2cpp_sortedlist_get_key_at")]
    [InlineData("GetValueAt", "il2cpp::runtime::il2cpp_sortedlist_get_value_at")]
    public void Resolve_SortedList_IndexObjectMembers_MapToIndexIcalls(string name, string expectedIcall)
    {
        // object GetByIndex/GetKey/GetValueAt(int32): 20 01 1C 08
        var resolved = ResolveMember(Synthesize(name, Blob(0x20, 0x01, 0x1C, 0x08)), name);

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_RemoveAt_MapsToSortedListRemoveAt()
    {
        // void RemoveAt(int32): 20 01 01 08
        var resolved = ResolveMember(Synthesize("RemoveAt", Blob(0x20, 0x01, 0x01, 0x08)), "RemoveAt");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_remove_at", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_SetByIndex_MapsToSortedListSetAt()
    {
        // void SetByIndex(int32, object): 20 02 01 08 1C
        var resolved = ResolveMember(Synthesize("SetByIndex", Blob(0x20, 0x02, 0x01, 0x08, 0x1C)), "SetByIndex");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_set_at", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "i", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // SortedList -- parameterless members (Count / Capacity / Keys / Values / Clear)
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("get_Count", "il2cpp::runtime::il2cpp_sortedlist_get_count", false)]
    [InlineData("get_Capacity", "il2cpp::runtime::il2cpp_sortedlist_get_capacity", false)]
    [InlineData("get_Keys", "il2cpp::runtime::il2cpp_sortedlist_get_keys", true)]
    [InlineData("get_Values", "il2cpp::runtime::il2cpp_sortedlist_get_values", true)]
    public void Resolve_SortedList_ParameterlessMembers_MapToIcalls(string name, string expectedIcall, bool returnsRef)
    {
        // int32 get_Count/get_Capacity(): 20 00 08; object get_Keys/get_Values(): 20 00 1C
        var blob = returnsRef ? Blob(0x20, 0x00, 0x1C) : Blob(0x20, 0x00, 0x08);
        var resolved = ResolveMember(Synthesize(name, blob), name);

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(returnsRef, resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedList_Clear_MapsToSortedListClear()
    {
        // void Clear(): 20 00 01
        var resolved = ResolveMember(Synthesize("Clear", Blob(0x20, 0x00, 0x01)), "Clear");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_clear", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Negative: unwired members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SortedList_UnwiredMember_FallsBackToGeneratedName()
    {
        // SetAt (index-based value replace) is NOT in the wired surface -- it
        // must fall to the generated-name path, not be mis-mapped.
        var resolved = ResolveMember(Synthesize("SetAt", Blob(0x20, 0x02, 0x01, 0x08, 0x1C)), "SetAt");

        Assert.StartsWith("System_Collections_SortedList_SetAt_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
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

    // The raw MemberRef signature blob: header 0x20 (HAS_THIS), param count,
    // return byte, then the parameter types with their trailing data.
    private static byte[] Blob(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections"),
            mdb.GetOrAddString("SortedList"));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    // A ctor MemberRef whose CLASS parameters name the IComparer (TypeRef row 2)
    // / IDictionary (TypeRef row 3) interfaces, so MethodSignatureCoder's
    // decoded parameter types resolve the (IComparer) vs (IDictionary) overload.
    private static MemoryStream SynthesizeCtor(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections"),
            mdb.GetOrAddString("SortedList"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections"), mdb.GetOrAddString("IComparer"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections"), mdb.GetOrAddString("IDictionary"));
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
