using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// T2 W3: HashSet<long/double/uint/object/class> resolver arms.
public class MethodMapTestsHashSetVal
{
    [Theory]
    [InlineData("System.Collections.Generic.HashSet`1_long", "il2cpp::runtime::icall_hashset_long_add")]
    [InlineData("System.Collections.Generic.HashSet`1_double", "il2cpp::runtime::icall_hashset_double_add")]
    [InlineData("System.Collections.Generic.HashSet`1_uint", "il2cpp::runtime::icall_hashset_uint_add")]
    [InlineData("System.Collections.Generic.HashSet`1_object", "il2cpp::runtime::icall_hashset_ref_add")]
    [InlineData("System.Collections.Generic.HashSet`1_class", "il2cpp::runtime::icall_hashset_ref_add")]
    public void Resolve_HashSet_Add_NewKinds(string receiver, string expected)
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Add", receiver);
        Assert.Equal(expected, resolved.CppName);
    }

    [Theory]
    [InlineData("System.Collections.Generic.HashSet`1_long", "il2cpp::il2cpp_hashset_long_get_enumerator", "System.Collections.Generic.HashSet`1.Enumerator_long")]
    [InlineData("System.Collections.Generic.HashSet`1_double", "il2cpp::il2cpp_hashset_double_get_enumerator", "System.Collections.Generic.HashSet`1.Enumerator_double")]
    [InlineData("System.Collections.Generic.HashSet`1_uint", "il2cpp::il2cpp_hashset_uint_get_enumerator", "System.Collections.Generic.HashSet`1.Enumerator_uint")]
    [InlineData("System.Collections.Generic.HashSet`1_object", "il2cpp::il2cpp_hashset_ref_get_enumerator", "System.Collections.Generic.HashSet`1.Enumerator_System.Object")]
    public void Resolve_HashSet_GetEnumerator_NewKinds(string receiver, string expected, string token)
    {
        var resolved = ResolveMember(Synthesize("GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)), "GetEnumerator", receiver);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(token, resolved.ReturnTypeName);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(), receiverType, false, null, null);
    }

    private static byte[] Blob(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()), default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"), new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("HashSet`1"));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(machine: Machine.I386, imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll), root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
