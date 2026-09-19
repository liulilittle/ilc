//------------------------------------------------------------------------------
// monitor.cpp -- System.Threading.Monitor (lock semantics) implementations
// (see monitor.h for the contract).
//
// The object header carries no sync state (klass pointer only, il2cpp_object.h),
// so every monitor lives in a GLOBAL SIDE TABLE: a fixed array of entries
// keyed by Il2CppObject*. Table mechanics (correctness first, not optimal):
//
//   * One global CRITICAL_SECTION (pthread_mutex on POSIX) guards the table
//     structure: slot allocation/freeing, the object-key lookup, and every
//     entry's state fields (owner thread id, recursion, refs, waiter count,
//     pulse generation). All these critical sections are SHORT.
//   * The only blocking operation -- acquiring an entry's monitor lock -- is
//     performed with the table lock RELEASED: the caller first reserves the
//     entry with a reference count under the table lock, then blocks on the
//     entry's own lock, then re-takes the table lock to publish ownership.
//
//   Lock ordering: the table lock and an entry lock are NEVER nested. The
//   table lock is only ever held alone (all acquires of an entry lock happen
//   with it released, and it is never taken while an entry lock is held),
//   so the lock-order graph has no edges and no cycles -- deadlock-free.
//
//   Entry lifetime: an entry exists while refs > 0. Every thread that blocks
//   on an entry's lock, or is currently inside it, holds a reservation
//   (refs >= 1); the monitor's own hold contributes one ref, released by the
//   Exit that drops the recursion counter to zero. A ref count of zero
//   therefore proves no thread is waiting on or inside the lock, and the
//   entry can be freed safely.
//
// Wait/pulse: no native condition variables -- il2cpp_monitor_wait releases
// the monitor (all recursion levels), then POLLS a per-entry pulse
// generation counter every few milliseconds until a Pulse bumps it (or the
// timeout expires), then reacquires the monitor and restores the recursion
// depth. Pulse bumps the generation only when at least one thread is
// waiting. SIMPLIFIED: one Pulse wakes ALL waiters (net48 wakes a single
// FIFO waiter); the wait loop therefore never consumes a pulse selectively.
//
// Timeout validation (net48 Wait(object, int), probe rtaud7): a timeout < -1
// throws ArgumentOutOfRangeException (param "millisecondsTimeout") BEFORE the
// ownership check -- exactly -1 is the only infinite sentinel. The
// timeout-based TryEnter(obj, timeoutMs) follows net48: < -1 -> AORE with NO
// parameter name, 0 = single non-blocking try, -1 = block forever.
//------------------------------------------------------------------------------

#include "monitor.h"

#include "il2cpp_exception.h"     // il2cpp_throw_new (SynchronizationLockException carrier)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception / invalid_operation
#include "platform.h"             // plat_thread_current_id (OS thread id)

#include <chrono>
#include <cstdint>
#include <cstdlib> // std::abort (defensive fallback in ThrowSynchronizationLockException)
#include <thread>
#include <vector>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#else
    #include <pthread.h>
#endif

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// GROWABLE side table (thaudit1): net48 has no monitor-count limit, so the
// table grows on demand. Entries are heap-allocated so their addresses stay
// STABLE while the table grows (a blocked thread holds an entry pointer past
// the table lock -- see the header comment); free slots (obj == nullptr) are
// reused and never deleted.
//------------------------------------------------------------------------------

// Poll interval of the wait loop (pulse-detection latency bound).
constexpr int32_t kWaitPollMs = 5;

struct MonitorEntry
{
    Il2CppObject* obj;      // table key; nullptr = free slot (table-lock guarded)
#if defined(_WIN32)
    CRITICAL_SECTION cs;    // the monitor lock itself
#else
    pthread_mutex_t cs;
#endif
    int32_t ownerThreadId;  // 0 = not owned (table-lock guarded)
    int32_t recursion;      // .NET reentrancy depth (table-lock guarded)
    int32_t refs;           // reservation count (table-lock guarded)
    int32_t waiterCount;    // threads inside il2cpp_monitor_wait (table-lock guarded)
    int32_t pulseGen;       // bumped by every Pulse that finds a waiter (table-lock guarded)
};

std::vector<MonitorEntry*> g_entries;

#if defined(_WIN32)
CRITICAL_SECTION g_tableLock;

// CRITICAL_SECTION has no static initializer: a static object runs
// InitializeCriticalSection before main and deletes it at process exit.
namespace {
struct TableLockInit
{
    TableLockInit() { ::InitializeCriticalSection(&g_tableLock); }
    ~TableLockInit() { ::DeleteCriticalSection(&g_tableLock); }
};
const TableLockInit g_tableLockInit;
}
#else
pthread_mutex_t g_tableLock = PTHREAD_MUTEX_INITIALIZER;
#endif

//------------------------------------------------------------------------------
// Lock wrappers (Windows CRITICAL_SECTION / POSIX pthread_mutex).
//------------------------------------------------------------------------------
void InitEntryLock(MonitorEntry& e)
{
#if defined(_WIN32)
    ::InitializeCriticalSection(&e.cs);
#else
    (void)pthread_mutex_init(&e.cs, nullptr);
#endif
}

void DestroyEntryLock(MonitorEntry& e)
{
#if defined(_WIN32)
    ::DeleteCriticalSection(&e.cs);
#else
    (void)pthread_mutex_destroy(&e.cs);
#endif
}

void LockTable()
{
#if defined(_WIN32)
    ::EnterCriticalSection(&g_tableLock);
#else
    (void)pthread_mutex_lock(&g_tableLock);
#endif
}

void UnlockTable()
{
#if defined(_WIN32)
    ::LeaveCriticalSection(&g_tableLock);
#else
    (void)pthread_mutex_unlock(&g_tableLock);
#endif
}

// RAII for the table lock: every throw path (SynchronizationLockException,
// ArgumentNullException, table exhaustion) unwinds through the destructor.
class TableGuard
{
public:
    TableGuard() { LockTable(); }
    ~TableGuard() { UnlockTable(); }
};

void LockEntry(MonitorEntry& e)
{
#if defined(_WIN32)
    ::EnterCriticalSection(&e.cs);
#else
    (void)pthread_mutex_lock(&e.cs);
#endif
}

bool TryLockEntry(MonitorEntry& e)
{
#if defined(_WIN32)
    return ::TryEnterCriticalSection(&e.cs) != 0;
#else
    return pthread_mutex_trylock(&e.cs) == 0;
#endif
}

void UnlockEntry(MonitorEntry& e)
{
#if defined(_WIN32)
    ::LeaveCriticalSection(&e.cs);
#else
    (void)pthread_mutex_unlock(&e.cs);
#endif
}

//------------------------------------------------------------------------------
// Thread identity -- delegated to the platform layer (the same mechanism as
// thread.cpp/pathproc.cpp; the layer owns the per-OS selection -- see
// plat_thread_current_id in platform.h).
//------------------------------------------------------------------------------
int32_t CurrentThreadId()
{
    return plat_thread_current_id();
}

//------------------------------------------------------------------------------
// Table lookups -- all called with the table lock HELD.
//------------------------------------------------------------------------------

// Lookup only; nullptr when the object has no entry (never held/waited on).
MonitorEntry* FindEntry(Il2CppObject* obj)
{
    for (size_t i = 0; i < g_entries.size(); ++i)
    {
        if (g_entries[i]->obj == obj)
        {
            return g_entries[i];
        }
    }
    return nullptr;
}

// Lookup or lazily allocate; never nullptr (the table grows on demand). The
// returned entry carries refs == 0; the caller must reserve it (++refs)
// before releasing the table lock.
MonitorEntry* FindOrCreateEntry(Il2CppObject* obj)
{
    MonitorEntry* existing = FindEntry(obj);
    if (existing != nullptr)
    {
        return existing;
    }
    for (size_t i = 0; i < g_entries.size(); ++i)
    {
        MonitorEntry* e = g_entries[i];
        if (e->obj == nullptr)
        {
            InitEntryLock(*e);
            e->obj = obj;
            e->ownerThreadId = 0;
            e->recursion = 0;
            e->refs = 0;
            e->waiterCount = 0;
            e->pulseGen = 0;
            return e;
        }
    }
    MonitorEntry* fresh = new MonitorEntry();
    InitEntryLock(*fresh);
    fresh->obj = obj;
    fresh->ownerThreadId = 0;
    fresh->recursion = 0;
    fresh->refs = 0;
    fresh->waiterCount = 0;
    fresh->pulseGen = 0;
    g_entries.push_back(fresh);
    return fresh;
}

// Drop a reservation; at zero the entry is freed (no thread can be waiting
// on or inside the lock, see the header comment). Table lock HELD.
void ReleaseRef(MonitorEntry& e)
{
    if (--e.refs == 0)
    {
        DestroyEntryLock(e);
        e.obj = nullptr;
    }
}

//------------------------------------------------------------------------------
// Throw helpers.
//------------------------------------------------------------------------------
[[noreturn]] void ThrowSynchronizationLockException()
{
    // D-MON-3: the REAL managed full name is System.Threading.Synchronization-
    // LockException (the transpiler emits catch clauses from metadata full
    // names -- OpenCatch/CodeGenerator.cs TypeTokenFullName -- so a typed
    // catch (SynchronizationLockException) matches only against this chain).
    il2cpp_throw_new(
        "System.Threading.SynchronizationLockException,"
        "System.Threading.SynchronizationLockException,"
        "System.SystemException,System.Object,System.Exception",
        "Object synchronization method was called from an unsynchronized block of code.");
    // il2cpp_throw_new throws a C++ exception and never returns; the abort
    // is a defensive fallback that also silences -Winvalid-noreturn.
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort();
#endif
}

} // namespace

// net48: Enter(obj, ref lockTaken) and TryEnter(obj, ref lockTaken) throw
// System.ArgumentException (parameter "lockTaken") when the flag is ALREADY
// true on entry -- and that check runs BEFORE the null-object check (probe:
// Enter(null, ref true) -> ArgumentException, not ArgumentNullException).
// The flag is left untouched on every throw path. The 1-arg forms call these
// functions with lockTaken == nullptr, which skips the check.
void CheckLockTaken(bool* lockTaken)
{
    if (lockTaken != nullptr && *lockTaken)
    {
        il2cpp_throw_argument_exception(
            "lockTaken must be initialized to false prior to entering the Monitor");
    }
}

//------------------------------------------------------------------------------
// il2cpp_monitor_enter -- blocking, reentrant acquire. The optional bool*
// lockTaken (Roslyn lock-lowering Monitor.Enter(obj, ref bool)) is set to
// true on every successful acquire -- fresh OR reentrant -- and left
// untouched on the throw paths (lockTaken already true / null / table
// exhaustion), matching the .NET contract that the generated if(lockTaken)
// finally-guard relies on.
//------------------------------------------------------------------------------
void il2cpp_monitor_enter(Il2CppObject* obj, bool* lockTaken)
{
    CheckLockTaken(lockTaken); // net48 ordering: the lockTaken check beats null
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("obj");
    }
    const int32_t me = CurrentThreadId();

    MonitorEntry* e;
    {
        TableGuard guard;
        e = FindOrCreateEntry(obj);
        if (e == nullptr)
        {
            il2cpp_throw_invalid_operation_exception("monitor side table exhausted");
        }
        if (e->ownerThreadId == me)
        {
            ++e->recursion; // reentrant: no lock operation needed
            if (lockTaken != nullptr)
            {
                *lockTaken = true;
            }
            return;
        }
        ++e->refs; // reservation held across the blocking acquire
    }

    LockEntry(*e); // blocks; the table lock is NOT held (deadlock-free)

    {
        TableGuard guard;
        e->ownerThreadId = me;
        e->recursion = 1;
        // the reservation stays as the monitor's hold ref; the matching
        // Exit drops it when the recursion counter reaches zero
    }
    if (lockTaken != nullptr)
    {
        *lockTaken = true;
    }
}

//------------------------------------------------------------------------------
// il2cpp_monitor_exit -- release one recursion level.
//------------------------------------------------------------------------------
void il2cpp_monitor_exit(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("obj");
    }
    const int32_t me = CurrentThreadId();

    TableGuard guard;
    MonitorEntry* e = FindEntry(obj);
    if (e == nullptr || e->ownerThreadId != me)
    {
        ThrowSynchronizationLockException(); // [[noreturn]]; the guard releases the table lock
    }
    if (--e->recursion > 0)
    {
        return; // still held; refs stay
    }
    e->ownerThreadId = 0;
    UnlockEntry(*e);
    ReleaseRef(*e);
}

//------------------------------------------------------------------------------
// il2cpp_monitor_try_enter -- non-blocking acquire.
//------------------------------------------------------------------------------
bool il2cpp_monitor_try_enter(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("obj");
    }
    const int32_t me = CurrentThreadId();

    MonitorEntry* e;
    {
        TableGuard guard;
        e = FindOrCreateEntry(obj);
        if (e == nullptr)
        {
            il2cpp_throw_invalid_operation_exception("monitor side table exhausted");
        }
        if (e->ownerThreadId == me)
        {
            ++e->recursion; // reentrant
            return true;
        }
        ++e->refs; // reservation held across the try
    }

    if (TryLockEntry(*e)) // non-blocking
    {
        TableGuard guard;
        e->ownerThreadId = me;
        e->recursion = 1;
        return true; // reservation stays as the monitor's hold ref
    }

    {
        TableGuard guard;
        ReleaseRef(*e);
    }
    return false;
}

//------------------------------------------------------------------------------
// il2cpp_monitor_try_enter(obj, lockTaken) -- the net48
// Monitor.TryEnter(object, ref bool) form. Same non-blocking semantics as
// the 1-arg TryEnter: the flag mirrors the result (*lockTaken = acquired)
// and an already-true flag throws ArgumentException before the null check
// (net48 ordering), leaving the flag untouched.
//------------------------------------------------------------------------------
bool il2cpp_monitor_try_enter(Il2CppObject* obj, bool* lockTaken)
{
    CheckLockTaken(lockTaken); // net48 ordering: the lockTaken check beats null
    const bool acquired = il2cpp_monitor_try_enter(obj);
    if (lockTaken != nullptr)
    {
        *lockTaken = acquired;
    }
    return acquired;
}

//------------------------------------------------------------------------------
// il2cpp_monitor_try_enter(obj, timeoutMs) -- the net48 timeout-based acquire.
// The caller blocks (polling) up to timeoutMs when the monitor is contended;
// timeoutMs == 0 is a single non-blocking try (net48 TryEnter(obj, 0) returns
// immediately), -1 waits forever, and a value < -1 throws
// ArgumentOutOfRangeException (net48: the int overload throws with NO
// parameter name -- probe rtaud7). Reentrant on the calling thread. A failed
// attempt leaks no reservation (the caller can retry after the owner exits).
//------------------------------------------------------------------------------
bool il2cpp_monitor_try_enter(Il2CppObject* obj, int32_t timeoutMs)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("obj");
    }
    if (timeoutMs < -1)
    {
        il2cpp_throw_argument_out_of_range_exception(nullptr, nullptr); // net48 int overload: no param name
    }
    const int32_t me = CurrentThreadId();

    MonitorEntry* e;
    {
        TableGuard guard;
        e = FindOrCreateEntry(obj);
        if (e == nullptr)
        {
            il2cpp_throw_invalid_operation_exception("monitor side table exhausted");
        }
        if (e->ownerThreadId == me)
        {
            ++e->recursion; // reentrant
            return true;
        }
        ++e->refs; // reservation held across the blocking acquire
    }

    // Non-blocking try first: the common uncontended path never sleeps.
    if (TryLockEntry(*e))
    {
        TableGuard guard;
        e->ownerThreadId = me;
        e->recursion = 1;
        return true; // reservation stays as the monitor's hold ref
    }
    if (timeoutMs == 0)
    {
        TableGuard guard;
        ReleaseRef(*e);
        return false; // single try, already contended
    }

    // Blocking acquire with a bounded deadline (or forever for -1).
    const bool infinite = (timeoutMs < 0);
    const auto deadline = infinite
        ? std::chrono::steady_clock::time_point::max()
        : std::chrono::steady_clock::now() + std::chrono::milliseconds(timeoutMs);
    bool acquired = false;
    for (;;)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(kWaitPollMs));
        if (TryLockEntry(*e))
        {
            acquired = true;
            break;
        }
        if (!infinite && std::chrono::steady_clock::now() >= deadline)
        {
            break; // timeout
        }
    }
    if (acquired)
    {
        TableGuard guard;
        e->ownerThreadId = me;
        e->recursion = 1;
        return true;
    }
    {
        TableGuard guard;
        ReleaseRef(*e);
    }
    return false;
}

//------------------------------------------------------------------------------
// il2cpp_monitor_try_enter(obj, timeoutMs, lockTaken) -- the net48 3-arg form.
// Ordering matches net48 (probe-verified): lockTaken already true ->
// ArgumentException BEFORE the null check; then null -> ArgumentNullException
// (before timeout validation); then timeout < -1 -> ArgumentOutOfRangeException
// (no param name). The flag is left untouched on every throw path.
//------------------------------------------------------------------------------
bool il2cpp_monitor_try_enter(Il2CppObject* obj, int32_t timeoutMs, bool* lockTaken)
{
    CheckLockTaken(lockTaken); // net48 ordering: the lockTaken check beats null and timeout
    const bool acquired = il2cpp_monitor_try_enter(obj, timeoutMs);
    if (lockTaken != nullptr)
    {
        *lockTaken = acquired;
    }
    return acquired;
}

//------------------------------------------------------------------------------
// il2cpp_monitor_pulse -- signal waiting threads (simplified broadcast).
//------------------------------------------------------------------------------
void il2cpp_monitor_pulse(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("obj");
    }
    const int32_t me = CurrentThreadId();

    TableGuard guard;
    MonitorEntry* e = FindEntry(obj);
    if (e == nullptr || e->ownerThreadId != me)
    {
        ThrowSynchronizationLockException();
    }
    if (e->waiterCount > 0)
    {
        ++e->pulseGen; // wakes every thread polling on this entry
    }
}

//------------------------------------------------------------------------------
// il2cpp_monitor_wait -- release, wait for a pulse (or timeout), reacquire.
//------------------------------------------------------------------------------
bool il2cpp_monitor_wait(Il2CppObject* obj)
{
    return il2cpp_monitor_wait(obj, -1); // Wait(object) = Timeout.Infinite
}

bool il2cpp_monitor_wait(Il2CppObject* obj, int32_t timeoutMs)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("obj");
    }
    // net48 Wait(object, int) validates the timeout BEFORE the ownership
    // check (probe: Wait(unowned, -2) -> ArgumentOutOfRangeException, but
    // Wait(unowned, 0) -> SynchronizationLockException). The runtime's
    // "negative = infinite" rule is narrowed to exactly -1 (Timeout.Infinite);
    // anything below -1 is an out-of-range argument, not an infinite wait.
    if (timeoutMs < -1)
    {
        il2cpp_throw_argument_out_of_range_exception("millisecondsTimeout", nullptr);
    }
    const int32_t me = CurrentThreadId();

    MonitorEntry* e;
    int32_t savedRecursion;
    int32_t gen;
    {
        TableGuard guard;
        e = FindEntry(obj);
        if (e == nullptr || e->ownerThreadId != me)
        {
            ThrowSynchronizationLockException();
        }
        savedRecursion = e->recursion;
        ++e->refs; // reservation held across the whole wait
        e->recursion = 0;
        e->ownerThreadId = 0;
        ++e->waiterCount;
        gen = e->pulseGen;
    }

    UnlockEntry(*e); // release the monitor (we were the owner)

    // --- wait for a pulse generation change or the timeout ---
    bool pulsed = false;
    if (timeoutMs == 0)
    {
        TableGuard guard;
        pulsed = (e->pulseGen != gen); // single non-blocking poll
    }
    else if (timeoutMs > 0)
    {
        const auto deadline = std::chrono::steady_clock::now()
                              + std::chrono::milliseconds(timeoutMs);
        for (;;)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(kWaitPollMs));
            {
                TableGuard guard;
                if (e->pulseGen != gen)
                {
                    pulsed = true;
                    break;
                }
            }
            if (std::chrono::steady_clock::now() >= deadline)
            {
                break;
            }
        }
    }
    else
    {
        // negative timeout = Timeout.Infinite: wait until pulsed
        for (;;)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(kWaitPollMs));
            TableGuard guard;
            if (e->pulseGen != gen)
            {
                pulsed = true;
                break;
            }
        }
    }

    LockEntry(*e); // reacquire the monitor (blocking; table lock not held)

    {
        TableGuard guard;
        e->ownerThreadId = me;
        e->recursion = savedRecursion; // restore the full recursion depth
        --e->waiterCount;
        ReleaseRef(*e); // the hold ref from the original Enter stays
    }
    return pulsed;
}

}} // namespace il2cpp::runtime
