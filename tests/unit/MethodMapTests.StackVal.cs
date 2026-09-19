using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// T2 W4: Stack<int/long/double> resolver arms.
public class MethodMapTestsStackVal
{
    [Theory]
    [InlineData("System.Collections.Generic.Stack`1_int", "il2cpp::runtime::icall_stack_int_push")]
    [InlineData("System.Collections.Generic.Stack`1_long", "il2cpp::runtime::icall_stack_long_push")]
    [InlineData("System.Collections.Generic.Stack`1_double", "il2cpp::runtime::icall_stack_double_push")]
    public void Resolve_Stack_Push_TypedKinds(string receiver, string expected)
    {
        var resolved = ResolveMember(Synthesize("Push", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Push", receiver);
        Assert.Equal(expected, resolved.CppName);
    }

    [Theory]
    [InlineData("System.Collections.Generic.Stack`1_int", "il2cpp::runtime::icall_stack_int_pop")]
    [InlineData("System.Collections.Generic.Stack`1_long", "il2cpp::runtime::icall_stack_long_pop")]
    [InlineData("System.Collections.Generic.Stack`1_double", "il2cpp::runtime::icall_stack_double_pop")]
    public void Resolve_Stack_Pop_TypedKinds(string receiver, string expected)
    {
        var resolved = ResolveMember(Synthesize("Pop", Blob(0x20, 0x00, 0x13, 0x00)), "Pop", receiver);
        Assert.Equal(expected, resolved.CppName);
    }

    [Theory]
    [InlineData("System.Collections.Generic.Stack`1_int", "il2cpp::il2cpp_stack_get_enumerator", "System.Collections.Generic.Stack`1.Enumerator_int")]
    [InlineData("System.Collections.Generic.Stack`1_long", "il2cpp::il2cpp_stack_get_enumerator", "System.Collections.Generic.Stack`1.Enumerator_long")]
    [InlineData("System.Collections.Generic.Stack`1_double", "il2cpp::il2cpp_stack_get_enumerator", "System.Collections.Generic.Stack`1.Enumerator_double")]
    public void Resolve_Stack_GetEnumerator_TypedKinds(string receiver, string expected, string token)
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
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("Stack`1"));
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
