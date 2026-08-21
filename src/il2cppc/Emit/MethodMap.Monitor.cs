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
//   * TryEnter(obj, timeout)     -- no timeout variant of the try_enter
//     icall exists; silently dropping the timeout would turn a bounded
//     wait into a single poll (semantics lie), so it stays loud.
//   * Wait(obj[, TimeSpan][, exitContext]) -- the wait icall requires an
//     explicit int32 timeout; there is no emitter mechanism to inject
//     Timeout.Infinite (-1) or convert TimeSpan, so these stay loud.
//     Wait(obj, int) is the wired form.
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
                    ? throw new NotSupportedException("TryEnter(object, timeout) -- the runtime icall has no timeout variant; add one to monitor.h first")
                    : null;
            case "Pulse":
            case "PulseAll":
                // monitor.h: the simplified Pulse wakes ALL waiters, so the
                // PulseAll overload maps to the same icall exactly.
                return ps.Length == 1 && ps[0] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_monitor_pulse", 1, false, false, false, new[] { "" })
                    : null;
            case "Wait":
                return ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x08
                    ? I("il2cpp::runtime::il2cpp_monitor_wait", 2, true, false, false, new[] { "", "i" })
                    : ps.Length == 1 && ps[0] == 0x1C
                    ? throw new NotSupportedException("Wait(object) -- no way to inject Timeout.Infinite (-1) into the timeout parameter; use Wait(object, int)")
                    : ps.Length >= 2 && ps[0] == 0x1C && ps[1] == 0x11
                    ? throw new NotSupportedException("Wait(object, TimeSpan) -- no TimeSpan-to-milliseconds conversion in the emitter; use Wait(object, int)")
                    : ps.Length >= 2 && ps[0] == 0x1C && ps[1] is 0x08 or 0x11
                    ? throw new NotSupportedException("Wait(object, timeout, exitContext) -- exitContext is not modeled; use the two-argument form")
                    : null;
            default: return null;
        }
    }
}
