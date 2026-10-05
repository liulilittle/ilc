#pragma once
//------------------------------------------------------------------------------
// stackint_obj.h -- the Stack<int/long/double> AOT expansion (T2 W4): thin
// boxing/unboxing wrappers over the NON-GENERIC stack runtime (stackqueue.h),
// exactly like queueint_obj.h for Queue<int>.
//
// The transpiler allocates the stack object through il2cpp_stack_class()
// (BclAllocExpr `Stack`1_int/long/double`) and maps the members here:
//   Push(T)    -> box + il2cpp_stack_push
//   Pop/Peek   -> il2cpp_stack_pop/peek + unbox
//   Contains   -> box + il2cpp_stack_contains (value-equality scan)
//   ToArray()  -> a typed array copy of the snapshot (top-first, net48 order)
//   GetEnumerator().get_Current -> unbox the element (B2 b2b8)
//   Count/Clear -> the non-generic icalls directly (resolver)
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "stackqueue.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

void icall_stack_int_push(Il2CppObject* self, int32_t value);
int32_t icall_stack_int_pop(Il2CppObject* self);
int32_t icall_stack_int_peek(Il2CppObject* self);
bool icall_stack_int_contains(Il2CppObject* self, int32_t value);
Il2CppObject* icall_stack_int_to_array(Il2CppObject* self);
int32_t icall_stack_int_enumerator_get_current(Il2CppObject* self);

void icall_stack_long_push(Il2CppObject* self, int64_t value);
int64_t icall_stack_long_pop(Il2CppObject* self);
int64_t icall_stack_long_peek(Il2CppObject* self);
bool icall_stack_long_contains(Il2CppObject* self, int64_t value);
Il2CppObject* icall_stack_long_to_array(Il2CppObject* self);
int64_t icall_stack_long_enumerator_get_current(Il2CppObject* self);

void icall_stack_double_push(Il2CppObject* self, double value);
double icall_stack_double_pop(Il2CppObject* self);
double icall_stack_double_peek(Il2CppObject* self);
bool icall_stack_double_contains(Il2CppObject* self, double value);
Il2CppObject* icall_stack_double_to_array(Il2CppObject* self);
double icall_stack_double_enumerator_get_current(Il2CppObject* self);

} // namespace runtime
} // namespace il2cpp
