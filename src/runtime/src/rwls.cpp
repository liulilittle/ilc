//------------------------------------------------------------------------------
// rwls.cpp -- System.Threading.ReaderWriterLockSlim runtime support
// (il2cpp_rwls.h, batch nb5 RWLS-LOCK). LockRecursionPolicy.NoRecursion minimum
// state machine, deterministic single-threaded (see the header contract).
//------------------------------------------------------------------------------

#include "il2cpp_rwls.h"

#include "il2cpp_exception.h"     // il2cpp_throw_new (carrier)
#include "il2cpp_exception_ext.h" // il2cpp_throw_not_supported_exception
#include "il2cpp_runtime.h"       // il2cpp_class_register

#include <condition_variable>
#include <cstdint>
#include <cstdlib> // std::abort (defensive fallback in the throw helpers)
#include <mutex>

//------------------------------------------------------------------------------
// Class descriptor (GLOBAL scope -- the transpiler emits
// &System_Threading_ReaderWriterLockSlim_class for newobj). Header + native
// state pointer; NO GC reference fields (refFieldBitmap 0).
//------------------------------------------------------------------------------
::il2cpp::Il2CppClass System_Threading_ReaderWriterLockSlim_class = {
    "System.Threading.ReaderWriterLockSlim",
    static_cast<int32_t>(sizeof(::il2cpp::runtime::Il2CppRwlsObject)),
    0u,
    0,
    &System_Object_class,
    nullptr, 0, nullptr, 0,
};

namespace il2cpp { namespace runtime {

namespace {

constexpr int32_t kLockRecursionPolicyNoRecursion = 0;
constexpr int32_t kLockRecursionPolicySupportsRecursion = 1;

// Native state. The mutex/condition variable exist so the blocking forms have
// their eventual concurrency semantics; the deterministic probes drive them
// single-threaded, so the counters are exact (no wall-clock).
//
// The three lock modes are tracked as per-thread RECURSION DEPTHS (readDepth /
// writeDepth / upgradeDepth): a recursive Enter increments the depth, each Exit
// decrements it, and an Exit with depth 0 is a SynchronizationLockException.
// readCount is the .NET CurrentReadCount -- the number of UNIQUE read holders
// (single-threaded: 0 or 1), NOT the recursion depth.
struct RwlsState
{
    std::mutex mtx;
    std::condition_variable cv;
    int32_t policy;          // 0 = NoRecursion, 1 = SupportsRecursion
    int32_t readCount;       // CurrentReadCount (unique read holders)
    int32_t readDepth;       // current thread's read-lock recursion depth
    int32_t writeDepth;      // current thread's write-lock recursion depth
    int32_t upgradeDepth;    // current thread's upgradeable-read recursion depth
    int32_t waitingRead;
    int32_t waitingWrite;
    int32_t waitingUpgrade;
};

bool g_rwlsClassRegistered = false;

RwlsState* AsState(Il2CppObject* self)
{
    return static_cast<RwlsState*>(reinterpret_cast<Il2CppRwlsObject*>(self)->state);
}

[[noreturn]] void ThrowLockRecursionException()
{
    il2cpp_throw_new(
        "System.Threading.LockRecursionException,"
        "System.Threading.LockRecursionException,"
        "System.SystemException,System.Object,System.Exception",
        "Entering the lock recursively is not permitted by the lock recursion policy.");
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort();
#endif
}

[[noreturn]] void ThrowSynchronizationLockException()
{
    il2cpp_throw_new(
        "System.Threading.SynchronizationLockException,"
        "System.Threading.SynchronizationLockException,"
        "System.SystemException,System.Object,System.Exception",
        "Object synchronization method was called from an unsynchronized block of code.");
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort();
#endif
}

// Acquire the read lock (single-thread deterministic).
//   * NoRecursion: ANY existing hold (a read or a write) is a recursion error.
//   * SupportsRecursion (net48 oracle p-nb8-rwlsrec): a recursive read from an
//     existing read, from a held write, or from a held upgradeable read is
//     allowed and increments the recursion depth; CurrentReadCount stays 1
//     (unique readers, not depth).
void AcquireRead(RwlsState* state)
{
    if (state->policy == kLockRecursionPolicySupportsRecursion)
    {
        if (state->readDepth == 0)
        {
            state->readCount = 1; // first read acquisition on this thread
        }
        ++state->readDepth;
        return;
    }
    if (state->readDepth > 0 || state->writeDepth != 0)
    {
        ThrowLockRecursionException();
    }
    ++state->readCount;
    state->readDepth = 1;
}

void AcquireWrite(RwlsState* s)
{
    if (s->policy == kLockRecursionPolicySupportsRecursion)
    {
        if (s->writeDepth > 0)
        {
            ++s->writeDepth; // recursive write
            return;
        }
        // read -> write is a CROSS-TYPE violation even under SupportsRecursion
        // (oracle [sup.read.write]=LockRecursionException; the upgradeable read
        // lock is the valid upgrade path). A read taken UNDER an upgradeable
        // read is not a cross-type violation (the upgradeable owner may write).
        if (s->readDepth > 0 && s->upgradeDepth == 0)
        {
            ThrowLockRecursionException();
        }
        s->writeDepth = 1; // fresh, or upgradeable read -> write (same thread)
        return;
    }
    if (s->writeDepth != 0)
    {
        ThrowLockRecursionException(); // nested write
    }
    if (s->readDepth > 0)
    {
        // Holding the read lock then requesting the write lock is a recursion
        // error in NoRecursion (upgradeable read holders are the valid path).
        ThrowLockRecursionException();
    }
    s->writeDepth = 1;
}

void AcquireUpgradeable(RwlsState* s)
{
    if (s->policy == kLockRecursionPolicySupportsRecursion)
    {
        if (s->upgradeDepth > 0)
        {
            ++s->upgradeDepth; // recursive upgradeable read
            return;
        }
        if (s->readDepth > 0 || s->writeDepth > 0)
        {
            ThrowLockRecursionException();
        }
        s->upgradeDepth = 1;
        return;
    }
    // D2: holding the WRITE lock then requesting the upgradeable read lock is
    // a recursion error in NoRecursion (the failed request must leave the
    // state untouched). Same path for EnterUpgradeableReadLock and
    // TryEnterUpgradeableReadLock(0).
    if (s->writeDepth != 0 || s->upgradeDepth != 0 || s->readDepth > 0)
    {
        ThrowLockRecursionException();
    }
    s->upgradeDepth = 1;
}

} // namespace

void il2cpp_rwls_register_classes()
{
    if (g_rwlsClassRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Threading_ReaderWriterLockSlim_class);
    g_rwlsClassRegistered = true;
}

//------------------------------------------------------------------------------
// Construction.
//------------------------------------------------------------------------------
void icall_rwls_ctor_default(Il2CppObject* self)
{
    il2cpp_rwls_register_classes();
    auto* state = new RwlsState();
    state->policy = kLockRecursionPolicyNoRecursion;
    reinterpret_cast<Il2CppRwlsObject*>(self)->state = state;
}

void icall_rwls_ctor_policy(Il2CppObject* self, int32_t recursionPolicy)
{
    il2cpp_rwls_register_classes();
    if (recursionPolicy != kLockRecursionPolicyNoRecursion &&
        recursionPolicy != kLockRecursionPolicySupportsRecursion)
    {
        // LockRecursionPolicy only has the two values; never silently ignore an
        // out-of-range argument.
        il2cpp_throw_not_supported_exception(
            "LockRecursionPolicy value is not supported by this runtime.");
    }
    auto* state = new RwlsState();
    state->policy = recursionPolicy;
    reinterpret_cast<Il2CppRwlsObject*>(self)->state = state;
}

//------------------------------------------------------------------------------
// Read lock.
//------------------------------------------------------------------------------
void icall_rwls_enter_read(Il2CppObject* self)
{
    AcquireRead(AsState(self));
}

void icall_rwls_exit_read(Il2CppObject* self)
{
    RwlsState* state = AsState(self);
    if (state->readDepth == 0)
    {
        ThrowSynchronizationLockException();
    }
    --state->readDepth;
    if (state->readDepth == 0)
    {
        state->readCount = 0; // last reader on this thread released
    }
}

bool icall_rwls_try_enter_read(Il2CppObject* self, int32_t millisecondsTimeout)
{
    (void)millisecondsTimeout;
    AcquireRead(AsState(self));
    return true;
}

//------------------------------------------------------------------------------
// Write lock.
//------------------------------------------------------------------------------
void icall_rwls_enter_write(Il2CppObject* self)
{
    AcquireWrite(AsState(self));
}

void icall_rwls_exit_write(Il2CppObject* self)
{
    RwlsState* state = AsState(self);
    if (state->writeDepth == 0)
    {
        ThrowSynchronizationLockException();
    }
    --state->writeDepth;
}

bool icall_rwls_try_enter_write(Il2CppObject* self, int32_t millisecondsTimeout)
{
    (void)millisecondsTimeout;
    AcquireWrite(AsState(self));
    return true;
}

//------------------------------------------------------------------------------
// Upgradeable read lock.
//------------------------------------------------------------------------------
void icall_rwls_enter_upgradeable(Il2CppObject* self)
{
    AcquireUpgradeable(AsState(self));
}

void icall_rwls_exit_upgradeable(Il2CppObject* self)
{
    RwlsState* state = AsState(self);
    if (state->upgradeDepth == 0)
    {
        ThrowSynchronizationLockException();
    }
    --state->upgradeDepth;
}

bool icall_rwls_try_enter_upgradeable(Il2CppObject* self, int32_t millisecondsTimeout)
{
    (void)millisecondsTimeout;
    AcquireUpgradeable(AsState(self));
    return true;
}

//------------------------------------------------------------------------------
// Property getters.
//------------------------------------------------------------------------------
bool icall_rwls_get_is_read_lock_held(Il2CppObject* self)
{
    return AsState(self)->readDepth > 0;
}

bool icall_rwls_get_is_write_lock_held(Il2CppObject* self)
{
    return AsState(self)->writeDepth != 0;
}

bool icall_rwls_get_is_upgradeable_read_lock_held(Il2CppObject* self)
{
    return AsState(self)->upgradeDepth != 0;
}

int32_t icall_rwls_get_current_read_count(Il2CppObject* self)
{
    return AsState(self)->readCount;
}

int32_t icall_rwls_get_waiting_read_count(Il2CppObject* self)
{
    return AsState(self)->waitingRead;
}

int32_t icall_rwls_get_waiting_write_count(Il2CppObject* self)
{
    return AsState(self)->waitingWrite;
}

int32_t icall_rwls_get_waiting_upgrade_count(Il2CppObject* self)
{
    return AsState(self)->waitingUpgrade;
}

int32_t icall_rwls_get_recursion_policy(Il2CppObject* self)
{
    return AsState(self)->policy;
}

}} // namespace il2cpp::runtime
