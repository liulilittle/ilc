#pragma once
//------------------------------------------------------------------------------
// safehandle.h -- System.Runtime.InteropServices.SafeHandle minimal reference
// counting (src/runtime/src/safehandle.cpp).
//
// The runtime has NO SafeHandle object model (no class descriptor, no
// allocation path -- a SafeHandle cannot be constructed end-to-end in ILC), so
// these icalls implement the MINIMAL .NET Framework 4.8 reference-counting
// semantics in a flat side table keyed by the receiver object address:
//
//   state entry : { object address, refCount, closed }
//   implicit construction reference: the FIRST touch of an object creates the
//     entry with refCount = 1 (net48's SafeHandle ctor starts at RefCountOne).
//
//   icall_safehandle_dangerous_add_ref(self, success):
//     * closed handle -> ObjectDisposedException BEFORE success is written
//                        (the caller's `success` slot stays false)
//     * else          -> refCount++, *success = true
//   icall_safehandle_dangerous_release(self):
//     * refCount == 0 -> ObjectDisposedException (unbalanced release)
//     * refCount--; zero -> handle released: entry marked closed and kept as a
//                           tombstone (a further release throws)
//   icall_safehandle_set_handle_as_invalid(self):
//     * marks the entry closed (idempotent); the ref count is NOT changed
//       (net48: SetHandleAsInvalid only sets the closed bit)
//
// DOCUMENTED DEVIATION: net48's zero-refcount "release" calls the subclass's
// ReleaseHandle() override to close the OS handle. ILC has no SafeHandle
// subclass model, so the release only transitions the state to closed -- the
// actual handle-close is outside the model (loud in MethodMap.SafeHandle.cs).
//
// THREAD MODEL: the table is unsynchronized, matching the other flat runtime
// side tables (pinvokeext callbacks) -- the generated-code model is
// single-threaded per process.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// DangerousAddRef(ref bool success): increment the reference count; on a
// closed handle throw ObjectDisposedException. On success *success = true.
void icall_safehandle_dangerous_add_ref(Il2CppObject* self, int32_t* success);

// DangerousRelease(): decrement the reference count; an unbalanced release
// (ref count already zero) throws ObjectDisposedException. A zero ref count
// marks the handle released (closed).
void icall_safehandle_dangerous_release(Il2CppObject* self);

// SetHandleAsInvalid(): mark the handle closed without releasing (the ref
// count is unchanged); idempotent.
void icall_safehandle_set_handle_as_invalid(Il2CppObject* self);

}} // namespace il2cpp::runtime
