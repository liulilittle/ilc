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

#include "dictgen.h"               // b5-gaps section 104: the gen-wrapper forward
#include "il2cpp_exception_ext.h" // il2cpp_throw_key_not_found_exception
#include "il2cpp_gc.h"             // il2cpp_write_barrier
#include "il2cpp_gcroot.h"         // GcRoot
#include "il2cpp_object.h"         // il2cpp_object_new / il2cpp_class_register
#include "il2cpp_string.h"

#include <cstddef>
#include <cstdint>
#include <cstring>

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

// b5-gaps section 104: a LINQ ToDictionary result whose KEY is a string and
// whose VALUE is an int32 is materialised through the DICTGEN wrapper
// (keysRoot + a boxed int32 value carrier) -- a DIFFERENT object layout from
// this SI domain's own wrapper. The IL TypeSpec parent name is IDENTICAL for
// both shapes, so the transpiler cannot tell them apart at the dispatch; the
// SI icalls detect the gen wrapper by its klass and forward to the gen arms
// (the gen wrapper's slot 1 is the raw Il2CppDictGen*, slot 2 the keysRoot).
bool IsDictGenWrapper(Il2CppObject* self)
{
    return self != nullptr && self->klass == &System_Collections_Generic_Dictionary_2_gen_class;
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

//------------------------------------------------------------------------------
// The two enumeration-object descriptors (P25d, dictp25).
//   System_Collections_Generic_Dictionary_2_Enumerator_string_int_class:
//     instanceSize = header(8) + keys(8) + values(8) + index(4) -> 28, padded
//     to 32; refFieldBitmap 0b110 = slots 1+2 (the two GC arrays).
//   System_Collections_Generic_KeyValuePair_2_string_int_class:
//     instanceSize = header(8) + key(8) + value(4) -> 20, padded to 24;
//     refFieldBitmap 0b010 = slot 1 (the GC key).
//------------------------------------------------------------------------------
Il2CppClass System_Collections_Generic_Dictionary_2_Enumerator_string_int_class = {
    "System.Collections.Generic.Dictionary`2<string,int>+Enumerator", // name
    static_cast<int32_t>(sizeof(Il2CppDictSIEnumerator)), // instanceSize
    0b110u,                              // refFieldBitmap: slots 1+2 (keys/values)
    2,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

Il2CppClass System_Collections_Generic_KeyValuePair_2_string_int_class = {
    "System.Collections.Generic.KeyValuePair`2<string,int>", // name
    static_cast<int32_t>(sizeof(Il2CppDictKvpSI)), // instanceSize
    0b010u,                              // refFieldBitmap: slot 1 (the key)
    1,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

namespace {

bool g_dictEnumRegistered = false;

void EnsureDictEnumRegistered()
{
    if (g_dictEnumRegistered) return;
    il2cpp_class_register(&System_Collections_Generic_Dictionary_2_Enumerator_string_int_class);
    il2cpp_class_register(&System_Collections_Generic_KeyValuePair_2_string_int_class);
    g_dictEnumRegistered = true;
}

// Content-equality (ordinal) for the array-scan Contains (same authority as
// dictsi.cpp's KeyEquals -- equal content, equal key).
bool StrOrdinalEqual(const Il2CppString* a, const Il2CppString* b)
{
    if (a == b) return true;
    return a != nullptr && b != nullptr &&
           a->length == b->length &&
           std::memcmp(a->chars, b->chars,
                       static_cast<size_t>(a->length) * sizeof(char16_t)) == 0;
}

int32_t EnumeratorLength(const Il2CppDictSIEnumerator* en)
{
    return en->keys != nullptr ? en->keys->length : en->values->length;
}

} // namespace

Il2CppClass* il2cpp_dict_si_enumerator_class()
{
    EnsureDictEnumRegistered();
    return &System_Collections_Generic_Dictionary_2_Enumerator_string_int_class;
}

Il2CppClass* il2cpp_kvp_si_class()
{
    EnsureDictEnumRegistered();
    return &System_Collections_Generic_KeyValuePair_2_string_int_class;
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
    if (IsDictGenWrapper(self))
    {
        // b5-gaps section 104: forward to the gen arm (keyKind=0 string,
        // valueKind=1 boxed int32).
        il2cpp::runtime::icall_dict_gen_add_o(self, key, il2cpp::il2cpp_dict_gen_box_int32(value), 0, 1);
        return;
    }
    il2cpp_dict_si_add(AsDict(self), AsKey(key), value);
}

//------------------------------------------------------------------------------
// icall_dict_si_set -- the indexer SET: insert-or-replace (never throws for
// a missing key).
//------------------------------------------------------------------------------
void icall_dict_si_set(Il2CppObject* self, Il2CppObject* key, int32_t value)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        il2cpp::runtime::icall_dict_gen_set_o(self, key, il2cpp::il2cpp_dict_gen_box_int32(value), 0, 1);
        return;
    }
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
    if (IsDictGenWrapper(self))
    {
        // The gen wrapper's key is a STRING -- get_o returns the boxed int32
        // carrier, unboxed to the leaf.
        Il2CppObject* boxed = il2cpp::runtime::icall_dict_gen_get_o(self, key, 0, 1);
        return boxed != nullptr ? il2cpp::il2cpp_dict_gen_unbox_int32(boxed) : 0;
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
// b5-gaps section 106: the int64-value SI twins. The int64-value wrapper is a
// DICTGEN wrapper (the int64 carrier is a boxed int64), so every arm forwards
// to the gen table with (keyKind=0 string, valueKind=16 boxed int64).
//------------------------------------------------------------------------------
void icall_dict_si_add_l(Il2CppObject* self, Il2CppObject* key, int64_t value)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        il2cpp::runtime::icall_dict_gen_add_l_o(self, key, value, 0, 16);
        return;
    }
    // The SI wrapper itself never stores an int64 value; keep the loud
    // contract rather than silently narrowing to int32.
    il2cpp_throw_not_supported_exception(
        "Dictionary<string,int> has no int64-value surface (b5-gaps section 106).");
}

void icall_dict_si_set_l(Il2CppObject* self, Il2CppObject* key, int64_t value)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        il2cpp::runtime::icall_dict_gen_set_l_o(self, key, value, 0, 16);
        return;
    }
    il2cpp_throw_not_supported_exception(
        "Dictionary<string,int> has no int64-value surface (b5-gaps section 106).");
}

int64_t icall_dict_si_get_l(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    if (IsDictGenWrapper(self))
    {
        return il2cpp::runtime::icall_dict_gen_get_l_o(self, key, 0, 16);
    }
    il2cpp_throw_not_supported_exception(
        "Dictionary<string,int> has no int64-value surface (b5-gaps section 106).");
    return 0; // unreachable (the factory throws)
}

bool icall_dict_si_try_get_value_l(Il2CppObject* self, Il2CppObject* key, int64_t* value)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        return il2cpp::runtime::icall_dict_gen_try_get_value_l_o(self, key, value, 0, 16);
    }
    il2cpp_throw_not_supported_exception(
        "Dictionary<string,int> has no int64-value surface (b5-gaps section 106).");
    return false; // unreachable (the factory throws)
}

//------------------------------------------------------------------------------
// icall_dict_si_try_get_value -- TryGetValue(key, &value): true + the value
// on a hit; false on a miss (the out-param is left untouched).
//------------------------------------------------------------------------------
bool icall_dict_si_try_get_value(Il2CppObject* self, Il2CppObject* key, int32_t* value)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        Il2CppObject* boxed = nullptr;
        const bool found = il2cpp::runtime::icall_dict_gen_try_get_value_o(
            self, key, &boxed, 0, 1);
        if (found && value != nullptr)
        {
            *value = boxed != nullptr ? il2cpp::il2cpp_dict_gen_unbox_int32(boxed) : 0;
        }
        return found;
    }
    return il2cpp_dict_si_try_get_value(AsDict(self), AsKey(key), value);
}

//------------------------------------------------------------------------------
// icall_dict_si_contains_key -- ContainsKey(key).
//------------------------------------------------------------------------------
bool icall_dict_si_contains_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        return il2cpp::runtime::icall_dict_gen_contains_key_o(self, key, 0, 1);
    }
    return il2cpp_dict_si_contains_key(AsDict(self), AsKey(key));
}

//------------------------------------------------------------------------------
// icall_dict_si_remove -- Remove(key): true when the key was present.
//------------------------------------------------------------------------------
bool icall_dict_si_remove(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        return il2cpp::runtime::icall_dict_gen_remove_o(self, key, 0, 1);
    }
    return il2cpp_dict_si_remove(AsDict(self), AsKey(key));
}

//------------------------------------------------------------------------------
// icall_dict_si_get_count -- Count.
//------------------------------------------------------------------------------
int32_t icall_dict_si_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        return il2cpp::runtime::icall_dict_gen_get_count(self);
    }
    return il2cpp_dict_si_get_count(AsDict(self));
}

//------------------------------------------------------------------------------
// icall_dict_si_clear -- Clear(): count -> 0; the dictionary stays reusable.
//------------------------------------------------------------------------------
void icall_dict_si_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        il2cpp::runtime::icall_dict_gen_clear(self);
        return;
    }
    il2cpp_dict_si_clear(AsDict(self));
}

//------------------------------------------------------------------------------
// icall_dict_si_get_keys -- Keys: a fresh reference array (Il2CppString*
// elements) in insertion order.
//------------------------------------------------------------------------------
Il2CppArray* icall_dict_si_get_keys(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        return il2cpp::runtime::icall_dict_gen_get_keys_o(self, 0);
    }
    return il2cpp_dict_si_get_keys(AsDict(self));
}

//------------------------------------------------------------------------------
// icall_dict_si_get_values -- Values: a fresh int32 array in the same order
// as Keys (positionally consistent).
//------------------------------------------------------------------------------
Il2CppArray* icall_dict_si_get_values(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    if (IsDictGenWrapper(self))
    {
        return il2cpp::runtime::icall_dict_gen_get_values_i(self);
    }
    return il2cpp_dict_si_get_values(AsDict(self));
}

}} // namespace il2cpp::runtime

//------------------------------------------------------------------------------
// P25d (dictp25) enumeration surface (namespace il2cpp -- the list_i4_
// enumerator / il2cpp_ienum_* convention the transpiler emits).
//------------------------------------------------------------------------------
namespace il2cpp {

//------------------------------------------------------------------------------
// il2cpp_dict_si_get_enumerator -- Dictionary`2+Enumerator..ctor result: a
// SNAPSHOT pair enumerator over the dictionary's current Keys + Values (the
// insertion order, positionally consistent -- dictsi.h). The
// enumerator holds both arrays; a later dictionary mutation cannot affect the
// ongoing foreach (snapshot independence -- net48 foreach semantics).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_dict_si_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureDictEnumRegistered();
    GcRoot<Il2CppObject*> root(self, "il2cpp_dict_si_get_enumerator");
    Il2CppDictSI* d = AsDict(self);
    Il2CppArray* keys = il2cpp_dict_si_get_keys(d);
    GcRoot<Il2CppArray*> keysRoot(keys, "dict_enumerator_keys");
    self = root.Get();
    d = AsDict(self);
    Il2CppArray* values = il2cpp_dict_si_get_values(d);
    GcRoot<Il2CppArray*> valuesRoot(values, "dict_enumerator_values");
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(
        il2cpp_object_new(&System_Collections_Generic_Dictionary_2_Enumerator_string_int_class));
    keys = keysRoot.Get();
    values = valuesRoot.Get();
    en->keys = keys;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->keys),
                         reinterpret_cast<Il2CppObject*>(en->keys));
    en->values = values;
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->values),
                         reinterpret_cast<Il2CppObject*>(en->values));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

int32_t il2cpp_dict_si_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(self);
    ++en->index;
    return en->index < EnumeratorLength(en) ? 1 : 0;
}

Il2CppObject* il2cpp_dict_si_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(self);
    const int32_t i = en->index;
    // net48: before the first MoveNext (and after the enumerator is
    // exhausted / Reset) Current is default(KeyValuePair<string,int>) --
    // Key = null, Value = 0 -- NOT an exception (struct enumerator semantics).
    if (i < 0 || i >= EnumeratorLength(en))
    {
        Il2CppDictKvpSI* dflt = reinterpret_cast<Il2CppDictKvpSI*>(
            il2cpp_object_new(&System_Collections_Generic_KeyValuePair_2_string_int_class));
        dflt->key = nullptr;
        dflt->value = 0;
        return reinterpret_cast<Il2CppObject*>(dflt);
    }
    Il2CppObject* const* keys = reinterpret_cast<Il2CppObject* const*>(en->keys->data);
    const int32_t* values = reinterpret_cast<const int32_t*>(en->values->data);
    Il2CppDictKvpSI* kvp = reinterpret_cast<Il2CppDictKvpSI*>(
        il2cpp_object_new(&System_Collections_Generic_KeyValuePair_2_string_int_class));
    kvp->key = reinterpret_cast<Il2CppString*>(keys[i]);
    il2cpp_write_barrier(reinterpret_cast<void**>(&kvp->key),
                         reinterpret_cast<Il2CppObject*>(kvp->key));
    kvp->value = values[i];
    return reinterpret_cast<Il2CppObject*>(kvp);
}

void il2cpp_dict_si_enumerator_dispose(Il2CppObject* self)
{
    // no-op (the enumerator holds snapshots; nothing to release).
    (void)self;
}

Il2CppObject* il2cpp_kvp_string_int_get_key(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(
        reinterpret_cast<Il2CppDictKvpSI*>(self)->key);
}

int32_t il2cpp_kvp_string_int_get_value(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppDictKvpSI*>(self)->value;
}

//------------------------------------------------------------------------------
// The KeyCollection / ValueCollection enumerators: the KEY / VALUE SNAPSHOT
// ARRAY is the receiver (get_Keys/get_Values verdict + KeyCollection.Count
// = the array length). GetEnumerator wraps it -- no copy (the array is the
// frozen snapshot dictsi.h produces; mutating the DICTIONARY after
// GetEnumerator cannot touch it).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_dict_si_keycollection_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureDictEnumRegistered();
    GcRoot<Il2CppObject*> root(self, "il2cpp_dict_si_keycollection_get_enumerator");
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(
        il2cpp_object_new(&System_Collections_Generic_Dictionary_2_Enumerator_string_int_class));
    self = root.Get();
    en->keys = reinterpret_cast<Il2CppArray*>(self);
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->keys),
                         reinterpret_cast<Il2CppObject*>(en->keys));
    en->values = nullptr;
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

int32_t il2cpp_dict_si_keycollection_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(self);
    ++en->index;
    return en->index < en->keys->length ? 1 : 0;
}

Il2CppObject* il2cpp_dict_si_keycollection_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(self);
    if (en->index < 0 || en->index >= en->keys->length)
    {
        il2cpp_throw_invalid_operation_exception("Enumeration has either not started or has already finished.");
    }
    Il2CppObject* const* keys = reinterpret_cast<Il2CppObject* const*>(en->keys->data);
    return reinterpret_cast<Il2CppObject*>(keys[en->index]);
}

void il2cpp_dict_si_keycollection_enumerator_dispose(Il2CppObject* self)
{
    (void)self; // no-op (see il2cpp_dict_si_enumerator_dispose).
}

Il2CppObject* il2cpp_dict_si_valuecollection_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureDictEnumRegistered();
    GcRoot<Il2CppObject*> root(self, "il2cpp_dict_si_valuecollection_get_enumerator");
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(
        il2cpp_object_new(&System_Collections_Generic_Dictionary_2_Enumerator_string_int_class));
    self = root.Get();
    en->keys = nullptr;
    en->values = reinterpret_cast<Il2CppArray*>(self);
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->values),
                         reinterpret_cast<Il2CppObject*>(en->values));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

int32_t il2cpp_dict_si_valuecollection_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(self);
    ++en->index;
    return en->index < en->values->length ? 1 : 0;
}

int32_t il2cpp_dict_si_valuecollection_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictSIEnumerator* en = reinterpret_cast<Il2CppDictSIEnumerator*>(self);
    if (en->index < 0 || en->index >= en->values->length)
    {
        il2cpp_throw_invalid_operation_exception("Enumeration has either not started or has already finished.");
    }
    return reinterpret_cast<const int32_t*>(en->values->data)[en->index];
}

void il2cpp_dict_si_valuecollection_enumerator_dispose(Il2CppObject* self)
{
    (void)self; // no-op (see il2cpp_dict_si_enumerator_dispose).
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_collection_get_count -- KeyCollection/ValueCollection/get_
// Count: the receiver is the snapshot ARRAY (the KeyCollection is a view over
// the keys array), so Count == the array length == the dictionary count.
// il2cpp_dict_si_keys_contains -- ICollection<string>.Contains over the keys
// snapshot array: an ordinal scan (equal content == the same key; net48
// KeyCollection<string>.Contains is dictionary-lookup true, the array scan
// returns the identical boolean).
//------------------------------------------------------------------------------
int32_t il2cpp_dict_si_collection_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppArray*>(self)->length;
}

int32_t il2cpp_dict_si_keys_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    const Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(self);
    const Il2CppObject* const* keys =
        reinterpret_cast<const Il2CppObject* const*>(arr->data);
    for (int32_t i = 0; i < arr->length; ++i)
    {
        if (StrOrdinalEqual(reinterpret_cast<const Il2CppString*>(keys[i]),
                            reinterpret_cast<const Il2CppString*>(item)))
        {
            return 1;
        }
    }
    return 0;
}

} // namespace il2cpp
