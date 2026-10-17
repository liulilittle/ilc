#pragma once
//------------------------------------------------------------------------------
// il2cpp_waithandle.h -- System.Threading wait-handle runtime support
// (waithandle.cpp): Semaphore / Mutex / EventWaitHandle.
//
// OBJECT MODEL: each wait handle is a managed object (Il2CppWaitHandleObject
// -- header + ONE native state pointer) allocated through its GLOBAL-scope
// class descriptor (System_Threading_Semaphore_class / Mutex_class /
// EventWaitHandle_class, defined in waithandle.cpp -- the System_Version_class
// contract: the transpiler's BclAllocExpr fallback emits
// il2cpp_object_new(&System_Threading_Semaphore_class) for `new Semaphore(...)`).
// The native state (WaitHandleState -- std::mutex + std::condition_variable +
// count + kind) is NOT a GC reference: refFieldBitmap is 0, so the collector
// never scans past the header. The state is heap-allocated by the ctor icalls
// and never freed (no finalizer in this runtime -- the stream-handle registry
// precedent).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), the project's reference
// baseline:
//   * Semaphore(initialCount, maximumCount): initialCount in [0, maximumCount],
//     maximumCount >= 1; otherwise ArgumentOutOfRangeException. WaitOne
//     decrements the count (blocking while 0); Release()/Release(int) increment
//     (bounded by maximumCount; exceeding throws SemaphoreFullException) and
//     return the PREVIOUS count.
//   * Mutex(): WaitOne acquires (blocking); ReleaseMutex releases.
//   * EventWaitHandle(initialState, EventResetMode): Set signals (count 1),
//     Reset unsignals (count 0), WaitOne blocks while unsignaled. AutoReset
//     (mode 0) clears the signal on a successful WaitOne; ManualReset (mode 1)
//     keeps it set.
//   * WaitOne() returns true (net48: true on success; the timeout overloads
//     are out of scope).
//
// The runtime is SINGLE-THREADED for managed allocation/GC, so the wait
// handle state (std::mutex/condition_variable) is used only for the
// synchronization itself -- no managed allocation happens while a thread
// holds a wait handle (the threadsync2/conccases worker contract).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

#include <cstdint>

// System.Threading wait-handle runtime descriptors (GLOBAL scope -- the
// transpiler emits &System_Threading_*_class for newobj/isinst/castclass;
// defined in waithandle.cpp). nb3 adds the two sealed EventWaitHandle
// subclasses (AutoResetEvent / ManualResetEvent -- same object layout, the
// reset mode fixed by the ctor).
extern il2cpp::Il2CppClass System_Threading_Semaphore_class;
extern il2cpp::Il2CppClass System_Threading_Mutex_class;
extern il2cpp::Il2CppClass System_Threading_EventWaitHandle_class;
extern il2cpp::Il2CppClass System_Threading_AutoResetEvent_class;
extern il2cpp::Il2CppClass System_Threading_ManualResetEvent_class;

namespace il2cpp { namespace runtime {

// Native wait-handle state (forward; defined in waithandle.cpp).
struct WaitHandleState;

// Managed wait-handle layout: object header + ONE native state pointer. The
// pointer is NOT a GC reference (refFieldBitmap 0 -- the collector never
// scans past the header).
struct Il2CppWaitHandleObject
{
    Il2CppObject obj;        // object header (klass pointer, offset 0)
    WaitHandleState* state;  // native state (offset 8) -- not a GC reference
};

// Register the wait-handle descriptors once (idempotent --
// il2cpp_class_register dedupes by pointer): Semaphore / Mutex /
// EventWaitHandle + AutoResetEvent / ManualResetEvent. Called defensively by
// the ctor icalls before the first allocation.
void il2cpp_waithandle_register_classes();

// --- construction -----------------------------------------------------------

// Semaphore(int initialCount, int maximumCount): initialCount in
// [0, maximumCount], maximumCount >= 1; otherwise
// ArgumentOutOfRangeException. Allocates the native state.
void icall_waithandle_semaphore_ctor(Il2CppObject* self, int32_t initialCount,
                                     int32_t maximumCount);

// Mutex(): allocates the native state (unowned).
void icall_waithandle_mutex_ctor(Il2CppObject* self);

// EventWaitHandle(bool initialState, EventResetMode mode): mode 0 = AutoReset,
// 1 = ManualReset (net48 EventResetMode enum); otherwise
// ArgumentOutOfRangeException. Allocates the native state.
void icall_waithandle_event_ctor(Il2CppObject* self, bool initialState,
                                 int32_t mode);

// AutoResetEvent(bool initialState) / ManualResetEvent(bool initialState) --
// the sealed EventWaitHandle subclasses fix the reset mode (0 / 1). Same
// native state and event icall family; the allocation happens through the
// System_Threading_AutoResetEvent_class / ManualResetEvent_class descriptors.
void icall_waithandle_autoresetevent_ctor(Il2CppObject* self, bool initialState);
void icall_waithandle_manualresetevent_ctor(Il2CppObject* self, bool initialState);

// --- wait / signal ----------------------------------------------------------

// WaitOne(): blocks until the handle is signaled, then returns true (net48
// success semantics). For a semaphore, decrements the count; for an
// auto-reset event, clears the signal; for a mutex, acquires it.
bool icall_waithandle_wait_one(Il2CppObject* self);

// WaitOne(int millisecondsTimeout): the net48 timeout overload. 0 is a
// single non-blocking probe, -1 is Timeout.Infinite (block forever), a value
// < -1 throws ArgumentOutOfRangeException("millisecondsTimeout"); returns
// false when the timeout expires before the handle is signaled.
bool icall_waithandle_wait_one_timeout(Il2CppObject* self, int32_t millisecondsTimeout);

// WaitOne(TimeSpan timeout): the net48 TimeSpan overload (nb3 SYNC-GAP) --
// TotalMilliseconds truncation toward zero (ticks / 10000), Timeout.
// InfiniteTimeSpan (-10000 ticks) = -1 infinite; outside [-1, INT32_MAX] ->
// ArgumentOutOfRangeException("timeout"). Mirrors the monitor
// wait_timespan conversion and delegates to the int-milliseconds icall.
bool icall_waithandle_wait_one_timespan(Il2CppObject* self, int64_t ticks);

// Semaphore.Release(): increments the count (bounded by maximumCount;
// exceeding throws SemaphoreFullException) and returns the PREVIOUS count.
int32_t icall_waithandle_semaphore_release(Il2CppObject* self);

// Semaphore.Release(int releaseCount): increments by releaseCount (>= 1;
// otherwise ArgumentOutOfRangeException; exceeding maximumCount throws
// SemaphoreFullException) and returns the PREVIOUS count.
int32_t icall_waithandle_semaphore_release_n(Il2CppObject* self, int32_t releaseCount);

// Mutex.ReleaseMutex(): releases the mutex.
void icall_waithandle_mutex_release(Il2CppObject* self);

// EventWaitHandle.Set(): signals the event (count 1, wakes all waiters).
void icall_waithandle_event_set(Il2CppObject* self);

// EventWaitHandle.Reset(): unsignals the event (count 0).
void icall_waithandle_event_reset(Il2CppObject* self);

}} // namespace il2cpp::runtime
