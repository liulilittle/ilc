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

// List<int>(capacity) -- allocate a list whose items array is pre-sized to
// 'capacity'. A capacity < 0 raises ArgumentOutOfRangeException (net48
// ArgumentOutOfRange_MustBeNonNegNum). A capacity of 0 allocates the default
// 4-slot array instead of net48's empty array (the wired surface cannot
// observe the internal size -- Capacity is not mapped -- and the growth
// logic never extends a length-0 array, so the empty state would be
// unusable). Returns the new object (never null).
Il2CppObject* il2cpp_list_i4_create_with_capacity(int32_t capacity);

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

// Clear() -- drop every element (all slots zeroed). Count -> 0.
void il2cpp_list_i4_clear(Il2CppObject* self);

// IndexOf(value) -- the index of the FIRST element equal to 'value', or -1.
int32_t il2cpp_list_i4_index_of(Il2CppObject* self, int32_t value);

// Contains(value) -- true when at least one element equals 'value'.
bool il2cpp_list_i4_contains(Il2CppObject* self, int32_t value);

// ToArray() -- a NEW int32 array with exactly the current elements
// (independent storage; the caller owns the reference).
Il2CppObject* il2cpp_list_i4_to_array(Il2CppObject* self);

} // namespace il2cpp
