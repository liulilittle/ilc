#pragma once
//------------------------------------------------------------------------------
// threadpool.h -- System.Threading.ThreadPool basic domain (simplified; see
// threadpool.cpp for the implementation).
//
// M-stage model: ONE fixed-size pool of dedicated OS worker threads serving
// a FIFO task queue (the simplified stand-in for QueueUserWorkItem). The
// callback form matches the timer domain (timer.h): plain function pointer
// void(*)(Il2CppObject*) -- the managed WaitCallback delegate unwraps to
// this when the wiring lands in MethodRefResolver.
//
// Simplified contract:
//   * il2cpp_threadpool_queue_work_item(callback, state) enqueues a work
//     item and wakes ONE worker; the callback runs on a worker thread, never
//     on the caller's stack. A NULL callback is silently ignored (no-op).
//   * The worker count is FIXED at first use (no growth/shrink):
//     N = min(CPU core count, 4). The core count is read once from the
//     system (GetSystemInfo dwNumberOfProcessors on Windows,
//     std::thread::hardware_concurrency elsewhere; fallback 1 when the
//     count is unknown). The "fixed 4" cap keeps the pool deterministic
//     regardless of host core count.
//   * il2cpp_threadpool_get_max_threads() returns N (the fixed worker
//     count). il2cpp_threadpool_set_max_threads() is a NO-OP placeholder:
//     the pool size is fixed at first use, so setting the max is
//     meaningless (net48 would resize on demand; the simplified model does
//     not).
//   * il2cpp_threadpool_get_min_threads() also returns N (min == max in the
//     fixed pool: every worker is spawned at first use and never retired).
//     il2cpp_threadpool_set_min_threads() validates its arguments against
//     the net48 contract (negative values or a worker count above the max ->
//     false; otherwise true) and is otherwise a NO-OP -- the fixed pool
//     already runs its min, so a valid set cannot change anything (the
//     completion-port count is dropped, matching set_max_threads).
//   * The pool shuts down at process exit (atexit): all workers are
//     signalled and joined, so no callback runs after exit() returns.
//   * The caller keeps `state` alive: the pool does not root it (no GC
//     rooting in the M-stage model); it must outlive the last callback that
//     may still be queued.
//   * Callbacks must be bounded: a callback that blocks forever stalls its
//     worker (and the exit-time join) -- net48 pool semantics apply.
//
// Platform mapping (see threadpool.cpp):
//   Windows: ::CreateThread workers + CRITICAL_SECTION + CONDITION_VARIABLE
//            (wake via ::WakeConditionVariable, wait via
//            ::SleepConditionVariableCS); join via ::WaitForSingleObject.
//   POSIX:   std::thread workers + std::mutex + std::condition_variable.
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h" // Il2CppObject (callback state type)

namespace il2cpp { namespace runtime {

// Callback invoked on a pool worker thread with the state passed to
// queue_work_item. Same form as the timer callback (timer.h).
using il2cpp_threadpool_work_item_callback = void (*)(Il2CppObject* state);

// Enqueue a work item and wake ONE worker thread. The callback runs on a
// pool thread, never on the caller's stack. A NULL callback is silently
// ignored (the enqueue is a no-op).
void il2cpp_threadpool_queue_work_item(il2cpp_threadpool_work_item_callback callback,
                                       Il2CppObject* state);

// Managed-form enqueue -- the QueueUserWorkItem wiring. The WaitCallback
// DELEGATE object unwraps to the callback form above (the "unwraps to this
// when the wiring lands" step from the header comment): a single-cast
// STATIC-method delegate's methodPtr IS the generated-code ABI
// void(*)(Il2CppObject*), so the runtime queues the extracted pointer
// directly (delegate-domain methodPtr direct call). Validation is shared
//   with icall_delegate_get_fn_ptr (delegatecb domain): a null delegate
//   raises ArgumentNullException("WaitCallback") -- the net48 contract
//   (reference source ThreadPool.cs throws the TYPE name as the param,
//   live-probe verified), unlike the plain NULL-callback no-op above --
//   and multicast containers / instance-bound (capturing-lambda shape)
//   delegates raise InvalidOperationException (no target binding exists in
//   the M-stage model). Returns true once the item is queued (net48's bool
//   success result; the enqueue either succeeds or throws, so it is always
//   true). The 1-arg overload passes a null state (net48's
//   QueueUserWorkItem(callBack) forwards state = null).
bool il2cpp_threadpool_queue_work_item_delegate(Il2CppObject* delegate, Il2CppObject* state);
bool il2cpp_threadpool_queue_work_item_delegate(Il2CppObject* delegate);

// SIMPLIFIED -> int32. Returns the fixed worker count
// (N = min(CPU core count, 4), decided at first pool use). Callable before
// any work item is queued (initializes the pool).
int32_t il2cpp_threadpool_get_max_threads();

// SIMPLIFIED -> void. NO-OP placeholder: the pool size is fixed at first
// use, so this never changes the worker count (net48 would resize the pool;
// the simplified model does not).
void il2cpp_threadpool_set_max_threads(int32_t maxThreads);

// SIMPLIFIED -> int32. ThreadPool.GetMinThreads worker count (net48 oracle:
// the minimum number of worker threads the pool keeps -- this model's fixed
// pool runs N workers from first use, so min == max == N). Callable before
// any work item is queued (initializes the pool).
int32_t il2cpp_threadpool_get_min_threads();

// SIMPLIFIED -> bool. ThreadPool.SetMinThreads validation contract (net48
// oracle, live-probe verified): returns false when workerThreads < 0,
// completionPortThreads < 0, or workerThreads > the fixed max worker count
// (net48: "setmin_big" above max -> False, "setmin_neg" -> False, "setmin_0_0"
// and "setmin_1_1" -> True). A valid call is a NO-OP: the pool is fixed at
// first use, so the min is already N and cannot be changed (the completion-
// port count is DROPPED, matching the set_max_threads drop). Returns true
// when the arguments are valid (net48's bool success result).
bool il2cpp_threadpool_set_min_threads(int32_t workerThreads,
                                       int32_t completionPortThreads);

}} // namespace il2cpp::runtime
