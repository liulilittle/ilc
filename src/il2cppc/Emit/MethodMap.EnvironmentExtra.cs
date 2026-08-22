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
            //   * get_Version is now REACHABLE: the base file no longer wires
            //     it to the int32_t stubs.h symbol (Version-object shape
            //     mismatch -- needs-new-icall).
            case "Exit":
            case "FailFast":
            case "ExpandEnvironmentVariables":
            case "GetEnvironmentVariable":
            case "GetEnvironmentVariables":
            case "GetFolderPath":
            case "GetLogicalDrives":
            case "GetCommandLineArgs":
            case "SetEnvironmentVariable":
            // net9/10 E2E campaign: the 64-bit probes now have runtime
            // icalls (stubs.cpp) -- wired BEFORE the LOUD gate.
            case "get_Is64BitOperatingSystem":
                return I("il2cpp::runtime::icall_environment_get_is_64bit_operating_system", 0, true, false, false);
            case "get_Is64BitProcess":
                return I("il2cpp::runtime::icall_environment_get_is_64bit_process", 0, true, false, false);
            case "get_UserDomainName":
            case "get_Version":
            case "get_WorkingSet":
            case "get_HasShutdownStarted":
            case "get_StackTrace":
            case "get_SystemDirectory":
            case "get_UserInteractive":
                throw new NotSupportedException(
                    $"System.Environment.{name} not wired: the runtime has no matching icall in il2cpp_environment.h");
            default: return null;
        }
    }
}
