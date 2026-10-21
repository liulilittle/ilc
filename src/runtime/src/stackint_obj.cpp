//------------------------------------------------------------------------------
// stackint_obj.cpp -- the Stack<int/long/double> AOT expansion (T2 W4).
//
// Thin boxing/unboxing wrappers over the NON-GENERIC stack runtime
// (stackqueue.h), exactly like queueint_obj.cpp for Queue<int>. Every boxing
// allocation can compact the heap, so the receiver is re-read from its GcRoot
// before each non-generic call; the stack's slots root the stored boxes
// themselves (reference-array items). Contains reuses the boxed value-equality
// path (SeqElementEquals covers boxed Int32/Int64/Double payloads).
//------------------------------------------------------------------------------

#include "stackint_obj.h"

#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h" // runtime::icall_unbox_int32/icall_box_double
#include "il2cpp_boxing_ext2.h" // runtime::icall_box_long/icall_unbox_long
#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32

namespace il2cpp {
namespace runtime {

namespace {

void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

} // namespace

void icall_stack_int_push(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "stack_int_push_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, value);
    GcRoot<Il2CppObject*> itemRoot(boxed, "stack_int_push_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    il2cpp_stack_push(self, boxed);
}

int32_t icall_stack_int_pop(Il2CppObject* self)
{
    return icall_unbox_int32(il2cpp_stack_pop(self));
}

int32_t icall_stack_int_peek(Il2CppObject* self)
{
    return icall_unbox_int32(il2cpp_stack_peek(self));
}

bool icall_stack_int_contains(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "stack_int_contains_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, value);
    self = selfRoot.Get();
    return il2cpp_stack_contains(self, boxed);
}

int32_t icall_stack_int_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_int32(il2cpp_stack_enumerator_get_current(self));
}

Il2CppObject* icall_stack_int_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snapshot = il2cpp_stack_to_array(self);
    GcRoot<Il2CppObject*> root(snapshot, "stack_int_to_array");
    Il2CppArray* src = reinterpret_cast<Il2CppArray*>(snapshot);
    const int32_t length = src->length;
    Il2CppArray* dst = il2cpp_array_new(nullptr, sizeof(int32_t), false, length);
    snapshot = root.Get();
    src = reinterpret_cast<Il2CppArray*>(snapshot);
    int32_t* out = reinterpret_cast<int32_t*>(dst->data);
    Il2CppObject** in = reinterpret_cast<Il2CppObject**>(src->data);
    for (int32_t i = 0; i < length; ++i)
    {
        out[i] = in[i] != nullptr ? icall_unbox_int32(in[i]) : 0;
    }
    return reinterpret_cast<Il2CppObject*>(dst);
}

void icall_stack_long_push(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "stack_long_push_self");
    Il2CppObject* boxed = icall_box_long(value);
    GcRoot<Il2CppObject*> itemRoot(boxed, "stack_long_push_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    il2cpp_stack_push(self, boxed);
}

int64_t icall_stack_long_pop(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_stack_pop(self));
}

int64_t icall_stack_long_peek(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_stack_peek(self));
}

bool icall_stack_long_contains(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "stack_long_contains_self");
    Il2CppObject* boxed = icall_box_long(value);
    self = selfRoot.Get();
    return il2cpp_stack_contains(self, boxed);
}

int64_t icall_stack_long_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_stack_enumerator_get_current(self));
}

Il2CppObject* icall_stack_long_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snapshot = il2cpp_stack_to_array(self);
    GcRoot<Il2CppObject*> root(snapshot, "stack_long_to_array");
    Il2CppArray* src = reinterpret_cast<Il2CppArray*>(snapshot);
    const int32_t length = src->length;
    Il2CppArray* dst = il2cpp_array_new_typed(sizeof(int64_t), 2, length);
    snapshot = root.Get();
    src = reinterpret_cast<Il2CppArray*>(snapshot);
    int64_t* out = reinterpret_cast<int64_t*>(dst->data);
    Il2CppObject** in = reinterpret_cast<Il2CppObject**>(src->data);
    for (int32_t i = 0; i < length; ++i)
    {
        out[i] = in[i] != nullptr ? icall_unbox_long(in[i]) : 0;
    }
    return reinterpret_cast<Il2CppObject*>(dst);
}

void icall_stack_double_push(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "stack_double_push_self");
    Il2CppObject* boxed = icall_box_double(value);
    GcRoot<Il2CppObject*> itemRoot(boxed, "stack_double_push_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    il2cpp_stack_push(self, boxed);
}

double icall_stack_double_pop(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_stack_pop(self));
}

double icall_stack_double_peek(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_stack_peek(self));
}

bool icall_stack_double_contains(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "stack_double_contains_self");
    Il2CppObject* boxed = icall_box_double(value);
    self = selfRoot.Get();
    return il2cpp_stack_contains(self, boxed);
}

double icall_stack_double_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_stack_enumerator_get_current(self));
}

Il2CppObject* icall_stack_double_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snapshot = il2cpp_stack_to_array(self);
    GcRoot<Il2CppObject*> root(snapshot, "stack_double_to_array");
    Il2CppArray* src = reinterpret_cast<Il2CppArray*>(snapshot);
    const int32_t length = src->length;
    Il2CppArray* dst = il2cpp_array_new_typed(sizeof(double), 4, length);
    snapshot = root.Get();
    src = reinterpret_cast<Il2CppArray*>(snapshot);
    double* out = reinterpret_cast<double*>(dst->data);
    Il2CppObject** in = reinterpret_cast<Il2CppObject**>(src->data);
    for (int32_t i = 0; i < length; ++i)
    {
        out[i] = in[i] != nullptr ? icall_unbox_double(in[i]) : 0.0;
    }
    return reinterpret_cast<Il2CppObject*>(dst);
}

} // namespace runtime
} // namespace il2cpp
