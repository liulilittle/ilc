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
    if (state->kind == kKindMutex)
    {
        // Mutex: acquire directly (blocks until free) and HOLD the mutex --
        // the unique_lock path below would release it on function return.
        // The mutex stays owned until icall_waithandle_mutex_release unlocks
        // it. A re-entrant WaitOne on the same thread would deadlock (net48
        // Mutex is re-entrant, but the simplified runtime mutex is not --
        // documented divergence).
        state->mtx.lock();
        return true;
    }
    std::unique_lock<std::mutex> lock(state->mtx);
    if (state->kind == kKindSemaphore)
    {
        state->cv.wait(lock, [&] { return state->count > 0; });
        --state->count;
    }
    else // event
    {
        state->cv.wait(lock, [&] { return state->count != 0; });
        if (state->mode == 0) // auto-reset: clear the signal on a successful wait
        {
            state->count = 0;
        }
    }
    return true;
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
    state->mtx.unlock();
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
