//------------------------------------------------------------------------------
// argiterator.cpp -- the varargs (__arglist) runtime model.
// See il2cpp_argiterator.h for the frame layout contract, the calling-convention
// decision and the net48 measurements this implements.
//
// Every read/write of the iterator fields happens here. The TypedReference
// carrier is the existing il2cpp_typedref_make accessor (the same identity
// mkrefany / __reftype / refanyval use) -- no second carrier.
//------------------------------------------------------------------------------

#include "il2cpp_argiterator.h"

#include "il2cpp_exception.h"

namespace il2cpp {
namespace runtime {

namespace {

// Managed exception type chains (comma-separated base chain, first segment is
// the exact type) -- the same shape every other runtime throw site uses so a
// generated catch (System.Exception / System.SystemException) matches.
const char kInvalidOperationChain[] =
    "System.InvalidOperationException,System.InvalidOperationException,"
    "System.SystemException,System.Exception";
const char kInvalidCastChain[] =
    "System.InvalidCastException,System.InvalidCastException,"
    "System.SystemException,System.Exception";
const char kNotSupportedChain[] =
    "System.NotSupportedException,System.NotSupportedException,"
    "System.SystemException,System.Exception";

// net48 message texts for the two boundary cases (the probe compares types).
const char kEmptyArgList[] = "ArgIterator is at the end of the argument list.";
const char kInvalidArgType[] = "Object cannot be converted to the type of the TypedReference.";
const char kBadArgCookie[] = "The argument handle is not valid.";

bool HasValidCookie(const Il2CppArgIterator* self)
{
    return self != nullptr && self->cookie == kArgIteratorCookie;
}

} // namespace

void icall_argiterator_ctor(Il2CppArgIterator* self, intptr_t handle)
{
    if (self == nullptr) return;
    self->frame = reinterpret_cast<const Il2CppArgFrame*>(handle);
    self->pos = 0;
    self->count = self->frame != nullptr ? self->frame->count : 0;
    self->cookie = kArgIteratorCookie;
}

int32_t icall_argiterator_get_remaining_count(Il2CppArgIterator* self)
{
    // net48 validates the handle cookie here (default(ArgIterator) ->
    // NotSupportedException, probe-measured).
    if (!HasValidCookie(self))
        il2cpp_throw_new(kNotSupportedChain, kBadArgCookie);
    const int32_t remaining = self->count - self->pos;
    return remaining > 0 ? remaining : 0;
}

Il2CppTypedReference icall_argiterator_get_next_arg(Il2CppArgIterator* self)
{
    // Order matters: a default iterator has cookie 0 AND count 0; net48 answers
    // InvalidOperationException for GetNextArg in that state (measured), while
    // GetRemainingCount answers NotSupportedException.
    if (self == nullptr || self->pos >= self->count)
        il2cpp_throw_new(kInvalidOperationChain, kEmptyArgList);
    const Il2CppClass* type = self->frame->types[self->pos];
    void* value = self->frame->values[self->pos];
    self->pos++;
    return il2cpp_typedref_make(const_cast<Il2CppClass*>(type), value);
}

Il2CppTypedReference icall_argiterator_get_next_arg_typed(Il2CppArgIterator* self,
                                                          intptr_t expected)
{
    if (self == nullptr || self->pos >= self->count)
        il2cpp_throw_new(kInvalidOperationChain, kEmptyArgList);
    const Il2CppClass* type = self->frame->types[self->pos];
    void* value = self->frame->values[self->pos];
    // net48's managed GetNextArg(RuntimeTypeHandle) consumes BEFORE the exact
    // type check (measured: a mismatch still leaves GetRemainingCount at 0).
    // The handle argument is the RuntimeTypeHandle CARRIER (intptr_t; the
    // transpiler's handle model is the descriptor address).
    self->pos++;
    const Il2CppClass* expectedType = reinterpret_cast<const Il2CppClass*>(expected);
    if (expectedType == nullptr || type != expectedType)
        il2cpp_throw_new(kInvalidCastChain, kInvalidArgType);
    return il2cpp_typedref_make(const_cast<Il2CppClass*>(type), value);
}

intptr_t icall_argiterator_get_next_arg_type(Il2CppArgIterator* self)
{
    if (!HasValidCookie(self))
        il2cpp_throw_new(kNotSupportedChain, kBadArgCookie);
    if (self->pos >= self->count)
        il2cpp_throw_new(kInvalidOperationChain, kEmptyArgList);
    // Peek: the offset does not advance (measured: two consecutive calls answer
    // the same type and GetRemainingCount is unchanged).
    return reinterpret_cast<intptr_t>(self->frame->types[self->pos]);
}

void icall_argiterator_end(Il2CppArgIterator* self)
{
    // net48's public End() is an empty method.
    static_cast<void>(self);
}

int32_t icall_argiterator_get_hash_code(Il2CppArgIterator* self)
{
    // net48: ValueType.GetHashCodeOfPtr(ArgCookie). The frame handle is this
    // runtime's cookie payload -- a splitmix-style mix keeps the answer
    // pointer-derived (not part of the byte-compare probe: non-deterministic).
    uintptr_t h = reinterpret_cast<uintptr_t>(self != nullptr ? self->frame : nullptr);
    h ^= h >> 33;
    h *= static_cast<uintptr_t>(0xff51afd7ed558ccdULL);
    h ^= h >> 33;
    return static_cast<int32_t>(h ^ (h >> 32));
}

bool icall_argiterator_equals(Il2CppArgIterator* self, void* other)
{
    // net48: throw new NotSupportedException(Environment.GetResourceString(
    // "NotSupported_NYI")). ArgIterator is a non-boxable special type, so C#
    // cannot even call this; kept for IL-level callers.
    static_cast<void>(self);
    static_cast<void>(other);
    il2cpp_throw_new(kNotSupportedChain, "ArgIterator.Equals is not supported.");
}

} // namespace runtime
} // namespace il2cpp
