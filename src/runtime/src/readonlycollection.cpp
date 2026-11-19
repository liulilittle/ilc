//------------------------------------------------------------------------------
// readonlycollection.cpp -- System.Collections.ObjectModel.ReadOnlyCollection<T>.
//
// See readonlycollection.h for the layout and the GC contract. Every member
// delegates to the wrapped array, so the wrapper is a VIEW: a write through the
// original array is visible through it (net48 contract), and no element is
// copied.
//------------------------------------------------------------------------------

#include "readonlycollection.h"

#include "arrayextra.h"      // il2cpp_array_get_value_at_offset (boxed element read)
#include "il2cpp_array.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"
#include "sortedlist.h"      // Il2CppArrayEnumerator + its class descriptor

#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

Il2CppClass g_readOnlyCollectionClass = {
    "System.Collections.ObjectModel.ReadOnlyCollection`1",
    static_cast<int32_t>(sizeof(Il2CppReadOnlyCollection)),
    0b00010u,   // refFieldBitmap: bit 1 = the wrapped array
    1,          // refFieldCount
    nullptr,    // baseClass
    nullptr,    // vtable
    0,          // vtableSize
    nullptr,    // interfaces
    0,          // interfaceCount
};

bool g_readOnlyCollectionRegistered = false;

void EnsureRegistered()
{
    if (g_readOnlyCollectionRegistered) return;
    il2cpp_class_register(&g_readOnlyCollectionClass);
    g_readOnlyCollectionRegistered = true;
}

Il2CppReadOnlyCollection* AsRoc(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppReadOnlyCollection*>(self);
}

void ThrowIfNull(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// The wrapped array, validated: a null receiver is a NullReferenceException and
// a wrapper whose list was never set is a caller bug (the only producer is
// il2cpp_readonlycollection_create).
Il2CppArray* RequireList(Il2CppObject* self)
{
    ThrowIfNull(self);
    Il2CppArray* a = AsRoc(self)->list;
    if (a == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    return a;
}

// Bounds check against the wrapped array's length (net48 indexer semantics:
// ArgumentOutOfRangeException, not IndexOutOfRangeException).
void RequireIndex(Il2CppArray* a, int32_t index)
{
    if (index < 0 || index >= a->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "index", "Index was out of range. Must be non-negative and less "
                     "than the size of the collection.");
    }
}

} // namespace

Il2CppClass* il2cpp_readonlycollection_class()
{
    EnsureRegistered();
    return &g_readOnlyCollectionClass;
}

Il2CppObject* il2cpp_readonlycollection_create(Il2CppObject* array)
{
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    EnsureRegistered();
    // il2cpp_object_new may compact and MOVE the array, so root it and re-read
    // the (possibly moved) address before storing it into the GC-scanned field.
    GcRoot<Il2CppObject*> root(array, "il2cpp_readonlycollection_create");
    Il2CppReadOnlyCollection* roc = reinterpret_cast<Il2CppReadOnlyCollection*>(
        il2cpp_object_new(&g_readOnlyCollectionClass));
    array = root.Get();
    roc->list = reinterpret_cast<Il2CppArray*>(array);
    il2cpp_write_barrier(reinterpret_cast<void**>(&roc->list),
                         reinterpret_cast<Il2CppObject*>(roc->list));
    return reinterpret_cast<Il2CppObject*>(roc);
}

int32_t il2cpp_readonlycollection_get_count(Il2CppObject* self)
{
    return RequireList(self)->length;
}

int32_t il2cpp_readonlycollection_get_item_i4(Il2CppObject* self, int32_t index)
{
    Il2CppArray* a = RequireList(self);
    RequireIndex(a, index);
    return *reinterpret_cast<const int32_t*>(
        il2cpp_array_get_elem_ptr(a, index));
}

Il2CppObject* il2cpp_readonlycollection_get_item_obj(Il2CppObject* self, int32_t index)
{
    Il2CppArray* a = RequireList(self);
    RequireIndex(a, index);
    return il2cpp_array_get_value_at_offset(a, index);
}

bool il2cpp_readonlycollection_contains_i4(Il2CppObject* self, int32_t value)
{
    return il2cpp_readonlycollection_index_of_i4(self, value) >= 0;
}

int32_t il2cpp_readonlycollection_index_of_i4(Il2CppObject* self, int32_t value)
{
    Il2CppArray* a = RequireList(self);
    for (int32_t i = 0; i < a->length; ++i)
    {
        const int32_t cur = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (cur == value) return i;
    }
    return -1;
}

bool il2cpp_readonlycollection_contains_obj(Il2CppObject* self, Il2CppObject* value)
{
    return il2cpp_readonlycollection_index_of_obj(self, value) >= 0;
}

int32_t il2cpp_readonlycollection_index_of_obj(Il2CppObject* self, Il2CppObject* value)
{
    Il2CppArray* a = RequireList(self);
    for (int32_t i = 0; i < a->length; ++i)
    {
        Il2CppObject* cur = il2cpp_array_get_value_at_offset(a, i);
        if (cur == value) return i;
    }
    return -1;
}

void il2cpp_readonlycollection_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t index)
{
    Il2CppArray* src = RequireList(self);
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    Il2CppArray* dst = reinterpret_cast<Il2CppArray*>(array);
    if (index < 0 || index > dst->length - src->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "index", "Index was out of range. Must be non-negative and less "
                     "than or equal to the size of the collection minus the "
                     "number of elements to copy.");
    }
    // Element-wise copy through the raw payload: the wrapper and the array
    // share the element stride, so a byte copy of the payload range is exact
    // for both value and reference elements (a reference copy moves the slot,
    // which is what net48 CopyTo does).
    const size_t bytes = static_cast<size_t>(src->length) *
                         static_cast<size_t>(src->elemSize);
    std::memcpy(dst->data + static_cast<size_t>(index) * static_cast<size_t>(dst->elemSize),
                src->data, bytes);
    if (dst->isRefElem)
    {
        for (int32_t i = 0; i < src->length; ++i)
        {
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                dst->data + static_cast<size_t>(index + i) * sizeof(void*));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), *slot);
        }
    }
}

Il2CppObject* il2cpp_readonlycollection_get_enumerator(Il2CppObject* self)
{
    Il2CppArray* a = RequireList(self);
    // GcRoot binds an lvalue, so the array is held in a named slot.
    Il2CppObject* rooted = reinterpret_cast<Il2CppObject*>(a);
    GcRoot<Il2CppObject*> root(rooted, "il2cpp_readonlycollection_get_enumerator");
    Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(
        il2cpp_object_new(&System_Collections_SortedList_Enumerator_class));
    a = reinterpret_cast<Il2CppArray*>(root.Get());
    en->array = a;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->array),
                         reinterpret_cast<Il2CppObject*>(en->array));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

} // namespace il2cpp
