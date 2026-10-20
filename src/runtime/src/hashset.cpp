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

#include "dictsi_obj.h"        // il2cpp_dict_si_keycollection_* (G4 enumeration)
#include "il2cpp_boxing_ext.h" // runtime::icall_unbox_int32/icall_box_double
#include "il2cpp_boxing_ext2.h" // runtime::icall_box_long/icall_box_uint
#include "il2cpp_exception_ext.h" // il2cpp_throw_null_reference_exception
#include "il2cpp_gcroot.h"     // GcRoot
#include "il2cpp_hashtable.h"  // the HashSet<int> backing store

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
// ONE hash + ONE chain lookup (dict_si_add_unique): net48 HashSet.Add is a
// single probe, so the old contains-then-set pair double-hashed and
// double-walked every call.
//------------------------------------------------------------------------------
bool icall_hashset_string_add(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_si_add_unique(AsSet(self), AsKey(item), 1); // insert with the presence marker
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

//------------------------------------------------------------------------------
// G4: HashSet<int> -- boxed-int members over the non-generic hashtable
// object model (il2cpp_hashtable.h). Every boxing allocation can compact,
// so the receiver is re-read from its GcRoot before each hashtable call;
// the hashtable entry itself roots the stored box (no caller contract).
//------------------------------------------------------------------------------
void icall_hashset_int_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_ctor(self, capacity);
}

bool icall_hashset_int_add(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_int_add_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_int_add_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (icall_hashtable_contains_key(self, boxed))
    {
        return false; // already a member: net48 Add returns false, no-op
    }
    icall_hashtable_set(self, boxed, nullptr); // presence entry (null value is legal)
    return true;
}

bool icall_hashset_int_remove(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_int_remove_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_int_remove_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (!icall_hashtable_contains_key(self, boxed))
    {
        return false;
    }
    icall_hashtable_remove(self, boxed);
    return true;
}

bool icall_hashset_int_contains(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_int_contains_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, item);
    self = selfRoot.Get();
    return icall_hashtable_contains_key(self, boxed);
}

int32_t icall_hashset_int_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_hashtable_get_count(self);
}

void icall_hashset_int_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_clear(self);
}

// The element get_Current: the keycollection wrapper walks the boxed-int
// snapshot, so the element is a boxed System.Int32 -- unbox it.
int32_t icall_hashset_int_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_int32(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W3: HashSet<long> -- boxed-int64 members over the hashtable (value hash +
// value equality now cover System.Int64 -- see hashtable.cpp).
//------------------------------------------------------------------------------
void icall_hashset_long_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_ctor(self, capacity);
}

bool icall_hashset_long_add(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_long_add_self");
    Il2CppObject* boxed = icall_box_long(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_long_add_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (icall_hashtable_contains_key(self, boxed))
    {
        return false;
    }
    icall_hashtable_set(self, boxed, nullptr);
    return true;
}

bool icall_hashset_long_remove(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_long_remove_self");
    Il2CppObject* boxed = icall_box_long(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_long_remove_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (!icall_hashtable_contains_key(self, boxed))
    {
        return false;
    }
    icall_hashtable_remove(self, boxed);
    return true;
}

bool icall_hashset_long_contains(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_long_contains_self");
    Il2CppObject* boxed = icall_box_long(item);
    self = selfRoot.Get();
    return icall_hashtable_contains_key(self, boxed);
}

int32_t icall_hashset_long_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_hashtable_get_count(self);
}

void icall_hashset_long_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_clear(self);
}

int64_t icall_hashset_long_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W3: HashSet<double> -- boxed-double members (System.Double value hash).
// NaN equality follows Double.Equals (NaN == NaN) via KeyEquals; hashing keeps
// the net48 raw-bits quirk (see hashtable.cpp BoxedDoubleHash).
//------------------------------------------------------------------------------
void icall_hashset_double_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_ctor(self, capacity);
}

bool icall_hashset_double_add(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_double_add_self");
    Il2CppObject* boxed = icall_box_double(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_double_add_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (icall_hashtable_contains_key(self, boxed))
    {
        return false;
    }
    icall_hashtable_set(self, boxed, nullptr);
    return true;
}

bool icall_hashset_double_remove(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_double_remove_self");
    Il2CppObject* boxed = icall_box_double(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_double_remove_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (!icall_hashtable_contains_key(self, boxed))
    {
        return false;
    }
    icall_hashtable_remove(self, boxed);
    return true;
}

bool icall_hashset_double_contains(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_double_contains_self");
    Il2CppObject* boxed = icall_box_double(item);
    self = selfRoot.Get();
    return icall_hashtable_contains_key(self, boxed);
}

int32_t icall_hashset_double_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_hashtable_get_count(self);
}

void icall_hashset_double_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_clear(self);
}

double icall_hashset_double_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W3: HashSet<uint> -- the uint bit pattern rides the int32 box slot
// (boxingext2: full 32-bit pattern, unsigned interpretation at the call site).
//------------------------------------------------------------------------------
void icall_hashset_uint_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_ctor(self, capacity);
}

bool icall_hashset_uint_add(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_uint_add_self");
    Il2CppObject* boxed = icall_box_uint(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_uint_add_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (icall_hashtable_contains_key(self, boxed))
    {
        return false;
    }
    icall_hashtable_set(self, boxed, nullptr);
    return true;
}

bool icall_hashset_uint_remove(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_uint_remove_self");
    Il2CppObject* boxed = icall_box_uint(item);
    GcRoot<Il2CppObject*> itemRoot(boxed, "hashset_uint_remove_item");
    self = selfRoot.Get();
    boxed = itemRoot.Get();
    if (!icall_hashtable_contains_key(self, boxed))
    {
        return false;
    }
    icall_hashtable_remove(self, boxed);
    return true;
}

bool icall_hashset_uint_contains(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "hashset_uint_contains_self");
    Il2CppObject* boxed = icall_box_uint(item);
    self = selfRoot.Get();
    return icall_hashtable_contains_key(self, boxed);
}

int32_t icall_hashset_uint_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_hashtable_get_count(self);
}

void icall_hashset_uint_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_clear(self);
}

int32_t icall_hashset_uint_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_uint(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// T2 W3: HashSet<object/class> reference family -- the reference is stored
// directly as the hashtable key (address hash + reference equality; see
// hashtable.h: every other key -> address hash). Null members are rejected
// to match net48 (ArgumentNullException via the hashtable null-key contract).
//------------------------------------------------------------------------------
void icall_hashset_ref_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_ctor(self, capacity);
}

bool icall_hashset_ref_add(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (icall_hashtable_contains_key(self, item))
    {
        return false;
    }
    icall_hashtable_set(self, item, nullptr);
    return true;
}

bool icall_hashset_ref_remove(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (!icall_hashtable_contains_key(self, item))
    {
        return false;
    }
    icall_hashtable_remove(self, item);
    return true;
}

bool icall_hashset_ref_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    return icall_hashtable_contains_key(self, item);
}

int32_t icall_hashset_ref_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_hashtable_get_count(self);
}

void icall_hashset_ref_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    icall_hashtable_clear(self);
}

} // namespace runtime

// The enumeration surface (namespace il2cpp): wrap the boxed-int KEYS
// snapshot in the dictsi keycollection enumerator (MoveNext/Dispose reuse
// that domain's icalls through the resolver).
Il2CppObject* il2cpp_hashset_int_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snap = runtime::icall_hashtable_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

Il2CppObject* il2cpp_hashset_long_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snap = runtime::icall_hashtable_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

Il2CppObject* il2cpp_hashset_double_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snap = runtime::icall_hashtable_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

Il2CppObject* il2cpp_hashset_uint_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snap = runtime::icall_hashtable_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

Il2CppObject* il2cpp_hashset_ref_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snap = runtime::icall_hashtable_get_keys(self);
    return il2cpp_dict_si_keycollection_get_enumerator(snap);
}

// G4 B2 (b2b8): the HashSet<string> enumeration surface -- wrap the string
// KEYS snapshot (dictsi) in the keycollection enumerator. The snapshot array
// is rooted across the wrapping allocation (the int path above passes it
// straight through; the root here closes the GC window).
Il2CppObject* il2cpp_hashset_string_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* snapshot =
        reinterpret_cast<Il2CppObject*>(il2cpp_dict_si_get_keys(AsSet(self)));
    GcRoot<Il2CppObject*> snapRoot(snapshot, "hashset_string_get_enumerator_keys");
    return il2cpp_dict_si_keycollection_get_enumerator(snapRoot.Get());
}

} // namespace il2cpp
