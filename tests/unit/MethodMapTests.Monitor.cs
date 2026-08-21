using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Monitor dispatch tests: prove the System.Threading.Monitor domain
// (MethodMap.Monitor.cs -> TryMapMonitor) maps the lock-semantics surface to
// the runtime side-table monitor declared in monitor.h:
//
//   Monitor.Enter(object)                    -> il2cpp_monitor_enter
//   Monitor.Enter(object, ref bool)          -> il2cpp_monitor_enter (the
//     Roslyn lock-statement shape: the flag's ADDRESS is passed through
//     (&lockTaken) for the runtime bool* overload to write *lockTaken =
//     true -- so the guard passes and the finally Exits. The 2-arg call
//     is a LOUD C++ compile failure until monitor.h/cpp add that
//     overload. "drop" is forbidden: the flag would stay false forever
//     and the guard would ALWAYS skip Exit -- a silent monitor leak)
//   Monitor.Exit(object)                     -> il2cpp_monitor_exit
//   Monitor.TryEnter(object)                 -> il2cpp_monitor_try_enter (bool)
//   Monitor.Pulse(object) / PulseAll(object) -> il2cpp_monitor_pulse (the
//     simplified runtime pulse wakes ALL waiters -- PulseAll is exact)
//   Monitor.Wait(object, int)                -> il2cpp_monitor_wait (bool)
//
// Known-unwireable overloads must be LOUD (NotSupportedException):
//   TryEnter(object, timeout) -- no timeout try_enter icall exists
//   Wait(object)              -- cannot inject Timeout.Infinite (-1)
//   Wait(object, TimeSpan[..])-- no TimeSpan conversion in the emitter
//
// The PEs are synthesized with System.Reflection.Metadata's writer, like
// MethodMapTests.IntPtr. Signatures are real ECMA blobs: a byref parameter
// is 0x10 followed by its element byte (a bool& is [0x10, 0x02]), a
// valuetype parameter is 0x11 followed by its TypeDefOrRef coded index
// (row 1 -> 0x05). RawSignature returns only the leading byte of each
// parameter (0x10 / 0x11), which is what TryMapMonitor dispatches on.
public class MethodMapTestsMonitor
{
    [Fact]
    public void Resolve_Enter_Object_MapsToMonitorEnter()
    {
        // static void Enter(object)
        var resolved = ResolveMember(Synthesize("Enter", Sig(0x00, B(0x1C))), "Enter");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_enter", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_Enter_ObjectRefBool_MapsToMonitorEnter_PassesFlagAddress()
    {
        // static void Enter(object, bool& lockTaken) -- the Roslyn lock
        // statement lowering. The flag's ADDRESS is passed through (cast
        // "" -> &lockTaken) for the runtime bool* overload to write
        // *lockTaken = true; until that overload lands the 2-arg call
        // fails loudly at C++ compile time. Never "drop": the flag would
        // stay false and the guard would always skip Exit (silent leak).
        var resolved = ResolveMember(Synthesize("Enter", Sig(0x00, B(0x1C), B(0x10, 0x02))), "Enter");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_enter", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_Exit_Object_MapsToMonitorExit()
    {
        var resolved = ResolveMember(Synthesize("Exit", Sig(0x00, B(0x1C))), "Exit");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_exit", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_TryEnter_Object_MapsToMonitorTryEnter()
    {
        // static bool TryEnter(object)
        var resolved = ResolveMember(Synthesize("TryEnter", Sig(0x02, B(0x1C))), "TryEnter");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_try_enter", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_TryEnter_ObjectRefBool_MapsToTryEnter_PassesFlagAddress()
    {
        // M7 mon (2026-08-08): static void TryEnter(object, bool& lockTaken)
        // -- the net48 2-arg form. The runtime overload
        // il2cpp_monitor_try_enter(Il2CppObject*, bool*) (monitor.h, added by
        // the mon audit F2) writes *lockTaken = acquired (ArgumentException on
        // an already-true flag, checked before the null object). The flag's
        // ADDRESS is passed through (cast "" -> &lockTaken, same as Enter 2-arg).
        // ReturnsValue false: net48 TryEnter(object, ref bool) is VOID.
        var resolved = ResolveMember(Synthesize("TryEnter", Sig(0x00, B(0x1C), B(0x10, 0x02))), "TryEnter");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_try_enter", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_TryEnter_ObjectTimeout_ThrowsLoud()
    {
        // static bool TryEnter(object, int): no timeout try_enter icall --
        // dropping the timeout would turn a bounded wait into a single poll.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("TryEnter", Sig(0x02, B(0x1C), B(0x08))), "TryEnter"));

        Assert.Contains("System.Threading.Monitor::TryEnter", ex.Message);
    }

    [Fact]
    public void Resolve_Pulse_Object_MapsToMonitorPulse()
    {
        var resolved = ResolveMember(Synthesize("Pulse", Sig(0x00, B(0x1C))), "Pulse");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_pulse", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_PulseAll_Object_MapsToSamePulseIcall()
    {
        // The simplified runtime pulse wakes ALL waiters, so PulseAll is exact.
        var resolved = ResolveMember(Synthesize("PulseAll", Sig(0x00, B(0x1C))), "PulseAll");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_pulse", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Wait_ObjectInt_MapsToMonitorWait()
    {
        // static bool Wait(object, int timeoutMs)
        var resolved = ResolveMember(Synthesize("Wait", Sig(0x02, B(0x1C), B(0x08))), "Wait");

        Assert.Equal("il2cpp::runtime::il2cpp_monitor_wait", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal("", resolved.ParamTypeNames![0]);
        Assert.Equal("i", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_Wait_Object_ThrowsLoud()
    {
        // static bool Wait(object): the wait icall needs an explicit timeout
        // and there is no emitter mechanism to inject Timeout.Infinite (-1).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Wait", Sig(0x02, B(0x1C))), "Wait"));

        Assert.Contains("System.Threading.Monitor::Wait", ex.Message);
    }

    [Fact]
    public void Resolve_Wait_ObjectTimeSpan_ThrowsLoud()
    {
        // static bool Wait(object, TimeSpan): no TimeSpan conversion exists.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Wait", Sig(0x02, B(0x1C), B(0x11, 0x05))), "Wait"));

        Assert.Contains("System.Threading.Monitor::Wait", ex.Message);
    }

    [Fact]
    public void Resolve_Enter_NoArgs_FallsBackToGeneratedName()
    {
        // A zero-parameter Enter shape must NOT hit the lock mapping.
        var resolved = ResolveMember(Synthesize("Enter", Sig(0x00)), "Enter");

        Assert.StartsWith("System_Threading_Monitor_Enter_", resolved.CppName);
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

    // One full parameter byte sequence (e.g. B(0x10, 0x02) for a bool&).
    private static byte[] B(params byte[] bytes) => bytes;

    // Signature blob: header 0x00 (DEFAULT, non-generic), LOGICAL parameter
    // count, return byte, then each parameter's full byte sequence (a byref
    // keeps its element byte, a valuetype keeps its TypeHandle index -- the
    // blob must be decodable by MethodSignatureCoder, which runs before the
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
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Threading"), mdb.GetOrAddString("Monitor"));
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
