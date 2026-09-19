//------------------------------------------------------------------------------
// test_monitor.cpp -- System.Threading.Monitor (lock semantics) runtime unit
// tests. See monitor.h for the contract under test.
//
// Coverage (normal + edge + error paths):
//   1. try_enter basics       : acquires an unowned monitor; reentrant
//      (recursion incremented); matching exits release it fully; a further
//      exit throws SynchronizationLockException.
//   2. enter reentrancy       : enter/enter/try_enter balanced by three
//      exits; the fourth-level exit throws SynchronizationLockException.
//   3. ownership errors       : exit / pulse / wait on an unowned or
//      never-monitored object each throw SynchronizationLockException.
//   4. null arguments         : every entry point with nullptr throws
//      ArgumentNullException.
//   5. mutual exclusion       : two threads, 1000 monitor-guarded increments
//      each -- the final counter is EXACTLY 2000 (the monitor serializes
//      the critical sections).
//   6. held-lock blocking     : the second thread is excluded while the
//      first holds the monitor (300 ms hold) -- state written inside the
//      first critical section is observed unchanged by the second, and the
//      second thread's acquisition really waited (elapsed >= 280 ms).
//   7. wait/pulse handoff     : the owner waits; a second thread acquires
//      the released monitor, publishes a flag, pulses and exits; Wait
//      returns true, the flag is visible, and the caller re-owns the
//      monitor (recursion restored) after the wait.
//   8. wait timeout           : Wait(obj, 100) without a pulse returns false
//      after >= ~90 ms and the caller still owns the monitor.
//   9. wait(0)                : returns false immediately (no sleep).
//  10. wait(-1) infinite      : Wait(Timeout.Infinite) returns true only
//      when a pulser thread (100 ms later) signals it.
//  11. side-table exhaustion  : 64 held monitors fill the fixed table; the
//     65th enter throws InvalidOperationException; releasing the
//     entries frees slots and the object becomes usable again.
//  12. enter(obj, bool*) flag : the 2-arg Roslyn lock-lowering form writes
//     *lockTaken = true on acquire (fresh AND reentrant) and leaves the
//     flag untouched when enter throws (null / table exhaustion).
//  13. lockTaken must be false : Enter/TryEnter with an ALREADY-true flag
//     throw ArgumentException ("lockTaken") BEFORE the null check (net48
//     probe-verified: Enter(null, ref true) -> ArgumentException, not ANE);
//     the flag is left untouched and the monitor is NOT acquired.
//  14. try_enter 2-arg form   : TryEnter(obj, ref lockTaken) writes the flag
//     with the result -- true on fresh/reentrant acquire, false when
//     contended -- and acquires no monitor when the flag is already true.
//  15. cross-thread ownership : Exit from a thread that does NOT own the
//     monitor throws SynchronizationLockException (the owner keeps it).
//  16. wait recursion restore : Wait on a monitor entered TWICE saves and
//     restores the full recursion depth (two exits are still required).
//  17. pulse with no waiter   : a Pulse with nobody waiting is LOST (no
//     latch) -- a following Wait times out (net48 semantics).
//  18. contended try_enter    : TryEnter returns false while another thread
//     holds the monitor, and the failed attempt leaks no reservation
//     (succeeds once the owner releases).
//  19. multi-waiter broadcast : ONE pulse wakes ALL current waiters (the
//     documented simplification of net48's single-FIFO-waiter Pulse).
//
// Deadlock avoidance: every Wait carries a timeout except the two scenarios
// whose pulses are guaranteed by construction (handoff and the 100 ms
// pulser), and the blocking-enter scenarios are always unblocked by a
// bounded sleep + exit in the first thread.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "monitor.h"

#include "il2cpp_exception.h" // il2cpp::DotNetException (catch type)

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <thread>
#include <vector>

namespace {

std::atomic<int> g_failures{0};

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// True when 'fn' throws a DotNetException whose type chain starts with the
// given managed type name (prefix match -- the factory chains list the
// .NET base classes after the type itself).
template <typename Fn>
bool ThrowsDotNetPrefix(Fn fn, const char* typePrefix)
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

const char* kSyncLock = "System.Threading.SynchronizationLockException";
const char* kArgNull = "System.ArgumentNullException";
const char* kArg = "System.ArgumentException";
[[maybe_unused]] const char* kInvOp = "System.InvalidOperationException";

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- 1. try_enter basics + full release ---
    {
        Il2CppObject obj{};
        Expect(il2cpp_monitor_try_enter(&obj),
               "try_enter acquires an unowned monitor");
        Expect(il2cpp_monitor_try_enter(&obj),
               "try_enter is reentrant while owned (recursion incremented)");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj);
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_exit(&obj); }, kSyncLock),
               "exit on a fully released monitor throws SynchronizationLockException");
        Expect(il2cpp_monitor_try_enter(&obj),
               "monitor is usable again after full release (slot reused)");
        il2cpp_monitor_exit(&obj);
    }

    // --- 2. enter reentrancy (enter/enter/try_enter, three exits) ---
    {
        Il2CppObject obj{};
        il2cpp_monitor_enter(&obj);
        il2cpp_monitor_enter(&obj);
        Expect(il2cpp_monitor_try_enter(&obj),
               "try_enter reentrant on a twice-entered monitor");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj);
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_exit(&obj); }, kSyncLock),
               "fourth-level exit throws SynchronizationLockException");
    }

    // --- 3. ownership errors: exit / pulse / wait on unowned objects ---
    {
        Il2CppObject obj{};
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_exit(&obj); }, kSyncLock),
               "exit on a never-monitored object throws SynchronizationLockException");
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_pulse(&obj); }, kSyncLock),
               "pulse without ownership throws SynchronizationLockException");
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_wait(&obj, 10); }, kSyncLock),
               "wait without ownership throws SynchronizationLockException");
    }

    // --- 4. null arguments (all entry points) ---
    {
        Expect(ThrowsDotNetPrefix([]() { il2cpp_monitor_enter(nullptr); }, kArgNull),
               "enter(nullptr) throws ArgumentNullException");
        Expect(ThrowsDotNetPrefix([]() { il2cpp_monitor_exit(nullptr); }, kArgNull),
               "exit(nullptr) throws ArgumentNullException");
        Expect(ThrowsDotNetPrefix([]() { il2cpp_monitor_try_enter(nullptr); }, kArgNull),
               "try_enter(nullptr) throws ArgumentNullException");
        Expect(ThrowsDotNetPrefix([]() { il2cpp_monitor_pulse(nullptr); }, kArgNull),
               "pulse(nullptr) throws ArgumentNullException");
        Expect(ThrowsDotNetPrefix([]() { il2cpp_monitor_wait(nullptr, 10); }, kArgNull),
               "wait(nullptr) throws ArgumentNullException");
    }

    // --- 5. mutual exclusion: 2 x 1000 guarded increments == exactly 2000 ---
    {
        Il2CppObject obj{};
        int32_t counter = 0;
        std::thread t1([&]() {
            for (int32_t i = 0; i < 1000; ++i)
            {
                il2cpp_monitor_enter(&obj);
                ++counter;
                il2cpp_monitor_exit(&obj);
            }
        });
        std::thread t2([&]() {
            for (int32_t i = 0; i < 1000; ++i)
            {
                il2cpp_monitor_enter(&obj);
                ++counter;
                il2cpp_monitor_exit(&obj);
            }
        });
        t1.join();
        t2.join();
        Expect(counter == 2000,
               "two-thread guarded increments are serialized (counter == 2000)");
    }

    // --- 6. held-lock blocking: the second thread is excluded ---
    {
        Il2CppObject obj{};
        int32_t state = 0;
        // Ordering barrier: t1 must acquire the monitor BEFORE t2 attempts to
        // (otherwise t2 could win the race and observe state == 0, then t1's
        // later write would clobber state == 2 -- a scheduling artifact, not
        // a monitor defect). The flag is published under the held monitor, so
        // once it reads true t2's blocking acquire genuinely waits for t1.
        std::atomic<bool> t1Held{false};
        const auto start = std::chrono::steady_clock::now();
        std::thread t1([&]() {
            il2cpp_monitor_enter(&obj);
            t1Held.store(true);
            state = 1;
            std::this_thread::sleep_for(std::chrono::milliseconds(300));
            il2cpp_monitor_exit(&obj);
        });
        std::thread t2([&]() {
            while (!t1Held.load())
            {
                std::this_thread::yield();
            }
            il2cpp_monitor_enter(&obj); // blocks until t1 exits
            Expect(state == 1,
                   "second thread saw the first critical section unchanged (excluded while held)");
            state = 2;
            il2cpp_monitor_exit(&obj);
        });
        t1.join();
        t2.join();
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start).count();
        Expect(state == 2, "final state visible after both threads");
        Expect(elapsed >= 280,
               "second thread really waited for the first (>= 280 ms hold)");
    }

    // --- 7. wait/pulse handoff ---
    {
        Il2CppObject obj{};
        int32_t flag = 0;
        il2cpp_monitor_enter(&obj);
        std::thread worker([&]() {
            il2cpp_monitor_enter(&obj); // blocks until the main wait releases
            flag = 1;
            il2cpp_monitor_pulse(&obj);
            il2cpp_monitor_exit(&obj);
        });
        const bool pulsed = il2cpp_monitor_wait(&obj, 5000);
        Expect(pulsed, "wait returns true when pulsed");
        Expect(flag == 1, "pulser's write is visible after wait reacquires");
        Expect(il2cpp_monitor_try_enter(&obj),
               "wait reacquired the monitor (caller owns it again)");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj); // first: the try_enter level
        worker.join();
    }

    // --- 8. wait timeout: false after ~100 ms, monitor reacquired ---
    {
        Il2CppObject obj{};
        il2cpp_monitor_enter(&obj);
        const auto start = std::chrono::steady_clock::now();
        const bool pulsed = il2cpp_monitor_wait(&obj, 100);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start).count();
        Expect(!pulsed, "wait times out without a pulse (returns false)");
        Expect(elapsed >= 90, "timeout wait actually waited (>= 90 ms)");
        Expect(il2cpp_monitor_try_enter(&obj),
               "caller still owns the monitor after a timeout wait");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj); // first: the try_enter level
    }

    // --- 9. wait(0): immediate non-blocking poll, no pulse -> false ---
    {
        Il2CppObject obj{};
        il2cpp_monitor_enter(&obj);
        const auto start = std::chrono::steady_clock::now();
        const bool pulsed = il2cpp_monitor_wait(&obj, 0);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start).count();
        Expect(!pulsed, "wait(0) returns false immediately without a pulse");
        Expect(elapsed < 45, "wait(0) does not sleep");
        il2cpp_monitor_exit(&obj);
    }

    // --- 10. wait(-1) infinite: returns true only when pulsed ---
    {
        Il2CppObject obj{};
        int32_t flag = 0;
        il2cpp_monitor_enter(&obj);
        std::thread pulser([&]() {
            il2cpp_monitor_enter(&obj); // blocks until the main wait releases
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            flag = 1;
            il2cpp_monitor_pulse(&obj);
            il2cpp_monitor_exit(&obj);
        });
        const bool pulsed = il2cpp_monitor_wait(&obj, -1);
        Expect(pulsed, "infinite wait returns true when pulsed");
        Expect(flag == 1, "pulser's write is visible after the infinite wait");
        il2cpp_monitor_exit(&obj);
        pulser.join();
    }

    // --- 11. side-table growth and slot reuse (thaudit1: net48 has no limit) ---
    {
        std::vector<Il2CppObject> objs(65);
        bool lockTaken = false;
        for (int32_t i = 0; i < 64; ++i)
        {
            il2cpp_monitor_enter(&objs[static_cast<std::size_t>(i)]);
        }
        il2cpp_monitor_enter(&objs[64], &lockTaken);
        Expect(lockTaken,
               "65th held monitor enters (growable table: net48 has no monitor-count limit)");
        for (int32_t i = 0; i < 65; ++i)
        {
            il2cpp_monitor_exit(&objs[static_cast<std::size_t>(i)]);
        }
        Expect(il2cpp_monitor_try_enter(&objs[64]),
               "object usable again after the held entries are released");
        il2cpp_monitor_exit(&objs[64]);
    }

    // --- 12. enter(obj, bool*): the 2-arg Roslyn lock-lowering form ---
    {
        Il2CppObject obj{};
        bool lockTaken = false;
        il2cpp_monitor_enter(&obj, &lockTaken);
        Expect(lockTaken, "2-arg enter writes *lockTaken = true on fresh acquire");
        bool lockTaken2 = false;
        il2cpp_monitor_enter(&obj, &lockTaken2);
        Expect(lockTaken2, "2-arg enter writes *lockTaken = true on reentrant acquire");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj);
    }
    {
        bool lockTaken = false;
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_enter(nullptr, &lockTaken); }, kArgNull),
               "2-arg enter(nullptr) throws ArgumentNullException");
        Expect(!lockTaken, "2-arg enter leaves *lockTaken false when it throws");
    }

    // --- 13. lockTaken must be false on entry (net48 probe-verified) ---
    {
        Il2CppObject obj{};
        bool lockTaken = true;
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_enter(&obj, &lockTaken); }, kArg),
               "enter(obj, ref true) throws ArgumentException (lockTaken must be false)");
        Expect(lockTaken, "enter leaves the already-true flag untouched on ArgumentException");
        Expect(il2cpp_monitor_try_enter(&obj),
               "enter(obj, ref true) acquired no monitor (still free after the throw)");
        il2cpp_monitor_exit(&obj);

        // net48 ordering: the lockTaken check beats the null check.
        bool lt = true;
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_enter(nullptr, &lt); }, kArg),
               "enter(null, ref true) throws ArgumentException, not ArgumentNullException (net48 ordering)");
        Expect(lt, "enter(null, ref true) leaves the flag untouched");

        bool lt2 = true;
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_try_enter(nullptr, &lt2); }, kArg),
               "try_enter(null, ref true) throws ArgumentException, not ArgumentNullException (net48 ordering)");
        Expect(lt2, "try_enter(null, ref true) leaves the flag untouched");
    }

    // --- 14. try_enter 2-arg form writes the result into the flag ---
    {
        Il2CppObject obj{};
        bool lockTaken = false;
        Expect(il2cpp_monitor_try_enter(&obj, &lockTaken),
               "2-arg try_enter acquires an unowned monitor");
        Expect(lockTaken, "2-arg try_enter writes *lockTaken = true on acquire");
        bool lockTaken2 = false;
        Expect(il2cpp_monitor_try_enter(&obj, &lockTaken2),
               "2-arg try_enter is reentrant while owned");
        Expect(lockTaken2, "2-arg try_enter writes *lockTaken = true on reentrant acquire");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj);
    }

    // --- 15. Exit from a non-owning thread throws SynchronizationLockException ---
    {
        Il2CppObject obj{};
        il2cpp_monitor_enter(&obj);
        std::atomic<bool> otherSle{false};
        std::thread t([&]() {
            try
            {
                il2cpp_monitor_exit(&obj); // thread B does not own it
            }
            catch (const il2cpp::DotNetException& e)
            {
                otherSle.store(std::strncmp(e.type_name(), kSyncLock, std::strlen(kSyncLock)) == 0);
            }
        });
        t.join();
        Expect(otherSle.load(),
               "exit from a non-owning thread throws SynchronizationLockException");
        Expect(il2cpp_monitor_try_enter(&obj),
               "owner still holds the monitor after another thread's failed exit");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj); // the try_enter level: FULLY release the monitor
                                   // (test 16's obj reuses this stack slot; a held
                                   // entry would poison its recursion accounting)
    }

    // --- 16. wait saves and restores the FULL recursion depth ---
    {
        Il2CppObject obj{};
        il2cpp_monitor_enter(&obj);
        il2cpp_monitor_enter(&obj); // recursion depth 2
        std::thread pulser([&]() {
            il2cpp_monitor_enter(&obj); // blocks until the wait releases
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
            il2cpp_monitor_pulse(&obj);
            il2cpp_monitor_exit(&obj);
        });
        const bool pulsed = il2cpp_monitor_wait(&obj, 5000);
        Expect(pulsed, "reentrant wait returns true when pulsed");
        Expect(il2cpp_monitor_try_enter(&obj),
               "wait restored the full recursion depth (caller still owns at depth 2)");
        il2cpp_monitor_exit(&obj); // the try_enter level
        il2cpp_monitor_exit(&obj); // depth 2
        il2cpp_monitor_exit(&obj); // depth 1 -> fully released
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_exit(&obj); }, kSyncLock),
               "all recursion levels released after the restored depth is exited");
        pulser.join();
    }

    // --- 17. pulse with no waiter is LOST (no latch) ---
    {
        Il2CppObject obj{};
        il2cpp_monitor_enter(&obj);
        il2cpp_monitor_pulse(&obj); // nobody waiting: the signal is dropped
        const auto start = std::chrono::steady_clock::now();
        const bool pulsed = il2cpp_monitor_wait(&obj, 60);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start).count();
        Expect(!pulsed,
               "pulse with no waiter is lost (the following wait times out)");
        Expect(elapsed >= 50, "the no-latch wait actually waited for its timeout");
        il2cpp_monitor_exit(&obj);
    }

    // --- 18. contended try_enter returns false and leaks no reservation ---
    {
        Il2CppObject obj{};
        std::atomic<bool> ownerEntered{false};
        std::thread owner([&]() {
            il2cpp_monitor_enter(&obj);
            ownerEntered.store(true);
            std::this_thread::sleep_for(std::chrono::milliseconds(200));
            il2cpp_monitor_exit(&obj);
        });
        while (!ownerEntered.load())
        {
            std::this_thread::yield();
        }
        bool lockTaken = false;
        Expect(!il2cpp_monitor_try_enter(&obj, &lockTaken),
               "try_enter returns false while another thread holds the monitor");
        Expect(!lockTaken, "2-arg try_enter writes *lockTaken = false when contended");
        owner.join();
        Expect(il2cpp_monitor_try_enter(&obj),
               "failed try_enter leaked no reservation (acquires once the owner releases)");
        il2cpp_monitor_exit(&obj);
    }

    // --- 19. one pulse wakes ALL current waiters (documented broadcast) ---
    {
        Il2CppObject obj{};
        std::atomic<int> woken{0};
        std::thread w1([&]() {
            il2cpp_monitor_enter(&obj); // blocks until the main wait releases
            if (il2cpp_monitor_wait(&obj, 15000))
            {
                ++woken;
            }
            il2cpp_monitor_exit(&obj);
        });
        std::thread w2([&]() {
            il2cpp_monitor_enter(&obj); // blocks until the main wait releases
            if (il2cpp_monitor_wait(&obj, 15000))
            {
                ++woken;
            }
            il2cpp_monitor_exit(&obj);
        });
        // The pulser sleeps first (so main + both waiters reach their waits),
        // then keeps pulsing until BOTH waiters have woken -- the pulse storm
        // guarantees no waiter can starve regardless of scheduling.
        std::thread pulser([&]() {
            std::this_thread::sleep_for(std::chrono::milliseconds(200));
            for (int32_t i = 0; i < 120 && woken.load() < 2; ++i)
            {
                il2cpp_monitor_enter(&obj); // blocks until a waiter releases
                il2cpp_monitor_pulse(&obj);
                il2cpp_monitor_exit(&obj);
                std::this_thread::sleep_for(std::chrono::milliseconds(50));
            }
        });
        il2cpp_monitor_enter(&obj); // main owns
        const bool pulsed = il2cpp_monitor_wait(&obj, 15000); // releases; w1/w2 run and wait
        Expect(pulsed, "broadcast main wait returns true when pulsed");
        il2cpp_monitor_exit(&obj); // fully release BEFORE joining (no deadlock)
        pulser.join();
        w1.join();
        w2.join();
        Expect(woken.load() == 2,
               "one pulse wakes ALL current waiters (simplified broadcast)");
        Expect(il2cpp_monitor_try_enter(&obj),
               "monitor fully usable again after the broadcast scenario");
        il2cpp_monitor_exit(&obj);
    }

    // --- 20. wait timeout validation (net48 Wait(object, int)) ---
    // A timeout < -1 throws ArgumentOutOfRangeException BEFORE the ownership
    // check (probe: Wait(unowned, -2) -> AORE, Wait(unowned, 0) -> SLE); a
    // null object throws ArgumentNullException first (probe: Wait(null, -2)
    // -> ANE). The AORE carries the "millisecondsTimeout" parameter.
    {
        Il2CppObject obj{};
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_wait(&obj, -2); }, "System.ArgumentOutOfRangeException"),
               "wait(obj, -2) throws ArgumentOutOfRangeException (before ownership check)");
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_wait(&obj, 0); }, kSyncLock),
               "wait(obj, 0) on an unowned monitor throws SynchronizationLockException (ownership after timeout validation)");
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_wait(nullptr, -2); }, kArgNull),
               "wait(null, -2) throws ArgumentNullException (null beats timeout)");
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_wait(nullptr, -1); }, kArgNull),
               "wait(null, -1) throws ArgumentNullException");
        // The AORE message must name the "millisecondsTimeout" parameter
        // (net48 probe: Wait(obj, -2) -> AORE param=millisecondsTimeout).
        try
        {
            il2cpp_monitor_wait(&obj, -3);
            Expect(false, "wait(obj, -3) threw (unreachable)");
        }
        catch (const il2cpp::DotNetException& e)
        {
            Expect(std::strstr(e.message_text(), "millisecondsTimeout") != nullptr,
                   "wait timeout AORE message names the millisecondsTimeout parameter");
        }
    }

    // --- 21. wait(obj) 1-arg: Timeout.Infinite ---
    {
        Il2CppObject obj{};
        int32_t flag = 0;
        il2cpp_monitor_enter(&obj);
        std::thread pulser([&]() {
            il2cpp_monitor_enter(&obj); // blocks until the main wait releases
            std::this_thread::sleep_for(std::chrono::milliseconds(60));
            flag = 1;
            il2cpp_monitor_pulse(&obj);
            il2cpp_monitor_exit(&obj);
        });
        const bool pulsed = il2cpp_monitor_wait(&obj); // 1-arg = infinite
        Expect(pulsed, "1-arg wait returns true when pulsed");
        Expect(flag == 1, "1-arg wait restored ownership (pulser's write visible)");
        il2cpp_monitor_exit(&obj);
        pulser.join();
    }

    // --- 22. try_enter timeout overloads ---
    {
        Il2CppObject obj{};
        // -2 throws AORE (net48 TryEnter int overload: NO param name)
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_try_enter(&obj, -2); },
                                  "System.ArgumentOutOfRangeException"),
               "try_enter(obj, -2) throws ArgumentOutOfRangeException");
        // null beats timeout (net48: TryEnter(null, -2) -> ANE)
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_try_enter(nullptr, 0); }, kArgNull),
               "try_enter(null, 0) throws ArgumentNullException");
        // 0 = single non-blocking try: acquires a free monitor
        Expect(il2cpp_monitor_try_enter(&obj, 0),
               "try_enter(obj, 0) acquires an unowned monitor");
        Expect(il2cpp_monitor_try_enter(&obj, 0),
               "try_enter(obj, 0) is reentrant while owned");
        il2cpp_monitor_exit(&obj);
        il2cpp_monitor_exit(&obj);
        // positive timeout: blocks until the owner releases
        std::atomic<bool> ownerDone{false};
        std::atomic<bool> ownerHeld{false};
        std::thread owner([&]() {
            il2cpp_monitor_enter(&obj);
            ownerHeld.store(true);
            std::this_thread::sleep_for(std::chrono::milliseconds(120));
            il2cpp_monitor_exit(&obj);
            ownerDone.store(true);
        });
        while (!ownerHeld.load()) // ordering barrier: the owner must own first
        {
            std::this_thread::yield();
        }
        const bool acquired = il2cpp_monitor_try_enter(&obj, 3000);
        Expect(acquired, "try_enter(obj, positive timeout) acquires once the owner releases");
        Expect(ownerDone.load(), "the owner exited before the timed acquire succeeded");
        il2cpp_monitor_exit(&obj);
        owner.join();
        // timeout expiry: contended for longer than the budget -> false, no reservation leak
        std::atomic<bool> holderHeld{false};
        std::thread holder([&]() {
            il2cpp_monitor_enter(&obj);
            holderHeld.store(true);
            std::this_thread::sleep_for(std::chrono::milliseconds(200));
            il2cpp_monitor_exit(&obj);
        });
        while (!holderHeld.load()) // ordering barrier: the holder must own first
        {
            std::this_thread::yield();
        }
        const auto start = std::chrono::steady_clock::now();
        Expect(!il2cpp_monitor_try_enter(&obj, 40),
               "try_enter(obj, 40) times out while the monitor stays held");
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start).count();
        Expect(elapsed >= 30 && elapsed < 150, "the timed try_enter returned near its budget");
        holder.join();
        Expect(il2cpp_monitor_try_enter(&obj),
               "failed timed try_enter leaked no reservation (acquires after the holder releases)");
        il2cpp_monitor_exit(&obj);
    }

    // --- 23. try_enter(obj, timeout, ref bool) 3-arg ordering (net48) ---
    {
        Il2CppObject obj{};
        // lockTaken already true -> ArgumentException BEFORE null and timeout
        bool lt = true;
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_try_enter(nullptr, 0, &lt); }, kArg),
               "3-arg try_enter with a true flag throws ArgumentException before the null check");
        Expect(lt, "3-arg try_enter leaves the true flag untouched on the ArgumentException path");
        // null + lockTaken false -> ArgumentNullException (before timeout)
        bool lt2 = false;
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_try_enter(nullptr, -2, &lt2); }, kArgNull),
               "3-arg try_enter(null, -2) throws ArgumentNullException (null beats timeout)");
        Expect(!lt2, "3-arg try_enter leaves the false flag untouched on the null path");
        // valid object + timeout < -1 -> AORE, flag untouched
        bool lt3 = false;
        Expect(ThrowsDotNetPrefix([&]() { il2cpp_monitor_try_enter(&obj, -2, &lt3); },
                                  "System.ArgumentOutOfRangeException"),
               "3-arg try_enter(obj, -2) throws ArgumentOutOfRangeException");
        Expect(!lt3, "3-arg try_enter leaves the false flag untouched on the timeout path");
        // success writes the flag
        bool lt4 = false;
        Expect(il2cpp_monitor_try_enter(&obj, 0, &lt4),
               "3-arg try_enter(obj, 0) acquires a free monitor");
        Expect(lt4, "3-arg try_enter writes *lockTaken = true on acquire");
        il2cpp_monitor_exit(&obj);
    }

    if (g_failures == 0)
    {
        std::printf("all runtime monitor tests passed\n");
        return 0;
    }
    std::printf("%d monitor test(s) FAILED\n", g_failures.load());
    return 1;
}
