namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ThreadPool -- System.Threading.ThreadPool icall mappings
// (threadpool.h). The WaitCallback DELEGATE unwraps in the runtime
// (il2cpp_threadpool_queue_work_item_delegate): a single-cast static-method
// delegate's methodPtr IS the generated void(*)(Il2CppObject*) callback the
// pool runs -- delegate-domain methodPtr direct call, no managed-side
// wrapper code. Null callback -> ArgumentNullException("callBack");
// multicast / instance-bound -> InvalidOperationException.
//
//   QueueUserWorkItem(WaitCallback)         -> the 1-arg runtime overload
//     (the runtime forwards state = null, net48 shape)
//   QueueUserWorkItem(WaitCallback, object) -> the 2-arg runtime overload
//   SetMaxThreads(int workerThreads, int completionPortThreads)
//     -> il2cpp_threadpool_set_max_threads (the simplified pool is
//     fixed-size: only the worker count is meaningful; the completion-port
//     count is DROPPED -- never honored, matching the fixed-pool contract)
//
// GetMaxThreads is LOUDLY rejected: its out params need a write-back the
// emitter cannot express (the runtime icall returns int32 with no out
// slots).
// Owners: System.Threading.ThreadPool
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapThreadPool(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "QueueUserWorkItem":
                // Both overloads map to the same C++ overload set (the
                // runtime distinguishes them by arity). ReturnsValue=true:
                // net48 returns bool (the runtime icall returns bool -- the
                // enqueue either succeeds or throws, so it is always true).
                return count == 1
                    ? I("il2cpp::runtime::il2cpp_threadpool_queue_work_item_delegate", 1, true, false, false,
                        new[] { "" })
                    : I("il2cpp::runtime::il2cpp_threadpool_queue_work_item_delegate", 2, true, false, false,
                        new[] { "", "" });
            case "SetMaxThreads":
                // (int workerThreads, int completionPortThreads): the pool
                // is fixed-size (see threadpool.h), so the runtime icall
                // takes the worker count only -- the completion-port count
                // is dropped.
                return I("il2cpp::runtime::il2cpp_threadpool_set_max_threads", 2, false, false, false,
                    new[] { "i", "drop" });
            case "GetMaxThreads":
                throw new NotSupportedException("System.Threading.ThreadPool::GetMaxThreads not wired: the out-param write-back (workerThreads/completionPortThreads) is not expressible in the emitter; the runtime icall il2cpp_threadpool_get_max_threads returns int32 with no out slots");
            // P6: ThreadPool internal helpers (stubs).
            case "NotifyWorkItemComplete": return I("il2cpp::runtime::icall_thread_pool_notify_work_item_complete", 0, false, false, false);
            case "ReportThreadStatus": return I("il2cpp::runtime::icall_thread_pool_report_thread_status", 1, false, false, false, new[] { "i" });
            case "NotifyWorkItemProgressNative": return I("il2cpp::runtime::icall_thread_pool_notify_work_item_progress_native", 0, false, false, false);
            case "IsThreadPoolHosted": return I("il2cpp::runtime::icall_thread_pool_is_thread_pool_hosted", 0, true, false, false);
            // netcore31/net5+ audit gap closure: ThreadPool.ThreadCount --
            // the pool's live worker count (stubs.cpp fixed-pool model).
            case "get_ThreadCount":
                return I("il2cpp::runtime::icall_thread_pool_get_thread_count", 0, true, false, false);
            // net6/net7: CanSetMin/MaxIOCompletionThreads -- the simplified
            // pool has no IOCP model; the honest constant answer is false
            // (SetMin/MaxThreads for completion ports are dropped above).
            case "CanSetMinIOCompletionThreads":
            case "CanSetMaxIOCompletionThreads":
                return I("il2cpp::runtime::icall_thread_pool_can_set_io_threads", 0, true, false, false);
            default: return null;
        }
    }
}
