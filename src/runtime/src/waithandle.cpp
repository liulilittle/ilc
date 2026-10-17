//------------------------------------------------------------------------------
// waithandle.cpp -- System.Threading wait-handle runtime support (see
// il2cpp_waithandle.h): Semaphore / Mutex / EventWaitHandle.
//
// Each wait handle is a managed object (Il2CppWaitHandleObject -- header +
// ONE native state pointer). The native state (WaitHandleState) holds a
// std::mutex + std::condition_variable + a count + kind/mode; it is NOT a GC
// reference (refFieldBitmap 0 -- the collector never scans past the header).
// The state is heap-allocated by the ctor icalls and never freed (no
// finalizer in this runtime -- the stream-handle registry precedent).
//
// The three class descriptors are GLOBAL (the transpiler emits
// &System_Threading_*_class for newobj/isinst/castclass -- the
// System_Version_class contract).
//------------------------------------------------------------------------------

#include "il2cpp_waithandle.h"

#include "il2cpp_exception.h"     // il2cpp_throw_new
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_runtime.h"       // il2cpp_class_register
#include "platform.h"             // plat_thread_current_id (OS thread id)

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <mutex>

//------------------------------------------------------------------------------
// Class descriptors (GLOBAL scope -- the transpiler emits
// &System_Threading_*_class for newobj/isinst/castclass). All three share the
// Il2CppWaitHandleObject layout (header + native state pointer); NO reference
// fields, so the collector never scans past the header (refFieldBitmap 0).
//------------------------------------------------------------------------------
#define WAITHANDLE_CLASS(SYM, DOTNAME) \
    ::il2cpp::Il2CppClass SYM = {   \
        DOTNAME,                                     \
        static_cast<int32_t>(sizeof(::il2cpp::runtime::Il2CppWaitHandleObject)), \
        0u,                                          \
        0,                                           \
        &System_Object_class,                        \
        nullptr, 0, nullptr, 0,                      \
    }

WAITHANDLE_CLASS(System_Threading_Semaphore_class, "System.Threading.Semaphore");
WAITHANDLE_CLASS(System_Threading_Mutex_class, "System.Threading.Mutex");
WAITHANDLE_CLASS(System_Threading_EventWaitHandle_class, "System.Threading.EventWaitHandle");
// nb3 SYNC-GAP: the two concrete DERIVED events the BCL commonly constructs
// (sealed subclasses of EventWaitHandle). Identical object layout (header +
// native state pointer); only the ctor differs (the reset mode is fixed).
WAITHANDLE_CLASS(System_Threading_AutoResetEvent_class, "System.Threading.AutoResetEvent");
WAITHANDLE_CLASS(System_Threading_ManualResetEvent_class, "System.Threading.ManualResetEvent");

#undef WAITHANDLE_CLASS

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// Native state.
//------------------------------------------------------------------------------
struct WaitHandleState
{
    std::mutex mtx;
    std::condition_variable cv;
    int32_t count;    // semaphore count / event signaled flag (1/0)
    int32_t kind;     // 0 = semaphore, 1 = mutex, 2 = event
    int32_t maxCount; // semaphore maximum (0 for mutex/event)
    int32_t mode;     // event: 0 = auto-reset, 1 = manual-reset

    int32_t mutexOwner;      // mutex: owning thread id (0 = free)
    int32_t mutexRecursion;  // mutex: reentrancy depth
};

namespace {

constexpr int32_t kKindSemaphore = 0;
constexpr int32_t kKindMutex = 1;
constexpr int32_t kKindEvent = 2;

Il2CppWaitHandleObject* AsWaitHandle(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppWaitHandleObject*>(self);
}

bool g_waithandleClassesRegistered = false;

} // namespace

void il2cpp_waithandle_register_classes()
{
    if (g_waithandleClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Threading_Semaphore_class);
    il2cpp_class_register(&System_Threading_Mutex_class);
    il2cpp_class_register(&System_Threading_EventWaitHandle_class);
    il2cpp_class_register(&System_Threading_AutoResetEvent_class);
    il2cpp_class_register(&System_Threading_ManualResetEvent_class);
    g_waithandleClassesRegistered = true;
}

//------------------------------------------------------------------------------
// Construction.
//------------------------------------------------------------------------------
void icall_waithandle_semaphore_ctor(Il2CppObject* self, int32_t initialCount,
                                     int32_t maximumCount)
{
    il2cpp_waithandle_register_classes();
    if (maximumCount < 1 || initialCount < 0 || initialCount > maximumCount)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "initialCount", "The initial count must be between 0 and the maximum count.");
    }
    auto* state = new WaitHandleState();
    state->count = initialCount;
    state->kind = kKindSemaphore;
    state->maxCount = maximumCount;
    AsWaitHandle(self)->state = state;
}

void icall_waithandle_mutex_ctor(Il2CppObject* self)
{
    il2cpp_waithandle_register_classes();
    auto* state = new WaitHandleState();
    state->count = 0;
    state->kind = kKindMutex;
    state->mutexOwner = 0;
    state->mutexRecursion = 0;
    AsWaitHandle(self)->state = state;
}

void icall_waithandle_event_ctor(Il2CppObject* self, bool initialState,
                                 int32_t mode)
{
    il2cpp_waithandle_register_classes();
    if (mode != 0 && mode != 1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "mode", "The event reset mode must be AutoReset or ManualReset.");
    }
    auto* state = new WaitHandleState();
    state->count = initialState ? 1 : 0;
    state->kind = kKindEvent;
    state->mode = mode;
    AsWaitHandle(self)->state = state;
}

// nb3 SYNC-GAP: AutoResetEvent(bool) / ManualResetEvent(bool) -- the sealed
// EventWaitHandle subclasses fix the reset mode at construction (0 =
// AutoReset, 1 = ManualReset; net48 EventResetMode enum). Same shared native
// state and event icall family; only the mode differs from the public
// EventWaitHandle(bool, EventResetMode) ctor.
void icall_waithandle_autoresetevent_ctor(Il2CppObject* self, bool initialState)
{
    il2cpp_waithandle_register_classes();
    auto* state = new WaitHandleState();
    state->count = initialState ? 1 : 0;
    state->kind = kKindEvent;
    state->mode = 0;
    AsWaitHandle(self)->state = state;
}

void icall_waithandle_manualresetevent_ctor(Il2CppObject* self, bool initialState)
{
    il2cpp_waithandle_register_classes();
    auto* state = new WaitHandleState();
    state->count = initialState ? 1 : 0;
    state->kind = kKindEvent;
    state->mode = 1;
    AsWaitHandle(self)->state = state;
}

//------------------------------------------------------------------------------
// WaitOne.
//------------------------------------------------------------------------------
bool icall_waithandle_wait_one(Il2CppObject* self)
{
    WaitHandleState* state = AsWaitHandle(self)->state;
    if (state == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The wait handle is not initialized.");
    }
    std::unique_lock<std::mutex> lock(state->mtx);
    if (state->kind == kKindMutex)
    {
        const int32_t me = plat_thread_current_id();
        if (state->mutexOwner == me)
        {
            ++state->mutexRecursion;
            return true;
        }
        state->cv.wait(lock, [&] { return state->mutexOwner == 0; });
        state->mutexOwner = me;
        state->mutexRecursion = 1;
        return true;
    }
    if (state->kind == kKindSemaphore)
    {
        state->cv.wait(lock, [&] { return state->count > 0; });
        --state->count;
    }
    else // event
    {
        state->cv.wait(lock, [&] { return state->count != 0; });
        if (state->mode == 0)
        {
            state->count = 0;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// WaitOne(int millisecondsTimeout) -- net48 timeout form (thaudit2; the
// mapping at MethodMap.WaitHandle.cs:63 previously pointed at a definition
// that did not exist). -1 = Timeout.Infinite (block); 0 = single
// non-blocking probe; > 0 = bounded wait; < -1 = ArgumentOutOfRangeException.
//------------------------------------------------------------------------------
bool icall_waithandle_wait_one_timeout(Il2CppObject* self, int32_t millisecondsTimeout)
{
    WaitHandleState* state = AsWaitHandle(self)->state;
    if (state == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The wait handle is not initialized.");
    }
    if (millisecondsTimeout < -1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "millisecondsTimeout", "The timeout must be -1 (infinite) or non-negative.");
    }
    std::unique_lock<std::mutex> lock(state->mtx);
    const bool infinite = millisecondsTimeout == -1;
    const std::chrono::milliseconds timeout(millisecondsTimeout < 0 ? 0 : millisecondsTimeout);
    if (state->kind == kKindMutex)
    {
        const int32_t me = plat_thread_current_id();
        if (state->mutexOwner == me)
        {
            ++state->mutexRecursion;
            return true;
        }
        const bool acquired = infinite
            ? (state->cv.wait(lock, [&] { return state->mutexOwner == 0; }), true)
            : state->cv.wait_for(lock, timeout, [&] { return state->mutexOwner == 0; });
        if (!acquired)
        {
            return false;
        }
        state->mutexOwner = me;
        state->mutexRecursion = 1;
        return true;
    }
    if (state->kind == kKindSemaphore)
    {
        const bool signalled = infinite
            ? (state->cv.wait(lock, [&] { return state->count > 0; }), true)
            : state->cv.wait_for(lock, timeout, [&] { return state->count > 0; });
        if (signalled)
        {
            --state->count;
        }
        return signalled;
    }
    // event
    const bool signalled = infinite
        ? (state->cv.wait(lock, [&] { return state->count != 0; }), true)
        : state->cv.wait_for(lock, timeout, [&] { return state->count != 0; });
    if (signalled && state->mode == 0)
    {
        state->count = 0;
    }
    return signalled;
}

// nb3 SYNC-GAP: WaitOne(TimeSpan) -- the net48 TimeSpan timeout overload
// (previously a transpiler fold-fail: only the int-milliseconds form
// existed, so TimeSpan waits never resolved). Mirrors the monitor
// wait_timespan conversion (il2cpp_monitor_wait_timespan): TotalMilliseconds
// truncates toward zero ((long)(ticks / 10000.0)); Timeout.InfiniteTimeSpan
// (-10000 ticks) converts to exactly -1. Outside [-1, INT32_MAX] ->
// ArgumentOutOfRangeException ("timeout", net48 parameter name).
bool icall_waithandle_wait_one_timespan(Il2CppObject* self, int64_t ticks)
{
    const int64_t ms = ticks / 10000LL;
    if (ms < -1LL || ms > 2147483647LL)
    {
        il2cpp_throw_argument_out_of_range_exception("timeout", nullptr);
    }
    return icall_waithandle_wait_one_timeout(self, static_cast<int32_t>(ms));
}

//------------------------------------------------------------------------------
// Semaphore release.
//------------------------------------------------------------------------------
int32_t icall_waithandle_semaphore_release(Il2CppObject* self)
{
    return icall_waithandle_semaphore_release_n(self, 1);
}

int32_t icall_waithandle_semaphore_release_n(Il2CppObject* self, int32_t releaseCount)
{
    WaitHandleState* state = AsWaitHandle(self)->state;
    if (state == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The wait handle is not initialized.");
    }
    if (releaseCount < 1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "releaseCount", "The release count must be greater than zero.");
    }
    std::lock_guard<std::mutex> lock(state->mtx);
    const int32_t previous = state->count;
    if (previous + releaseCount > state->maxCount)
    {
        il2cpp_throw_new("System.Threading.SemaphoreFullException",
                         "Adding the specified count to the semaphore would cause it to exceed its maximum count.");
    }
    state->count = previous + releaseCount;
    state->cv.notify_all();
    return previous;
}

//------------------------------------------------------------------------------
// Mutex release.
//------------------------------------------------------------------------------
void icall_waithandle_mutex_release(Il2CppObject* self)
{
    WaitHandleState* state = AsWaitHandle(self)->state;
    if (state == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The wait handle is not initialized.");
    }
    std::unique_lock<std::mutex> lock(state->mtx);
    const int32_t me = plat_thread_current_id();
    if (state->mutexOwner != me)
    {
        // net48 ReleaseMutex ownership violation (same carrier as Monitor.Exit).
        il2cpp_throw_new("System.ApplicationException,System.Exception",
                         "Object synchronization method was called from an unsynchronized block of code.");
    }
    if (--state->mutexRecursion == 0)
    {
        state->mutexOwner = 0;
        state->cv.notify_one();
    }
}

//------------------------------------------------------------------------------
// Event set / reset.
//------------------------------------------------------------------------------
void icall_waithandle_event_set(Il2CppObject* self)
{
    WaitHandleState* state = AsWaitHandle(self)->state;
    if (state == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The wait handle is not initialized.");
    }
    {
        std::lock_guard<std::mutex> lock(state->mtx);
        state->count = 1;
    }
    state->cv.notify_all();
}

void icall_waithandle_event_reset(Il2CppObject* self)
{
    WaitHandleState* state = AsWaitHandle(self)->state;
    if (state == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The wait handle is not initialized.");
    }
    std::lock_guard<std::mutex> lock(state->mtx);
    state->count = 0;
}

}} // namespace il2cpp::runtime
