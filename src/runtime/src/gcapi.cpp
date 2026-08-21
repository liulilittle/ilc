//------------------------------------------------------------------------------
// gcapi.cpp -- System.GC icall implementations (see il2cpp_gc_api.h).
//
// The module is deliberately thin: every real behaviour delegates to the
// collector's public contract (il2cpp_gc.h). Both statistics icalls
// (icall_gc_get_total_memory, icall_gc_get_allocated_bytes) delegate to the
// collector's public accessors -- gc.cpp maintains the live-block total and
// the cumulative allocation counter behind il2cpp_gc_get_total_memory /
// il2cpp_gc_get_allocated_bytes(_for_current_thread) (see gc.cpp AllocCore).
//------------------------------------------------------------------------------

#include "il2cpp_gc_api.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// icall_gc_collect -- GC.Collect(): full collection of all generations.
// Directly delegates to the collector (il2cpp_gc.h public contract).
//------------------------------------------------------------------------------
void icall_gc_collect()
{
    il2cpp::il2cpp_gc_collect();
}

//------------------------------------------------------------------------------
// icall_gc_get_total_memory -- GC.GetTotalMemory(false).
//
// Delegates to the collector's public accessor il2cpp_gc_get_total_memory
// (gc.cpp): the sum of the sizes of all LIVE blocks in the block table.
// Snapshot semantics -- changes with every allocation and collection.
//------------------------------------------------------------------------------
int64_t icall_gc_get_total_memory()
{
    return il2cpp::il2cpp_gc_get_total_memory();
}

//------------------------------------------------------------------------------
// icall_gc_get_generation -- GC.GetGeneration(obj): 0/1/2 (small-object
// generations) or 3 (LOH). null -> ArgumentNullException (net48 contract);
// the runtime's own helper returns -1 for pointers that are no longer live
// blocks (a caller bug -- dangling pointers are UB in .NET too).
//------------------------------------------------------------------------------
int32_t icall_gc_get_generation(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp::il2cpp_throw_new("System.ArgumentNullException", "obj");
    }
    return il2cpp::il2cpp_gc_get_generation(obj);
}

//------------------------------------------------------------------------------
// icall_gc_keep_alive -- GC.KeepAlive(obj): a no-op by design.
//
// The argument is a GC root for the duration of this call (the generated
// frame-root protocol roots reference arguments), which is exactly the
// liveness .NET's GC.KeepAlive guarantees. The parameter is intentionally
// unused -- its only role is to keep the object alive at the call site.
//------------------------------------------------------------------------------
void icall_gc_keep_alive(Il2CppObject* obj)
{
    (void)obj;
}

//------------------------------------------------------------------------------
// icall_gc_suppress_finalize -- GC.SuppressFinalize(obj).
//
// net48 contract: null -> ArgumentNullException. For a non-null object the
// net48 semantic is "the object's finalizer will not run". The collector's
// finalizer table (gc.cpp) has no public unregister/suppress primitive
// (il2cpp_gc.h exposes only il2cpp_gc_register_finalizer + the run trigger),
// so true suppression requires a gc.cpp extension (a public
// il2cpp_gc_remove_finalizer or a suppress flag) -- reported to the
// orchestrator with this module's audit. Until that lands, the non-null path
// stays a no-op; note that this is net48-equivalent for System.WeakReference
// (net48 WeakReference has no finalizer, so SuppressFinalize(wr) is a no-op
// and the gcext handle-release finalizer stays armed -- the handle still
// gets released exactly as net48's internal handle table does).
//------------------------------------------------------------------------------
void icall_gc_suppress_finalize(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp::il2cpp_throw_new("System.ArgumentNullException", "obj");
    }
}

//------------------------------------------------------------------------------
// icall_gc_re_register_for_finalize -- GC.ReRegisterForFinalize(obj).
//
// net48 contract: null -> ArgumentNullException. The non-null path re-arms a
// previously suppressed finalizer -- the same suppression-primitive gap as
// icall_gc_suppress_finalize above (see that docblock); the collector's
// il2cpp_gc_register_finalizer already replaces/re-arms a callback when a
// registration exists, so this is a no-op for objects with no registration.
//------------------------------------------------------------------------------
void icall_gc_re_register_for_finalize(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp::il2cpp_throw_new("System.ArgumentNullException", "obj");
    }
}

//------------------------------------------------------------------------------
// icall_gc_get_allocated_bytes -- GC.GetAllocatedBytesForCurrentThread().
//
// Delegates to the collector's cumulative allocation counter for the current
// thread (il2cpp_gc_get_allocated_bytes_for_current_thread, gc.cpp AllocCore
// g_allocatedBytes). The collector is single-threaded, so the per-thread and
// process-wide counters coincide. Monotonic: never reset by a collection.
//------------------------------------------------------------------------------
int64_t icall_gc_get_allocated_bytes()
{
    return il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread();
}

}} // namespace il2cpp::runtime
