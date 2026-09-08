using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Thread dispatch tests: prove the System.Threading.Thread domain
// (TryMapThread in MethodMap.Thread.cs). The statics (Sleep/Yield/critical
// regions -> icall_thread_*) and the OBJECT model (threadobj domain:
// .ctor/Start/Join/IsAlive/ManagedThreadId/Priority/CurrentThread ->
// icall_threadobj_*) assert the exact C++ name + instance/static + return
// shape:
//
//   Thread.Sleep(int)              -> icall_thread_sleep (static, void)
//   Thread.Yield()                 -> icall_thread_yield
//   Thread.Begin/EndCriticalRegion -> icall_thread_begin/end_critical_region
//   Thread.ctor(ThreadStart)       -> icall_threadobj_ctor (instance, void)
//   Thread.Start()/Join()          -> icall_threadobj_start/join (instance)
//   Thread.IsAlive                 -> icall_threadobj_get_is_alive (bool)
//   Thread.ManagedThreadId         -> icall_threadobj_get_managed_thread_id
//   Thread.Priority get/set        -> icall_threadobj_get/set_priority
//   Thread.IsBackground get/set    -> icall_threadobj_get/set_is_background
//   Thread.CurrentThread           -> icall_threadobj_get_current_thread
//                                    (static, returns a Thread object)
//   Thread.Abort                   -> generated-name path (default: null)
//
// Signature blobs are the raw bytes RawSignature consumes (0x01=void,
// 0x08=i4, 0x02=bool, 0x0E=string, 0x12=class); CLASS (0x12) params carry
// the TypeDefOrRef coded index after the 0x12 byte, exactly as
// RawSignature reads them.
public class MethodMapThreadTests
{
    //--------------------------------------------------------------------------
    // Statics (thread.cpp icalls)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Thread_Sleep_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "Sleep", Sig(0x01, 0x08)), "Sleep");

        Assert.Equal("il2cpp::runtime::icall_thread_sleep", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_Yield_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "Yield", Sig(0x01)), "Yield");

        Assert.Equal("il2cpp::runtime::icall_thread_yield", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
    }

    [Theory]
    [InlineData("BeginCriticalRegion", "il2cpp::runtime::icall_thread_begin_critical_region")]
    [InlineData("EndCriticalRegion", "il2cpp::runtime::icall_thread_end_critical_region")]
    public void Resolve_Thread_CriticalRegion_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", name, Sig(0x01)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Thread OBJECT model (threadobj domain icalls)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Thread_CtorThreadStart_MapsToIcall()
    {
        // .ctor(ThreadStart): one CLASS parameter (0x12 + the TypeDefOrRef
        // coded index of the owner TypeRef, row 1 -> 0x05). The paramCount
        // byte (0x01) counts the PARAM, not the coded-index trailer (a
        // trailing 0x05 fed to Sig's ps.Length would declare 2 params and
        // the second read runs past the blob -- see MethodMapTests.StringIO).
        var m = ResolveMember(Synthesize("System.Threading", "Thread", ".ctor",
            new byte[] { 0x00, 0x01, 0x01, 0x12, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_threadobj_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    [Theory]
    [InlineData("Start", "il2cpp::runtime::icall_threadobj_start")]
    [InlineData("Join", "il2cpp::runtime::icall_threadobj_join")]
    public void Resolve_Thread_StartJoin_MapsToIcall(string name, string cppName)
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", name, Sig(0x01)), name);

        Assert.Equal(cppName, m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_JoinTimeout_MapsToIcall()
    {
        // D-THR3-1 (2026-08-11): Thread.Join(int32 millisecondsTimeout) --
        // the net48 Join(int) overload (returns bool). Signature: ret bool
        // (0x02), one int32 param (0x08). Must map to the join-with-timeout
        // icall (threadobj.h/cpp icall_threadobj_join_timeout) instead of
        // falling to the generated-name path (a link failure at HEAD).
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "Join", Sig(0x02, 0x08)), "Join");

        Assert.Equal("il2cpp::runtime::icall_threadobj_join_timeout", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_JoinTimeSpan_FallsThroughToGeneratedName()
    {
        // Thread.Join(TimeSpan) stays unwired (no TimeSpan conversion in the
        // emitter -- the generated-name path, like other unwired overloads).
        // Signature: header 0x20 (HAS_THIS -- the real instance MemberRef,
        // like the Abort test), ret bool (0x02), one valuetype param (0x11 +
        // the TypeDefOrRef coded index of the owner TypeRef, row 1 -> 0x05).
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "Join",
            new byte[] { 0x20, 0x01, 0x02, 0x11, 0x05 }), "Join");

        Assert.StartsWith("System_Threading_Thread_Join_", m.CppName);
        Assert.True(m.IsInstance);
    }

    [Fact]
    public void Resolve_Thread_IsAlive_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_IsAlive", Sig(0x02)), "get_IsAlive");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_is_alive", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    [Fact]
    public void Resolve_Thread_ManagedThreadId_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_ManagedThreadId", Sig(0x08)), "get_ManagedThreadId");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_managed_thread_id", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_GetPriority_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_Priority", Sig(0x08)), "get_Priority");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_priority", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_SetPriority_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "set_Priority", Sig(0x01, 0x08)), "set_Priority");

        Assert.Equal("il2cpp::runtime::icall_threadobj_set_priority", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_GetIsBackground_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_IsBackground", Sig(0x02)), "get_IsBackground");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_is_background", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    [Fact]
    public void Resolve_Thread_SetIsBackground_MapsToIcall()
    {
        // set_IsBackground(bool): one BOOL parameter (0x02).
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "set_IsBackground", Sig(0x01, 0x02)), "set_IsBackground");

        Assert.Equal("il2cpp::runtime::icall_threadobj_set_is_background", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_GetName_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_Name",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_Name");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_name", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_SetName_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "set_Name",
            new byte[] { 0x00, 0x01, 0x01, 0x0E }), "set_Name");

        Assert.Equal("il2cpp::runtime::icall_threadobj_set_name", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_CtorParameterized_MapsToCtorIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", ".ctor",
            new byte[] { 0x00, 0x02, 0x01, 0x12, 0x05, 0x08 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_threadobj_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
    }

    [Fact]
    public void Resolve_Thread_GetApartmentState_MapsToNativeIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "GetApartmentState", Sig(0x08)), "GetApartmentState");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_apartment_state_native", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
    }

    [Fact]
    public void Resolve_Thread_TrySetApartmentState_MapsToNativeIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "TrySetApartmentState", Sig(0x08, 0x08)), "TrySetApartmentState");

        Assert.Equal("il2cpp::runtime::icall_threadobj_set_apartment_state_native", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
    }

    [Fact]
    public void Resolve_Thread_CurrentThread_MapsToIcall()
    {
        // get_CurrentThread: CLASS return (0x12 + the TypeDefOrRef coded
        // index of the owner TypeRef, row 1 -> 0x05), zero params.
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_CurrentThread",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_CurrentThread");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_current_thread", m.CppName);
        Assert.False(m.IsInstance); // static
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // returns a Thread object
        Assert.Equal("System.Threading.Thread", m.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Thread startup three-piece: Start(object)/UnsafeStart()/UnsafeStart(object)/get_ThreadState
    //--------------------------------------------------------------------------

    [Fact]
    public void Resolve_Thread_StartParameterized_MapsToIcall()
    {
        // Start(object): one OBJECT parameter (0x1C, System.Object -- not 0x12
        // CLASS which needs a TypeDefOrRef coded index; see MethodMap.WeakReference.cs:40).
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "Start",
            new byte[] { 0x00, 0x01, 0x01, 0x1C }), "Start");

        Assert.Equal("il2cpp::runtime::icall_threadobj_start_parameterized", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_UnsafeStart_MapsToIcall()
    {
        // UnsafeStart(): no params, void return. M-stage no EC, delegates to Start.
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "UnsafeStart",
            Sig(0x01)), "UnsafeStart");

        Assert.Equal("il2cpp::runtime::icall_threadobj_unsafe_start", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_UnsafeStartParameterized_MapsToIcall()
    {
        // UnsafeStart(object): one OBJECT parameter (0x1C).
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "UnsafeStart",
            new byte[] { 0x00, 0x01, 0x01, 0x1C }), "UnsafeStart");

        Assert.Equal("il2cpp::runtime::icall_threadobj_unsafe_start_parameterized", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_GetThreadState_MapsToIcall()
    {
        // get_ThreadState(): returns int32 (0x8) ThreadState flags.
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_ThreadState",
            Sig(0x08)), "get_ThreadState");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_thread_state", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Unwired members fall through to the generated-name path (Thread's
    // default is null -- the pre-M6 behaviour, unlike Version's LOUD
    // default).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Thread_Abort_FallsThroughToGeneratedName()
    {
        // Unwired member -> generated-name path. Header 0x20 (HAS_THIS)
        // models the real instance MemberRef (Abort is instance in net48) --
        // the #44 header-based classification, not the old name heuristic.
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "Abort",
            new byte[] { 0x20, 0x00, 0x01 }), "Abort");

        Assert.StartsWith("System_Threading_Thread_Abort_", m.CppName);
        Assert.True(m.IsInstance);
    }

    //--------------------------------------------------------------------------
    // thr-b23: the 5-gap closure (MemoryBarrier / IsThreadPoolThread /
    // DisableComObjectEagerCleanup / Begin+EndThreadAffinity)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Thread_MemoryBarrier_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "MemoryBarrier", Sig(0x01)), "MemoryBarrier");

        Assert.Equal("il2cpp::runtime::icall_thread_memory_barrier", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(0, m.ParamCount);
    }

    [Fact]
    public void Resolve_Thread_GetIsThreadPoolThread_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "get_IsThreadPoolThread", Sig(0x02)), "get_IsThreadPoolThread");

        Assert.Equal("il2cpp::runtime::icall_thread_get_is_thread_pool_thread", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(0, m.ParamCount);
    }

    [Fact]
    public void Resolve_Thread_DisableComObjectEagerCleanup_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Threading", "Thread", "DisableComObjectEagerCleanup", Sig(0x01)), "DisableComObjectEagerCleanup");

        Assert.Equal("il2cpp::runtime::icall_thread_disable_com_object_eager_cleanup", m.CppName);
        Assert.True(m.IsInstance);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Thread_BeginEndThreadAffinity_MapsToIcall()
    {
        var begin = ResolveMember(Synthesize("System.Threading", "Thread", "BeginThreadAffinity", Sig(0x01)), "BeginThreadAffinity");
        Assert.Equal("il2cpp::runtime::icall_thread_begin_thread_affinity", begin.CppName);
        Assert.False(begin.IsInstance);
        Assert.False(begin.ReturnsValue);

        var end = ResolveMember(Synthesize("System.Threading", "Thread", "EndThreadAffinity", Sig(0x01)), "EndThreadAffinity");
        Assert.Equal("il2cpp::runtime::icall_thread_end_thread_affinity", end.CppName);
        Assert.False(end.IsInstance);
        Assert.False(end.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.VersionUri.cs)
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
