//------------------------------------------------------------------------------
// threadpool.cpp -- System.Threading.ThreadPool basic domain (see
// threadpool.h for the contract).
//
// Model: ONE fixed-size pool of dedicated OS worker threads serving a FIFO
// queue. The worker count is decided ONCE at first pool use (lazy singleton
// via std::call_once): N = min(CPU core count, 4) -- the core count comes
// from GetSystemInfo dwNumberOfProcessors on Windows and
// std::thread::hardware_concurrency elsewhere (fallback 1 when unknown),
// and the "fixed 4" cap keeps the pool deterministic regardless of host.
//
// Queue protocol (both platforms):
//   * enqueue: lock, push_back, unlock, wake ONE waiter (the wake happens
//     after releasing the lock so the woken worker never contends with the
//     enqueuer).
//   * worker: lock, loop { while queue empty && !shutdown: cv wait }, pop
//     front, unlock, run callback. A shutdown drains the queue first (all
//     queued callbacks run before the worker exits) then exits.
//   * shutdown (atexit): set shutdown under lock, wake ALL, join every
//     worker (no callback runs after shutdown returns), release resources.
//     The drain-before-exit ordering means no queued work is ever lost.
//
// The single lock (pool->lock) is the only synchronization object touched
// by the workers and the enqueuer; wakeup 1:1 pairing is guaranteed by the
// wait-predicate-under-lock pattern (a wake is never lost: the predicate is
// re-checked after every wait, and shutdown uses WakeAll to release every
// waiter). Lock-order analysis: there is exactly one lock, so no ordering
// cycle is possible (D11).
//------------------------------------------------------------------------------

#include "threadpool.h"

#include "il2cpp_delegate_ext.h"  // icall_delegate_get_fn_ptr (unwrap)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception
#include "threadobj.h"            // il2cpp_threadobj_register_pool_worker (MTID)

#include <atomic>
#include <cstdint>
#include <cstdlib> // std::atexit
#include <deque>
#include <mutex>   // std::call_once / std::mutex
#include <vector>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #ifndef _WIN32_WINNT
        #define _WIN32_WINNT 0x0600 // CONDITION_VARIABLE (Vista+)
    #endif
    #include <windows.h>
#else
    #include <condition_variable>
    #include <thread>
#endif

namespace il2cpp { namespace runtime {

namespace {

struct WorkItem
{
    il2cpp_threadpool_work_item_callback callback;
    Il2CppObject* state;
};

struct ThreadPool
{
    std::deque<WorkItem> queue; // FIFO
    bool shutdown = false;
#if defined(_WIN32)
    CRITICAL_SECTION lock;
    CONDITION_VARIABLE workCv;
    std::vector<HANDLE> workers;
#else
    std::mutex lock;
    std::condition_variable workCv;
    std::vector<std::thread> workers;
#endif
};

std::once_flag g_initFlag;
ThreadPool* g_pool = nullptr;

// ThreadPool worker membership -- the platform-independent registration the
// Thread.IsThreadPoolThread icall (thread.cpp) queries. Every worker sets
// this thread_local flag to true at its entry; all other threads (main,
// threadobj-spawned) keep the default false. Written only by the two worker
// entry points below (single writer per thread: each thread owns its own
// thread_local copy -- D1), read only by
// il2cpp_threadpool_is_current_thread_worker().
thread_local bool g_isPoolWorker = false;

void mark_pool_worker()
{
    g_isPoolWorker = true;
    // MTID (net48 contract, A1 F50B348A item 5): a pool worker shares the
    // process-wide managed-id space with explicit Thread objects and REUSES
    // its id across every work item it serves. Register ONCE at thread start
    // (idempotent per thread) so Thread.CurrentThread.ManagedThreadId inside a
    // work item reports the worker's id, not the main-thread default 1.
    il2cpp::runtime::il2cpp_threadobj_register_pool_worker();
}

// Fixed worker count: min(CPU core count, 4), never below 1.
int32_t worker_count()
{
#if defined(_WIN32)
    SYSTEM_INFO info;
    ::GetSystemInfo(&info);
    const DWORD cores = info.dwNumberOfProcessors;
#else
    const unsigned cores = std::thread::hardware_concurrency();
#endif
    int32_t n = (cores > 0) ? static_cast<int32_t>(cores) : 1;
    if (n > 4)
    {
        n = 4; // fixed 4-thread-pool cap (see the header contract)
    }
    return n;
}

#if defined(_WIN32)
DWORD WINAPI worker_thread_proc(LPVOID param)
{
    mark_pool_worker(); // this OS thread is a ThreadPool worker (IsThreadPoolThread)
    ThreadPool* pool = static_cast<ThreadPool*>(param);
    for (;;)
    {
        ::EnterCriticalSection(&pool->lock);
        while (pool->queue.empty() && !pool->shutdown)
        {
            ::SleepConditionVariableCS(&pool->workCv, &pool->lock, INFINITE);
        }
        if (pool->queue.empty())
        {
            // shutdown AND drained: exit
            ::LeaveCriticalSection(&pool->lock);
            break;
        }
        WorkItem item = pool->queue.front();
        pool->queue.pop_front();
        ::LeaveCriticalSection(&pool->lock);

        item.callback(item.state);
    }
    return 0;
}
#else
void worker_thread_main(ThreadPool* pool)
{
    mark_pool_worker(); // this OS thread is a ThreadPool worker (IsThreadPoolThread)
    for (;;)
    {
        std::unique_lock<std::mutex> lock(pool->lock);
        pool->workCv.wait(lock, [pool] {
            return !pool->queue.empty() || pool->shutdown;
        });
        if (pool->queue.empty())
        {
            // shutdown AND drained: exit
            break;
        }
        WorkItem item = pool->queue.front();
        pool->queue.pop_front();
        lock.unlock();

        item.callback(item.state);
    }
}
#endif

// atexit: signal shutdown, join every worker, release resources. Runs on the
// main thread after main() returns; callbacks must be bounded (see header).
void pool_shutdown()
{
    ThreadPool* pool = g_pool;
    if (pool == nullptr)
    {
        return;
    }
    {
#if defined(_WIN32)
        ::EnterCriticalSection(&pool->lock);
        pool->shutdown = true;
        ::LeaveCriticalSection(&pool->lock);
        ::WakeAllConditionVariable(&pool->workCv); // release EVERY waiter
        for (HANDLE worker : pool->workers)
        {
            ::WaitForSingleObject(worker, INFINITE); // join (drains first)
            ::CloseHandle(worker);
        }
        ::DeleteCriticalSection(&pool->lock);
#else
        {
            std::lock_guard<std::mutex> lock(pool->lock);
            pool->shutdown = true;
        }
        pool->workCv.notify_all();
        for (std::thread& worker : pool->workers)
        {
            worker.join();
        }
#endif
    }
    delete pool;
    g_pool = nullptr;
}

void init_pool()
{
    auto* pool = new ThreadPool{};
#if defined(_WIN32)
    ::InitializeCriticalSection(&pool->lock);
    ::InitializeConditionVariable(&pool->workCv);
#else
    // default-constructed mutex/cv
#endif
    const int32_t n = worker_count();
    pool->workers.reserve(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
#if defined(_WIN32)
        const HANDLE h = ::CreateThread(nullptr, 0, &worker_thread_proc, pool, 0, nullptr);
        if (h == nullptr)
        {
            break; // thread creation failed: run with fewer workers
        }
        pool->workers.push_back(h);
#else
        pool->workers.emplace_back(&worker_thread_main, pool);
#endif
    }
    g_pool = pool;
    std::atexit(&pool_shutdown);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_threadpool_queue_work_item -- enqueue + wake one worker.
//------------------------------------------------------------------------------
void il2cpp_threadpool_queue_work_item(il2cpp_threadpool_work_item_callback callback,
                                       Il2CppObject* state)
{
    if (callback == nullptr)
    {
        return; // NULL callback: documented no-op (see the header contract)
    }
    std::call_once(g_initFlag, &init_pool);
    ThreadPool* pool = g_pool;
    {
#if defined(_WIN32)
        ::EnterCriticalSection(&pool->lock);
        pool->queue.push_back(WorkItem{callback, state});
        ::LeaveCriticalSection(&pool->lock);
        ::WakeConditionVariable(&pool->workCv); // wake ONE waiter
#else
        std::lock_guard<std::mutex> lock(pool->lock);
        pool->queue.push_back(WorkItem{callback, state});
        // wake after releasing the lock (see the comment at the top)
#endif
    }
#if !defined(_WIN32)
    pool->workCv.notify_one();
#endif
}

//------------------------------------------------------------------------------
// il2cpp_threadpool_queue_work_item_delegate -- managed-form enqueue (the
// QueueUserWorkItem wiring). The WaitCallback delegate object unwraps to
// its methodPtr -- single-cast STATIC-method delegates only, the same
// validation as icall_delegate_get_fn_ptr (delegatecb domain): null ->
// ArgumentNullException("WaitCallback") (net48 reference source ThreadPool.cs
// throws the TYPE name as the param, verified by live probe; unlike the
// plain callback's NULL no-op), multicast / instance-bound ->
// InvalidOperationException. The extracted methodPtr IS the generated-code
// ABI void(*)(Il2CppObject*), i.e. the exact callback form the pool runs --
// queue it directly.
//------------------------------------------------------------------------------
bool il2cpp_threadpool_queue_work_item_delegate(Il2CppObject* delegate, Il2CppObject* state)
{
    if (delegate == nullptr)
    {
        il2cpp_throw_argument_null_exception("WaitCallback");
    }
    void* fn = icall_delegate_get_fn_ptr(delegate);
    il2cpp_threadpool_queue_work_item(
        reinterpret_cast<il2cpp_threadpool_work_item_callback>(fn), state);
    return true; // the enqueue either succeeds or throws (see the header)
}

// 1-arg form: QueueUserWorkItem(callBack) forwards a null state.
bool il2cpp_threadpool_queue_work_item_delegate(Il2CppObject* delegate)
{
    return il2cpp_threadpool_queue_work_item_delegate(delegate, nullptr);
}

//------------------------------------------------------------------------------
// il2cpp_threadpool_get_max_threads -- the fixed worker count.
//------------------------------------------------------------------------------
int32_t il2cpp_threadpool_get_max_threads()
{
    std::call_once(g_initFlag, &init_pool);
    return static_cast<int32_t>(g_pool->workers.size());
}

//------------------------------------------------------------------------------
// il2cpp_threadpool_set_max_threads -- NO-OP placeholder (fixed pool).
//------------------------------------------------------------------------------
void il2cpp_threadpool_set_max_threads(int32_t maxThreads)
{
    (void)maxThreads; // the pool size is fixed at first use (see header)
}

//------------------------------------------------------------------------------
// il2cpp_threadpool_get_min_threads -- the fixed worker count (min == max in
// the fixed pool: every worker is spawned at first use and never retired).
//------------------------------------------------------------------------------
int32_t il2cpp_threadpool_get_min_threads()
{
    std::call_once(g_initFlag, &init_pool);
    return static_cast<int32_t>(g_pool->workers.size());
}

//------------------------------------------------------------------------------
// il2cpp_threadpool_set_min_threads -- net48 validation contract (returns
// false when workerThreads < 0, completionPortThreads < 0, or workerThreads
// exceeds the fixed max; otherwise true), then a NO-OP: the pool is fixed at
// first use, so the min is already N and cannot be changed. The completion-
// port count is dropped (never honored), matching the set_max_threads drop.
//------------------------------------------------------------------------------
bool il2cpp_threadpool_set_min_threads(int32_t workerThreads,
                                       int32_t completionPortThreads)
{
    if (workerThreads < 0 || completionPortThreads < 0
        || workerThreads > il2cpp_threadpool_get_max_threads())
    {
        return false;
    }
    return true; // valid request: accepted, no-op (fixed pool)
}

//------------------------------------------------------------------------------
// il2cpp_threadpool_is_current_thread_worker -- the platform-independent
// pool membership probe (declared in il2cpp_thread.h, see its comment).
// Returns true only when the CURRENT executing thread is one of the fixed
// pool workers -- a thread_local flag set at each worker's entry. Main and
// spawned threads report false. This is the backing query for
// icall_thread_get_is_thread_pool_thread.
//------------------------------------------------------------------------------
bool il2cpp_threadpool_is_current_thread_worker()
{
    return g_isPoolWorker;
}

}} // namespace il2cpp::runtime
