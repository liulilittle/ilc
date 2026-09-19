using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// T2 W4: Queue<long/double> resolver arms + URI-FIX dotted class-element tag.
public class MethodMapTestsQueueVal
{
    [Theory]
    [InlineData("System.Collections.Generic.Queue`1_long", "il2cpp::runtime::icall_queue_long_enqueue")]
    [InlineData("System.Collections.Generic.Queue`1_double", "il2cpp::runtime::icall_queue_double_enqueue")]
    public void Resolve_Queue_Enqueue_TypedKinds(string receiver, string expected)
    {
        var resolved = ResolveMember(Synthesize("Enqueue", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Enqueue", receiver);
        Assert.Equal(expected, resolved.CppName);
    }

    [Theory]
    [InlineData("System.Collections.Generic.Queue`1_long", "il2cpp::runtime::icall_queue_long_dequeue")]
    [InlineData("System.Collections.Generic.Queue`1_double", "il2cpp::runtime::icall_queue_double_dequeue")]
    public void Resolve_Queue_Dequeue_TypedKinds(string receiver, string expected)
    {
        var resolved = ResolveMember(Synthesize("Dequeue", Blob(0x20, 0x00, 0x13, 0x00)), "Dequeue", receiver);
        Assert.Equal(expected, resolved.CppName);
    }

    [Theory]
    [InlineData("System.Collections.Generic.Queue`1_long", "il2cpp::il2cpp_queue_get_enumerator", "System.Collections.Generic.Queue`1.Enumerator_long")]
    [InlineData("System.Collections.Generic.Queue`1_double", "il2cpp::il2cpp_queue_get_enumerator", "System.Collections.Generic.Queue`1.Enumerator_double")]
    public void Resolve_Queue_GetEnumerator_TypedKinds(string receiver, string expected, string token)
    {
        var resolved = ResolveMember(Synthesize("GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)), "GetEnumerator", receiver);
        Assert.Equal(expected, resolved.CppName);
        Assert.Equal(token, resolved.ReturnTypeName);
    }

    // URI-FIX (diffuri001): the dotted CLASS-arg tag rides the reference slots.
    [Fact]
    public void Resolve_Queue_Enqueue_DottedUriTag_MapsToRefEnqueue()
    {
        var resolved = ResolveMember(Synthesize("Enqueue", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Enqueue",
            receiverType: "System.Collections.Generic.Queue`1_System.Uri");
        Assert.Equal("il2cpp::il2cpp_queue_enqueue", resolved.CppName);
    }

    [Fact]
    public void Resolve_Queue_Dequeue_DottedUriTag_MapsToRefDequeue()
    {
        var resolved = ResolveMember(Synthesize("Dequeue", Blob(0x20, 0x00, 0x13, 0x00)), "Dequeue",
            receiverType: "System.Collections.Generic.Queue`1_System.Uri");
        Assert.Equal("il2cpp::il2cpp_queue_dequeue", resolved.CppName);
    }

    [Fact]
    public void Resolve_Queue_GetEnumerator_DottedUriTag_TracksDottedToken()
    {
        var resolved = ResolveMember(Synthesize("GetEnumerator", Blob(0x20, 0x00, 0x12, 0x05)), "GetEnumerator",
            receiverType: "System.Collections.Generic.Queue`1_System.Uri");
        Assert.Equal("il2cpp::il2cpp_queue_get_enumerator", resolved.CppName);
        Assert.Equal("System.Collections.Generic.Queue`1.Enumerator_System.Uri", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Queue_ToArray_DottedUriTag_TracksUriArray()
    {
        var resolved = ResolveMember(Synthesize("ToArray", Blob(0x20, 0x00, 0x12, 0x05)), "ToArray",
            receiverType: "System.Collections.Generic.Queue`1_System.Uri");
        Assert.Equal("il2cpp::il2cpp_queue_to_array", resolved.CppName);
        Assert.Equal("System.Uri[]", resolved.ReturnTypeName);
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
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("Queue`1"));
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
