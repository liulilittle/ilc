//------------------------------------------------------------------------------
// thread.cpp -- System.Threading.Thread icall implementations (see
// il2cpp_thread.h for the contract).
//
// Portability strategy: the Windows branch uses the Win32 primitives
// directly (::Sleep / ::SwitchToThread -- available in both MSVC and
// MinGW-w64). The POSIX branch uses std::this_thread::sleep_for (<thread>)
// and ::sched_yield (<sched.h>). The current-thread-id query is delegated
// to plat_thread_current_id (platform.h): the platform layer owns the
// per-OS selection (SYS_gettid / pthread_threadid_np / getthrid /
// GetCurrentThreadId / stable hash) -- see threadobj.cpp for the same
// migration precedent.
//
// net48 semantics verified against the threadcase reference run (see the
// header): a negative sleep argument throws ArgumentOutOfRangeException
// (every negative value, including -1 -- net48 would block forever on -1
// because it treats it as Timeout.Infinite, but the simplified contract
// rejects it, see the e2e case comment); Sleep(0) yields without sleeping.
//------------------------------------------------------------------------------

#include "il2cpp_thread.h"

#include "il2cpp_exception_ext.h"
#include "platform.h" // plat_thread_current_id (the platform layer owns the OS-tid selection)

#include <atomic> // std::atomic_thread_fence
#include <chrono>
#include <cstdint>
#include <thread>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#else
    #include <sched.h> // ::sched_yield
#endif

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// icall_thread_sleep -- Thread.Sleep(int32).
//------------------------------------------------------------------------------
void icall_thread_sleep(int32_t milliseconds)
{
    if (milliseconds < 0)
    {
        // net48: a negative value throws ArgumentOutOfRangeException. The
        // simplified contract rejects EVERY negative value (including -1,
        // which net48 would treat as Timeout.Infinite and block forever).
        il2cpp_throw_argument_out_of_range_exception("milliseconds", nullptr);
    }
    if (milliseconds == 0)
    {
        icall_thread_sleep_zero(); // Sleep(0): yield, do not sleep
        return;
    }
#if defined(_WIN32)
    ::Sleep(static_cast<DWORD>(milliseconds));
#else
    std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds));
#endif
}

//------------------------------------------------------------------------------
// icall_thread_sleep_zero -- Thread.Sleep(0): yield the time slice.
//------------------------------------------------------------------------------
void icall_thread_sleep_zero()
{
#if defined(_WIN32)
    ::Sleep(0);
#else
    // sched_yield returns EAGAIN when the calling thread is the only one
    // runnable -- for Sleep(0) semantics the call itself is the contract.
    (void)::sched_yield();
#endif
}

//------------------------------------------------------------------------------
// icall_thread_get_current_thread_id -- SIMPLIFIED ManagedThreadId.
//------------------------------------------------------------------------------
int32_t icall_thread_get_current_thread_id()
{
    // OS thread id -- the platform layer owns the per-platform selection
    // (GetCurrentThreadId on Windows, SYS_gettid on Linux/Android,
    // pthread_threadid_np on Apple, getthrid on OpenBSD, a stable hash on
    // other POSIX). Centralizing it here removes the duplicate
    // implementations in monitor.cpp and pathproc.cpp and fixes the Apple/
    // OpenBSD cases where truncating pthread_self to int32 is wrong.
    return plat_thread_current_id();
}

//------------------------------------------------------------------------------
// icall_thread_yield -- Thread.Yield(): let other threads run.
//------------------------------------------------------------------------------
void icall_thread_yield()
{
#if defined(_WIN32)
    // SwitchToThread returns FALSE when no other thread was ready; the
    // managed bool is not modeled -- the call is the contract.
    (void)::SwitchToThread();
#else
    (void)::sched_yield();
#endif
}

//------------------------------------------------------------------------------
// icall_thread_get_priority -- always ThreadPriority.Normal (0).
//------------------------------------------------------------------------------
int32_t icall_thread_get_priority()
{
    return 0; // ThreadPriority.Normal (the single-threaded model)
}

//------------------------------------------------------------------------------
// Critical-region markers -- no-op (see the header comment).
//
// net48 probe (csc 4.0.30319 x64): BeginCriticalRegion/EndCriticalRegion on
// the main thread AND on a spawned worker both return without throwing and
// leave no managed-visible state (the counter only affects CLR reliability
// escalation, which this runtime does not model). The calls are therefore the
// whole contract here -- a no-op is the measured behavior, not an assumption.
//------------------------------------------------------------------------------
void icall_thread_begin_critical_region()
{
}

void icall_thread_end_critical_region()
{
}

//------------------------------------------------------------------------------
// icall_thread_memory_barrier -- Thread.MemoryBarrier(): a full (seq_cst)
// memory fence. Orders ALL memory accesses around it -- reads/writes before
// the call stay before, reads/writes after stay after. C++11
// std::atomic_thread_fence is portable across every platform (no OS API);
// the reference netcore implementation delegates to
// Interlocked.MemoryBarrier, which is the same guarantee.
//------------------------------------------------------------------------------
void icall_thread_memory_barrier()
{
    std::atomic_thread_fence(std::memory_order_seq_cst);
}

//------------------------------------------------------------------------------
// icall_thread_get_current_processor_number -- Thread.GetCurrentProcessorNumber()
// (the net5+ audit gap): the CPU id the CURRENT thread is running on.
// Win32 GetCurrentProcessorNumber(); Linux glibc sched_getcpu() (a macro/
// vDSO call, no syscall wrapper needed). Cross-platform consistency: both
// answer the same contract; the numeric range differs by host topology,
// which is an environment property, not a runtime divergence.
//------------------------------------------------------------------------------
int32_t icall_thread_get_current_processor_number()
{
#if defined(_WIN32)
    return static_cast<int32_t>(::GetCurrentProcessorNumber());
#elif defined(__linux__)
    return static_cast<int32_t>(::sched_getcpu());
#else
    // Generic POSIX fallback: no portable CPU-id query -- report 0 (the
    // documented single-processor model) rather than fail.
    return 0;
#endif
}

//------------------------------------------------------------------------------
// icall_thread_get_is_thread_pool_thread -- Thread.IsThreadPoolThread.
// Returns whether the CURRENT executing thread is a ThreadPool worker. The
// `self` receiver is accepted and IGNORED: pool membership is tracked per
// OS thread (a thread_local flag registered at each worker's entry in
// threadpool.cpp -- platform-independent C++11), NOT per Thread object
// (documented deviation in the header; net48 reads the `self` object's own
// state, and the model has no per-object pool tracking). Main and spawned
// threads report false; pool workers report true.
//------------------------------------------------------------------------------
bool icall_thread_get_is_thread_pool_thread(Il2CppObject* /*self*/)
{
    return il2cpp_threadpool_is_current_thread_worker();
}

//------------------------------------------------------------------------------
// icall_thread_begin_thread_affinity / end_thread_affinity --
// Thread.BeginThreadAffinity() / EndThreadAffinity(): no-ops.
//
// net48 probe (csc 4.0.30319 x64): both calls on the main thread and on a
// spawned worker return without throwing and without any managed-visible
// state change; the marker only tells the CLR not to migrate the thread
// mid-region. This runtime runs every managed thread on one OS thread and
// never migrates it, so there is no state for the marker to guard -- the
// no-op preserves the measured contract.
//------------------------------------------------------------------------------
void icall_thread_begin_thread_affinity()
{
}

void icall_thread_end_thread_affinity()
{
}

//------------------------------------------------------------------------------
// icall_thread_disable_com_object_eager_cleanup --
// Thread.DisableComObjectEagerCleanup(): no-op.
//
// net48 probe (csc 4.0.30319 x64): the call returns without throwing on the
// main thread and on a spawned worker. Its only effect is to stop the CLR
// from eagerly releasing this thread's COM objects; this runtime has no COM
// object model (no RCW/CCW teardown), so there is nothing to disable. The
// `self` receiver is accepted and ignored.
//------------------------------------------------------------------------------
void icall_thread_disable_com_object_eager_cleanup(Il2CppObject* /*self*/)
{
}

}} // namespace il2cpp::runtime
