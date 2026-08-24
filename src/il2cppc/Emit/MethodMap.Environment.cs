namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Environment -- System.Environment icall mappings
// (il2cpp_environment.h).
// Owners: System.Environment
//
// 2026-08-22 surface sweep: every mapping below is arity-guarded where the
// BCL has overloads the runtime does not cover (GetEnvironmentVariable /
// SetEnvironmentVariable / GetFolderPath / FailFast) -- wrong-arity shapes
// return null and hit TryMapEnvironmentExtra's LOUD gate. get_CommandLine
// added (the net48 property-getter name); get_Version unwired (int32 stub vs
// Version-object member -- needs-new-icall, see the case comment).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapEnvironment(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_NewLine": return I("il2cpp::runtime::icall_environment_get_newline", 0, true, true, false);
            // 2026-08-22 sweep: ARITY GUARDS -- only the plain overloads have
            // runtime icalls. The EnvironmentVariableTarget overloads fall
            // through (null) to TryMapEnvironmentExtra's LOUD gate instead of
            // silently dropping the extra argument (the documented KNOWN GAP).
            case "GetEnvironmentVariable": return count == 1 ? I("il2cpp::runtime::icall_environment_get_environment_variable", 1, true, true, false) : null;
            case "SetEnvironmentVariable": return count == 2 ? I("il2cpp::runtime::icall_environment_set_environment_variable", 2, false, false, false) : null;
            case "get_CurrentDirectory":
            case "GetCurrentDirectory": return I("il2cpp::runtime::icall_environment_get_current_directory", 0, true, true, false);
            case "SetCurrentDirectory": return I("il2cpp::runtime::icall_environment_set_current_directory", 1, false, false, false);
            case "get_TickCount": return I("il2cpp::runtime::icall_environment_get_tick_count", 0, true, false, false);
            // netcore21+ audit surface: HasShutdownStarted -- constant false
            // (the simplified runtime never initiates shutdown while managed
            // code runs; environment.cpp).
            case "get_HasShutdownStarted": return I("il2cpp::runtime::icall_environment_get_has_shutdown_started", 0, true, false, false);
            // P6: Environment.TickCount64 (net8+): 64-bit monotonic counter.
            // Returns int64 (ResultKind "l") -- the 64-bit millisecond count.
            case "get_TickCount64": return I("il2cpp::runtime::icall_environment_get_tick_count64", 0, true, false, false, resultKind: "l");
            case "get_ProcessorCount": return I("il2cpp::runtime::icall_environment_get_processor_count", 0, true, false, false);
            case "get_UserName": return I("il2cpp::runtime::icall_environment_get_user_name", 0, true, true, false);
            case "get_MachineName": return I("il2cpp::runtime::icall_environment_get_machine_name", 0, true, true, false);
            case "get_OSVersion": return I("il2cpp::runtime::icall_environment_get_os_version", 0, true, true, false);
            // D5 dscmp: Environment.Version -- returns a System.Version OBJECT
            // (il2cpp_environment.h contract: the net48 closure reports
            // 4.0.30319.42000). Previously unwired (a PNSE int32 stub would
            // have corrupted the reference-typed member result slot).
            case "get_Version": return I("il2cpp::runtime::icall_environment_get_version", 0, true, true, false, returnTypeName: "System.Version");
            // 2026-08-22 sweep: arity guard -- only the 1-param overload has an
            // icall (il2cpp_environment.h:123); the (SpecialFolder,
            // SpecialFolderOption) overload falls through to the LOUD gate.
            case "GetFolderPath": return count == 1 ? I("il2cpp::runtime::icall_environment_get_special_folder", 1, true, true, false, new[] { "i" }) : null;
            // 2026-08-22 sweep: net48's CommandLine PROPERTY getter is
            // get_CommandLine ("GetCommandLine" is the forward-looking .NET
            // Core alias -- envdeep.cs header note). Both hit the same
            // existing icall (il2cpp_environment.h:133).
            case "get_CommandLine":
            case "GetCommandLine": return I("il2cpp::runtime::icall_environment_get_command_line", 0, true, true, false);
            // M7 env (2026-08-08): ExitCode get/set -- the env audit added
            // icall_environment_get_exit_code / icall_environment_set_exit_code
            // (il2cpp_environment.h:77-78, process-lifetime int32 storage,
            // default 0). get_ExitCode returns the stored int; set_ExitCode
            // stores and never fails (returns status 0 -- dropped).
            case "get_ExitCode": return I("il2cpp::runtime::icall_environment_get_exit_code", 0, true, false, false);
            case "set_ExitCode": return I("il2cpp::runtime::icall_environment_set_exit_code", 1, false, false, false);
            // P6: Environment.FailFast(string message) -- terminate the process
            // immediately. Prints the message to stderr (if non-null), flushes,
            // and calls std::abort(). The managed message is Il2CppString* (UTF-16),
            // converted to UTF-8 via il2cpp_pinvoke_string_to_utf8_alloc.
            // 2026-08-22 sweep: the netcore FailFast(string, string) overload
            // (the second string is an error-source annotation) rides the SAME
            // icall -- the annotation is dropped ("drop" cast).
            case "FailFast": return count == 1 ? I("il2cpp::runtime::icall_environment_fail_fast", 1, false, false, false,
                new[] { "s" })
                : count == 2 ? I("il2cpp::runtime::icall_environment_fail_fast", 2, false, false, false,
                new[] { "s", "drop" })
                // netcore31's FailFast(string, Exception, string): the
                // exception + error-source annotation are dropped.
                : count == 3 ? I("il2cpp::runtime::icall_environment_fail_fast", 3, false, false, false,
                new[] { "s", "drop", "drop" })
                : null;
            // P6: Environment.CurrentManagedThreadId (net8+): the current
            // thread's managed thread id. Returns int32 (OS thread id).
            case "get_CurrentManagedThreadId": return I("il2cpp::runtime::icall_environment_get_current_managed_thread_id", 0, true, false, false);
            // P6: Environment internal methods (stubs).
            case "nativeGetEnvironmentVariable": return I("il2cpp::runtime::icall_environment_get_environment_variable_native", 1, true, true, false, new[] { "s" });
            // 2026-08-22 sweep: get_Version/GetVersion REMOVED from the wired
            // set. net48's Environment.Version is a System.Version OBJECT, but
            // the only runtime symbol (stubs.h icall_environment_get_version)
            // returns int32_t -- wiring it emitted an int32-typed result slot
            // for a reference-typed member (silent shape corruption). The name
            // now falls through to TryMapEnvironmentExtra's LOUD gate;
            // a Version-object-returning icall is NEEDS-NEW-ICALL.
            // GetVersionEx stays: unreachable from the net48/net9/net10 public
            // surface (mono-internal alias), wired to its existing stub.
            // NET48-CLOSURE (2026-08-22): GetVersion/GetVersionEx wire to the
            // PNSE stubs (stubs.cpp) -- the OperatingSystem object model is
            // not carried; the string surface is get_OSVersion above.
            case "GetVersion": return I("il2cpp::runtime::icall_environment_get_version", 0, false, false, false);
            case "GetVersionEx": return I("il2cpp::runtime::icall_environment_get_version_ex", 0, false, false, false);
            case "GetResourceFromDefault": return I("il2cpp::runtime::icall_environment_get_resource_from_default", 2, true, false, false, new[] { "s", "" });
            case "GetCompatibilityFlag": return I("il2cpp::runtime::icall_environment_get_compatibility_flag", 1, true, false, false, new[] { "s" });
            default: return null;
        }
    }
}
