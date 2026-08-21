//------------------------------------------------------------------------------
// timer.cpp -- System.Threading.Timer / System.Timers.Timer basic domain
// (see timer.h for the contract).
//
// Model: ONE dedicated OS thread per timer (the simplified stand-in for the
// managed thread-pool -- the callback runs on its own thread, never on the
// creator's stack). The thread waits on a wake signal with a timeout equal
// to the remaining time until the next fire; on timeout it invokes the
// callback, on a wake it re-reads the schedule (change/dispose).
//
// State sharing: `stopped`, `nextFireMs` and `periodMs` are atomics. Every
// change()/dispose() writes the schedule FIRST and signals SECOND, so a
// wake can never be observed without the new schedule:
//   Windows: auto-reset event -- a SetEvent without a waiter is queued and
//            consumed by the next wait, so no signal is ever lost.
//   POSIX:   a `wakeRequested` flag checked by the cv wait predicate.
// Writes happen only from the owner thread (create/change/dispose) and the
// timer thread itself (the callback + schedule advance), and the advance is
// a compare-exchange: if a change() lands mid-fire, the CAS fails and the
// fresh schedule wins, so the timer thread can never clobber a newer arm.
// Dispose joins the thread, so no callback runs after dispose returns.
//------------------------------------------------------------------------------

#include "timer.h"

#include "il2cpp_delegate_ext.h" // icall_delegate_get_fn_ptr (delegate unwrap)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_runtime.h"       // il2cpp_class_register / object layout
#include "il2cpp_object.h"

#include <atomic>
#include <chrono>
#include <cstdint>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <windows.h>
#else
    #include <condition_variable>
    #include <mutex>
    #include <thread>
#endif

namespace il2cpp { namespace runtime {

// The opaque timer handle (forward-declared in timer.h): defined here so the
// timer threads can reach the shared state.
struct il2cpp_timer
{
    il2cpp_timer_callback callback;
    Il2CppObject* state;
    std::atomic<int64_t> nextFireMs; // steady-clock ms deadline; kNotScheduled = unarmed
    std::atomic<int64_t> periodMs;   // > 0 repeat; <= 0 one-shot (net48: 0 disables period)
    std::atomic<bool> stopped;
#if defined(_WIN32)
    HANDLE threadHandle;
    HANDLE wakeEvent;
#else
    std::thread threadObj;
    std::mutex wakeMutex;
    std::condition_variable wakeCv;
    std::atomic<bool> wakeRequested;
#endif
};

namespace {

// Sentinel deadline: the timer is not armed (created/changed with -1).
constexpr int64_t kNotScheduled = INT64_MAX;

int64_t steady_ms()
{
    using namespace std::chrono;
    return duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count();
}

// Arm the schedule from NOW. dueTimeMs is validated (>= -1) by the callers.
void arm_timer(il2cpp_timer* timer, int32_t dueTimeMs, int32_t periodMs)
{
    timer->periodMs.store(periodMs);
    timer->nextFireMs.store(dueTimeMs < 0 ? kNotScheduled : steady_ms() + dueTimeMs);
}

// Signal the timer thread to re-read the schedule (change/dispose).
void wake_timer(il2cpp_timer* timer)
{
#if defined(_WIN32)
    ::SetEvent(timer->wakeEvent);
#else
    timer->wakeRequested.store(true);
    timer->wakeCv.notify_all();
#endif
}

// Invoke the callback; if the timer repeats, advance the deadline from the
// SCHEDULED time (drift-free). The advance is a compare-exchange against the
// deadline this loop just used: a change() that re-armed mid-callback makes
// the CAS fail and the fresh schedule is honored on the next iteration.
// Returns false when the timer is done (one-shot fired) or was disposed.
bool fire_and_advance(il2cpp_timer* timer)
{
    timer->callback(timer->state);
    if (timer->periodMs.load() <= 0)
    {
        return false; // one-shot: the timer is done
    }
    int64_t expected = timer->nextFireMs.load();
    if (timer->nextFireMs.compare_exchange_strong(expected, expected + timer->periodMs.load()))
    {
        // advanced from the scheduled deadline (no drift accumulation)
    }
    return !timer->stopped.load();
}

#if defined(_WIN32)
DWORD WINAPI timer_thread_proc(LPVOID param)
{
    il2cpp_timer* timer = static_cast<il2cpp_timer*>(param);
    for (;;)
    {
        if (timer->stopped.load())
        {
            break;
        }
        const int64_t next = timer->nextFireMs.load();
        if (next == kNotScheduled)
        {
            // Unarmed: wait indefinitely for a change/dispose wake.
            ::WaitForSingleObject(timer->wakeEvent, INFINITE);
            continue;
        }
        const int64_t waitMs = next - steady_ms();
        const DWORD result = ::WaitForSingleObject(
            timer->wakeEvent, static_cast<DWORD>(waitMs < 0 ? 0 : waitMs));
        if (result == WAIT_OBJECT_0)
        {
            continue; // change/dispose arrived: re-read the schedule
        }
        if (result == WAIT_TIMEOUT)
        {
            if (!fire_and_advance(timer))
            {
                break;
            }
            continue;
        }
        break; // WaitForSingleObject failed: stop the thread
    }
    return 0;
}
#else
void timer_thread_main(il2cpp_timer* timer)
{
    std::unique_lock<std::mutex> lock(timer->wakeMutex);
    for (;;)
    {
        if (timer->stopped.load())
        {
            break;
        }
        const int64_t next = timer->nextFireMs.load();
        if (next == kNotScheduled)
        {
            timer->wakeRequested.store(false);
            timer->wakeCv.wait(lock, [timer] {
                return timer->wakeRequested.load() || timer->stopped.load();
            });
            continue;
        }
        const int64_t waitMs = next - steady_ms();
        timer->wakeRequested.store(false);
        const bool signalled = timer->wakeCv.wait_for(
            lock, std::chrono::milliseconds(waitMs < 0 ? 0 : waitMs), [timer] {
                return timer->wakeRequested.load() || timer->stopped.load();
            });
        if (timer->stopped.load())
        {
            break;
        }
        if (signalled)
        {
            continue; // change arrived: re-read the schedule
        }
        if (!fire_and_advance(timer))
        {
            break;
        }
    }
}
#endif

} // namespace

il2cpp_timer* il2cpp_timer_create(il2cpp_timer_callback callback, Il2CppObject* state,
                                  int32_t dueTimeMs, int32_t periodMs)
{
    if (dueTimeMs < -1)
    {
        il2cpp_throw_argument_out_of_range_exception("dueTime", nullptr);
    }
    if (periodMs < -1)
    {
        il2cpp_throw_argument_out_of_range_exception("period", nullptr);
    }
    auto* timer = new il2cpp_timer{};
    timer->callback = callback;
    timer->state = state;
    timer->stopped.store(false);
    arm_timer(timer, dueTimeMs, periodMs);
#if defined(_WIN32)
    timer->wakeEvent = ::CreateEventW(nullptr, FALSE /* auto-reset */, FALSE, nullptr);
    timer->threadHandle = (timer->wakeEvent != nullptr)
        ? ::CreateThread(nullptr, 0, &timer_thread_proc, timer, 0, nullptr)
        : nullptr;
    if (timer->threadHandle == nullptr)
    {
        if (timer->wakeEvent != nullptr)
        {
            ::CloseHandle(timer->wakeEvent);
        }
        delete timer;
        return nullptr;
    }
#else
    // std::thread construction failure propagates as std::system_error
    // (documented in timer.h).
    timer->threadObj = std::thread(&timer_thread_main, timer);
#endif
    return timer;
}

void il2cpp_timer_change(il2cpp_timer* timer, int32_t dueTimeMs, int32_t periodMs)
{
    if (dueTimeMs < -1)
    {
        il2cpp_throw_argument_out_of_range_exception("dueTime", nullptr);
    }
    if (periodMs < -1)
    {
        il2cpp_throw_argument_out_of_range_exception("period", nullptr);
    }
    arm_timer(timer, dueTimeMs, periodMs); // schedule FIRST ...
    wake_timer(timer);                     // ... signal SECOND (never lost)
}

void il2cpp_timer_dispose(il2cpp_timer* timer)
{
    timer->stopped.store(true);
    wake_timer(timer);
#if defined(_WIN32)
    ::WaitForSingleObject(timer->threadHandle, INFINITE); // join
    ::CloseHandle(timer->threadHandle);
    ::CloseHandle(timer->wakeEvent);
#else
    timer->threadObj.join();
#endif
    delete timer;
}

//------------------------------------------------------------------------------
// Managed surface (batch 158, timercase): System.Threading.Timer.
//
// The managed Timer object is a runtime-registered class with ONE raw
// il2cpp_timer* handle slot (slot 1 -- a NON-reference slot; the runtime
// owns the timer handle, the collector never scans it). Layout:
//   slot 0: object header
//   slot 1: il2cpp_timer* handle (raw pointer, NOT a GC reference)
//
// The ctor takes the TimerCallback DELEGATE and unwraps its methodPtr
// (single-cast STATIC-method delegate only -- the exact
// icall_delegate_get_fn_ptr contract, delegatecb domain): the extracted
// pointer IS the generated void(*)(Il2CppObject*) callback the timer thread
// runs. Null delegate -> ArgumentNullException; multicast/instance-bound ->
// InvalidOperationException.
//
// GC contract: the timer handle owns the callback's target state pointer
// WITHOUT rooting it (header contract -- the caller keeps `state` alive and
// must dispose before the state dies). The delegate object itself must be
// kept alive by the caller for the timer's lifetime (the handle stores only
// the extracted fnptr, not the delegate).
//------------------------------------------------------------------------------
namespace {

il2cpp_timer* TimerHandle(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    // slot 1: the raw handle (byte offset sizeof(Il2CppObject)).
    void** slot = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(self) + sizeof(Il2CppObject));
    return reinterpret_cast<il2cpp_timer*>(*slot);
}

} // namespace

}} // namespace il2cpp::runtime

// System.Threading.Timer runtime descriptor (GLOBAL scope, see timer.h --
// the transpiler emits the unqualified &System_Threading_Timer_class).
il2cpp::Il2CppClass System_Threading_Timer_class = {
    "System.Threading.Timer",           // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject) + sizeof(void*)), // instanceSize: header + handle slot
    0u,                                 // refFieldBitmap: slot 1 is a RAW pointer, not a GC ref
    0,                                  // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

namespace il2cpp { namespace runtime {

// Timer(TimerCallback, object state, int dueTimeMs, int periodMs).
void icall_timer_ctor(Il2CppObject* self, Il2CppObject* callback, Il2CppObject* state,
                      int32_t dueTimeMs, int32_t periodMs)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    void* fn = icall_delegate_get_fn_ptr(callback); // null/multicast/instance -> loud
    auto* handle = il2cpp_timer_create(reinterpret_cast<il2cpp_timer_callback>(fn), state,
                                       dueTimeMs, periodMs);
    if (handle == nullptr)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("Timer thread creation failed.");
    }
    void** slot = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(self) + sizeof(Il2CppObject));
    *slot = handle;
}

// Timer.Change(int dueTimeMs, int periodMs) -> bool (net48 returns true when
// the change succeeded; the runtime throws on invalid args, so true).
bool icall_timer_change(Il2CppObject* self, int32_t dueTimeMs, int32_t periodMs)
{
    il2cpp_timer_change(TimerHandle(self), dueTimeMs, periodMs);
    return true;
}

// Timer.Dispose() -- stops the timer and joins its thread.
void icall_timer_dispose(Il2CppObject* self)
{
    il2cpp_timer* handle = TimerHandle(self);
    if (handle != nullptr)
    {
        il2cpp_timer_dispose(handle);
        void** slot = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(self) + sizeof(Il2CppObject));
        *slot = nullptr; // idempotent: a second Dispose is a no-op
    }
}

//------------------------------------------------------------------------------
// batch 161: System.Timers.Timer -- the Component-wrapper surface.
//
// Managed object layout (runtime-registered class):
//   slot 0: object header
//   slot 1: raw il2cpp_timer* handle (non-reference; the owning threading
//           timer -- created DISARMED at ctor, armed by Start())
//   slot 2: the Elapsed EVENT delegate (GC reference -- the multicast
//           ElapsedEventHandler chain; add_Elapsed combines into it)
//   slot 3: AutoReset flag (int32; 1 = periodic, 0 = one-shot)
//   slot 4: Interval (int32 ms; the double IL value truncates to int32 ms)
//
// The threading timer's 1-arg callback receives the TIMERS object as its
// state; the bridge (TimersBridge) reads slot 2 and invokes the multicast
// chain with the two-reference-arg void ABI (icall_multicast_invoke_rr_void
// -- `void Handler(object sender, ElapsedEventArgs e)`); the event args
// placeholder is null (the minimal surface handlers ignore `e`). The bridge
// re-arms per AutoReset: periodic when set, one-shot otherwise (the
// threading timer's period semantics: <= 0 one-shot).
//------------------------------------------------------------------------------
namespace {

constexpr size_t kHandleSlot = sizeof(Il2CppObject);
constexpr size_t kElapsedSlot = sizeof(Il2CppObject) + sizeof(void*);
constexpr size_t kAutoResetSlot = sizeof(Il2CppObject) + 2 * sizeof(void*);
constexpr size_t kIntervalSlot = sizeof(Il2CppObject) + 3 * sizeof(void*);

bool g_timersClassRegistered = false;

void EnsureTimersClassRegistered()
{
    // The GLOBAL descriptor (defined below, referenced by the transpiler's
    // &System_Timers_Timer_class) is the one registered; the ctor slot math
    // (k*Slot constants above) matches its layout.
    if (g_timersClassRegistered) return;
    il2cpp_class_register(&System_Timers_Timer_class);
    g_timersClassRegistered = true;
}

Il2CppObject* TimersElapsed(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    return *reinterpret_cast<Il2CppObject**>(
        reinterpret_cast<uint8_t*>(self) + kElapsedSlot);
}

// The threading-timer bridge: fires the Elapsed event on the timers object.
void TimersBridge(Il2CppObject* state)
{
    Il2CppObject* elapsed = TimersElapsed(state);
    if (elapsed != nullptr)
    {
        icall_multicast_invoke_rr_void(elapsed, state, nullptr); // e = null placeholder
    }
}

// Re-arm from the stored interval/autoReset slots (Start or property change
// while running). Reads the raw handle slot directly (no null check -- the
// caller guarantees a live timer).
void TimersArm(Il2CppObject* self, bool force)
{
    void** hslot = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(self) + kHandleSlot);
    il2cpp_timer* handle = reinterpret_cast<il2cpp_timer*>(*hslot);
    if (handle == nullptr) return; // disposed: no-op
    if (!force && handle->stopped.load()) return; // not running
    int32_t interval = *reinterpret_cast<int32_t*>(
        reinterpret_cast<uint8_t*>(self) + kIntervalSlot);
    int32_t autoReset = *reinterpret_cast<int32_t*>(
        reinterpret_cast<uint8_t*>(self) + kAutoResetSlot);
    if (interval <= 0) interval = 1; // net48: Interval <= 0 clamps to 1 ms
    il2cpp_timer_change(handle, interval, autoReset != 0 ? interval : -1);
}

} // namespace

}} // namespace il2cpp::runtime

// System.Timers.Timer runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_Timers_Timer_class for newobj; declared in timer.h).
il2cpp::Il2CppClass System_Timers_Timer_class = {
    "System.Timers.Timer",              // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject) + 4 * sizeof(void*)), // header + 4 slots
    0b00010u,                           // refFieldBitmap: bit 1 = the Elapsed delegate
    1,                                  // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

namespace il2cpp { namespace runtime {

// System.Timers.Timer(double interval) -- create disarmed + store interval.
void icall_timers_ctor(Il2CppObject* self, double intervalMs)
{
    EnsureTimersClassRegistered();
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    void** hslot = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(self) + kHandleSlot);
    *hslot = il2cpp_timer_create(TimersBridge, self, -1, -1); // disarmed; state = self
    if (*hslot == nullptr)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("Timer thread creation failed.");
    }
    int32_t ms = static_cast<int32_t>(intervalMs);
    *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(self) + kIntervalSlot) = ms <= 0 ? 1 : ms;
    *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(self) + kAutoResetSlot) = 1; // net48 default
}

// System.Timers.Timer() -- parameterless ctor (default Interval 100 ms).
void icall_timers_ctor_default(Il2CppObject* self)
{
    icall_timers_ctor(self, 100.0);
}

// set_Interval(double ms) -- store; re-arm if running.
void icall_timers_set_interval(Il2CppObject* self, double intervalMs)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    int32_t ms = static_cast<int32_t>(intervalMs);
    *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(self) + kIntervalSlot) = ms <= 0 ? 1 : ms;
    TimersArm(self, false);
}

// set_AutoReset(bool) -- store; re-arm if running.
void icall_timers_set_auto_reset(Il2CppObject* self, int32_t autoReset)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(self) + kAutoResetSlot) = autoReset;
    TimersArm(self, false);
}

// add_Elapsed(ElapsedEventHandler) -- combine into the event chain.
void icall_timers_add_elapsed(Il2CppObject* self, Il2CppObject* handler)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
        reinterpret_cast<uint8_t*>(self) + kElapsedSlot);
    Il2CppObject* combined = icall_delegate_combine(*slot, handler);
    *slot = combined;
    il2cpp_write_barrier(reinterpret_cast<void**>(slot), combined);
}

// Start() -- arm from the stored interval/autoReset.
void icall_timers_start(Il2CppObject* self)
{
    TimersArm(self, true);
}

// Stop() -- disarm (period -1, due -1).
void icall_timers_stop(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    void** hslot = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(self) + kHandleSlot);
    il2cpp_timer* handle = reinterpret_cast<il2cpp_timer*>(*hslot);
    if (handle != nullptr)
    {
        il2cpp_timer_change(handle, -1, -1);
    }
}

// Dispose() -- stop + join the threading timer; null the handle (idempotent).
void icall_timers_dispose(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    void** hslot = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(self) + kHandleSlot);
    il2cpp_timer* handle = reinterpret_cast<il2cpp_timer*>(*hslot);
    if (handle != nullptr)
    {
        il2cpp_timer_dispose(handle);
        *hslot = nullptr;
    }
}

}} // namespace il2cpp::runtime
