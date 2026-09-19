using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Task dotted user-class dispatch tests: a Task<T> instantiation
// whose suffix is a dotted full name with no arity marker (e.g.
// "Task`1_Cases.AsyncCase+Point") names a namespace-qualified reference
// type and rides the SHARED ref carrier (the "_s" icalls -- the Queue
// dotted-tag precedent). BCL struct full names stay LOUD.
public class MethodMapTestsTaskDotted
{
    [Fact]
    public void Resolve_Task_GetResult_DottedClassReceiver_MapsToRefCarrier()
    {
        // Task<Point>.Result (asynccase A1): instance T get_Result() --
        // real blob 20 00 13 00 (ret VAR 0).
        var resolved = ResolveMember(Synthesize("System.Threading.Tasks", "Task`1", "get_Result", Blob(0x20, 0x00, 0x13, 0x00)),
            "get_Result", receiverType: "System.Threading.Tasks.Task`1_Cases.AsyncCase+Point");

        Assert.Equal("il2cpp::runtime::icall_task_get_result_s", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_Task_GetResult_DottedStructReceiver_StaysLoud()
    {
        // Task<System.DateTime> is a value carrier without a built domain --
        // LOUD, never silently riding the reference carrier.
        Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Threading.Tasks", "Task`1", "get_Result", Blob(0x20, 0x00, 0x13, 0x00)),
                "get_Result", receiverType: "System.Threading.Tasks.Task`1_System.DateTime"));
    }

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
