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
#include "il2cpp_boxing_ext.h" // runtime::icall_unbox_int32/icall_box_double
#include "il2cpp_boxing_ext2.h" // runtime::icall_box_long/icall_unbox_long
#include "il2cpp_exception_ext.h" // il2cpp_throw_{invalid_operation,null_reference}_exception
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
