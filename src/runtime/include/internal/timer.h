#pragma once
//------------------------------------------------------------------------------
// timer.h -- System.Threading.Timer / System.Timers.Timer basic domain
// (callback invoked on a runtime thread; see timer.cpp for the
// implementation).
//
// M-stage model: REAL timers on REAL threads. Each timer owns ONE dedicated
// OS thread (the simplified stand-in for the managed thread-pool: the
// callback runs on a thread other than the creator's, never on the caller's
// stack). The callback is the plain function-pointer form
// void(*)(Il2CppObject*) -- the managed TimerCallback delegate unwraps to
// this when the wiring lands in MethodRefResolver.
//
// net48 semantics (simplified where noted):
//   * create(callback, state, dueTimeMs, periodMs):
//       - dueTimeMs < -1       -> System.ArgumentOutOfRangeException.
//       - dueTimeMs == -1      -> created but NOT armed: no callback runs
//         until change() arms it (net48 Timeout.Infinite).
//       - dueTimeMs == 0       -> fires as soon as the timer thread runs.
//       - periodMs < -1        -> System.ArgumentOutOfRangeException.
//       - periodMs <= 0        -> ONE-SHOT: fires once and never again
//         (net48: -1 disables periodic signaling, 0 disables it too).
//       - periodMs > 0         -> periodic: fires every periodMs after the
//         first fire.
//   * change(timer, dueTimeMs, periodMs): re-arms the timer FROM NOW with
//     the new due/period; a timer created with dueTimeMs == -1 starts
//     firing. A one-shot timer stays armed after it fires: its worker
//     thread waits for a later change() (net48 re-schedules; thaudit4).
//   * dispose(timer): stops the timer, JOINS its thread (no callback may be
//     running when dispose returns), releases resources and frees the
//     handle. The handle is invalid after dispose; disposing twice is
//     undefined (no reference counting in the simplified model). Dispose
//     must NOT be called from the timer's own callback thread (self-join
//     would deadlock; not supported).
//   * The caller keeps `state` alive: the timer does not root it (no GC
//     rooting in the M-stage model); it must outlive dispose().
//
// Platform mapping (see timer.cpp):
//   Windows: ::CreateThread + ::WaitForSingleObject(timeout) loop +
//            auto-reset ::CreateEventW wake + ::WaitForSingleObject(INFINITE)
//            join; returns nullptr only if thread/event creation fails.
//   POSIX:   std::thread + std::condition_variable timed wait (throws
//            std::system_error if the thread cannot be created).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h" // Il2CppObject (callback state type)

//------------------------------------------------------------------------------
// System.Threading.Timer runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_Threading_Timer_class for newobj; defined in timer.cpp).
// Declared OUTSIDE the il2cpp::runtime namespace so the generated code's
// unqualified `il2cpp_object_new(&System_Threading_Timer_class)` resolves.
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Threading_Timer_class;

//------------------------------------------------------------------------------
// System.Timers.Timer runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_Timers_Timer_class for newobj; defined in timer.cpp).
// Layout: header | raw threading-timer handle | Elapsed delegate (ref) |
// AutoReset (int32) | Interval (int32 ms) | Enabled (int32) |
// Disposed (int32) | SynchronizingObject (ref, nullable).
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Timers_Timer_class;

namespace il2cpp { namespace runtime {

// Opaque timer handle (allocated by create, freed by dispose).
struct il2cpp_timer;

// Callback invoked on the timer's own thread with the state passed to
// create. The callback must not call dispose on its own timer.
using il2cpp_timer_callback = void (*)(Il2CppObject* state);

// Create + arm a timer. See the header comment for dueTimeMs/periodMs
// semantics. Returns the handle (nullptr only on Windows thread/event
// creation failure; on POSIX a creation failure throws std::system_error).
il2cpp_timer* il2cpp_timer_create(il2cpp_timer_callback callback,
                                  Il2CppObject* state,
                                  int32_t dueTimeMs,
                                  int32_t periodMs);

// Re-arm the timer from now; timers created with dueTimeMs == -1 start
// firing, and a fired one-shot can be re-armed (thaudit4).
void il2cpp_timer_change(il2cpp_timer* timer, int32_t dueTimeMs, int32_t periodMs);

// Stop the timer (joins its thread -- no callback may be running when this
// returns), release resources, free the handle. Call exactly once, never
// from the timer's own callback thread.
void il2cpp_timer_dispose(il2cpp_timer* timer);

//------------------------------------------------------------------------------
// Managed surface (batch 158, timercase): the System.Threading.Timer object
// is a runtime-registered class with ONE raw il2cpp_timer* handle slot
// (slot 1, non-reference). The ctor unwraps the TimerCallback delegate's
// methodPtr (icall_delegate_get_fn_ptr contract: single-cast static only)
// -- the extracted pointer IS the generated void(*)(Il2CppObject*) callback
// the timer thread runs. The handle does NOT root the callback state: the
// caller keeps it alive and disposes before it dies (header contract).
//------------------------------------------------------------------------------

// Timer(TimerCallback, object state, int dueTimeMs, int periodMs).
void icall_timer_ctor(Il2CppObject* self, Il2CppObject* callback, Il2CppObject* state,
                      int32_t dueTimeMs, int32_t periodMs);
// Timer.Change(int, int) -> bool (true; invalid args throw).
bool icall_timer_change(Il2CppObject* self, int32_t dueTimeMs, int32_t periodMs);
// Timer.Dispose() -- stop + join; idempotent (second call is a no-op).
void icall_timer_dispose(Il2CppObject* self);

//------------------------------------------------------------------------------
// nb1 TIM-OVL: the long/uint/TimeSpan overloads.
//   * icall_timer_ctor_64 / icall_timer_change_64 -- the (long,long) and
//     (uint,uint) overloads. net48 semantics: the values ARE MILLISECONDS
//     (the uint values ride the int64 slot as bit patterns -- the
//     transpiler's uint carrier; uint.MaxValue = -1 = Timeout.Infinite).
//     The int64 ms values are range-validated (valid [-1, INT32_MAX];
//     otherwise ArgumentOutOfRangeException -- net48's long/uint overload
//     contract) and truncated to int32 ms before arming (the previous
//     count-blind binding narrowed to int32 silently at the C++ call site).
//   * icall_timer_ctor_ts / icall_timer_change_ts -- the (TimeSpan,
//     TimeSpan) overloads. The Il2CppTimeSpan values ride RAW TICKS (the
//     "ts_ticks" emitter cast); the runtime divides by TimeSpan.
//     TicksPerMillisecond (10000) -- net48's TimeSpan.TotalMilliseconds
//     truncation -- then validates/arms. This eliminates the C0 build break
//     (cannot convert Il2CppTimeSpan to int32_t).
void icall_timer_ctor_64(Il2CppObject* self, Il2CppObject* callback, Il2CppObject* state,
                         int64_t dueTimeMs, int64_t periodMs);
bool icall_timer_change_64(Il2CppObject* self, int64_t dueTimeMs, int64_t periodMs);
void icall_timer_ctor_ts(Il2CppObject* self, Il2CppObject* callback, Il2CppObject* state,
                         int64_t dueTicks, int64_t periodTicks);
bool icall_timer_change_ts(Il2CppObject* self, int64_t dueTicks, int64_t periodTicks);

//------------------------------------------------------------------------------
// System.Timers.Timer icalls (batch 161, timerscase).
//------------------------------------------------------------------------------
// System.Timers.Timer(double interval) -- create DISARMED + store interval.
void icall_timers_ctor(Il2CppObject* self, double intervalMs);
// System.Timers.Timer() -- parameterless (default Interval 100 ms).
void icall_timers_ctor_default(Il2CppObject* self);
// set_Interval(double ms) / set_AutoReset(bool) -- store; re-arm if running.
void icall_timers_set_interval(Il2CppObject* self, double intervalMs);
void icall_timers_set_auto_reset(Il2CppObject* self, int32_t autoReset);
// add_Elapsed(ElapsedEventHandler) -- combine into the event chain.
void icall_timers_add_elapsed(Il2CppObject* self, Il2CppObject* handler);
// Start() / Stop() -- arm / disarm the threading timer.
void icall_timers_start(Il2CppObject* self);
void icall_timers_stop(Il2CppObject* self);
// Dispose() -- stop + join; idempotent.
void icall_timers_dispose(Il2CppObject* self);
// get_Interval() -- stored interval ms (double).
double icall_timers_get_interval(Il2CppObject* self);
// get_AutoReset() -- stored flag (0/1).
int32_t icall_timers_get_auto_reset(Il2CppObject* self);
// get_Enabled() -- 1 while armed (Start/set_Enabled(true)), else 0.
int32_t icall_timers_get_enabled(Il2CppObject* self);
// set_Enabled(bool) -- arm (nonzero) or disarm (zero).
void icall_timers_set_enabled(Il2CppObject* self, int32_t enabled);
// remove_Elapsed(handler) -- remove from the event chain.
void icall_timers_remove_elapsed(Il2CppObject* self, Il2CppObject* handler);
// Close() -- same as Dispose().
void icall_timers_close(Il2CppObject* self);
// get/set_SynchronizingObject() -- stored nullable ref (never invoked).
Il2CppObject* icall_timers_get_sync(Il2CppObject* self);
void icall_timers_set_sync(Il2CppObject* self, Il2CppObject* sync);

}} // namespace il2cpp::runtime
