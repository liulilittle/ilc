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
    Il2CppObject obj;      // object header: klass pointer
    Il2CppArray* items;    // reference array: slot i = element i
    int32_t      count;    // number of elements
    // Wrapper marker (slot 3, NOT a GC reference -- an int32 tag):
    //   0 = a plain ArrayList, 1 = Adapter, 2 = FixedSize, 3 = ReadOnly,
    //   4 = Synchronized. A wrapper SHARES the source list's items array and
    //   count (the net48 wrapper is a live view; the tag lets the flags and
    //   the mutating guards answer per wrapper kind).
    int32_t      wrapperKind;
};

// The wrapper kinds (Il2CppArrayList::wrapperKind).
enum Il2CppArrayListWrapperKind : int32_t
{
    kArrayListPlain = 0,
    kArrayListAdapter = 1,
    kArrayListFixedSize = 2,
    kArrayListReadOnly = 3,
    kArrayListSynchronized = 4,
};

// The System.Collections.ArrayList runtime descriptor (registered lazily on
// first use; instanceSize + refFieldBitmap per the header comment). Create
// the managed objects through the *_create icall below.
Il2CppClass* il2cpp_arraylist_class();

// ArrayList() -- allocate the managed list with an empty 4-slot reference
// array. Returns the new object (never null).
Il2CppObject* il2cpp_arraylist_create();

// ArrayList(int capacity) -- an INSTANCE-style ctor: it pre-sizes the receiver
// list's items array to 'capacity' reference slots (a fresh list's Capacity
// reads 'capacity'). A capacity of 0 keeps items null (Capacity 0, the first
// Add grows to the default 4 -- matching .NET Framework 4.8's empty-items ctor
// arm); a negative capacity raises ArgumentOutOfRangeException. The items
// array is a reference array, so the allocation can compact the container:
// 'list' is GC-rooted and re-read before the publication store.
void il2cpp_arraylist_create_capacity(Il2CppObject* self, int32_t capacity);

// ArrayList(ICollection c) [net48 arraylist.cs:86]: copy every element of 'c'
// into the receiver list. The elements are enumerated through the NON-generic
// IEnumerator surface (GetEnumerator/MoveNext/get_Current) in two passes: the
// first counts them (the items array is allocated with EXACTLY that capacity,
// matching net48's `_items = new Object[c.Count]`), the second fills it. A
// null 'c' raises ArgumentNullException("c"). The receiver and the collection
// are GC-rooted across both enumerations and the allocation.
void il2cpp_arraylist_create_from_collection(Il2CppObject* self, Il2CppObject* collection);

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

// ArrayList A-class: capacity read (fresh lists report 0), the three Is-*
// flags (always false for a plain ArrayList), and SyncRoot (the list itself).
int32_t il2cpp_arraylist_get_capacity(Il2CppObject* self);
bool il2cpp_arraylist_get_is_fixed_size(Il2CppObject* self);
bool il2cpp_arraylist_get_is_read_only(Il2CppObject* self);
bool il2cpp_arraylist_get_is_synchronized(Il2CppObject* self);
Il2CppObject* il2cpp_arraylist_get_sync_root(Il2CppObject* self);

// ArrayList B-class: reshape/clone/copy/reverse/remove-range.
void il2cpp_arraylist_set_capacity(Il2CppObject* self, int32_t value);
Il2CppObject* il2cpp_arraylist_clone(Il2CppObject* self);
void il2cpp_arraylist_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex);
void il2cpp_arraylist_copy_to_array(Il2CppObject* self, Il2CppObject* array);
void il2cpp_arraylist_trim_to_size(Il2CppObject* self);
void il2cpp_arraylist_reverse(Il2CppObject* self);
void il2cpp_arraylist_reverse_range(Il2CppObject* self, int32_t index, int32_t length);
void il2cpp_arraylist_remove_range(Il2CppObject* self, int32_t index, int32_t length);

// ArrayList C-class: enumeration. GetEnumerator() returns the shared
// Il2CppArrayEnumerator (the same one Array.GetEnumerator uses) wrapping a
// SNAPSHOT of the list's current elements -- net48 ArrayList's enumerator is
// a live view that throws InvalidOperationException on MoveNext after a
// structural change, so the snapshot makes the version check a no-op and the
// "collection modified" contract is answered by the snapshot's frozen array.
// GetEnumerator(index, count) additionally bounds the snapshot window; its
// ArgumentOutOfRangeException (index < 0) / ArgumentException (index > Count
// or count > Count - index) contract matches net48 (probe-alextra).
Il2CppObject* il2cpp_arraylist_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_arraylist_get_enumerator_range(Il2CppObject* self, int32_t index, int32_t count);

// ArrayList D-class: range copy / insertion / overwrite. The ICollection
// argument is enumerated through the non-generic IEnumerator surface (the same
// two-pass helper as the ctor); a null argument raises
// ArgumentNullException("c"). GetRange returns a NEW ArrayList whose Capacity
// equals the SOURCE list's current Capacity (net48 copies the whole backing
// array -- probe-alextra pins gr_cap == src_cap), or 0 for an empty source.
Il2CppObject* il2cpp_arraylist_get_range(Il2CppObject* self, int32_t index, int32_t count);
void il2cpp_arraylist_add_range(Il2CppObject* self, Il2CppObject* collection);
void il2cpp_arraylist_insert_range(Il2CppObject* self, int32_t index, Il2CppObject* collection);
void il2cpp_arraylist_set_range(Il2CppObject* self, int32_t index, Il2CppObject* collection);

// ArrayList E-class: ToArray. ToArray() is a NEW object[] with exactly the
// current elements (the reference-slot copy of to_array). ToArray(Type)
// returns an array of the REQUESTED element type: an int[]/string[]/object[]
// shaped result whose elements are copied with the net48 cast semantics
// (a value element must match the target primitive; a reference element must
// match the target reference type; a mismatch raises InvalidCastException --
// probe-alextra pins int->string / int->long / int->int[] mismatches).
Il2CppObject* il2cpp_arraylist_to_array(Il2CppObject* self);
Il2CppObject* il2cpp_arraylist_to_array_typed(Il2CppObject* self, Il2CppObject* type);

// ArrayList F-class: LastIndexOf / Sort / BinarySearch (the value-family
// scans). LastIndexOf uses the same value equality as IndexOf, scanning
// BACKWARD; the 2-arg form bounds the scan to [0, startIndex] and the 3-arg
// form to [startIndex - count + 1, startIndex]. Sort() orders by
// Comparer.Default (ComparerCompareCore) through an in-place quicksort;
// Sort(IComparer) and Sort(index, count, IComparer) take a user comparer
// (a null comparer means Comparer.Default). BinarySearch returns the index or
// the bitwise complement of the insertion point (net48).
int32_t il2cpp_arraylist_last_index_of(Il2CppObject* self, Il2CppObject* value);
int32_t il2cpp_arraylist_last_index_of_start(Il2CppObject* self, Il2CppObject* value, int32_t startIndex);
int32_t il2cpp_arraylist_last_index_of_range(Il2CppObject* self, Il2CppObject* value, int32_t startIndex, int32_t count);
void il2cpp_arraylist_sort(Il2CppObject* self);
void il2cpp_arraylist_sort_comparer(Il2CppObject* self, Il2CppObject* comparer);
void il2cpp_arraylist_sort_range_comparer(Il2CppObject* self, int32_t index, int32_t count, Il2CppObject* comparer);
int32_t il2cpp_arraylist_binary_search(Il2CppObject* self, Il2CppObject* value);
int32_t il2cpp_arraylist_binary_search_comparer(Il2CppObject* self, Il2CppObject* value, Il2CppObject* comparer);
int32_t il2cpp_arraylist_binary_search_range_comparer(Il2CppObject* self, int32_t index, int32_t count, Il2CppObject* value, Il2CppObject* comparer);

// ArrayList G-class: the static wrappers. Repeat(value, count) builds a fresh
// list of 'count' copies (a negative count raises
// ArgumentOutOfRangeException). Adapter(list) / FixedSize(list) / ReadOnly(list)
// / Synchronized(list) return a WRAPPER view over the receiver: the wrapper
// shares the receiver's storage, reports the receiver's flags
// (IsFixedSize / IsReadOnly / IsSynchronized), and mutating calls on a
// read-only / fixed-size wrapper raise NotSupportedException (net48). The
// wrapper's SyncRoot is a fresh System.Object (net48's wrapper SyncRoot is a
// new object, never the wrapper itself).
Il2CppObject* il2cpp_arraylist_repeat(Il2CppObject* value, int32_t count);
Il2CppObject* il2cpp_arraylist_adapter(Il2CppObject* list);
Il2CppObject* il2cpp_arraylist_fixed_size(Il2CppObject* list);
Il2CppObject* il2cpp_arraylist_read_only(Il2CppObject* list);
Il2CppObject* il2cpp_arraylist_synchronized(Il2CppObject* list);

// ArrayList H-class: the CopyTo(index, array, index, count) overload (the
// 4-arg ICollection.CopyTo shape). The bounds contract matches net48: a
// negative source index / destination index / count raises
// ArgumentOutOfRangeException; an over-long copy raises ArgumentException; a
// null destination raises ArgumentNullException.
void il2cpp_arraylist_copy_to_range(Il2CppObject* self, int32_t index, Il2CppObject* array, int32_t arrayIndex, int32_t count);

} // namespace il2cpp
