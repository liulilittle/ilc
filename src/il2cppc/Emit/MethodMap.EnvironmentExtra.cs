namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.EnvironmentExtra -- System.Environment gap gate (environmentext gap-fill).
// Owners: System.Environment (environmentext gap-fill)
//
// The runtime surface (il2cpp_environment.h + icall_environment_get_newline
// in il2cpp_runtime.h) is consumed by MethodMap.Environment.cs. This file
// carries NO new icall mappings -- it is the LOUD rejection gate for the
// net48 System.Environment members that have no runtime implementation.
// Members not listed here return null (the documented fallback to the
// generated-name path).
//
// 2026-08-22 surface sweep: the full reachable net48 public surface is now
// either WIRED (base file, arity-guarded) or LOUD-gated here -- nothing
// falls through silently:
//   * get_CurrentManagedThreadId gate case REMOVED (dead: the base file has
//     wired it since the P6 batch; the case could never fire).
//   * The KNOWN GAP below is CLOSED: the base file arity-guards
//     GetEnvironmentVariable / SetEnvironmentVariable / GetFolderPath /
//     FailFast, so only the uncovered overload shapes land here.
//   * get_Version now actually reaches this gate: its former base wiring
//     targeted the int32_t stubs.h symbol while the managed member returns
//     a System.Version object (shape mismatch = needs-new-icall).
//
// RESOLVED KNOWN GAP (was: out of this file's domain -- MethodMap.Environment.cs
// was count-blind): the EnvironmentVariableTarget overloads used to hit the
// base-name case and resolve to the 1/2-param icall with the extra argument
// silently dropped. Fixed by arity guards in MethodMap.Environment.cs
// (2026-08-22); the uncovered shapes fall through to this gate.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapEnvironmentExtra(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // net48 members with no runtime icall -- LOUD: the transpiler
            // stops here instead of emitting a dangling C++ call to a
            // non-existent symbol (the generated-name fallback would link
            // against nothing).
            //
            // 2026-08-22 surface sweep additions:
            //   * get_WorkingSet / get_Is64BitProcess /
            //     get_Is64BitOperatingSystem / get_UserDomainName -- reachable
            //     net48 property getters that previously fell through BOTH
            //     files to the generated-name path (dangling symbol).
            //   * GetEnvironmentVariable / SetEnvironmentVariable /
            //     GetFolderPath / FailFast -- the EnvironmentVariableTarget /
            //     SpecialFolderOption / FailFast(string,string) OVERLOAD
            //     shapes. MethodMap.Environment.cs now arity-guards these
            //     names and returns null for the uncovered overloads, which
            //     lands here (the correct-arity shapes never reach this gate).
            //   * get_Version was gated here while the base file had no wiring;
            //     it is WIRED unconditionally by MethodMap.Environment.cs now,
            //     so its gate arm was dead code (removed 2026-11-01 with
            //     get_HasShutdownStarted, same situation).
            // nb1 ENV-SPOT (2026-10-17): GetCommandLineArgs / GetLogicalDrives
            // / ExpandEnvironmentVariables / get_SystemDirectory /
            // get_UserDomainName / get_UserInteractive are now WIRED in the
            // base file (new environment.cpp icalls); their LOUD-gate cases
            // were REMOVED from the set below.
            //
            // stacktrace-real (2026-11-01): get_StackTrace (Environment.
            // StackTrace) is now a REAL wired member -- net48 has it (it is
            // what `Environment.StackTrace` IS, and the net48 oracle's own
            // output prints "at System.Environment.get_StackTrace()"), and the
            // runtime can produce it: Environment.StackTrace is
            // `new StackTrace(true).ToString()`, and the transpiled runtime
            // keeps a per-thread shadow frame stack with every live method's
            // managed spelling (il2cpp_frame.h), so the text is formattable.
            // It NEVER belonged in a LOUD gate:
            // the old arm deleted the whole enclosing method, so one
            // `Console.WriteLine(Environment.StackTrace)` made an entire
            // program untranspilable.
            case "get_StackTrace":
                return I("il2cpp::runtime::icall_environment_get_stack_trace", 0, true, true, false);
            // net9/10 E2E campaign: the 64-bit probes now have runtime
            // icalls (stubs.cpp) -- wired BEFORE the LOUD gate. NOTE: these
            // two arms MUST precede the fall-through chain -- a return
            // placed INSIDE the chain would silently wire every label above
            // it to the probe icall (the regression the EnvRandom unit
            // tests caught: Exit/GetEnvironmentVariable resolved to
            // is_64bit_operating_system instead of rejecting LOUD).
            case "get_Is64BitOperatingSystem":
                return I("il2cpp::runtime::icall_environment_get_is_64bit_operating_system", 0, true, false, false);
            case "get_Is64BitProcess":
                return I("il2cpp::runtime::icall_environment_get_is_64bit_process", 0, true, false, false);
            case "Exit": return I("il2cpp::runtime::icall_environment_exit", 1, false, false, false, new[] { "i" });
            // The QCall stub spelling of Exit (same-assembly InternalCall
            // MethodDef named "_Exit"): same icall, same shape.
            case "_Exit":
                if (count == 1)
                    return I("il2cpp::runtime::icall_environment_exit", 1, false, false, false, new[] { "i" });
                return null;
            // The unimplemented overload shapes reject LOUD: the base file
            // arity-guards these names and returns null for the shapes it does
            // not serve, which land here (a phantom generated-name call would
            // never link).
            //
            // MESSAGE CORRECTION (stacktrace-real): the old text claimed
            // "LOUD whitelist (net48 has no such member)" for EVERY member in
            // this set. That is false for all of them -- net48 has
            // FailFast(string, Exception), GetEnvironmentVariable(string,
            // EnvironmentVariableTarget), GetEnvironmentVariables,
            // GetFolderPath(SpecialFolder, SpecialFolderOption) and
            // SetEnvironmentVariable(string, string,
            // EnvironmentVariableTarget). The TRUE reason is "the runtime has
            // no icall for this overload yet", which is what the message says
            // now. (get_Version and get_HasShutdownStarted used to sit here
            // too -- both are WIRED unconditionally by the base file now, so
            // their arms were dead code and are gone.)
            case "FailFast":
            case "GetEnvironmentVariable":
            case "GetEnvironmentVariables":
            case "GetFolderPath":
            case "SetEnvironmentVariable":
                throw new NotSupportedException(
                    $"the runtime has no System.Environment.{name} icall for this parameter shape yet -- " +
                    "the wired overloads are arity-guarded in MethodMap.Environment.cs, so only the uncovered " +
                    "net48 overloads reach this gate [Reference Source: net48 mscorlib System.Environment]");
            default: return null;
        }
    }
}
