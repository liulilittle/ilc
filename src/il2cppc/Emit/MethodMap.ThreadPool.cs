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
            default: return null;
        }
    }
}
