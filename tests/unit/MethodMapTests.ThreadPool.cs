using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ThreadPool dispatch tests: prove the System.Threading.ThreadPool
// domain (MethodMap.ThreadPool.cs -> TryMapThreadPool) maps the
// QueueUserWorkItem surface to the runtime threadpool domain (threadpool.h):
//
//   QueueUserWorkItem(WaitCallback)         -> il2cpp_threadpool_queue_work_item_delegate
//     (the 1-arg C++ overload -- the runtime forwards state = null; the
//     WaitCallback delegate unwraps to its methodPtr, which IS the
//     generated void(*)(Il2CppObject*) callback the pool runs)
//   QueueUserWorkItem(WaitCallback, object) -> the same C++ name, 2-arg
//     overload (arity dispatch -- the runtime overload set distinguishes
//     the managed overloads, no literal injection needed)
//   SetMaxThreads(int, int)                 -> il2cpp_threadpool_set_max_threads
//     (fixed-size pool: the worker count is passed, the completion-port
//     count is DROPPED -- matching the runtime's 1-param simplified icall)
//
// Known-unwireable overloads must be LOUD (NotSupportedException):
//   GetMaxThreads(out int, out int) -- out-param write-back is not
//     expressible in the emitter (the runtime icall returns int32 with no
//     out slots)
//
// The PEs are synthesized with System.Reflection.Metadata's writer, like
// MethodMapTests.Monitor. A WaitCallback parameter is a class (0x12)
// followed by its TypeDefOrRef coded index; RawSignature yields only the
// leading byte, which is what the domain dispatches on (arity for
// QueueUserWorkItem).
public class MethodMapTestsThreadPool
{
    [Fact]
    public void Resolve_QueueUserWorkItem_OneArg_MapsToDelegateIcall_NoState()
    {
        // static bool QueueUserWorkItem(WaitCallback)
        var resolved = ResolveMember(Synthesize("QueueUserWorkItem", Sig(0x02, B(0x12, 0x05))), "QueueUserWorkItem");

        Assert.Equal("il2cpp::runtime::il2cpp_threadpool_queue_work_item_delegate", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_QueueUserWorkItem_TwoArgs_MapsToDelegateIcall_WithState()
    {
        // static bool QueueUserWorkItem(WaitCallback, object)
        var resolved = ResolveMember(Synthesize("QueueUserWorkItem", Sig(0x02, B(0x12, 0x05), B(0x1C))), "QueueUserWorkItem");

        Assert.Equal("il2cpp::runtime::il2cpp_threadpool_queue_work_item_delegate", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_SetMaxThreads_MapsToIcall_DropsCompletionPortCount()
    {
        // static void SetMaxThreads(int workerThreads, int completionPortThreads)
        var resolved = ResolveMember(Synthesize("SetMaxThreads", Sig(0x00, B(0x08), B(0x08))), "SetMaxThreads");

        Assert.Equal("il2cpp::runtime::il2cpp_threadpool_set_max_threads", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("i", resolved.ParamTypeNames![0]);
        Assert.Equal("drop", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_GetMaxThreads_MapsToOutWrapper()
    {
        // icallwide: the out-param form now wires through the *_out runtime
        // wrappers + the emitter's "addr:i" cast (ps carry the BYREF ELEMENT
        // byte 0x08, not the 0x10 marker).
        var resolved = ResolveMember(Synthesize("GetMaxThreads", Sig(0x00, B(0x10, 0x08), B(0x10, 0x08))), "GetMaxThreads");

        Assert.Equal("il2cpp::runtime::icall_thread_pool_get_max_threads_out", resolved.CppName);
    }

    [Fact]
    public void Resolve_UnknownName_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("RegisterWaitForSingleObject", Sig(0x00, B(0x1C))), "RegisterWaitForSingleObject");

        Assert.StartsWith("System_Threading_ThreadPool_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);
    }

    // One full parameter byte sequence (e.g. B(0x10, 0x08) for an int&).
    private static byte[] B(params byte[] bytes) => bytes;

    // Signature blob: header 0x00 (DEFAULT, non-generic), LOGICAL parameter
    // count, return byte, then each parameter's full byte sequence (a byref
    // keeps its element byte, a class keeps its TypeHandle index -- the blob
    // must be decodable by MethodSignatureCoder, which runs before the
    // resolver's dispatch; RawSignature then yields only the leading byte).
    private static byte[] Sig(byte ret, params byte[][] ps)
    {
        var bytes = new byte[3 + ps.Sum(p => p.Length)];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        var at = 3;
        foreach (var p in ps)
        {
            Array.Copy(p, 0, bytes, at, p.Length);
            at += p.Length;
        }
        return bytes;
    }

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Threading"), mdb.GetOrAddString("ThreadPool"));
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
