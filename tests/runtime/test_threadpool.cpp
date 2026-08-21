//------------------------------------------------------------------------------
// test_threadpool.cpp -- System.Threading.ThreadPool (simplified) runtime
// unit tests. See threadpool.h for the contract under test.
//
// Coverage (normal + edge + error paths):
//   1. 100 queued tasks      : every task runs exactly once (atomic counter
//      reaches 100; FIFO order additionally proven by the empty-task probe
//      in group 3).
//   2. concurrency ceiling   : the observed peak number of concurrently
//      running callbacks never exceeds the worker count returned by
//      get_max_threads; with >= 2 workers the 100 x 20 ms sleep tasks must
//      overlap (peak >= 2), proving real parallelism rather than serial
//      execution.
//   3. empty task            : a no-op callback (null state) is executed
//      without crashing and, being FIFO, runs before a follow-up counting
//      task; a NULL callback is silently ignored (no crash, pool healthy
//      afterwards).
//   4. max-threads contract  : get_max_threads is in [1, 4] (the fixed
//      pool cap); set_max_threads is a callable no-op that does not change
//      the reported count.
//   4b. min-threads contract : get_min_threads == get_max_threads (the
//      fixed pool runs every worker from first use, so min == max); the
//      net48 SetMinThreads validation (live-probe oracle) -- 0/1 accepted,
//      negative or above-max rejected, the pool never resizes.
//   5. delegate form          : the QueueUserWorkItem unwrap -- a managed
//      delegate OBJECT (il2cpp_delegate_new, the generated ldftn + newobj
//      DelegateCtor shape) queues through its methodPtr (both arities);
//      a null delegate raises ArgumentNullException("callBack") and an
//      instance-bound delegate raises InvalidOperationException.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "threadpool.h"

#include "il2cpp_delegate.h"      // il2cpp_delegate_new (managed delegate shape)
#include "il2cpp_exception.h"     // DotNetException (raise-and-catch helper)

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <thread>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Poll 'pred' until it returns true or 'timeoutMs' elapses (1 ms steps).
// A bounded wait, never a bare sleep: a broken pool fails the test via the
// timeout instead of hanging the process forever.
template <typename Pred>
bool WaitUntilMs(Pred pred, int64_t timeoutMs)
{
    const auto deadline = std::chrono::steady_clock::now()
                          + std::chrono::milliseconds(timeoutMs);
    while (std::chrono::steady_clock::now() < deadline)
    {
        if (pred())
        {
            return true;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    return pred();
}

// Callback payload: a plain counter; incremented once per execution.
void CountTask(il2cpp::Il2CppObject* state)
{
    auto* counter = reinterpret_cast<std::atomic<int>*>(state);
    counter->fetch_add(1, std::memory_order_relaxed);
}

// Empty task: a no-op callback (does nothing with its state).
void NoopTask(il2cpp::Il2CppObject* state)
{
    (void)state;
}

// Concurrency probe context: running/max track the live and peak callback
// counts; done counts completed callbacks.
struct ConcurrencyCtx
{
    std::atomic<int> running{0};
    std::atomic<int> maxRunning{0};
    std::atomic<int> done{0};
};

void ConcurrencyTask(il2cpp::Il2CppObject* state)
{
    auto* ctx = reinterpret_cast<ConcurrencyCtx*>(state);
    const int now = ctx->running.fetch_add(1, std::memory_order_relaxed) + 1;
    int seen = ctx->maxRunning.load(std::memory_order_relaxed);
    while (seen < now
           && !ctx->maxRunning.compare_exchange_weak(
               seen, now, std::memory_order_relaxed, std::memory_order_relaxed))
    {
    }
    // Hold the worker long enough for the other workers to pick up tasks
    // and overlap (the sleep granularity on Windows is ~15.6 ms).
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
    ctx->running.fetch_sub(1, std::memory_order_relaxed);
    ctx->done.fetch_add(1, std::memory_order_relaxed);
}

// Raise-and-catch helper: run 'fn', capture the managed exception carrier
// and compare the full type-name chain (see test_linq.cpp).
template <typename Fn>
bool ThrowsDotNet(Fn fn, const char* expectedType)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
}

const char* kArgNullChain = "System.ArgumentNullException,System.ArgumentNullException,"
                            "System.ArgumentException,System.SystemException,System.Exception";
// delegatecb's icall_delegate_get_fn_ptr rejects multicast / instance-bound
// delegates with a raw il2cpp_throw_new SHORT chain (delegateext.cpp).
const char* kInvalidOpShortChain = "System.InvalidOperationException,System.Exception";

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 4. max-threads contract (probed first: pool init + no-op set) ---
    const int32_t maxThreads = il2cpp_threadpool_get_max_threads();
    Expect(maxThreads >= 1 && maxThreads <= 4,
           "get_max_threads is in [1, 4] (fixed pool cap)");
    il2cpp_threadpool_set_max_threads(64);
    Expect(il2cpp_threadpool_get_max_threads() == maxThreads,
           "set_max_threads is a no-op (fixed pool)");

    // --- 4b. min-threads contract (rtaud11: net48 Get/SetMinThreads) ---
    // min == max in the fixed pool (every worker spawned at first use).
    Expect(il2cpp_threadpool_get_min_threads() == maxThreads,
           "get_min_threads == the fixed worker count (min == max)");
    // net48 SetMinThreads validation (live-probe oracle): valid requests
    // (0 or 1 workers) return true; a worker count above the max, or any
    // negative argument, return false. The fixed pool never changes, so the
    // reported min stays maxThreads throughout.
    Expect(il2cpp_threadpool_set_min_threads(0, 0),
           "set_min_threads(0,0) accepted (net48: setmin_0_0=True)");
    Expect(il2cpp_threadpool_set_min_threads(1, 1),
           "set_min_threads(1,1) accepted (net48: setmin_1_1=True)");
    Expect(!il2cpp_threadpool_set_min_threads(-1, 1),
           "set_min_threads(negative worker) rejected (net48: setmin_neg=False)");
    Expect(!il2cpp_threadpool_set_min_threads(1, -1),
           "set_min_threads(negative completion) rejected (net48)");
    Expect(!il2cpp_threadpool_set_min_threads(maxThreads + 1, 1),
           "set_min_threads(above max) rejected (net48: setmin_big=False)");
    Expect(il2cpp_threadpool_get_min_threads() == maxThreads,
           "get_min_threads unchanged after set (fixed pool)");

    // --- 1. 100 queued tasks all execute (atomic counter) ---
    {
        std::atomic<int> counter{0};
        for (int i = 0; i < 100; ++i)
        {
            il2cpp_threadpool_queue_work_item(
                &CountTask, reinterpret_cast<il2cpp::Il2CppObject*>(&counter));
        }
        Expect(WaitUntilMs([&counter] { return counter.load() == 100; }, 10000),
               "all 100 queued tasks executed");
    }

    // --- 2. concurrency ceiling <= worker count (and real overlap) ---
    {
        ConcurrencyCtx ctx;
        for (int i = 0; i < 100; ++i)
        {
            il2cpp_threadpool_queue_work_item(
                &ConcurrencyTask, reinterpret_cast<il2cpp::Il2CppObject*>(&ctx));
        }
        Expect(WaitUntilMs([&ctx] { return ctx.done.load() == 100; }, 30000),
               "100 concurrency-probe tasks completed");
        const int peak = ctx.maxRunning.load();
        Expect(peak >= 1, "peak concurrency is at least 1");
        Expect(peak <= maxThreads,
               "peak concurrency never exceeds the worker count");
        if (maxThreads >= 2)
        {
            // 100 x 20 ms tasks cannot finish serially within a 30 s window
            // without overlap; with >= 2 workers the pool MUST overlap.
            Expect(peak >= 2, "tasks ran in parallel (peak >= 2)");
        }
    }

    // --- 3. empty task: no-op callback runs (FIFO first) + null guard ---
    {
        std::atomic<int> counter{0};
        // FIFO: the no-op (queued first) executes BEFORE the counting task,
        // so reaching counter == 1 proves the no-op ran without crashing.
        il2cpp_threadpool_queue_work_item(&NoopTask, nullptr); // empty task
        il2cpp_threadpool_queue_work_item(
            &CountTask, reinterpret_cast<il2cpp::Il2CppObject*>(&counter));
        Expect(WaitUntilMs([&counter] { return counter.load() == 1; }, 10000),
               "no-op task executed (FIFO) without crashing");

        // NULL callback: documented no-op, must not crash or wedge the pool.
        il2cpp_threadpool_queue_work_item(nullptr, nullptr);
        il2cpp_threadpool_queue_work_item(
            &CountTask, reinterpret_cast<il2cpp::Il2CppObject*>(&counter));
        Expect(WaitUntilMs([&counter] { return counter.load() == 2; }, 10000),
               "null callback ignored; pool still executes tasks");
    }

    // --- 5. delegate-form enqueue (the QueueUserWorkItem unwrap) ---
    {
        std::atomic<int> counter{0};
        il2cpp::Il2CppDelegate* d = il2cpp::il2cpp_delegate_new(
            reinterpret_cast<void*>(&CountTask));
        il2cpp_threadpool_queue_work_item_delegate(
            reinterpret_cast<il2cpp::Il2CppObject*>(d),
            reinterpret_cast<il2cpp::Il2CppObject*>(&counter));
        Expect(WaitUntilMs([&counter] { return counter.load() == 1; }, 10000),
               "delegate-form task executed (methodPtr unwrap, 2-arg)");
    }
    {
        std::atomic<int> counter{0};
        il2cpp::Il2CppDelegate* noop = il2cpp::il2cpp_delegate_new(
            reinterpret_cast<void*>(&NoopTask));
        il2cpp_threadpool_queue_work_item_delegate(
            reinterpret_cast<il2cpp::Il2CppObject*>(noop)); // 1-arg: null state
        il2cpp_threadpool_queue_work_item(
            &CountTask, reinterpret_cast<il2cpp::Il2CppObject*>(&counter));
        Expect(WaitUntilMs([&counter] { return counter.load() == 1; }, 10000),
               "1-arg delegate-form noop ran (FIFO) with null state");
    }
    // Null delegate -> ArgumentNullException ("WaitCallback" -- the net48
    // reference source throws the TYPE name as the param (live-probe
    // verified), NOT the plain callback's NULL no-op).
    Expect(ThrowsDotNet([] {
        il2cpp_threadpool_queue_work_item_delegate(nullptr, nullptr);
    }, kArgNullChain), "null delegate raises ArgumentNullException");
    // net48-exact param name: the message must carry "(Parameter 'WaitCallback')".
    {
        bool hasParam = false;
        try
        {
            il2cpp_threadpool_queue_work_item_delegate(nullptr, nullptr);
        }
        catch (const il2cpp::DotNetException& e)
        {
            hasParam = std::strstr(e.message_text(), "WaitCallback") != nullptr;
        }
        Expect(hasParam, "null delegate message names the WaitCallback param (net48)");
    }
    // Instance-bound delegate (target != null) -> InvalidOperationException
    // (no target binding in the M-stage model -- same rejection as
    // icall_delegate_get_fn_ptr).
    {
        il2cpp::Il2CppDelegate bound{};
        bound.methodPtr = reinterpret_cast<void*>(&CountTask);
        bound.target = reinterpret_cast<il2cpp::Il2CppObject*>(&bound); // non-null target
        Expect(ThrowsDotNet([&] {
            il2cpp_threadpool_queue_work_item_delegate(
                reinterpret_cast<il2cpp::Il2CppObject*>(&bound), nullptr);
        }, kInvalidOpShortChain), "instance-bound delegate raises InvalidOperationException");
    }

    if (g_failures == 0)
    {
        std::printf("all runtime threadpool tests passed\n");
        return 0;
    }
    std::printf("%d threadpool test(s) FAILED\n", g_failures);
    return 1;
}
