#pragma once
//------------------------------------------------------------------------------
// il2cpp_synclite.h -- System.Threading lightweight synchronization objects
// (sync_lite.cpp): SemaphoreSlim / CountdownEvent / SpinLock / Barrier.
//
// OBJECT MODEL: SemaphoreSlim / CountdownEvent / Barrier are managed objects
// (Il2CppSyncObject -- header + ONE native state pointer, the wait-handle
// contract) allocated through their GLOBAL-scope class descriptors
// (System_Threading_SemaphoreSlim_class / CountdownEvent_class / Barrier_class
// -- defined in sync_lite.cpp, the System_Threading_Semaphore_class
// contract; the transpiler's newobj fallback emits
// il2cpp_object_new(&System_Threading_*_class)). The native state is NOT a
// GC reference (refFieldBitmap 0). SpinLock is a VALUETYPE: the transpiler's
// CodeGenerator declares the `SpinLock` local as an Il2CppObject* carrier
// (the AsyncTaskMethodBuilder "ref carrier" model), the .ctor icall allocates
// a System_Threading_SpinLock_class object into the slot, and the
// Enter/Exit/IsHeld icalls deref:r it. Every native state is heap-allocated
// by the ctor icalls and never freed (the wait-handle/stream-handle
// precedent).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48):
//   * SemaphoreSlim(initialCount[, maxCount]): the counting semaphore.
//     Wait() decrements (blocking while 0); Wait(int)/Wait(TimeSpan) probe
//     with a timeout returning bool; Release()/Release(int) increment
//     (bounded by maxCount; exceeding throws SemaphoreFullException) and
//     return the PREVIOUS count; CurrentCount is the live count.
//   * CountdownEvent(initialCount): Signal() decrements (returns true when
//     the count reaches 0); CurrentCount / IsSet observe the remaining count
//     / reached-zero state.
//   * SpinLock(enableThreadOwnerTracking): Enter(ref bool) acquires (the
//     flag is written true on success; false on failure/exception), Exit()
//     releases, IsHeld reports the locked state. Tracking is accepted but the
//     ownership assertions are not enforced (simplified single-threaded
//     protocol surface). The ctor allocates the state into the value-slot
//     carrier (Il2CppObject** slot).
//   * Barrier(participantCount): SignalAndWait() blocks until every
//     participant signalled, then advances CurrentPhaseNumber by 1 and
//     returns whether a post-phase action ran (always false: none are
//     modeled); ParticipantCount is the configured size.
//
// The runtime is SINGLE-THREADED for managed allocation/GC, so the state
// mutexes are uncontended in the deterministic probes (the wait-handle
// precedent); the condition variables are still there for the blocking
// forms' future concurrency use.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

#include <cstdint>

// System.Threading lightweight-sync runtime descriptors (GLOBAL scope -- the
// transpiler emits &System_Threading_*_class for newobj; defined in
// sync_lite.cpp). SpinLock is a VALUE type but its carrier objects still need
// a descriptor for the ctor allocation.
extern il2cpp::Il2CppClass System_Threading_SemaphoreSlim_class;
extern il2cpp::Il2CppClass System_Threading_CountdownEvent_class;
extern il2cpp::Il2CppClass System_Threading_Barrier_class;
extern il2cpp::Il2CppClass System_Threading_SpinLock_class;

namespace il2cpp { namespace runtime {

// Managed layout for the three reference sync objects + the SpinLock carrier:
// object header + ONE native state pointer. The pointer is NOT a GC reference
// (refFieldBitmap 0 -- the collector never scans past the header).
struct Il2CppSyncObject
{
    Il2CppObject obj;       // object header (klass pointer, offset 0)
    void* state;            // native state (offset 8) -- not a GC reference
};

// Register the lightweight-sync descriptors once (idempotent --
// il2cpp_class_register dedupes by pointer). Called defensively by the ctor
// icalls before the first allocation.
void il2cpp_synclite_register_classes();

// --- construction -----------------------------------------------------------

// SemaphoreSlim(int initialCount) / SemaphoreSlim(int initialCount,
// int maximumCount): initialCount in [0, maximumCount], maximumCount >= 1
// (the single-arg form uses int.MaxValue) -- otherwise
// ArgumentOutOfRangeException. Allocates the native state.
void icall_synclite_semslim_ctor(Il2CppObject* self, int32_t initialCount,
                                 int32_t maximumCount);

// CountdownEvent(int initialCount): initialCount >= 0, else
// ArgumentOutOfRangeException. Allocates the native state.
void icall_synclite_countdownevent_ctor(Il2CppObject* self, int32_t initialCount);

// Barrier(int participantCount): participantCount >= 1, else
// ArgumentOutOfRangeException. Allocates the native state.
void icall_synclite_barrier_ctor(Il2CppObject* self, int32_t participantCount);

// SpinLock(bool enableThreadOwnerTracking): allocates the state OBJECT into
// the value-slot CARRIER (the VALUETYPE local is an Il2CppObject* slot, so
// the ctor receives its address Il2CppObject**). The tracking flag is stored
// but the ownership assertions stay unenforced (simplified protocol).
void icall_synclite_spinlock_ctor(Il2CppObject** slot, int32_t enableThreadOwnerTracking);

// --- SemaphoreSlim -----------------------------------------------------------

// int CurrentCount: the live count.
int32_t icall_synclite_semslim_get_current_count(Il2CppObject* self);

// void Wait(): blocks until the count is > 0, then decrements.
void icall_synclite_semslim_wait(Il2CppObject* self);

// bool Wait(int millisecondsTimeout): 0 = single non-blocking probe;
// -1 = infinite; < -1 throws ArgumentOutOfRangeException. Returns false when
// the timeout expires before a slot frees.
bool icall_synclite_semslim_wait_ms(Il2CppObject* self, int32_t millisecondsTimeout);

// int Release(): increments the count (bounded by maximumCount; exceeding
// throws SemaphoreFullException) and returns the PREVIOUS count.
int32_t icall_synclite_semslim_release(Il2CppObject* self);

// int Release(int releaseCount): increments by releaseCount (>= 1, else
// ArgumentOutOfRangeException; exceeding maximumCount throws
// SemaphoreFullException) and returns the PREVIOUS count.
int32_t icall_synclite_semslim_release_n(Il2CppObject* self, int32_t releaseCount);

// --- CountdownEvent ----------------------------------------------------------

// bool Signal(): decrements the count (>= 1, else InvalidOperationException)
// and returns true when the count reaches 0.
bool icall_synclite_countdownevent_signal(Il2CppObject* self);

// int CurrentCount / bool IsSet.
int32_t icall_synclite_countdownevent_get_current_count(Il2CppObject* self);
bool icall_synclite_countdownevent_get_is_set(Il2CppObject* self);

// --- SpinLock ----------------------------------------------------------------

// void Enter(ref bool lockTaken): the flag's ADDRESS (int32_t*); on acquire
// writes *lockTaken = true (ArgumentException when it was already true,
// net48). Writes false on failure.
void icall_synclite_spinlock_enter(Il2CppObject* self, int32_t* lockTaken);

// void Exit(): releases the lock. bool IsHeld: the locked state.
void icall_synclite_spinlock_exit(Il2CppObject* self);
bool icall_synclite_spinlock_is_held(Il2CppObject* self);

// --- Barrier -----------------------------------------------------------------

// int ParticipantCount / int CurrentPhaseNumber.
int32_t icall_synclite_barrier_get_participant_count(Il2CppObject* self);
int32_t icall_synclite_barrier_get_current_phase_number(Il2CppObject* self);

// bool SignalAndWait(): decrements the pending-signal count; when every
// participant has signalled (this one is last), advances the phase and wakes
// all. Returns whether a post-phase action ran (always false -- none are
// modeled). -1 = infinite; a timeout overload is out of scope.
bool icall_synclite_barrier_signal_and_wait(Il2CppObject* self);

}} // namespace il2cpp::runtime