namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Monitor -- System.Threading.Monitor icall mappings (monitor.h --
// the runtime's side-table monitor: enter/exit/try_enter/pulse/wait).
// Owners: System.Threading.Monitor
//
// LOCK STATEMENT: the C# lock lowering (Roslyn) is ordinary calls --
//   Monitor.Enter(obj, ref lockTaken) + try/finally { if (lockTaken)
//   Monitor.Exit(obj); }  (older compilers: Enter(obj) only) -- so wiring
//   these members IS lock support; the transpiler needs no lock special-
//   casing.
//
//   Enter(object, ref bool) maps to the SAME enter icall with the flag
//   argument passed through as its ADDRESS (&lockTaken). The runtime
//   overload il2cpp_monitor_enter(Il2CppObject*, bool*) writes
//   *lockTaken = true on acquire, so the generated if(lockTaken) guard
//   passes and the finally always Exits -- exact net48 semantics.
//   NOTE: monitor.h/cpp currently declare ONLY the 1-arg form, so the
//   2-arg call fails LOUDLY at C++ compile time until that overload
//   lands (a 2-line runtime change, gap paradigm).
//   NEVER "drop" the flag argument: the flag local stays false forever,
//   the guard if(lockTaken) ALWAYS skips Monitor.Exit and the monitor
//   silently leaks (recursion counter grows unbounded; other threads
//   block forever). Verified 2026-08-04 on a transpiled lock statement.
//
// Known-unwireable overloads throw NotSupportedException (LOUD):
//   * (none in the Monitor domain since nb3 -- Wait(object) now maps to the
//     1-arg infinite overload; see the Wait arm below).
// (parity-T8: TryEnter(obj, timeout[, lockTaken]) and Wait(obj, TimeSpan) /
// Wait(obj, timeout, exitContext) ride the landed monitor.h overloads;
// nb3 SYNC-GAP: Wait(obj) rides il2cpp_monitor_wait(Il2CppObject*).)
//
// PulseAll maps to the same pulse icall: monitor.h documents the
// SIMPLIFIED runtime semantics as "one Pulse wakes ALL waiters" -- i.e.
// the runtime's pulse IS net48 PulseAll, so the mapping is exact.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapMonitor(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Enter":
                // Enter(object) -- older compiler lock lowering / direct calls.
                return ps.Length == 1 && ps[0] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_monitor_enter", 1, false, false, false, new[] { "" })
                    // Enter(object, ref bool lockTaken) -- Roslyn lock lowering.
                    // ps[1] == 0x02 (the BYREF element: ReadParamByte carries
                    // the ELEMENT byte, not the 0x10 marker): the flag's
                    // ADDRESS is passed through (cast "" emits &lockTaken) so
                    // the runtime bool* overload can write *lockTaken = true.
                    // Loud C++ compile failure until that overload lands (see
                    // header).
                    : ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x02
                    ? I("il2cpp::runtime::il2cpp_monitor_enter", 2, false, false, false, new[] { "", "" })
                    : null;
            case "Exit":
                return ps.Length == 1 && ps[0] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_monitor_exit", 1, false, false, false, new[] { "" })
                    : null;
            case "TryEnter":
                return ps.Length == 1 && ps[0] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_monitor_try_enter", 1, true, false, false, new[] { "" })
                    : ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x02
                    // M7 mon (2026-08-08): TryEnter(object, ref bool lockTaken)
                    // -- the net48 2-arg form. The runtime overload
                    // il2cpp_monitor_try_enter(Il2CppObject*, bool*) (monitor.h:91,
                    // added by the mon audit F2) writes *lockTaken = acquired
                    // (net48: ArgumentException on an already-true flag, checked
                    // BEFORE the null object). The flag's ADDRESS is passed
                    // through (cast "" -> &lockTaken, same as the Enter 2-arg
                    // shape). ReturnsValue false: net48 TryEnter(object, ref
                    // bool) is VOID -- the acquired result rides the flag, not
                    // the call's return.
                    ? I("il2cpp::runtime::il2cpp_monitor_try_enter", 2, false, false, false, new[] { "", "" })
                    : ps.Length == 2 && ps[0] == 0x1C && ps[1] is 0x08 or 0x11
                    // parity-T8: TryEnter(object, timeoutMs) -- the landed
                    // 2-arg overload (monitor.h:124; A2). -1 blocks until
                    // acquired, 0 polls once, < -1 throws AORE in runtime.
                    ? I("il2cpp::runtime::il2cpp_monitor_try_enter", 2, true, false, false, new[] { "", "i" })
                    : ps.Length == 3 && ps[0] == 0x1C && ps[1] == 0x08 && ps[2] == 0x02
                    // parity-T8: TryEnter(object, timeoutMs, ref bool
                    // lockTaken) -- the landed 3-arg overload (monitor.h:133).
                    // The flag ADDRESS passes through (Enter 2-arg precedent).
                    // net48 is VOID (the result rides the flag -- the runtime
                    // bool return is dropped, the 2-arg ref-bool precedent).
                    ? I("il2cpp::runtime::il2cpp_monitor_try_enter", 3, false, false, false, new[] { "", "i", "" })
                    : null;
            case "Pulse":
            case "PulseAll":
                // monitor.h: the simplified Pulse wakes ALL waiters, so the
                // PulseAll overload maps to the same icall exactly.
                return ps.Length == 1 && ps[0] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_monitor_pulse", 1, false, false, false, new[] { "" })
                    : null;
            case "Wait":
                // nb3 SYNC-GAP: Wait(object) -- the single-arg form IS
                // Timeout.Infinite (-1); the runtime overload
                // il2cpp_monitor_wait(Il2CppObject*) (monitor.h) already
                // delegates to wait(obj, -1), so the arm is a plain 1-param
                // mapping (previously a LOUD NotSupportedException -- the
                // "no way to inject -1" argument died when the 1-arg overload
                // landed). probe-pinned: p-nb3-sync [sync.mw.single].
                return ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x08
                    ? I("il2cpp::runtime::il2cpp_monitor_wait", 2, true, false, false, new[] { "", "i" })
                    : ps.Length == 1 && ps[0] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_monitor_wait", 1, true, false, false, new[] { "" })
                    : ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x11
                    // parity-T8: Wait(object, TimeSpan) -- the landed
                    // wait_timespan overload (monitor.h:143; A2). The TimeSpan
                    // rides raw ticks ("ts_ticks" reads Il2CppTimeSpan.ticks;
                    // the runtime converts with net48 truncation semantics).
                    ? I("il2cpp::runtime::il2cpp_monitor_wait_timespan", 2, true, false, false, new[] { "", "ts_ticks" })
                    : ps.Length == 3 && ps[0] == 0x1C && ps[1] == 0x08 && ps[2] == 0x02
                    // parity-T8: Wait(object, timeoutMs, exitContext) -- the
                    // landed wait_exit overload (monitor.h:150; A2). No sync
                    // contexts are modeled, so the flag is an observably exact
                    // no-op; the bool slot rides the int32 carrier ("i").
                    ? I("il2cpp::runtime::il2cpp_monitor_wait_exit", 3, true, false, false, new[] { "", "i", "i" })
                    : null;
            default: return null;
        }
    }
}
