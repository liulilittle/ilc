namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Debugger -- System.Diagnostics.Debugger icall mappings
// (src/runtime/include/internal/debuggerext.h -- il2cpp::runtime::
// il2cpp_debugger_*). Owners: System.Diagnostics.Debugger
//
// net48 surface (audit icall-inventory-net48.md §5 item 7): ILC runs WITHOUT
// a debugger, so the attached/logging state is constant and Break is a no-op.
// The icalls below return the AOT-faithful values instead of leaving the
// members to the generated-name fallback (an undefined C++ symbol):
//
//   get_IsAttached   -> il2cpp_debugger_is_attached (always 0)
//   IsLogging()      -> il2cpp_debugger_is_logging  (always 0)
//   Break()          -> il2cpp_debugger_break        (no-op)
//   Log(level, category, message) -> il2cpp_debugger_log (writes to stderr)
//
// net48 Debugger.Break() routes through the internal BreakInternal icall; in
// user IL the PUBLIC Break() is the call target, so it maps to the no-op
// directly (a debugger to break into never exists). net48 Log() is a silent
// no-op without a debugger; this stub DIVERGES deliberately (audit-sanctioned):
// it writes the message to the platform-independent stderr so AOT diagnostics
// survive -- IsLogging() still reports false (no logging channel exists).
//
// STRING ABI: Log's category/message cross as Il2CppString* (the emitter's
// "s" cast); the runtime converts them to UTF-8 internally -- the console
// module convention (icall_console_write_line_s), NOT the debugext.h
// const char* convention (which has a documented generated-code ABI gap).
//
// Unmapped public members (Launch, BreakForUserUnhandledException, ...) fall
// to the generated-name path (loud link error).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDebugger(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_IsAttached":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_debugger_is_attached", 0, true, false, false);
                return null;
            case "IsLogging":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_debugger_is_logging", 0, true, false, false);
                return null;
            case "Break":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_debugger_break", 0, false, false, false);
                return null;
            case "Log":
                if (count == 3 && ps.Length == 3 && ps[0] == 0x08 && ps[1] == 0x0E && ps[2] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_debugger_log", 3, false, false, false, new[] { "i", "s", "s" });
                throw new NotSupportedException($"Debugger.Log with {count} param(s) not wired: the stub takes (int level, string category, string message)");
            default: return null;
        }
    }
}
