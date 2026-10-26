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
// D-DEBUG-REST (2026-10-27): the full net48 Debug/Trace surface is wired.
// The runtime stubs take Il2CppString* / Il2CppObject* (the "s" cast) and
// convert internally (debugext.cpp's pinvokeext encoder), so the earlier
// const char* ABI gap is closed:
//   * Assert(bool[, string[, string[, object[]]]]) -> il2cpp_debug_assert
//     (the detail message / args are dropped -- the stub never renders them)
//   * WriteLine(string | object) -> il2cpp_debug_write_line[_obj]
//   * Write(string | object) -> il2cpp_trace_write[_obj]
//   * the CATEGORY forms Write/WriteLine(string|object, string) ->
//     il2cpp_{debug,trace}_write[_line][_obj]_category (net48 renders
//     `category + ":" + text`)
//   * the COMPOSITE-FORMAT form WriteLine(string format, object[] args) ->
//     il2cpp_{debug,trace}_write_line_fmt (String.Format via
//     icall_string_format)
//   * Fail(string[, string]) -> il2cpp_{debug,trace}_fail[_detail]
//   * Trace.Assert(bool[, string[, string[, object[]]]]) ->
//     il2cpp_trace_assert[_msg|_detail|_fmt]
//
// Known-but-unwireable overloads throw NotSupportedException (LOUD, the
// standard gap shape -- the debug module has no numeric/boxed formatter, no
// detail-message parameter and no zero-argument write):
//   * Debug.WriteLine() / Fail() -- the stubs require a text argument
//     (net48 has no such overload -> LOUD whitelist, self-identifying).
//   * Debug.WriteLine(int | char | bool | ...) / Trace.Write(int | ...) --
//     no numeric formatter in the debug module (net48 has no such overload
//     -> LOUD whitelist, self-identifying).
//
// Unmapped names fall back to the generated-name path (loud link error).
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
                    case "Write":
                        // Debug.Write(string) -> TraceInternal.Write(message)
                        // (stdout, no newline -- the Trace.Write semantics).
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_write", 1, false, false, false, new[] { "s" });
                        // Debug.Write(object) -> the object's ToString text.
                        if (ps.Length == 1 && ps[0] == 0x1C)
                            return I("il2cpp::runtime::il2cpp_trace_write_obj", 1, false, false, false, new[] { "" });
                        // Debug.Write(string message, string category) /
                        // Debug.Write(object value, string category): the
                        // net48 category forms (reference :177/:186).
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_category", 2, false, false, false, new[] { "s", "s" });
                        if (ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_obj_category", 2, false, false, false, new[] { "", "s" });
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Debug.Write arity {ps.Length} " +
                            $"(0x{(ps.Length > 0 ? ps[0] : 0):X2}, ...) -- net48 Debug.Write forms are (string), " +
                            "(object), (string,string category) and (object,string category) " +
                            "[Reference Source System_compmod_system_diagnostics_Debug.cs:159,168,177,186]");
                    case "WriteLine":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_line", 1, false, false, false, new[] { "s" });
                        // Debug.WriteLine(object): the object's ToString text
                        // (icall_object_to_string -- null raises
                        // NullReferenceException, a boxed primitive formats
                        // its value; net48 semantics).
                        if (ps.Length == 1 && ps[0] == 0x1C)
                            return I("il2cpp::runtime::il2cpp_debug_write_line_obj", 1, false, false, false, new[] { "" });
                        // Debug.WriteLine(string message, string category) /
                        // Debug.WriteLine(object value, string category): the
                        // net48 category forms (reference :217/:228).
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_line_category", 2, false, false, false, new[] { "s", "s" });
                        if (ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_line_obj_category", 2, false, false, false, new[] { "", "s" });
                        // Debug.WriteLine(string format, params object[] args):
                        // String.Format(format, args) then the line writer
                        // (reference :239). The args ride an SZARRAY (0x1D).
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                            return I("il2cpp::runtime::il2cpp_debug_write_line_fmt", 2, false, false, false, new[] { "", "a" });
                        if (ps.Length == 0)
                            throw new NotSupportedException(
                                "LOUD whitelist (net48 has no such overload): Debug.WriteLine() with 0 params -- " +
                                "net48 Debug.WriteLine forms are (string), (object), (string,string category), " +
                                "(object,string category) and (string,object[] args) " +
                                "[Reference Source System_compmod_system_diagnostics_Debug.cs:196,207,217,228,239]");
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Debug.WriteLine arity {ps.Length} " +
                            $"(0x{ps[0]:X2}, ...) -- net48 Debug.WriteLine forms are (string), (object), " +
                            "(string,string category), (object,string category) and (string,object[] args) " +
                            "[Reference Source System_compmod_system_diagnostics_Debug.cs:196,207,217,228,239]");
                    case "Fail":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_fail", 1, false, false, false, new[] { "s" });
                        // Debug.Fail(string message, string detailMessage):
                        // the net48 detail form (reference :141).
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_fail_detail", 2, false, false, false, new[] { "s", "s" });
                        if (ps.Length == 0)
                            throw new NotSupportedException(
                                "LOUD whitelist (net48 has no such overload): Debug.Fail() with 0 params -- " +
                                "net48 Debug.Fail forms are (string) and (string,string detailMessage) " +
                                "[Reference Source System_compmod_system_diagnostics_Debug.cs:133,141]");
                        return null;
                    default: return null;
                }
            case "System.Diagnostics.Trace":
                switch (name)
                {
                    case "Assert":
                        if (ps.Length == 1 && ps[0] == 0x02)
                            return I("il2cpp::runtime::il2cpp_trace_assert", 1, false, false, false, new[] { "b" });
                        if (ps.Length == 2 && ps[0] == 0x02 && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_assert_msg", 2, false, false, false, new[] { "b", "s" });
                        if (ps.Length == 3 && ps[0] == 0x02 && ps[1] == 0x0E && ps[2] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_assert_detail", 3, false, false, false, new[] { "b", "s", "s" });
                        // Trace.Assert(bool, string, string detailFormat,
                        // object[] args): the detail is a composite-format
                        // string (reference :447).
                        if (ps.Length == 4 && ps[0] == 0x02 && ps[1] == 0x0E && ps[2] == 0x0E && ps[3] == 0x1D)
                            return I("il2cpp::runtime::il2cpp_trace_assert_fmt", 4, false, false, false, new[] { "b", "s", "", "a" });
                        return null;
                    case "Fail":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_fail", 1, false, false, false, new[] { "s" });
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_fail_detail", 2, false, false, false, new[] { "s", "s" });
                        return null;
                    case "Write":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_write", 1, false, false, false, new[] { "s" });
                        // Trace.Write(object): the object's ToString text
                        // (no trailing newline -- .NET Trace.Write semantics).
                        if (ps.Length == 1 && ps[0] == 0x1C)
                            return I("il2cpp::runtime::il2cpp_trace_write_obj", 1, false, false, false, new[] { "" });
                        // Trace.Write(string message, string category) /
                        // Trace.Write(object value, string category): the
                        // net48 category forms (reference :522/:532).
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_write_category", 2, false, false, false, new[] { "s", "s" });
                        if (ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_write_obj_category", 2, false, false, false, new[] { "", "s" });
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Trace.Write arity {ps.Length} " +
                            $"(0x{(ps.Length > 0 ? ps[0] : 0):X2}, ...) -- net48 Trace.Write forms are (string), " +
                            "(object), (string,string category) and (object,string category) " +
                            "[Reference Source System_compmod_system_diagnostics_Debug.cs:458,517,522,532]");
                    case "WriteLine":
                        if (ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_debug_write_line", 1, false, false, false, new[] { "s" });
                        // Trace.WriteLine(object): the object's ToString text
                        // plus a trailing newline.
                        if (ps.Length == 1 && ps[0] == 0x1C)
                            return I("il2cpp::runtime::il2cpp_debug_write_line_obj", 1, false, false, false, new[] { "" });
                        // Trace.WriteLine(string message, string category) /
                        // Trace.WriteLine(object value, string category): the
                        // net48 category forms (reference :507/:497).
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_write_line_category", 2, false, false, false, new[] { "s", "s" });
                        if (ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x0E)
                            return I("il2cpp::runtime::il2cpp_trace_write_line_obj_category", 2, false, false, false, new[] { "", "s" });
                        // Trace.WriteLine(string format, params object[] args):
                        // String.Format(format, args) then the line writer
                        // (reference :502). The args ride an SZARRAY (0x1D).
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                            return I("il2cpp::runtime::il2cpp_trace_write_line_fmt", 2, false, false, false, new[] { "", "a" });
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Trace.WriteLine arity {ps.Length} " +
                            $"(0x{(ps.Length > 0 ? ps[0] : 0):X2}, ...) -- net48 Trace.WriteLine forms are (string), " +
                            "(object), (string,string category), (object,string category) and (string,object[] args) " +
                            "[Reference Source System_compmod_system_diagnostics_Debug.cs:452,492,497,502,507]");
                    default: return null;
                }
            default: return null;
        }
    }
}
