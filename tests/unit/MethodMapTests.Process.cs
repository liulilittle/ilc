using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Process dispatch tests: prove the System.Diagnostics.Process
// domain (TryMapProcess in MethodMap.Process.cs) -- the PID handle-gap
// design (pathproc.h: start returns the child PID, and wait/exit-code/
// stdout take the PID -- the managed "Process value" is carried in the
// int32 value slot, no runtime object model):
//
//   Start(string)             -> il2cpp_process_start (static, PID result,
//                                ReturnTypeName System.Diagnostics.Process)
//   Start(string, string)     -> il2cpp_process_start (static, 2 params)
//   WaitForExit()             -> il2cpp_process_wait_for_exit (instance,
//                                receiver cast "i" -- the PID slot)
//   get_ExitCode              -> il2cpp_process_get_exit_code (instance)
//   get_StandardOutput        -> "::identity::" (the reader IS the pid --
//                                fold pushes the receiver)
//   ReadToEnd on a Process receiver (owner System.IO.StreamReader/TextReader
//   -- ReadToEnd is inherited from TextReader, so the callvirt owner is NOT
//   Process; the receiver hint discriminates the StandardOutput shape) ->
//   il2cpp_process_read_stdout_line (SIMPLIFIED ReadToEnd = one captured
//   line, the only stdout read pathproc exposes)
//   GetCurrentProcess()       -> il2cpp_process_get_current_process_id
//                                (static, PID result, ReturnTypeName Process)
//   get_Id                    -> "::identity::" (the receiver IS the pid:
//                                .Id of a process value is the pid itself)
//
// LOUD rejections (known but unwireable): .ctor (no object model),
// Start with 3+ params (only the two string forms), Start(ProcessStartInfo)
// (the CLASS form -- no object model for the info object), WaitForExit(int)
// (the runtime waits without a timeout), get_StandardInput (pathproc
// redirects stdout/stderr only; stdin is never redirected -- construction
// order). ProcessStartInfo::.ctor and unlisted members (Kill, ...) fall
// through to the generated-name path (Thread-domain convention: the
// ProcessStartInfo owner is not dispatched to TryMapProcess -- the shared
// MethodRefResolver dispatch is a do-not-edit region, so the ctor cannot
// be LOUD-rejected there; the generated name fails LOUD at C++ link).
//
// Signature blobs are the raw bytes RawSignature consumes (0x01=void,
// 0x08=i4, 0x0E=string, 0x12=class); CLASS (0x12) params/returns carry the
// TypeDefOrRef coded index after the 0x12 byte (row 1 -> 0x05), exactly as
// RawSignature reads them.
public class MethodMapProcessTests
{
    //--------------------------------------------------------------------------
    // Start -- the PID factory (static, returns the child PID as an int32
    // value slot; the managed Process type is tracked via ReturnTypeName so
    // later instance calls on the value resolve).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_Start_OneString_MapsToPidIcall()
    {
        // string Start(string): CLASS return (0x12 + coded index 0x05), one
        // STRING param (0x0E).
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "Start",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "Start");

        Assert.Equal("il2cpp::runtime::il2cpp_process_start", m.CppName);
        Assert.False(m.IsInstance); // static
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef); // handle-gap: the PID is an int32 value slot
        Assert.Equal("System.Diagnostics.Process", m.ReturnTypeName);
        Assert.Equal(new[] { "" }, m.ParamTypeNames); // managed string passthrough
    }

    [Fact]
    public void Resolve_Process_Start_TwoStrings_MapsToPidIcall()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "Start",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x0E }), "Start");

        Assert.Equal("il2cpp::runtime::il2cpp_process_start", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal("System.Diagnostics.Process", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Process_Start_ThreeParams_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Diagnostics", "Process", "Start",
                new byte[] { 0x00, 0x03, 0x12, 0x05, 0x0E, 0x0E, 0x0E }), "Start"));

        Assert.Contains("System.Diagnostics.Process::Start", ex.Message);
    }

    //--------------------------------------------------------------------------
    // WaitForExit / ExitCode -- instance members on the PID handle (the
    // receiver is args[0] and gets the "i" cast at the call site).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_WaitForExit_MapsToWaitIcall()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "WaitForExit", Sig(0x01)),
            "WaitForExit");

        Assert.Equal("il2cpp::runtime::il2cpp_process_wait_for_exit", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames); // receiver = PID slot
    }

    [Fact]
    public void Resolve_Process_WaitForExit_WithTimeout_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Diagnostics", "Process", "WaitForExit", Sig(0x01, 0x08)),
                "WaitForExit"));

        Assert.Contains("System.Diagnostics.Process::WaitForExit", ex.Message);
    }

    [Fact]
    public void Resolve_Process_ExitCode_MapsToExitCodeIcall()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_ExitCode", Sig(0x08)),
            "get_ExitCode");

        Assert.Equal("il2cpp::runtime::il2cpp_process_get_exit_code", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // StandardOutput -- the reader IS the pid: the getter folds to its
    // receiver (IdentityMarker), and ReadToEnd on the Process-typed receiver
    // maps to the runtime's line reader (SIMPLIFIED: one captured line).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_StandardOutput_IsIdentity()
    {
        // StreamReader get_StandardOutput(): CLASS return (0x12 + 0x05).
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_StandardOutput",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_StandardOutput");

        Assert.Equal(ResolvedMethod.IdentityMarker, m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_StreamReader_ReadToEnd_OnProcessReceiver_MapsToStdoutLine()
    {
        // StandardOutput.ReadToEnd(): the callvirt OWNER is StreamReader
        // (ReadToEnd is inherited from TextReader) -- the receiver hint
        // "System.Diagnostics.Process" discriminates the StandardOutput
        // shape (a genuine StreamReader can never exist: its ctor is
        // LOUD-rejected in the stream domain).
        var m = ResolveMember(Synthesize("System.IO", "StreamReader", "ReadToEnd", Sig(0x0E)),
            "ReadToEnd", receiverType: "System.Diagnostics.Process");

        Assert.Equal("il2cpp::runtime::il2cpp_process_read_stdout_line", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // the line is a managed string
        Assert.Equal("System.String", m.ReturnTypeName);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StreamReader_ReadToEnd_WithoutProcessHint_MapsToStreamIcall()
    {
        // A genuine StreamReader receiver (no Process hint) resolves to the
        // stream domain's ReadToEnd icall (D-STO-1: TryMapStreamIO now wires
        // StreamReader.ReadToEnd -> icall_stream_read_to_end). The Process
        // stdout shape is discriminated ONLY by the receiverType hint
        // "System.Diagnostics.Process" (see the Process-receiver test above);
        // without it, the member is a plain StreamReader text read.
        // Header 0x20 (HAS_THIS) models the real instance MemberRef.
        var m = ResolveMember(Synthesize("System.IO", "StreamReader", "ReadToEnd",
            new byte[] { 0x20, 0x00, 0x0E }), "ReadToEnd");

        Assert.Equal("il2cpp::runtime::icall_stream_read_to_end", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal("System.String", m.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // GetCurrentProcess / get_Id -- the current-process value: the static
    // factory folds to the current OS pid (int32 value slot, Process typed
    // via ReturnTypeName), and get_Id is an identity (the receiver IS the
    // pid -- .Id of any process value is the pid itself).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_GetCurrentProcess_MapsToCurrentPidIcall()
    {
        // Process GetCurrentProcess(): static, zero params, CLASS return.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "GetCurrentProcess",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetCurrentProcess");

        Assert.Equal("il2cpp::runtime::il2cpp_process_get_current_process_id", m.CppName);
        Assert.False(m.IsInstance); // static
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef); // handle-gap: the pid is an int32 value slot
        Assert.Equal("System.Diagnostics.Process", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Process_GetId_IsIdentity()
    {
        // int32 get_Id(): the receiver is the pid value slot -- .Id returns
        // the pid itself, so the getter folds to its receiver.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_Id", Sig(0x08)),
            "get_Id");

        Assert.Equal(ResolvedMethod.IdentityMarker, m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // ProcessStartInfo form -- known but unwireable: Start(ProcessStartInfo)
    // is LOUD (the info object has no runtime model); ProcessStartInfo::.ctor
    // falls through to the generated-name path (the shared dispatch does not
    // route that owner to TryMapProcess -- do-not-edit region), failing LOUD
    // at C++ link like any other unlisted member.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_Start_ProcessStartInfo_ThrowsLoud()
    {
        // Process Start(ProcessStartInfo): one CLASS param (0x12 + coded index).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Diagnostics", "Process", "Start",
                new byte[] { 0x00, 0x01, 0x12, 0x05, 0x12, 0x05 }), "Start"));

        Assert.Contains("System.Diagnostics.Process::Start", ex.Message);
        Assert.Contains("ProcessStartInfo", ex.Message);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_Ctor_FallsThroughToGeneratedName()
    {
        // The owner is NOT dispatched to TryMapProcess (MethodRefResolver's
        // do-not-edit region routes only System.Diagnostics.Process) -- the
        // ctor keeps the Thread-domain convention: generated-name path.
        // The synthetic MemberRef header carries HAS_THIS (0x20): an instance
        // ctor's real signature has the this-flag set, so the #44 header-based
        // classification (not the name heuristic) pins IsInstance.
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", ".ctor",
            new byte[] { 0x20, 0x00, 0x01 }), ".ctor");

        Assert.StartsWith("System_Diagnostics_ProcessStartInfo_", m.CppName);
        Assert.True(m.IsInstance);
    }

    //--------------------------------------------------------------------------
    // StandardInput -- known but unwireable: pathproc redirects stdout and
    // stderr to a capture file only; stdin is inherited or NUL (pathproc.cpp
    // STARTF_USESTDHANDLES), so a managed stdin write cannot reach the child.
    // LOUD, so the missing redirect fails at transpile, not C++ link.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_StandardInput_ThrowsLoud()
    {
        // StreamReader get_StandardInput(): CLASS return.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Diagnostics", "Process", "get_StandardInput",
                new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_StandardInput"));

        Assert.Contains("System.Diagnostics.Process::get_StandardInput", ex.Message);
        Assert.Contains("stdin", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Known-but-unwireable members are LOUD; unlisted members fall through
    // to the generated-name path (Thread-domain convention).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_Ctor_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Diagnostics", "Process", ".ctor", Sig(0x01)), ".ctor"));

        Assert.Contains("System.Diagnostics.Process::.ctor", ex.Message);
        Assert.Contains("Process.Start", ex.Message);
    }

    [Fact]
    public void Resolve_Process_Kill_FallsThroughToGeneratedName()
    {
        // nb3 PROC-GAP: Kill is now WIRED to il2cpp_process_kill (previously
        // unlisted -> the generated-name path; this test now pins the wired
        // arm -- the old fall-through behavior is retired).
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "Kill",
            new byte[] { 0x20, 0x00, 0x01 }), "Kill");

        Assert.Equal("il2cpp::runtime::il2cpp_process_kill", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // nb3 PROC-GAP: ProcessName / HasExited -- the PID-domain closures on the
    // instance value (receiver cast "i"; get_ProcessName returns the managed
    // file stem, HasExited a bool).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_ProcessName_MapsToNameIcall()
    {
        // string get_ProcessName(): instance, zero params, STRING return.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_ProcessName",
            Sig(0x0E)), "get_ProcessName");

        Assert.Equal("il2cpp::runtime::il2cpp_process_get_name", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // the file stem is a managed string
        Assert.Equal("System.String", m.ReturnTypeName);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Process_HasExited_MapsToExitedIcall()
    {
        // bool get_HasExited(): instance, zero params, BOOLEAN return.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_HasExited",
            Sig(0x02)), "get_HasExited");

        Assert.Equal("il2cpp::runtime::il2cpp_process_has_exited", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Thread.cs / MethodRefResolverDispatchTests.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null);
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
