using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// G4 spec (RED until the generic SortedDictionary`2<string,int> arm lands):
// SortedDictionary<string,int> has the SAME observable contract as
// SortedList<string,int> for the wired probe surface (sorted-by-key order,
// duplicate Add -> ArgumentException, missing get_Item -> KeyNotFoundException,
// Remove -> bool, Keys -> sorted snapshot), so it REUSES the
// sortedlistint_obj.h wrapper layer (icall_sortedlist_si_*) and the
// il2cpp_sortedlist_class() descriptor instead of a second storage domain
// (C6 knowledge reuse; the Queue/Stack "same runtime object" precedent).
//
// instName = System.Collections.Generic.SortedDictionary`2_string_int.
public class MethodMapTestsSortedDictionaryGeneric
{
    public const string SdReceiver = "System.Collections.Generic.SortedDictionary`2_string_int";

    [Fact]
    public void Resolve_SortedDictionarySi_CtorParameterless_AppendsZeroArgument()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_create", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_Add_ReusesSortedListSiAdd()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_add", resolved.CppName);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_SetItem_ReusesSortedListSiSetByKey()
    {
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_set_by_key", resolved.CppName);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_GetItem_ReusesSortedListSiGetByKey()
    {
        // net48 OPEN signature: ret VAR1 (13 01) leaks its index byte, param
        // VAR0 (13 00).
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_by_key", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal("i", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_ContainsKey_ReusesSortedListSiContainsKey()
    {
        var resolved = ResolveMember(Synthesize("ContainsKey", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "ContainsKey",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_contains_key", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_Remove_ReusesSortedListSiRemove()
    {
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Remove",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_remove", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_GetCount_ReusesSortedListCount()
    {
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_get_count", resolved.CppName);
        Assert.Equal(new[] { "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_GetKeys_SnapshotsStringArray()
    {
        var resolved = ResolveMember(Synthesize("get_Keys", Blob(0x20, 0x00, 0x12, 0x05)), "get_Keys",
            receiverType: SdReceiver);

        Assert.Equal("il2cpp::runtime::icall_sortedlist_si_get_keys", resolved.CppName);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_UnwiredMember_FallsBackToGeneratedName()
    {
        // TryGetValue is NOT in the wired surface yet (the out-int32 byref
        // address encoding is a separate wiring step) -- it must fall to the
        // generated-name path, not be silently mis-mapped.
        // No receiver hint: the null-tag path defers to the generated-name
        // fallback (a receiver-typed attempt would be a LOUD P26 gap).
        var resolved = ResolveMember(Synthesize("TryGetValue", Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x10, 0x13, 0x01)), "TryGetValue");

        Assert.StartsWith("System_Collections_Generic_SortedDictionary", resolved.CppName);
    }

    [Fact]
    public void Resolve_SortedDictionarySi_UnwiredInstantiation_FallsBackToGeneratedName()
    {
        // No receiver hint: the null-tag path defers to the generated-name
        // fallback (a receiver-typed attempt would be a LOUD P26 gap).
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add");

        Assert.StartsWith("System_Collections_Generic_SortedDictionary", resolved.CppName);
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
            mdb.GetOrAddString("SortedDictionary`2"));
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
