#pragma once
//------------------------------------------------------------------------------
// hashset.h -- the GC-OBJECT wrapper for HashSet<string> (D-HSET-1, hset-b23).
//
// System.Collections.Generic.HashSet<string> is a set of UNIQUE members. Its
// storage REUSES the dictsi (string->int32) domain: each member is a dictsi
// key whose value is always the presence marker 1. The wrapper is a MANAGED
// object (Il2CppHashSetStringObject) whose slot 1 holds the raw Il2CppDictSI*
// in a NON-reference field (refFieldBitmap 0), mirroring the Dictionary
//<string,int> wrapper (dictsi_obj.h):
//
//   struct Il2CppHashSetStringObject
//   {
//       Il2CppObject  obj;   // object header (slot 0)
//       Il2CppDictSI* set;   // slot 1 -- RAW pointer, NOT in the refFieldBitmap
//   };
//
//   GC SAFETY (refFieldBitmap contract): the wrapper class descriptor has
//   refFieldBitmap 0 -- the collector scans NO reference fields, so the raw
//   Il2CppDictSI* is never mistaken for a managed reference. The set does NOT
//   root its keys (dictsi.h GC CONTRACT): the CALLER keeps every stored
//   member reachable. In the e2e (hashsetdeep) every member is a string
//   LITERAL -- interned through g_strPool and rooted for the process lifetime.
//
//   LIFETIME: il2cpp_dict_si_destroy is the 1:1 pair of create. A wrapper that
//   is collected WITHOUT a finalizer leaks its Il2CppDictSI (documented
//   limitation, mirror dictsi_obj.h -- the e2e keeps the set alive for the
//   whole program).
//
//   The icall_* entry points (il2cpp::runtime, hashtable/dictsi convention)
//   take Il2CppObject* self and extract the set through the wrapper slot.
//   Member semantics (net48 probe, hset-b23):
//     Add(item)     -> bool: true when ADDED (the member was absent); false
//                     when already present (no-op). Duplicates never throw.
//     Remove(item)  -> bool: true when the member was present (removed).
//     Contains(item)-> bool: membership by ordinal content equality.
//     Count         -> int32: number of members.
//     Clear()       -> drops every member; Count -> 0; the set stays reusable.
//
//   The class descriptor is a runtime GLOBAL
//   (System_Collections_Generic_HashSet_1_string_class) because the transpiler
//   emits `il2cpp_object_new(&System_Collections_Generic_HashSet_1_string_
//   class)` for `new HashSet<string>()` (BclAllocExpr has no HashSet entry --
//   the newobj fallback references the global symbol; dictsi_obj.cpp precedent).
//   It is lazily REGISTERED on first ctor use (il2cpp_class_register is
//   idempotent; the minimal e2e performs no isinst/class_by_name on the type,
//   registration is kept for conformance-scan consistency).
//------------------------------------------------------------------------------

#include "dictsi.h"

#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>

namespace il2cpp {

// The managed wrapper object: header + raw set pointer (slot 1).
struct Il2CppHashSetStringObject
{
    Il2CppObject  obj;   // slot 0: object header
    Il2CppDictSI* set;   // slot 1: raw pointer (NOT in the refFieldBitmap)
};

// GLOBAL scope class descriptor -- the transpiler emits
// il2cpp_object_new(&System_Collections_Generic_HashSet_1_string_class) for
// `new HashSet<string>()` (the newobj fallback, CodeGenerator.cs BclAllocExpr).
extern Il2CppClass System_Collections_Generic_HashSet_1_string_class;

namespace runtime {

// .ctor(capacity) -- allocate the raw set (capacity 0 for the parameterless
// form) and store it in the wrapper slot.
void icall_hashset_string_ctor(Il2CppObject* self, int32_t capacity);

// Add(item): true when the member was ABSENT (inserted); false when already
// present. The item arrives as a managed reference (Il2CppObject*); the
// wrapper casts it to Il2CppString*.
bool icall_hashset_string_add(Il2CppObject* self, Il2CppObject* item);

// Remove(item): true when the member was present (removed).
bool icall_hashset_string_remove(Il2CppObject* self, Il2CppObject* item);

// Contains(item): membership by ordinal content equality.
bool icall_hashset_string_contains(Il2CppObject* self, Il2CppObject* item);

// get_Count: the number of members.
int32_t icall_hashset_string_get_count(Il2CppObject* self);

// Clear(): count -> 0; the set stays reusable.
void icall_hashset_string_clear(Il2CppObject* self);

//------------------------------------------------------------------------------
// G4: HashSet<int> -- the int-member expansion. It REUSES the non-generic
// hashtable object model (il2cpp_hashtable.h) with BOXED int32 members: the
// hashtable's boxed-primitive key contract (value hash + value equality)
// gives net48 HashSet<int> membership for free, and its entries root every
// stored key (no dictsi-style caller-rooting contract). The receiver is the
// same hashtable object (the transpiler allocates it through
// il2cpp_hashtable_class()).
//
//   Add(item)     -> bool: true when ADDED; false when already present.
//   Remove(item)  -> bool: true when present (removed).
//   Contains(item)-> bool: membership by value equality.
//   Count         -> the number of members.
//   Clear()       -> count -> 0; reusable.
//------------------------------------------------------------------------------
void icall_hashset_int_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_int_add(Il2CppObject* self, int32_t item);
bool icall_hashset_int_remove(Il2CppObject* self, int32_t item);
bool icall_hashset_int_contains(Il2CppObject* self, int32_t item);
int32_t icall_hashset_int_get_count(Il2CppObject* self);
void icall_hashset_int_clear(Il2CppObject* self);
int32_t icall_hashset_int_enumerator_get_current(Il2CppObject* self);
// T2 W3: long/double/uint/ref families (hashset.cpp).
void icall_hashset_long_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_long_add(Il2CppObject* self, int64_t item);
bool icall_hashset_long_remove(Il2CppObject* self, int64_t item);
bool icall_hashset_long_contains(Il2CppObject* self, int64_t item);
int32_t icall_hashset_long_get_count(Il2CppObject* self);
void icall_hashset_long_clear(Il2CppObject* self);
int64_t icall_hashset_long_enumerator_get_current(Il2CppObject* self);
void icall_hashset_double_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_double_add(Il2CppObject* self, double item);
bool icall_hashset_double_remove(Il2CppObject* self, double item);
bool icall_hashset_double_contains(Il2CppObject* self, double item);
int32_t icall_hashset_double_get_count(Il2CppObject* self);
void icall_hashset_double_clear(Il2CppObject* self);
double icall_hashset_double_enumerator_get_current(Il2CppObject* self);
void icall_hashset_uint_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_uint_add(Il2CppObject* self, int32_t item);
bool icall_hashset_uint_remove(Il2CppObject* self, int32_t item);
bool icall_hashset_uint_contains(Il2CppObject* self, int32_t item);
int32_t icall_hashset_uint_get_count(Il2CppObject* self);
void icall_hashset_uint_clear(Il2CppObject* self);
int32_t icall_hashset_uint_enumerator_get_current(Il2CppObject* self);
void icall_hashset_ref_ctor(Il2CppObject* self, int32_t capacity);
bool icall_hashset_ref_add(Il2CppObject* self, Il2CppObject* item);
bool icall_hashset_ref_remove(Il2CppObject* self, Il2CppObject* item);
bool icall_hashset_ref_contains(Il2CppObject* self, Il2CppObject* item);
int32_t icall_hashset_ref_get_count(Il2CppObject* self);
void icall_hashset_ref_clear(Il2CppObject* self);

} // namespace runtime

// G4 enumeration surface (namespace il2cpp, the dict_si_keycollection
// convention): the boxed-int KEYS snapshot (il2cpp_hashtable_get_keys)
// wrapped in the dictsi keycollection enumerator object; the element
// get_Current (runtime namespace, above) unboxes the int32 value.
Il2CppObject* il2cpp_hashset_int_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_long_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_double_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_uint_get_enumerator(Il2CppObject* self);
Il2CppObject* il2cpp_hashset_ref_get_enumerator(Il2CppObject* self);

// G4 B2 (b2b8): the HashSet<string> enumeration surface -- the string KEYS
// snapshot (il2cpp_dict_si_get_keys) wrapped in the same dictsi keycollection
// enumerator object; the element get_Current passes the string reference
// through (resolver: il2cpp_dict_si_keycollection_enumerator_get_current).
Il2CppObject* il2cpp_hashset_string_get_enumerator(Il2CppObject* self);

} // namespace il2cpp
