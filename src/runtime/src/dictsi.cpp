//------------------------------------------------------------------------------
// dictsi.cpp -- strongly-typed string->int32 dictionary (see dictsi.h for
// the layout, hash contract, GC contract and growth contract).
//
// Data structure (net48 Dictionary<TKey,TValue> model, spec section 8 R1-R7):
//   Il2CppDictSI { buckets: int32* (bucket -> entry index, -1 = empty),
//   entries: Il2CppDictSIEntry* (slot array in INSERTION ORDER), bucketCount
//   (power of two), count (slots ever used), entryCapacity, freeList (head of
//   the freed-slot chain), freeCount }
//   A bucket chain is a linked list of entry INDICES through 'next' (-1 at
//   tail). A free slot has hashCode == -1 and is skipped by enumeration; the
//   free list is threaded through 'next'.
//
// ENUMERATION ORDER (R1-R7): entries[0..count) ascending, skipping free slots.
//   R2: overwriting an existing key keeps its slot. R3: remove frees the slot.
//   R4: re-adding a removed key reuses the freed slot (free-list LIFO), so it
//   returns to its ORIGINAL position -- never appended. R7: a resize rebuilds
//   only the bucket array; the entries array (and its order) is untouched.
//
// Hash contract (implemented here, documented in the header):
//   * Keys are managed strings (Il2CppString*). The hash is the runtime's
//     ordinal string hash (icall_string_get_hash_code -- byte-identical to
//     hashtable.cpp's HashStringOrdinal, see valuesemantics.cpp), so equal
//     CONTENT -> equal hash, regardless of instance identity.
//   * Equality is also by ordinal content (length + UTF-16 units), so two
//     distinct string instances with equal content are the SAME key.
//   Hashes are cached in the slot (net48 entry.hashCode), so a lookup never
//   recomputes the key hash; string hashes are content-based and move-stable,
//   so the cache never goes stale.
//
// Growth: load factor 0.72 (integer-exact 72/100), same as the non-generic
// hashtable. Before an insert that would exceed it, the bucket array doubles
// (power of two, capped at 2^30) and every OCCUPIED slot is rehashed from its
// cached hashCode into the new array. Slots are reused across resizes (only
// the bucket array is rebuilt); the entries array grows only when a new slot
// is needed.
//
// Memory contract: plain C++ heap ownership (new/delete), 1:1 paired with
// il2cpp_dict_si_create / il2cpp_dict_si_destroy -- no GC involvement.
// Keys are NOT rooted (the caller must keep them reachable; see the header's
// GC CONTRACT); values are inline int32 and never involve the collector.
// get_keys()/get_values() allocate fresh managed arrays (GC objects).
//
// Exception contract: null keys raise ArgumentNullException("key"); a
// duplicate add raises ArgumentException; a negative create capacity raises
// ArgumentOutOfRangeException -- all through the il2cpp_exception_ext.h
// factories (chains carry type + base classes, so generated catch clauses
// follow .NET inheritance semantics).
//------------------------------------------------------------------------------

#include "dictsi.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_metadata.h"
#include "il2cpp_value_semantics.h"

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

// Minimum bucket count: avoids a degenerate single-bucket table for tiny
// capacities (a 1-bucket table would resize on the very first insert).
constexpr int32_t kMinBuckets = 4;

// Doubling cap: keeps the power-of-two loop inside int32 (no overflow) and
// the bucket-index mask well below 2^31.
constexpr int32_t kMaxBucketCount = 1 << 30;

// Initial slot capacity (net48 uses a small prime-ish start; any small
// positive value works -- the array doubles as needed).
constexpr int32_t kInitialEntries = 4;

// A free slot marker: entry.hashCode < 0 means the slot holds no pair.
constexpr int32_t kFreeHashCode = -1;

// Bucket index from a hash: bucketCount is always a power of two, so the
// exact mask is (bucketCount - 1) applied in unsigned arithmetic (a negative
// hash wraps to a well-defined unsigned pattern, e.g. -1 -> all-ones).
int32_t BucketIndex(int32_t hash, int32_t bucketCount)
{
    return static_cast<int32_t>(static_cast<uint32_t>(hash) & static_cast<uint32_t>(bucketCount - 1));
}

// Smallest power of two >= 'capacity', floored at kMinBuckets and capped at
// kMaxBucketCount (the doubling loop can never overflow int32 this way).
int32_t RoundUpPow2(int32_t capacity)
{
    int32_t n = (capacity < kMinBuckets) ? kMinBuckets : capacity;
    if (n > kMaxBucketCount)
    {
        n = kMaxBucketCount;
    }
    int32_t p = 1;
    while (p < n)
    {
        p <<= 1;
    }
    return p;
}

// Load-factor test with exact integer math (no float): resize when the
// post-insert OCCUPIED count would exceed 72% of the bucket count.
bool NeedsResize(int32_t occupiedAfterInsert, int32_t bucketCount)
{
    return static_cast<int64_t>(occupiedAfterInsert) * 100 >
           static_cast<int64_t>(bucketCount) * 72;
}

// The ordinal string hash -- the runtime's established string hash
// (icall_string_get_hash_code, valuesemantics.cpp; byte-identical to
// hashtable.cpp's HashStringOrdinal). Equal content -> equal hash.
//
// MASKED to non-negative (net48: `hashCode = GetHashCode(key) & 0x7FFFFFFF`):
// the cached slot hashCode reserves NEGATIVE values (specifically -1) to mark
// a FREE slot, so a raw negative hash would be mistaken for a free slot and
// the pair would vanish from lookups/enumeration (diffdict001 large case).
int32_t StringHash(const Il2CppString* key)
{
    return runtime::icall_string_get_hash_code(reinterpret_cast<Il2CppObject*>(
        const_cast<Il2CppString*>(key))) & 0x7FFFFFFF;
}

// Content equality (ordinal): length + UTF-16 units. Consistent with the
// hash contract -- equal content -> equal hash -> same bucket.
bool KeyEquals(const Il2CppString* a, const Il2CppString* b)
{
    if (a == b)
    {
        return true;
    }
    return a != nullptr && b != nullptr &&
           a->length == b->length &&
           std::memcmp(a->chars, b->chars,
                       static_cast<size_t>(a->length) * sizeof(char16_t)) == 0;
}

// The number of live (occupied) pairs.
int32_t OccupiedCount(const Il2CppDictSI* d)
{
    return d->count - d->freeCount;
}

// FindEntryIndex -- the slot index holding 'key' (>= 0) or -1. Only OCCUPIED
// slots on the key's bucket chain are compared; a free slot never appears on
// a bucket chain.
int32_t FindEntryIndex(const Il2CppDictSI* d, const Il2CppString* key, int32_t hash)
{
    for (int32_t i = d->buckets[BucketIndex(hash, d->bucketCount)]; i >= 0;
         i = d->entries[i].next)
    {
        if (d->entries[i].hashCode == hash && KeyEquals(d->entries[i].key, key))
        {
            return i;
        }
    }
    return -1;
}

// Grow the entries slot array to at least 'needed' capacity (doubling), and
// move every slot. The order is preserved (index-for-index copy).
void GrowEntries(Il2CppDictSI* d, int32_t needed)
{
    if (needed <= d->entryCapacity)
    {
        return;
    }
    int32_t newCap = d->entryCapacity > 0 ? d->entryCapacity : kInitialEntries;
    while (newCap < needed)
    {
        newCap <<= 1;
    }
    Il2CppDictSIEntry* grown = new Il2CppDictSIEntry[newCap];
    for (int32_t i = 0; i < d->count; ++i)
    {
        grown[i] = d->entries[i];
    }
    delete[] d->entries;
    d->entries = grown;
    d->entryCapacity = newCap;
}

//------------------------------------------------------------------------------
// ResizeBuckets -- double the BUCKET array (capped) and rehash every OCCUPIED
// slot from its CACHED hashCode into the new array. The entries array (and the
// enumeration order) is untouched (R7). Chains are relinked as index lists.
//------------------------------------------------------------------------------
void ResizeBuckets(Il2CppDictSI* d)
{
    const int32_t oldCount = d->bucketCount;
    int32_t newCount = oldCount * 2;
    if (newCount < oldCount || newCount > kMaxBucketCount)
    {
        return; // already at the cap: the table stays (see header contract)
    }
    int32_t* newBuckets = new int32_t[newCount];
    for (int32_t i = 0; i < newCount; ++i)
    {
        newBuckets[i] = -1;
    }
    for (int32_t i = 0; i < d->count; ++i)
    {
        Il2CppDictSIEntry& e = d->entries[i];
        if (e.hashCode < 0)
        {
            continue; // free slot: not on any chain
        }
        const int32_t index = BucketIndex(e.hashCode, newCount);
        e.next = newBuckets[index];
        newBuckets[index] = i;
    }
    delete[] d->buckets;
    d->buckets = newBuckets;
    d->bucketCount = newCount;
}

// Defensive null guards: the header contract pins null-key semantics; the
// dict pointer itself must be valid (create/destroy are 1:1 paired).
void ThrowIfNullKey(const Il2CppString* key)
{
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
}

// Acquire a slot index for a NEW pair: reuse the free list head when present
// (R4 -- the slot returns to its original position), else append at 'count'.
int32_t AcquireSlot(Il2CppDictSI* d)
{
    if (d->freeCount > 0)
    {
        const int32_t slot = d->freeList;
        d->freeList = d->entries[slot].next;
        --d->freeCount;
        return slot;
    }
    if (d->count >= d->entryCapacity)
    {
        GrowEntries(d, d->count + 1);
    }
    return d->count++;
}

// Link a freshly written slot into its bucket chain.
void LinkSlot(Il2CppDictSI* d, int32_t slot, int32_t hash)
{
    const int32_t index = BucketIndex(hash, d->bucketCount);
    d->entries[slot].hashCode = hash;
    d->entries[slot].next = d->buckets[index];
    d->buckets[index] = slot;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_dict_si_create -- create with the smallest power-of-two bucket count
// >= capacity (minimum 4). A negative capacity raises
// ArgumentOutOfRangeException.
//------------------------------------------------------------------------------
Il2CppDictSI* il2cpp_dict_si_create(int32_t capacity)
{
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity", nullptr);
    }
    Il2CppDictSI* d = new Il2CppDictSI();
    d->bucketCount = RoundUpPow2(capacity);
    d->buckets = new int32_t[d->bucketCount];
    for (int32_t i = 0; i < d->bucketCount; ++i)
    {
        d->buckets[i] = -1;
    }
    d->entryCapacity = (capacity > kInitialEntries) ? RoundUpPow2(capacity) : kInitialEntries;
    d->entries = new Il2CppDictSIEntry[d->entryCapacity];
    d->count = 0;
    d->freeList = -1;
    d->freeCount = 0;
    return d;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_destroy -- free the bucket array, the entries array and the
// dictionary itself (the 1:1 pair of create).
//------------------------------------------------------------------------------
void il2cpp_dict_si_destroy(Il2CppDictSI* d)
{
    if (d == nullptr)
    {
        return;
    }
    delete[] d->entries;
    delete[] d->buckets;
    delete d;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_add -- Add(key, value) (see header): insert a new pair. A
// null key raises ArgumentNullException; a duplicate key (per the
// content-equality contract) raises ArgumentException and leaves the
// dictionary unchanged. May grow (double + rehash) when the 0.72 load factor
// would be exceeded.
//------------------------------------------------------------------------------
void il2cpp_dict_si_add(Il2CppDictSI* d, Il2CppString* key, int32_t value)
{
    ThrowIfNullKey(key);
    const int32_t hash = StringHash(key);
    if (FindEntryIndex(d, key, hash) >= 0)
    {
        il2cpp_throw_argument_exception("Item has already been added.");
    }
    if (NeedsResize(OccupiedCount(d) + 1, d->bucketCount))
    {
        ResizeBuckets(d);
    }
    const int32_t slot = AcquireSlot(d);
    d->entries[slot].key = key;
    d->entries[slot].value = value;
    LinkSlot(d, slot, hash);
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_set -- Set(key, value) (see header): the indexer SET.
// Insert a new pair, or REPLACE the value of an existing key in place (no
// ArgumentException on a duplicate -- unlike Add; R2: the slot does not move).
// A null key raises ArgumentNullException. The insert path may grow (double +
// rehash) when the 0.72 load factor would be exceeded; a replace never grows.
//------------------------------------------------------------------------------
void il2cpp_dict_si_set(Il2CppDictSI* d, Il2CppString* key, int32_t value)
{
    ThrowIfNullKey(key);
    const int32_t hash = StringHash(key);
    const int32_t existing = FindEntryIndex(d, key, hash);
    if (existing >= 0)
    {
        d->entries[existing].value = value; // in-place replace (no slot move)
        return;
    }
    if (NeedsResize(OccupiedCount(d) + 1, d->bucketCount))
    {
        ResizeBuckets(d);
    }
    const int32_t slot = AcquireSlot(d);
    d->entries[slot].key = key;
    d->entries[slot].value = value;
    LinkSlot(d, slot, hash);
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_try_get_value -- TryGetValue(key, &value) (see header):
// true + the stored value on a hit; false on a miss (the out-param is left
// untouched). Null key -> ArgumentNullException.
//------------------------------------------------------------------------------
bool il2cpp_dict_si_try_get_value(Il2CppDictSI* d, Il2CppString* key, int32_t* value)
{
    ThrowIfNullKey(key);
    const int32_t i = FindEntryIndex(d, key, StringHash(key));
    if (i < 0)
    {
        return false;
    }
    if (value != nullptr)
    {
        *value = d->entries[i].value;
    }
    return true;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_remove -- Remove(key) (see header): true when the key was
// present (unlinked), false otherwise. A null key raises
// ArgumentNullException. The freed slot joins the free list (R3/R4), so a
// later re-add of the same key reuses it and returns to the original position.
//------------------------------------------------------------------------------
bool il2cpp_dict_si_remove(Il2CppDictSI* d, Il2CppString* key)
{
    ThrowIfNullKey(key);
    const int32_t hash = StringHash(key);
    const int32_t bucket = BucketIndex(hash, d->bucketCount);
    int32_t prev = -1;
    for (int32_t i = d->buckets[bucket]; i >= 0; i = d->entries[i].next)
    {
        if (d->entries[i].hashCode == hash && KeyEquals(d->entries[i].key, key))
        {
            if (prev < 0)
            {
                d->buckets[bucket] = d->entries[i].next;
            }
            else
            {
                d->entries[prev].next = d->entries[i].next;
            }
            // Free the slot: thread it onto the free list, mark it free.
            d->entries[i].hashCode = kFreeHashCode;
            d->entries[i].key = nullptr;
            d->entries[i].next = d->freeList;
            d->freeList = i;
            ++d->freeCount;
            return true;
        }
        prev = i;
    }
    return false;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_clear -- Clear() (see header): drop every pair; count -> 0;
// the dictionary stays reusable. Buckets reset to empty; the slot array is
// retained (net48 keeps the arrays and resets count/freeList).
//------------------------------------------------------------------------------
void il2cpp_dict_si_clear(Il2CppDictSI* d)
{
    for (int32_t i = 0; i < d->bucketCount; ++i)
    {
        d->buckets[i] = -1;
    }
    for (int32_t i = 0; i < d->count; ++i)
    {
        d->entries[i].hashCode = kFreeHashCode;
        d->entries[i].key = nullptr;
        d->entries[i].next = -1;
    }
    d->count = 0;
    d->freeList = -1;
    d->freeCount = 0;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_get_count -- Count: the number of LIVE pairs (occupied
// slots, i.e. count - freeCount).
//------------------------------------------------------------------------------
int32_t il2cpp_dict_si_get_count(Il2CppDictSI* d)
{
    return OccupiedCount(d);
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_contains_key -- ContainsKey(key) (see header).
//------------------------------------------------------------------------------
bool il2cpp_dict_si_contains_key(Il2CppDictSI* d, Il2CppString* key)
{
    ThrowIfNullKey(key);
    return FindEntryIndex(d, key, StringHash(key)) >= 0;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_get_keys -- Keys (see header): a fresh REFERENCE array
// (Il2CppString* elements, elemSize sizeof(void*), isRefElem true) holding
// the current keys in net48 ENUMERATION ORDER (entries ascending, skipping
// free slots -- R1/R7). Empty dictionary -> length-0 array. The array is
// freshly allocated, then filled with pure pointer writes (no further
// allocation inside), so no collection can run between its birth and its
// return; the keys themselves are caller-rooted (header GC contract).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_dict_si_get_keys(Il2CppDictSI* d)
{
    const int32_t live = OccupiedCount(d);
    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, live);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    // net48 spec section 8 R1: the entries slot array is walked in INDEX order
    // (0..count ascending), skipping free slots -- INSERTION order. This is
    // the order the net48 oracle enumerates (the 2026-09-24 hash-bucket
    // experiment disagreed with the oracle and was reverted 2026-10-05).
    int32_t n = 0;
    for (int32_t i = 0; i < d->count && n < live; ++i)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue; // free slot
        }
        dst[n++] = reinterpret_cast<Il2CppObject*>(d->entries[i].key);
    }
    return result;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_get_values -- Values (see header): a fresh int32 VALUE array
// (elemSize 4, isRefElem false) holding the current values in the same net48
// enumeration order as get_keys (positionally consistent).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_dict_si_get_values(Il2CppDictSI* d)
{
    const int32_t live = OccupiedCount(d);
    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(int32_t)), false, live);
    int32_t* dst = reinterpret_cast<int32_t*>(result->data);
    // Same insertion-order walk as get_keys (entries index order, skipping
    // free slots) so keys[i] <-> values[i] line up positionally (net48 R1).
    int32_t n = 0;
    for (int32_t i = 0; i < d->count && n < live; ++i)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue; // free slot
        }
        dst[n++] = d->entries[i].value;
    }
    return result;
}

} // namespace il2cpp
