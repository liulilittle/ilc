using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// P3-extra spec: SortedList<string,int>.Values (the net48 LIVE IList<TValue>
// view) + ContainsValue. Contract under test:
//
//   get_Values()        -> icall_sortedlist_si_get_values (returns the list;
//                          tracked pseudo-type
//                          "System.Collections.Generic.SortedList`2.ValueList_string_int")
//   ContainsValue(int)  -> icall_sortedlist_si_contains_value (snapshot scan)
//
// The view members arrive as GENERIC INTERFACE callvirts (the declaring owner
// is the interface, not the class) and are receiver-gated by the pseudo-type
// (TryMapSortedListSiView):
//   IList`1.get_Item            -> icall_sortedlist_si_get_by_index (live, unboxed)
//   ICollection`1.get_Count     -> il2cpp_sortedlist_get_count       (live)
//   IEnumerable`1.GetEnumerator -> icall_sortedlist_si_values_get_enumerator
//                                  (tracks "System.Collections.SortedList.Enumerator")
//   IEnumerator`1.MoveNext/get_Current/Reset on the tracked enumerator:
//     get_Current unboxes (icall_sortedlist_si_enumerator_get_current).
//
// Unknown receivers / instantiations stay LOUD: the P26 M5 guard throws
// NotSupportedException for an un-routed BCL owner rather than silently
// mapping onto the si arms (both foreign cases below assert the throw).
public class MethodMapTestsSortedListValues
{
    public const string SiReceiver = "System.Collections.Generic.SortedList`2_string_int";
    public const string ValuesView = "System.Collections.Generic.SortedList`2.ValueList_string_int";
    public const string ValuesEnumerator = "System.Collections.SortedList.Enumerator";

    [Fact]
    public void Resolve_SortedListSi_GetValues_ReturnsTrackedLiveView()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "SortedList`2", "get_Values", Blob(0x20, 0x00, 0x12, 0x05)),
            "get_Values", receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_values", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(ValuesView, resolved.ReturnTypeName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedListSi_ContainsValue_MapsToSnapshotScan()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "SortedList`2", "ContainsValue", Blob(0x20, 0x01, 0x02, 0x13, 0x01)),
            "ContainsValue", receiverType: SiReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_contains_value", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ValuesView_Indexer_MapsToLiveByIndex()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Blob(0x20, 0x01, 0x08, 0x08)),
            "get_Item", receiverType: ValuesView);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_by_index", resolved.CppName);
        Assert.Equal("i", resolved.ResultKind);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ValuesView_Count_MapsToLiveCount()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "ICollection`1", "get_Count", Blob(0x20, 0x00, 0x08)),
            "get_Count", receiverType: ValuesView);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_get_count", resolved.CppName);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ValuesView_GetEnumerator_TracksSnapshotEnumerator()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "IEnumerable`1", "GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)),
            "GetEnumerator", receiverType: ValuesView);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_values_get_enumerator", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(ValuesEnumerator, resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ValuesEnumerator_Members_MapToCoreSnapshotEnumerator()
    {
        var moveNext = ResolveMember(Synthesize("System.Collections.Generic", "IEnumerator`1", "MoveNext", Blob(0x20, 0x00, 0x02)),
            "MoveNext", receiverType: ValuesEnumerator);
        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", moveNext.CppName);

        var current = ResolveMember(Synthesize("System.Collections.Generic", "IEnumerator`1", "get_Current", Blob(0x20, 0x00, 0x08)),
            "get_Current", receiverType: ValuesEnumerator);
        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_enumerator_get_current", current.CppName);
        Assert.Equal("i", current.ResultKind);

        var reset = ResolveMember(Synthesize("System.Collections.Generic", "IEnumerator`1", "Reset", Blob(0x20, 0x00, 0x01)),
            "Reset", receiverType: ValuesEnumerator);
        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", reset.CppName);
    }

    [Fact]
    public void Resolve_ValuesView_ForeignReceiver_StaysLoud()
    {
        // The non-generic KeyList pseudo-type must NOT ride the si values arms
        // (it dispatches through MethodMap.Collections instead): the un-routed
        // BCL owner stays LOUD (the P26 M5 guard throws).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Blob(0x20, 0x01, 0x08, 0x08)),
                "get_Item", receiverType: "System.Collections.SortedList.KeyList"));
        Assert.Contains("IList`1::get_Item", ex.Message);
    }

    [Fact]
    public void Resolve_ValuesForeignInstantiation_StaysLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "SortedList`2", "get_Values", Blob(0x20, 0x00, 0x12, 0x05)),
                "get_Values", receiverType: "System.Collections.Generic.SortedList`2_int_int"));
        Assert.Contains("get_Values", ex.Message);
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

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
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
