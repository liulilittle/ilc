namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Thread -- System.Threading.Thread icall mappings
// (il2cpp_thread.h for the statics + threadobj.h for the OBJECT model).
// Owners: System.Threading.Thread
//
// STATICS (thread.cpp): Sleep / Yield / BeginCriticalRegion /
// EndCriticalRegion -- the existing icall_thread_* entries.
//
// OBJECT MODEL (threadobj domain, threadobj.cpp): new Thread(ThreadStart),
// Start, Join, IsAlive, ManagedThreadId, Priority, CurrentThread. The
// Thread object is a managed object (System_Threading_Thread_class
// descriptor in the runtime; generated newobj allocates through the
// BclAllocExpr fallback il2cpp_object_new(&System_Threading_Thread_class));
// the ctor extracts the ThreadStart delegate's code pointer
// (icall_delegate_get_fn_ptr validation: null -> ArgumentNullException,
// multicast/instance-bound -> InvalidOperationException) and Start spawns
// a REAL OS thread that runs it (see threadobj.h for the model).
//
//   .ctor(ThreadStart)      -> icall_threadobj_ctor (instance, void)
//   Start()                 -> icall_threadobj_start
//   Join()                  -> icall_threadobj_join
//   get_IsAlive             -> icall_threadobj_get_is_alive (bool)
//   get_ManagedThreadId     -> icall_threadobj_get_managed_thread_id (int32)
//   get_Priority            -> icall_threadobj_get_priority (int32, pinned 0)
//   set_Priority(int32)     -> icall_threadobj_set_priority (accepted-ignored)
//   get_IsBackground        -> icall_threadobj_get_is_background (bool)
//   set_IsBackground(bool)  -> icall_threadobj_set_is_background (flag
//                              storage; accepted on any state -- net48
//                              throws on stopped threads, documented
//                              deviation in threadobj.h)
//   get_CurrentThread       -> icall_threadobj_get_current_thread (static,
//                              returns a fresh Thread object)
//
// Members without an icall (Abort, GetHashCode, ThreadPool bridging, ...)
// fall to the generated-name path -- not LOUD by convention here (pre-M6
// behaviour preserved).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapThread(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Sleep":
                // Sleep(0) goes through the same icall (its runtime path
                // yields the time slice -- identical to sleep_zero).
                return I("il2cpp::runtime::icall_thread_sleep", 1, false, false, false);
            case "Yield":
                return I("il2cpp::runtime::icall_thread_yield", 0, false, false, false);
            case "BeginCriticalRegion":
                return I("il2cpp::runtime::icall_thread_begin_critical_region", 0, false, false, false);
            case "EndCriticalRegion":
                return I("il2cpp::runtime::icall_thread_end_critical_region", 0, false, false, false);
            // ---- thr-b23: the net48 audit §4 Thread 5-gap closure ------------
            // Thread.MemoryBarrier(): a full (seq_cst) memory fence
            // (icall_thread_memory_barrier -- std::atomic_thread_fence).
            case "MemoryBarrier":
                if (count == 0)
                    return I("il2cpp::runtime::icall_thread_memory_barrier", 0, false, false, false);
                return null;
            // Thread.IsThreadPoolThread: whether the CURRENT thread is a pool
            // worker (thread-local registry, threadpool.cpp). self-ignored.
            case "get_IsThreadPoolThread":
                if (count == 0)
                    return I("il2cpp::runtime::icall_thread_get_is_thread_pool_thread", 0, true, false, true);
                return null;
            // Thread.DisableComObjectEagerCleanup(): net48 COM teardown hint --
            // no COM model, no-op (self-ignored).
            case "DisableComObjectEagerCleanup":
                if (count == 0)
                    return I("il2cpp::runtime::icall_thread_disable_com_object_eager_cleanup", 0, false, false, true);
                return null;
            // Thread.Begin/EndThreadAffinity(): CPU-affinity segment markers --
            // the runtime never migrates threads, both no-ops.
            case "BeginThreadAffinity":
                if (count == 0)
                    return I("il2cpp::runtime::icall_thread_begin_thread_affinity", 0, false, false, false);
                return null;
            case "EndThreadAffinity":
                if (count == 0)
                    return I("il2cpp::runtime::icall_thread_end_thread_affinity", 0, false, false, false);
                return null;
            case ".ctor":
                // new Thread(ThreadStart): the delegate is validated and its
                // code pointer extracted; the thread is NEW until Start.
                if (count == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_threadobj_ctor", 1, false, false, true);
                throw new NotSupportedException($"Thread ctor with {count} param(s) not wired");
            case "Start":
                if (count == 0)
                    return I("il2cpp::runtime::icall_threadobj_start", 0, false, false, true);
                return null;
            case "Join":
                if (count == 0)
                    return I("il2cpp::runtime::icall_threadobj_join", 0, false, false, true);
                // D-THR3-1 (2026-08-11): Thread.Join(int32 millisecondsTimeout)
                // -- net48 Join(int): waits until the thread terminates or the
                // timeout expires, returning bool (icall_threadobj_join_timeout,
                // threadobj.h/cpp: -1 = infinite, 0 = single probe, < -1 throws
                // ArgumentOutOfRangeException). The int32 arg rides the M-stage
                // int32 slot -- no cast needed. Join(TimeSpan) stays unwired
                // (falls to the generated-name path, like the pre-M6 convention).
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_threadobj_join_timeout", 1, true, false, true);
                return null;
            case "get_IsAlive":
                if (count == 0)
                    return I("il2cpp::runtime::icall_threadobj_get_is_alive", 0, true, false, true);
                return null;
            case "get_ManagedThreadId":
                if (count == 0)
                    return I("il2cpp::runtime::icall_threadobj_get_managed_thread_id", 0, true, false, true);
                return null;
            case "get_Priority":
                if (count == 0)
                    return I("il2cpp::runtime::icall_threadobj_get_priority", 0, true, false, true);
                return null;
            case "set_Priority":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_threadobj_set_priority", 1, false, false, true);
                return null;
            case "get_IsBackground":
                if (count == 0)
                    return I("il2cpp::runtime::icall_threadobj_get_is_background", 0, true, false, true);
                return null;
            case "set_IsBackground":
                if (count == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_threadobj_set_is_background", 1, false, false, true);
                return null;
            case "get_CurrentThread":
                if (count == 0)
                    return I("il2cpp::runtime::icall_threadobj_get_current_thread", 0, true, true, false,
                        returnTypeName: "System.Threading.Thread");
                return null;
            // P6: Thread internal helpers -- simplified model stubs.
            case "set_IsThreadPoolThread": return I("il2cpp::runtime::icall_threadobj_set_is_thread_pool_thread", 1, false, false, true, new[] { "", "i" });
            case "GetApartmentStateNative": return I("il2cpp::runtime::icall_threadobj_get_apartment_state_native", 0, true, false, true);
            case "SetApartmentStateNative": return I("il2cpp::runtime::icall_threadobj_set_apartment_state_native", 1, true, false, true, new[] { "", "i" });
            case "Interrupt": return I("il2cpp::runtime::icall_threadobj_interrupt", 0, false, false, true);
            case "get_OptimalMaxSpinWaitsPerSpinIteration": return I("il2cpp::runtime::icall_threadobj_get_optimal_max_spin_waits", 0, true, false, true);
            default: return null;
        }
    }
}
