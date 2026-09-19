#pragma once
//------------------------------------------------------------------------------
// queueint_obj.h -- the Queue<int> AOT expansion (G4): thin boxing/unboxing
// wrappers over the NON-GENERIC queue runtime (stackqueue.h), exactly like
// the dictss/listobj value-domain expansions.
//
// The transpiler allocates the queue object through il2cpp_queue_class()
// (BclAllocExpr `Queue`1_int`) and maps the members here:
//   Enqueue(int) -> box + il2cpp_queue_enqueue
//   Dequeue/Peek -> il2cpp_queue_dequeue/peek + unbox
//   Contains(int)-> box + il2cpp_queue_contains (value-equality scan)
//   ToArray()    -> an int32[] copy of the object[] snapshot (HEAD-first,
//                   net48 Queue.ToArray order)
//   GetEnumerator().get_Current -> unbox the boxed int (B2 b2b8)
//   Count/Clear  -> the non-generic icalls directly (resolver)
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "stackqueue.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

void icall_queue_int_enqueue(Il2CppObject* self, int32_t value);
int32_t icall_queue_int_dequeue(Il2CppObject* self);
int32_t icall_queue_int_peek(Il2CppObject* self);
bool icall_queue_int_contains(Il2CppObject* self, int32_t value);
Il2CppObject* icall_queue_int_to_array(Il2CppObject* self);
// T2 W4: long/double families (queueint_obj.cpp).
void icall_queue_long_enqueue(Il2CppObject* self, int64_t value);
int64_t icall_queue_long_dequeue(Il2CppObject* self);
int64_t icall_queue_long_peek(Il2CppObject* self);
bool icall_queue_long_contains(Il2CppObject* self, int64_t value);
Il2CppObject* icall_queue_long_to_array(Il2CppObject* self);
void icall_queue_double_enqueue(Il2CppObject* self, double value);
double icall_queue_double_dequeue(Il2CppObject* self);
double icall_queue_double_peek(Il2CppObject* self);
bool icall_queue_double_contains(Il2CppObject* self, double value);
Il2CppObject* icall_queue_double_to_array(Il2CppObject* self);

// GetEnumerator().get_Current: the current boxed-int element unboxed (the
// enumerator object is the shared non-generic snapshot enumerator).
int32_t icall_queue_int_enumerator_get_current(Il2CppObject* self);
int64_t icall_queue_long_enumerator_get_current(Il2CppObject* self);
double icall_queue_double_enumerator_get_current(Il2CppObject* self);

} // namespace runtime
} // namespace il2cpp
