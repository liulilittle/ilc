namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Stopwatch -- System.Diagnostics.Stopwatch mappings (stopwatch.h).
// Owners: System.Diagnostics.Stopwatch
//
// RUNTIME OBJECT CLOSURE (M6, swcase #35): Stopwatch is now a managed object
// -- the runtime defines the System_Diagnostics_Stopwatch_class descriptor
// (Il2CppStopwatchObject: header + start timestamp + accumulated ticks +
// isRunning flag, stopwatch.h) and the object icalls below own the state
// storage (the Il2CppVersionObject / icall_version_* precedent). StartNew
// allocates internally through System_Diagnostics_Stopwatch_class; plain
// new Stopwatch() uses the BclAllocExpr fallback
// il2cpp_object_new(&System_Diagnostics_Stopwatch_class) -- the same GLOBAL-descriptor
// contract as System.Version, so NO BclAllocExpr entry is required.
//
//   StartNew()                    -> icall_stopwatch_start_new  (static,
//                                     allocates + starts, returns Stopwatch)
//   .ctor()                       -> icall_stopwatch_ctor_default
//   Start/Stop/Restart/Reset      -> icall_stopwatch_start/stop/restart/reset
//   IsRunning                     -> icall_stopwatch_get_is_running (bool)
//   ElapsedTicks                  -> icall_stopwatch_get_elapsed_ticks (int64)
//   ElapsedMilliseconds           -> icall_stopwatch_get_elapsed_milliseconds
//   ElapsedMicroseconds           -> icall_stopwatch_get_elapsed_microseconds
//   Elapsed                       -> icall_stopwatch_get_elapsed -> Il2CppTimeSpan
//                                     (ReturnTypeName "System.TimeSpan" so a
//                                     chained .TotalMilliseconds resolves)
//
// net48 semantics owned by the runtime: Start is a no-op while running,
// Stop freezes the elapsed (frozen ticks are stable across reads), Restart =
// Reset + Start. The five static primitives (Frequency/GetTimestamp and the
// (start, end) conversion helpers) remain as before.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStopwatch(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // --- static primitives (real mappings) ---
            case "get_Frequency":
                // Stopwatch.Frequency (static readonly long): counter ticks
                // per second, stable for the process lifetime.
                return I("il2cpp::runtime::il2cpp_stopwatch_get_frequency", 0, true, false, false, null, "l");
            case "GetTimestamp":
                // Stopwatch.GetTimestamp() (static long): current raw counter
                // reading (monotonic).
                return I("il2cpp::runtime::il2cpp_stopwatch_get_timestamp", 0, true, false, false, null, "l");
            case "StartNew":
                // Stopwatch.StartNew() -- STATIC factory: the runtime
                // allocates a Stopwatch object through System_Diagnostics_Stopwatch_class,
                // starts it (running, elapsed 0) and returns it. The result is
                // a tracked "System.Diagnostics.Stopwatch" reference; instance
                // members receive it as their receiver argument.
                if (count == 0)
                    return I("il2cpp::runtime::icall_stopwatch_start_new", 0, true, true, false,
                        returnTypeName: "System.Diagnostics.Stopwatch");
                return null;
            case ".ctor":
                // Stopwatch() -- zero state (not running, elapsed 0). The
                // descriptor's zero-init makes this the ctor's whole job; the
                // icall makes the state explicit.
                if (count == 0) return I("il2cpp::runtime::icall_stopwatch_ctor_default", 0, false, false, true);
                return null;

            // --- instance members: object-state icalls (Il2CppObject* self) ---
            case "Start":
                return I("il2cpp::runtime::icall_stopwatch_start", 0, false, false, true);
            case "Stop":
                return I("il2cpp::runtime::icall_stopwatch_stop", 0, false, false, true);
            case "Restart":
                return I("il2cpp::runtime::icall_stopwatch_restart", 0, false, false, true);
            case "Reset":
                return I("il2cpp::runtime::icall_stopwatch_reset", 0, false, false, true);
            case "get_IsRunning":
                return I("il2cpp::runtime::icall_stopwatch_get_is_running", 0, true, false, true);
            case "get_ElapsedTicks":
                return I("il2cpp::runtime::icall_stopwatch_get_elapsed_ticks", 0, true, false, true, null, "l");
            case "get_ElapsedMilliseconds":
                return I("il2cpp::runtime::icall_stopwatch_get_elapsed_milliseconds", 0, true, false, true, null, "l");
            case "get_ElapsedMicroseconds":
                return I("il2cpp::runtime::icall_stopwatch_get_elapsed_microseconds", 0, true, false, true, null, "l");
            case "get_Elapsed":
                // Returns the elapsed as a TimeSpan (Il2CppTimeSpan); the
                // ReturnTypeName tracks the result so a chained member call
                // (sw.Elapsed.TotalMilliseconds) resolves with a
                // "System.TimeSpan" receiver.
                return I("il2cpp::runtime::icall_stopwatch_get_elapsed", 0, true, false, true, null, "ts",
                    returnTypeName: "System.TimeSpan");
            default: return null;
        }
    }
}
