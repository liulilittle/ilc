//------------------------------------------------------------------------------
// test_thread.cpp -- System.Threading.Thread icall runtime unit tests.
// See il2cpp_thread.h for the contract under test.
//
// Coverage (normal + edge + error paths):
//   1. Sleep(50)          : the measured elapsed time is >= 45 ms (net48
//      semantics verified against the threadcase reference run, where the
//      Environment.TickCount delta across Sleep(50) is >= 45).
//   2. Sleep negative     : Sleep(-1), Sleep(-2) and Sleep(INT32_MIN) each
//      throw System.ArgumentOutOfRangeException (every negative value is
//      rejected; see the header for the -1/Timeout.Infinite deviation).
//   3. Sleep(0)           : yields and returns without sleeping (the
//      measured elapsed time stays far below the 45 ms floor).
//   4. sleep_zero         : the dedicated Sleep(0) entry is callable and
//      also stays below the 45 ms floor.
//   5. yield              : Thread.Yield is callable (no crash, returns).
//   6. current thread id  : nonzero and stable across two consecutive calls
//      (the same thread reports the same id).
//   7. priority           : always 0 (ThreadPriority.Normal).
//   8. critical regions   : begin/end are callable no-ops (no crash).
//   9. memory barrier     : a bare call is a no-crash smoke, AND a real
//      cross-thread ordering proof -- a relaxed-atomic flag + seq_cst
//      fences on both sides guarantee the consumer observes the producer's
//      plain write (the data race the fences eliminate would otherwise be
//      UB / unobservable).
//  10. IsThreadPoolThread : the MAIN thread reports false; a queued
//      ThreadPool work item (running on a dedicated worker) reports true --
//      the platform-independent thread_local registration in threadpool.cpp.
//  11. thread affinity    : begin/end are callable no-ops (no crash).
//  12. com cleanup        : DisableComObjectEagerCleanup is a callable
//      no-op (no COM model; accepts any Thread receiver, null included).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_thread.h"

#include "il2cpp_exception.h" // il2cpp::DotNetException (catch type)
#include "threadpool.h"       // il2cpp_threadpool_queue_work_item (worker probe)

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

// Elapsed milliseconds of 'fn' (steady clock; the test only asserts coarse
// floors so timer noise is irrelevant).
int64_t ElapsedMs(void (*fn)())
{
    const auto start = std::chrono::steady_clock::now();
    fn();
    const auto end = std::chrono::steady_clock::now();
    return std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();
}

// True when 'fn' throws a DotNetException whose type chain starts with the
// given managed type name (prefix match -- the factory chains list the
// .NET base classes after the type itself).
bool ThrowsDotNetPrefix(void (*fn)(), const char* typePrefix)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strncmp(e.type_name(), typePrefix, std::strlen(typePrefix)) == 0;
    }
    return false;
}

// Poll 'pred' until it returns true or 'timeoutMs' elapses (1 ms steps). A
// bounded wait, never a bare sleep: a broken pool fails the test via the
// timeout instead of hanging the process forever (same helper as
// test_threadpool.cpp).
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

// IsThreadPoolThread probe payload: `seen` records the icall result from
// INSIDE a pool worker, `done` signals completion to the waiting main
// thread. A struct (not globals) so the test stays self-contained.
struct PoolProbe
{
    std::atomic<bool> seen{false};
    std::atomic<bool> done{false};
};

// The ThreadPool work-item callback: answers IsThreadPoolThread on the
// worker thread (the state is the PoolProbe above).
void PoolProbeTask(il2cpp::Il2CppObject* state)
{
    auto* probe = reinterpret_cast<PoolProbe*>(state);
    probe->seen.store(il2cpp::runtime::icall_thread_get_is_thread_pool_thread(nullptr),
                      std::memory_order_relaxed);
    probe->done.store(true, std::memory_order_relaxed);
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 1. Sleep(50) actually sleeps (>= 45 ms; net48 oracle floor) ---
    Expect(ElapsedMs([]() { icall_thread_sleep(50); }) >= 45,
           "Sleep(50) elapsed >= 45 ms");

    // --- 2. Negative values throw ArgumentOutOfRangeException ---
    Expect(ThrowsDotNetPrefix([]() { icall_thread_sleep(-1); },
                              "System.ArgumentOutOfRangeException"),
           "Sleep(-1) throws ArgumentOutOfRangeException");
    Expect(ThrowsDotNetPrefix([]() { icall_thread_sleep(-2); },
                              "System.ArgumentOutOfRangeException"),
           "Sleep(-2) throws ArgumentOutOfRangeException");
    Expect(ThrowsDotNetPrefix([]() { icall_thread_sleep(INT32_MIN); },
                              "System.ArgumentOutOfRangeException"),
           "Sleep(INT32_MIN) throws ArgumentOutOfRangeException");

    // --- 3. Sleep(0) yields without sleeping (far below the 45 ms floor) ---
    Expect(ElapsedMs([]() { icall_thread_sleep(0); }) < 45,
           "Sleep(0) does not sleep (elapsed < 45 ms)");

    // --- 4. The dedicated Sleep(0) entry is callable and yields ---
    Expect(ElapsedMs([]() { icall_thread_sleep_zero(); }) < 45,
           "sleep_zero returns without sleeping (elapsed < 45 ms)");

    // --- 5. Thread.Yield is callable ---
    Expect(ElapsedMs([]() { icall_thread_yield(); }) >= 0,
           "yield is callable");

    // --- 6. Current thread id: nonzero and stable within the thread ---
    const int32_t id1 = icall_thread_get_current_thread_id();
    const int32_t id2 = icall_thread_get_current_thread_id();
    Expect(id1 != 0, "current thread id is nonzero");
    Expect(id1 == id2, "current thread id is stable for the thread");

    // --- 7. Priority is always Normal (0) ---
    Expect(icall_thread_get_priority() == 0,
           "priority is 0 (ThreadPriority.Normal)");

    // --- 8. Critical-region placeholders are callable no-ops ---
    icall_thread_begin_critical_region();
    icall_thread_end_critical_region();
    Expect(true, "begin/end critical region are callable");

    // --- 9. MemoryBarrier: bare-call smoke + cross-thread ordering proof ---
    icall_thread_memory_barrier();
    Expect(true, "MemoryBarrier is callable");

    {
        // Data-race elimination: the producer writes a PLAIN int then
        // releases the flag; the consumer acquires the flag then reads the
        // int. With a seq_cst fence on both sides the write/read pair is
        // well-ordered (the release fence synchronizes with the acquire
        // fence through the relaxed-atomic flag) -- the consumer is
        // GUARANTEED to observe 42. Without the fences this is a data race
        // (UB); the fence pair is what makes the assertion deterministic.
        int shared = 0;
        std::atomic<int> flag{0};
        int observed = -1;

        std::thread consumer([&] {
            const auto deadline = std::chrono::steady_clock::now()
                                  + std::chrono::seconds(5);
            while (flag.load(std::memory_order_relaxed) == 0
                   && std::chrono::steady_clock::now() < deadline)
            {
                icall_thread_memory_barrier();
                std::this_thread::yield();
            }
            icall_thread_memory_barrier(); // acquire side of the fence pair
            observed = shared;
        });

        shared = 42;
        icall_thread_memory_barrier(); // release side of the fence pair
        flag.store(1, std::memory_order_relaxed);

        consumer.join();
        Expect(observed == 42,
               "MemoryBarrier orders the shared write before the flag (no data race)");
    }

    // --- 10. IsThreadPoolThread: main false, pool worker true ---
    Expect(!icall_thread_get_is_thread_pool_thread(nullptr),
           "main thread is not a threadpool thread");
    {
        PoolProbe probe;
        il2cpp_threadpool_queue_work_item(
            &PoolProbeTask, reinterpret_cast<il2cpp::Il2CppObject*>(&probe));
        Expect(WaitUntilMs([&probe] { return probe.done.load(std::memory_order_relaxed); },
                           5000),
               "IsThreadPoolThread probe ran on a pool worker");
        Expect(probe.seen.load(std::memory_order_relaxed),
               "a pool worker reports IsThreadPoolThread == true");
    }

    // --- 11. Thread-affinity placeholders are callable no-ops ---
    icall_thread_begin_thread_affinity();
    icall_thread_end_thread_affinity();
    Expect(true, "begin/end thread affinity are callable");

    // --- 12. COM-cleanup placeholder is a callable no-op (null receiver) ---
    icall_thread_disable_com_object_eager_cleanup(nullptr);
    Expect(true, "DisableComObjectEagerCleanup is callable");

    if (g_failures == 0)
    {
        std::printf("all runtime thread tests passed\n");
        return 0;
    }
    std::printf("%d thread test(s) FAILED\n", g_failures);
    return 1;
}
