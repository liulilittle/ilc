#pragma once
//------------------------------------------------------------------------------
// il2cpp_argiterator.h -- the varargs (__arglist) runtime model.
//
// ECMA-335 III.3.9 / II.23.2.2: `arglist` pushes the RuntimeArgumentHandle of
// the CURRENT method; an ArgIterator walks the caller's optional arguments.
//
// Calling convention chosen by this transpiler: an EXPLICIT TYPE FRAME, not a
// C `...` list. A native vararg list cannot carry the DECLARED type of each
// element (net48 `__reftype` MUST answer System.Int32 / System.String /
// System.Double / the user struct / the enum -- probe-measured), and va_list is
// single-consumption state that cannot live in a copyable managed struct whose
// copies advance independently (also probe-measured). The call-site MemberRef
// signature already carries the declared element types, so the CALLER builds:
//
//   const Il2CppClass* types[N]  -- the declared type descriptor per element
//   void*              values[N] -- the address of the caller-side slot
//   Il2CppArgFrame               -- { types, values, N }
//
// and passes the frame handle to the callee's trailing `intptr_t va_frame`
// parameter (the RuntimeArgumentHandle carrier). The generated call site owns
// the slot storage (an immediately-invoked lambda), so the addresses stay live
// for exactly the callee's call -- net48's vararg slot lifetime.
//
// The generated code is the ONLY writer of `Il2CppArgIterator`; it is a plain
// copyable value (a local, a temp, a managed struct copy). Every FIELD is read
// and written exclusively by this translation unit (the accessors below); no
// generated call site touches an offset.
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_typedref.h"

namespace il2cpp {

struct Il2CppClass;

namespace runtime {

// The caller-built typed argument frame. `values[i]` is the ADDRESS of the
// argument's storage, so any element size (primitive, pointer, arbitrary
// struct) round-trips through the TypedReference unchanged.
struct Il2CppArgFrame
{
    const Il2CppClass* const* types; // declared type descriptor per element
    void* const* values;             // address of the element storage
    int32_t count;                   // number of optional arguments
};

// The managed System.ArgIterator value (net48 shape: handle + consumed offset +
// remaining count + a validity cookie; the cookie reproduces net48's
// NotSupportedException for default(ArgIterator), measured).
struct Il2CppArgIterator
{
    const Il2CppArgFrame* frame;
    int32_t pos;     // next element index
    int32_t count;   // total elements of `frame`
    uint32_t cookie; // kArgIteratorCookie once constructed, 0 for default
};

// Magic stamp applied by the constructor (net48 ArgCookie role).
constexpr uint32_t kArgIteratorCookie = 0x9E3779B9u;

// ArgIterator(RuntimeArgumentHandle): bind to the caller's frame. `handle` is
// null for a malformed/absent frame -> an empty iterator (GetNextArg then
// reports InvalidOperationException, matching an empty __arglist).
void icall_argiterator_ctor(Il2CppArgIterator* self, intptr_t handle);

// GetRemainingCount(): number of arguments not yet consumed (decrements).
int32_t icall_argiterator_get_remaining_count(Il2CppArgIterator* self);

// GetNextArg(): the next argument as a TypedReference (declared type + storage
// address), consuming it. Past the end -> InvalidOperationException.
Il2CppTypedReference icall_argiterator_get_next_arg(Il2CppArgIterator* self);

// GetNextArg(RuntimeTypeHandle): consume the next argument and require the
// EXACT declared type (net48: mismatch -> InvalidCastException, and the element
// is still consumed). Past the end -> InvalidOperationException.
Il2CppTypedReference icall_argiterator_get_next_arg_typed(Il2CppArgIterator* self,
                                                          intptr_t expected);

// _GetNextArgType(): the next argument's declared type handle, WITHOUT
// consuming. Past the end -> InvalidOperationException; default iterator ->
// NotSupportedException.
intptr_t icall_argiterator_get_next_arg_type(Il2CppArgIterator* self);

// End(): net48's public End() is an empty method; kept as a no-op icall.
void icall_argiterator_end(Il2CppArgIterator* self);

// GetHashCode(): net48 hashes the ArgCookie pointer (pointer-derived; this
// runtime hashes the frame handle the same way -- deliberately not part of the
// byte-compare probe).
int32_t icall_argiterator_get_hash_code(Il2CppArgIterator* self);

// Equals(object): net48 throws NotSupportedException("NotSupported_NYI") --
// ArgIterator is a non-boxable special type, so C# cannot even call this.
bool icall_argiterator_equals(Il2CppArgIterator* self, void* other);

} // namespace runtime
} // namespace il2cpp
