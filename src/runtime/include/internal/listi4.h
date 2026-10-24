#pragma once
//------------------------------------------------------------------------------
// listi4.h -- managed System.Collections.Generic.List<int32> runtime
// (icall domain), the int32-instantiation of List<T> for AOT generic
// expansion (List`1<int> -> "System.Collections.Generic.List`1" dispatch),
// modeled on the stackqueue.h conventions (same object-model / GC-bitmap /
// doubling rules).
//
// Layout (every field below is a GC-scanned slot in the class descriptor's
// refFieldBitmap unless noted):
//
//   struct Il2CppListI4
//   {
//       Il2CppObject obj;      // object header: klass pointer
//       Il2CppArray* items;    // INT32 ELEMENT array (elemSize 4, isRefElem
//                              //   false): slot i holds element i
//       int32_t      count;    // number of elements (NOT a reference)
//   };
//
//   GC SAFETY (refFieldBitmap contract):
//     * instanceSize 24, refFieldBitmap bit 1 (slot 1 = the items array; the
//       int32 count slot is NOT a reference). The collector keeps the array
//       alive whenever the list is reachable.
//     * The items array is a VALUE array (il2cpp_array_new with elemSize 4,
//       isRefElement == false): int32 elements are NOT GC references, so the
//       collector never scans them and element writes need no write barrier.
//       Only the publication of the array pointer itself goes through the
//       barrier (old -> young tracking).
//
// .NET SEMANTICS BASELINE (matches List<int> on .NET Framework 4.8):
//   * add appends; the items array doubles when full (min 4, cap 2^30).
//   * insert(index, value): 0 <= index <= count, else ArgumentOutOfRange
//     with the net48 INSERT message ("Index must be within the bounds of the
//     List." -- ArgumentOutOfRange_ListInsert, distinct from the element
//     message below). Elements shift right; grows when full.
//   * remove_at(index): 0 <= index < count, else ArgumentOutOfRange ("Index
//     was out of range. Must be non-negative and less than the size of the
//     collection."). Elements shift left; the vacated tail slot is zeroed.
//   * get_item/set_item(index): 0 <= index < count, else ArgumentOutOfRange
//     (same element message).
//   * remove(value): removes the FIRST equal element (int32 ==), returns
//     true; false when absent (count unchanged).
//   * index_of(value): first equal index, or -1. contains == index_of != -1.
//   * to_array: a NEW int32 array of exactly count elements (independent of
//     the internal storage).
//   * clear: count -> 0, every slot zeroed (value slots: hygiene, mirrors
//     the stack/queue vacate contract for symmetry).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "sortedlist.h" // Il2CppArrayEnumerator re-use

#include <cstdint>

namespace il2cpp {

// Managed int32 list (see header comment for layout + GC bitmap).
struct Il2CppListI4
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* items;    // int32 value array: slot i = element i
    int32_t      count;    // number of elements
};

// The System.Collections.Generic.List`1 (int32 instantiation) runtime
// descriptor (registered lazily on first use; instanceSize + refFieldBitmap
// per the header comment). Create the managed objects through the *_create
// icall below.
Il2CppClass* il2cpp_list_i4_class();

// List<int>() -- allocate the managed list with an empty 4-slot int32 items
// array. Returns the new object (never null).
Il2CppObject* il2cpp_list_i4_create();

// List<int>(capacity) -- initialize the ALREADY-ALLOCATED list object 'self'
// with an items array pre-sized to 'capacity' (the self-mutating ctor ABI: the
// transpiler's newobj allocates the shell and the ctor call fills it, exactly
// like il2cpp_sortedlist_create). A capacity < 0 raises
// ArgumentOutOfRangeException (net48 ArgumentOutOfRange_MustBeNonNegNum). The
// items-array length IS the Capacity (net48 observable): a zero capacity keeps a
// zero-length array (the first Add grows to the 4-slot minimum).
void il2cpp_list_i4_create_with_capacity(Il2CppObject* self, int32_t capacity);

// List<int>(IEnumerable<int> collection) -- initialize the ALREADY-ALLOCATED
// list object 'self' with an items array pre-sized to the collection's element
// count holding a copy of its elements (net48: the initial Capacity equals the
// source count). A null source raises ArgumentNullException; the wired surface
// passes a one-dimensional int32 array.
void il2cpp_list_i4_create_with_collection(Il2CppObject* self, Il2CppObject* source);

// Add(value) -- append at the end. Grows (doubles the items array, min 4)
// when the list is full.
void il2cpp_list_i4_add(Il2CppObject* self, int32_t value);

// Insert(index, value) -- insert before the current element at 'index'
// (appending when index == count). An index outside [0, count] raises
// ArgumentOutOfRangeException.
void il2cpp_list_i4_insert(Il2CppObject* self, int32_t index, int32_t value);

// RemoveAt(index) -- remove the element at 'index'. An index outside
// [0, count-1] raises ArgumentOutOfRangeException.
void il2cpp_list_i4_remove_at(Il2CppObject* self, int32_t index);

// Remove(value) -- remove the FIRST element equal to 'value'; true when one
// was removed, false when absent (the list is unchanged in that case).
bool il2cpp_list_i4_remove(Il2CppObject* self, int32_t value);

// get_Item(index) -- the element at 'index'. An index outside [0, count-1]
// raises ArgumentOutOfRangeException.
int32_t il2cpp_list_i4_get_item(Il2CppObject* self, int32_t index);

// set_Item(index, value) -- overwrite the element at 'index'. An index
// outside [0, count-1] raises ArgumentOutOfRangeException.
void il2cpp_list_i4_set_item(Il2CppObject* self, int32_t index, int32_t value);

// Count -- the number of elements.
int32_t il2cpp_list_i4_get_count(Il2CppObject* self);

// Capacity -- the allocated items-array length (>= Count; net48 observable
// surface: default 0, first Add grows to 4, doubling, ctor(capacity) exact).
int32_t il2cpp_list_i4_get_capacity(Il2CppObject* self);

// set_Capacity(value) -- the net48 Capacity setter: a value below Count raises
// ArgumentOutOfRangeException("value"); an equal capacity is a no-op;
// otherwise the items array is rebuilt at exactly 'value' slots holding the
// first min(Count, value) elements (the element storage is preserved).
void il2cpp_list_i4_set_capacity(Il2CppObject* self, int32_t value);

// Clear() -- drop every element (all slots zeroed). Count -> 0.
void il2cpp_list_i4_clear(Il2CppObject* self);

// IndexOf(value) -- the index of the FIRST element equal to 'value', or -1.
int32_t il2cpp_list_i4_index_of(Il2CppObject* self, int32_t value);

// Contains(value) -- true when at least one element equals 'value'.
bool il2cpp_list_i4_contains(Il2CppObject* self, int32_t value);

// ToArray() -- a NEW int32 array with exactly the current elements
// (independent storage; the caller owns the reference).
Il2CppObject* il2cpp_list_i4_to_array(Il2CppObject* self);

// GetEnumerator() -- return a snapshot enumerator over the list's items
// array [0, count). The enumerator is a managed object (Il2CppArrayEnumerator
// re-used from sortedlist.h) holding the items array and a cursor starting
// at -1 (before the first element).
Il2CppObject* il2cpp_list_i4_get_enumerator(Il2CppObject* self);

// Sort() -- ascending in-place sort of the elements [0, count).
// Reuses il2cpp_array_sort_int32 on a temporary exact-length copy, then
// copies back (the items array may be longer than count).
void il2cpp_list_i4_sort(Il2CppObject* self);

// CopyTo(T[] array, int32_t arrayIndex) -- copy elements [0, count) into
// 'array' starting at 'arrayIndex'. Validates: null array, negative index,
// insufficient space (ArgumentOutOfRangeException/ArgumentNullException).
void il2cpp_list_i4_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex);

//--------------------------------------------------------------------------
// listb1: the range / predicate / reverse / binary-search / sort overload
// surface. All validate the receiver (NullReferenceException) and the net48
// contract of the corresponding List<int> member; the predicate/comparison
// delegates ride the single-cast static methodPtr ABI (an instance-bound leaf
// receives its target in the `this` slot) and the delegate + the list are
// GC-rooted across a callback loop.
//--------------------------------------------------------------------------

// AddRange(IEnumerable<int>): append every source element. A null source raises
// ArgumentNullException; a non-array/non-int32 source raises
// ArgumentException/NotSupportedException.
void il2cpp_list_i4_add_range(Il2CppObject* self, Il2CppObject* collection);

// InsertRange(index, IEnumerable<int>): insert the source elements before
// 'index'. An index outside [0, count] raises ArgumentOutOfRangeException.
void il2cpp_list_i4_insert_range(Il2CppObject* self, int32_t index, Il2CppObject* collection);

// RemoveRange(index, count): remove 'count' elements starting at 'index'. An
// invalid range raises ArgumentOutOfRangeException/ArgumentException.
void il2cpp_list_i4_remove_range(Il2CppObject* self, int32_t index, int32_t count);

// GetRange(index, count): a NEW List<int> holding the requested elements.
Il2CppObject* il2cpp_list_i4_get_range(Il2CppObject* self, int32_t index, int32_t count);

// RemoveAll(Predicate<int>): remove every element whose predicate returns true;
// returns the number removed.
int32_t il2cpp_list_i4_remove_all(Il2CppObject* self, Il2CppObject* match);

// LastIndexOf(value): the index of the LAST equal element, or -1.
int32_t il2cpp_list_i4_last_index_of(Il2CppObject* self, int32_t value);

// Predicate family: Find / FindLast / FindIndex / FindLastIndex / Exists /
// TrueForAll / ForEach / FindAll (the net48 contracts mirror the Array family).
int32_t il2cpp_list_i4_find(Il2CppObject* self, Il2CppObject* match);
int32_t il2cpp_list_i4_find_last(Il2CppObject* self, Il2CppObject* match);
int32_t il2cpp_list_i4_find_index(Il2CppObject* self, Il2CppObject* match);
int32_t il2cpp_list_i4_find_last_index(Il2CppObject* self, Il2CppObject* match);
int32_t il2cpp_list_i4_exists(Il2CppObject* self, Il2CppObject* match);
int32_t il2cpp_list_i4_true_for_all(Il2CppObject* self, Il2CppObject* match);
void il2cpp_list_i4_for_each(Il2CppObject* self, Il2CppObject* action);
Il2CppObject* il2cpp_list_i4_find_all(Il2CppObject* self, Il2CppObject* match);

// Reverse() / Reverse(index, count): in-place reversal of the whole list / the
// range [index, index + count).
void il2cpp_list_i4_reverse(Il2CppObject* self);
void il2cpp_list_i4_reverse_range(Il2CppObject* self, int32_t index, int32_t count);

// BinarySearch(value): the net48 complement semantics (a found index, or
// ~insertionPoint when absent) over [0, count).
int32_t il2cpp_list_i4_binary_search(Il2CppObject* self, int32_t value);

// Sort(Comparison<int>) / Sort(index, count, IComparer<int>): the delegate and
// comparer overloads (the int32 array-sort domain is reused on a temporary
// exact-length copy).
void il2cpp_list_i4_sort_comparison(Il2CppObject* self, Il2CppObject* comparison);
void il2cpp_list_i4_sort_range_comparer(Il2CppObject* self, int32_t index, int32_t count, Il2CppObject* comparer);

// Enumerator members (the generated code passes (&loc) where loc is an
// Il2CppObject* reference slot; the "deref:r" cast dereferences to the
// enumerator object pointer, so the icall receives Il2CppObject*).
int32_t il2cpp_list_i4_enumerator_move_next(Il2CppObject* self);
int32_t il2cpp_list_i4_enumerator_get_current(Il2CppObject* self);
void il2cpp_list_i4_enumerator_dispose(Il2CppObject* self);

} // namespace il2cpp
