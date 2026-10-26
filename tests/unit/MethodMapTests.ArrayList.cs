using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ArrayList dispatch tests (arraylist.h icalls): prove the
// BclResolveCore owner->domain switch routes System.Collections.ArrayList
// members to TryMapArrayList with the runtime icall names, arity, ref
// classifications and arg cast codes.
//
// NAMESPACE: the arraylist.h/.cpp icalls live in PLAIN namespace il2cpp
// (like stackqueue -- interlocked/hashtable are under il2cpp::runtime), so
// the CppNames are "il2cpp::il2cpp_arraylist_*".
//
// Boxing semantics: Add/set_Item value slots are Il2CppObject* -- the IL
// `box` opcode boxes value arguments BEFORE the call (box wiring), so no
// cast code is applied; `arr.Add(5)` pushes a boxed int. get_Item results
// stay Il2CppObject* -- the CALLER's castclass/unbox turns them back.
//
// .ctor: il2cpp_arraylist_create takes NO self and returns the new object;
// the fold-supplied newobj temp must be DROPPED ("drop"). Capacity ctors
// have no runtime icall -> LOUD.
//
// Add: .NET returns the int32 insertion index but the runtime add returns
// void -- the managed return is DROPPED (ReturnsValue false).
//
// Remove(object): no runtime icall (remove_at takes an index; the index_of
// lookup is a separate icall) -> LOUD with guidance.
public class MethodMapArrayListTests
{
    [Fact]
    public void Resolve_ArrayList_CtorParameterless_DropsReceiverOnCreateIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::il2cpp_arraylist_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        // The create icall takes NO self -- the newobj temp must be dropped.
        Assert.Equal("drop", resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_ArrayList_CtorCapacity_MapsToCreateCapacityIcall()
    {
        // ArrayList(int capacity): an INSTANCE-style ctor -- the receiver passes
        // through uncast ("") and the int32 capacity rides the "i" cast
        // (arraylist.h il2cpp_arraylist_create_capacity(self, capacity)).
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", ".ctor", Sig(0x01, 0x08)), ".ctor");

        Assert.Equal("il2cpp::il2cpp_arraylist_create_capacity", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ArrayList_CtorNonInt32Capacity_ThrowsLoud()
    {
        // Only the int-capacity ctor is wired; an object/long capacity keeps
        // the LOUD contract (no matching runtime create icall).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Collections", "ArrayList", ".ctor", Sig(0x01, 0x0A)), ".ctor"));

        Assert.Contains("System.Collections.ArrayList::.ctor", ex.Message);
    }

    [Fact]
    public void Resolve_ArrayList_AddObject_MapsToAddIcallWithDroppedInt32Return()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "Add", Sig(0x08, 0x1C)), "Add");

        Assert.Equal("il2cpp::il2cpp_arraylist_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        // .NET Add returns the int32 index; the runtime add returns void --
        // the managed return is dropped (no silent wrong value).
        Assert.False(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_GetItem_Index_MapsToGetItemIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "get_Item", Sig(0x1C, 0x08)), "get_Item");

        Assert.Equal("il2cpp::il2cpp_arraylist_get_item", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        // The index is a raw int32 slot (cast "i"); the receiver stays "".
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_ArrayList_SetItem_IndexObject_MapsToSetItemIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "set_Item", Sig(0x01, 0x08, 0x1C)), "set_Item");

        Assert.Equal("il2cpp::il2cpp_arraylist_set_item", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
        Assert.Equal("", resolved.ParamTypeNames![2]);
    }

    [Fact]
    public void Resolve_ArrayList_GetCount_MapsToCountIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "get_Count", Sig(0x08)), "get_Count");

        Assert.Equal("il2cpp::il2cpp_arraylist_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_Clear_MapsToClearIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "Clear", Sig(0x01)), "Clear");

        Assert.Equal("il2cpp::il2cpp_arraylist_clear", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_RemoveAt_Index_MapsToRemoveAtIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "RemoveAt", Sig(0x01, 0x08)), "RemoveAt");

        Assert.Equal("il2cpp::il2cpp_arraylist_remove_at", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_ArrayList_IndexOf_Object_MapsToIndexOfIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "IndexOf", Sig(0x08, 0x1C)), "IndexOf");

        Assert.Equal("il2cpp::il2cpp_arraylist_index_of", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_Contains_Object_MapsToContainsIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "Contains", Sig(0x02, 0x1C)), "Contains");

        Assert.Equal("il2cpp::il2cpp_arraylist_contains", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_Remove_Object_MapsToRemoveIcall()
    {
        // M7 collchain (2026-08-09): the runtime value-matching Remove icall
        // il2cpp_arraylist_remove (arraylist.h:106) implements .NET
        // ArrayList.Remove as a single call (index_of + remove_at + null-shift
        // inside the icall -- no composite needed).
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "Remove", Sig(0x01, 0x1C)), "Remove");

        Assert.Equal("il2cpp::il2cpp_arraylist_remove", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_GetCapacity_MapsToCapacityIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "get_Capacity", Sig(0x08)), "get_Capacity");

        Assert.Equal("il2cpp::il2cpp_arraylist_get_capacity", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_IsFlags_MapToFalseIcalls()
    {
        foreach (var name in new[] { "get_IsFixedSize", "get_IsReadOnly", "get_IsSynchronized" })
        {
            var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", name, Sig(0x02)), name);

            Assert.True(resolved.IsInstance);
            Assert.True(resolved.ReturnsValue);
            Assert.Equal(0, resolved.ParamCount);
        }
    }

    [Fact]
    public void Resolve_ArrayList_GetSyncRoot_MapsToSelfIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "get_SyncRoot", Sig(0x1C)), "get_SyncRoot");

        Assert.Equal("il2cpp::il2cpp_arraylist_get_sync_root", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_SetCapacity_MapsToSetCapacityIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "set_Capacity", Sig(0x01, 0x08)), "set_Capacity");

        Assert.Equal("il2cpp::il2cpp_arraylist_set_capacity", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_ArrayList_Clone_MapsToCloneIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "Clone", Sig(0x1C)), "Clone");

        Assert.Equal("il2cpp::il2cpp_arraylist_clone", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_CopyTo_MapsToCopyToIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "CopyTo", Sig(0x01, 0x1C, 0x08)), "CopyTo");

        Assert.Equal("il2cpp::il2cpp_arraylist_copy_to", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_TrimToSize_MapsToTrimIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "TrimToSize", Sig(0x01)), "TrimToSize");

        Assert.Equal("il2cpp::il2cpp_arraylist_trim_to_size", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_Reverse_MapsToReverseIcalls()
    {
        var plain = ResolveMember(Synthesize("System.Collections", "ArrayList", "Reverse", Sig(0x01)), "Reverse");
        Assert.Equal("il2cpp::il2cpp_arraylist_reverse", plain.CppName);
        Assert.Equal(0, plain.ParamCount);

        var ranged = ResolveMember(Synthesize("System.Collections", "ArrayList", "Reverse", Sig(0x01, 0x08, 0x08)), "Reverse");
        Assert.Equal("il2cpp::il2cpp_arraylist_reverse_range", ranged.CppName);
        Assert.Equal(2, ranged.ParamCount);
    }

    [Fact]
    public void Resolve_ArrayList_RemoveRange_MapsToRemoveRangeIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "ArrayList", "RemoveRange", Sig(0x01, 0x08, 0x08)), "RemoveRange");

        Assert.Equal("il2cpp::il2cpp_arraylist_remove_range", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapQueueStackTests).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then one byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
    }

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
