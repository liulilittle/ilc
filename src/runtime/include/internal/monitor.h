#pragma once
//------------------------------------------------------------------------------
// monitor.h -- System.Threading.Monitor (lock semantics) icall contract
// (see monitor.cpp for the implementation).
//
// Managed object header note: Il2CppObject (il2cpp_object.h) carries ONLY the
// klass pointer -- there is no sync block / header bit for locking state (the
// M-stage layout is stable and must not change). The monitor state therefore
// lives in a GLOBAL SIDE TABLE inside the runtime: a growable pointer table of
// entries keyed by Il2CppObject* (see monitor.cpp for the table mechanics).
//
// Semantics (net48-aligned, simplified for the M-stage):
//
//   * il2cpp_monitor_enter   -- acquire the object's monitor. Reentrant:
//     a thread that already owns the monitor re-enters with a recursion
//     counter (every Enter must be matched by an Exit). A nullptr object
//     throws System.ArgumentNullException. The optional bool* lockTaken
//     is set to true whenever the monitor was acquired (fresh or
//     reentrant) and left untouched when enter throws. When lockTaken
//     is ALREADY true on entry, System.ArgumentException (parameter
//     "lockTaken") is thrown BEFORE the null check -- the exact net48
//     Monitor.Enter(object, ref bool) contract (probe-verified: net48
//     Enter(null, ref true) -> ArgumentException, not ArgumentNullException).
//   * il2cpp_monitor_exit    -- release one recursion level; the monitor is
//     released when the recursion counter reaches zero. Calling Exit when
//     the calling thread does NOT own the monitor throws
//     System.SynchronizationLockException.
//   * il2cpp_monitor_try_enter -- non-blocking acquire: returns true when
//     the monitor was acquired (or was already owned -- reentrant, recursion
//     incremented) and false when another thread holds it. The 2-arg
//     (object, bool* lockTaken) form writes the flag with the result and,
//     like Enter, throws ArgumentException on an already-true flag before
//     the null check.
//   * il2cpp_monitor_pulse   -- signal WAITING threads: increments the
//     pulse generation when at least one thread is inside il2cpp_monitor_wait
//     (a no-op when no thread waits, like .NET). Requires ownership, else
//     System.SynchronizationLockException. SIMPLIFIED: one Pulse wakes ALL
//     current waiters (net48 wakes a single FIFO waiter; PulseAll semantics
//     are not modeled separately).
//   * il2cpp_monitor_wait    -- atomically release the monitor (ALL recursion
//     levels), block until a Pulse arrives or the timeout expires, then
//     reacquire the monitor (restoring the saved recursion depth) and return
//     true when a Pulse woke the thread, false on timeout. The 1-arg form
//     waits forever (Timeout.Infinite). A timeout of exactly -1 also waits
//     forever; 0 polls once without blocking; a timeout < -1 throws
//     ArgumentOutOfRangeException (param "millisecondsTimeout") BEFORE the
//     ownership check (net48 Wait(object, int) validation order). Requires
//     ownership, else System.SynchronizationLockException.
//
// Wait/pulse are implemented by POLLING (the wait loop re-checks a pulse
// generation counter every few milliseconds while the caller's monitor is
// released) instead of native condition variables -- correct and portable,
// not optimal; the pulse-detection latency is bounded by the poll interval.
//
// Thread identity uses the same mechanism as thread.cpp (GetCurrentThreadId
// on Windows, SYS_gettid on Linux).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// Monitor.Enter(object obj) -> void. Blocks until the monitor is acquired;
// reentrant (recursion counter). nullptr throws ArgumentNullException.
// The optional bool* lockTaken writes *lockTaken = true whenever the monitor
// was acquired (fresh acquire OR reentrant recursion increment) and leaves
// it untouched when enter throws -- the exact Monitor.Enter(object, ref bool)
// lockTaken contract that Roslyn lock lowering relies on (MethodMap.Monitor
// emits the flag's ADDRESS through this parameter). An ALREADY-true flag
// throws System.ArgumentException (parameter "lockTaken") before the null
// check (net48 probe-verified ordering). The default nullptr keeps the 1-arg
// form call-compatible.
void il2cpp_monitor_enter(Il2CppObject* obj, bool* lockTaken = nullptr);

// Monitor.Exit(object obj) -> void. Releases one recursion level; the
// monitor is released at zero. Not owning the monitor (or a nullptr) throws
// (SynchronizationLockException / ArgumentNullException).
void il2cpp_monitor_exit(Il2CppObject* obj);

// Monitor.TryEnter(object obj) -> bool. Non-blocking acquire; true when the
// monitor was acquired (or already owned by the caller, recursion
// incremented), false when another thread holds it. nullptr throws
// ArgumentNullException.
bool il2cpp_monitor_try_enter(Il2CppObject* obj);

// Monitor.TryEnter(object obj, ref bool lockTaken) -> bool. The net48
// 2-arg TryEnter: the flag is written with the acquire result (true on
// acquired/reentrant, false when contended), and an ALREADY-true flag
// throws System.ArgumentException (parameter "lockTaken") BEFORE the null
// check -- matching Monitor.Enter's lockTaken contract. The flag is left
// untouched on the throw path.
bool il2cpp_monitor_try_enter(Il2CppObject* obj, bool* lockTaken);

// Monitor.Pulse(object obj) -> void. Wakes the threads waiting in
// il2cpp_monitor_wait (no-op when none wait). Requires ownership. SIMPLIFIED:
// wakes ALL waiters (net48 wakes one FIFO waiter).
void il2cpp_monitor_pulse(Il2CppObject* obj);

// Monitor.Wait(object obj) -> bool. The 1-arg net48 form: an infinite wait
// (Timeout.Infinite), equivalent to il2cpp_monitor_wait(obj, -1). Requires
// ownership; returns true when pulsed.
bool il2cpp_monitor_wait(Il2CppObject* obj);

// Monitor.Wait(object obj, int32 timeoutMs) -> bool. Releases the monitor
// (all recursion levels), waits for a Pulse or the timeout, reacquires the
// monitor (restoring the saved recursion depth), and returns true when
// pulsed, false on timeout. timeoutMs == -1 waits forever, 0 polls once,
// positive waits up to that many milliseconds. A timeout < -1 throws
// ArgumentOutOfRangeException (net48 "millisecondsTimeout" parameter)
// BEFORE the ownership check -- the net48 Wait(object, int) validation
// order (probe: Wait(unowned, -2) -> AORE, Wait(unowned, 0) -> SyncLock).
// Requires ownership (checked after the timeout validation).
bool il2cpp_monitor_wait(Il2CppObject* obj, int32_t timeoutMs);

// Monitor.TryEnter(object obj, int32 timeoutMs) -> bool. The net48 timeout
// form: acquires immediately when free, otherwise BLOCKS up to timeoutMs
// (re-entrant like the 1-arg form). timeoutMs == 0 is a single non-blocking
// try (net48: TryEnter(obj, 0) returns immediately); -1 (Timeout.Infinite)
// blocks until acquired; a timeout < -1 throws ArgumentOutOfRangeException
// (net48: the int overload throws with NO parameter name). nullptr throws
// ArgumentNullException BEFORE the timeout validation (net48 ordering).
bool il2cpp_monitor_try_enter(Il2CppObject* obj, int32_t timeoutMs);

// Monitor.TryEnter(object obj, int32 timeoutMs, ref bool lockTaken) -> bool.
// The net48 3-arg form: writes *lockTaken with the acquire result and
// returns the same value. Check ordering matches net48:
//   1. lockTaken already true -> ArgumentException (BEFORE null and timeout)
//   2. null obj -> ArgumentNullException (before timeout validation)
//   3. timeout < -1 -> ArgumentOutOfRangeException (no parameter name)
// The flag is left untouched on every throw path.
bool il2cpp_monitor_try_enter(Il2CppObject* obj, int32_t timeoutMs, bool* lockTaken);

}} // namespace il2cpp::runtime
