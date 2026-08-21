//------------------------------------------------------------------------------
// hashset.cpp -- the GC-OBJECT wrapper for HashSet<string> (D-HSET-1, hset-b23;
// see hashset.h for the layout, GC contract and lifetime contract).
//
// The wrapper is a MANAGED object (Il2CppHashSetStringObject) whose slot 1
// holds the raw Il2CppDictSI* (dictsi.h) in a NON-reference field
// (refFieldBitmap 0). HashSet<string> IS a set of unique members; the dictsi
// domain gives ordinal content equality + the growth contract for free. Each
// member is a dictsi key whose value is always the presence marker 1.
//
// Member semantics (net48 HashSet<string> probe, hset-b23):
//   * Add(item)    -> bool: true when the member was ABSENT (inserted with
//     value 1); false when already present (no-op, never throws). net48
//     returns false for a duplicate Add (unlike Dictionary.Add which throws).
//   * Remove(item) -> bool: true when present (unlinked); false otherwise.
//   * Contains(item)-> bool: membership by ordinal content equality.
//   * Count         -> the number of members (dictsi count).
//   * Clear()       -> every member dropped; Count -> 0; reusable.
//   A null item raises ArgumentNullException from add/remove/contains (the
//   dictsi null-key contract, ANE "key" -- message not pinned by the e2e).
//
// GC SAFETY: the wrapper is a normal managed object (its klass header lets
// the collector mark it); the RAW set pointer in slot 1 is never scanned.
// The MEMBERS remain the dictsi.h caller contract -- the caller roots every
// stored member. The e2e (hashsetdeep) uses string LITERALS, interned and
// rooted by the string pool for the process lifetime (see hashset.h).
//------------------------------------------------------------------------------

#include "hashset.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_null_reference_exception

#include <cstddef>
#include <cstdint>

namespace il2cpp {

//------------------------------------------------------------------------------
// System.Collections.Generic.HashSet`1<string> runtime descriptor
// (GLOBAL linkage within namespace il2cpp -- the transpiler emits
// il2cpp_object_new(&System_Collections_Generic_HashSet_1_string_class)
// for the newobj fallback from generated code inside namespace il2cpp, and
// icall_hashset_string_ctor registers it lazily; the same contract as
// System_Collections_Generic_Dictionary_2_string_int_class in dictsi_obj.cpp).
// instanceSize covers the header + the raw set pointer; refFieldBitmap 0
// (slot 1 is a RAW pointer, NOT a reference).
//------------------------------------------------------------------------------
Il2CppClass System_Collections_Generic_HashSet_1_string_class = {
    "System.Collections.Generic.HashSet`1<string>", // name
    static_cast<int32_t>(sizeof(Il2CppHashSetStringObject)), // instanceSize
    0u,                                  // refFieldBitmap: no reference fields
    0,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

namespace {

bool g_hashSetStringRegistered = false;

void EnsureHashSetStringRegistered()
{
    if (g_hashSetStringRegistered) return;
    il2cpp_class_register(&System_Collections_Generic_HashSet_1_string_class);
    g_hashSetStringRegistered = true;
}

// The set pointer from a wrapper receiver.
Il2CppDictSI* AsSet(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppHashSetStringObject*>(self)->set;
}

// A member arriving from generated code is a managed reference
// (Il2CppObject*); the dictsi surface needs Il2CppString* (a null stays null).
Il2CppString* AsKey(Il2CppObject* item)
{
    return reinterpret_cast<Il2CppString*>(item);
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

namespace runtime {

//------------------------------------------------------------------------------
// icall_hashset_string_ctor -- HashSet<string>(capacity) (or the parameterless
// form with capacity 0, appended by the transpiler). Allocates the raw dictsi
// and stores it in the wrapper slot. Also registers the wrapper descriptor
// (idempotent) so class_by_name / conformance scans resolve it.
//------------------------------------------------------------------------------
void icall_hashset_string_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    EnsureHashSetStringRegistered();
    reinterpret_cast<Il2CppHashSetStringObject*>(self)->set = il2cpp_dict_si_create(capacity);
}

//------------------------------------------------------------------------------
// icall_hashset_string_add -- Add(item): true when the member was ABSENT
// (inserted); false when already present. Never throws on a duplicate (net48
// HashSet.Add semantics; a null item raises ArgumentNullException via dictsi).
//------------------------------------------------------------------------------
bool icall_hashset_string_add(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    Il2CppDictSI* set = AsSet(self);
    Il2CppString* key = AsKey(item);
    if (il2cpp_dict_si_contains_key(set, key))
    {
        return false; // already a member: net48 Add returns false, no-op
    }
    il2cpp_dict_si_set(set, key, 1); // insert with the presence marker
    return true;
}

//------------------------------------------------------------------------------
// icall_hashset_string_remove -- Remove(item): true when the member was
// present (unlinked); false otherwise.
//------------------------------------------------------------------------------
bool icall_hashset_string_remove(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_remove(AsSet(self), AsKey(item));
}

//------------------------------------------------------------------------------
// icall_hashset_string_contains -- Contains(item): membership by ordinal
// content equality (two distinct string instances with equal content are the
// SAME member -- the dictsi content-equality contract).
//------------------------------------------------------------------------------
bool icall_hashset_string_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_contains_key(AsSet(self), AsKey(item));
}

//------------------------------------------------------------------------------
// icall_hashset_string_get_count -- Count: the number of members.
//------------------------------------------------------------------------------
int32_t icall_hashset_string_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_get_count(AsSet(self));
}

//------------------------------------------------------------------------------
// icall_hashset_string_clear -- Clear(): count -> 0; the set stays reusable.
//------------------------------------------------------------------------------
void icall_hashset_string_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_si_clear(AsSet(self));
}

}} // namespace il2cpp::runtime
