//------------------------------------------------------------------------------
// queueint_obj.cpp -- the Queue<int> AOT expansion (G4); see queueint_obj.h.
//
// Every boxing allocation can compact the heap, so the receiver is re-read
// from its GcRoot before each non-generic call; the queue's slots root the
// stored boxes themselves (reference-array items).
//------------------------------------------------------------------------------

#include "queueint_obj.h"

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

void icall_queue_int_enqueue(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "queue_int_enqueue_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, value);
    GcRoot<Il2CppObject*> itemRoot(boxed, "queue_int_enqueue_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    il2cpp_queue_enqueue(self, boxed);
}

int32_t icall_queue_int_dequeue(Il2CppObject* self)
{
    return icall_unbox_int32(il2cpp_queue_dequeue(self));
}

int32_t icall_queue_int_peek(Il2CppObject* self)
{
    return icall_unbox_int32(il2cpp_queue_peek(self));
}

bool icall_queue_int_contains(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "queue_int_contains_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, value);
    self = selfRoot.Get();
    return il2cpp_queue_contains(self, boxed);
}

int32_t icall_queue_int_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_int32(il2cpp_queue_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W4: Queue<long> -- boxed-int64 elements over the non-generic queue slots.
// Contains reuses the boxed-Int64 value-equality path (SeqElementEquals covers
// boxed Int64 payloads); ToArray materialises a real int64[] in HEAD-first
// order (the int precedent above).
//------------------------------------------------------------------------------
void icall_queue_long_enqueue(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "queue_long_enqueue_self");
    Il2CppObject* boxed = icall_box_long(value);
    GcRoot<Il2CppObject*> itemRoot(boxed, "queue_long_enqueue_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    il2cpp_queue_enqueue(self, boxed);
}

int64_t icall_queue_long_dequeue(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_queue_dequeue(self));
}

int64_t icall_queue_long_peek(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_queue_peek(self));
}

bool icall_queue_long_contains(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "queue_long_contains_self");
    Il2CppObject* boxed = icall_box_long(value);
    self = selfRoot.Get();
    return il2cpp_queue_contains(self, boxed);
}

int64_t icall_queue_long_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_queue_enumerator_get_current(self));
}

Il2CppObject* icall_queue_long_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snapshot = il2cpp_queue_to_array(self);
    GcRoot<Il2CppObject*> root(snapshot, "queue_long_to_array");
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

//------------------------------------------------------------------------------
// T2 W4: Queue<double> -- boxed-double elements, same shape as long.
//------------------------------------------------------------------------------
void icall_queue_double_enqueue(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "queue_double_enqueue_self");
    Il2CppObject* boxed = icall_box_double(value);
    GcRoot<Il2CppObject*> itemRoot(boxed, "queue_double_enqueue_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    il2cpp_queue_enqueue(self, boxed);
}

double icall_queue_double_dequeue(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_queue_dequeue(self));
}

double icall_queue_double_peek(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_queue_peek(self));
}

bool icall_queue_double_contains(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "queue_double_contains_self");
    Il2CppObject* boxed = icall_box_double(value);
    self = selfRoot.Get();
    return il2cpp_queue_contains(self, boxed);
}

double icall_queue_double_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_queue_enumerator_get_current(self));
}

Il2CppObject* icall_queue_double_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snapshot = il2cpp_queue_to_array(self);
    GcRoot<Il2CppObject*> root(snapshot, "queue_double_to_array");
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

Il2CppObject* icall_queue_int_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // The non-generic snapshot is an object[] in HEAD-first order; copy it
    // into an exact-length int32[] (the tracked System.Int32[] downstream).
    Il2CppObject* snapshot = il2cpp_queue_to_array(self);
    GcRoot<Il2CppObject*> root(snapshot, "queue_int_to_array");
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

} // namespace runtime
} // namespace il2cpp
