using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// G4 spec (RED until the SortedSet<int> domain lands): SortedSet<int> is a
// sorted-unique-int set; it rides the non-generic sorted-array storage
// (sortedlist.h) with BOXED int32 keys -- ascending order comes from the
// core's boxed-Int32 sort contract (sortedlist.h). New wrapper icalls
// (sortedset_obj.h): icall_sortedset_int_{add,contains,remove} convert to/from
// the boxed key slots, get_Min/get_Max unbox GetKey(0)/GetKey(Count-1), and
// GetEnumerator returns the dictsi keycollection wrapper over the boxed-int
// keys SNAPSHOT (the HashSet<int> enumeration precedent -- the tracked token
// reuses Dictionary`2.KeyCollection.Enumerator and the struct members route
// through the sortedset enumerator arm).
//
// instName = System.Collections.Generic.SortedSet`1_int.
public class MethodMapTestsSortedSetGeneric
{
    public const string SsReceiver = "System.Collections.Generic.SortedSet`1_int";

    [Fact]
    public void Resolve_SortedSetInt_CtorParameterless_AppendsZeroArgument()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: SsReceiver);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create", resolved.CppName);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_SortedSetInt_Add_MapsToSortedSetIntAdd()
    {
        // bool Add(T item): 20 01 02 13 00 (false on a duplicate)
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Add",
            receiverType: SsReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedset_int_add", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedSetInt_Contains_MapsToSortedSetIntContains()
    {
        var resolved = ResolveMember(Synthesize("Contains", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Contains",
            receiverType: SsReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedset_int_contains", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedSetInt_Remove_MapsToSortedSetIntRemove()
    {
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Remove",
            receiverType: SsReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedset_int_remove", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_SortedSetInt_GetCount_ReusesSortedListCount()
    {
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: SsReceiver);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_get_count", resolved.CppName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("get_Min", "il2cpp::runtime::icall_sortedset_int_get_min")]
    [InlineData("get_Max", "il2cpp::runtime::icall_sortedset_int_get_max")]
    public void Resolve_SortedSetInt_MinMax_UnboxKeyBounds(string name, string expectedIcall)
    {
        // T get_Min/get_Max(): the VAR-return index byte leaks into ps[0]
        // (the dictsi get_Item precedent) -- guard on count only.
        var resolved = ResolveMember(Synthesize(name, Blob(0x20, 0x00, 0x13, 0x00)), name,
            receiverType: SsReceiver);

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal("i", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_SortedSetInt_GetEnumerator_TracksKeyCollectionEnumerator()
    {
        // Enumerator get_Enumerator(): 20 00 12 05
        var resolved = ResolveMember(Synthesize("GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)), "GetEnumerator",
            receiverType: SsReceiver);

        Assert.Equal("il2cpp::il2cpp_sortedset_int_get_enumerator", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SortedSetInt_UnwiredInstantiation_FallsBackToGeneratedName()
    {
        // No receiver hint: the null-tag path defers to the generated-name
        // fallback (a receiver-typed attempt would be a LOUD P26 gap).
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Add");

        Assert.StartsWith("System_Collections_Generic_SortedSet", resolved.CppName);
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
            mdb.GetOrAddString("SortedSet`1"));
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
