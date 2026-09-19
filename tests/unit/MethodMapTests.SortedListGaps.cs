using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// G4 residual spec (g4gaps): the two pool-evidenced gaps left after P3/P3-extra.
//
// GAP 1 -- SortedDictionary<string,int>.KeyCollection enumeration.
//   transpile log (g4sorteddict): "SortedDictionary`2+KeyCollection::
//   GetEnumerator ... receiverType='System.String[]'". P3 maps get_Keys to the
//   String[] Keys snapshot, so the pattern-based foreach finds the nested
//   KeyCollection.GetEnumerator owner-receiver pair below. Contract:
//
//     KeyCollection.GetEnumerator (receiver String[]) ->
//         il2cpp_sortedlist_snapshot_get_enumerator
//         tracked "System.Collections.Generic.SortedDictionary`2+KeyCollection.Enumerator"
//     KeyCollection+Enumerator.MoveNext/get_Current/Reset (struct-local
//       address receivers; deref:r) -> the core sortedlist snapshot
//       enumerator icalls (get_Current carries the string reference).
//     IDisposable.Dispose on the tracked enumerator (foreach finally) ->
//       il2cpp_sortedlist_enumerator_dispose (no-op core).
//
//   Unknown receivers stay LOUD (the P26 BCL-owner guard throws
//   NotSupportedException; never a silent mis-map).
//
// GAP 2 -- IList<TKey>.get_Item on the Keys String[] snapshot.
//   transpile log (g4sortedlist2, `sl.Keys[i]`): "IList`1::get_Item ...
//   receiverType='System.String[]'". The tracked snapshot IS an Il2CppArray,
//   so the element read is Array.GetValue's reference-slot read:
//
//     IList`1.get_Item (receiver String[]) -> il2cpp_array_get_value
//       (additional cast codes "a", "i"; reference result).
//
//   The List`1 receiver regression (ilistException) must keep routing to the
//   list arms.
public class MethodMapTestsSortedListGaps
{
    public const string KeysSnapshot = "System.String[]";
    public const string KeyCollection = "System.Collections.Generic.SortedDictionary`2+KeyCollection";
    public const string KeyCollectionEnumerator = "System.Collections.Generic.SortedDictionary`2+KeyCollection+Enumerator";
    public const string TrackedEnumerator = "System.Collections.Generic.SortedDictionary`2+KeyCollection.Enumerator";

    // GAP 1: KeyCollection.GetEnumerator on the tracked Keys snapshot.
    [Fact]
    public void Resolve_SortedDictKeyCollection_GetEnumerator_TracksSnapshotEnumerator()
    {
        var resolved = ResolveMember(SynthesizeNested("System.Collections.Generic", "SortedDictionary`2", "KeyCollection",
                "GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)),
            "GetEnumerator", receiverType: KeysSnapshot);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator", resolved!.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(TrackedEnumerator, resolved.ReturnTypeName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedDictKeyCollection_GetEnumerator_HintLessPass_Defers()
    {
        // The hint-less FIRST pass defers via the fictional BCL fallback
        // (IsFictionalBclFallback) so StackFold re-resolves with the tracked
        // String[] receiver; only a RECEIVER-TYPED attempt that stays
        // unmapped is a loud gap (the P26 contract, asserted in the foreign
        // array test below).
        var deferred = ResolveMember(SynthesizeNested("System.Collections.Generic", "SortedDictionary`2", "KeyCollection",
                "GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)),
            "GetEnumerator", receiverType: null);
        Assert.NotNull(deferred);
        Assert.True(deferred!.IsFictionalBclFallback);
        Assert.StartsWith("System_Collections_Generic_SortedDictionary_2_KeyCollection_GetEnumerator_", deferred.CppName);
    }

    [Fact]
    public void Resolve_SortedDictKeyCollection_GetEnumerator_ForeignArray_StaysLoud()
    {
        // Only the string Keys snapshot is wired; the Int32[] snapshot of a
        // foreign instantiation must not ride the string arm.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(SynthesizeNested("System.Collections.Generic", "SortedDictionary`2", "KeyCollection",
                    "GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)),
                "GetEnumerator", receiverType: "System.Int32[]"));
        Assert.Contains("GetEnumerator", ex.Message);
    }

    [Fact]
    public void Resolve_SortedDictKeyCollectionEnumerator_MoveNext_AddressReceiver()
    {
        var resolved = ResolveMember(SynthesizeNested("System.Collections.Generic", "SortedDictionary`2", "KeyCollection",
                "MoveNext", Blob(0x20, 0x00, 0x02), inner2: "Enumerator"),
            "MoveNext", receiverType: TrackedEnumerator);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", resolved!.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "deref:r" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedDictKeyCollectionEnumerator_GetCurrent_RefCarrier()
    {
        var resolved = ResolveMember(SynthesizeNested("System.Collections.Generic", "SortedDictionary`2", "KeyCollection",
                "get_Current", Blob(0x20, 0x00, 0x08), inner2: "Enumerator"),
            "get_Current", receiverType: TrackedEnumerator);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current", resolved!.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "deref:r" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedDictKeyCollectionEnumerator_Reset()
    {
        var resolved = ResolveMember(SynthesizeNested("System.Collections.Generic", "SortedDictionary`2", "KeyCollection",
                "Reset", Blob(0x20, 0x00, 0x01), inner2: "Enumerator"),
            "Reset", receiverType: TrackedEnumerator);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", resolved!.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(new[] { "deref:r" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedDictKeyCollectionEnumerator_Dispose_RoutesToCoreDispose()
    {
        var resolved = ResolveMember(Synthesize("System", "IDisposable", "Dispose", Blob(0x20, 0x00, 0x01)),
            "Dispose", receiverType: TrackedEnumerator);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose", resolved!.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(new[] { "deref:r" }, resolved.ParamTypeNames);
    }

    // GAP 2: IList<TKey>.get_Item on the tracked Keys snapshot.
    [Fact]
    public void Resolve_SnapshotArray_IListGetItem_MapsToArrayRead()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Blob(0x20, 0x01, 0x08, 0x08)),
            "get_Item", receiverType: KeysSnapshot);

        Assert.Equal("il2cpp::il2cpp_array_get_value", resolved!.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "a", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SnapshotArray_IListGetItem_ForeignReceiver_StaysLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Blob(0x20, 0x01, 0x08, 0x08)),
                "get_Item", receiverType: "System.Int32[]"));
        Assert.Contains("get_Item", ex.Message);
    }

    // Regression: the List`1 receiver keeps its list-indexer arms.
    [Fact]
    public void Resolve_ListInt_IListGetItem_Unchanged()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "IList`1", "get_Item", Blob(0x20, 0x01, 0x08, 0x08)),
            "get_Item", receiverType: "System.Collections.Generic.List`1_int");

        Assert.Equal("il2cpp::il2cpp_list_i4_get_item", resolved!.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod? ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
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
        var mdb = NewRoot(out var asmRef);
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static MemoryStream SynthesizeNested(string ns, string outer, string inner, string memberName,
        byte[] signature, string? inner2 = null)
    {
        var mdb = NewRoot(out var asmRef);
        var tOuter = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(outer));
        var tInner = mdb.AddTypeReference(tOuter, mdb.GetOrAddString(""), mdb.GetOrAddString(inner));
        var tLeaf = inner2 == null
            ? tInner
            : mdb.AddTypeReference(tInner, mdb.GetOrAddString(""), mdb.GetOrAddString(inner2));
        mdb.AddMemberReference(tLeaf, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static MetadataBuilder NewRoot(out AssemblyReferenceHandle asmRef)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        return mdb;
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
