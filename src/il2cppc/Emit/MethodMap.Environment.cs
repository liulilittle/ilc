namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Environment -- System.Environment icall mappings
// (il2cpp_environment.h).
// Owners: System.Environment
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapEnvironment(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_NewLine": return I("il2cpp::runtime::icall_environment_get_newline", 0, true, true, false);
            case "GetEnvironmentVariable": return I("il2cpp::runtime::icall_environment_get_environment_variable", 1, true, true, false);
            case "SetEnvironmentVariable": return I("il2cpp::runtime::icall_environment_set_environment_variable", 2, false, false, false);
            case "get_CurrentDirectory":
            case "GetCurrentDirectory": return I("il2cpp::runtime::icall_environment_get_current_directory", 0, true, true, false);
            case "SetCurrentDirectory": return I("il2cpp::runtime::icall_environment_set_current_directory", 1, false, false, false);
            case "get_TickCount": return I("il2cpp::runtime::icall_environment_get_tick_count", 0, true, false, false);
            // P6: Environment.TickCount64 (net8+): 64-bit monotonic counter.
            // Returns int64 (ResultKind "l") -- the 64-bit millisecond count.
            case "get_TickCount64": return I("il2cpp::runtime::icall_environment_get_tick_count64", 0, true, false, false, resultKind: "l");
            case "get_ProcessorCount": return I("il2cpp::runtime::icall_environment_get_processor_count", 0, true, false, false);
            case "get_UserName": return I("il2cpp::runtime::icall_environment_get_user_name", 0, true, true, false);
            case "get_MachineName": return I("il2cpp::runtime::icall_environment_get_machine_name", 0, true, true, false);
            case "get_OSVersion": return I("il2cpp::runtime::icall_environment_get_os_version", 0, true, true, false);
            case "GetFolderPath": return I("il2cpp::runtime::icall_environment_get_special_folder", 1, true, true, false, new[] { "i" });
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
            case "FailFast": return I("il2cpp::runtime::icall_environment_fail_fast", 1, false, false, false,
                new[] { "s" });
            // P6: Environment.CurrentManagedThreadId (net8+): the current
            // thread's managed thread id. Returns int32 (OS thread id).
            case "get_CurrentManagedThreadId": return I("il2cpp::runtime::icall_environment_get_current_managed_thread_id", 0, true, false, false);
            default: return null;
        }
    }
}
