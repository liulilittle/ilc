#pragma once
//------------------------------------------------------------------------------
// il2cpp_rwlock.h -- System.Threading.ReaderWriterLock (the LEGACY lock, NOT
// ReaderWriterLockSlim -- that one lives in il2cpp_rwls.h).
//
// p0c D37: the lock previously had no object model at all -- AnyWritersSince
// was a constant-false stub and every other member was LOUD, so a program
// that used the lock could not even be translated. The object model here is
// the same shape as ReaderWriterLockSlim (il2cpp_rwls.h): header + ONE
// native state pointer, allocated through the GLOBAL descriptor
// System_Threading_ReaderWriterLock_class, so the transpiler's newobj
// fallback needs no BclAllocExpr entry.
//
// MEASURED net48 semantics (probe-p0c, .NET Framework 4.8 x64):
//   * WriterSeqNum starts at 1 and increments on EVERY writer acquisition
//     (a recursive re-acquire included).
//   * AnyWritersSince(seqNum) is exactly `WriterSeqNum > seqNum` -- measured:
//     seq=1 -> AnyWritersSince(0)=True, AnyWritersSince(1)=False; after one
//     writer seq=2 -> AnyWritersSince(1)=True, AnyWritersSince(2)=False.
//     (A reader acquisition never advances the sequence.)
//   * IsReaderLockHeld / IsWriterLockHeld report the CURRENT thread's hold.
//   * ReleaseReaderLock / ReleaseWriterLock without the matching hold throw
//     ApplicationException ("... not held by the current thread").
//   * A timeout below -1 throws ArgumentOutOfRangeException.
//
// SINGLE-THREAD DETERMINISM: this runtime is deterministic and single-threaded
// for the probes; the state tracks the current thread's recursion levels
// (readLevel / writeLevel). Cross-thread contention semantics (waiting,
// timeouts) are not exercised and are documented as unsupported here -- the
// Monitor / ReaderWriterLockSlim domains cover real concurrency.
//
// NOT WIRED (documented): UpgradeToWriterLock / DowngradeFromWriterLock /
// ReleaseLock / RestoreLock exchange a System.Threading.LockCookie, a BCL
// value type with no C++ model in this transpiler (a TypeEmitter concern, a
// different owner's file domain). Those four stay LOUD rather than pretending.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// The managed object: header + native state pointer (like Il2CppRwlsObject).
struct Il2CppReaderWriterLockObject
{
    Il2CppObject header;
    void* state;
};

// ReaderWriterLock() -- the only ctor net48 exposes.
void icall_rwl_ctor(Il2CppObject* self);

// Acquire/Release (int timeout; -1 = infinite, < -1 = ArgumentOutOfRange).
void icall_rwl_acquire_reader(Il2CppObject* self, int32_t timeout);
void icall_rwl_release_reader(Il2CppObject* self);
void icall_rwl_acquire_writer(Il2CppObject* self, int32_t timeout);
void icall_rwl_release_writer(Il2CppObject* self);

// Property getters.
bool icall_rwl_get_is_reader_lock_held(Il2CppObject* self);
bool icall_rwl_get_is_writer_lock_held(Il2CppObject* self);
int32_t icall_rwl_get_writer_seq_num(Il2CppObject* self);

// ReaderWriterLock.AnyWritersSince(int seqNum) -- `WriterSeqNum > seqNum`.
bool icall_rwl_any_writers_since(Il2CppObject* self, int32_t seqNum);

}} // namespace il2cpp::runtime
