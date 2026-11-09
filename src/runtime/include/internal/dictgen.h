#pragma once
//-----------------------------------------------------------------------------
// dictgen.h -- the SHARED generic Dictionary`2 storage + kind-descriptor
// registry (Phase-1: GENERIC-DESCRIPTOR-DESIGN.md). ONE ordered object-model
// table serves every key/value kind combination; kind functions (hash /
// equals / box / unbox / carrier) come from the klass descriptor registry,
// not from per-instantiation icall families.
//
// Phase-1 kinds: 0 = string (reference passthrough), 1 = int32 (boxed).
// Uri normalization and further kinds ride the same registry (Phase-2).
//
// Layout (insertion-ordered; net48 Dictionary<TKey,TValue> model, spec section 8
// R1-R7 -- the same discipline as Il2CppDictSI, but every pair is a pair of
// MANAGED carriers so the collector roots the whole contents):
//
//   struct Il2CppDictGen
//   {
//       int32_t*           buckets;      // bucket -> entry INDEX (-1 = empty)
//       Il2CppDictGenEntry* entries;     // slot array (entryCapacity slots)
//       int32_t            bucketCount;  // power of two (min 4, cap 2^30)
//       int32_t            count;        // slots ever used (INCLUDES freed)
//       int32_t            entryCapacity;// entries[] length
//       int32_t            freeList;     // free-slot chain head (-1 = none)
//       int32_t            freeCount;    // number of freed slots
//       int32_t            seqHead;      // insertion-sequence head (-1 = empty)
//       int32_t            seqTail;      // insertion-sequence tail
//   };
//   Active pair count = count - freeCount.
//
//   struct Il2CppDictGenEntry
//   {
//       int32_t       hashCode;  // -1 = FREE slot (skipped by enumeration)
//       int32_t       next;      // bucket-chain successor INDEX (-1 at tail)
//       int32_t       seqNext;   // insertion-sequence successor (-1 at tail)
//       Il2CppObject* key;       // managed carrier (boxed value or reference)
//       Il2CppObject* value;     // managed carrier (boxed value or reference)
//   };
//
// ENUMERATION ORDER (R1-R7): seqHead -> seqNext ascending, skipping free
// slots. R2: overwrite keeps its slot AND its sequence link. R3: remove frees
// the slot but leaves the sequence link (skipped). R4: re-add reuses the freed
// slot -> original sequence position. R7: resize rebuilds buckets only.
//
// HASH CONTRACT: hashes are masked non-negative (the -1 free marker is
// reserved). string = ordinal content hash; int32 = the value; unknown kinds
// degrade to the address-hash fallback (the hashtable "every other key"
// rule -- no silent wrong-bucket, equality still decides).
//
// GC CONTRACT: int32 keys live INLINE in the entries (no collector
// involvement -- the table never holds an unrooted box). Reference keys and
// values are managed carriers: the caller must keep them reachable (static
// root, managed field, frame parameter) for as long as they remain in the
// table. get_keys()/get_values() return fresh managed arrays that ARE GC
// objects (the caller roots those if needed).
//-----------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {

// Phase-1 kind codes (ride the gen icalls as trailing int32 literals).
constexpr int32_t kGenKind_String = 0; // reference passthrough
constexpr int32_t kGenKind_Int32 = 1;  // boxed int32
// b5-gaps section 102: the int64 key carrier (the long key does not fit the
// int32 keyInt slot; it rides a parallel int64 array in the lookup table).
// Distinct from kGenKind_Uri (which is normalized to the string kind on entry).
constexpr int32_t kGenKind_Int64 = 16;
// Phase-2: Uri keys normalize to their string form at the twin entry, then
// ride the string-kind table path (the T4 uristr contract, no twin family).
constexpr int32_t kGenKind_Uri = 2; // normalized to string on entry
// Value-type tuple key: the key carrier is a BOXED ValueTuple (a GC object
// whose payload holds the struct bytes; klass name "System.ValueTuple`<kinds>"
// -- see il2cpp_valuetuple.h). The hash/equals are the structural net48
// ValueTuple semantics (EqualityComparer<ValueTuple<...>>.Default).
constexpr int32_t kGenKind_ValueTuple = 3;

// One slot; collision chains link through 'next', the insertion sequence
// through 'seqNext'. hashCode == -1 marks a FREE slot (skipped by
// enumeration). int32 keys are stored INLINE (no boxing, no GC lifetime);
// reference keys/values are managed carriers (caller-rooted, the dictsi
// contract -- literals interned for the process lifetime).
struct Il2CppDictGenEntry
{
    int32_t       hashCode; // cached hash (-1 = FREE slot)
    int32_t       next;     // bucket-chain successor INDEX (-1 at tail)
    int32_t       seqNext;  // insertion-sequence successor (-1 at tail)
    int32_t       keyInt;   // inline int32 key (kGenKind_Int32)
    Il2CppObject* keyRef;   // reference key (other kinds; null for int32)
    Il2CppObject* value;    // managed carrier (null is legal)
};

// Ordered object-model dictionary (see header comment for layout, hash
// contract and GC contract).
struct Il2CppDictGen
{
    int32_t*            buckets;      // bucket -> entry INDEX (-1 = empty)
    Il2CppDictGenEntry* entries;      // slot array (entryCapacity slots)
    int32_t             bucketCount;  // power of two (min 4, cap 2^30)
    int32_t             count;        // slots ever used (INCLUDES freed)
    int32_t             entryCapacity;// entries[] length
    int32_t             freeList;     // free-slot chain head (-1 = none)
    int32_t             freeCount;    // number of freed slots
    int32_t             seqHead;      // insertion-sequence head (-1 = empty)
    int32_t             seqTail;      // insertion-sequence tail
};

// Descriptor registry: kind -> carrier/hashing/equality behavior. Phase-1
// covers string + int32; further kinds extend this switch (one line each),
// never a new icall family.
bool il2cpp_dict_gen_kind_is_ref(int32_t kind);
int32_t il2cpp_dict_gen_hash_carrier(const Il2CppObject* carrier, int32_t kind);
bool il2cpp_dict_gen_carrier_equals(const Il2CppObject* a, const Il2CppObject* b, int32_t kind);
Il2CppObject* il2cpp_dict_gen_box_int32(int32_t value);
int32_t il2cpp_dict_gen_unbox_int32(const Il2CppObject* boxed);

// Create / destroy (1:1 paired). A negative capacity raises
// ArgumentOutOfRangeException.
Il2CppDictGen* il2cpp_dict_gen_create(int32_t capacity);
void il2cpp_dict_gen_destroy(Il2CppDictGen* d);

// Add / set / try_get / contains / remove / clear / get_count. int32 keys
// arrive unboxed and are boxed inside (keyKind selects the carrier rule).
// Duplicate add -> ArgumentException, table unchanged. get-miss and remove
// follow the Dictionary (not Hashtable) contract: the raw try_get reports
// hit/miss; remove returns whether the key was present.
void il2cpp_dict_gen_add(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    Il2CppObject* value, int32_t keyKind, int32_t valueKind);
void il2cpp_dict_gen_set(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    Il2CppObject* value, int32_t keyKind, int32_t valueKind);
bool il2cpp_dict_gen_try_get_value(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    Il2CppObject** value, int32_t keyKind, int32_t valueKind);
bool il2cpp_dict_gen_contains_key(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    int32_t keyKind, int32_t valueKind);
bool il2cpp_dict_gen_remove(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    int32_t keyKind, int32_t valueKind);
void il2cpp_dict_gen_clear(Il2CppDictGen* d);
int32_t il2cpp_dict_gen_get_count(Il2CppDictGen* d);

// Snapshots in enumeration order (fresh managed arrays): keys unboxed to
// int32 (keyKind Int32) or passed as references; values always references.
Il2CppArray* il2cpp_dict_gen_get_keys(Il2CppDictGen* d, int32_t keyKind);
Il2CppArray* il2cpp_dict_gen_get_values(Il2CppDictGen* d);

//-----------------------------------------------------------------------------
// Wrapper object + enumeration objects (Phase-1 int_string shape). The wrapper
// is ONE shared class for every future kind combination (it holds only the
// raw table pointer; kinds ride the icalls as trailing literals).
//-----------------------------------------------------------------------------
struct Il2CppDictGenObject
{
    Il2CppObject   obj;      // slot 0: object header
    Il2CppDictGen* dict;     // slot 1: raw pointer (NOT in the refFieldBitmap)
    // b5-gaps section 78: LINQ ToDictionary REF-KEY ROOTING. The raw dict
    // entries hold Il2CppObject* carriers the GC cannot see (the table is a
    // raw allocation, refFieldBitmap bit 1 is OFF). A delegate-produced key /
    // value (a selector result, NOT an interned literal) would dangle. The
    // section-78 kernel therefore stores every ref key + value into this
    // managed `object[]` EXTENSION slot (refFieldBitmap bit 2), so the
    // dictionary object itself roots them for its whole lifetime. The
    // instance path (new Dictionary...) leaves it null -- its keys are
    // literals / caller-rooted carriers (the dictgen GC contract), so no root
    // pack is needed there.
    Il2CppArray*   keysRoot; // slot 2: managed root pack (refFieldBitmap bit 2)
};

struct Il2CppDictGenEnumerator
{
    Il2CppObject  obj;    // slot 0: object header
    Il2CppArray*  keys;   // slot 1: keys snapshot (GC ref, bit 1)
    Il2CppArray*  values; // slot 2: values snapshot (GC ref, bit 2)
    int32_t       index;  // snapshot cursor (-1 = before the first pair)
};

struct Il2CppDictKvpGenIS
{
    Il2CppObject   obj;   // slot 0: object header
    int32_t        key;   // inline int32 key (no boxing)
    Il2CppString*  value; // slot 2: the pair's value (GC ref, bit 2)
};

extern Il2CppClass System_Collections_Generic_Dictionary_2_gen_class;
extern Il2CppClass System_Collections_Generic_Dictionary_2_Enumerator_gen_int_string_class;
extern Il2CppClass System_Collections_Generic_KeyValuePair_2_gen_int_string_class;

Il2CppClass* il2cpp_dict_gen_object_class();
Il2CppClass* il2cpp_dict_gen_enumerator_class();
Il2CppClass* il2cpp_kvp_gen_is_class();

Il2CppObject* il2cpp_dict_gen_get_enumerator(Il2CppObject* self);
int32_t il2cpp_dict_gen_enumerator_move_next(Il2CppObject* self);
Il2CppObject* il2cpp_dict_gen_enumerator_get_current(Il2CppObject* self);
void il2cpp_dict_gen_enumerator_dispose(Il2CppObject* self);
int32_t il2cpp_kvp_gen_int_string_get_key(Il2CppObject* self);
Il2CppObject* il2cpp_kvp_gen_int_string_get_value(Il2CppObject* self);

namespace runtime {

// .ctor(capacity) -- allocate the table and store it in the wrapper slot.
void icall_dict_gen_ctor(Il2CppObject* self, int32_t capacity);

// Add / set_Item (insert-or-replace) / get_Item (KNF on miss) /
// TryGetValue / ContainsKey / Remove / get_Count / Clear. int32 keys arrive
// unboxed ("i" cast) and are boxed inside; kinds ride trailing literals.
void icall_dict_gen_add(Il2CppObject* self, int32_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind);
void icall_dict_gen_set(Il2CppObject* self, int32_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind);
Il2CppObject* icall_dict_gen_get(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind);
bool icall_dict_gen_try_get_value(Il2CppObject* self, int32_t key, Il2CppObject** value,
    int32_t keyKind, int32_t valueKind);
bool icall_dict_gen_contains_key(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind);
bool icall_dict_gen_remove(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind);
int32_t icall_dict_gen_get_count(Il2CppObject* self);
void icall_dict_gen_clear(Il2CppObject* self);

// Phase-2 ref-key twins (ONE twin set for every reference-key pair --
// string, Uri-normalized, future object keys -- sharing the raw table fns
// above; NOT a per-pair family). int32 keys keep the int-slot icalls above.
void icall_dict_gen_add_o(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind);
void icall_dict_gen_set_o(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind);
Il2CppObject* icall_dict_gen_get_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind);
bool icall_dict_gen_try_get_value_o(Il2CppObject* self, Il2CppObject* key, Il2CppObject** value,
    int32_t keyKind, int32_t valueKind);
bool icall_dict_gen_contains_key_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind);
bool icall_dict_gen_remove_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind);
Il2CppArray* icall_dict_gen_get_keys_o(Il2CppObject* self, int32_t keyKind);
Il2CppArray* icall_dict_gen_get_values_o(Il2CppObject* self);

} // namespace runtime

} // namespace il2cpp
