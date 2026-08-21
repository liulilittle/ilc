#pragma once
//------------------------------------------------------------------------------
// dictsi_obj.h -- the GC-OBJECT wrapper for the string->int32 dictionary
// (System.Collections.Generic.Dictionary<string,int>, rtdict-b23 DICT-GC).
//
// The raw dictsi domain (dictsi.h) is a plain C++ heap structure INVISIBLE to
// the collector. The transpiler emits `il2cpp_object_new(&System_Collections_
// Generic_Dictionary_2_string_int_class)` for `new Dictionary<string,int>()`,
// so the runtime exposes a MANAGED wrapper object whose instance holds the
// Il2CppDictSI* in a NON-reference slot:
//
//   struct Il2CppDictSIObject
//   {
//       Il2CppObject  obj;   // object header (slot 0)
//       Il2CppDictSI* dict;  // slot 1 -- RAW pointer, NOT in the refFieldBitmap
//   };
//
//   GC SAFETY (refFieldBitmap contract): the wrapper class descriptor has
//   refFieldBitmap 0 -- the collector scans NO reference fields, so the raw
//   Il2CppDictSI* is never mistaken for a managed reference. The dict does
//   NOT root its keys (dictsi.h GC CONTRACT): the CALLER must keep every
//   stored key reachable for as long as it remains in the dictionary. In the
//   e2e (dictcase) every key is a string LITERAL -- interned through the
//   g_strPool (il2cpp_string_intern_literal) and rooted by the intern pool
//   for the process lifetime, so the dict's raw key pointers never dangle.
//   Runtime-created keys are the documented limitation (mirror dictsi.h).
//
//   LIFETIME: il2cpp_dict_si_destroy is the 1:1 pair of create. A wrapper
//   that is collected WITHOUT a finalizer leaks its Il2CppDictSI (documented
//   DICT-GC limitation -- no finalizer is registered in this stage; the e2e
//   keeps the dictionary alive for the whole program).
//
//   The icall_* entry points (il2cpp::runtime, hashtable convention) take
//   Il2CppObject* self and extract the dict through the wrapper slot. They
//   mirror the raw dictsi surface PLUS the two wrapper-only semantics:
//     * set_Item (indexer SET)  -> il2cpp_dict_si_set (insert-or-replace)
//     * get_Item (indexer GET)  -> KeyNotFoundException on a miss
//------------------------------------------------------------------------------

#include "dictsi.h"

// hset-b23 (D-HSET-1): the HashSet<string> GC-object wrapper (hashset.h --
// System_Collections_Generic_HashSet_1_string_class GLOBAL descriptor + the
// icall_hashset_string_* wrappers). The transpiler emits the class symbol and
// the icalls for `new HashSet<string>()`, and the generated.h include table
// (CodeGenerator.cs) already lists dictsi_obj.h -- the HashSet wrapper rides
// this include so the generated C++ sees the declarations.
#include "hashset.h"

#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>

namespace il2cpp {

// The managed wrapper object: header + raw dict pointer (slot 1).
struct Il2CppDictSIObject
{
    Il2CppObject  obj;   // slot 0: object header
    Il2CppDictSI* dict;  // slot 1: raw pointer (NOT in the refFieldBitmap)
};

// GLOBAL scope class descriptor -- the transpiler emits
// il2cpp_object_new(&System_Collections_Generic_Dictionary_2_string_int_class)
// for `new Dictionary<string,int>()` (the streamio/stopwatch precedent).
extern Il2CppClass System_Collections_Generic_Dictionary_2_string_int_class;

// Lazy registration + descriptor accessor (constraint #12: no global-ctor
// allocation; idempotent).
Il2CppClass* il2cpp_dict_si_object_class();

namespace runtime {

// .ctor(capacity) -- allocate the dict (capacity 0 for the parameterless
// form) and store it in the wrapper slot.
void icall_dict_si_ctor(Il2CppObject* self, int32_t capacity);

// Add / set_Item (indexer SET: insert-or-replace). The key arrives as a
// managed reference (Il2CppObject* -- the generated code's uniform
// reference representation); the wrapper casts it to Il2CppString*.
void icall_dict_si_add(Il2CppObject* self, Il2CppObject* key, int32_t value);
void icall_dict_si_set(Il2CppObject* self, Il2CppObject* key, int32_t value);

// get_Item (indexer GET): the value or KeyNotFoundException on a miss.
int32_t icall_dict_si_get(Il2CppObject* self, Il2CppObject* key);

// TryGetValue(key, &value): true + value on a hit; false on a miss.
bool icall_dict_si_try_get_value(Il2CppObject* self, Il2CppObject* key, int32_t* value);

// ContainsKey / Remove / get_Count / Clear.
bool icall_dict_si_contains_key(Il2CppObject* self, Il2CppObject* key);
bool icall_dict_si_remove(Il2CppObject* self, Il2CppObject* key);
int32_t icall_dict_si_get_count(Il2CppObject* self);
void icall_dict_si_clear(Il2CppObject* self);

// Keys / Values snapshot arrays (deterministic hash-bucket order).
Il2CppArray* icall_dict_si_get_keys(Il2CppObject* self);
Il2CppArray* icall_dict_si_get_values(Il2CppObject* self);

} // namespace runtime
} // namespace il2cpp
