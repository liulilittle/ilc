#pragma once
//------------------------------------------------------------------------------
// il2cpp_thread.h -- System.Threading.Thread icall contract (see thread.cpp
// for the implementation).
//
// M-stage model: the runtime is SINGLE-THREADED, but Thread.Sleep is a
// commonly-used API in real programs, so the sleep/yield primitives are
// real; everything thread-identity related is simplified. ThreadPool
// (threadpool.cpp) owns a fixed set of dedicated worker threads; the
// icall_thread_get_is_thread_pool_thread query answers "is the CURRENT
// executing thread one of those workers" through the platform-independent
// thread_local registration below.
//
// The simplified thread-identity model:
//   * icall_thread_get_current_thread_id returns the PROCESS thread id
//     (Windows GetCurrentThreadId low 32 bits, POSIX syscall(SYS_gettid))
//     -- NOT the ManagedThreadId (which would always be 1 in the
//     single-threaded model and is therefore useless as an identity probe;
//     the native id is nonzero, stable for the process lifetime, and
//     distinct across OS threads).
//   * icall_thread_get_priority always returns 0 (ThreadPriority.Normal) --
//     there is exactly one thread and it never changes priority.
//   * icall_thread_begin_critical_region / end_critical_region are no-op
//     placeholders (the real semantics -- marking code regions that must not
//     be interrupted by thread aborts -- are meaningless without abort
//     support).
//
// net48 semantics (verified against the threadcase reference run):
//   * Thread.Sleep(50) actually sleeps >= ~45 ms (Environment.TickCount
//     delta >= 45 in the reference run).
//   * Sleep(0) yields the time slice and returns (does NOT sleep).
//   * A NEGATIVE argument throws ArgumentOutOfRangeException. NOTE: net48
//     accepts -1 (Timeout.Infinite) and blocks FOREVER; the runtime icall
//     rejects EVERY negative value with ArgumentOutOfRangeException -- the
//     e2e throw probe therefore uses -2 (documented in threadcase.cs).
//   * Thread.Yield() lets other threads run and returns immediately.
//   * Thread.MemoryBarrier() orders ALL memory accesses around it
//     (std::atomic_thread_fence(seq_cst), the reference netcore
//     implementation delegates to Interlocked.MemoryBarrier).
//   * Thread.BeginThreadAffinity() / EndThreadAffinity() are no-ops -- the
//     reference netcore implementation has empty bodies (the runtime never
//     migrates a thread to another CPU).
//   * Thread.DisableComObjectEagerCleanup() is a no-op -- no COM object
//     model exists in this runtime.
//   * Thread.IsThreadPoolThread reports whether the CURRENT executing
//     thread is a ThreadPool worker (main and spawned threads: false, pool
//     workers: true). This is an instance getter whose `self` argument is
//     accepted and ignored (documented deviation: net48 reads the state of
//     the `self` Thread object; the model has no per-object pool tracking).
//
// Platform mapping (see thread.cpp):
//   Windows:  ::Sleep(ms) / ::Sleep(0) / ::SwitchToThread() / ::GetCurrentThreadId()
//   POSIX:    std::this_thread::sleep_for / ::sched_yield() / ::syscall(SYS_gettid)
//   MemoryBarrier: C++11 std::atomic_thread_fence (std::memory_order_seq_cst),
//                  portable across every platform -- no OS API needed.
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h" // Il2CppObject (the Thread instance receiver)

namespace il2cpp { namespace runtime {

// Thread.Sleep(int32 milliseconds) -> void. A negative value throws
// System.ArgumentOutOfRangeException (net48 semantics; every negative value,
// including -1, per the simplified contract above). 0 yields the time slice
// (same behavior as icall_thread_sleep_zero); positive values suspend the
// current thread for the given number of milliseconds.
void icall_thread_sleep(int32_t milliseconds);

// Thread.Sleep(0) dedicated entry -> void. Yields the time slice without
// sleeping (Windows ::Sleep(0), POSIX ::sched_yield()).
void icall_thread_sleep_zero();

// Thread.CurrentThread.ManagedThreadId (SIMPLIFIED) -> int32. Returns the
// OS thread id (GetCurrentThreadId / SYS_gettid), NOT the managed id --
// see the header comment; always nonzero in practice, stable for the
// process lifetime.
int32_t icall_thread_get_current_thread_id();

// Thread.Yield() -> void. Yields the current time slice to other threads
// (Windows ::SwitchToThread(), POSIX ::sched_yield()); the return value
// (whether a thread actually yielded) is dropped -- the managed API's bool
// is not modeled.
void icall_thread_yield();

// Thread.CurrentThread.Priority (SIMPLIFIED) -> int32. Always 0 =
// ThreadPriority.Normal (the single-threaded model never changes priority).
int32_t icall_thread_get_priority();

// Thread.BeginCriticalRegion() -> void. No-op placeholder (see the header
// comment: without thread-abort support the critical-region semantics are
// meaningless).
void icall_thread_begin_critical_region();

// Thread.EndCriticalRegion() -> void. No-op placeholder (see
// icall_thread_begin_critical_region).
void icall_thread_end_critical_region();

// Thread.MemoryBarrier() -> void. Orders ALL memory accesses around the
// call: no read/write before it may move after, and no read/write after it
// may move before (C++11 std::atomic_thread_fence with
// std::memory_order_seq_cst). Portable across every platform -- the
// reference netcore implementation delegates to Interlocked.MemoryBarrier.
void icall_thread_memory_barrier();

// Thread.GetCurrentProcessorNumber() (net5+ audit gap): the CPU id the
// current thread runs on (Win32 GetCurrentProcessorNumber / Linux
// sched_getcpu; generic-POSIX fallback reports 0).
int32_t icall_thread_get_current_processor_number();

// Thread.IsThreadPoolThread -> bool. Reports whether the CURRENT executing
// thread is a ThreadPool worker thread. `self` (the managed Thread object)
// is accepted and IGNORED -- the model tracks pool membership per OS thread
// (thread_local in threadpool.cpp), not per Thread object (documented
// deviation: net48 reads the `self` object's own state).
bool icall_thread_get_is_thread_pool_thread(Il2CppObject* self);

// Thread.BeginThreadAffinity() -> void. No-op: marks a region where the
// thread must not be migrated across CPUs, but this runtime never migrates
// threads (same empty-body contract as the reference netcore
// implementation).
void icall_thread_begin_thread_affinity();

// Thread.EndThreadAffinity() -> void. No-op (the counterpart of
// icall_thread_begin_thread_affinity; reference netcore: empty body).
void icall_thread_end_thread_affinity();

// Thread.DisableComObjectEagerCleanup() -> void. No-op: disables the eager
// cleanup of the thread's COM objects in net48; this runtime has no COM
// object model, so there is nothing to disable. `self` is accepted and
// ignored.
void icall_thread_disable_com_object_eager_cleanup(Il2CppObject* self);

// ThreadPool worker membership probe (IMPLEMENTED in threadpool.cpp; declared
// here because this header is the writable Thread-domain contract). Returns
// true only when the CURRENT executing thread is one of the fixed pool
// workers: a thread_local flag set at each worker's entry, platform-
// independent (C++11 thread_local). All other threads (main, spawned)
// report false.
bool il2cpp_threadpool_is_current_thread_worker();

}} // namespace il2cpp::runtime
