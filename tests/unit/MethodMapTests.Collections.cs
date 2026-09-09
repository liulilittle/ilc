using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Collections dispatch tests: prove the System.Collections
// INTERFACE surface (ICollection / IEnumerable / IEnumerator -- D-SL-3,
// sldeep) maps the interface members on a SortedList Keys/Values snapshot to
// the snapshot-enumerator runtime icalls declared in
// src/runtime/include/internal/sortedlist.h:
//
//   ICollection::get_Count        -> il2cpp_sortedlist_snapshot_get_count
//   IEnumerable::GetEnumerator    -> il2cpp_sortedlist_snapshot_get_enumerator
//   IEnumerator::MoveNext         -> il2cpp_sortedlist_enumerator_move_next
//   IEnumerator::get_Current      -> il2cpp_sortedlist_enumerator_get_current
//   IEnumerator::Reset            -> il2cpp_sortedlist_enumerator_reset
//
// Dispatch is RECEIVER-AWARE: the MemberRef declares the member on the
// INTERFACE, so the concrete mapping is chosen by the receiver's TRACKED
// pseudo-type (get_Keys/get_Values ReturnTypeName -- "System.Collections.
// SortedList.KeyList"/".ValueList" -- and the get_enumerator result's
// ReturnTypeName -- "System.Collections.SortedList.Enumerator"). A null
// receiver (first-pass deferral) falls through to the generated-name path;
// a receiver of ANY other tracked type falls through too (loud).
//
// The signatures are the CONCRETE net48 interface signatures:
//   ICollection.get_Count() -> 20 00 08
//   IEnumerable.GetEnumerator() -> 20 00 1C
//   IEnumerator.MoveNext() -> 20 00 02
//   IEnumerator.get_Current() -> 20 00 1C
//   IEnumerator.Reset() -> 20 00 01
public class MethodMapTestsCollections
{
    //--------------------------------------------------------------------------
    // ICollection.get_Count on a Keys / Values snapshot
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.Collections.SortedList.KeyList")]
    [InlineData("System.Collections.SortedList.ValueList")]
    public void Resolve_ICollectionGetCount_OnSnapshot_MapsToSnapshotCount(string receiverType)
    {
        var resolved = ResolveInterface("System.Collections.ICollection", "get_Count",
            Blob(0x20, 0x00, 0x08), receiverType);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ICollectionGetCount_NullReceiver_FallsThrough()
    {
        // A null receiver (first-pass deferral) has no interface mapping --
        // it must fall to the generated-name path, not be silently
        // mis-mapped to the snapshot count.
        var resolved = ResolveInterface("System.Collections.ICollection", "get_Count",
            Blob(0x20, 0x00, 0x08), null);

        Assert.StartsWith("System_Collections_ICollection_get_Count_", resolved.CppName);
    }

    [Theory]
    [InlineData("System.Collections.SortedList.Enumerator")]
    [InlineData("System.IO.StreamWriter")]
    public void Resolve_ICollectionGetCount_NonSnapshot_FallsThrough(string receiverType)
    {
        // A non-snapshot TRACKED receiver is a phantom generated-name call
        // (the input assembly never defines System.Collections.ICollection),
        // so the P26 final-resolution guard rejects it LOUDLY instead of
        // emitting an undeclared identifier.
        var ex = Assert.Throws<NotSupportedException>(() => ResolveInterface("System.Collections.ICollection", "get_Count",
            Blob(0x20, 0x00, 0x08), receiverType));

        Assert.Contains("not wired in M5", ex.Message);
    }

    //--------------------------------------------------------------------------
    // IEnumerable.GetEnumerator on a Keys / Values snapshot
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.Collections.SortedList.KeyList")]
    [InlineData("System.Collections.SortedList.ValueList")]
    public void Resolve_IEnumerableGetEnumerator_OnSnapshot_MapsToSnapshotGetEnumerator(string receiverType)
    {
        var resolved = ResolveInterface("System.Collections.IEnumerable", "GetEnumerator",
            Blob(0x20, 0x00, 0x1C), receiverType);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        // the enumerator result is tracked for the IEnumerator member dispatch
        Assert.Equal("System.Collections.SortedList.Enumerator", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_IEnumerableGetEnumerator_NonSnapshot_MapsToEnumDispatcher()
    {
        // Non-generic IEnumerable::GetEnumerator with a TRACKED non-snapshot
        // receiver (a user enumerable / yield state machine, tracked via the
        // user method's ReturnTypeName) resolves to the runtime enum
        // dispatcher. A null receiver is the FIRST-PASS deferral (generated
        // name) -- the caller re-resolves with the tracked hint.
        var resolved = ResolveInterface("System.Collections.IEnumerable", "GetEnumerator",
            Blob(0x20, 0x00, 0x1C), "System.Collections.Generic.IEnumerator`1");

        Assert.Equal("il2cpp::il2cpp_ienum_get_enumerator_nongeneric", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // IEnumerator members on the snapshot enumerator object
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("MoveNext", "il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", false)]
    [InlineData("get_Current", "il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current", true)]
    [InlineData("Reset", "il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", false)]
    public void Resolve_IEnumeratorMember_OnEnumerator_MapsToEnumeratorIcall(string name, string expectedIcall, bool returnsRef)
    {
        var ret = returnsRef ? (byte)0x1C : name == "MoveNext" ? (byte)0x02 : (byte)0x01;
        var resolved = ResolveInterface("System.Collections.IEnumerator", name,
            Blob(0x20, 0x00, ret), "System.Collections.SortedList.Enumerator");

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(returnsRef, resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_IEnumeratorMember_NonEnumeratorReceiver_MapsToEnumDispatcher()
    {
        // A TRACKED non-snapshot IEnumerator receiver resolves MoveNext to the
        // runtime enum dispatcher. Null receiver = first-pass deferral (the
        // caller re-resolves with the tracked hint).
        var resolved = ResolveInterface("System.Collections.IEnumerator", "MoveNext",
            Blob(0x20, 0x00, 0x02), "System.Collections.Generic.IEnumerator`1");

        Assert.Equal("il2cpp::il2cpp_ienum_move_next", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // IDisposable::Dispose on the enumerator (the foreach finally) -> no-op
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IDisposableDispose_UntrackedReceiver_MapsToIenumDispose()
    {
        // The foreach-finally Dispose call's receiver is the `isinst
        // System.IDisposable` result -- untracked (receiverType null, arg0Type
        // null). It must map to the null-safe no-op enum dispose so the symbol
        // links (dispose dispatches the v() slot when present; any receiver is
        // safe -- the isinst guard never reaches it otherwise).
        var resolved = ResolveInterface("System.IDisposable", "Dispose",
            Blob(0x20, 0x00, 0x01), null, arg0Type: null);

        Assert.Equal("il2cpp::il2cpp_ienum_dispose", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_IDisposableDispose_StreamReceiver_MapsToStreamClose()
    {
        // A stream `using` block's Dispose keeps routing to the StreamIO close
        // icall -- the interface arm must not shadow it.
        var resolved = ResolveInterface("System.IDisposable", "Dispose",
            Blob(0x20, 0x00, 0x01), "System.IO.StreamWriter");

        Assert.Equal("il2cpp::runtime::icall_stream_close", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // P25c: the GENERIC iterator-interface surface (yieldprobe / enprobe).
    // A foreach over a user-enumerable / yield-iterator emits callvirts whose
    // declaring owner is the BCL interface (no translated TypeDef exists).
    // These map to the runtime interface dispatch entries (il2cpp_ienum_*,
    // arraysort.h) -- receiver-agnostic, resolved via the receiver's vtable
    // slot at runtime. The synthetic PE parent is a TypeReference (instName
    // null), so the generic get_Current carrier defaults to the i4 slot.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GenericIEnumerableGetEnumerator_MapsToEnumDispatcher()
    {
        var resolved = ResolveInterface("System.Collections.Generic.IEnumerable`1", "GetEnumerator",
            Blob(0x20, 0x00, 0x1C), null);

        Assert.Equal("il2cpp::il2cpp_ienum_get_enumerator", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_GenericIEnumeratorGetCurrent_MapsToI4Carrier()
    {
        var resolved = ResolveInterface("System.Collections.Generic.IEnumerator`1", "get_Current",
            Blob(0x20, 0x00, 0x08), null);

        Assert.Equal("il2cpp::il2cpp_ienum_get_current_i4", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_GenericIEnumeratorMoveNextResetDispose_MapsToEnumDispatcher()
    {
        var moveNext = ResolveInterface("System.Collections.Generic.IEnumerator`1", "MoveNext",
            Blob(0x20, 0x00, 0x02), null);
        Assert.Equal("il2cpp::il2cpp_ienum_move_next", moveNext.CppName);

        var reset = ResolveInterface("System.Collections.Generic.IEnumerator`1", "Reset",
            Blob(0x20, 0x00, 0x01), null);
        Assert.Equal("il2cpp::il2cpp_ienum_reset", reset.CppName);

        var dispose = ResolveInterface("System.Collections.Generic.IEnumerator`1", "Dispose",
            Blob(0x20, 0x00, 0x01), null);
        Assert.Equal("il2cpp::il2cpp_ienum_dispose", dispose.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveInterface(string ownerName, string memberName, byte[] signature,
        string? receiverType, string? arg0Type = null)
    {
        using var reader = new PEReader(Synthesize(ownerName, memberName, signature));
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, arg0Type, null);
    }

    private static byte[] Blob(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string ownerName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var ns = ownerName.Contains('.') ? ownerName[..ownerName.LastIndexOf('.')] : "";
        var name = ownerName.Contains('.') ? ownerName[(ownerName.LastIndexOf('.') + 1)..] : ownerName;
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(name));
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
