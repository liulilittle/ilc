#pragma once
//------------------------------------------------------------------------------
// hashset.h -- System.Collections.Generic.HashSet`1 runtime domain.
//
// MODEL (net48 HashSet<T>, byte-exact enumeration): a SLOT array in insertion
// order plus a LIFO free list and a bucket index array. Enumeration walks the
// slot array [0, count) ascending and SKIPS free slots (hashCode < 0), which is
// exactly what net48's Enumerator does. A removed slot is REUSED by the next
// Add (LIFO), so a re-added member returns to its ORIGINAL enumeration
// position -- probe hsetmodel (net48 oracle):
//     add 0..5, remove 2,4 -> 0,1,3,5
//     add 99 -> 0,1,3,99,5      (slot 4 reused)
//     add 98 -> 0,1,98,3,99,5   (slot 2 reused)
// The bucket order is NEVER observable (no public Capacity, enumeration is
// slot-ordered), so the bucket count is an internal detail.
//
// ONE STORAGE FOR EVERY ELEMENT KIND. Members are stored as MANAGED CARRIERS
// (Il2CppObject*: a real reference for reference types, a BOX for value
// types), and membership uses the runtime's universal value semantics --
// icall_object_get_hash_code / icall_object_equals -- which already implement
// the net48 EqualityComparer<T>.Default contract per type:
//     string   -> ordinal content hash / content equality
//     boxed primitives (int/long/uint/double/char/bool/...) -> value hash /
//                 value equality
//     boxed decimal / DateTime / TimeSpan / Guid -> the value hash/equality
//     boxed user struct with an override -> the reflected GetHashCode/Equals
//     any other reference -> address hash / reference equality
// This replaces the historical split (string->dictsi, value->boxed hashtable)
// whose enumeration was BUCKET ordered and therefore failed byte-exact against
// net48 for HashSet<int> (probe hseta1: oracle 3,1,7,2,5 vs runtime 1,2,3,5,7).
//
// CUSTOM IEqualityComparer<T>: a ctor may install a comparer; hashing and
// equality then dispatch the comparer's reflected GetHashCode/Equals slots
// (the same virtual-dispatch discipline the comparer domains use). keyKind
// selects the reflected signature family (int32 vs reference carrier).
//
// GC CONTRACT: keys/hashes/nexts/buckets are MANAGED arrays held in GC-scanned
// slots (refFieldBitmap), so every stored member is rooted by the set itself --
// no caller-rooting contract (unlike dictsi).
//
// LAYOUT:
//   struct Il2CppHashSetObject
//   {
//       Il2CppObject  obj;            // slot 0
//       Il2CppArray*  keys;           // slot 1: carriers (ref array; null = free)
//       Il2CppArray*  hashes;         // slot 2: int32 cached hash (-1 = free)
//       Il2CppArray*  nexts;          // slot 3: int32 bucket-chain / free-list link
//       Il2CppArray*  buckets;        // slot 4: int32 bucket -> slot index
//       Il2CppObject* comparer;       // slot 5: custom IEqualityComparer or null
//       int32_t       bucketCount;    // slot 6
//       int32_t       count;          // slot 7: slots ever used (INCLUDES free)
//       int32_t       entryCapacity;  // slot 8
//       int32_t       freeList;       // slot 9: free-slot chain head (-1 = none)
//       int32_t       freeCount;      // slot 10
//       int32_t       keyKind;        // slot 11: comparer signature family
//       int32_t       family;         // slot 11: element family (0 ref, 1 i4,
//                                     //   2 i8, 3 double, 4 uint, 5 decimal)
//   };
//------------------------------------------------------------------------------

#include "decimal.h"          // Il2CppDecimal (the decimal element family)
#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>

namespace il2cpp {

struct Il2CppHashSetObject
{
    Il2CppObject  obj;           // slot 0: object header
    Il2CppArray*  keys;          // slot 1: carriers (GC ref)
    Il2CppArray*  hashes;        // slot 2: int32 hashes (GC ref)
    Il2CppArray*  nexts;         // slot 3: int32 links (GC ref)
    Il2CppArray*  buckets;       // slot 4: int32 buckets (GC ref)
    Il2CppObject* comparer;      // slot 5: custom comparer (GC ref) or null
    int32_t       bucketCount;   // slot 6
    int32_t       count;         // slot 7
    int32_t       entryCapacity; // slot 8
    int32_t       freeList;      // slot 9
    int32_t       freeCount;     // slot 10
    int32_t       keyKind;       // slot 11 (comparer signature family)
    int32_t       family;        // slot 11 upper half: the ELEMENT family
};

// The GLOBAL descriptor keeps its historical symbol: generated code emits
// il2cpp_object_new(&System_Collections_Generic_HashSet_1_string_class) for
// `new HashSet<string>()` (the newobj fallback) and tests/runtime names it.
extern Il2CppClass System_Collections_Generic_HashSet_1_string_class;

// Descriptor accessor for EVERY instantiation (lazy registration; the
// historical string symbol is the same object).
Il2CppClass* il2cpp_hashset_class();

namespace runtime {

// .ctor(capacity) -- allocate the storage. capacity 0 = the parameterless form.
void icall_hashset_int_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_int_add(Il2CppObject* self, int32_t item);
bool icall_hashset_int_remove(Il2CppObject* self, int32_t item);
bool icall_hashset_int_contains(Il2CppObject* self, int32_t item);
int32_t icall_hashset_int_get_count(Il2CppObject* self);
void icall_hashset_int_clear(Il2CppObject* self);
bool icall_hashset_int_try_get_value(Il2CppObject* self, int32_t equalValue, int32_t* actualValue);
void icall_hashset_int_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex);
void icall_hashset_int_copy_to_range(Il2CppObject* self, Il2CppArray* array,
                                     int32_t index, int32_t arrayIndex, int32_t count);
int32_t icall_hashset_int_enumerator_get_current(Il2CppObject* self);

void icall_hashset_long_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_long_add(Il2CppObject* self, int64_t item);
bool icall_hashset_long_remove(Il2CppObject* self, int64_t item);
bool icall_hashset_long_contains(Il2CppObject* self, int64_t item);
int32_t icall_hashset_long_get_count(Il2CppObject* self);
void icall_hashset_long_clear(Il2CppObject* self);
bool icall_hashset_long_try_get_value(Il2CppObject* self, int64_t equalValue, int64_t* actualValue);
void icall_hashset_long_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex);
int64_t icall_hashset_long_enumerator_get_current(Il2CppObject* self);

void icall_hashset_double_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_double_add(Il2CppObject* self, double item);
bool icall_hashset_double_remove(Il2CppObject* self, double item);
bool icall_hashset_double_contains(Il2CppObject* self, double item);
int32_t icall_hashset_double_get_count(Il2CppObject* self);
void icall_hashset_double_clear(Il2CppObject* self);
bool icall_hashset_double_try_get_value(Il2CppObject* self, double equalValue, double* actualValue);
void icall_hashset_double_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex);
double icall_hashset_double_enumerator_get_current(Il2CppObject* self);

void icall_hashset_uint_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_uint_add(Il2CppObject* self, int32_t item);
bool icall_hashset_uint_remove(Il2CppObject* self, int32_t item);
bool icall_hashset_uint_contains(Il2CppObject* self, int32_t item);
int32_t icall_hashset_uint_get_count(Il2CppObject* self);
void icall_hashset_uint_clear(Il2CppObject* self);
bool icall_hashset_uint_try_get_value(Il2CppObject* self, int32_t equalValue, int32_t* actualValue);
void icall_hashset_uint_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex);
int32_t icall_hashset_uint_enumerator_get_current(Il2CppObject* self);

void icall_hashset_string_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_string_add(Il2CppObject* self, Il2CppObject* item);
bool icall_hashset_string_remove(Il2CppObject* self, Il2CppObject* item);
bool icall_hashset_string_contains(Il2CppObject* self, Il2CppObject* item);
int32_t icall_hashset_string_get_count(Il2CppObject* self);
void icall_hashset_string_clear(Il2CppObject* self);
bool icall_hashset_string_try_get_value(Il2CppObject* self, Il2CppObject* equalValue, Il2CppObject** actualValue);
void icall_hashset_string_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex);
Il2CppObject* icall_hashset_string_enumerator_get_current(Il2CppObject* self);

void icall_hashset_ref_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_ref_add(Il2CppObject* self, Il2CppObject* item);
bool icall_hashset_ref_remove(Il2CppObject* self, Il2CppObject* item);
bool icall_hashset_ref_contains(Il2CppObject* self, Il2CppObject* item);
int32_t icall_hashset_ref_get_count(Il2CppObject* self);
void icall_hashset_ref_clear(Il2CppObject* self);
bool icall_hashset_ref_try_get_value(Il2CppObject* self, Il2CppObject* equalValue, Il2CppObject** actualValue);
void icall_hashset_ref_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex);

// decimal / user struct carriers (box on entry, unbox on exit).
void icall_hashset_dec_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_dec_add(Il2CppObject* self, const runtime::Il2CppDecimal* item);
bool icall_hashset_dec_remove(Il2CppObject* self, const runtime::Il2CppDecimal* item);
bool icall_hashset_dec_contains(Il2CppObject* self, const runtime::Il2CppDecimal* item);
int32_t icall_hashset_dec_get_count(Il2CppObject* self);
void icall_hashset_dec_clear(Il2CppObject* self);
void icall_hashset_dec_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex);
// T3 result-slot contract: 'result' is the caller's 16-byte slot (FIRST arg).
void icall_hashset_dec_enumerator_get_current(runtime::Il2CppDecimal* result, Il2CppObject* self);

// comparer ctors (custom IEqualityComparer<T>) and enumerable ctors.
void icall_hashset_int_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer);
void icall_hashset_ref_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer);
void icall_hashset_long_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer);
void icall_hashset_double_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer);
void icall_hashset_uint_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer);
void icall_hashset_dec_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer);

void icall_hashset_int_ctor_enum(Il2CppObject* self, Il2CppObject* items);
void icall_hashset_ref_ctor_enum(Il2CppObject* self, Il2CppObject* items);
void icall_hashset_long_ctor_enum(Il2CppObject* self, Il2CppObject* items);
void icall_hashset_double_ctor_enum(Il2CppObject* self, Il2CppObject* items);
void icall_hashset_uint_ctor_enum(Il2CppObject* self, Il2CppObject* items);
void icall_hashset_dec_ctor_enum(Il2CppObject* self, Il2CppObject* items);

void icall_hashset_int_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer);
void icall_hashset_ref_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer);
void icall_hashset_long_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer);
void icall_hashset_double_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer);
void icall_hashset_uint_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer);
void icall_hashset_dec_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer);

// Set algebra (IEnumerable<T> operand; a HashSet operand iterates its slots
// directly, any other carrier goes through the shared IEnumerable surface).
void icall_hashset_union_with(Il2CppObject* self, Il2CppObject* other);
void icall_hashset_intersect_with(Il2CppObject* self, Il2CppObject* other);
void icall_hashset_except_with(Il2CppObject* self, Il2CppObject* other);
void icall_hashset_symmetric_except_with(Il2CppObject* self, Il2CppObject* other);
bool icall_hashset_set_equals(Il2CppObject* self, Il2CppObject* other);
bool icall_hashset_is_subset_of(Il2CppObject* self, Il2CppObject* other);
bool icall_hashset_is_superset_of(Il2CppObject* self, Il2CppObject* other);
bool icall_hashset_is_proper_subset_of(Il2CppObject* self, Il2CppObject* other);
bool icall_hashset_is_proper_superset_of(Il2CppObject* self, Il2CppObject* other);
bool icall_hashset_overlaps(Il2CppObject* self, Il2CppObject* other);
int32_t icall_hashset_remove_where(Il2CppObject* self, Il2CppObject* match);

// get_Comparer: the installed comparer (null when the default is in use).
Il2CppObject* icall_hashset_get_comparer(Il2CppObject* self);

} // namespace runtime

// Enumeration surface (namespace il2cpp, the dictsi keycollection convention):
// a slot-ordered keys SNAPSHOT wrapped in the shared keycollection enumerator.
Il2CppObject* il2cpp_hashset_int_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_long_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_double_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_uint_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_ref_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_string_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_dec_get_enumerator(Il2CppObject* self);

} // namespace il2cpp
