namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Debug -- System.Diagnostics.Debug / System.Diagnostics.Trace
// icall mappings (src/runtime/include/internal/debugext.h).
// Owners: System.Diagnostics.Debug, System.Diagnostics.Trace
//
// Mapping surface (all static):
//   Debug.Assert(bool)           -> il2cpp::runtime::il2cpp_debug_assert
//   Debug.Assert(bool, string)   -> il2cpp::runtime::il2cpp_debug_assert
//   Debug.WriteLine(string)      -> il2cpp::runtime::il2cpp_debug_write_line
//   Debug.Fail(string)           -> il2cpp::runtime::il2cpp_debug_fail
//   Trace.Write(string)          -> il2cpp::runtime::il2cpp_trace_write
//   Trace.WriteLine(string)      -> il2cpp::runtime::il2cpp_debug_write_line
//   (debugext.h: Trace.WriteLine IS il2cpp_debug_write_line)
//
// Known-but-unwireable overloads throw NotSupportedException (LOUD, the
// standard gap shape -- the debug module has no numeric/boxed formatter, no
// detail-message parameter and no zero-argument write):
//   * Debug.Assert(bool, string, string) -- the stub takes (condition, message)
//     only; the detail message has no parameter.
//   * Debug.WriteLine() / Fail() -- the stubs require a text argument.
//   * Debug.WriteLine(object | numeric | char | bool) / Trace.Write(object) /
//     Trace.WriteLine(object | ...) -- no boxed-value or numeric printer in
//     the debug module (unlike the console module's _i/_b/_c/_d family).
//
// Unmapped names fall back to the generated-name path (loud link error).
//
// RUNTIME ABI GAP (blocks end-to-end use; the generated C++ fails LOUDLY at
// compile time -- same precedent as MethodMap.Version.cs's object gap and
// MethodMap.Guid.cs's slot gap):
//   * debugext.h/cpp stubs take UTF-8 C strings (const char*) -- the module
//     header claims "the generated-code ABI passes managed string contents in
//     that form ... same policy as the M1 string icalls". That policy is
//     OUTDATED: the emitter's "s" cast produces (Il2CppString*) -- managed
//     strings cross the icall boundary as Il2CppString* and the runtime
//     converts internally (e.g. icall_console_write_line_s(Il2CppObject*)).
//     EmitIcallCallArgs has NO const char* producer for icall arguments.
//   * Every mapped call therefore emits `stub((Il2CppString*)(...))` against
//     a `const char*` parameter -- a C++ type mismatch, loud at compile time.
//     Debug.Assert(bool) additionally emits a one-argument call against the
//     (condition, message) signature -- an arity mismatch, also loud.
//   * The closing task (out of scope: this file is transpiler-only, the
//     runtime is read-only) must either switch the debugext stubs to
//     Il2CppString* / Il2CppObject* parameters (converting internally like
//     icall_console_write_line_s) or add a C-string marshal cast (and its
//     free chain) to the emitter's cast table.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDebug(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Diagnostics.Debug":
                switch (name)
                {
                    case "Assert":
                        if (ps.Length == 1 && ps[0] == 0x02)
                            return I("il2cpp::runtime::il2cpp_debug_assert", 1, false, false, false, new[] { "b" });
                        if (ps.Length == 2 && ps[0] == 0x02 && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_assert", 2, false, false, false, new[] { "b", "s" });
                        // Debug.Assert(bool condition, string message, string
                        // detailMessage): the net48 detail-message overload. The
                        // runtime stub il2cpp_debug_assert takes (condition,
                        // message) only -- the detail message is dropped at
                        // emission (the stub never renders a detail message, so
                        // dropping it is observable-exact).
                        if (ps.Length == 3 && ps[0] == 0x02 && ps[1] == 0x0E && ps[2] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_assert", 3, false, false, false, new[] { "b", "s", "drop" });
                        // Debug.Assert(bool, string, string, object[]): the
                        // detail-message + format-args overload. The runtime
                        // stub takes (condition, message) only -- both the
                        // detail message and the args array are dropped (the
                        // stub never renders them).
                        if (ps.Length == 4 && ps[0] == 0x02 && ps[1] == 0x0E && ps[2] == 0x0E && ps[3] == 0x1D)
                            return I("il2cpp::runtime::il2cpp_debug_assert", 4, false, false, false, new[] { "b", "s", "drop", "drop" });
                        return null;
                    case "WriteLine":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_line", 1, false, false, false, new[] { "s" });
                        // Debug.WriteLine(object): the object's ToString text
                        // (icall_object_to_string -- null raises
                        // NullReferenceException, a boxed primitive formats
                        // its value; net48 semantics).
                        if (ps.Length == 1 && ps[0] == 0x1C)
                            return I("il2cpp::runtime::il2cpp_debug_write_line_obj", 1, false, false, false, new[] { "" });
                        if (ps.Length == 0)
                            throw new NotSupportedException("Debug.WriteLine(): the runtime stub il2cpp_debug_write_line requires a text argument");
                        throw new NotSupportedException($"Debug.WriteLine param type 0x{ps[0]:X2} not wired: the debug module has no formatter for it");
                    case "Fail":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_fail", 1, false, false, false, new[] { "s" });
                        if (ps.Length == 0)
                            throw new NotSupportedException("Debug.Fail(): the runtime stub il2cpp_debug_fail requires a message argument");
                        return null;
                    default: return null;
                }
            case "System.Diagnostics.Trace":
                switch (name)
                {
                    case "Write":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_write", 1, false, false, false, new[] { "s" });
                        // Trace.Write(object): the object's ToString text
                        // (no trailing newline -- .NET Trace.Write semantics).
                        if (ps.Length == 1 && ps[0] == 0x1C)
                            return I("il2cpp::runtime::il2cpp_trace_write_obj", 1, false, false, false, new[] { "" });
                        throw new NotSupportedException($"Trace.Write param type 0x{ps[0]:X2} not wired: the debug module has no formatter for it");
                    case "WriteLine":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_line", 1, false, false, false, new[] { "s" });
                        // Trace.WriteLine(object): the object's ToString text
                        // plus a trailing newline.
                        if (ps.Length == 1 && ps[0] == 0x1C)
                            return I("il2cpp::runtime::il2cpp_debug_write_line_obj", 1, false, false, false, new[] { "" });
                        throw new NotSupportedException($"Trace.WriteLine param type 0x{ps[0]:X2} not wired: the debug module has no formatter for it");
                    default: return null;
                }
            default: return null;
        }
    }
}
