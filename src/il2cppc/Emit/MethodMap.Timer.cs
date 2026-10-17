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
                //
                // nb1 TIM-OVL ps guard: the net48 ctor overloads are
                // distinguished by the LAST TWO parameter bytes (ps[2] /
                // ps[3] -- ps[0]=TimerCallback CLASS 0x12, ps[1]=object
                // 0x1C; ECMA element bytes: I4=0x08, I8=0x0A, U4=0x09,
                // VALUETYPE=0x11):
                //   int,int      (0x08,0x08)   -> icall_timer_ctor (int32 ms)
                //   long,long    (0x0A,0x0A)   -> icall_timer_ctor_64
                //   uint,uint    (0x09,0x09)   -> icall_timer_ctor_64 (the
                //                                uint bit pattern rides the
                //                                int64 slot; MaxValue=-1)
                //   TimeSpan,..  (0x11,0x11)   -> icall_timer_ctor_ts (raw
                //                                ticks; runtime /10000 -> ms)
                // Previously the count==4 shape blindly bound EVERY overload
                // to the int32 icall (long/uint silent narrowing, TimeSpan a
                // C0 compile break: cannot convert Il2CppTimeSpan to int32_t).
                if (count == 4 && ps.Length >= 4)
                {
                    if (ps[2] == 0x08 && ps[3] == 0x08)
                        return I("il2cpp::runtime::icall_timer_ctor", 4, false, false, true,
                            new[] { "", "", "", "", "" }, returnTypeName: "System.Threading.Timer");
                    if (ps[2] == 0x0A && ps[3] == 0x0A)
                        return I("il2cpp::runtime::icall_timer_ctor_64", 4, false, false, true,
                            new[] { "", "", "", "l", "l" }, returnTypeName: "System.Threading.Timer");
                    if (ps[2] == 0x09 && ps[3] == 0x09)
                        return I("il2cpp::runtime::icall_timer_ctor_64", 4, false, false, true,
                            new[] { "", "", "", "l", "l" }, returnTypeName: "System.Threading.Timer");
                    if (ps[2] == 0x11 && ps[3] == 0x11)
                        return I("il2cpp::runtime::icall_timer_ctor_ts", 4, false, false, true,
                            new[] { "", "", "", "ts_ticks", "ts_ticks" }, returnTypeName: "System.Threading.Timer");
                }
                return null;
            case "Change":
                // bool Change(int dueTimeMs, int periodMs): re-arms the
                // timer from now; returns true (invalid args throw).
                // nb1 TIM-OVL ps guard: the Change overloads (2 explicit
                // params; ps[0]/ps[1] carry the time-arg types):
                //   (int,int) / (long,long) / (uint,uint) / (TimeSpan,..)
                // map to their icall family exactly like the ctor above.
                if (count == 2 && ps.Length >= 2)
                {
                    if (ps[0] == 0x08 && ps[1] == 0x08)
                        return I("il2cpp::runtime::icall_timer_change", 2, true, false, true,
                            new[] { "", "", "" });
                    if (ps[0] == 0x0A && ps[1] == 0x0A)
                        return I("il2cpp::runtime::icall_timer_change_64", 2, true, false, true,
                            new[] { "", "l", "l" });
                    if (ps[0] == 0x09 && ps[1] == 0x09)
                        return I("il2cpp::runtime::icall_timer_change_64", 2, true, false, true,
                            new[] { "", "l", "l" });
                    if (ps[0] == 0x11 && ps[1] == 0x11)
                        return I("il2cpp::runtime::icall_timer_change_ts", 2, true, false, true,
                            new[] { "", "ts_ticks", "ts_ticks" });
                }
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
