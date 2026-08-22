namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.EnvironmentExtra -- System.Environment gap gate (environmentext gap-fill).
// Owners: System.Environment (environmentext gap-fill)
//
// The runtime surface (il2cpp_environment.h + icall_environment_get_newline
// in il2cpp_runtime.h) is FULLY consumed by MethodMap.Environment.cs (12/12
// icalls wired: NewLine/CommandLine/GetFolderPath among them -- the 2026-08-03
// difference-set check). This file therefore carries NO new icall mappings --
// it is the LOUD rejection gate for the remaining net48 System.Environment
// members that have no runtime implementation (get_CurrentManagedThreadId
// added 2026-08-03: previously it fell through to the generated-name path,
// emitting a dangling C++ symbol instead of failing loudly). Members not
// listed here return null (the documented fallback to the generated-name
// path).
//
// KNOWN GAP (out of this file's domain -- MethodMap.Environment.cs is
// count-blind): the EnvironmentVariableTarget overloads
// GetEnvironmentVariable(string, EnvironmentVariableTarget) and
// SetEnvironmentVariable(string, string, EnvironmentVariableTarget) hit the
// base-name case in MethodMap.Environment.cs and resolve to the 1/2-param
// icall with the extra argument silently dropped. Fix = count guard in
// MethodMap.Environment.cs; cannot be intercepted here (the ?? fallback only
// runs when the base file returns null).
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
            case "Exit":
            case "FailFast":
            case "ExpandEnvironmentVariables":
            case "GetLogicalDrives":
            case "GetCommandLineArgs":
            case "GetEnvironmentVariables":
            case "get_CurrentManagedThreadId":
            case "get_HasShutdownStarted":
            case "get_StackTrace":
            case "get_SystemDirectory":
            case "get_UserInteractive":
            case "get_Version":
                throw new NotSupportedException(
                    $"System.Environment.{name} not wired: the runtime has no matching icall in il2cpp_environment.h");
            default: return null;
        }
    }
}
