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
// Layout (plain C++ heap structure -- NOT a GC object, unlike the hashtable):
//
//   struct Il2CppDictSI
//   {
//       Il2CppDictSIEntry** buckets;   // heap array of chain heads (len
//                                      //   bucketCount, zero-initialized)
//       int32_t             bucketCount;// always a power of two
//       int32_t             count;      // number of key/value pairs
//   };
//
//   struct Il2CppDictSIEntry            // one pair; collision chains are
//   {                                   // singly linked through 'next'
//       Il2CppString*     key;          // never null
//       int32_t           value;        // inline int32 (no boxing)
//       Il2CppDictSIEntry* next;        // chain successor (null at tail)
//   };
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
//   * Hashes are derived from the LIVE key at every use (lookup, insert,
//     remove, resize) -- nothing is cached, so no stale-hash state exists.
//
// GC CONTRACT (the caller's duty): the dictionary is a plain C++ structure
// INVISIBLE to the collector -- it does not root its keys. The caller must
// keep every stored key reachable (static root, managed field, frame
// parameter) for as long as it remains in the dictionary; values are int32
// and never involve the collector. get_keys()/get_values() return fresh
// managed arrays that ARE GC objects (the caller roots those if needed).
//
// GROWTH CONTRACT: load factor 0.72 (integer-exact 72/100), same as the
// non-generic hashtable. Before an insert that would push count past 72% of
// the bucket count, the bucket array doubles (still a power of two, capped
// at 2^30) and every entry is rehashed from its live key. Entry nodes are
// reused across resizes (only the array is rebuilt).
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
//   * get_keys / get_values: fresh arrays holding the current contents. The
//     enumeration order is the runtime's DETERMINISTIC hash-bucket order:
//     buckets ascending (0..bucketCount-1), and within a bucket the chain
//     head-first (most recently added first). .NET makes NO enumeration-order
//     guarantee (net48 Dictionary<string,int> walks its internal entry slots,
//     roughly insertion order), so this deterministic order is net48-
//     compliant; consumers must not rely on any specific order.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {

// One key/value pair; collision chains are singly linked through 'next'.
struct Il2CppDictSIEntry
{
    Il2CppString*     key;   // the key (never null)
    int32_t           value; // the int32 value (stored inline, no boxing)
    Il2CppDictSIEntry* next; // chain successor (null at tail)
};

// Strongly-typed string->int32 dictionary (see header comment for layout,
// hash contract, GC contract and growth contract).
struct Il2CppDictSI
{
    Il2CppDictSIEntry** buckets;    // heap array of chain heads
    int32_t             bucketCount;// power of two (min 4, cap 2^30)
    int32_t             count;      // number of key/value pairs
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
// keys in the deterministic hash-bucket order (buckets ascending 0..
// bucketCount-1; within a bucket, chain head-first -- most recently added
// first). No .NET order guarantee, but the order is a pure function of the
// table state. Empty dictionary -> length-0 array.
Il2CppArray* il2cpp_dict_si_get_keys(Il2CppDictSI* d);

// Values: a fresh int32 array holding the current values in the same
// deterministic hash-bucket order as get_keys (positionally consistent).
// Empty dictionary -> length-0 array.
Il2CppArray* il2cpp_dict_si_get_values(Il2CppDictSI* d);

} // namespace il2cpp
