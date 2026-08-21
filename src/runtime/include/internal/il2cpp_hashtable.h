#pragma once
//------------------------------------------------------------------------------
// il2cpp_hashtable.h -- managed non-generic hashtable (System.Collections.
// Hashtable semantics, M4 icall domain).
//
// Layout (aligned with the object-model convention; every field below is a
// GC-scanned slot in the class descriptor's refFieldBitmap):
//
//   struct Il2CppHashtable
//   {
//       Il2CppObject obj;      // object header: klass pointer
//       Il2CppArray* buckets;  // reference array (Il2CppObject* elements):
//                              //   bucket i = head of the i-th collision chain
//       int32_t      count;    // number of key/value pairs (NOT a reference)
//   };
//
//   struct Il2CppHashEntry          // one key/value pair (chain node)
//   {
//       Il2CppObject  obj;    // object header
//       Il2CppObject* key;    // the key        (GC reference)
//       Il2CppObject* value;  // the value      (GC reference; null is legal)
//       Il2CppObject* next;   // chain successor (GC reference)
//   };
//
//   GC SAFETY (refFieldBitmap contract):
//     * Il2CppHashtable  -- instanceSize 24, refFieldBitmap bit 1 (slot 1 =
//       the buckets array; the int32 count slot is NOT a reference). The
//       collector therefore keeps the bucket array alive whenever the
//       hashtable is reachable.
//     * Il2CppHashEntry   -- instanceSize 32, refFieldBitmap bits 1,2,3
//       (slots 1..3 = key, value, next). Every key, every value (including
//       null values) and every chain link is scanned by the collector.
//     * The buckets array is a REFERENCE array (il2cpp_array_new with
//       isRefElement == true): the collector scans each element as an
//       Il2CppObject* (see gc.cpp ScanObject), so the chain heads are kept
//       alive through the array. Keys and values are reachable transitively
//       through the chain nodes, so a rooted hashtable keeps its whole
//       contents alive -- verified by the unit tests' collect-and-query
//       section.
//
// HASH CONTRACT (the exact rules hashtable.cpp implements; equal keys MUST
// produce equal hashes, or lookups break):
//   * System.String keys (klass name "System.String")   -- ordinal string
//     hash, a simplified .NET Framework string.GetHashCode (unrandomized,
//     deterministic for AOT; see hashtable.cpp). Equal content -> equal hash.
//   * boxed PRIMITIVE keys (D-HT-1, klass names "System.Int32" /
//     "System.Double" / "System.Char" / "System.Boolean" / "System.Single")
//     -- the boxed VALUE's hash, mirroring the net48 value-type GetHashCode
//     (int/char/bool: the value; double: 0 for +-0.0 else (int)bits ^
//     (int)(bits >> 32); single: the 32-bit float bits, 0 for +-0.0f -- the
//     box stores the float as a double, see boxingext2.cpp). Equal values ->
//     equal hash.
//   * every other key -- ADDRESS hash (derived from the object pointer,
//     shifted past the alignment bits so bucket indices use well-distributed
//     higher bits). Reference equality -> equal hash.
//   All hashes are DERIVED FROM THE LIVE KEY at every use (lookup, insert,
//   remove, resize) -- nothing is cached, so no stale-hash state exists.
//   Bucket index = (uint32_t)hash & (bucketCount - 1): bucketCount is always
//   a power of two, so the mask is exact (no modulo needed).
//
//   COMPACTION NOTE (why there is no hash cache): the M3 collector COMPACTS
//   Gen0 -- surviving young objects are copied to a Gen1 segment and every
//   reference is rewritten (see gc.cpp Collect / UpdateReferencesAfterCompaction).
//   String and boxed-primitive keys are unaffected (content/value hashes are
//   move-stable). An ADDRESS-hashed key, however, hashes differently after a
//   move, so its entry can sit in a STALE bucket. Lookups self-heal: a miss
//   in the fresh bucket triggers a full-table scan for the key, and a hit is
//   RELINKED into the correct bucket (heal-on-access; the scan only runs for
//   non-string, non-boxed-primitive keys in tables that have survived a
//   collection -- stable-hash keys are never scanned, so absent-key lookups
//   stay O(1) for them, and healed address keys become O(1) again after one
//   scan).
//
// GROWTH CONTRACT: the load factor is 0.72 (72/100); before an insert that
// would push count past 72% of the bucket count, the table doubles its
// bucket array (still a power of two) and REHASHES every entry from its
// live key pointer (fresh derivation -- always correct, even right after a
// compacting collection). Entry nodes are reused across resizes (only the
// array is rebuilt), so resize is allocation-light.
//
// .NET SEMANTICS BASELINE (matches System.Collections.Hashtable on .NET
// Framework 4.8):
//   * Add(self, null, v) / indexer / ContainsKey / Remove with a null key
//     -> ArgumentNullException("key").
//   * Add with a duplicate key -> ArgumentException (indexer SET replaces
//     instead -- set never throws on an existing key).
//   * Null VALUES are legal everywhere (Hashtable, unlike Dictionary, allows
//     null values); get() returns null for both "missing" and "stored null",
//     exactly like the .NET indexer.
//   * Remove of a missing key -> silent success; the indexer get of a
//     missing key -> null (no exception).
//   * ctor with a negative capacity -> ArgumentOutOfRangeException.
//   * Keys / Values return a SNAPSHOT array (not .NET's live ICollection
//     view): the key (resp. value) of every entry, in the table's
//     enumeration order. The order is unspecified but STABLE (bucket-index
//     ascending, then chain head -> tail), and it is IDENTICAL for Keys and
//     Values -- Keys[i] and Values[i] always describe the same entry,
//     matching the .NET guarantee that the two collections enumerate in the
//     same order. A snapshot does not reflect later mutations (a table
//     changed after the call keeps the old snapshot).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Managed non-generic hashtable (see header comment for layout + GC bitmap).
struct Il2CppHashtable
{
    Il2CppObject obj;      // object header (klass pointer)
    Il2CppArray* buckets;  // reference array: bucket i = head of chain i
    int32_t      count;    // number of key/value pairs
};

// One key/value pair; collision chains are singly linked through 'next'.
struct Il2CppHashEntry
{
    Il2CppObject  obj;   // object header (klass pointer)
    Il2CppObject* key;   // the key (GC reference, never null)
    Il2CppObject* value; // the value (GC reference; null is legal)
    Il2CppObject* next;  // chain successor (GC reference, null at tail)
};

// The System.Collections.Hashtable runtime descriptor (registered lazily on
// first use; instanceSize 24, refFieldBitmap bit 1 -- see header comment).
Il2CppClass* il2cpp_hashtable_class();

namespace runtime {

// Hashtable(int capacity) -- allocate the bucket array. The bucket count is
// the smallest power of two >= capacity (minimum 4); a negative capacity
// raises ArgumentOutOfRangeException. 'self' must be a fresh object created
// with il2cpp_object_new(il2cpp_hashtable_class()).
void icall_hashtable_ctor(Il2CppObject* self, int32_t capacity);

// Add(key, value) -- insert a new pair. A null key raises
// ArgumentNullException; a duplicate key (per the hash/equality contract)
// raises ArgumentException and leaves the table unchanged. Null values are
// legal. May grow (double + rehash) when the load factor 0.72 is exceeded.
void icall_hashtable_add(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value);

// Indexer SET: replace the value of an existing key, or insert when absent.
// A null key raises ArgumentNullException. Never throws for a missing key.
void icall_hashtable_set(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value);

// Indexer GET: the stored value, or null when the key is absent (a stored
// null value also yields null -- indistinguishable, exactly like .NET). A
// null key raises ArgumentNullException.
Il2CppObject* icall_hashtable_get(Il2CppObject* self, Il2CppObject* key);

// ContainsKey(key): true when the key is present. Null key -> ANE.
bool icall_hashtable_contains_key(Il2CppObject* self, Il2CppObject* key);

// Remove(key): unlink the pair; a missing key is a silent success. Null key
// -> ArgumentNullException.
void icall_hashtable_remove(Il2CppObject* self, Il2CppObject* key);

// Count: the number of key/value pairs.
int32_t icall_hashtable_get_count(Il2CppObject* self);

// Keys: a snapshot of every key, in the table's enumeration order (see the
// .NET SEMANTICS BASELINE note). The order is unspecified but stable and
// IDENTICAL to icall_hashtable_get_values, so Keys[i] and Values[i] always
// describe the same entry. The result is a reference array (Il2CppArray*).
Il2CppObject* icall_hashtable_get_keys(Il2CppObject* self);

// Values: a snapshot of every value (stored nulls included) in the SAME
// order as Keys (see above).
Il2CppObject* icall_hashtable_get_values(Il2CppObject* self);

// Clear(): drop every pair (all buckets become null; the entries become
// unreachable and are reclaimed by the next collection). Count -> 0.
void icall_hashtable_clear(Il2CppObject* self);

} // namespace runtime

} // namespace il2cpp
