//------------------------------------------------------------------------------
// dictsi_obj.cpp -- the GC-OBJECT wrapper for the string->int32 dictionary
// (System.Collections.Generic.Dictionary<string,int>, rtdict-b23 DICT-GC;
// see dictsi_obj.h for the layout, GC contract and lifetime contract).
//
// The wrapper is a MANAGED object (Il2CppDictSIObject) whose slot 1 holds the
// raw Il2CppDictSI* (dictsi.h) in a NON-reference field (refFieldBitmap 0).
// The icalls below extract the dict from the wrapper and forward to the raw
// dictsi surface, adding the two wrapper-only semantics:
//   * set_Item -> il2cpp_dict_si_set (insert-or-replace; Add throws on dup)
//   * get_Item -> KeyNotFoundException on a miss
//
// GC SAFETY: the wrapper is a normal managed object (its klass header lets
// the collector mark it); the RAW dict pointer in slot 1 is never scanned.
// The KEYS remain the dictsi.h caller contract -- the caller roots every
// stored key. The e2e (dictcase) uses string LITERALS, interned and rooted
// by the string pool for the process lifetime (see dictsi_obj.h).
//------------------------------------------------------------------------------

#include "dictsi_obj.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_key_not_found_exception
#include "il2cpp_string.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp {

//------------------------------------------------------------------------------
// System.Collections.Generic.Dictionary`2<string,int> runtime descriptor
// (GLOBAL linkage within namespace il2cpp -- the transpiler emits
// il2cpp_object_new(&System_Collections_Generic_Dictionary_2_string_int_class)
// for the newobj from generated code inside namespace il2cpp, and metadata.cpp
// registers it by the same qualified name; the same contract as
// System_Diagnostics_Stopwatch_class in stopwatch.cpp). instanceSize covers
// the header + the raw dict pointer; refFieldBitmap 0 (slot 1 is a RAW
// pointer, NOT a reference).
//------------------------------------------------------------------------------
Il2CppClass System_Collections_Generic_Dictionary_2_string_int_class = {
    "System.Collections.Generic.Dictionary`2<string,int>", // name
    static_cast<int32_t>(sizeof(Il2CppDictSIObject)), // instanceSize
    0u,                                  // refFieldBitmap: no reference fields
    0,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

namespace {

bool g_dictSIObjectRegistered = false;

void EnsureDictSIObjectRegistered()
{
    if (g_dictSIObjectRegistered) return;
    il2cpp_class_register(&System_Collections_Generic_Dictionary_2_string_int_class);
    g_dictSIObjectRegistered = true;
}

// The dict pointer from a wrapper receiver.
Il2CppDictSI* AsDict(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppDictSIObject*>(self)->dict;
}

// A key arriving from generated code is a managed reference (Il2CppObject*);
// the dictsi surface needs Il2CppString* (a null stays null).
Il2CppString* AsKey(Il2CppObject* key)
{
    return reinterpret_cast<Il2CppString*>(key);
}

// Defensive null-receiver guard (hashtable.cpp convention).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_dict_si_object_class -- the wrapper descriptor (lazily registered on
// first use; idempotent).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_dict_si_object_class()
{
    EnsureDictSIObjectRegistered();
    return &System_Collections_Generic_Dictionary_2_string_int_class;
}

namespace runtime {

//------------------------------------------------------------------------------
// icall_dict_si_ctor -- Dictionary<string,int>(capacity) (or the
// parameterless form with capacity 0, appended by the transpiler). Allocates
// the raw dict and stores it in the wrapper slot.
//------------------------------------------------------------------------------
void icall_dict_si_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    reinterpret_cast<Il2CppDictSIObject*>(self)->dict = il2cpp_dict_si_create(capacity);
}

//------------------------------------------------------------------------------
// icall_dict_si_add -- Add(key, value): a duplicate key raises
// ArgumentException and leaves the dictionary unchanged (dictsi.h contract).
//------------------------------------------------------------------------------
void icall_dict_si_add(Il2CppObject* self, Il2CppObject* key, int32_t value)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_si_add(AsDict(self), AsKey(key), value);
}

//------------------------------------------------------------------------------
// icall_dict_si_set -- the indexer SET: insert-or-replace (never throws for
// a missing key).
//------------------------------------------------------------------------------
void icall_dict_si_set(Il2CppObject* self, Il2CppObject* key, int32_t value)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_si_set(AsDict(self), AsKey(key), value);
}

//------------------------------------------------------------------------------
// icall_dict_si_get -- the indexer GET: the stored value, or
// KeyNotFoundException on a miss (net48 Dictionary<string,int> contract).
//------------------------------------------------------------------------------
int32_t icall_dict_si_get(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    int32_t value = 0;
    if (il2cpp_dict_si_try_get_value(AsDict(self), AsKey(key), &value))
    {
        return value;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0; // unreachable (the factory throws)
}

//------------------------------------------------------------------------------
// icall_dict_si_try_get_value -- TryGetValue(key, &value): true + the value
// on a hit; false on a miss (the out-param is left untouched).
//------------------------------------------------------------------------------
bool icall_dict_si_try_get_value(Il2CppObject* self, Il2CppObject* key, int32_t* value)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_try_get_value(AsDict(self), AsKey(key), value);
}

//------------------------------------------------------------------------------
// icall_dict_si_contains_key -- ContainsKey(key).
//------------------------------------------------------------------------------
bool icall_dict_si_contains_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_contains_key(AsDict(self), AsKey(key));
}

//------------------------------------------------------------------------------
// icall_dict_si_remove -- Remove(key): true when the key was present.
//------------------------------------------------------------------------------
bool icall_dict_si_remove(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_remove(AsDict(self), AsKey(key));
}

//------------------------------------------------------------------------------
// icall_dict_si_get_count -- Count.
//------------------------------------------------------------------------------
int32_t icall_dict_si_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_get_count(AsDict(self));
}

//------------------------------------------------------------------------------
// icall_dict_si_clear -- Clear(): count -> 0; the dictionary stays reusable.
//------------------------------------------------------------------------------
void icall_dict_si_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_si_clear(AsDict(self));
}

//------------------------------------------------------------------------------
// icall_dict_si_get_keys -- Keys: a fresh reference array (Il2CppString*
// elements) in the deterministic hash-bucket order.
//------------------------------------------------------------------------------
Il2CppArray* icall_dict_si_get_keys(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_get_keys(AsDict(self));
}

//------------------------------------------------------------------------------
// icall_dict_si_get_values -- Values: a fresh int32 array in the same order
// as Keys (positionally consistent).
//------------------------------------------------------------------------------
Il2CppArray* icall_dict_si_get_values(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_get_values(AsDict(self));
}

}} // namespace il2cpp::runtime
