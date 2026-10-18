namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Process -- System.Diagnostics.Process icall mappings
// (src/runtime/include/internal/pathproc.h: the PID-keyed simplified domain).
// Owners: System.Diagnostics.Process (+ the StandardOutput ReadToEnd
// receiver-typed fallback for the System.IO.StreamReader owner).
//
// OBJECT GAP -- handle-gap design (chosen over objectification): pathproc
// has NO Process object model -- no class descriptor, no pid field; the API
// surface is plain PID integers (start returns the child PID; wait/exit-
// code/stdout take the PID). The managed "Process value" is therefore
// carried in the int32 value slot: Process.Start folds to a call returning
// the PID (ReturnsRef=false -- the value slot is int32_t, NOT a GC-rooted
// Il2CppObject*), instance members take the receiver as the PID (receiver
// cast "i"), and StandardOutput is an identity (the reader IS the pid).
// No Il2CppObject* is ever involved -- no GC root, no class descriptor, no
// runtime object model needed.
//
//   Start(string)               -> il2cpp_process_start (static, PID result,
//                                  tracked type System.Diagnostics.Process)
//   Start(string, string)       -> il2cpp_process_start (2 params)
//   WaitForExit()               -> il2cpp_process_wait_for_exit (instance;
//                                  the receiver PID slot is cast "i")
//   get_ExitCode                -> il2cpp_process_get_exit_code (instance)
//   get_StandardOutput          -> "::identity::" (the reader IS the pid:
//                                  the fold pushes the receiver -- the same
//                                  mechanism as MemberInfo::get_Name)
//   GetCurrentProcess()         -> il2cpp_process_get_current_process_id
//                                  (static, PID result, tracked type Process)
//   get_Id                      -> "::identity::" (the receiver IS the pid:
//                                  .Id of a process value is the pid itself)
//   get_ProcessName             -> il2cpp_process_get_name (instance; the
//                                  receiver PID slot is cast "i"; the file
//                                  stem without ".exe" -- nb3 PROC-GAP)
//   HasExited                   -> il2cpp_process_has_exited (instance;
//                                  non-blocking PID state probe -- nb3)
//   Kill                        -> il2cpp_process_kill (instance; nb3)
//   get_StandardError           -> "::identity::" (same PID fold, tracked as
//                                  the "System.Diagnostics.Process.Stderr"
//                                  pseudo-type -- nb7)
//   ReadToEnd on a Process / Stderr receiver (owner System.IO.StreamReader /
//   TextReader -- ReadToEnd is inherited, so the callvirt OWNER is not
//   Process; the receiver-type hint recorded by the StandardOutput /
//   StandardError identity folds discriminates the stream) ->
//   il2cpp_process_read_stdout_to_end / read_stderr_to_end (nb7 PROC-STARTINFO:
//   the FULL anonymous-pipe drain replacing the legacy first-line read)
//   Start(ProcessStartInfo)     -> il2cpp_process_start_info (static, PID
//                                  result; nb7: the info object is a real
//                                  light object, MethodMap.ProcessInfo.cs)
//
// Documented deviations (pathproc.h "simplified surface"):
//   * The Process value is not a managed object: storing it where a real
//     object reference is expected (object[] slots, boxing, fields) is out
//     of the value-slot handle-gap's round-trip.
//   * Start failures (0) / null inputs (-1) follow the pathproc result
//     conventions, not .NET exceptions (the exception system is a later
//     milestone); ProcessStartInfo UseShellExecute=true + a redirect DOES
//     raise the net48 InvalidOperationException through the managed transport.
//   * StandardOutput/StandardError.ReadToEnd returns the raw captured bytes
//     (1:1), not a console-encoding decode; a not-redirected stream's read
//     throws InvalidOperationException (net48).
//
// LOUD rejections (known but unwireable): new Process() (no object model --
// the newobj path would fabricate an object), Start with 3+ params,
// WaitForExit(int) (the runtime waits without a timeout), and get_StandardInput
// (the child is built with NUL stdin -- pathproc.cpp STARTF_USESTDHANDLES;
// a managed stdin write cannot reach the child: construction order; deferred).
// Unlisted members (Refresh, ...) fall through to the generated-name path
// (the Thread-domain convention). ProcessStartInfo and its members route to
// MethodMap.ProcessInfo.cs (nb7).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapProcess(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner == "System.Diagnostics.Process")
        {
            switch (name)
            {
                case "Start":
                    if (count == 1 && ps[0] == 0x0E)
                        return I("il2cpp::runtime::il2cpp_process_start", 1, true, false, false,
                            new[] { "" }, null, false, false, false, returnTypeName: "System.Diagnostics.Process");
                    if (count == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                        return I("il2cpp::runtime::il2cpp_process_start", 2, true, false, false,
                            new[] { "", "" }, null, false, false, false, returnTypeName: "System.Diagnostics.Process");
                    // nb7 PROC-STARTINFO: the ProcessStartInfo form -- the
                    // psi object (a real light object now, MethodMap.
                    // ProcessInfo.cs) is unpacked by the runtime launch icall
                    // (FileName + Arguments + the redirect flags; anonymous
                    // pipes for the redirected streams). The PID result rides
                    // the int32 value slot exactly like the static string form.
                    if (count == 1 && ps[0] == 0x12)
                        return I("il2cpp::runtime::il2cpp_process_start_info", 1, true, false, false,
                            new[] { "" }, null, false, false, false, returnTypeName: "System.Diagnostics.Process");
                    throw new NotSupportedException($"Process.Start with {count} param(s) not wired (only (string), (string, string) and (ProcessStartInfo) -- the pathproc start takes an exe path + a command line)");
                case "GetCurrentProcess":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_get_current_process_id", 0, true, false, false,
                            returnTypeName: "System.Diagnostics.Process");
                    return null;
                case "WaitForExit":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_wait_for_exit", 0, false, false, true, new[] { "i" });
                    throw new NotSupportedException($"Process.WaitForExit(int) not wired: the runtime waits with no timeout (pathproc.h)");
                case "get_ExitCode":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_get_exit_code", 0, true, false, true, new[] { "i" });
                    return null;
                case "get_Id":
                    // .Id of a process value is the pid itself (the receiver
                    // IS the pid value slot) -- same identity fold as
                    // get_StandardOutput.
                    if (count == 0)
                        return I(ResolvedMethod.IdentityMarker, 0, true, false, true);
                    return null;
                case "get_StandardOutput":
                    if (count == 0)
                        // The reader IS the pid: fold the property to its
                        // receiver (the callvirt IdentityMarker path,
                        // StackFold) -- the runtime has no StreamReader
                        // object; stdout is read back by PID. The
                        // ReturnTypeName RESETS the receiver tracking to the
                        // stdout shape, so a StandardError access earlier in
                        // the same scope cannot leak its stderr pseudo-type
                        // into a later StandardOutput.ReadToEnd (the identity
                        // fold records ReturnTypeName into objTypes).
                        return I(ResolvedMethod.IdentityMarker, 0, true, false, true,
                            returnTypeName: "System.Diagnostics.Process");
                    return null;
                // nb7 PROC-STARTINFO: StandardError is the same PID identity
                // fold, but tracked under the distinct pseudo-type
                // "System.Diagnostics.Process.Stderr" so ReadToEnd (whose
                // callvirt OWNER is StreamReader/TextReader) can pick the
                // stderr pipe instead of the stdout one.
                case "get_StandardError":
                    if (count == 0)
                        return I(ResolvedMethod.IdentityMarker, 0, true, false, true,
                            returnTypeName: "System.Diagnostics.Process.Stderr");
                    return null;
                // nb3 PROC-GAP: ProcessName / HasExited / Kill -- all three
                // were unwired (get_ProcessName / HasExited / Kill folded to
                // phantom generated-name calls). They take the receiver PID
                // slot (cast "i"); get_ProcessName returns the managed stem
                // string, HasExited a bool, Kill is void.
                case "get_ProcessName":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_get_name", 0, true, true, true,
                            new[] { "i" }, returnTypeName: "System.String");
                    return null;
                case "get_HasExited":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_has_exited", 0, true, false, true,
                            new[] { "i" });
                    return null;
                case "Kill":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_kill", 0, false, false, true,
                            new[] { "i" });
                    return null;
                // nb4 PROC-DIAG: the diagnostic properties -- StartTime /
                // ExitTime return DateTime ("dt" result kind -- the receiver
                // PID slot is cast "i"); WorkingSet64 returns int64 ("l"). The
                // net48 post-exit InvalidOperationException on WorkingSet64 is
                // raised INSIDE the runtime icall (a waited tracked slot / an
                // unopenable PID); StartTime/ExitTime read the Win32 times.
                case "get_StartTime":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_get_start_time", 0, true, false, true,
                            new[] { "i" }, "dt");
                    return null;
                case "get_ExitTime":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_get_exit_time", 0, true, false, true,
                            new[] { "i" }, "dt");
                    return null;
                case "get_WorkingSet64":
                    if (count == 0)
                        return I("il2cpp::runtime::il2cpp_process_get_working_set64", 0, true, false, true,
                            new[] { "i" }, "l");
                    return null;
                case "get_StandardInput":
                    throw new NotSupportedException("Process.StandardInput not wired: pathproc builds the child with NUL stdin (pathproc.cpp STARTF_USESTDHANDLES) -- a managed stdin write cannot reach the child (construction order); deferred to a later batch");
                case ".ctor":
                    throw new NotSupportedException("new Process() not wired: the runtime has no Process object model -- use Process.Start (handle-gap: the process value is the PID, not an object)");
                default:
                    return null;
            }
        }
        // StandardOutput/StandardError.ReadToEnd(): the callvirt's OWNER is
        // System.IO.StreamReader (ReadToEnd is inherited from TextReader),
        // so the owner dispatch lands in the stream domain -- but a
        // Process-stream reader is a PID (get_StandardOutput / get_StandardError
        // folded to the receiver above), NEVER a genuine StreamReader (whose
        // ctor is LOUD-rejected, MethodMap.StreamIO.cs). The receiver-type
        // hint discriminates this exact shape (recorded by the StandardOutput /
        // StandardError identity folds: "System.Diagnostics.Process" vs the
        // stderr pseudo-type); the StreamIO dispatch line falls back here on
        // its null. nb7 PROC-STARTINFO: the read is now the FULL pipe drain
        // (read_stdout_to_end / read_stderr_to_end), not the legacy first line.
        if (name == "ReadToEnd" && count == 0)
        {
            if (receiverType == "System.Diagnostics.Process")
                return I("il2cpp::runtime::il2cpp_process_read_stdout_to_end", 0, true, true, true,
                    new[] { "i" }, null, false, false, false, returnTypeName: "System.String");
            if (receiverType == "System.Diagnostics.Process.Stderr")
                return I("il2cpp::runtime::il2cpp_process_read_stderr_to_end", 0, true, true, true,
                    new[] { "i" }, null, false, false, false, returnTypeName: "System.String");
        }
        return null;
    }
}
