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
//   get_StandardOutput        -> "::identity::" (the reader IS the pid --
//                                fold pushes the receiver; ReturnTypeName
//                                "System.Diagnostics.Process")
//   get_StandardError         -> "::identity::" (same PID fold, tracked as the
//                                "System.Diagnostics.Process.Stderr" pseudo-type,
//                                nb7)
//   ReadToEnd on a Process/Stderr receiver (owner System.IO.StreamReader /
//   TextReader -- ReadToEnd is inherited, so the callvirt owner is NOT
//   Process; the receiver hint discriminates the stream shape) ->
//   il2cpp_process_read_stdout_to_end / read_stderr_to_end (nb7 PROC-STARTINFO:
//   the FULL anonymous-pipe drain, not the legacy first line)
//   GetCurrentProcess()       -> il2cpp_process_get_current_process_id
//                                (static, PID result, ReturnTypeName Process)
//   get_Id                    -> "::identity::" (the receiver IS the pid:
//                                .Id of a process value is the pid itself)
//   Start(ProcessStartInfo)   -> il2cpp_process_start_info (static, PID result;
//                                nb7: the info object is a light managed object)
//
// LOUD rejections (known but unwireable): .ctor (no object model),
// Start with 3+ params (only the two string forms + ProcessStartInfo),
// WaitForExit(int) (the runtime waits without a timeout). nb8 wired
// get_StandardInput (the PID identity fold under the "...Process.Stdin"
// pseudo-type; the StreamWriter members route to the stdin pipe icalls).
// Unlisted members (Refresh, ...) fall through to the generated-name path
// (Thread-domain convention). The ProcessStartInfo members live in
// MethodMap.ProcessInfo.cs.
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

    //--------------------------------------------------------------------------
    // nb8 PROC-SECURE: the net48 SecureString Start forms (Process.cs:2288 /
    // :2299). The runtime validates the args and launches through the
    // current-credential path.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_StartSecure4_MapsToSecure4Icall()
    {
        // Start(fileName, userName, SecureString password, domain): CLASS
        // return, params string/string/CLASS(SecureString)/string.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "Start",
            new byte[] { 0x00, 0x04, 0x12, 0x05, 0x0E, 0x0E, 0x12, 0x05, 0x0E }), "Start");

        Assert.Equal("il2cpp::runtime::il2cpp_process_start_secure_4", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(4, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.Equal("System.Diagnostics.Process", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Process_StartSecure5_MapsToSecure5Icall()
    {
        // Start(fileName, arguments, userName, SecureString password, domain):
        // CLASS return, params string x3/CLASS(SecureString)/string.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "Start",
            new byte[] { 0x00, 0x05, 0x12, 0x05, 0x0E, 0x0E, 0x0E, 0x12, 0x05, 0x0E }), "Start");

        Assert.Equal("il2cpp::runtime::il2cpp_process_start_secure_5", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(5, m.ParamCount);
        Assert.True(m.ReturnsValue);
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
        // StreamReader get_StandardOutput(): CLASS return (0x12 + 0x05). The
        // identity fold records the ReturnTypeName so a stderr pseudo-type
        // cannot leak into a later stdout ReadToEnd.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_StandardOutput",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_StandardOutput");

        Assert.Equal(ResolvedMethod.IdentityMarker, m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal("System.Diagnostics.Process", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Process_StandardError_IsIdentityWithStderrPseudoType()
    {
        // nb7 PROC-STARTINFO: StandardError is the same PID identity fold but
        // tracked as the distinct stderr pseudo-type.
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_StandardError",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_StandardError");

        Assert.Equal(ResolvedMethod.IdentityMarker, m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal("System.Diagnostics.Process.Stderr", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StreamReader_ReadToEnd_OnProcessReceiver_MapsToStdoutDrain()
    {
        // StandardOutput.ReadToEnd(): the callvirt OWNER is TextReader (the
        // Process dispatch routes the Process receiver hint here) -- the hint
        // "System.Diagnostics.Process" discriminates the stdout shape (a
        // genuine StreamReader can never exist: its ctor is LOUD-rejected).
        // nb7: the FULL pipe drain (read_stdout_to_end), not the first line.
        var m = ResolveMember(Synthesize("System.IO", "StreamReader", "ReadToEnd", Sig(0x0E)),
            "ReadToEnd", receiverType: "System.Diagnostics.Process");

        Assert.Equal("il2cpp::runtime::il2cpp_process_read_stdout_to_end", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // the text is a managed string
        Assert.Equal("System.String", m.ReturnTypeName);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StreamReader_ReadToEnd_OnStderrReceiver_MapsToStderrDrain()
    {
        // StandardError.ReadToEnd(): the stderr pseudo-type selects the stderr
        // pipe drain.
        var m = ResolveMember(Synthesize("System.IO", "StreamReader", "ReadToEnd", Sig(0x0E)),
            "ReadToEnd", receiverType: "System.Diagnostics.Process.Stderr");

        Assert.Equal("il2cpp::runtime::il2cpp_process_read_stderr_to_end", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
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
    // nb7 PROC-STARTINFO -- Start(ProcessStartInfo): the info object is a real
    // light object now (MethodMap.ProcessInfo.cs); the CLASS param launches
    // through il2cpp_process_start_info (the PID value slot result).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_Start_ProcessStartInfo_MapsToStartInfoIcall()
    {
        // Process Start(ProcessStartInfo): one CLASS param (0x12 + coded index).
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "Start",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x12, 0x05 }), "Start");

        Assert.Equal("il2cpp::runtime::il2cpp_process_start_info", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef); // handle-gap: the PID is an int32 value slot
        Assert.Equal("System.Diagnostics.Process", m.ReturnTypeName);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // nb7 PROC-STARTINFO -- the ProcessStartInfo object-model members:
    // .ctor / get_/set_ FileName / Arguments / UseShellExecute /
    // RedirectStandardOutput / RedirectStandardError / CreateNoWindow.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ProcessStartInfo_Ctor_MapsToCtorIcall()
    {
        // The synthetic MemberRef header carries HAS_THIS (0x20): an instance
        // ctor's real signature has the this-flag set, so the #44 header-based
        // classification (not the name heuristic) pins IsInstance.
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", ".ctor",
            new byte[] { 0x20, 0x00, 0x01 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_CtorFn_MapsToCtorFnIcall()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", ".ctor",
            new byte[] { 0x20, 0x01, 0x01, 0x0E }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_ctor_fn", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "", "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_GetFileName_MapsToGetter()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "get_FileName",
            Sig(0x0E)), "get_FileName");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_get_filename", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal("System.String", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_SetFileName_MapsToSetter()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "set_FileName",
            Sig(0x01, 0x0E)), "set_FileName");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_set_filename", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.Equal(new[] { "", "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_GetUseShellExecute_MapsToGetter()
    {
        // bool get_UseShellExecute(): BOOLEAN return (0x02).
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "get_UseShellExecute",
            Sig(0x02)), "get_UseShellExecute");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_get_useshellexecute", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_SetUseShellExecute_MapsToSetter()
    {
        // set_UseShellExecute(bool): the bool rides the int32 slot ("i").
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "set_UseShellExecute",
            Sig(0x01, 0x02)), "set_UseShellExecute");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_set_useshellexecute", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "", "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_GetRedirectStandardError_MapsToGetter()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "get_RedirectStandardError",
            Sig(0x02)), "get_RedirectStandardError");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_get_redirectstderr", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_GetCreateNoWindow_MapsToGetter()
    {
        var m = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "get_CreateNoWindow",
            Sig(0x02)), "get_CreateNoWindow");

        Assert.Equal("il2cpp::runtime::icall_processstartinfo_get_createnowindow", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // nb8 STDIN-WRITE: StandardInput -- the same PID identity fold as
    // StandardOutput, tracked as the "System.Diagnostics.Process.Stdin"
    // pseudo-type; the StreamWriter members route to the stdin pipe icalls.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Process_StandardInput_IsIdentityWithStdinPseudoType()
    {
        // StreamWriter get_StandardInput(): CLASS return (0x12 + 0x05).
        var m = ResolveMember(Synthesize("System.Diagnostics", "Process", "get_StandardInput",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_StandardInput");

        Assert.Equal(ResolvedMethod.IdentityMarker, m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal("System.Diagnostics.Process.Stdin", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_StreamWriter_WriteLine_OnStdinReceiver_MapsToStdinLine()
    {
        var m = ResolveMember(Synthesize("System.IO", "StreamWriter", "WriteLine", Sig(0x01, 0x0E)),
            "WriteLine", receiverType: "System.Diagnostics.Process.Stdin");

        Assert.Equal("il2cpp::runtime::il2cpp_process_write_stdin_line", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "i", "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StreamWriter_Write_OnStdinReceiver_MapsToStdinText()
    {
        var m = ResolveMember(Synthesize("System.IO", "StreamWriter", "Write", Sig(0x01, 0x0E)),
            "Write", receiverType: "System.Diagnostics.Process.Stdin");

        Assert.Equal("il2cpp::runtime::il2cpp_process_write_stdin", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(new[] { "i", "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StreamWriter_FlushAndClose_OnStdinReceiver()
    {
        var flush = ResolveMember(Synthesize("System.IO", "StreamWriter", "Flush", Sig(0x01)),
            "Flush", receiverType: "System.Diagnostics.Process.Stdin");
        Assert.Equal("il2cpp::runtime::il2cpp_process_flush_stdin", flush.CppName);
        Assert.Equal(new[] { "i" }, flush.ParamTypeNames);

        var close = ResolveMember(Synthesize("System.IO", "StreamWriter", "Close", Sig(0x01)),
            "Close", receiverType: "System.Diagnostics.Process.Stdin");
        Assert.Equal("il2cpp::runtime::il2cpp_process_close_stdin", close.CppName);
        Assert.Equal(new[] { "i" }, close.ParamTypeNames);
    }

    [Fact]
    public void Resolve_StreamWriter_AutoFlush_OnStdinReceiver()
    {
        var get = ResolveMember(Synthesize("System.IO", "StreamWriter", "get_AutoFlush", Sig(0x02)),
            "get_AutoFlush", receiverType: "System.Diagnostics.Process.Stdin");
        Assert.Equal("il2cpp::runtime::il2cpp_process_get_stdin_autofush", get.CppName);
        Assert.True(get.ReturnsValue);
        Assert.False(get.ReturnsRef); // D1: a bool (int32) getter, not a managed ref

        var set = ResolveMember(Synthesize("System.IO", "StreamWriter", "set_AutoFlush", Sig(0x01, 0x02)),
            "set_AutoFlush", receiverType: "System.Diagnostics.Process.Stdin");
        Assert.Equal("il2cpp::runtime::il2cpp_process_set_stdin_autofush", set.CppName);
        Assert.Equal(new[] { "i", "i" }, set.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ProcessStartInfo_RedirectStandardInput_GetterAndSetter()
    {
        var get = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "get_RedirectStandardInput",
            Sig(0x02)), "get_RedirectStandardInput");
        Assert.Equal("il2cpp::runtime::icall_processstartinfo_get_redirectstdin", get.CppName);
        Assert.True(get.ReturnsValue);

        var set = ResolveMember(Synthesize("System.Diagnostics", "ProcessStartInfo", "set_RedirectStandardInput",
            Sig(0x01, 0x02)), "set_RedirectStandardInput");
        Assert.Equal("il2cpp::runtime::icall_processstartinfo_set_redirectstdin", set.CppName);
        Assert.Equal(new[] { "", "i" }, set.ParamTypeNames);
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

        Assert.Contains("new Process()", ex.Message);
        Assert.Contains("LOUD whitelist", ex.Message);
        Assert.Contains("no runtime descriptor/model", ex.Message);
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

