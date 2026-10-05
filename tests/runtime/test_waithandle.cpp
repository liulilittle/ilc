//------------------------------------------------------------------------------
// test_waithandle.cpp -- System.Threading wait-handle runtime unit tests
// (Semaphore / Mutex / EventWaitHandle).
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in il2cpp_waithandle.h:
//   1. Semaphore ctor validation: initialCount in [0, maximumCount],
//      maximumCount >= 1; out-of-range throws ArgumentOutOfRangeException.
//   2. Semaphore WaitOne/Release: WaitOne decrements, Release increments and
//      returns the PREVIOUS count; Release beyond maximumCount throws
//      SemaphoreFullException; Release(0) throws ArgumentOutOfRangeException.
//   3. Mutex WaitOne/ReleaseMutex round-trip.
//   4. EventWaitHandle Set/Reset/WaitOne: WaitOne blocks while unsignaled;
//      auto-reset clears the signal on a successful wait, manual-reset keeps
//      it set.
//   5. Two-thread synchronization: a worker thread blocks in WaitOne on a
//      semaphore; the main thread releases it; the worker proceeds and sets a
//      completion flag (read after join -- the threadsync2 worker contract:
//      no allocation on the worker).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_waithandle.h"
#include "il2cpp_exception.h" // DotNetException
#include "il2cpp_object.h"    // il2cpp_object_new
#include "il2cpp_runtime.h"   // il2cpp_object_new

#include <atomic>
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

// Runs fn; returns true iff it throws a DotNetException whose type chain
// contains 'typeFragment' (e.g. "System.ArgumentOutOfRangeException").
template <typename Fn>
bool ThrowsWithType(Fn fn, const char* typeFragment)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeFragment) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

il2cpp::Il2CppObject* NewSemaphore()
{
    return il2cpp::il2cpp_object_new(&System_Threading_Semaphore_class);
}

il2cpp::Il2CppObject* NewMutex()
{
    return il2cpp::il2cpp_object_new(&System_Threading_Mutex_class);
}

il2cpp::Il2CppObject* NewEvent()
{
    return il2cpp::il2cpp_object_new(&System_Threading_EventWaitHandle_class);
}

} // namespace

int main()
{
    // --- 1. Semaphore ctor validation ---
    {
        Expect(ThrowsWithType([&] {
                   il2cpp::Il2CppObject* s = NewSemaphore();
                   il2cpp::runtime::icall_waithandle_semaphore_ctor(s, 2, 1);
               }, "System.ArgumentOutOfRangeException"),
               "initialCount > maximumCount throws ArgumentOutOfRangeException");
        Expect(ThrowsWithType([&] {
                   il2cpp::Il2CppObject* s = NewSemaphore();
                   il2cpp::runtime::icall_waithandle_semaphore_ctor(s, -1, 1);
               }, "System.ArgumentOutOfRangeException"),
               "negative initialCount throws ArgumentOutOfRangeException");
        Expect(ThrowsWithType([&] {
                   il2cpp::Il2CppObject* s = NewSemaphore();
                   il2cpp::runtime::icall_waithandle_semaphore_ctor(s, 0, 0);
               }, "System.ArgumentOutOfRangeException"),
               "maximumCount < 1 throws ArgumentOutOfRangeException");
        // Valid ctor: initial 0, max 1.
        {
            il2cpp::Il2CppObject* s = NewSemaphore();
            il2cpp::runtime::icall_waithandle_semaphore_ctor(s, 0, 1);
            Expect(true, "valid semaphore ctor (0, 1) succeeds");
        }
    }

    // --- 2. Semaphore WaitOne/Release ---
    {
        il2cpp::Il2CppObject* s = NewSemaphore();
        il2cpp::runtime::icall_waithandle_semaphore_ctor(s, 1, 2);
        Expect(il2cpp::runtime::icall_waithandle_wait_one(s),
               "WaitOne on a count-1 semaphore succeeds");
        Expect(il2cpp::runtime::icall_waithandle_semaphore_release(s) == 0,
               "Release returns the previous count (0)");
        Expect(il2cpp::runtime::icall_waithandle_semaphore_release(s) == 1,
               "second Release returns the previous count (1)");
        // Release beyond max (2) throws SemaphoreFullException.
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::icall_waithandle_semaphore_release(s);
               }, "System.Threading.SemaphoreFullException"),
               "Release beyond maximumCount throws SemaphoreFullException");
        // Release(0) throws ArgumentOutOfRangeException.
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::icall_waithandle_semaphore_release_n(s, 0);
               }, "System.ArgumentOutOfRangeException"),
               "Release(0) throws ArgumentOutOfRangeException");
        // Release(2) from count 2 exceeds max 2 -> SemaphoreFullException.
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::icall_waithandle_semaphore_release_n(s, 2);
               }, "System.Threading.SemaphoreFullException"),
               "Release(2) beyond maximumCount throws SemaphoreFullException");
    }

    // --- 3. Mutex WaitOne/ReleaseMutex round-trip ---
    {
        il2cpp::Il2CppObject* m = NewMutex();
        il2cpp::runtime::icall_waithandle_mutex_ctor(m);
        Expect(il2cpp::runtime::icall_waithandle_wait_one(m),
               "Mutex WaitOne acquires");
        il2cpp::runtime::icall_waithandle_mutex_release(m);
        Expect(true, "Mutex ReleaseMutex releases");
        Expect(il2cpp::runtime::icall_waithandle_wait_one(m),
               "Mutex WaitOne re-acquires after release");
        il2cpp::runtime::icall_waithandle_mutex_release(m);
    }

    // --- 4. EventWaitHandle Set/Reset/WaitOne ---
    {
        // Auto-reset: WaitOne clears the signal.
        il2cpp::Il2CppObject* e = NewEvent();
        il2cpp::runtime::icall_waithandle_event_ctor(e, true, 0); // AutoReset, signaled
        Expect(il2cpp::runtime::icall_waithandle_wait_one(e),
               "auto-reset event WaitOne on a signaled event succeeds");
        // After the auto-reset wait, the signal is cleared -- a second
        // WaitOne would block, so verify via Reset/Set round-trip instead.
        il2cpp::runtime::icall_waithandle_event_reset(e);
        il2cpp::runtime::icall_waithandle_event_set(e);
        Expect(il2cpp::runtime::icall_waithandle_wait_one(e),
               "auto-reset event WaitOne after Set succeeds");
        // Manual-reset: WaitOne does NOT clear the signal.
        il2cpp::Il2CppObject* m = NewEvent();
        il2cpp::runtime::icall_waithandle_event_ctor(m, true, 1); // ManualReset, signaled
        Expect(il2cpp::runtime::icall_waithandle_wait_one(m),
               "manual-reset event WaitOne succeeds");
        Expect(il2cpp::runtime::icall_waithandle_wait_one(m),
               "manual-reset event WaitOne succeeds again (signal not cleared)");
        il2cpp::runtime::icall_waithandle_event_reset(m);
        // Invalid mode throws.
        Expect(ThrowsWithType([&] {
                   il2cpp::Il2CppObject* bad = NewEvent();
                   il2cpp::runtime::icall_waithandle_event_ctor(bad, false, 7);
               }, "System.ArgumentOutOfRangeException"),
               "invalid EventResetMode throws ArgumentOutOfRangeException");
    }

    // --- 5. Two-thread synchronization (semaphore) ---
    {
        il2cpp::Il2CppObject* s = NewSemaphore();
        il2cpp::runtime::icall_waithandle_semaphore_ctor(s, 0, 1);
        std::atomic<int> g_entered{0};
        std::atomic<int> g_done{0};
        std::thread worker([&] {
            // Worker contract: no managed allocation -- only WaitOne + flags.
            if (il2cpp::runtime::icall_waithandle_wait_one(s))
            {
                g_entered.store(1);
            }
            g_done.store(1);
        });
        // The worker is blocked (count 0); give it a moment to reach WaitOne.
        for (int i = 0; i < 2000 && g_done.load() == 0; ++i)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        Expect(g_entered.load() == 0,
               "worker is blocked before the release (entered flag still 0)");
        il2cpp::runtime::icall_waithandle_semaphore_release(s);
        worker.join();
        Expect(g_entered.load() == 1,
               "worker proceeds after the release (entered flag set)");
        Expect(g_done.load() == 1,
               "worker completes after the release");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d waithandle assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime waithandle tests passed\n");
    return 0;
}
