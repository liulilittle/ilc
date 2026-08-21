namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Timers -- System.Timers.Timer managed surface (batch 161,
// timerscase). The Component-wrapper Timer object is a runtime-registered
// class (timer.cpp): header | raw threading-timer handle | Elapsed delegate
// (ref) | AutoReset | Interval. The threading timer fires the Elapsed event
// through the 2-ref-arg multicast invoke (icall_multicast_invoke_rr_void).
//
// Owners: System.Timers.Timer
//
//   Timer(double interval)      -> icall_timers_ctor(self, ms)  (disarmed)
//   Timer()                     -> icall_timers_ctor_default(self)
//   set_Interval(double)        -> icall_timers_set_interval
//   set_AutoReset(bool)         -> icall_timers_set_auto_reset
//   add_Elapsed(ElapsedEventHandler) -> icall_timers_add_elapsed (Combine)
//   Start() / Stop()            -> icall_timers_start / icall_timers_stop
//   Dispose()                   -> icall_timers_dispose (also IDisposable)
//
// ElapsedEventHandler delegate ctor (ldftn + newobj) goes through the
// standard delegate machinery (il2cpp_delegate_new -- the whitelist in
// StackFold.IsDelegateType accepts it). The handler ABI is the 2-ref-arg
// void form the runtime bridge dispatches.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapTimers(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // Timer(double interval) -- the double IL value truncates to
                // int32 ms in the runtime (net48 millisecond granularity).
                // ReturnTypeName: the newobj receiver temp tracks as
                // "System.Timers.Timer" so the using-block
                // IDisposable::Dispose re-resolves with the receiver hint.
                if (count == 1)
                    return I("il2cpp::runtime::icall_timers_ctor", 1, false, false, true,
                        new[] { "", "d" }, returnTypeName: "System.Timers.Timer");
                // Timer() -- parameterless (default Interval 100 ms).
                if (count == 0)
                    return I("il2cpp::runtime::icall_timers_ctor_default", 0, false, false, true,
                        new[] { "" }, returnTypeName: "System.Timers.Timer");
                return null;
            case "set_Interval":
                if (count == 1)
                    return I("il2cpp::runtime::icall_timers_set_interval", 1, false, false, true,
                        new[] { "", "d" });
                return null;
            case "set_AutoReset":
                if (count == 1)
                    return I("il2cpp::runtime::icall_timers_set_auto_reset", 1, false, false, true,
                        new[] { "", "b" });
                return null;
            case "add_Elapsed":
                if (count == 1)
                    return I("il2cpp::runtime::icall_timers_add_elapsed", 1, false, false, true,
                        new[] { "", "" });
                return null;
            case "Start":
                if (count == 0)
                    return I("il2cpp::runtime::icall_timers_start", 0, false, false, true,
                        new[] { "" });
                return null;
            case "Stop":
                if (count == 0)
                    return I("il2cpp::runtime::icall_timers_stop", 0, false, false, true,
                        new[] { "" });
                return null;
            case "Dispose":
                if (count == 0)
                    return I("il2cpp::runtime::icall_timers_dispose", 0, false, false, true,
                        new[] { "" });
                return null;
            default:
                return null;
        }
    }
}
