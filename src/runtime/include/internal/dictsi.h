#pragma once
//------------------------------------------------------------------------------
// dictsi.h -- strongly-typed string->int32 dictionary (System.Collections.
// Generic.Dictionary<string,int> semantics, simplified hash table domain).
//
// DESIGN NOTE (why this is NOT a wrapper over il2cpp_hashtable.h): the
// existing non-generic hashtable is a GC OBJECT-MODEL implementation -- its
// keys and values are Il2CppObject* stored in managed class descriptors with
// write barriers, so an int32 value would have to be BOXED into a managed
// object on every store. A strongly-typed string->int32 dictionary stores
// values inline as plain int32 (no boxing) and is therefore a standalone
// simplified table. It deliberately REUSES the runtime's established string
// hash (icall_string_get_hash_code -- the ordinal content hash, byte-identical
// to hashtable.cpp's HashStringOrdinal), so equal keys hash equal.
//
// Layout (plain C++ heap structure -- NOT a GC object, unlike the hashtable).
// The model MIRRORS net48 Dictionary<TKey,TValue>: a bucket index array plus a
// slot ("entries") array walked in index order for enumeration, with a free
// list so a removed slot is REUSED on re-add (returning to its original
// enumeration position -- net48 semantics, spec COLLECTIONS-WIRING-SPEC section 8):
//
//   struct Il2CppDictSI
//   {
//       int32_t*            buckets;      // bucket -> entry INDEX (-1 = empty)
//       Il2CppDictSIEntry*  entries;      // slot array (entryCapacity slots)
//       int32_t             bucketCount;  // power of two (min 4, cap 2^30)
//       int32_t             count;        // slots ever used (INCLUDES freed)
//       int32_t             entryCapacity;// entries[] length
//       int32_t             freeList;     // head of the free-slot chain (-1)
//       int32_t             freeCount;    // number of freed slots
//   };
//   Active pair count = count - freeCount.
//
//   struct Il2CppDictSIEntry            // one slot; chains via 'next' INDEX
//   {
//       int32_t           hashCode;      // -1 = FREE slot (skipped by enum)
//       int32_t           next;          // chain successor INDEX (-1 at tail)
//       Il2CppString*     key;           // never null while occupied
//       int32_t           value;         // inline int32 (no boxing)
//   };
//
// ENUMERATION ORDER (net48 Dictionary<TKey,TValue>, spec section 8 R1-R7):
//   * R1: insertion order -- entries[0..count) ascending, skipping free slots.
//   * R2: overwrite of an existing key does NOT move it.
//   * R3: remove takes the pair out of enumeration.
//   * R4: re-adding a removed key REUSES its freed slot -> original position.
//   * R7: resize/rehash does NOT reorder entries (only buckets are rebuilt).
//
// HASH CONTRACT (equal keys MUST hash equal -- lookups break otherwise):
//   * Keys are managed strings (Il2CppString*). The hash is the runtime's
//     ordinal string hash (icall_string_get_hash_code / HashStringOrdinal):
//     equal CONTENT -> equal hash, regardless of instance identity.
//   * Equality is also by ordinal CONTENT (length + UTF-16 units), so two
//     distinct string instances with equal content are the SAME key --
//     mirroring Dictionary<string,int>.
//   * Bucket index = (uint32_t)hash & (bucketCount - 1); bucketCount is
//     always a power of two, so the mask is exact (no modulo needed).
//   * The hash is CACHED per slot (entries[i].hashCode, net48 entry.hashCode)
//     -- content hashes are move-stable, so the cache never goes stale.
//
// GC CONTRACT (the caller's duty): the dictionary is a plain C++ structure
// INVISIBLE to the collector -- it does not root its keys. The caller must
// keep every stored key reachable (static root, managed field, frame
// parameter) for as long as it remains in the dictionary; values are int32
// and never involve the collector. get_keys()/get_values() return fresh
// managed arrays that ARE GC objects (the caller roots those if needed).
//
// GROWTH CONTRACT: load factor 0.72 (integer-exact 72/100) on the OCCUPIED
// pair count. Before an insert that would push the occupied count past 72% of
// the bucket count, the bucket array doubles (still a power of two, capped at
// 2^30) and every OCCUPIED slot is rehashed from its CACHED hash; the entries
// array (and its enumeration order) is untouched (R7). The entries array
// grows independently when it is exhausted (append at count).
//
// .NET SEMANTICS BASELINE (matches System.Collections.Generic.Dictionary
// <string,int>):
//   * add(d, null, v) / try_get_value / remove / contains_key with a null
//     key -> ArgumentNullException("key").
//   * add with a duplicate key (per the content-equality contract) ->
//     ArgumentException; the table is left unchanged.
//   * try_get_value: true + *value out-param on hit; false on miss (the
//     out-param is left untouched on a miss).
//   * remove: true when the key was present (unlinked), false otherwise.
//   * create with a negative capacity -> ArgumentOutOfRangeException.
//   * clear: drops every pair; count -> 0; the dictionary stays reusable.
//   * get_keys / get_values: fresh arrays holding the current contents in
//     net48 ENUMERATION ORDER (insertion order; see the ENUMERATION ORDER
//     contract above -- R1-R7).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {

#ifdef IL2CPP_DICT_SI_PROBE
// Test-only instrumentation (compile with -DIL2CPP_DICT_SI_PROBE): the number
// of FindEntryIndex (bucket-chain) lookups performed so far. It backs
// tests/runtime/test_hashset.cpp's "one lookup per Add" assertion -- the
// red/green evidence for the Add double-lookup fix. The macro is OFF in every
// production build, so neither the counter nor the increment exists.
extern int32_t g_dictSiFindEntryCalls;
#endif

// One slot; collision chains are linked through the 'next' INDEX (-1 at
// tail). hashCode == -1 marks a FREE slot (skipped by enumeration).
struct Il2CppDictSIEntry
{
    int32_t           hashCode; // cached hash (-1 = FREE slot)
    int32_t           next;     // chain successor INDEX (-1 at tail)
    Il2CppString*     key;      // the key (never null while occupied)
    int32_t           value;    // the int32 value (stored inline, no boxing)
};

// Strongly-typed string->int32 dictionary (see header comment for layout,
// hash contract, GC contract and growth contract).
struct Il2CppDictSI
{
    int32_t*            buckets;      // bucket -> entry INDEX (-1 = empty)
    Il2CppDictSIEntry*  entries;      // slot array (entryCapacity slots)
    int32_t             bucketCount;  // power of two (min 4, cap 2^30)
    int32_t             count;        // slots ever used (INCLUDES freed)
    int32_t             entryCapacity;// entries[] length
    int32_t             freeList;     // free-slot chain head (-1 = none)
    int32_t             freeCount;    // number of freed slots
};

// Create a dictionary with the smallest power-of-two bucket count >= capacity
// (minimum 4). A negative capacity raises ArgumentOutOfRangeException.
Il2CppDictSI* il2cpp_dict_si_create(int32_t capacity);

// Destroy a dictionary created by il2cpp_dict_si_create: frees every entry
// node, the bucket array and the dictionary itself (1:1 pair of create).
void il2cpp_dict_si_destroy(Il2CppDictSI* d);

// Add(key, value) -- insert a new pair. A null key raises
// ArgumentNullException; a duplicate key (per the content-equality contract)
// raises ArgumentException and leaves the dictionary unchanged. May grow
// (double + rehash) when the 0.72 load factor would be exceeded.
void il2cpp_dict_si_add(Il2CppDictSI* d, Il2CppString* key, int32_t value);

// Set(key, value) -- the indexer SET: insert a new pair, or REPLACE the value
// of an existing key in place (no ArgumentException on a duplicate). A null
// key raises ArgumentNullException. May grow when the 0.72 load factor would
// be exceeded (the insert path; a replace never grows).
void il2cpp_dict_si_set(Il2CppDictSI* d, Il2CppString* key, int32_t value);

// AddUnique(key, value) -- insert-only Add that reports whether it inserted:
// true when the pair was ABSENT (inserted with 'value'), false when the key
// was already present (NO write, no slot move -- the stored value is left
// untouched, R2). A null key raises ArgumentNullException, exactly like
// add/set. This is the HashSet<T>.Add contract (true = added, false = already
// a member, never throws on a duplicate) expressed as ONE hash + ONE chain
// lookup: contains-then-set would repeat both. Growth (0.72 load factor,
// double + rehash) applies only on the insert path.
bool il2cpp_dict_si_add_unique(Il2CppDictSI* d, Il2CppString* key, int32_t value);

// TryGetValue(key, &value): true + the stored value on a hit; false on a
// miss (the out-param is left untouched). Null key -> ArgumentNullException.
bool il2cpp_dict_si_try_get_value(Il2CppDictSI* d, Il2CppString* key, int32_t* value);

// Remove(key): true when the key was present (unlinked), false otherwise. A
// null key raises ArgumentNullException.
bool il2cpp_dict_si_remove(Il2CppDictSI* d, Il2CppString* key);

// Clear(): drop every pair; count -> 0; the dictionary stays reusable.
void il2cpp_dict_si_clear(Il2CppDictSI* d);

// Count: the number of key/value pairs.
int32_t il2cpp_dict_si_get_count(Il2CppDictSI* d);

// ContainsKey(key): true when the key is present. Null key -> ANE.
bool il2cpp_dict_si_contains_key(Il2CppDictSI* d, Il2CppString* key);

// Keys: a fresh reference array (Il2CppString* elements) holding the current
// keys in net48 ENUMERATION ORDER (spec section 8 R1: entries[0..count) ascending,
// skipping free slots). Overwrites keep their position; a removed-then-readded
// key returns to its original position (R4). Empty dictionary -> length-0
// array.
Il2CppArray* il2cpp_dict_si_get_keys(Il2CppDictSI* d);

// Values: a fresh int32 array holding the current values in the same net48
// enumeration order as get_keys (positionally consistent).
// Empty dictionary -> length-0 array.
Il2CppArray* il2cpp_dict_si_get_values(Il2CppDictSI* d);

} // namespace il2cpp
