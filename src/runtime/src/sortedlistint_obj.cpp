//------------------------------------------------------------------------------
// sortedlistint_obj.cpp -- the SortedList<string,int> AOT expansion (G4,
// STAGED -- see sortedlistint_obj.h for the design and semantics contract).
//
// Every entry point is a THIN forwarder to the sortedlist icalls (sortedlist.h)
// plus the box/unbox conversion at the C++ boundary and the net48 deltas
// (get_Item KNF, Remove's bool verdict). The core owns the sort order, the
// null-key -> ArgumentNullException contract, duplicate Add -> ArgumentException
// and the index range checks.
//------------------------------------------------------------------------------

#include "sortedlistint_obj.h"

#include "dictsi_obj.h" // Il2CppDictSIEnumerator + the pair-enumerator class
#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h" // runtime::icall_unbox_int32
#include "il2cpp_exception_ext.h" // il2cpp_throw_key_not_found_exception
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32
#include "sortedlist.h" // il2cpp::runtime::il2cpp_sortedlist_*

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

void icall_sortedlist_si_add(Il2CppObject* self, Il2CppObject* key, int32_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedlist_si_add_self");
    GcRoot<Il2CppObject*> keyRoot(key, "sortedlist_si_add_key");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, value);
    GcRoot<Il2CppObject*> valueRoot(boxed, "sortedlist_si_add_value");
    self = selfRoot.Get();
    key = keyRoot.Get();
    boxed = valueRoot.Get();
    il2cpp_sortedlist_add(self, key, boxed);
}

void icall_sortedlist_si_set_by_key(Il2CppObject* self, Il2CppObject* key, int32_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedlist_si_set_self");
    GcRoot<Il2CppObject*> keyRoot(key, "sortedlist_si_set_key");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, value);
    GcRoot<Il2CppObject*> valueRoot(boxed, "sortedlist_si_set_value");
    self = selfRoot.Get();
    key = keyRoot.Get();
    boxed = valueRoot.Get();
    il2cpp_sortedlist_set_by_key(self, key, boxed);
}

int32_t icall_sortedlist_si_get_by_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = il2cpp_sortedlist_get_by_key(self, key);
    if (boxed == nullptr)
    {
        // A stored value is always a boxed Int32, so a null slot means the
        // key is absent -- net48 raises KeyNotFoundException.
        il2cpp_throw_key_not_found_exception(nullptr);
    }
    return icall_unbox_int32(boxed);
}

bool icall_sortedlist_si_contains_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    return il2cpp_sortedlist_contains_key(self, key);
}

int32_t icall_sortedlist_si_remove(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (!il2cpp_sortedlist_contains_key(self, key))
    {
        return 0;
    }
    il2cpp_sortedlist_remove_by_key(self, key);
    return 1;
}

int32_t icall_sortedlist_si_get_by_index(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    return icall_unbox_int32(il2cpp_sortedlist_get_by_index(self, index));
}

void icall_sortedlist_si_set_at(Il2CppObject* self, int32_t index, int32_t value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedlist_si_set_at_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, value);
    self = selfRoot.Get();
    il2cpp_sortedlist_set_at(self, index, boxed);
}

Il2CppObject* icall_sortedlist_si_get_keys(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // The non-generic snapshot is an object[] holding the string keys in
    // ascending order; copy it into an exact-length reference array so the
    // tracked System.String[] downstream loads elements as string refs (the
    // queueint_obj to_array discipline).
    Il2CppObject* snapshot = il2cpp_sortedlist_get_keys(self);
    GcRoot<Il2CppObject*> root(snapshot, "sortedlist_si_get_keys");
    Il2CppArray* src = reinterpret_cast<Il2CppArray*>(snapshot);
    const int32_t length = src->length;
    Il2CppArray* dst = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, length);
    snapshot = root.Get();
    src = reinterpret_cast<Il2CppArray*>(snapshot);
    Il2CppObject** in = reinterpret_cast<Il2CppObject**>(src->data);
    Il2CppObject** out = reinterpret_cast<Il2CppObject**>(dst->data);
    for (int32_t i = 0; i < length; ++i)
    {
        out[i] = in[i];
    }
    return reinterpret_cast<Il2CppObject*>(dst);
}

Il2CppObject* icall_sortedlist_si_get_values(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // net48: Values is a LIVE IList<TValue> view over the list. Return the
    // receiver itself; the view arms (indexer -> si_get_by_index, Count ->
    // il2cpp_sortedlist_get_count) read the CURRENT entries, so the wrapper
    // adds no snapshot staleness. The foreach arm below snapshots at
    // GetEnumerator time (net48's enumerator is also captured at that point).
    return self;
}

Il2CppObject* icall_sortedlist_si_values_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // Fresh boxed-value snapshot + the core array-enumerator wrapper (the
    // non-generic ValueList GetEnumerator precedent). snapshot_get_enumerator
    // roots the array internally across its allocation.
    Il2CppObject* snapshot = il2cpp_sortedlist_get_values(self);
    return il2cpp_sortedlist_snapshot_get_enumerator(snapshot);
}

int32_t icall_sortedlist_si_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_unbox_int32(il2cpp_sortedlist_enumerator_get_current(self));
}

bool icall_sortedlist_si_try_get_value(Il2CppObject* self, Il2CppObject* key, int32_t* value)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = il2cpp_sortedlist_get_by_key(self, key);
    if (boxed == nullptr)
    {
        return false; // a stored value is never null, so null means "absent"
    }
    *value = icall_unbox_int32(boxed);
    return true;
}

Il2CppObject* icall_sortedlist_si_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // The keys snapshot is a fresh string[]; the values snapshot is a fresh
    // int32[] unboxed from the core boxed-value snapshot. Both allocations can
    // compact, so every managed local is re-read through a GcRoot.
    Il2CppObject* keysSnapshot = icall_sortedlist_si_get_keys(self);
    GcRoot<Il2CppObject*> keysRoot(keysSnapshot, "sortedlist_si_get_enumerator_keys");
    Il2CppArray* boxed = reinterpret_cast<Il2CppArray*>(il2cpp_sortedlist_get_values(self));
    GcRoot<Il2CppArray*> boxedRoot(boxed, "sortedlist_si_get_enumerator_boxed");
    const int32_t length = boxed->length;
    Il2CppArray* values = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(int32_t)), false, length);
    GcRoot<Il2CppArray*> valuesRoot(values, "sortedlist_si_get_enumerator_values");
    boxed = boxedRoot.Get();
    Il2CppObject** src = reinterpret_cast<Il2CppObject**>(boxed->data);
    int32_t* dst = reinterpret_cast<int32_t*>(values->data);
    for (int32_t i = 0; i < length; ++i)
    {
        dst[i] = src[i] != nullptr ? icall_unbox_int32(src[i]) : 0;
    }
    il2cpp_class_register(&System_Collections_Generic_Dictionary_2_Enumerator_string_int_class);
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(
        il2cpp_object_new(&System_Collections_Generic_Dictionary_2_Enumerator_string_int_class));
    en->keys = reinterpret_cast<Il2CppArray*>(keysRoot.Get());
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->keys),
                         reinterpret_cast<Il2CppObject*>(en->keys));
    en->values = valuesRoot.Get();
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->values),
                         reinterpret_cast<Il2CppObject*>(en->values));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

bool icall_sortedlist_si_contains_value(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    // No core contains_value icall: scan the values snapshot. Every element is
    // a boxed Int32; the loop allocates nothing, so no GC re-read is needed.
    Il2CppArray* snapshot = reinterpret_cast<Il2CppArray*>(il2cpp_sortedlist_get_values(self));
    const int32_t length = snapshot->length;
    Il2CppObject** elems = reinterpret_cast<Il2CppObject**>(snapshot->data);
    for (int32_t i = 0; i < length; ++i)
    {
        if (elems[i] != nullptr && icall_unbox_int32(elems[i]) == value)
        {
            return true;
        }
    }
    return false;
}

} // namespace runtime
} // namespace il2cpp
