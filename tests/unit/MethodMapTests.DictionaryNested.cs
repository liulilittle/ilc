using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// G4 spec (RED until the nested-value Dictionary arm lands): the M3 instance
// name for Dictionary<string,List<int>> is the full-name encoding
//   System.Collections.Generic.Dictionary`2_string_System.Collections.Generic.List`1_int
// (defName + "_" + short arg tags; the nested generic recurses -- see
// CodeGenerator.GenericInstantiationName). The VALUE is a managed reference
// (List<int>), so the whole member surface is the dictss object-model arm
// (hashtable storage, no boxing); get_Item additionally TRACKS the value
// instance name so member calls on the returned list dispatch as List<int>.
public class MethodMapTestsDictionaryNested
{
    public const string NestedReceiver =
        "System.Collections.Generic.Dictionary`2_string_System.Collections.Generic.List`1_int";

    [Fact]
    public void Resolve_DictionaryNested_Add_RefValuePassthrough()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: NestedReceiver);

        Assert.Equal("il2cpp::runtime::icall_dict_gen_add_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DictionaryNested_SetItem_RefValuePassthrough()
    {
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item",
            receiverType: NestedReceiver);

        Assert.Equal("il2cpp::runtime::icall_dict_gen_set_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DictionaryNested_GetItem_TracksListValueType()
    {
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: NestedReceiver);

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_o", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Collections.Generic.List`1_int", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_DictionaryNested_ContainsKey_ReusesDictGen()
    {
        var resolved = ResolveMember(Synthesize("ContainsKey", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "ContainsKey",
            receiverType: NestedReceiver);

        Assert.Equal("il2cpp::runtime::icall_dict_gen_contains_key_o", resolved.CppName);
    }

    [Fact]
    public void Resolve_DictionaryNested_Remove_ReusesDictGenBool()
    {
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Remove",
            receiverType: NestedReceiver);

        Assert.Equal("il2cpp::runtime::icall_dict_gen_remove_o", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_DictionaryNested_GetCount_ReusesDictGenCount()
    {
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: NestedReceiver);

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_count", resolved.CppName);
    }

    [Fact]
    public void Resolve_DictionaryNested_GetKeys_StringArraySnapshot()
    {
        var resolved = ResolveMember(Synthesize("get_Keys", Blob(0x20, 0x00, 0x12, 0x05)), "get_Keys",
            receiverType: NestedReceiver);

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_keys_o", resolved.CppName);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_DictionaryNested_UnwiredValueShape_RejectsLoudly()
    {
        // Dictionary<string,List<string>> has no ref-value arm yet -- the
        // receiver-typed attempt is a LOUD gap (the MethodMapTests.List.cs
        // ForeignReceiver precedent).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
                receiverType: "System.Collections.Generic.Dictionary`2_string_System.Collections.Generic.List`1_string"));

        Assert.Contains("Dictionary`2::Add", ex.Message);
        Assert.Contains("System.Collections.Generic.List`1_string", ex.Message);
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
            mdb.GetOrAddString("Dictionary`2"));
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
