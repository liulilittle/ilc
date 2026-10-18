#pragma once
//------------------------------------------------------------------------------
// il2cpp_rwls.h -- System.Threading.ReaderWriterLockSlim runtime support
// (rwls.cpp, batch nb5 RWLS-LOCK).
//
// OBJECT MODEL: ReaderWriterLockSlim is a managed object (Il2CppRwlsObject --
// header + ONE native state pointer, the wait-handle / sync_lite contract)
// allocated through the GLOBAL-scope descriptor
// System_Threading_ReaderWriterLockSlim_class; the transpiler's newobj
// fallback emits il2cpp_object_new(&System_Threading_ReaderWriterLockSlim_class)
// and the .ctor icalls below allocate/initialize the native state. The
// pointer is NOT a GC reference (refFieldBitmap 0). The state is never freed
// (the wait-handle precedent).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48). The runtime implements the
// LockRecursionPolicy.NoRecursion minimum state machine -- deterministic
// single-threaded held/flag/count gates:
//   * default ctor -> NoRecursion; the recursion policy is observable.
//   * Enter/Exit read|write|upgradeable + Try*Enter(0) success path.
//   * CurrentReadCount counts plain read-lock holders only (upgradeable read
//     holders are NOT counted -> 0 while upgradeable is held).
//   * in NoRecursion: a nested Enter (read or write), or requesting the write
//     lock while holding the read lock (even TryEnterWriteLock(0)), throws
//     System.Threading.LockRecursionException.
//   * Exit without entering throws System.Threading.SynchronizationLockException.
//   * upgradeable read -> write is the same-thread upgrade (allowed).
// LockRecursionPolicy.SupportsRecursion is implemented (nb8): a recursive
// Enter{Read,Write,UpgradeableRead}Lock increments the per-mode recursion depth
// (each Exit decrements it; an Exit at depth 0 throws), CurrentReadCount stays
// the UNIQUE reader count (1 while a thread holds any read/un upgrade), a
// recursive read from a held write / upgradeable read is allowed, and
// read -> write stays a LockRecursionException (cross-type, oracle
// [sup.read.write]). The blocking/waiting counters read 0 (single-thread
// deterministic probes); the state mutex/condition variable exist for the
// future concurrent forms.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

#include <cstdint>

// System.Threading.ReaderWriterLockSlim runtime descriptor (GLOBAL scope --
// the transpiler emits &System_Threading_ReaderWriterLockSlim_class for
// newobj; defined in rwls.cpp, registered lazily by the ctor icalls).
extern il2cpp::Il2CppClass System_Threading_ReaderWriterLockSlim_class;

namespace il2cpp { namespace runtime {

// Managed layout: object header + ONE native state pointer (NOT a GC
// reference -- refFieldBitmap 0, the Il2CppSyncObject contract).
struct Il2CppRwlsObject
{
    Il2CppObject obj; // object header (klass pointer, offset 0)
    void* state;      // native state (offset 8) -- not a GC reference
};

// Register the ReaderWriterLockSlim descriptor once (idempotent). Called
// defensively by the ctor icalls before the first allocation.
void il2cpp_rwls_register_classes();

// --- construction -----------------------------------------------------------

// ReaderWriterLockSlim() -- default policy NoRecursion. Allocates the native
// state.
void icall_rwls_ctor_default(Il2CppObject* self);

// ReaderWriterLockSlim(LockRecursionPolicy recursionPolicy): NoRecursion (0)
// and SupportsRecursion (1) are both accepted (nb8: recursion counters are
// modelled); any other value throws NotSupportedException (never silently
// ignored). Allocates the native state.
void icall_rwls_ctor_policy(Il2CppObject* self, int32_t recursionPolicy);

// --- read lock --------------------------------------------------------------

void icall_rwls_enter_read(Il2CppObject* self);
void icall_rwls_exit_read(Il2CppObject* self);
// bool TryEnterReadLock(int millisecondsTimeout): recursive acquire throws
// LockRecursionException; otherwise acquires (single-thread) and returns true.
bool icall_rwls_try_enter_read(Il2CppObject* self, int32_t millisecondsTimeout);

// --- write lock -------------------------------------------------------------

void icall_rwls_enter_write(Il2CppObject* self);
void icall_rwls_exit_write(Il2CppObject* self);
bool icall_rwls_try_enter_write(Il2CppObject* self, int32_t millisecondsTimeout);

// --- upgradeable read lock --------------------------------------------------

void icall_rwls_enter_upgradeable(Il2CppObject* self);
void icall_rwls_exit_upgradeable(Il2CppObject* self);
bool icall_rwls_try_enter_upgradeable(Il2CppObject* self, int32_t millisecondsTimeout);

// --- property getters -------------------------------------------------------

bool icall_rwls_get_is_read_lock_held(Il2CppObject* self);
bool icall_rwls_get_is_write_lock_held(Il2CppObject* self);
bool icall_rwls_get_is_upgradeable_read_lock_held(Il2CppObject* self);
int32_t icall_rwls_get_current_read_count(Il2CppObject* self);
int32_t icall_rwls_get_waiting_read_count(Il2CppObject* self);
int32_t icall_rwls_get_waiting_write_count(Il2CppObject* self);
int32_t icall_rwls_get_waiting_upgrade_count(Il2CppObject* self);
int32_t icall_rwls_get_recursion_policy(Il2CppObject* self);

}} // namespace il2cpp::runtime
