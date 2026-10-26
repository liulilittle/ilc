#pragma once
//------------------------------------------------------------------------------
// sortedlist.h -- managed non-generic sorted list (System.Collections.
// SortedList semantics, sorted-array icall domain).
//
// Layout (aligned with the object-model convention; every field below is a
// GC-scanned slot in the class descriptor's refFieldBitmap):
//
//   struct Il2CppSortedList
//   {
//       Il2CppObject obj;     // object header: klass pointer
//       Il2CppArray* items;   // reference array (Il2CppObject* elements):
//                             //   [0, capacity)     = keys, ascending order
//                             //   [capacity, 2*cap) = values (index-aligned)
//       int32_t      count;   // number of key/value pairs (NOT a reference)
//       int32_t      capacity;// allocated capacity (NOT a reference)
//   };
//
//   GC SAFETY (refFieldBitmap contract):
//     * Il2CppSortedList -- instanceSize 24 (obj 8 + items 8 + count 4 +
//       capacity 4), refFieldBitmap bit 1 (slot 1 = the items array;
//       count/capacity slots are NOT references). The collector therefore
//       keeps the items array alive whenever the list is reachable.
//     * The items array is a REFERENCE array (il2cpp_array_new with
//       isRefElement == true): the collector scans EVERY element as an
//       Il2CppObject* (see gc.cpp ScanObject), so keys and values (including
//       null values) are kept alive through the array -- verified by the
//       unit tests' collect-and-query section.
//     * All reference stores go through il2cpp_write_barrier first (old ->
//       young card marking, same convention as hashtable.cpp). Bulk shifts
//       use memmove followed by a per-slot barrier pass over the moved
//       region; the trailing slots left by a removal are cleared to null so
//       the collector never scans stale references.
//
// SORTING CONTRACT (implemented in sortedlist.cpp; the key half stays sorted
// in ascending order, so lookups are binary searches):
//   * two boxed System.Int32 keys -- compared by VALUE (ascending);
//   * two System.String keys      -- compared ORDINALLY by UTF-16 code
//     units (.NET Framework semantics: unit-by-unit, code-unit order; the
//     shorter string sorts first when one is a prefix of the other). NOTE:
//     comparison is per code unit, never bytewise -- memcmp would impose
//     little-endian byte order and disagree with .NET on non-ASCII keys;
//   * a boxed Int32 vs a string   -- the Int32 sorts first (a deterministic
//     total order for the mixed case; the .NET default comparer would throw
//     for such a comparison -- this domain instead defines the order);
//   * any other key kind          -- ADDRESS order (ascending pointer value,
//     reference identity). This keeps the total order the binary search
//     requires. NOTE: the M3 collector COMPACTS Gen0 (survivors move), so
//     address-ordered keys are only reliable for non-moving keys -- the
//     supported domain is int/string keys.
//
// GROWTH CONTRACT: the capacity starts at the smallest power of two >= the
// ctor argument (minimum 4; doubling cap 2^29 keeps capacity * 2 inside
// int32). When an insert would exceed capacity, the items array is rebuilt
// at double capacity and every pair is copied with pure pointer writes --
// ONE allocation, then no further allocation until the array is published
// (a collection triggered by that allocation runs before the array exists),
// so the fresh array cannot be reclaimed mid-copy.
//
// .NET SEMANTICS BASELINE (matches System.Collections.SortedList on .NET
// Framework 4.8 for the supported domain):
//   * Add(self, null, v) / GetByKey / IndexOfKey with a null key
//     -> ArgumentNullException("key").
//   * Add with a duplicate key -> ArgumentException (the list is unchanged).
//   * Null VALUES are legal everywhere; get_by_key returns null for both
//     "missing" and "stored null", exactly like the .NET indexer.
//   * GetKeyAt / GetValueAt / GetByIndex / SetAt / RemoveAt with an index
//     outside [0, Count) -> ArgumentOutOfRangeException.
//   * ctor with a negative capacity -> ArgumentOutOfRangeException.
//   * The indexer SET by key (il2cpp_sortedlist_set_by_key) replaces the
//     value of an existing key, or inserts a new pair at its sorted position
//     when the key is absent -- it NEVER throws for a missing key (unlike
//     Add).
//   * Keys / Values return a SNAPSHOT array (not .NET's live ICollection
//     view): Keys holds every key in ascending (sorted) order -- the list's
//     enumeration order -- and Values holds the index-aligned values, so
//     Keys[i] and Values[i] always describe the same pair. A snapshot does
//     not reflect later mutations.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Managed non-generic sorted list (see header comment for layout + GC
// bitmap). Keys live in items[0, capacity) in ascending order; the value of
// the pair at sorted index i lives in items[capacity + i].
struct Il2CppSortedList
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* items;    // reference array: [0,cap) keys, [cap,2cap) values
    int32_t      count;    // number of key/value pairs
    int32_t      capacity; // allocated capacity (items->length / 2)
    // nb9 (M4/M18): the SortedSet<T>(IComparer<T>) / SortedSet<T>(items,
    // comparer) comparer. nullptr = the default (boxed-value) order above;
    // a non-null object is dispatched through its reflected virtual
    // Compare(T,T) slot. The field sits AFTER count/capacity so every existing
    // offset is unchanged (instanceSize 24 -> 32, refFieldBitmap gains bit 3).
    Il2CppObject* comparer;
};

// The System.Collections.SortedList runtime descriptor (registered lazily on
// first use; instanceSize 24, refFieldBitmap bit 1 -- see header comment).
Il2CppClass* il2cpp_sortedlist_class();

namespace runtime {

// SortedList(int capacity) -- allocate the items array. The capacity is the
// smallest power of two >= capacity (minimum 4); a negative capacity raises
// ArgumentOutOfRangeException. 'self' must be a fresh object created with
// il2cpp_object_new(il2cpp_sortedlist_class()).
void il2cpp_sortedlist_create(Il2CppObject* self, int32_t capacity);

// SetComparer(comparer) -- nb9 (M4/M18): install the SortedSet<T> comparer.
// nullptr restores the default boxed-value order. The store goes through a
// write barrier (the container may already be old when the comparer is young).
void il2cpp_sortedlist_set_comparer(Il2CppObject* self, Il2CppObject* comparer);

// SortedList(IComparer comparer) / SortedList(IComparer comparer, int
// capacity) -- allocate the items array (capacity defaults to the minimum when
// omitted) and install the comparer (nullptr = the default boxed-value order).
// This is the net48 (IComparer[, int]) ctor pair (collections_sortedlist.cs:
// 129/143) folded into one self-mutating icall.
void il2cpp_sortedlist_create_with_comparer(Il2CppObject* self, Il2CppObject* comparer,
                                            int32_t capacity);

// SortedList(IDictionary d) / SortedList(IDictionary d, IComparer comparer) --
// allocate the items array with the dictionary's count, install the comparer
// (nullptr = the default order), then copy every key/value pair from the
// dictionary. The non-generic IDictionary is modelled as a Hashtable object
// (the same representation the interface's Count / indexer icalls use), so the
// copy reads the Hashtable's Keys/Values snapshots (same order). A null
// dictionary raises ArgumentNullException (net48).
void il2cpp_sortedlist_create_from_dict(Il2CppObject* self, Il2CppObject* dict,
                                        Il2CppObject* comparer);

// Add(key, value) -- insert a new pair, keeping the array sorted. A null key
// raises ArgumentNullException; a duplicate key (per the sorting contract)
// raises ArgumentException and leaves the list unchanged. Null values are
// legal. May grow (double) when the array is full.
void il2cpp_sortedlist_add(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value);

// GetByIndex(index): the VALUE at the given sorted index (see
// il2cpp_sortedlist_get_value_at). An index outside [0, Count) raises
// ArgumentOutOfRangeException.
Il2CppObject* il2cpp_sortedlist_get_by_index(Il2CppObject* self, int32_t index);

// Indexer GET by key: the stored value, or null when the key is absent (a
// stored null value also yields null -- indistinguishable, like .NET). A
// null key raises ArgumentNullException.
Il2CppObject* il2cpp_sortedlist_get_by_key(Il2CppObject* self, Il2CppObject* key);

// TryGetValue(object key, out object value): true and the stored value on a
// hit; false and a null out value on a miss (net48).
bool il2cpp_sortedlist_try_get_value(Il2CppObject* self, Il2CppObject* key,
                                     Il2CppObject** outValue);

// IndexOfValue(object value): the first sorted index whose value equals the
// argument (net48 value equality), or -1.
int32_t il2cpp_sortedlist_index_of_value(Il2CppObject* self, Il2CppObject* value);

// SetAt(index, value): replace the value at the given sorted index (the key
// order is untouched). An index outside [0, Count) raises
// ArgumentOutOfRangeException.
void il2cpp_sortedlist_set_at(Il2CppObject* self, int32_t index, Il2CppObject* value);

// Indexer SET by key (.NET set_Item): replace the value of an existing key,
// or insert a new pair at its sorted position when the key is absent. A null
// key raises ArgumentNullException. Never throws for a missing key (unlike
// Add) -- the list stays sorted in both cases.
void il2cpp_sortedlist_set_by_key(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value);

// Keys: a snapshot of every key in ascending (sorted) order -- the list's
// enumeration order. The order is IDENTICAL to il2cpp_sortedlist_get_values,
// so Keys[i] and Values[i] always describe the same pair. The result is a
// reference array (Il2CppArray*).
Il2CppObject* il2cpp_sortedlist_get_keys(Il2CppObject* self);

// Values: a snapshot of every value (stored nulls included), index-aligned
// with the sorted Keys (see above).
Il2CppObject* il2cpp_sortedlist_get_values(Il2CppObject* self);

// RemoveAt(index): drop the pair at the given sorted index (the array stays
// sorted). An index outside [0, Count) raises ArgumentOutOfRangeException.
void il2cpp_sortedlist_remove_at(Il2CppObject* self, int32_t index);

// Count: the number of key/value pairs.
int32_t il2cpp_sortedlist_get_count(Il2CppObject* self);

// GetKey(index): the KEY at the given sorted index. An index outside
// [0, Count) raises ArgumentOutOfRangeException.
Il2CppObject* il2cpp_sortedlist_get_key_at(Il2CppObject* self, int32_t index);

// GetValueAt(index): the VALUE at the given sorted index. An index outside
// [0, Count) raises ArgumentOutOfRangeException.
Il2CppObject* il2cpp_sortedlist_get_value_at(Il2CppObject* self, int32_t index);

// IndexOfKey(key): the sorted index of the key, or -1 when absent. A null
// key raises ArgumentNullException.
int32_t il2cpp_sortedlist_index_of_key(Il2CppObject* self, Il2CppObject* key);

// ContainsKey(key): 1 when the key is present, 0 when absent (a stored null
// value still counts as present). A null key raises ArgumentNullException.
int32_t il2cpp_sortedlist_contains_key(Il2CppObject* self, Il2CppObject* key);

// Remove(key): drop the pair with the given key; a missing key is a no-op
// (unlike RemoveAt). A null key raises ArgumentNullException.
void il2cpp_sortedlist_remove_by_key(Il2CppObject* self, Il2CppObject* key);

// Capacity: the allocated capacity (always >= Count).
int32_t il2cpp_sortedlist_get_capacity(Il2CppObject* self);

// Clear(): drop every pair (every slot becomes null; the entries become
// unreachable and are reclaimed by the next collection). Count -> 0; the
// capacity is unchanged.
void il2cpp_sortedlist_clear(Il2CppObject* self);

//------------------------------------------------------------------------------
// Snapshot enumerator surface -- ICollection.Count / IEnumerable enumeration
// over the Keys / Values snapshots (D-SL-3, sldeep). The snapshots returned by
// il2cpp_sortedlist_get_keys / get_values are REFERENCE ARRAYS (Il2CppArray*);
// the interface methods route through this enumerator domain:
//
//   ICollection.get_Count      -> il2cpp_sortedlist_snapshot_get_count
//   IEnumerable.GetEnumerator  -> il2cpp_sortedlist_snapshot_get_enumerator
//   IEnumerator.MoveNext       -> il2cpp_sortedlist_enumerator_move_next
//   IEnumerator.get_Current    -> il2cpp_sortedlist_enumerator_get_current
//   IEnumerator.Reset          -> il2cpp_sortedlist_enumerator_reset
//   IDisposable.Dispose        -> il2cpp_sortedlist_enumerator_dispose (no-op)
//
// The enumerator object (Il2CppArrayEnumerator) wraps the snapshot array plus
// a next-index cursor; it is a normal managed object (klass + GC bitmap), so
// the snapshot array it references stays reachable while the foreach runs.
//
// VERSION-CHECK DIVERGENCE (recorded, D-STK-1 family): net48's SortedList
// enumerator is a LIVE view -- mutating the list after GetEnumerator() throws
// InvalidOperationException on the next MoveNext. THIS runtime's enumerator
// walks a FROZEN Keys/Values snapshot array: the enumerator has NO reference
// to the list (GetEnumerator is routed on the snapshot array itself, so the
// list is unreachable from the icall), so a list mutation after GetEnumerator
// CANNOT be detected -- the snapshot walk simply continues (frozen-copy
// semantics). This is a DOCUMENTED divergence, not a version-check gap; the
// Keys/Values snapshots themselves are already documented as frozen copies
// ("A snapshot does not reflect later mutations").
//------------------------------------------------------------------------------
int32_t il2cpp_sortedlist_snapshot_get_count(Il2CppObject* self);
Il2CppObject* il2cpp_sortedlist_snapshot_get_enumerator(Il2CppObject* self);
int32_t il2cpp_sortedlist_enumerator_move_next(Il2CppObject* self);
Il2CppObject* il2cpp_sortedlist_enumerator_get_current(Il2CppObject* self);
void il2cpp_sortedlist_enumerator_reset(Il2CppObject* self);
void il2cpp_sortedlist_enumerator_dispose(Il2CppObject* self);

} // namespace runtime

//------------------------------------------------------------------------------
// Snapshot enumerator layout: header + the wrapped snapshot array (a GC ref,
// bitmap bit 1) + the next-index cursor (NOT a reference).
//------------------------------------------------------------------------------
struct Il2CppArrayEnumerator
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* array;    // the snapshot array (GC ref, bitmap bit 1)
    int32_t index;         // next index to return (-1 = before the first)
};

} // namespace il2cpp

//------------------------------------------------------------------------------
// GLOBAL-scope class descriptors for the snapshot enumerator + the
// System.IDisposable interface (the foreach finally emits
// `isinst System.IDisposable` / `IDisposable.Dispose()` over the enumerator;
// the enumerator does NOT implement IDisposable, so the isinst guard never
// runs Dispose, but the symbols must still link). Defined at global scope in
// sortedlist.cpp with external linkage; declared here so generated code and
// the runtime share the same addresses.
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Collections_SortedList_Enumerator_class;
extern il2cpp::Il2CppClass System_IDisposable_class;

//------------------------------------------------------------------------------
// GLOBAL-scope class descriptor -- the transpiler emits
// &System_Collections_SortedList_class for isinst/castclass (and the newobj
// fallback), the same contract as System_Collections_Stack_class in
// stackqueue.h / System_Version_class in versionuri.cpp. Defined in
// sortedlist.cpp at GLOBAL scope with external linkage; registered lazily
// through the il2cpp_sortedlist_class() accessor, which returns its address.
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Collections_SortedList_class;
