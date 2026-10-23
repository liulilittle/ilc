using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.HashSet dispatch tests: prove the System.Collections.Generic.
// HashSet`1 domain (MethodMap.HashSet.cs -> TryMapHashSet) maps the
// HashSet<int> expansion (G4) to the hashtable-backed boxed-int runtime
// icalls declared in src/runtime/include/internal/hashset.h
// (namespace il2cpp::runtime):
//
//   .ctor()        -> icall_hashset_int_ctor(self, 0)  (AppendZeroArg)
//   Add(T)         -> icall_hashset_int_add      (value cast "i")
//   Contains(T)    -> icall_hashset_int_contains
//   Remove(T)      -> icall_hashset_int_remove
//   get_Count()    -> icall_hashset_int_get_count
//   Clear()        -> icall_hashset_int_clear
//   GetEnumerator()-> il2cpp_hashset_int_get_enumerator (tracked
//                     "System.Collections.Generic.HashSet`1.Enumerator_int")
//
// The MemberRef signatures are the GENERIC DEFINITION signatures (T arrives
// as ELEMENT_TYPE_VAR 0x13 with a compressed index, consumed by
// ReadParamByte) -- the real net48 hsprobe blob 20 01 02 13 00 -> ps [13].
public class MethodMapTestsHashSet
{
    [Fact]
    public void Resolve_HashSet_Ctor_IntReceiver_MapsToIntCtor()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: "System.Collections.Generic.HashSet`1_int");

        Assert.Equal("il2cpp::runtime::icall_hashset_int_ctor", resolved.CppName);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_HashSet_Add_IntReceiver_MapsToIntAdd()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Add",
            receiverType: "System.Collections.Generic.HashSet`1_int");

        Assert.Equal("il2cpp::runtime::icall_hashset_int_add", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_HashSet_Contains_IntReceiver_MapsToIntContains()
    {
        var resolved = ResolveMember(Synthesize("Contains", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Contains",
            receiverType: "System.Collections.Generic.HashSet`1_int");

        Assert.Equal("il2cpp::runtime::icall_hashset_int_contains", resolved.CppName);
    }

    [Fact]
    public void Resolve_HashSet_Remove_IntReceiver_MapsToIntRemove()
    {
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Remove",
            receiverType: "System.Collections.Generic.HashSet`1_int");

        Assert.Equal("il2cpp::runtime::icall_hashset_int_remove", resolved.CppName);
    }

    [Fact]
    public void Resolve_HashSet_GetCount_IntReceiver_MapsToIntGetCount()
    {
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: "System.Collections.Generic.HashSet`1_int");

        Assert.Equal("il2cpp::runtime::icall_hashset_int_get_count", resolved.CppName);
    }

    [Fact]
    public void Resolve_HashSet_Clear_IntReceiver_MapsToIntClear()
    {
        var resolved = ResolveMember(Synthesize("Clear", Blob(0x20, 0x00, 0x01)), "Clear",
            receiverType: "System.Collections.Generic.HashSet`1_int");

        Assert.Equal("il2cpp::runtime::icall_hashset_int_clear", resolved.CppName);
    }

    [Fact]
    public void Resolve_HashSet_GetEnumerator_IntReceiver_TracksEnumeratorInt()
    {
        var resolved = ResolveMember(Synthesize("GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)), "GetEnumerator",
            receiverType: "System.Collections.Generic.HashSet`1_int");

        Assert.Equal("il2cpp::il2cpp_hashset_int_get_enumerator", resolved.CppName);
        Assert.Equal("System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_HashSet_Add_StringReceiver_MapsToRefFamily()
    {
        // colls (slot-ordered rewrite): ONE carrier table serves every element
        // kind so `foreach` reproduces net48 insertion order (hseta1). String
        // and object elements therefore share the reference family -- the
        // historical dictsi string split enumerated in bucket order and failed
        // that probe. String CONTENT equality still holds: SetHash/FindCarrier
        // compare strings by value inside the ref family (hsetbis4 pins
        // Contains + OrdinalIgnoreCase comparer).
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Add",
            receiverType: "System.Collections.Generic.HashSet`1_string");

        Assert.Equal("il2cpp::runtime::icall_hashset_ref_add", resolved.CppName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
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
            mdb.GetOrAddString("HashSet`1"));
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
