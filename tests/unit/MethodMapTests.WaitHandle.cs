using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.WaitHandle dispatch tests for the nb3 SYNC-GAP additions:
// (TryMapWaitHandle in MethodMap.WaitHandle.cs)
//
//   WaitOne(TimeSpan)               -> icall_waithandle_wait_one_timespan
//     (the TimeSpan rides raw ticks via the "ts_ticks" cast; the runtime
//     converts with the monitor wait_timespan truncation; previously the
//     TimeSpan arm was null -> a phantom generated-name fold-fail)
//   AutoResetEvent(bool).ctor       -> icall_waithandle_autoresetevent_ctor
//   ManualResetEvent(bool).ctor     -> icall_waithandle_manualresetevent_ctor
//     (the two sealed EventWaitHandle subclasses were NOT wired -- the
//     ctor fell to the generated-name path and the newobj allocation
//     referenced a missing class descriptor; the object allocation uses the
//     System_Threading_AutoResetEvent_class / ManualResetEvent_class
//     descriptors via the BclAllocExpr fallback)
//   WaitOne(TimeSpan) on the WaitHandle BASE owner (an inherited wait)
//     defaults to the same timespan icall.
//
// Signature blobs are the raw bytes RawSignature consumes: a VALUETYPE
// parameter is 0x11 followed by its TypeDefOrRef coded index (row 1 ->
// 0x05), exactly as the Monitor tests synthesize them.
public class MethodMapTestsWaitHandle
{
    [Fact]
    public void Resolve_WaitOne_TimeSpan_MapsToTimespanIcall()
    {
        // The MemberRef declares the inherited WaitOne(TimeSpan) on the
        // WaitHandle base; the SAME icall serves every handle kind.
        var resolved = ResolveMember("System.Threading", "WaitHandle", "WaitOne",
            Sig(0x02, B(0x11, 0x05)), "WaitOne");

        Assert.Equal("il2cpp::runtime::icall_waithandle_wait_one_timespan", resolved.CppName);
        Assert.True(resolved.IsInstance); // receiver-first
        Assert.Equal(1, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "", "ts_ticks" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_WaitOne_Int_StillMapsToTimeoutIcall()
    {
        // The int-milliseconds arm is untouched by the TimeSpan closure.
        // (No cast array on that arm -- the emitter derives the passthrough
        // casts itself; only the CppName + arity are pinned here.)
        var resolved = ResolveMember("System.Threading", "WaitHandle", "WaitOne",
            Sig(0x02, B(0x08)), "WaitOne");

        Assert.Equal("il2cpp::runtime::icall_waithandle_wait_one_timeout", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_AutoResetEvent_Ctor_MapsToAutoResetIcall()
    {
        // void AutoResetEvent(bool initialState) -- receiver-first ctor.
        var resolved = ResolveMember("System.Threading", "AutoResetEvent", ".ctor",
            Sig(0x00, B(0x02)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_waithandle_autoresetevent_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "b" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ManualResetEvent_Ctor_MapsToManualResetIcall()
    {
        var resolved = ResolveMember("System.Threading", "ManualResetEvent", ".ctor",
            Sig(0x00, B(0x02)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_waithandle_manualresetevent_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "b" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_AutoResetEvent_Set_StillRoutesToEventSet()
    {
        // Set/Reset/WaitOne are INHERITED (declared on the EventWaitHandle /
        // WaitHandle owners); an AutoResetEvent receiver's Set call still
        // resolves through the EventWaitHandle owner arm.
        var resolved = ResolveMember("System.Threading", "EventWaitHandle", "Set",
            Sig(0x02), "Set");

        Assert.Equal("il2cpp::runtime::icall_waithandle_event_set", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Monitor.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(string ns, string typeName, string memberName, byte[] signature,
        string resolveName)
    {
        using var reader = new PEReader(Synthesize(ns, typeName, memberName, signature));
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == resolveName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);
    }

    // One full parameter byte sequence (e.g. B(0x11, 0x05) for a valuetype).
    private static byte[] B(params byte[] bytes) => bytes;

    // Signature blob: header 0x00 (DEFAULT, non-generic), LOGICAL parameter
    // count, return byte, then each parameter's full byte sequence (a byref
    // keeps its element byte, a valuetype keeps its TypeHandle index -- the
    // blob must be decodable by MethodSignatureCoder; RawSignature then
    // yields only the LEADING byte the arms dispatch on).
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