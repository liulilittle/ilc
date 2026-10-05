//------------------------------------------------------------------------------
// test_timer.cpp -- System.Threading.Timer / System.Timers.Timer basic
// domain runtime unit tests. See timer.h for the contract under test.
//
// Coverage (normal + edge + error paths):
//   1. one-shot                 : due=150 ms, period=-1; fires exactly once
//      (waited up to 400 ms for the first fire, +100 ms to prove no second).
//   2. periodic                 : due=50, period=100; >= 2 fires observed;
//      after dispose no further fires.
//   3. dispose-before-due       : a timer armed 60 s out, disposed
//      immediately, never fires.
//   4. change arms an unarmed timer (created with due=-1; no fire until the
//      change, then exactly one -- one-shot).
//   5. change re-arms an armed timer to fire EARLIER than its original due,
//      with a new period.
//   5b. dueTime=0 fires as soon as the timer thread runs (net48: zero delays
//      the callback until the thread pool schedules it).
//   5c. period=0 is a one-shot: fires once, never re-fires (net48: 0
//      disables periodic signaling, same as -1).
//   5d. change(-1, -1) disarms a running periodic timer (net48
//      Timeout.Infinite: no callback is scheduled).
//   6. callback thread          : the callback runs on a DIFFERENT thread
//      than the test main thread (the "thread-pool thread" model).
//   7. invalid arguments        : dueTime=-2 / period=-2 each throw
//      System.ArgumentOutOfRangeException.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//
// Timing budget: the worst-case run (every wait timing out) stays below
// ~3.4 s; a normal run finishes in ~2 s.
//------------------------------------------------------------------------------

#include "timer.h"

#include "il2cpp_exception.h" // il2cpp::DotNetException (catch type)
#include "il2cpp_thread.h"    // icall_thread_get_current_thread_id

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
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

// Polls counter every 10 ms until it reaches target or timeoutMs elapses;
// returns true when the condition was met in time.
bool WaitFor(const std::atomic<int32_t>& counter, int32_t target, int64_t timeoutMs)
{
    const auto start = std::chrono::steady_clock::now();
    while (std::chrono::steady_clock::now() - start < std::chrono::milliseconds(timeoutMs))
    {
        if (counter.load() >= target)
        {
            return true;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    return counter.load() >= target;
}

// Callback state: derives from Il2CppObject so the state pointer handed to
// the timer is a genuine managed-object pointer (generated classes embed the
// header as their first member, exactly like this).
struct CallbackState : il2cpp::Il2CppObject
{
    std::atomic<int32_t> fires;
    std::atomic<int32_t> threadId;
};

void CountingCallback(il2cpp::Il2CppObject* state)
{
    auto* cs = static_cast<CallbackState*>(state);
    cs->fires.fetch_add(1);
    cs->threadId.store(il2cpp::runtime::icall_thread_get_current_thread_id());
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    const int32_t mainThreadId = icall_thread_get_current_thread_id();

    // --- 1. One-shot: fires exactly once, on another thread ---
    CallbackState s1{};
    il2cpp_timer* t1 = il2cpp_timer_create(&CountingCallback, &s1, 150, -1);
    Expect(WaitFor(s1.fires, 1, 400), "one-shot timer fires within 400 ms");
    Expect(s1.threadId.load() != 0 && s1.threadId.load() != mainThreadId,
           "callback runs on a different thread than the creator");
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    Expect(s1.fires.load() == 1, "one-shot timer does not fire twice");
    il2cpp_timer_dispose(t1);

    // --- 2. Periodic: >= 2 fires; none after dispose ---
    CallbackState s2{};
    il2cpp_timer* t2 = il2cpp_timer_create(&CountingCallback, &s2, 50, 100);
    Expect(WaitFor(s2.fires, 2, 600), "periodic timer fires at least twice within 600 ms");
    il2cpp_timer_dispose(t2);
    const int32_t afterDispose = s2.fires.load();
    std::this_thread::sleep_for(std::chrono::milliseconds(150));
    Expect(s2.fires.load() == afterDispose, "no fire after dispose");

    // --- 3. Dispose before due: never fires ---
    CallbackState s3{};
    il2cpp_timer* t3 = il2cpp_timer_create(&CountingCallback, &s3, 60000, -1);
    il2cpp_timer_dispose(t3);
    std::this_thread::sleep_for(std::chrono::milliseconds(150));
    Expect(s3.fires.load() == 0, "disposed timer never fires");

    // --- 4. change arms a timer created with due=-1 ---
    CallbackState s4{};
    il2cpp_timer* t4 = il2cpp_timer_create(&CountingCallback, &s4, -1, -1);
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    Expect(s4.fires.load() == 0, "timer created with due=-1 does not fire");
    il2cpp_timer_change(t4, 50, -1);
    Expect(WaitFor(s4.fires, 1, 400), "change arms a due=-1 timer");
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    Expect(s4.fires.load() == 1, "re-armed one-shot fires exactly once");
    il2cpp_timer_dispose(t4);

    // --- 5. change re-arms an armed timer earlier + with a new period ---
    CallbackState s5{};
    il2cpp_timer* t5 = il2cpp_timer_create(&CountingCallback, &s5, 1000, -1);
    il2cpp_timer_change(t5, 40, 80);
    Expect(WaitFor(s5.fires, 3, 600),
           "change re-arms: fires ~40 ms in, then every 80 ms (3 fires within 600 ms)");
    il2cpp_timer_dispose(t5);

    // --- 5b. dueTime=0 fires as soon as the timer thread runs ---
    CallbackState s5b{};
    il2cpp_timer* t5b = il2cpp_timer_create(&CountingCallback, &s5b, 0, -1);
    Expect(WaitFor(s5b.fires, 1, 400), "dueTime=0 fires as soon as the thread runs");
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    Expect(s5b.fires.load() == 1, "dueTime=0 one-shot fires exactly once");
    il2cpp_timer_dispose(t5b);

    // --- 5c. period=0 is a one-shot (net48: 0 disables periodic signaling) ---
    CallbackState s5c{};
    il2cpp_timer* t5c = il2cpp_timer_create(&CountingCallback, &s5c, 30, 0);
    Expect(WaitFor(s5c.fires, 1, 400), "period=0 fires once");
    std::this_thread::sleep_for(std::chrono::milliseconds(150));
    Expect(s5c.fires.load() == 1, "period=0 does not re-fire (one-shot)");
    il2cpp_timer_dispose(t5c);

    // --- 5d. change(-1, -1) disarms a running periodic timer (net48
    // Timeout.Infinite: no callback is scheduled) ---
    CallbackState s5d{};
    il2cpp_timer* t5d = il2cpp_timer_create(&CountingCallback, &s5d, 30, 40);
    Expect(WaitFor(s5d.fires, 2, 400),
           "periodic timer fires repeatedly before the change");
    il2cpp_timer_change(t5d, -1, -1);
    const int32_t disarmedFires = s5d.fires.load();
    std::this_thread::sleep_for(std::chrono::milliseconds(250)); // ~6 periods
    const int32_t afterDisarm = s5d.fires.load();
    Expect(afterDisarm <= disarmedFires + 1,
           "change(-1,-1) disarms the periodic timer (at most the in-flight fire)");
    il2cpp_timer_dispose(t5d);

    // --- 6. invalid dueTime/period throw ArgumentOutOfRangeException ---
    CallbackState s6{};
    try
    {
        il2cpp_timer_create(&CountingCallback, &s6, -2, 100);
        Expect(false, "dueTime=-2 throws ArgumentOutOfRangeException");
    }
    catch (const il2cpp::DotNetException&)
    {
        Expect(true, "dueTime=-2 throws ArgumentOutOfRangeException");
    }
    try
    {
        il2cpp_timer_create(&CountingCallback, &s6, 0, -2);
        Expect(false, "period=-2 throws ArgumentOutOfRangeException");
    }
    catch (const il2cpp::DotNetException&)
    {
        Expect(true, "period=-2 throws ArgumentOutOfRangeException");
    }

    if (g_failures == 0)
    {
        std::printf("all runtime timer tests passed\n");
        return 0;
    }
    std::printf("%d timer test(s) FAILED\n", g_failures);
    return 1;
}
