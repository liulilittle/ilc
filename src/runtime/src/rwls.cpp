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

// Native state. The mutex/condition variable exist so the blocking forms have
// their eventual concurrency semantics; the deterministic probes drive them
// single-threaded, so the counters are exact (no wall-clock).
struct RwlsState
{
    std::mutex mtx;
    std::condition_variable cv;
    int32_t policy;          // 0 = NoRecursion (only supported value)
    int32_t readCount;       // plain read-lock holders (CurrentReadCount)
    int32_t readDepth;       // current thread's read-lock recursion depth
    int32_t writeHeld;       // write lock held by the current thread
    int32_t upgradeHeld;     // upgradeable read lock held by the current thread
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

// Acquire the read lock (single-thread deterministic). Recursive acquire in
// NoRecursion is a LockRecursionException.
void AcquireRead(RwlsState* state)
{
    if (state->readDepth > 0 || state->writeHeld != 0)
    {
        ThrowLockRecursionException();
    }
    ++state->readCount;
    state->readDepth = 1;
}

void AcquireWrite(RwlsState* s)
{
    if (s->writeHeld != 0)
    {
        ThrowLockRecursionException(); // nested write
    }
    if (s->readDepth > 0)
    {
        // Holding the read lock then requesting the write lock is a recursion
        // error in NoRecursion (upgradeable read holders are the valid path).
        ThrowLockRecursionException();
    }
    s->writeHeld = 1;
}

void AcquireUpgradeable(RwlsState* s)
{
    // D2: holding the WRITE lock then requesting the upgradeable read lock is
    // a recursion error in NoRecursion (the failed request must leave the
    // state untouched). Same path for EnterUpgradeableReadLock and
    // TryEnterUpgradeableReadLock(0).
    if (s->writeHeld != 0 || s->upgradeHeld != 0 || s->readDepth > 0)
    {
        ThrowLockRecursionException();
    }
    s->upgradeHeld = 1;
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
    if (recursionPolicy != kLockRecursionPolicyNoRecursion)
    {
        // SupportsRecursion is the documented LOUD subset (the runtime does
        // not model recursion counters); never silently ignore the policy.
        il2cpp_throw_not_supported_exception(
            "LockRecursionPolicy.SupportsRecursion is not supported by this runtime.");
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
    --state->readCount;
    state->readDepth = 0;
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
    if (state->writeHeld == 0)
    {
        ThrowSynchronizationLockException();
    }
    state->writeHeld = 0;
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
    if (state->upgradeHeld == 0)
    {
        ThrowSynchronizationLockException();
    }
    state->upgradeHeld = 0;
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
    return AsState(self)->writeHeld != 0;
}

bool icall_rwls_get_is_upgradeable_read_lock_held(Il2CppObject* self)
{
    return AsState(self)->upgradeHeld != 0;
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
