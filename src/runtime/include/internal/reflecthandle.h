#pragma once
//------------------------------------------------------------------------------
// reflecthandle.h -- the reflection HANDLE value-type icall family
// (System.RuntimeTypeHandle / System.RuntimeMethodHandle /
// System.RuntimeFieldHandle).
//
// The three handle structs are VALUE TYPES wrapping an IntPtr -- .NET uses
// them as a cheap, allocation-free way to carry a type / method / field
// identity across the managed/native boundary (ldtoken produces them;
// Type.GetTypeFromHandle consumes them). This runtime models a handle VALUE
// as the address of its identity:
//   * RuntimeTypeHandle   = Il2CppClass*            (the type descriptor;
//     reflection-runtime-design.md §5.2: "RuntimeTypeHandle = Il2CppClass*
//     包装" -- the transpiler folds typeof(T) to &{T}_class, so the handle
//     the IL carries IS that descriptor address)
//   * RuntimeMethodHandle = const Il2CppMethodInfo* (the typeinfo method
//     table entry: declaringType + name + signature identity, typeinfo.h)
//   * RuntimeFieldHandle  = const Il2CppFieldInfo*  (the typeinfo field table
//     entry: name + typeName identity, typeinfo.h)
//
// MINIMAL semantic surface (net48 mscorlib handle semantics, probed on the
// reference machine; icall-inventory-core.md §8.2 -- the full FCall/QCall
// table -- GetAttributes/GetSlot/GetMethodDef/GetStaticFieldAddress etc. --
// is a documented later expansion, NOT this surface):
//   * get_value          -- handle.Value: the raw IntPtr (0 for the default
//                           handle, net48).
//   * equals             -- handle.Equals: handle identity (default==default
//                           is TRUE and default vs any real handle is FALSE,
//                           net48 probed).
//   * get_hash_code      -- handle.GetHashCode: deterministic, CONSISTENT
//                           with equals (null -> 0; else a name-based fold).
//                           Deliberately NOT the net48 address/pointer hash:
//                           AOT output must be deterministic (valuesemantics.h),
//                           and two distinct descriptors with the SAME name
//                           are equal (the typecheck name-equality rule), so
//                           an address hash would violate "equal keys -> equal
//                           hashes".
//   * get_runtime_type   -- handle -> the type VALUE (full-name string -- the
//                           simplified Type model); DELEGATES to the existing
//                           il2cpp_type_get_type_from_handle (typeext.cpp)
//                           after the null guard (no string-building logic
//                           duplicated). NULL semantics mirror the net48
//                           STATIC Type.GetTypeFromHandle(RuntimeTypeHandle)
//                           (probe-verified against v4.0.30319: default
//                           handle -> null, real handle -> the type).
//                           GetRuntimeType() itself is a .NET Core-shaped
//                           convenience -- NOT a net48 mscorlib member
//                           (probe: the member is absent on net48) -- but
//                           maps onto the SAME primitive, so the net48
//                           GetTypeFromHandle null contract is the correct
//                           semantic anchor.
//
// ALREADY COVERED (NOT duplicated here -- see the task research report):
//   * Type.GetTypeFromHandle -- il2cpp_type_get_type_from_handle (typeext.cpp
//     107) + the StackFold typeof fold (ldtoken + GetTypeFromHandle -> the
//     full-name string, StackFold.cs:1280-1287). The handle->Type direction
//     belongs to typeext, not here.
//   * Type identity -- icall_type_equals (typecheck.cpp:130). The
//     RuntimeTypeHandle.Equals below DELEGATES to it (a public declared API
//     in il2cpp_typecheck.h -- no conformance logic re-implemented).
//   * typeinfo member queries -- il2cpp_typeinfo_get_method/field/property
//     (typeinfo.cpp). Handle equality here only READS the table entries'
//     identity fields; it never queries the registry.
//
// Exception contract: every function is defensive by convention -- null
// inputs return 0 / nullptr ("default handle" semantics, mirroring net48
// default(RuntimeTypeHandle).Equals/GetHashCode and the STATIC
// Type.GetTypeFromHandle(default) which all succeed on the default handle;
// GetRuntimeType is the .NET Core-shaped convenience over the same
// primitive -- see the get_runtime_type bullet), never throw (the
// typecheck.cpp null-input convention).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_metadata.h" // Il2CppClass
#include "il2cpp_object.h"   // Il2CppObject (managed-string return slot)
#include "typeinfo.h"        // Il2CppMethodInfo / Il2CppFieldInfo

namespace il2cpp {

//------------------------------------------------------------------------------
// RuntimeTypeHandle (handle value = Il2CppClass* descriptor address)
//------------------------------------------------------------------------------

// handle.Value -> the raw handle IntPtr (the descriptor address as an
// intptr_t). null -> 0 (net48: default(RuntimeTypeHandle).Value == 0).
intptr_t il2cpp_rthandle_get_value(Il2CppClass* handle);

// handle.Equals(other) -> 1 when the two handles denote the same TYPE
// (delegates to icall_type_equals: descriptor identity fast path incl.
// both-null -> 1, then full-name equality). null vs non-null -> 0.
int32_t il2cpp_rthandle_equals(Il2CppClass* a, Il2CppClass* b);

// handle.GetHashCode() -> deterministic type-name hash (djb2 over the full
// type name, the value-semantics type-hash convention, valuesemantics.cpp:
// 419). null -> 0 (net48: default.GetHashCode() == 0). Consistent with
// equals: equal handles (same name) always hash equal.
int32_t il2cpp_rthandle_get_hash_code(Il2CppClass* handle);

// handle.GetRuntimeType() -> the type VALUE (full-name managed string, the
// simplified Type model -- the same output shape as
// il2cpp_type_get_type_from_handle). null -> nullptr -- mirrors the net48
// STATIC Type.GetTypeFromHandle(RuntimeTypeHandle) default-handle contract
// (probe-verified against v4.0.30319: default handle -> null, NOT a throw).
// NOTE: RuntimeTypeHandle.GetRuntimeType() itself is NOT a net48 member
// (probe: absent from net48 mscorlib) -- it is a .NET Core-shaped
// convenience mapping onto the same primitive, so the net48 GetTypeFromHandle
// contract is the anchor. Non-null delegates to il2cpp_type_get_type_from_handle.
Il2CppObject* il2cpp_rthandle_get_runtime_type(Il2CppClass* handle);

//------------------------------------------------------------------------------
// RuntimeMethodHandle (handle value = const Il2CppMethodInfo* table entry)
//------------------------------------------------------------------------------

// handle.Value -> the raw handle IntPtr (the method table entry address as
// an intptr_t). null -> 0.
intptr_t il2cpp_rmethodhandle_get_value(const Il2CppMethodInfo* handle);

// handle.Equals(other) -> 1 when both handles denote the SAME method
// (declaringType + name + signature all equal; identity fast path incl.
// both-null -> 1). null vs non-null -> 0. Note: equality is the full method
// identity, so overloads of the same name stay DISTINCT.
int32_t il2cpp_rmethodhandle_equals(const Il2CppMethodInfo* a, const Il2CppMethodInfo* b);

// handle.GetHashCode() -> deterministic fold of the method identity
// (declaringType, name, signature djb2 hashes, combined with the same
// djb2 step). null -> 0. Consistent with equals (identical identity fields
// -> identical hash).
int32_t il2cpp_rmethodhandle_get_hash_code(const Il2CppMethodInfo* handle);

//------------------------------------------------------------------------------
// RuntimeFieldHandle (handle value = const Il2CppFieldInfo* table entry)
//------------------------------------------------------------------------------

// handle.Value -> the raw handle IntPtr (the field table entry address as an
// intptr_t). null -> 0.
intptr_t il2cpp_rfieldhandle_get_value(const Il2CppFieldInfo* handle);

// handle.Equals(other) -> 1 when both handles denote the SAME field
// (name + typeName both equal; identity fast path incl. both-null -> 1).
// null vs non-null -> 0. DIVERGENCE from net48 field identity, documented:
// net48 field handles compare (declaringType, fieldName) -- probe-verified
// against v4.0.30319 that typeof(string).GetField("Empty") vs
// typeof(Guid).GetField("Empty") are NOT equal (same name, different
// declaring types). Il2CppFieldInfo carries NO declaring-type field
// (typeinfo.h; the transpiler emits name + typeName only -- TypeEmitter.cs),
// so the declaring-type dimension is not recoverable from the entry and the
// identity is approximated as (name, fieldTypeName). Same-named same-typed
// fields from DIFFERENT declaring types therefore compare EQUAL here vs
// DIFFERENT in net48 -- the residual divergence (pinned by the runtime test).
// Within a type the identity is exact (fields are name-unique per table).
int32_t il2cpp_rfieldhandle_equals(const Il2CppFieldInfo* a, const Il2CppFieldInfo* b);

// handle.GetHashCode() -> deterministic fold of the field identity
// (name + typeName djb2 hashes, combined with the same djb2 step).
// null -> 0. Consistent with equals.
int32_t il2cpp_rfieldhandle_get_hash_code(const Il2CppFieldInfo* handle);

} // namespace il2cpp
