namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Timer -- System.Threading.Timer managed surface (batch 158,
// timercase). The managed Timer object is a runtime-registered class with
// ONE raw il2cpp_timer* handle slot (timer.cpp). The ctor unwraps the
// TimerCallback delegate's methodPtr (icall_delegate_get_fn_ptr contract:
// single-cast STATIC-method delegate only -- null -> ArgumentNullException,
// multicast/instance-bound -> InvalidOperationException); the extracted
// pointer IS the generated void(*)(Il2CppObject*) callback the timer thread
// runs.
//
// Owners: System.Threading.Timer
//
//   Timer(TimerCallback, object state, int dueTimeMs, int periodMs)
//     -> icall_timer_ctor(self, callback, state, due, period)
//   Change(int dueTimeMs, int periodMs) -> bool (true; invalid args throw)
//   Dispose() -> icall_timer_dispose(self) (stop + join; idempotent)
//
// TimerCallback ctor (ldftn + newobj) goes through the standard delegate
// machinery (il2cpp_delegate_new). Timeout.Infinite is a const (-1) --
// constant-folded, no mapping needed.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapTimer(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // Timer(TimerCallback, object, int, int) -- the only wired
                // ctor (4 explicit params + receiver). The callback delegate
                // unwraps in the runtime. ReturnTypeName: the newobj receiver
                // temp tracks as "System.Threading.Timer" so a later
                // IDisposable::Dispose callvirt (using block) re-resolves
                // with the receiver hint (the IDisposable arm dispatches on
                // receiverType/arg0Type).
                if (count == 4)
                    return I("il2cpp::runtime::icall_timer_ctor", 4, false, false, true,
                        new[] { "", "", "", "", "" }, returnTypeName: "System.Threading.Timer");
                return null;
            case "Change":
                // bool Change(int dueTimeMs, int periodMs): re-arms the
                // timer from now; returns true (invalid args throw).
                if (count == 2)
                    return I("il2cpp::runtime::icall_timer_change", 2, true, false, true,
                        new[] { "", "", "" });
                return null;
            case "Dispose":
                // void Dispose() -- stop + join the timer thread. Both
                // Timer::Dispose() and IDisposable::Dispose() route here
                // (the callvirt on the interface resolves through the
                // receiver-typed fallback to this owner mapping).
                if (count == 0)
                    return I("il2cpp::runtime::icall_timer_dispose", 0, false, false, true,
                        new[] { "" });
                return null;
            default:
                return null;
        }
    }
}
