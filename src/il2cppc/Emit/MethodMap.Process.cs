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
//   ReadToEnd on a Process receiver (owner System.IO.StreamReader --
//   ReadToEnd is inherited from TextReader, so the callvirt OWNER is not
//   Process; the receiver-type hint "System.Diagnostics.Process" recorded
//   by Start's ReturnTypeName discriminates the StandardOutput shape) ->
//   il2cpp_process_read_stdout_line (SIMPLIFIED ReadToEnd = the first
//   captured line; read_stdout_line is the only stdout read pathproc
//   exposes and the capture is consumed at EOF)
//
// Documented deviations (pathproc.h "simplified surface"):
//   * ReadToEnd returns ONE line (first captured, terminator stripped), not
//     the full stream -- net48 ReadToEnd would include the CRLF. The
//     StandardOutput shape is only reachable when a genuine StreamReader
//     cannot exist (its ctor is LOUD-rejected in the stream domain), so the
//     receiver-type discriminator is exact.
//   * The Process value is not a managed object: storing it where a real
//     object reference is expected (object[] slots, boxing, fields) is out
//     of the value-slot handle-gap's round-trip.
//   * Start failures (0) / null inputs (-1) follow the pathproc result
//     conventions, not .NET exceptions (the exception system is a later
//     milestone).
//
// LOUD rejections (known but unwireable): .ctor (no object model -- the
// newobj path would fabricate an object), Start with 3+ params, the
// ProcessStartInfo Start form (a CLASS param -- the info object has no
// runtime model), WaitForExit(int) (the runtime waits without a timeout),
// and get_StandardInput (pathproc redirects stdout/stderr to a capture
// file only -- stdin is inherited or NUL, pathproc.cpp STARTF_USESTDHANDLES;
// a managed stdin write cannot reach the child: construction order).
// ProcessStartInfo::.ctor and unlisted members (Kill, Refresh, ...) fall
// through to the generated-name path -- the Thread-domain convention
// (pre-M6 behaviour preserved); the ProcessStartInfo owner is not routed
// to this table by the shared MethodRefResolver dispatch (do-not-edit
// region), so its ctor cannot be LOUD-rejected here and fails LOUD at
// C++ link instead.
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
                    if (count == 1 && ps[0] == 0x12)
                        throw new NotSupportedException("Process.Start(ProcessStartInfo) not wired: the info object has no runtime model -- use the (string) / (string, string) forms (handle-gap: the process value is the PID)");
                    throw new NotSupportedException($"Process.Start with {count} param(s) not wired (only (string) and (string, string) -- the pathproc start takes exactly an exe path + a command line)");
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
                        // object; stdout is read back by PID.
                        return I(ResolvedMethod.IdentityMarker, 0, true, false, true);
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
                case "get_StandardInput":
                    throw new NotSupportedException("Process.StandardInput not wired: pathproc redirects stdout/stderr to a capture file only (pathproc.cpp STARTF_USESTDHANDLES) -- stdin is inherited or NUL, never redirected (construction order)");
                case ".ctor":
                    throw new NotSupportedException("new Process() not wired: the runtime has no Process object model -- use Process.Start (handle-gap: the process value is the PID, not an object)");
                default:
                    return null;
            }
        }
        // StandardOutput.ReadToEnd(): the callvirt's OWNER is
        // System.IO.StreamReader (ReadToEnd is inherited from TextReader),
        // so the owner dispatch lands in the stream domain -- but a
        // Process-stdout reader is a PID (get_StandardOutput folded to the
        // receiver above), NEVER a genuine StreamReader (whose ctor is
        // LOUD-rejected, MethodMap.StreamIO.cs). The receiver-type hint
        // "System.Diagnostics.Process" (recorded by Start's ReturnTypeName
        // and propagated through stores) discriminates this exact shape;
        // the StreamIO dispatch line falls back here on its null.
        if (name == "ReadToEnd" && count == 0 && receiverType == "System.Diagnostics.Process")
            return I("il2cpp::runtime::il2cpp_process_read_stdout_line", 0, true, true, true,
                new[] { "i" }, null, false, false, false, returnTypeName: "System.String");
        return null;
    }
}
