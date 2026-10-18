//------------------------------------------------------------------------------
// sortedset_obj.cpp -- the SortedSet<int> AOT expansion (G4, STAGED -- see
// sortedset_obj.h for the design and semantics contract).
//
// Every entry point is a THIN forwarder to the sortedlist icalls (sortedlist.h)
// plus the box/unbox conversion at the C++ boundary and the net48 deltas
// (Add's duplicate -> false verdict, Min/Max's empty-set
// InvalidOperationException). The core owns the ascending sort order and the
// duplicate-key ArgumentException contract (pre-checked here) and the index
// range checks.
//------------------------------------------------------------------------------

#include "sortedset_obj.h"

#include "dictsi_obj.h" // il2cpp_dict_si_keycollection_*
#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h" // runtime::icall_unbox_int32/icall_box_double
#include "il2cpp_boxing_ext2.h" // runtime::icall_box_long/icall_unbox_long
#include "il2cpp_delegate.h" // RemoveWhere callback ABI
#include "il2cpp_exception_ext.h" // il2cpp_throw_{invalid_operation,null_reference}_exception
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32
#include "sortedlist.h" // il2cpp::runtime::il2cpp_sortedlist_*

#include <vector>

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

bool icall_sortedset_int_add(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_add_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "sortedset_int_add_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (il2cpp_sortedlist_contains_key(self, boxed))
    {
        return false; // net48: Add on a member is a no-op that returns false
    }
    il2cpp_sortedlist_add(self, boxed, boxed);
    return true;
}

bool icall_sortedset_int_contains(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_contains_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, item);
    self = selfRoot.Get();
    return il2cpp_sortedlist_contains_key(self, boxed);
}

bool icall_sortedset_int_remove(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_remove_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "sortedset_int_remove_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (!il2cpp_sortedlist_contains_key(self, boxed))
    {
        return false;
    }
    il2cpp_sortedlist_remove_by_key(self, boxed);
    return true;
}

int32_t icall_sortedset_int_get_min(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (il2cpp_sortedlist_get_count(self) == 0)
    {
        return 0; // net48: empty SortedSet<int>.Min returns default(int)
    }
    return icall_unbox_int32(il2cpp_sortedlist_get_key_at(self, 0));
}

int32_t icall_sortedset_int_get_max(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const int32_t count = il2cpp_sortedlist_get_count(self);
    if (count == 0)
    {
        return 0; // net48: empty Max returns default(int)
    }
    return icall_unbox_int32(il2cpp_sortedlist_get_key_at(self, count - 1));
}

int32_t icall_sortedset_int_enumerator_get_current(Il2CppObject* self)
{
    // net48 SortedSet<int>.Enumerator.Current before MoveNext / after exhaustion
    // returns default(int) == 0; shared dictsi enumerator throws (Dictionary contract).
    const Il2CppDictSIEnumerator* en = reinterpret_cast<const Il2CppDictSIEnumerator*>(self);
    if (en == nullptr || en->keys == nullptr || en->index < 0 || en->index >= en->keys->length)
    {
        return 0;
    }
    return icall_unbox_int32(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W2: SortedSet<long> -- boxed-int64 keys over the same sortedlist storage.
// Ordering comes from the extended CompareKeys (value order for System.Int64).
// Empty Min/Max return default(long) == 0 to match the net48 oracle.
//------------------------------------------------------------------------------
bool icall_sortedset_long_add(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_long_add_self");
    Il2CppObject* boxed = icall_box_long(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "sortedset_long_add_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (il2cpp_sortedlist_contains_key(self, boxed))
    {
        return false;
    }
    il2cpp_sortedlist_add(self, boxed, boxed);
    return true;
}

bool icall_sortedset_long_contains(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_long_contains_self");
    Il2CppObject* boxed = icall_box_long(item);
    self = selfRoot.Get();
    return il2cpp_sortedlist_contains_key(self, boxed) != 0;
}

bool icall_sortedset_long_remove(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_long_remove_self");
    Il2CppObject* boxed = icall_box_long(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "sortedset_long_remove_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (!il2cpp_sortedlist_contains_key(self, boxed))
    {
        return false;
    }
    il2cpp_sortedlist_remove_by_key(self, boxed);
    return true;
}

int64_t icall_sortedset_long_get_min(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (il2cpp_sortedlist_get_count(self) == 0)
    {
        return 0;
    }
    return icall_unbox_long(il2cpp_sortedlist_get_key_at(self, 0));
}

int64_t icall_sortedset_long_get_max(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const int32_t count = il2cpp_sortedlist_get_count(self);
    if (count == 0)
    {
        return 0;
    }
    return icall_unbox_long(il2cpp_sortedlist_get_key_at(self, count - 1));
}

int64_t icall_sortedset_long_enumerator_get_current(Il2CppObject* self)
{
    const Il2CppDictSIEnumerator* en = reinterpret_cast<const Il2CppDictSIEnumerator*>(self);
    if (en == nullptr || en->keys == nullptr || en->index < 0 || en->index >= en->keys->length)
    {
        return 0;
    }
    return icall_unbox_long(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W2: SortedSet<double> -- boxed-double keys, value order with the .NET
// total order (NaN last, +0.0 == -0.0; see CompareKeys).
//------------------------------------------------------------------------------
bool icall_sortedset_double_add(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_double_add_self");
    Il2CppObject* boxed = icall_box_double(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "sortedset_double_add_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (il2cpp_sortedlist_contains_key(self, boxed))
    {
        return false;
    }
    il2cpp_sortedlist_add(self, boxed, boxed);
    return true;
}

bool icall_sortedset_double_contains(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_double_contains_self");
    Il2CppObject* boxed = icall_box_double(item);
    self = selfRoot.Get();
    return il2cpp_sortedlist_contains_key(self, boxed) != 0;
}

bool icall_sortedset_double_remove(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_double_remove_self");
    Il2CppObject* boxed = icall_box_double(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "sortedset_double_remove_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (!il2cpp_sortedlist_contains_key(self, boxed))
    {
        return false;
    }
    il2cpp_sortedlist_remove_by_key(self, boxed);
    return true;
}

double icall_sortedset_double_get_min(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (il2cpp_sortedlist_get_count(self) == 0)
    {
        return 0.0;
    }
    return icall_unbox_double(il2cpp_sortedlist_get_key_at(self, 0));
}

double icall_sortedset_double_get_max(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const int32_t count = il2cpp_sortedlist_get_count(self);
    if (count == 0)
    {
        return 0.0;
    }
    return icall_unbox_double(il2cpp_sortedlist_get_key_at(self, count - 1));
}

double icall_sortedset_double_enumerator_get_current(Il2CppObject* self)
{
    const Il2CppDictSIEnumerator* en = reinterpret_cast<const Il2CppDictSIEnumerator*>(self);
    if (en == nullptr || en->keys == nullptr || en->index < 0 || en->index >= en->keys->length)
    {
        return 0.0;
    }
    return icall_unbox_double(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W2: SortedSet<string/object/class> reference family -- the reference is
// stored directly (no box). String keys order by ordinal content; other
// references order by address (the sortedlist total-order contract). Empty
// Min/Max return null to match net48 (reference default).
//------------------------------------------------------------------------------
bool icall_sortedset_ref_add(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr)
    {
        il2cpp_throw_argument_null_exception("item");
    }
    if (il2cpp_sortedlist_contains_key(self, item))
    {
        return false;
    }
    il2cpp_sortedlist_add(self, item, item);
    return true;
}

bool icall_sortedset_ref_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr)
    {
        return false;
    }
    return il2cpp_sortedlist_contains_key(self, item) != 0;
}

bool icall_sortedset_ref_remove(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr)
    {
        return false;
    }
    if (!il2cpp_sortedlist_contains_key(self, item))
    {
        return false;
    }
    il2cpp_sortedlist_remove_by_key(self, item);
    return true;
}

Il2CppObject* icall_sortedset_ref_get_min(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (il2cpp_sortedlist_get_count(self) == 0)
    {
        return nullptr;
    }
    return il2cpp_sortedlist_get_key_at(self, 0);
}

Il2CppObject* icall_sortedset_ref_get_max(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const int32_t count = il2cpp_sortedlist_get_count(self);
    if (count == 0)
    {
        return nullptr;
    }
    return il2cpp_sortedlist_get_key_at(self, count - 1);
}

//------------------------------------------------------------------------------
// COLL-SETOPS (nb9): SortedSet<T> set algebra over the boxed-key storage.
//------------------------------------------------------------------------------
namespace {

Il2CppObject* KeyAt(Il2CppObject* keysSnapshot, int32_t index)
{
    return *reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(keysSnapshot), index));
}

int32_t KeysLength(Il2CppObject* keysSnapshot)
{
    return reinterpret_cast<Il2CppArray*>(keysSnapshot)->length;
}

// A snapshot of the set's keys (ascending boxed values).
Il2CppObject* KeysOf(Il2CppObject* set)
{
    return il2cpp_sortedlist_get_keys(set);
}

bool ContainsKey(Il2CppObject* set, Il2CppObject* key)
{
    return il2cpp_sortedlist_contains_key(set, key) != 0;
}

} // namespace

void icall_sortedset_int_ctor_array(Il2CppObject* self, Il2CppArray* items)
{
    ThrowIfNullReceiver(self);
    if (items == nullptr)
    {
        il2cpp_throw_argument_null_exception("collection");
    }
    if (items->isRefElem || items->elemSize != static_cast<int32_t>(sizeof(int32_t)))
    {
        il2cpp_throw_not_supported_exception(
            "SortedSet<int> ctor: only an int32[] enumerable carrier is supported.");
    }
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_ctor_array_self");
    Il2CppObject* itemsObj = reinterpret_cast<Il2CppObject*>(items);
    GcRoot<Il2CppObject*> itemsRoot(itemsObj, "sortedset_int_ctor_array_items");
    il2cpp_sortedlist_create(selfRoot.Get(), 0);
    const int32_t n = reinterpret_cast<Il2CppArray*>(itemsRoot.Get())->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t v = *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(itemsRoot.Get()), i));
        Il2CppObject* boxed = il2cpp_box_int32(nullptr, v);
        GcRoot<Il2CppObject*> keyRoot(boxed, "sortedset_int_ctor_array_key");
        Il2CppObject* set = selfRoot.Get();
        if (!ContainsKey(set, keyRoot.Get()))
        {
            il2cpp_sortedlist_add(set, keyRoot.Get(), keyRoot.Get());
        }
    }
}

void icall_sortedset_union_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_union_self");
    GcRoot<Il2CppObject*> otherRoot(other, "sortedset_union_other");
    Il2CppObject* keys = KeysOf(otherRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_union_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* key = KeyAt(keysRoot.Get(), i);
        Il2CppObject* set = selfRoot.Get();
        if (!ContainsKey(set, key))
        {
            il2cpp_sortedlist_add(set, key, key);
        }
    }
}

void icall_sortedset_except_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_except_self");
    GcRoot<Il2CppObject*> otherRoot(other, "sortedset_except_other");
    Il2CppObject* keys = KeysOf(selfRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_except_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* key = KeyAt(keysRoot.Get(), i);
        Il2CppObject* set = selfRoot.Get();
        if (ContainsKey(otherRoot.Get(), key))
        {
            il2cpp_sortedlist_remove_by_key(set, key);
        }
    }
}

void icall_sortedset_intersect_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_intersect_self");
    GcRoot<Il2CppObject*> otherRoot(other, "sortedset_intersect_other");
    Il2CppObject* keys = KeysOf(selfRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_intersect_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* key = KeyAt(keysRoot.Get(), i);
        Il2CppObject* set = selfRoot.Get();
        if (!ContainsKey(otherRoot.Get(), key))
        {
            il2cpp_sortedlist_remove_by_key(set, key);
        }
    }
}

void icall_sortedset_symmetric_except_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    if (self == other)
    {
        il2cpp_sortedlist_clear(self);
        return;
    }
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_symexcept_self");
    GcRoot<Il2CppObject*> otherRoot(other, "sortedset_symexcept_other");
    Il2CppObject* keys = KeysOf(otherRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_symexcept_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* key = KeyAt(keysRoot.Get(), i);
        Il2CppObject* set = selfRoot.Get();
        if (ContainsKey(set, key))
        {
            il2cpp_sortedlist_remove_by_key(set, key);
        }
        else
        {
            il2cpp_sortedlist_add(set, key, key);
        }
    }
}

bool icall_sortedset_is_subset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_subset_self");
    GcRoot<Il2CppObject*> otherRoot(other, "sortedset_subset_other");
    Il2CppObject* keys = KeysOf(selfRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_subset_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        if (!ContainsKey(otherRoot.Get(), KeyAt(keysRoot.Get(), i)))
        {
            return false;
        }
    }
    return true;
}

bool icall_sortedset_is_superset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_superset_self");
    GcRoot<Il2CppObject*> otherRoot(other, "sortedset_superset_other");
    Il2CppObject* keys = KeysOf(otherRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_superset_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        if (!ContainsKey(selfRoot.Get(), KeyAt(keysRoot.Get(), i)))
        {
            return false;
        }
    }
    return true;
}

bool icall_sortedset_is_proper_subset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    if (il2cpp_sortedlist_get_count(self) >= il2cpp_sortedlist_get_count(other))
    {
        return false;
    }
    return icall_sortedset_is_subset_of(self, other);
}

bool icall_sortedset_is_proper_superset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    if (il2cpp_sortedlist_get_count(self) <= il2cpp_sortedlist_get_count(other))
    {
        return false;
    }
    return icall_sortedset_is_superset_of(self, other);
}

bool icall_sortedset_overlaps(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_overlaps_self");
    GcRoot<Il2CppObject*> otherRoot(other, "sortedset_overlaps_other");
    Il2CppObject* keys = KeysOf(selfRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_overlaps_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        if (ContainsKey(otherRoot.Get(), KeyAt(keysRoot.Get(), i)))
        {
            return true;
        }
    }
    return false;
}

bool icall_sortedset_set_equals(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    ThrowIfNullReceiver(other);
    if (il2cpp_sortedlist_get_count(self) != il2cpp_sortedlist_get_count(other))
    {
        return false;
    }
    return icall_sortedset_is_subset_of(self, other);
}

//------------------------------------------------------------------------------
// int-family members.
//------------------------------------------------------------------------------
namespace {

Il2CppArray* CheckInt32Array(Il2CppArray* array, const char* paramName)
{
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
    }
    if (array->isRefElem || array->elemSize != static_cast<int32_t>(sizeof(int32_t)))
    {
        il2cpp_throw_argument_exception("Destination array is not an int32 array.");
    }
    return array;
}

} // namespace

int32_t icall_sortedset_int_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    array = CheckInt32Array(array, "array");
    const int32_t n = il2cpp_sortedlist_get_count(self);
    if (arrayIndex < 0 || static_cast<int64_t>(arrayIndex) + n > array->length)
    {
        il2cpp_throw_argument_exception("Destination array was not long enough.");
    }
    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(array, arrayIndex + i)) =
            icall_unbox_int32(il2cpp_sortedlist_get_key_at(self, i));
    }
    return n;
}

int32_t icall_sortedset_int_copy_to_range(Il2CppObject* self, Il2CppArray* array,
                                          int32_t index, int32_t arrayIndex, int32_t count)
{
    ThrowIfNullReceiver(self);
    array = CheckInt32Array(array, "array");
    const int32_t n = il2cpp_sortedlist_get_count(self);
    if (index < 0 || count < 0 || static_cast<int64_t>(index) + count > n)
    {
        il2cpp_throw_argument_out_of_range_exception("count", "Index was out of range.");
    }
    if (arrayIndex < 0 || static_cast<int64_t>(arrayIndex) + count > array->length)
    {
        il2cpp_throw_argument_exception("Destination array was not long enough.");
    }
    for (int32_t i = 0; i < count; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(array, arrayIndex + i)) =
            icall_unbox_int32(il2cpp_sortedlist_get_key_at(self, index + i));
    }
    return count;
}

bool icall_sortedset_int_try_get_value(Il2CppObject* self, int32_t equalValue, int32_t* actualValue)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_tryget_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, equalValue);
    GcRoot<Il2CppObject*> keyRoot(boxed, "sortedset_int_tryget_key");
    const bool found = ContainsKey(selfRoot.Get(), keyRoot.Get());
    *actualValue = found ? equalValue : 0;
    return found;
}

Il2CppArray* icall_sortedset_int_reverse(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_reverse_self");
    Il2CppObject* keys = KeysOf(selfRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_int_reverse_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(int32_t), false, n);
    Il2CppObject* outObj = reinterpret_cast<Il2CppObject*>(out);
    GcRoot<Il2CppObject*> outRoot(outObj, "sortedset_int_reverse_out");
    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(
            reinterpret_cast<Il2CppArray*>(outRoot.Get()), n - 1 - i)) =
            icall_unbox_int32(KeyAt(keysRoot.Get(), i));
    }
    return reinterpret_cast<Il2CppArray*>(outRoot.Get());
}

Il2CppObject* icall_sortedset_int_get_view_between(Il2CppObject* self, int32_t lowerValue, int32_t upperValue)
{
    ThrowIfNullReceiver(self);
    if (lowerValue > upperValue)
    {
        il2cpp_throw_argument_exception("lowerValue must be less than or equal to upperValue.");
    }
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_view_self");
    Il2CppObject* keys = KeysOf(selfRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_int_view_keys");
    Il2CppObject* view = il2cpp_object_new(il2cpp_sortedlist_class());
    GcRoot<Il2CppObject*> viewRoot(view, "sortedset_int_view_result");
    il2cpp_sortedlist_create(viewRoot.Get(), 0);
    const int32_t n = KeysLength(keysRoot.Get());
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t v = icall_unbox_int32(KeyAt(keysRoot.Get(), i));
        if (v < lowerValue || v > upperValue)
        {
            continue;
        }
        Il2CppObject* boxed = il2cpp_box_int32(nullptr, v);
        GcRoot<Il2CppObject*> keyRoot(boxed, "sortedset_int_view_key");
        il2cpp_sortedlist_add(viewRoot.Get(), keyRoot.Get(), keyRoot.Get());
    }
    return viewRoot.Get();
}

int32_t icall_sortedset_int_remove_where(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    if (match == nullptr)
    {
        il2cpp_throw_argument_null_exception("match");
    }
    Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(match);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedset_int_removewhere_self");
    GcRoot<Il2CppObject*> matchRoot(match, "sortedset_int_removewhere_match");
    Il2CppObject* keys = KeysOf(selfRoot.Get());
    GcRoot<Il2CppObject*> keysRoot(keys, "sortedset_int_removewhere_keys");
    const int32_t n = KeysLength(keysRoot.Get());
    std::vector<int32_t> remove;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t v = icall_unbox_int32(KeyAt(keysRoot.Get(), i));
        d = reinterpret_cast<Il2CppDelegate*>(matchRoot.Get());
        const bool hit = d->target != nullptr
            ? reinterpret_cast<bool (*)(Il2CppObject*, int32_t)>(d->methodPtr)(d->target, v)
            : reinterpret_cast<bool (*)(int32_t)>(d->methodPtr)(v);
        if (hit)
        {
            remove.push_back(v);
        }
    }
    for (size_t i = 0; i < remove.size(); ++i)
    {
        Il2CppObject* boxed = il2cpp_box_int32(nullptr, remove[i]);
        GcRoot<Il2CppObject*> keyRoot(boxed, "sortedset_int_removewhere_key");
        il2cpp_sortedlist_remove_by_key(selfRoot.Get(), keyRoot.Get());
    }
    return static_cast<int32_t>(remove.size());
}

} // namespace runtime

Il2CppObject* il2cpp_sortedset_int_get_enumerator(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    // The ascending boxed-int keys snapshot wrapped in the dictsi
    // keycollection enumerator (the HashSet<int> enumeration precedent).
    Il2CppObject* snap = runtime::il2cpp_sortedlist_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

Il2CppObject* il2cpp_sortedset_long_get_enumerator(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    Il2CppObject* snap = runtime::il2cpp_sortedlist_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

Il2CppObject* il2cpp_sortedset_double_get_enumerator(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    Il2CppObject* snap = runtime::il2cpp_sortedlist_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

Il2CppObject* il2cpp_sortedset_ref_get_enumerator(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    Il2CppObject* snap = runtime::il2cpp_sortedlist_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

} // namespace il2cpp
