#pragma once
//------------------------------------------------------------------------------
// arraylist.h -- managed non-generic System.Collections.ArrayList runtime
// (icall domain), an object[] container modeled on the stackqueue.h
// conventions (same layout / GC-bitmap / reference-array / doubling rules).
//
// Layout (every field below is a GC-scanned slot in the class descriptor's
// refFieldBitmap unless noted):
//
//   struct Il2CppArrayList
//   {
//       Il2CppObject obj;      // object header: klass pointer
//       Il2CppArray* items;    // reference array (Il2CppObject* elements):
//                              //   slot i holds element i
//       int32_t      count;    // number of elements (NOT a reference)
//   };
//
//   GC SAFETY (refFieldBitmap contract):
//     * instanceSize 24, refFieldBitmap bit 1 (slot 1 = the items array; the
//       int32 count slot is NOT a reference). The collector keeps the array
//       alive whenever the list is reachable.
//     * The items array is a REFERENCE array (il2cpp_array_new with
//       isRefElement == true): the collector scans every element as an
//       Il2CppObject*, so a rooted list keeps its whole contents alive.
//       Element writes therefore go through the write barrier, and RemoveAt
//       / Clear null the vacated slots so the collector never rescans a
//       stale reference past the logical end (mirrors .NET's
//       _items[_size] = null).
//
// .NET SEMANTICS BASELINE (matches System.Collections.ArrayList on .NET
// Framework 4.8):
//   * add appends; the items array doubles when full (min 4, cap 2^30).
//   * get_item/set_item(index): 0 <= index < count, else
//     ArgumentOutOfRangeException ("Index was out of range. Must be
//     non-negative and less than the size of the collection.").
//   * remove_at(index): 0 <= index < count, else ArgumentOutOfRangeException
//     (same message). Elements shift left; the vacated tail slot is nulled.
//   * remove(value): removes the FIRST element EQUAL to 'value' (static
//     Object.Equals: null matches null; boxed values by payload; strings by
//     ordinal content; reference identity otherwise); followers shift left,
//     the tail slot is nulled, and an absent value is a silent success
//     (no-op, .NET Framework semantics).
//   * index_of(value): the first slot whose element EQUALS 'value' (net48
//     ArrayList.IndexOf uses Object.Equals value semantics -- two distinct
//     boxed ints with the same payload compare equal), or -1.
//   * contains(value) == index_of(value) != -1.
//   * to_array: a NEW object[] (reference array) of exactly count elements,
//     independent of the internal storage.
//   * clear: count -> 0, every slot nulled (stack/queue vacate contract).
//   * Null elements are legal everywhere (add/set_item store them; index_of
//     finds them; to_array copies them). Elements are Il2CppObject* -- value
//     boxing is the CALLER's responsibility.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Managed non-generic array list (see header comment for layout + GC bitmap).
struct Il2CppArrayList
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* items;    // reference array: slot i = element i
    int32_t      count;    // number of elements
};

// The System.Collections.ArrayList runtime descriptor (registered lazily on
// first use; instanceSize + refFieldBitmap per the header comment). Create
// the managed objects through the *_create icall below.
Il2CppClass* il2cpp_arraylist_class();

// ArrayList() -- allocate the managed list with an empty 4-slot reference
// array. Returns the new object (never null).
Il2CppObject* il2cpp_arraylist_create();

// Add(value) -- append at the end. Null is legal. Grows (doubles the items
// array, min 4) when the list is full.
void il2cpp_arraylist_add(Il2CppObject* self, Il2CppObject* value);

// get_Item(index) -- the element at 'index'. An index outside [0, count-1]
// raises ArgumentOutOfRangeException.
Il2CppObject* il2cpp_arraylist_get_item(Il2CppObject* self, int32_t index);

// set_Item(index, value) -- overwrite the element at 'index'. An index
// outside [0, count-1] raises ArgumentOutOfRangeException.
void il2cpp_arraylist_set_item(Il2CppObject* self, int32_t index, Il2CppObject* value);

// Count -- the number of elements.
int32_t il2cpp_arraylist_get_count(Il2CppObject* self);

// Clear() -- drop every element (all slots nulled). Count -> 0.
void il2cpp_arraylist_clear(Il2CppObject* self);

// RemoveAt(index) -- remove the element at 'index'. An index outside
// [0, count-1] raises ArgumentOutOfRangeException.
void il2cpp_arraylist_remove_at(Il2CppObject* self, int32_t index);

// Insert(index, value) -- insert 'value' at 'index' (net48 ArrayList.Insert):
// elements [index, count) shift RIGHT by one, the count grows by one. An
// index outside [0, count] raises ArgumentOutOfRangeException. Mirrors the
// remove_at shift in reverse, with the write barrier on every moved slot and
// the inserted element. The items array is lazily allocated on first use
// (EnsureItems inside GrowList), so Insert into a FRESH list (one created
// through the newobj path with a null items array -- D-AL-2) is safe.
void il2cpp_arraylist_insert(Il2CppObject* self, int32_t index, Il2CppObject* value);

// Remove(value) -- remove the FIRST element EQUAL to 'value' (static
// Object.Equals: null matches null; boxed values by payload; strings by
// ordinal content; reference identity otherwise); followers shift left and
// the vacated tail slot is nulled. An absent value is a SILENT success (the
// list is unchanged), matching .NET Framework 4.8 ArrayList.Remove.
void il2cpp_arraylist_remove(Il2CppObject* self, Il2CppObject* value);

// IndexOf(value) -- the index of the FIRST element EQUAL to 'value' (net48
// ArrayList.IndexOf value semantics: boxed values by payload, strings by
// ordinal content, reference identity otherwise), or -1.
int32_t il2cpp_arraylist_index_of(Il2CppObject* self, Il2CppObject* value);

// Contains(value) -- true when at least one element EQUALS 'value' (same
// value semantics as IndexOf).
bool il2cpp_arraylist_contains(Il2CppObject* self, Il2CppObject* value);

// ToArray() -- a NEW object[] with exactly the current elements
// (independent storage; the caller owns the reference).
Il2CppObject* il2cpp_arraylist_to_array(Il2CppObject* self);

} // namespace il2cpp
