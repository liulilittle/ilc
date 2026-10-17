//------------------------------------------------------------------------------
// sync_lite.cpp -- System.Threading lightweight synchronization objects (see
// il2cpp_synclite.h): SemaphoreSlim / CountdownEvent / SpinLock / Barrier.
//
// Every object owns a heap-allocated native state behind the Il2CppSyncObject
// header + ONE pointer slot (the wait-handle contract); the pointer is NOT a
// GC reference (refFieldBitmap 0). The states are never freed (no finalizer
// in this runtime -- the wait-handle precedent). The mutex/condition-variable
// pairs exist so the blocking forms (Semslim Wait(), Barrier SignalAndWait)
// have their eventual concurrency semantics; the nb4 probes drive them
// single-threaded, so the counting is deterministic (no wall-clock).
//------------------------------------------------------------------------------

#include "il2cpp_synclite.h"

#include "il2cpp_exception.h"     // il2cpp_throw_new (SemaphoreFullException)
#include "il2cpp_exception_ext.h" // il2cpp_throw_* helpers
#include "il2cpp_runtime.h"       // il2cpp_class_register
#include "platform.h"             // plat_thread_current_id (SpinLock owner)

#include <condition_variable>
#include <cstdint>
#include <mutex>

//------------------------------------------------------------------------------
// Class descriptors (GLOBAL scope -- the transpiler emits
// &System_Threading_*_class for newobj). All four share the Il2CppSyncObject
// layout (header + native state pointer); NO reference fields (refFieldBitmap
// 0). SpinLock's descriptor backs the VALUETYPE carrier objects the ctor
// allocates into the ref-carrier slots.
//------------------------------------------------------------------------------
#define SYNCLITE_CLASS(SYM, DOTNAME) \
    ::il2cpp::Il2CppClass SYM = {   \
        DOTNAME,                                     \
        static_cast<int32_t>(sizeof(::il2cpp::runtime::Il2CppSyncObject)), \
        0u,                                          \
        0,                                           \
        &System_Object_class,                        \
        nullptr, 0, nullptr, 0,                      \
    }

SYNCLITE_CLASS(System_Threading_SemaphoreSlim_class, "System.Threading.SemaphoreSlim");
SYNCLITE_CLASS(System_Threading_CountdownEvent_class, "System.Threading.CountdownEvent");
SYNCLITE_CLASS(System_Threading_Barrier_class, "System.Threading.Barrier");
SYNCLITE_CLASS(System_Threading_SpinLock_class, "System.Threading.SpinLock");

#undef SYNCLITE_CLASS

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// Native states.
//------------------------------------------------------------------------------
struct SemslimState
{
    std::mutex mtx;
    std::condition_variable cv;
    int32_t count;
    int32_t maxCount;
};

struct CountdownState
{
    std::mutex mtx;
    std::condition_variable cv;
    int32_t count; // 0 = set
};

struct SpinLockState
{
    int32_t locked;      // 0 = free, 1 = held
    int32_t ownerThread;
    int32_t tracking;    // enableThreadOwnerTracking (stored, unenforced)
};

struct BarrierState
{
    std::mutex mtx;
    std::condition_variable cv;
    int32_t participants;
    int32_t remaining;   // participants not yet signalled in this phase
    int32_t currentPhase;
};

namespace {

constexpr int32_t kIntMax = 0x7FFFFFFF;

Il2CppSyncObject* AsSync(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppSyncObject*>(self);
}

bool g_syncliteClassesRegistered = false;

// net48: SemaphoreSlim(maximumCount) validation -- initialCount in
// [0, maximumCount], maximumCount >= 1, else ArgumentOutOfRangeException.
void ValidateSemslimCounts(int32_t initialCount, int32_t maximumCount)
{
    if (maximumCount < 1 || initialCount < 0 || initialCount > maximumCount)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count", "The count argument is out of range.");
    }
}

} // namespace

void il2cpp_synclite_register_classes()
{
    if (g_syncliteClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Threading_SemaphoreSlim_class);
    il2cpp_class_register(&System_Threading_CountdownEvent_class);
    il2cpp_class_register(&System_Threading_Barrier_class);
    il2cpp_class_register(&System_Threading_SpinLock_class);
    g_syncliteClassesRegistered = true;
}

//------------------------------------------------------------------------------
// Construction.
//------------------------------------------------------------------------------
void icall_synclite_semslim_ctor(Il2CppObject* self, int32_t initialCount,
                                 int32_t maximumCount)
{
    il2cpp_synclite_register_classes();
    ValidateSemslimCounts(initialCount, maximumCount);
    auto* state = new SemslimState();
    state->count = initialCount;
    state->maxCount = maximumCount;
    AsSync(self)->state = state;
}

void icall_synclite_countdownevent_ctor(Il2CppObject* self, int32_t initialCount)
{
    il2cpp_synclite_register_classes();
    if (initialCount < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "initialCount", "The initial count must be non-negative.");
    }
    auto* state = new CountdownState();
    state->count = initialCount;
    AsSync(self)->state = state;
}

void icall_synclite_barrier_ctor(Il2CppObject* self, int32_t participantCount)
{
    il2cpp_synclite_register_classes();
    if (participantCount < 1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "participantCount", "The participant count must be at least one.");
    }
    auto* state = new BarrierState();
    state->participants = participantCount;
    state->remaining = participantCount;
    state->currentPhase = 0;
    AsSync(self)->state = state;
}

void icall_synclite_spinlock_ctor(Il2CppObject** slot, int32_t enableThreadOwnerTracking)
{
    il2cpp_synclite_register_classes();
    Il2CppObject* obj = il2cpp_object_new(&System_Threading_SpinLock_class);
    auto* state = new SpinLockState();
    state->locked = 0;
    state->ownerThread = 0;
    state->tracking = enableThreadOwnerTracking != 0 ? 1 : 0;
    AsSync(obj)->state = state;
    *slot = obj;
}

//------------------------------------------------------------------------------
// SemaphoreSlim.
//------------------------------------------------------------------------------
int32_t icall_synclite_semslim_get_current_count(Il2CppObject* self)
{
    auto* state = static_cast<SemslimState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    return state->count;
}

void icall_synclite_semslim_wait(Il2CppObject* self)
{
    icall_synclite_semslim_wait_ms(self, -1); // infinite
}

bool icall_synclite_semslim_wait_ms(Il2CppObject* self, int32_t millisecondsTimeout)
{
    if (millisecondsTimeout < -1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "millisecondsTimeout", "The timeout must be -1, 0 or a positive value.");
    }
    auto* state = static_cast<SemslimState*>(AsSync(self)->state);
    std::unique_lock<std::mutex> lock(state->mtx);
    if (millisecondsTimeout == 0)
    {
        if (state->count > 0)
        {
            --state->count;
            return true;
        }
        return false;
    }
    while (state->count == 0)
    {
        if (millisecondsTimeout == -1)
        {
            state->cv.wait(lock);
        }
        else
        {
            const auto now = std::chrono::steady_clock::now();
            if (state->cv.wait_until(lock, now + std::chrono::milliseconds(millisecondsTimeout)) ==
                std::cv_status::timeout)
            {
                return false;
            }
        }
    }
    --state->count;
    return true;
}

int32_t icall_synclite_semslim_release(Il2CppObject* self)
{
    auto* state = static_cast<SemslimState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    const int32_t previous = state->count;
    if (previous >= state->maxCount)
    {
        il2cpp_throw_new("System.Threading.SemaphoreFullException",
                         "Adding the given count to the semaphore would cause it to exceed its maximum count.");
    }
    state->count = previous + 1;
    state->cv.notify_one();
    return previous;
}

int32_t icall_synclite_semslim_release_n(Il2CppObject* self, int32_t releaseCount)
{
    if (releaseCount < 1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "releaseCount", "The release count must be positive.");
    }
    auto* state = static_cast<SemslimState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    if (state->count > state->maxCount - releaseCount)
    {
        il2cpp_throw_new("System.Threading.SemaphoreFullException",
                         "Adding the given count to the semaphore would cause it to exceed its maximum count.");
    }
    const int32_t previous = state->count;
    state->count += releaseCount;
    state->cv.notify_all();
    return previous;
}

//------------------------------------------------------------------------------
// CountdownEvent.
//------------------------------------------------------------------------------
bool icall_synclite_countdownevent_signal(Il2CppObject* self)
{
    auto* state = static_cast<CountdownState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    if (state->count == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "The event has already been signaled.");
    }
    --state->count;
    const bool reachedZero = (state->count == 0);
    if (reachedZero)
    {
        state->cv.notify_all();
    }
    return reachedZero;
}

int32_t icall_synclite_countdownevent_get_current_count(Il2CppObject* self)
{
    auto* state = static_cast<CountdownState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    return state->count;
}

bool icall_synclite_countdownevent_get_is_set(Il2CppObject* self)
{
    auto* state = static_cast<CountdownState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    return state->count == 0;
}

//------------------------------------------------------------------------------
// SpinLock.
//------------------------------------------------------------------------------
void icall_synclite_spinlock_enter(Il2CppObject* self, int32_t* lockTaken)
{
    if (*lockTaken != 0)
    {
        il2cpp_throw_argument_exception(
            "The lockTaken argument must be initialized to false before calling this method.");
    }
    auto* state = static_cast<SpinLockState*>(AsSync(self)->state);
    if (state->locked != 0)
    {
        *lockTaken = 0;
        return;
    }
    state->locked = 1;
    state->ownerThread = plat_thread_current_id();
    *lockTaken = 1;
}

void icall_synclite_spinlock_exit(Il2CppObject* self)
{
    auto* state = static_cast<SpinLockState*>(AsSync(self)->state);
    state->locked = 0;
    state->ownerThread = 0;
}

bool icall_synclite_spinlock_is_held(Il2CppObject* self)
{
    auto* state = static_cast<SpinLockState*>(AsSync(self)->state);
    return state->locked != 0;
}

//------------------------------------------------------------------------------
// Barrier.
//------------------------------------------------------------------------------
int32_t icall_synclite_barrier_get_participant_count(Il2CppObject* self)
{
    auto* state = static_cast<BarrierState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    return state->participants;
}

int32_t icall_synclite_barrier_get_current_phase_number(Il2CppObject* self)
{
    auto* state = static_cast<BarrierState*>(AsSync(self)->state);
    std::lock_guard<std::mutex> lock(state->mtx);
    return state->currentPhase;
}

bool icall_synclite_barrier_signal_and_wait(Il2CppObject* self)
{
    auto* state = static_cast<BarrierState*>(AsSync(self)->state);
    std::unique_lock<std::mutex> lock(state->mtx);
    --state->remaining;
    if (state->remaining > 0)
    {
        // Block until the last participant arrives (the phase advance).
        while (state->remaining > 0)
        {
            state->cv.wait(lock);
        }
        return false;
    }
    // This is the last signal: phase advance + wake.
    ++state->currentPhase;
    state->remaining = state->participants;
    state->cv.notify_all();
    return false; // no post-phase action modeled
}

}} // namespace il2cpp::runtime