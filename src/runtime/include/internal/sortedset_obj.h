#pragma once
//------------------------------------------------------------------------------
// sortedset_obj.h -- the SortedSet<int> AOT expansion (G4, STAGED -- copy
// into src/runtime/include/internal + src/runtime/src when the full-pool
// rescan completes).
//
// SortedSet<int> is a sorted-unique-int set; it rides the NON-GENERIC
// sorted-array storage (sortedlist.h) with BOXED int32 keys. Ascending order
// comes from the core's boxed-Int32 sort contract; uniqueness from the
// core's duplicate-key -> ArgumentException contract (the wrapper pre-checks
// ContainsKey so Add returns the net48 bool instead of throwing).
//
//   * Add(item)      -> bool (false on a duplicate; net48 does not throw)
//   * Contains(item) -> bool
//   * Remove(item)   -> bool
//   * Min / Max      -> the smallest / largest item; an EMPTY set raises
//                       InvalidOperationException (net48)
//   * GetEnumerator  -> the dictsi keycollection wrapper over the ascending
//                       boxed-int keys snapshot (the HashSet<int> precedent),
//                       with MoveNext/Dispose routed to the dictsi enumerator
//                       and get_Current unboxing the element
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// GetEnumerator: the snapshot enumerator object (tracked as the dictsi
// KeyCollection.Enumerator token downstream for int; per-kind tokens for
// long/double/ref -- see MethodMap.SortedSet.cs).
Il2CppObject* il2cpp_sortedset_int_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_sortedset_long_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_sortedset_double_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_sortedset_ref_get_enumerator(Il2CppObject* self);

namespace runtime {

bool icall_sortedset_int_add(Il2CppObject* self, int32_t item);
bool icall_sortedset_int_contains(Il2CppObject* self, int32_t item);
bool icall_sortedset_int_remove(Il2CppObject* self, int32_t item);

// Min / Max: the boxed key bounds unboxed; empty returns default (net48:
// empty SortedSet<int>.Min/Max return 0, not InvalidOperationException).
int32_t icall_sortedset_int_get_min(Il2CppObject* self);
int32_t icall_sortedset_int_get_max(Il2CppObject* self);

// The element get_Current: unbox the boxed Int32 from the keycollection
// enumerator wrapper.
int32_t icall_sortedset_int_enumerator_get_current(Il2CppObject* self);

// T2 W2: long/double/ref families (sortedset_obj.cpp).
bool icall_sortedset_long_add(Il2CppObject* self, int64_t item);
bool icall_sortedset_long_contains(Il2CppObject* self, int64_t item);
bool icall_sortedset_long_remove(Il2CppObject* self, int64_t item);
int64_t icall_sortedset_long_get_min(Il2CppObject* self);
int64_t icall_sortedset_long_get_max(Il2CppObject* self);
int64_t icall_sortedset_long_enumerator_get_current(Il2CppObject* self);
bool icall_sortedset_double_add(Il2CppObject* self, double item);
bool icall_sortedset_double_contains(Il2CppObject* self, double item);
bool icall_sortedset_double_remove(Il2CppObject* self, double item);
double icall_sortedset_double_get_min(Il2CppObject* self);
double icall_sortedset_double_get_max(Il2CppObject* self);
double icall_sortedset_double_enumerator_get_current(Il2CppObject* self);
bool icall_sortedset_ref_add(Il2CppObject* self, Il2CppObject* item);
bool icall_sortedset_ref_contains(Il2CppObject* self, Il2CppObject* item);
bool icall_sortedset_ref_remove(Il2CppObject* self, Il2CppObject* item);
Il2CppObject* icall_sortedset_ref_get_min(Il2CppObject* self);
Il2CppObject* icall_sortedset_ref_get_max(Il2CppObject* self);

} // namespace runtime
} // namespace il2cpp
