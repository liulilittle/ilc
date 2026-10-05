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

// MethodMap.Task Run dispatch tests (exc001): Task.Run<T>(Func<T>) in the
// synchronous-completion model resolves to the FromResult icall with the
// "run:i"/"run:r" value cast (Run(f) = FromResult(f.Invoke()) -- the
// synchronous invoke-then-wrap, no per-method emission). int-family rides
// the plain icall, ref carriers (string/object/class/array) the _s icall;
// long/double/float/ulong/valuetype/nested-generic T stay LOUD.
public class MethodMapTestsTaskRun
{
    [Fact]
    public void Resolve_Task_RunInt_MethodSpec_MapsToFromResult()
    {
        // Run<int>(Func<int>) -- MethodSpec carries int32 (0x08); the member
        // signature is the Roslyn shape (MVAR 0 return; GenericInst Func`1
        // of MVAR 0 param).
        var resolved = ResolveMethodSpec(SynthesizeRunGeneric(
            new byte[] { 0x00, 0x01, 0x1E, 0x00, 0x15, 0x12, 0x09, 0x01, 0x1E, 0x00 },
            new byte[] { 0x0A, 0x01, 0x08 }));

        Assert.Equal("il2cpp::runtime::icall_task_from_result", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Threading.Tasks.Task`1", resolved.ReturnTypeName);
        Assert.Equal(new[] { "run:i", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Task_RunString_MethodSpec_MapsToFromResultRef()
    {
        // Run<string>(Func<string>) -- the ref carrier rides _s with run:r.
        var resolved = ResolveMethodSpec(SynthesizeRunGeneric(
            new byte[] { 0x00, 0x01, 0x1E, 0x00, 0x15, 0x12, 0x09, 0x01, 0x1E, 0x00 },
            new byte[] { 0x0A, 0x01, 0x0E }));

        Assert.Equal("il2cpp::runtime::icall_task_from_result_s", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "run:r", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Task_RunLong_MethodSpec_ThrowsLoud_NoInvokeAbi()
    {
        // Run<long> -- no zero-arg int64 multicast ABI exists (raw methodPtr
        // would read a multicast container as code); LOUD, never a silent
        // int32 truncation.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMethodSpec(SynthesizeRunGeneric(
                new byte[] { 0x00, 0x01, 0x1E, 0x00, 0x15, 0x12, 0x09, 0x01, 0x1E, 0x00 },
                new byte[] { 0x0A, 0x01, 0x0A })));

        Assert.Contains("Task.Run", ex.Message);
    }

    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, null, null);
    }

    // System.Threading.Tasks.Task::Run MemberReference (Task TypeRef row 1,
    // System.Func`1 TypeRef row 2 -- the 0x09 coded index is (2<<2)|1) + one
    // MethodSpecification with the GENERICINST blob (0x0A header, 1 generic
    // argument byte).
    private static MemoryStream SynthesizeRunGeneric(byte[] memberRefSignature, byte[] genericSignature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var taskRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Threading.Tasks"), mdb.GetOrAddString("Task"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`1"));
        var memberRef = mdb.AddMemberReference(taskRef, mdb.GetOrAddString("Run"), mdb.GetOrAddBlob(memberRefSignature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
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
