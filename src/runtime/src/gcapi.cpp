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

#include <atomic>

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
        il2cpp::il2cpp_throw_new("System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception", "obj");
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
        il2cpp::il2cpp_throw_new("System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception", "obj");
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
        il2cpp::il2cpp_throw_new("System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception", "obj");
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

//------------------------------------------------------------------------------
// GC latency-mode / LOH-compaction-mode preferences.
//
// The collector is a single-threaded mark-sweep with no generational tuning, so
// these settings have no behavioural effect -- they are stored preferences
// echoed back by the getters (net48 returns the last value set via
// SetGCLatencyMode / SetLOHCompactionMode). Default latency mode is
// GCLatencyMode.Interactive (1); default LOH compaction is
// GCLargeObjectHeapCompactionMode.Default (0).
//------------------------------------------------------------------------------
static int32_t g_gcLatencyMode = 1;        // GCLatencyMode.Interactive
static int32_t g_gcLohCompactionMode = 0;  // GCLargeObjectHeapCompactionMode.Default

int32_t icall_gc_get_latency_mode()
{
    return g_gcLatencyMode;
}

void icall_gc_set_latency_mode(int32_t mode)
{
    // net48 GCSettings.LatencyMode setter validates: only Batch(0)..
    // SustainedLowLatency(3) are accepted; anything else throws
    // ArgumentException (diffenv002 gc.latency.bad). Mirrors the gc.cpp
    // range (kGcLatencyModeBatch..kGcLatencyModeSustainedLowLatency).
    if (mode < 0 || mode > 3)
    {
        il2cpp::il2cpp_throw_new(
            "System.ArgumentException,System.ArgumentException,"
            "System.SystemException,System.Exception",
            "Value was invalid.");
    }
    g_gcLatencyMode = mode;
}

int32_t icall_gc_get_loh_compaction_mode()
{
    return g_gcLohCompactionMode;
}

void icall_gc_set_loh_compaction_mode(int32_t mode)
{
    g_gcLohCompactionMode = mode;
}

int32_t icall_gc_is_server_gc()
{
    return 0; // workstation collector model
}

//------------------------------------------------------------------------------
// nb1 GC-LEAK: GC.AddMemoryPressure / GC.RemoveMemoryPressure -- no-ops.
// net48 calls these to inform the CLR of native memory allocations so the
// GC can trigger collections under memory pressure. The simplified collector
// (gc.cpp) has no pressure-based trigger; the count is accepted and dropped
// (the managed surface never observes these -- both return void).
//------------------------------------------------------------------------------
void icall_gc_add_memory_pressure(int64_t bytesAllocated)
{
    (void)bytesAllocated;
}

void icall_gc_remove_memory_pressure(int64_t bytesAllocated)
{
    (void)bytesAllocated;
}

// P6: GCFrameRegistration internal helpers (stubs).

void icall_gc_register_for_gc_reporting()
{
    // no-op: GC frame registration not needed in simplified model
}

void icall_gc_unregister_for_gc_reporting()
{
    // no-op: GC frame registration not needed in simplified model
}

//------------------------------------------------------------------------------
// corelib-p0: the CoreLib-INTERNAL GC QCall bodies (see il2cpp_gc_api.h).
//
// These back the members the shipped System.Private.CoreLib 8.0.31/9.0.20
// declares as QCall bodies behind the public GC API. The collector has no NoGC
// region, no frozen heap and no tunable configuration, so each one reports the
// status that is TRUE for this collector rather than inventing state -- the
// public wrappers then take exactly the branch .NET takes in that situation.
//------------------------------------------------------------------------------

// EndNoGCRegionStatus.NotInProgress -- a region can never be in progress.
int32_t icall_gc_end_no_gc_region()
{
    return 1;
}

// EnableNoGCRegionCallbackStatus.NotStarted -- registering a callback requires
// an in-progress NoGC region, which this collector never grants.
int32_t icall_gc_enable_no_gc_region_callback(void* workItem, int64_t totalSize)
{
    (void)workItem;
    (void)totalSize;
    return 1;
}

// No configuration values exist, so the callback is never invoked.
void icall_gc_enumerate_configuration_values(void* configurationDictionary, void* callback)
{
    (void)configurationDictionary;
    (void)callback;
}

//------------------------------------------------------------------------------
// Frozen segments: this collector has no frozen (read-only, non-moving) heap,
// so a registered segment cannot be adopted. The handle is an opaque token
// that _UnregisterFrozenSegment accepts; nothing else observes it.
//
// The token is deliberately NOT derived from the address: two registrations of
// the same segment must not alias (the caller owns the segment lifetime), and
// a handle value of 0 is never minted (0 is the "no handle" / default IntPtr).
//------------------------------------------------------------------------------
namespace {

std::atomic<intptr_t> g_frozenSegmentToken{0};

} // namespace

intptr_t icall_gc_register_frozen_segment(intptr_t sectionAddress, intptr_t sectionSize)
{
    (void)sectionAddress;
    (void)sectionSize;
    return g_frozenSegmentToken.fetch_add(1) + 1;
}

void icall_gc_unregister_frozen_segment(intptr_t segmentHandle)
{
    // The token is opaque and carries no resources: there is no frozen heap to
    // detach, so unregistering is only the contract that the handle stops
    // being used. A default (0) handle is ignored like any unknown token.
    (void)segmentHandle;
}

//------------------------------------------------------------------------------
// corelib-p0: the PUBLIC GC wrappers (see il2cpp_gc_api.h). A net8/net9
// caller binds these directly -- the wrapper IL is CoreLib's and is never
// transpiled -- so they re-state the same truth as the internal arms.
//------------------------------------------------------------------------------

Il2CppTimeSpan icall_gc_get_total_pause_duration()
{
    // GC.GetTotalPauseDuration() == new TimeSpan(_GetTotalPauseDuration()):
    // the raw 100ns ticks ride straight into the TimeSpan value.
    return Il2CppTimeSpan(il2cpp_gc_get_total_pause_ticks());
}

void icall_gc_end_no_gc_region_public()
{
    // Measured on the shipped 8.0.31 runtime: EndNoGCRegion outside a region
    // throws InvalidOperationException with this exact message.
    il2cpp_throw_new("System.InvalidOperationException,System.SystemException,System.Exception",
                     "NoGCRegion mode must be set.");
}

void icall_gc_register_no_gc_region_callback_public(int64_t totalSize, Il2CppObject* callback)
{
    // Measured on the shipped 8.0.31 runtime: RegisterNoGCRegionCallback
    // outside a region throws InvalidOperationException with the SAME message
    // as EndNoGCRegion (the region check precedes any argument validation --
    // probed with a non-null callback, and .NET's own code path raises the
    // region error first).
    (void)callback;
    (void)totalSize;
    il2cpp_throw_new("System.InvalidOperationException,System.SystemException,System.Exception",
                     "NoGCRegion mode must be set.");
}

void icall_gc_refresh_memory_limit()
{
    // Fixed-budget collector: there is no heap hard limit to recompute, so the
    // call completes without changing anything (measured: normal return).
}

int32_t icall_gc_refresh_memory_limit_internal()
{
    // RefreshMemoryStatus.Succeeded. The hard-limit struct the caller passed is
    // not even on the ABI here: the resolver drops it at the call site, because
    // a fixed-budget collector has nothing to configure from it.
    return 0;
}

}} // namespace il2cpp::runtime
