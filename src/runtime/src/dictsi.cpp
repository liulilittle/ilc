//------------------------------------------------------------------------------
// dictsi.cpp -- strongly-typed string->int32 dictionary (see dictsi.h for
// the layout, hash contract, GC contract and growth contract).
//
// Data structure:
//   Il2CppDictSI { buckets: Il2CppDictSIEntry** (heap array of chain heads,
//   zero-initialized), bucketCount (power of two), count }
//   buckets[i] = head of collision chain i; chains are singly linked lists of
//   Il2CppDictSIEntry nodes (key/value/next) allocated on the C++ heap.
//
// Hash contract (implemented here, documented in the header):
//   * Keys are managed strings (Il2CppString*). The hash is the runtime's
//     ordinal string hash (icall_string_get_hash_code -- byte-identical to
//     hashtable.cpp's HashStringOrdinal, see valuesemantics.cpp), so equal
//     CONTENT -> equal hash, regardless of instance identity.
//   * Equality is also by ordinal content (length + UTF-16 units), so two
//     distinct string instances with equal content are the SAME key.
//   Hashes are ALWAYS derived from the LIVE key (nothing cached) -- there is
//   no cached-hash state to go stale, and string hashes are move-stable
//   (content-based), so no heal-on-access logic is needed (unlike the
//   address-hashed keys of the non-generic hashtable).
//
// Growth: load factor 0.72 (integer-exact 72/100), same as the non-generic
// hashtable. Before an insert that would exceed it, the bucket array doubles
// (power of two, capped at 2^30) and every chain is rehashed from the live
// keys into the new array. Entry nodes are reused across resizes (only the
// array is rebuilt).
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
// post-insert count would exceed 72% of the bucket count.
bool NeedsResize(int32_t countAfterInsert, int32_t bucketCount)
{
    return static_cast<int64_t>(countAfterInsert) * 100 >
           static_cast<int64_t>(bucketCount) * 72;
}

// The ordinal string hash -- the runtime's established string hash
// (icall_string_get_hash_code, valuesemantics.cpp; byte-identical to
// hashtable.cpp's HashStringOrdinal). Equal content -> equal hash.
int32_t StringHash(const Il2CppString* key)
{
    return runtime::icall_string_get_hash_code(reinterpret_cast<Il2CppObject*>(
        const_cast<Il2CppString*>(key)));
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

// FindEntry -- locate the entry holding 'key' (see the header's hash
// contract; KeyEquals is the only equality authority). Stable content hashes
// mean a primary-bucket miss is always a genuine absence -- no repair scan.
Il2CppDictSIEntry* FindEntry(const Il2CppDictSI* d, const Il2CppString* key, int32_t hash)
{
    Il2CppDictSIEntry* cur = d->buckets[BucketIndex(hash, d->bucketCount)];
    while (cur != nullptr)
    {
        if (KeyEquals(cur->key, key))
        {
            return cur;
        }
        cur = cur->next;
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// Resize -- double the bucket array (capped) and rehash every entry from its
// LIVE key pointer. Entry nodes are reused; only the array is rebuilt. The
// new array is zero-initialized; rehashing is pure pointer writes.
//------------------------------------------------------------------------------
void Resize(Il2CppDictSI* d)
{
    const int32_t oldCount = d->bucketCount;
    int32_t newCount = oldCount * 2;
    if (newCount < oldCount || newCount > kMaxBucketCount)
    {
        return; // already at the cap: the table stays (see header contract)
    }
    Il2CppDictSIEntry** newBuckets = new Il2CppDictSIEntry*[newCount]();
    for (int32_t i = 0; i < oldCount; ++i)
    {
        Il2CppDictSIEntry* cur = d->buckets[i];
        while (cur != nullptr)
        {
            Il2CppDictSIEntry* entry = cur;
            cur = entry->next; // capture the successor BEFORE relinking
            const int32_t index = BucketIndex(StringHash(entry->key), newCount);
            entry->next = newBuckets[index];
            newBuckets[index] = entry;
        }
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
    d->buckets = new Il2CppDictSIEntry*[d->bucketCount](); // zero-initialized
    d->count = 0;
    return d;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_destroy -- free every entry node, the bucket array and the
// dictionary itself (the 1:1 pair of create).
//------------------------------------------------------------------------------
void il2cpp_dict_si_destroy(Il2CppDictSI* d)
{
    if (d == nullptr)
    {
        return;
    }
    if (d->buckets != nullptr)
    {
        for (int32_t i = 0; i < d->bucketCount; ++i)
        {
            Il2CppDictSIEntry* cur = d->buckets[i];
            while (cur != nullptr)
            {
                Il2CppDictSIEntry* next = cur->next;
                delete cur;
                cur = next;
            }
        }
        delete[] d->buckets;
    }
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
    if (FindEntry(d, key, hash) != nullptr)
    {
        il2cpp_throw_argument_exception("Item has already been added.");
    }
    if (NeedsResize(d->count + 1, d->bucketCount))
    {
        Resize(d);
    }
    Il2CppDictSIEntry* entry = new Il2CppDictSIEntry();
    entry->key = key;
    entry->value = value;
    const int32_t index = BucketIndex(hash, d->bucketCount);
    entry->next = d->buckets[index];
    d->buckets[index] = entry;
    ++d->count;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_set -- Set(key, value) (see header): the indexer SET.
// Insert a new pair, or REPLACE the value of an existing key in place (no
// ArgumentException on a duplicate -- unlike Add). A null key raises
// ArgumentNullException. The insert path may grow (double + rehash) when the
// 0.72 load factor would be exceeded; a replace never grows.
//------------------------------------------------------------------------------
void il2cpp_dict_si_set(Il2CppDictSI* d, Il2CppString* key, int32_t value)
{
    ThrowIfNullKey(key);
    const int32_t hash = StringHash(key);
    Il2CppDictSIEntry* existing = FindEntry(d, key, hash);
    if (existing != nullptr)
    {
        existing->value = value; // in-place replace (no count change, no grow)
        return;
    }
    if (NeedsResize(d->count + 1, d->bucketCount))
    {
        Resize(d);
    }
    Il2CppDictSIEntry* entry = new Il2CppDictSIEntry();
    entry->key = key;
    entry->value = value;
    const int32_t index = BucketIndex(hash, d->bucketCount);
    entry->next = d->buckets[index];
    d->buckets[index] = entry;
    ++d->count;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_try_get_value -- TryGetValue(key, &value) (see header):
// true + the stored value on a hit; false on a miss (the out-param is left
// untouched). Null key -> ArgumentNullException.
//------------------------------------------------------------------------------
bool il2cpp_dict_si_try_get_value(Il2CppDictSI* d, Il2CppString* key, int32_t* value)
{
    ThrowIfNullKey(key);
    const Il2CppDictSIEntry* entry = FindEntry(d, key, StringHash(key));
    if (entry == nullptr)
    {
        return false;
    }
    if (value != nullptr)
    {
        *value = entry->value;
    }
    return true;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_remove -- Remove(key) (see header): true when the key was
// present (unlinked), false otherwise. A null key raises
// ArgumentNullException.
//------------------------------------------------------------------------------
bool il2cpp_dict_si_remove(Il2CppDictSI* d, Il2CppString* key)
{
    ThrowIfNullKey(key);
    const int32_t hash = StringHash(key);
    const int32_t index = BucketIndex(hash, d->bucketCount);
    Il2CppDictSIEntry* prev = nullptr;
    Il2CppDictSIEntry* cur = d->buckets[index];
    while (cur != nullptr)
    {
        if (KeyEquals(cur->key, key))
        {
            if (prev == nullptr)
            {
                d->buckets[index] = cur->next;
            }
            else
            {
                prev->next = cur->next;
            }
            cur->next = nullptr; // detach before freeing
            delete cur;
            --d->count;
            return true;
        }
        prev = cur;
        cur = cur->next;
    }
    return false;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_clear -- Clear() (see header): drop every pair; count -> 0;
// the dictionary stays reusable.
//------------------------------------------------------------------------------
void il2cpp_dict_si_clear(Il2CppDictSI* d)
{
    for (int32_t i = 0; i < d->bucketCount; ++i)
    {
        Il2CppDictSIEntry* cur = d->buckets[i];
        d->buckets[i] = nullptr;
        while (cur != nullptr)
        {
            Il2CppDictSIEntry* next = cur->next;
            delete cur;
            cur = next;
        }
    }
    d->count = 0;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_get_count -- Count (see header).
//------------------------------------------------------------------------------
int32_t il2cpp_dict_si_get_count(Il2CppDictSI* d)
{
    return d->count;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_contains_key -- ContainsKey(key) (see header).
//------------------------------------------------------------------------------
bool il2cpp_dict_si_contains_key(Il2CppDictSI* d, Il2CppString* key)
{
    ThrowIfNullKey(key);
    return FindEntry(d, key, StringHash(key)) != nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_get_keys -- Keys (see header): a fresh REFERENCE array
// (Il2CppString* elements, elemSize sizeof(void*), isRefElem true) holding
// the current keys in the deterministic hash-bucket order (buckets ascending
// 0..bucketCount-1; within a bucket, chain head-first -- most recently added
// first). Empty dictionary -> length-0 array. The array is freshly allocated,
// then filled with pure pointer writes (no further allocation inside), so no
// collection can run between its birth and its return; the keys themselves
// are caller-rooted (header GC contract).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_dict_si_get_keys(Il2CppDictSI* d)
{
    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, d->count);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    int32_t n = 0;
    for (int32_t i = 0; i < d->bucketCount && n < d->count; ++i)
    {
        for (const Il2CppDictSIEntry* cur = d->buckets[i]; cur != nullptr; cur = cur->next)
        {
            dst[n++] = reinterpret_cast<Il2CppObject*>(cur->key);
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// il2cpp_dict_si_get_values -- Values (see header): a fresh int32 VALUE array
// (elemSize 4, isRefElem false) holding the current values in the same
// deterministic hash-bucket order as get_keys (positionally consistent).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_dict_si_get_values(Il2CppDictSI* d)
{
    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(int32_t)), false, d->count);
    int32_t* dst = reinterpret_cast<int32_t*>(result->data);
    int32_t n = 0;
    for (int32_t i = 0; i < d->bucketCount && n < d->count; ++i)
    {
        for (const Il2CppDictSIEntry* cur = d->buckets[i]; cur != nullptr; cur = cur->next)
        {
            dst[n++] = cur->value;
        }
    }
    return result;
}

} // namespace il2cpp

