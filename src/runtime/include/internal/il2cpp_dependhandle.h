#pragma once
//------------------------------------------------------------------------------
// il2cpp_dependhandle.h -- System.Runtime.DependentHandle minimal dependent
// GC-handle semantics (src/runtime/src/dependhandle.cpp).
//
// A dependent handle pairs a PRIMARY object (the .NET "target") with a
// SECONDARY object (the .NET "dependent"). Real .NET GC semantics (coreclr
// dependent handle table):
//
//   primary  : LONG-WEAK  -- the handle does NOT keep the primary alive.
//   secondary: strong WHILE the primary is alive; weak (collectible) once the
//              primary dies. Conceptually the primary owns a strong reference
//              to the secondary for as long as the primary itself lives.
//
// The ILC mark-sweep collector (il2cpp_gc.h) has NO dependent-handle table:
// its root set is static roots, root ranges, weak slots and finalizers only.
// Dependent rooting therefore uses the documented DEGRADED implementation:
//
//   * primary  is stored in a registered WEAK slot  (il2cpp_gc_add_weak_ref).
//     When nothing else roots the primary, the collector clears the slot at
//     the next collection -- the primary dies exactly like .NET.
//   * secondary is stored in a STRONG slot whose whole array is registered
//     ONCE as a root range (il2cpp_gc_add_root_range). The root range is the
//     "strong reference table" that keeps the secondary alive while the
//     handle is allocated.
//   * when a handle operation observes the primary slot cleared (primary
//     died), the secondary slot is cleared too -- the dependent becomes
//     collectible at the FOLLOWING collection.
//
// DOCUMENTED DEVIATION: in .NET the secondary is released by the SAME
// collection that reaps the primary (the GC knows the dependency). Here the
// release happens on the next handle operation AFTER the primary death, so a
// secondary whose primary died but whose handle is never touched again stays
// alive until Free(). Memory retention is the only cost; liveness is never
// under-reported (the secondary is never reclaimed early).
//
// Handle ABI: the handle is the table INDEX + 1, so the zero handle means
// "not allocated" (mirrors IsAllocated => _handle != 0). Invalid/zero handles
// are handled defensively (reads return null, writes are no-ops) -- the
// managed wrapper throws InvalidOperationException BEFORE reaching the icalls,
// matching the netcore DependentHandle wrapper.
//
// The FCall table (ecalllist.h gDependentHandleFuncs) maps 1:1:
//   InternalAlloc                 -> icall_dependenthandle_alloc
//   InternalGetTarget             -> icall_dependenthandle_get_target
//   InternalGetDependent          -> icall_dependenthandle_get_dependent
//   InternalGetTargetAndDependent -> icall_dependenthandle_get_target_and_dependent
//   InternalSetTargetToNull       -> icall_dependenthandle_set_target_to_null
//   InternalSetDependent          -> icall_dependenthandle_set_dependent
//   InternalFree                  -> icall_dependenthandle_free
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// Allocate a dependent handle pairing 'primary' (weakly held) with
// 'secondary' (strongly held while the primary lives). Returns a non-zero
// handle; a zero primary means the dependent is not tracked (.NET: the
// dependent is strong only while the target is non-null). Table overflow
// aborts loudly (compile-time bound, constraint #12).
intptr_t icall_dependenthandle_alloc(Il2CppObject* primary, Il2CppObject* secondary);

// The primary object, or null when the primary has been collected or nulled.
Il2CppObject* icall_dependenthandle_get_target(intptr_t handle);

// The secondary object, but only while the primary is non-null (.NET:
// InternalGetDependent returns null when the target is null).
Il2CppObject* icall_dependenthandle_get_dependent(intptr_t handle);

// Atomically return the primary and write the secondary into *outDependent
// (null when the primary is null). The primary is returned as the result.
Il2CppObject* icall_dependenthandle_get_target_and_dependent(intptr_t handle,
    Il2CppObject** outDependent);

// Set the primary to null: the dependent starts returning null and becomes
// eligible for collection (.NET Target setter only allows null).
void icall_dependenthandle_set_target_to_null(intptr_t handle);

// Set the secondary. A secondary stored on a null-primary handle is not
// retained (.NET: the dependent is tracked only while the target is non-null).
void icall_dependenthandle_set_dependent(intptr_t handle, Il2CppObject* dependent);

// Set BOTH the primary and the secondary at once (the ILC Core surface's
// SetPrimaryAndSecondary). The weak slot is re-armed to the new primary; a
// null primary drops the secondary immediately (.NET: the dependent is
// tracked only while the target is non-null).
void icall_dependenthandle_set_primary_and_secondary(intptr_t handle,
    Il2CppObject* primary, Il2CppObject* secondary);

// Free the handle: the weak slot is unregistered and both slots are cleared,
// so the primary and secondary become collectible at the next collection.
// Returns true on success, false for an already-freed/invalid handle.
bool icall_dependenthandle_free(intptr_t handle);

//------------------------------------------------------------------------------
// net48 static-extern n* adapters (System.Runtime.CompilerServices.
// DependentHandle.nInitialize / nGetPrimary / nGetPrimaryAndSecondary /
// nFree -- FSI-decoded mscorlib signatures). The BCL spells these as VOID
// methods writing through out-slots while the core icalls above RETURN
// their results -- these thin adapters bridge the shapes (no new table
// state, no new semantics). Out-slots address managed LOCALS (GC roots),
// so no write barrier is needed. The IntPtr& handle slot is int32-sized in
// the M-stage model; the handle (a small table index + 1) narrows to int32
// on store.
//------------------------------------------------------------------------------
// nInitialize(Object, Object, IntPtr&): alloc + store the handle.
void icall_dependenthandle_n_init(Il2CppObject* primary, Il2CppObject* secondary, int32_t* outHandle);
// nGetPrimary(IntPtr, Object&): write the primary through the out slot.
void icall_dependenthandle_n_get_primary(intptr_t handle, Il2CppObject** outPrimary);
// nGetPrimaryAndSecondary(IntPtr, Object&, Object&): return-primary shape
// bridged to two out slots.
void icall_dependenthandle_n_get_primary_and_secondary(intptr_t handle,
    Il2CppObject** outPrimary, Il2CppObject** outSecondary);

}} // namespace il2cpp::runtime
