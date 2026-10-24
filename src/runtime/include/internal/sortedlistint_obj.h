#pragma once
//------------------------------------------------------------------------------
// sortedlistint_obj.h -- the SortedList<string,int> AOT expansion (G4,
// STAGED -- copy into src/runtime/include/internal + src/runtime/src when
// the full-pool rescan completes).
//
// The string->int expansion rides the NON-GENERIC sorted-array storage
// (sortedlist.h) exactly like Queue<int> rides queue_class and
// Dictionary<string,string> rides hashtable_class:
//
//   * the KEY is already a managed string reference (no conversion),
//   * the VALUE is stored in the non-generic table as a BOXED Int32 slot;
//     every wrapper entry point boxes on the way in / unboxes on the way out
//     at the C++ boundary, so the transpiled IL never sees the box (the
//     value never floats as a managed int in an object slot).
//
// WRAPPER-ONLY SEMANTICS (net48 SortedList<string,int> vs non-generic
// SortedList):
//   * get_Item (by key)  -> KeyNotFoundException on a miss. The non-generic
//     get_by_key returns null for a miss; a STORED value is always a boxed
//     Int32 (int values cannot be null), so null reliably means "missing".
//   * Remove(TKey)       -> bool (the non-generic remove is void): the
//     wrapper reports presence first, then removes.
//   * Add duplicate key  -> ArgumentException (sortedlist core contract),
//     null key -> ArgumentNullException (core).
//   * GetByIndex / this[int] GET -> int32 (core returns the boxed slot).
//   * SetByIndex / this[int] SET -> box the value into the core set_at.
//   * Keys -> the core's string[] snapshot (keys are already string refs;
//     no copy needed).
//
// Every boxing allocation can compact the heap, so receivers/keys are
// re-read from their GcRoot before each non-generic call (the queueint_obj
// discipline).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// .ctor(capacity): the parameterless form arrives as capacity 0
// (AppendZeroArg) and reuses il2cpp_sortedlist_create directly in the
// mapping -- no wrapper needed (no managed values cross the boundary).

// Add(key, value) / set_Item(key, value) (insert-or-replace).
void icall_sortedlist_si_add(Il2CppObject* self, Il2CppObject* key, int32_t value);
void icall_sortedlist_si_set_by_key(Il2CppObject* self, Il2CppObject* key, int32_t value);

// get_Item(key): the unboxed value, or KeyNotFoundException on a miss.
int32_t icall_sortedlist_si_get_by_key(Il2CppObject* self, Il2CppObject* key);

// ContainsKey(key) / Remove(key) (bool verdict) / get_Count.
bool icall_sortedlist_si_contains_key(Il2CppObject* self, Il2CppObject* key);
int32_t icall_sortedlist_si_remove(Il2CppObject* self, Il2CppObject* key);

// GetByIndex(index) / this[int] GET: the unboxed value at the sorted index
// (ArgumentOutOfRangeException comes from the core).
int32_t icall_sortedlist_si_get_by_index(Il2CppObject* self, int32_t index);

// this[int] SET: box the value into the core set_at.
void icall_sortedlist_si_set_at(Il2CppObject* self, int32_t index, int32_t value);

// Keys: the core string[] snapshot (ascending sorted order).
Il2CppObject* icall_sortedlist_si_get_keys(Il2CppObject* self);

// Values: net48 returns a LIVE IList<TValue> view, so the wrapper returns the
// list itself and the view members below read the CURRENT entries (the
// transpiler tracks the result as the ValueList view pseudo-type):
//   IList`1.get_Item       -> icall_sortedlist_si_get_by_index
//   ICollection`1.get_Count-> il2cpp_sortedlist_get_count
//   IEnumerable`1.GetEnumerator -> icall_sortedlist_si_values_get_enumerator
Il2CppObject* icall_sortedlist_si_get_values(Il2CppObject* self);

// Values foreach: snapshot the boxed values now and wrap the array in the core
// snapshot enumerator (the non-generic ValueList GetEnumerator precedent).
Il2CppObject* icall_sortedlist_si_values_get_enumerator(Il2CppObject* self);

// TryGetValue(key, &value): true + the unboxed value on a hit; false on a miss
// (the out slot is left untouched). A null stored slot means the key is absent
// (a stored value is always a boxed Int32).
bool icall_sortedlist_si_try_get_value(Il2CppObject* self, Il2CppObject* key, int32_t* value);

// GetEnumerator: the foreach KeyValuePair surface. The sorted keys snapshot
// (string[]) plus a fresh int32[] values snapshot (unboxed from the core
// boxed-value snapshot) feed the SHARED Dictionary<string,int> pair-enumerator
// object (dictsi_obj.h), so the MoveNext/get_Current/Dispose and KeyValuePair
// members reuse the dictsi domain exactly (the two snapshots are positionally
// aligned, like the dictsi snapshots).
Il2CppObject* icall_sortedlist_si_get_enumerator(Il2CppObject* self);

// IEnumerator`1<int>.get_Current: unbox the core array-enumerator element.
int32_t icall_sortedlist_si_enumerator_get_current(Il2CppObject* self);

// ContainsValue(value): net48 value equality over the sorted entries. The
// core has no contains_value icall, so scan the values snapshot (no
// allocation after the snapshot; boxed ints compare by unboxed equality).
bool icall_sortedlist_si_contains_value(Il2CppObject* self, int32_t value);

} // namespace runtime
} // namespace il2cpp
