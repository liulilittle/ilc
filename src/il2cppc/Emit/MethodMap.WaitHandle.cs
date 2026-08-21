namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.WaitHandle -- System.Threading wait-handle icall mappings
// (il2cpp_waithandle.h, implementation waithandle.cpp).
// Owners: System.Threading.Semaphore / Mutex / EventWaitHandle
//
// OBJECT MODEL: each wait handle is a managed object (Il2CppWaitHandleObject
// -- header + ONE native state pointer) allocated through its GLOBAL class
// descriptor (System_Threading_Semaphore_class / Mutex_class /
// EventWaitHandle_class) -- the transpiler's BclAllocExpr fallback emits
// il2cpp_object_new(&System_Threading_*_class) for `new Semaphore(...)` etc.
// (TypeCppName sanitizes the full names, no explicit BclAllocExpr entry
// needed -- the System.Version contract).
//
// Every icall takes the managed receiver (Il2CppObject* self) as its first
// parameter -- the receiver passes through the "" passthrough cast (the
// object IS the pointer). int32 args use the "i" cast; the EventWaitHandle
// ctor's bool initialState uses the "b" cast (0x02 BOOLEAN) and the
// EventResetMode enum uses "i" (0x11 VALUETYPE, int32 carrier).
//
// Wired surface:
//   * Semaphore(int, int) ctor / WaitOne / Release() / Release(int)
//   * Mutex() ctor / WaitOne / ReleaseMutex
//   * EventWaitHandle(bool, EventResetMode) ctor / WaitOne / Set / Reset
//
// Known-unwireable members return null (the generated-name path):
//   * WaitOne(int) / WaitOne(TimeSpan) -- the timeout overloads are out of
//     scope (the runtime has the blocking WaitOne only).
//   * WaitAll / WaitAny / SignalAndWait -- the static multi-handle surface is
//     out of scope.
//   * Semaphore(int, int, string) / Mutex(bool, string) -- the named-handle
//     ctors are out of scope.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapWaitHandle(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // Semaphore(int initialCount, int maximumCount).
                if (owner == "System.Threading.Semaphore" && ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_waithandle_semaphore_ctor", 2, false, false, true, new[] { "", "i", "i" });
                // Mutex().
                if (owner == "System.Threading.Mutex" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_waithandle_mutex_ctor", 0, false, false, true);
                // EventWaitHandle(bool initialState, EventResetMode mode).
                if (owner == "System.Threading.EventWaitHandle" && ps.Length == 2 && ps[0] == 0x02 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_waithandle_event_ctor", 2, false, false, true, new[] { "", "b", "i" });
                return null;
            // bool WaitOne(): the blocking form (net48 success semantics).
            case "WaitOne":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_waithandle_wait_one", 0, true, false, true);
                return null;
            // int Release() / int Release(int releaseCount) -- Semaphore only.
            case "Release":
                if (owner == "System.Threading.Semaphore" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_waithandle_semaphore_release", 0, true, false, true);
                if (owner == "System.Threading.Semaphore" && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_waithandle_semaphore_release_n", 1, true, false, true, new[] { "", "i" });
                return null;
            // void ReleaseMutex() -- Mutex only.
            case "ReleaseMutex":
                if (owner == "System.Threading.Mutex" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_waithandle_mutex_release", 0, false, false, true);
                return null;
            // bool Set() / bool Reset() -- EventWaitHandle only.
            case "Set":
                if (owner == "System.Threading.EventWaitHandle" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_waithandle_event_set", 0, true, false, true);
                return null;
            case "Reset":
                if (owner == "System.Threading.EventWaitHandle" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_waithandle_event_reset", 0, true, false, true);
                return null;
            default: return null;
        }
    }
}
