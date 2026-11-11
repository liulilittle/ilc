//-----------------------------------------------------------------------------
// dictgen.cpp -- the SHARED generic Dictionary`2 storage + kind-descriptor
// registry (see dictgen.h). Ordered object-model table (insertion sequence +
// buckets); every pair is a pair of managed carriers; kind functions come
// from the registry switch, never from per-instantiation code.
//-----------------------------------------------------------------------------

#include "dictgen.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"       // il2cpp_write_barrier
#include "il2cpp_gcroot.h"   // GcRoot
#include "il2cpp_metadata.h" // (array + class registration surface, dictsi precedent)
#include "il2cpp_object.h"   // il2cpp_object_new / il2cpp_class_register
#include "il2cpp_runtime.h"  // il2cpp_box_int32
#include "il2cpp_boxing_ext.h"  // icall_box_double / icall_unbox_double (section 109)
#include "il2cpp_boxing_ext2.h" // icall_box_long / icall_unbox_long (b5-gaps section 106)
#include "il2cpp_string.h"
#include "il2cpp_value_semantics.h" // (string-hash authority, dictsi precedent)
#include "il2cpp_valuetuple.h" // il2cpp_tuple_box_hash / il2cpp_tuple_box_equals (tuple keys)
#include "versionuri.h" // icall_uri_to_string (Uri-key normalization, T4 contract)

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

// Minimum bucket count / doubling cap / initial slots (dictsi constants --
// the same growth discipline, so the same edge behavior).
constexpr int32_t kMinBuckets = 4;
constexpr int32_t kMaxBucketCount = 1 << 30;
constexpr int32_t kInitialEntries = 4;
constexpr int32_t kFreeHashCode = -1;

int32_t BucketIndex(int32_t hash, int32_t bucketCount)
{
    return static_cast<int32_t>(static_cast<uint32_t>(hash) & static_cast<uint32_t>(bucketCount - 1));
}

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

bool NeedsResize(int32_t occupiedAfterInsert, int32_t bucketCount)
{
    return static_cast<int64_t>(occupiedAfterInsert) * 100 >
           static_cast<int64_t>(bucketCount) * 72;
}

// The ordinal string hash -- the runtime's established string hash
// (icall_string_get_hash_code, valuesemantics.cpp), masked non-negative (the
// -1 free marker is reserved).
int32_t StringHash(const Il2CppString* key)
{
    return runtime::icall_string_get_hash_code(reinterpret_cast<Il2CppObject*>(
        const_cast<Il2CppString*>(key))) & 0x7FFFFFFF;
}

bool StrEquals(const Il2CppString* a, const Il2CppString* b)
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

// Address-hash fallback for kinds without a value rule (the hashtable "every
// other key" rule -- reference equality decides, so no silent wrong-bucket).
int32_t AddressHash(const Il2CppObject* key)
{
    return static_cast<int32_t>(
        (static_cast<uintptr_t>(reinterpret_cast<uintptr_t>(key)) >> 3) & 0x7FFFFFFF);
}

int32_t OccupiedCount(const Il2CppDictGen* d)
{
    return d->count - d->freeCount;
}

// Incoming hash (lookup path allocates nothing; int32 keys hash INLINE).
int32_t IncomingHash(int32_t keyInt, Il2CppObject* keyRef, int32_t kind)
{
    if (kind == kGenKind_Int32)
    {
        return keyInt & 0x7FFFFFFF;
    }
    if (kind == kGenKind_String || kind == kGenKind_Uri)
    {
        return StringHash(reinterpret_cast<Il2CppString*>(keyRef));
    }
    if (kind == kGenKind_ValueTuple)
    {
        // A boxed ValueTuple carrier: the structural ValueTuple hash.
        return runtime::il2cpp_tuple_box_hash(keyRef);
    }
    return AddressHash(keyRef);
}

// b5-gaps section 108: the int64 KEY hash (the int64 hash contract used by the
// lookup table's int64 keys -- fold the high word so adjacent longs do not
// collide into one bucket).
int32_t IncomingHash64(int64_t key)
{
    const uint64_t u = static_cast<uint64_t>(key);
    return static_cast<int32_t>((u ^ (u >> 33)) & 0x7FFFFFFFULL);
}

// Incoming-vs-stored equality (int32 keys compare INLINE -- the table never
// holds an unrooted box, so no unbox is needed on any path).
bool IncomingEquals(int32_t keyInt, Il2CppObject* keyRef,
    const Il2CppDictGenEntry* stored, int32_t kind)
{
    if (kind == kGenKind_Int32)
    {
        return keyInt == stored->keyInt;
    }
    if (kind == kGenKind_String || kind == kGenKind_Uri)
    {
        return StrEquals(reinterpret_cast<Il2CppString*>(keyRef),
                         reinterpret_cast<const Il2CppString*>(stored->keyRef));
    }
    if (kind == kGenKind_ValueTuple)
    {
        // A boxed ValueTuple carrier: the structural ValueTuple equality.
        return runtime::il2cpp_tuple_box_equals(keyRef, stored->keyRef);
    }
    return keyRef == stored->keyRef;
}

// b5-gaps section 108: the int64 KEY equality (the full 64-bit compare).
bool IncomingEquals64(int64_t key, const Il2CppDictGenEntry* stored)
{
    return stored->keyI64 == key;
}

int32_t FindEntryIndex(const Il2CppDictGen* d, int32_t keyInt,
    Il2CppObject* keyRef, int32_t kind, int32_t hash)
{
    for (int32_t i = d->buckets[BucketIndex(hash, d->bucketCount)]; i >= 0;
         i = d->entries[i].next)
    {
        if (d->entries[i].hashCode == hash &&
            IncomingEquals(keyInt, keyRef, &d->entries[i], kind))
        {
            return i;
        }
    }
    return -1;
}

// b5-gaps section 108: the int64 KEY entry scan (the same bucket-chain walk with
// the 64-bit equality/hash).
int32_t FindEntryIndex64(const Il2CppDictGen* d, int64_t key, int32_t hash)
{
    for (int32_t i = d->buckets[BucketIndex(hash, d->bucketCount)]; i >= 0;
         i = d->entries[i].next)
    {
        if (d->entries[i].hashCode == hash &&
            IncomingEquals64(key, &d->entries[i]))
        {
            return i;
        }
    }
    return -1;
}

// b5-gaps section 108: the int64 KEY scan through the generic (int32-keyInt)
// entry point -- the table's hash/equality helpers cannot see the parallel
// keyI64 slot, so the int64-key callers route here instead.
int32_t FindEntryIndexKL(const Il2CppDictGen* d, int64_t key)
{
    return FindEntryIndex64(d, key, IncomingHash64(key));
}

void GrowEntries(Il2CppDictGen* d, int32_t needed)
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
    Il2CppDictGenEntry* grown = new Il2CppDictGenEntry[newCap];
    for (int32_t i = 0; i < d->count; ++i)
    {
        grown[i] = d->entries[i];
    }
    delete[] d->entries;
    d->entries = grown;
    d->entryCapacity = newCap;
}

void ResizeBuckets(Il2CppDictGen* d)
{
    const int32_t oldCount = d->bucketCount;
    int32_t newCount = oldCount * 2;
    if (newCount < oldCount || newCount > kMaxBucketCount)
    {
        return;
    }
    int32_t* newBuckets = new int32_t[newCount];
    for (int32_t i = 0; i < newCount; ++i)
    {
        newBuckets[i] = -1;
    }
    for (int32_t i = 0; i < d->count; ++i)
    {
        Il2CppDictGenEntry& e = d->entries[i];
        if (e.hashCode < 0)
        {
            continue;
        }
        const int32_t index = BucketIndex(e.hashCode, newCount);
        e.next = newBuckets[index];
        newBuckets[index] = i;
    }
    delete[] d->buckets;
    d->buckets = newBuckets;
    d->bucketCount = newCount;
}

// Acquire a slot for a NEW pair. A reused free slot keeps its sequence link
// (R4 -- original position); a fresh slot is appended at the sequence tail.
// Reports which case applied through 'reused'.
int32_t AcquireSlot(Il2CppDictGen* d, bool* reused)
{
    if (d->freeCount > 0)
    {
        const int32_t slot = d->freeList;
        d->freeList = d->entries[slot].next;
        --d->freeCount;
        *reused = true;
        return slot;
    }
    if (d->count >= d->entryCapacity)
    {
        GrowEntries(d, d->count + 1);
    }
    const int32_t slot = d->count++;
    d->entries[slot].seqNext = -1;
    if (d->seqTail < 0)
    {
        d->seqHead = slot;
    }
    else
    {
        d->entries[d->seqTail].seqNext = slot;
    }
    d->seqTail = slot;
    *reused = false;
    return slot;
}

void LinkSlot(Il2CppDictGen* d, int32_t slot, int32_t hash)
{
    const int32_t index = BucketIndex(hash, d->bucketCount);
    d->entries[slot].hashCode = hash;
    d->entries[slot].next = d->buckets[index];
    d->buckets[index] = slot;
}

void ThrowIfNullCarrier(Il2CppObject* carrier)
{
    if (carrier == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
}

void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

Il2CppDictGen* AsDict(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppDictGenObject*>(self)->dict;
}

} // namespace

//-----------------------------------------------------------------------------
// Descriptor registry.
//-----------------------------------------------------------------------------
bool il2cpp_dict_gen_kind_is_ref(int32_t kind)
{
    return kind != kGenKind_Int32;
}

int32_t il2cpp_dict_gen_hash_carrier(const Il2CppObject* carrier, int32_t kind)
{
    if (kind == kGenKind_Int32)
    {
        return il2cpp_dict_gen_unbox_int32(carrier) & 0x7FFFFFFF;
    }
    if (kind == kGenKind_Int64)
    {
        return static_cast<int32_t>(runtime::icall_unbox_long(const_cast<Il2CppObject*>(carrier)) & 0x7FFFFFFFLL);
    }
    if (kind == kGenKind_Double)
    {
        double v = runtime::icall_unbox_double(const_cast<Il2CppObject*>(carrier));
        int64_t bits;
        std::memcpy(&bits, &v, sizeof(bits));
        return static_cast<int32_t>(bits & 0x7FFFFFFFLL);
    }
    if (kind == kGenKind_String || kind == kGenKind_Uri)
    {
        return StringHash(reinterpret_cast<const Il2CppString*>(carrier));
    }
    if (kind == kGenKind_ValueTuple)
    {
        return runtime::il2cpp_tuple_box_hash(carrier);
    }
    return AddressHash(carrier);
}

bool il2cpp_dict_gen_carrier_equals(const Il2CppObject* a, const Il2CppObject* b, int32_t kind)
{
    if (kind == kGenKind_Int32)
    {
        return il2cpp_dict_gen_unbox_int32(a) == il2cpp_dict_gen_unbox_int32(b);
    }
    if (kind == kGenKind_Int64)
    {
        return runtime::icall_unbox_long(const_cast<Il2CppObject*>(a)) == runtime::icall_unbox_long(const_cast<Il2CppObject*>(b));
    }
    if (kind == kGenKind_Double)
    {
        return runtime::icall_unbox_double(const_cast<Il2CppObject*>(a)) == runtime::icall_unbox_double(const_cast<Il2CppObject*>(b));
    }
    if (kind == kGenKind_String || kind == kGenKind_Uri)
    {
        return StrEquals(reinterpret_cast<const Il2CppString*>(a),
                         reinterpret_cast<const Il2CppString*>(b));
    }
    if (kind == kGenKind_ValueTuple)
    {
        return runtime::il2cpp_tuple_box_equals(a, b);
    }
    return a == b;
}

Il2CppObject* il2cpp_dict_gen_box_int32(int32_t value)
{
    return il2cpp_box_int32(nullptr, value);
}

int32_t il2cpp_dict_gen_unbox_int32(const Il2CppObject* boxed)
{
    return *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(boxed) + 8);
}

Il2CppObject* il2cpp_dict_gen_box_double(double value)
{
    return runtime::icall_box_double(value);
}

double il2cpp_dict_gen_unbox_double(const Il2CppObject* boxed)
{
    return runtime::icall_unbox_double(const_cast<Il2CppObject*>(boxed));
}

//-----------------------------------------------------------------------------
// Raw table.
//-----------------------------------------------------------------------------
Il2CppDictGen* il2cpp_dict_gen_create(int32_t capacity)
{
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity", nullptr);
    }
    Il2CppDictGen* d = new Il2CppDictGen();
    d->bucketCount = RoundUpPow2(capacity);
    d->buckets = new int32_t[d->bucketCount];
    for (int32_t i = 0; i < d->bucketCount; ++i)
    {
        d->buckets[i] = -1;
    }
    d->entryCapacity = (capacity > kInitialEntries) ? RoundUpPow2(capacity) : kInitialEntries;
    d->entries = new Il2CppDictGenEntry[d->entryCapacity];
    d->count = 0;
    d->freeList = -1;
    d->freeCount = 0;
    d->seqHead = -1;
    d->seqTail = -1;
    return d;
}

void il2cpp_dict_gen_destroy(Il2CppDictGen* d)
{
    if (d == nullptr)
    {
        return;
    }
    delete[] d->entries;
    delete[] d->buckets;
    delete d;
}

void il2cpp_dict_gen_add(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    Il2CppObject* value, int32_t keyKind, int32_t valueKind)
{
    (void)valueKind;
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64Key)
    {
        ThrowIfNullCarrier(keyRef);
    }
    // b5-gaps section 108: the int64 key rides the parallel keyI64 slot.
    if (keyKind == kGenKind_Int64Key)
    {
        il2cpp_dict_gen_add_kl(d, static_cast<int64_t>(keyInt), value,
                               keyKind, valueKind);
        return;
    }
    const int32_t hash = IncomingHash(keyInt, keyRef, keyKind);
    if (FindEntryIndex(d, keyInt, keyRef, keyKind, hash) >= 0)
    {
        il2cpp_throw_argument_exception("Item has already been added.");
    }
    if (NeedsResize(OccupiedCount(d) + 1, d->bucketCount))
    {
        ResizeBuckets(d);
    }
    bool reused = false;
    const int32_t slot = AcquireSlot(d, &reused);
    (void)reused;
    d->entries[slot].keyInt = keyInt;
    d->entries[slot].keyRef = (keyKind == kGenKind_Int32) ? nullptr : keyRef;
    d->entries[slot].value = value;
    LinkSlot(d, slot, hash);
}

void il2cpp_dict_gen_set(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    Il2CppObject* value, int32_t keyKind, int32_t valueKind)
{
    (void)valueKind;
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64Key)
    {
        ThrowIfNullCarrier(keyRef);
    }
    // b5-gaps section 108: the int64 key rides the parallel keyI64 slot.
    if (keyKind == kGenKind_Int64Key)
    {
        il2cpp_dict_gen_set_kl(d, static_cast<int64_t>(keyInt), value,
                               keyKind, valueKind);
        return;
    }
    const int32_t hash = IncomingHash(keyInt, keyRef, keyKind);
    const int32_t existing = FindEntryIndex(d, keyInt, keyRef, keyKind, hash);
    if (existing >= 0)
    {
        d->entries[existing].value = value; // in-place replace (R2)
        return;
    }
    if (NeedsResize(OccupiedCount(d) + 1, d->bucketCount))
    {
        ResizeBuckets(d);
    }
    bool reused = false;
    const int32_t slot = AcquireSlot(d, &reused);
    (void)reused;
    d->entries[slot].keyInt = keyInt;
    d->entries[slot].keyRef = (keyKind == kGenKind_Int32) ? nullptr : keyRef;
    d->entries[slot].value = value;
    LinkSlot(d, slot, hash);
}

bool il2cpp_dict_gen_try_get_value(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    Il2CppObject** value, int32_t keyKind, int32_t valueKind)
{
    (void)valueKind;
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64Key)
    {
        ThrowIfNullCarrier(keyRef);
    }
    // b5-gaps section 108: the int64 key scans the parallel keyI64 slot.
    const int32_t i = keyKind == kGenKind_Int64Key
        ? FindEntryIndexKL(d, static_cast<int64_t>(keyInt))
        : FindEntryIndex(d, keyInt, keyRef, keyKind,
            IncomingHash(keyInt, keyRef, keyKind));
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

bool il2cpp_dict_gen_contains_key(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    int32_t keyKind, int32_t valueKind)
{
    (void)valueKind;
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64Key)
    {
        ThrowIfNullCarrier(keyRef);
    }
    // b5-gaps section 108: the int64 key scans the parallel keyI64 slot.
    return keyKind == kGenKind_Int64Key
        ? FindEntryIndexKL(d, static_cast<int64_t>(keyInt)) >= 0
        : FindEntryIndex(d, keyInt, keyRef, keyKind,
            IncomingHash(keyInt, keyRef, keyKind)) >= 0;
}

bool il2cpp_dict_gen_remove(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    int32_t keyKind, int32_t valueKind)
{
    (void)valueKind;
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64Key)
    {
        ThrowIfNullCarrier(keyRef);
    }
    // b5-gaps section 108: the int64 key removes through the parallel slot.
    if (keyKind == kGenKind_Int64Key)
    {
        return il2cpp_dict_gen_remove_kl(d, static_cast<int64_t>(keyInt),
                                         keyKind, valueKind);
    }
    const int32_t hash = IncomingHash(keyInt, keyRef, keyKind);
    const int32_t bucket = BucketIndex(hash, d->bucketCount);
    int32_t prev = -1;
    for (int32_t i = d->buckets[bucket]; i >= 0; i = d->entries[i].next)
    {
        if (d->entries[i].hashCode == hash &&
            IncomingEquals(keyInt, keyRef, &d->entries[i], keyKind))
        {
            if (prev < 0)
            {
                d->buckets[bucket] = d->entries[i].next;
            }
            else
            {
                d->entries[prev].next = d->entries[i].next;
            }
            // Free the slot (sequence link untouched -- R3/R4).
            d->entries[i].hashCode = kFreeHashCode;
            d->entries[i].keyInt = 0;
            d->entries[i].keyRef = nullptr;
            d->entries[i].value = nullptr;
            d->entries[i].next = d->freeList;
            d->freeList = i;
            ++d->freeCount;
            return true;
        }
        prev = i;
    }
    return false;
}

void il2cpp_dict_gen_clear(Il2CppDictGen* d)
{
    for (int32_t i = 0; i < d->bucketCount; ++i)
    {
        d->buckets[i] = -1;
    }
    for (int32_t i = 0; i < d->count; ++i)
    {
        d->entries[i].hashCode = kFreeHashCode;
        d->entries[i].keyInt = 0;
        d->entries[i].keyI64 = 0;
        d->entries[i].keyRef = nullptr;
        d->entries[i].value = nullptr;
        d->entries[i].next = -1;
        d->entries[i].seqNext = -1;
    }
    d->count = 0;
    d->freeList = -1;
    d->freeCount = 0;
    d->seqHead = -1;
    d->seqTail = -1;
}

int32_t il2cpp_dict_gen_get_count(Il2CppDictGen* d)
{
    return OccupiedCount(d);
}

//-----------------------------------------------------------------------------
// b5-gaps section 108: the INT64 KEY arms. The entry's keyInt slot is 4 bytes,
// so a long key rides the parallel keyI64 slot (same entry index). A duplicate
// add throws ArgumentException, get-miss throws KeyNotFoundException, and the
// enumeration order follows the insertion sequence (the shared seqHead chain).
//-----------------------------------------------------------------------------
void il2cpp_dict_gen_add_kl(Il2CppDictGen* d, int64_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    (void)keyKind;
    (void)valueKind;
    const int32_t hash = IncomingHash64(key);
    if (FindEntryIndex64(d, key, hash) >= 0)
    {
        il2cpp_throw_argument_exception("Item has already been added.");
    }
    if (NeedsResize(OccupiedCount(d) + 1, d->bucketCount))
    {
        ResizeBuckets(d);
    }
    bool reused = false;
    const int32_t slot = AcquireSlot(d, &reused);
    (void)reused;
    d->entries[slot].keyI64 = key;
    d->entries[slot].keyRef = nullptr;
    d->entries[slot].value = value;
    LinkSlot(d, slot, hash);
}

void il2cpp_dict_gen_set_kl(Il2CppDictGen* d, int64_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    (void)keyKind;
    (void)valueKind;
    const int32_t hash = IncomingHash64(key);
    const int32_t existing = FindEntryIndex64(d, key, hash);
    if (existing >= 0)
    {
        d->entries[existing].value = value; // in-place replace (R2)
        return;
    }
    if (NeedsResize(OccupiedCount(d) + 1, d->bucketCount))
    {
        ResizeBuckets(d);
    }
    bool reused = false;
    const int32_t slot = AcquireSlot(d, &reused);
    (void)reused;
    d->entries[slot].keyI64 = key;
    d->entries[slot].keyRef = nullptr;
    d->entries[slot].value = value;
    LinkSlot(d, slot, hash);
}

bool il2cpp_dict_gen_try_get_value_kl(Il2CppDictGen* d, int64_t key,
    Il2CppObject** value, int32_t keyKind, int32_t valueKind)
{
    (void)keyKind;
    (void)valueKind;
    const int32_t i = FindEntryIndex64(d, key, IncomingHash64(key));
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

bool il2cpp_dict_gen_contains_key_kl(Il2CppDictGen* d, int64_t key,
    int32_t keyKind, int32_t valueKind)
{
    (void)keyKind;
    (void)valueKind;
    return FindEntryIndex64(d, key, IncomingHash64(key)) >= 0;
}

bool il2cpp_dict_gen_remove_kl(Il2CppDictGen* d, int64_t key,
    int32_t keyKind, int32_t valueKind)
{
    (void)keyKind;
    (void)valueKind;
    const int32_t hash = IncomingHash64(key);
    const int32_t bucket = BucketIndex(hash, d->bucketCount);
    int32_t prev = -1;
    for (int32_t i = d->buckets[bucket]; i >= 0; i = d->entries[i].next)
    {
        if (d->entries[i].hashCode == hash &&
            IncomingEquals64(key, &d->entries[i]))
        {
            if (prev < 0)
            {
                d->buckets[bucket] = d->entries[i].next;
            }
            else
            {
                d->entries[prev].next = d->entries[i].next;
            }
            d->entries[i].hashCode = kFreeHashCode;
            d->entries[i].keyInt = 0;
            d->entries[i].keyI64 = 0;
            d->entries[i].keyRef = nullptr;
            d->entries[i].value = nullptr;
            d->entries[i].next = d->freeList;
            d->freeList = i;
            ++d->freeCount;
            return true;
        }
        prev = i;
    }
    return false;
}

// The int64-key snapshot (a real int64[] -- net48's Dictionary<long,T>.Keys
// layout), in insertion order.
Il2CppArray* il2cpp_dict_gen_get_keys_kl(Il2CppDictGen* d)
{
    const int32_t live = OccupiedCount(d);
    Il2CppArray* result = il2cpp_array_new_typed(
        static_cast<int32_t>(sizeof(int64_t)), kElemKindI8, live);
    int64_t* dst = reinterpret_cast<int64_t*>(result->data);
    int32_t n = 0;
    for (int32_t i = d->seqHead; i >= 0 && n < live; i = d->entries[i].seqNext)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue;
        }
        dst[n++] = d->entries[i].keyI64;
    }
    return result;
}

Il2CppArray* il2cpp_dict_gen_get_keys(Il2CppDictGen* d, int32_t keyKind)
{
    const int32_t live = OccupiedCount(d);
    if (keyKind == kGenKind_Int32)
    {
        Il2CppArray* result = il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(int32_t)), false, live);
        int32_t* dst = reinterpret_cast<int32_t*>(result->data);
        int32_t n = 0;
        for (int32_t i = d->seqHead; i >= 0 && n < live; i = d->entries[i].seqNext)
        {
            if (d->entries[i].hashCode < 0)
            {
                continue;
            }
            dst[n++] = d->entries[i].keyInt;
        }
        return result;
    }
    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, live);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    int32_t n = 0;
    for (int32_t i = d->seqHead; i >= 0 && n < live; i = d->entries[i].seqNext)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue;
        }
        dst[n++] = d->entries[i].keyRef;
    }
    return result;
}

Il2CppArray* il2cpp_dict_gen_get_values(Il2CppDictGen* d)
{
    const int32_t live = OccupiedCount(d);
    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, live);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    int32_t n = 0;
    for (int32_t i = d->seqHead; i >= 0 && n < live; i = d->entries[i].seqNext)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue;
        }
        dst[n++] = d->entries[i].value;
    }
    return result;
}

// b5-gaps section 104: the int32-value instance arms. The value carrier is a
// BOXED int32 (the section-78 kernel's il2cpp_dict_gen_box_int32); the raw
// table fns are shared with the reference-value form.
int32_t il2cpp_dict_gen_get_i(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, &value, keyKind, valueKind))
    {
        return value != nullptr ? il2cpp_dict_gen_unbox_int32(value) : 0;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0; // unreachable (the factory throws)
}

void il2cpp_dict_gen_add_i(Il2CppObject* self, int32_t key, int32_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_add(AsDict(self), key, nullptr,
        il2cpp_dict_gen_box_int32(value), keyKind, valueKind);
}

void il2cpp_dict_gen_set_i(Il2CppObject* self, int32_t key, int32_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_set(AsDict(self), key, nullptr,
        il2cpp_dict_gen_box_int32(value), keyKind, valueKind);
}

bool il2cpp_dict_gen_try_get_value_i(Il2CppObject* self, int32_t key,
    int32_t* value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = nullptr;
    if (!il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, &boxed,
                                       keyKind, valueKind))
    {
        if (value != nullptr)
        {
            *value = 0; // net48: out is default(TValue) on a miss
        }
        return false;
    }
    if (value != nullptr)
    {
        *value = boxed != nullptr ? il2cpp_dict_gen_unbox_int32(boxed) : 0;
    }
    return true;
}

// b5-gaps section 104: the int32-value snapshot (a real int32[] -- net48's
// Dictionary<int,int>.Values layout). Every value carrier is a boxed int32
// (il2cpp_dict_gen_box_int32), so the unbox is a payload read at offset 8.
Il2CppArray* il2cpp_dict_gen_get_values_i(Il2CppDictGen* d)
{
    const int32_t live = OccupiedCount(d);
    Il2CppArray* result = il2cpp_array_new_typed(
        static_cast<int32_t>(sizeof(int32_t)), kElemKindI4, live);
    int32_t* dst = reinterpret_cast<int32_t*>(result->data);
    int32_t n = 0;
    for (int32_t i = d->seqHead; i >= 0 && n < live; i = d->entries[i].seqNext)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue;
        }
        Il2CppObject* boxed = d->entries[i].value;
        dst[n++] = boxed != nullptr
            ? il2cpp_dict_gen_unbox_int32(boxed) : 0;
    }
    return result;
}

// b5-gaps section 106: the int64-value arms. The value carrier is a BOXED
// int64 (icall_box_long) -- the same 8-byte payload slot as the boxed int32,
// so the raw table fns are shared with the int32/reference forms.
int64_t il2cpp_dict_gen_get_l(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    // b5-gaps section 108: the int64 key rides the dedicated scan.
    if (keyKind == kGenKind_Int64Key)
    {
        if (!il2cpp_dict_gen_try_get_value_kl(AsDict(self), static_cast<int64_t>(key),
                                              &value, keyKind, valueKind))
        {
            il2cpp_throw_key_not_found_exception(nullptr);
            return 0; // unreachable (the factory throws)
        }
        return value != nullptr ? runtime::icall_unbox_long(value) : 0;
    }
    if (il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, &value, keyKind, valueKind))
    {
        return value != nullptr ? runtime::icall_unbox_long(value) : 0;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0; // unreachable (the factory throws)
}

void il2cpp_dict_gen_add_l(Il2CppObject* self, int32_t key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    // b5-gaps section 108: the int64 key rides the dedicated arm.
    if (keyKind == kGenKind_Int64Key)
    {
        il2cpp_dict_gen_add_kl(AsDict(self), static_cast<int64_t>(key),
                               runtime::icall_box_long(value), keyKind, valueKind);
        return;
    }
    il2cpp_dict_gen_add(AsDict(self), key, nullptr,
        runtime::icall_box_long(value), keyKind, valueKind);
}

void il2cpp_dict_gen_set_l(Il2CppObject* self, int32_t key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    // b5-gaps section 108: the int64 key rides the dedicated arm.
    if (keyKind == kGenKind_Int64Key)
    {
        il2cpp_dict_gen_set_kl(AsDict(self), static_cast<int64_t>(key),
                               runtime::icall_box_long(value), keyKind, valueKind);
        return;
    }
    il2cpp_dict_gen_set(AsDict(self), key, nullptr,
        runtime::icall_box_long(value), keyKind, valueKind);
}

bool il2cpp_dict_gen_try_get_value_l(Il2CppObject* self, int32_t key,
    int64_t* value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = nullptr;
    // b5-gaps section 108: the int64 key rides the dedicated scan.
    const bool hit = keyKind == kGenKind_Int64Key
        ? il2cpp_dict_gen_try_get_value_kl(AsDict(self), static_cast<int64_t>(key),
                                           &boxed, keyKind, valueKind)
        : il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, &boxed,
                                        keyKind, valueKind);
    if (!hit)
    {
        // net48: out is default(TValue) on a miss.
        if (value != nullptr)
        {
            *value = 0;
        }
        return false;
    }
    if (value != nullptr)
    {
        *value = boxed != nullptr ? runtime::icall_unbox_long(boxed) : 0;
    }
    return true;
}

// b5-gaps section 106: the int64-value snapshot (a real int64[] -- net48's
// Dictionary<int,long>.Values layout). Every value carrier is a boxed int64.
Il2CppArray* il2cpp_dict_gen_get_values_l(Il2CppDictGen* d)
{
    const int32_t live = OccupiedCount(d);
    Il2CppArray* result = il2cpp_array_new_typed(
        static_cast<int32_t>(sizeof(int64_t)), kElemKindI8, live);
    int64_t* dst = reinterpret_cast<int64_t*>(result->data);
    int32_t n = 0;
    for (int32_t i = d->seqHead; i >= 0 && n < live; i = d->entries[i].seqNext)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue;
        }
        Il2CppObject* boxed = d->entries[i].value;
        dst[n++] = boxed != nullptr
            ? runtime::icall_unbox_long(boxed) : 0;
    }
    return result;
}

// b5-gaps section 109: the double-value instance arms. The value carrier is a
// BOXED double (icall_box_double) -- the same 8-byte payload slot as the boxed
// int64/int32.
double il2cpp_dict_gen_get_d(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, &value, keyKind, valueKind))
    {
        return value != nullptr ? runtime::icall_unbox_double(value) : 0.0;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0.0; // unreachable (the factory throws)
}

void il2cpp_dict_gen_add_d(Il2CppObject* self, int32_t key, double value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_add(AsDict(self), key, nullptr,
        runtime::icall_box_double(value), keyKind, valueKind);
}

void il2cpp_dict_gen_set_d(Il2CppObject* self, int32_t key, double value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_set(AsDict(self), key, nullptr,
        runtime::icall_box_double(value), keyKind, valueKind);
}

bool il2cpp_dict_gen_try_get_value_d(Il2CppObject* self, int32_t key,
    double* value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = nullptr;
    if (!il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, &boxed,
                                       keyKind, valueKind))
    {
        if (value != nullptr)
        {
            *value = 0.0; // net48: out is default(TValue) on a miss
        }
        return false;
    }
    if (value != nullptr)
    {
        *value = boxed != nullptr ? runtime::icall_unbox_double(boxed) : 0.0;
    }
    return true;
}

// b5-gaps section 109: the double-value snapshot (a real double[] -- net48's
// Dictionary<TKey,double>.Values layout). Every value carrier is a boxed double.
Il2CppArray* il2cpp_dict_gen_get_values_d(Il2CppDictGen* d)
{
    const int32_t live = OccupiedCount(d);
    Il2CppArray* result = il2cpp_array_new_typed(
        static_cast<int32_t>(sizeof(double)), kElemKindD, live);
    double* dst = reinterpret_cast<double*>(result->data);
    int32_t n = 0;
    for (int32_t i = d->seqHead; i >= 0 && n < live; i = d->entries[i].seqNext)
    {
        if (d->entries[i].hashCode < 0)
        {
            continue;
        }
        Il2CppObject* boxed = d->entries[i].value;
        dst[n++] = boxed != nullptr
            ? runtime::icall_unbox_double(boxed) : 0.0;
    }
    return result;
}

namespace runtime {

// b5-gaps section 104: the int32-value icall arms. The value carrier is a
// BOXED int32; the raw table fns are shared with the reference-value form.
int32_t icall_dict_gen_get_i(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    return il2cpp_dict_gen_get_i(self, key, keyKind, valueKind);
}

void icall_dict_gen_add_i(Il2CppObject* self, int32_t key, int32_t value,
    int32_t keyKind, int32_t valueKind)
{
    il2cpp_dict_gen_add_i(self, key, value, keyKind, valueKind);
}

void icall_dict_gen_set_i(Il2CppObject* self, int32_t key, int32_t value,
    int32_t keyKind, int32_t valueKind)
{
    il2cpp_dict_gen_set_i(self, key, value, keyKind, valueKind);
}

bool icall_dict_gen_try_get_value_i(Il2CppObject* self, int32_t key,
    int32_t* value, int32_t keyKind, int32_t valueKind)
{
    return il2cpp_dict_gen_try_get_value_i(self, key, value, keyKind, valueKind);
}

Il2CppArray* icall_dict_gen_get_values_i(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_get_values_i(AsDict(self));
}

// b5-gaps section 106: the int64-value icall twins (int32-key form).
int64_t icall_dict_gen_get_l(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    return il2cpp_dict_gen_get_l(self, key, keyKind, valueKind);
}

void icall_dict_gen_add_l(Il2CppObject* self, int32_t key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    il2cpp_dict_gen_add_l(self, key, value, keyKind, valueKind);
}

void icall_dict_gen_set_l(Il2CppObject* self, int32_t key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    il2cpp_dict_gen_set_l(self, key, value, keyKind, valueKind);
}

bool icall_dict_gen_try_get_value_l(Il2CppObject* self, int32_t key,
    int64_t* value, int32_t keyKind, int32_t valueKind)
{
    return il2cpp_dict_gen_try_get_value_l(self, key, value, keyKind, valueKind);
}

Il2CppArray* icall_dict_gen_get_values_l(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_get_values_l(AsDict(self));
}

// b5-gaps section 109: the double-value icall twins (int32-key form).
double icall_dict_gen_get_d(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    return il2cpp_dict_gen_get_d(self, key, keyKind, valueKind);
}

void icall_dict_gen_add_d(Il2CppObject* self, int32_t key, double value,
    int32_t keyKind, int32_t valueKind)
{
    il2cpp_dict_gen_add_d(self, key, value, keyKind, valueKind);
}

void icall_dict_gen_set_d(Il2CppObject* self, int32_t key, double value,
    int32_t keyKind, int32_t valueKind)
{
    il2cpp_dict_gen_set_d(self, key, value, keyKind, valueKind);
}

bool icall_dict_gen_try_get_value_d(Il2CppObject* self, int32_t key,
    double* value, int32_t keyKind, int32_t valueKind)
{
    return il2cpp_dict_gen_try_get_value_d(self, key, value, keyKind, valueKind);
}

Il2CppArray* icall_dict_gen_get_values_d(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_get_values_d(AsDict(self));
}

// b5-gaps section 109: the string-key + double-value twin.
double icall_dict_gen_get_d_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    // b5-gaps section 109: string keys are reference carriers; pass keyInt=0 and
    // keyRef=key (the int32 key slot is unused for reference keys).
    if (il2cpp_dict_gen_try_get_value(AsDict(self), 0, key, &value, keyKind, valueKind))
    {
        return value != nullptr ? runtime::icall_unbox_double(value) : 0.0;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0.0; // unreachable (the factory throws)
}

bool icall_dict_gen_try_get_value_d_o(Il2CppObject* self, Il2CppObject* key,
    double* value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = nullptr;
    if (!il2cpp_dict_gen_try_get_value(AsDict(self), 0, key, &boxed,
                                       keyKind, valueKind))
    {
        if (value != nullptr)
        {
            *value = 0.0;
        }
        return false;
    }
    if (value != nullptr)
    {
        *value = boxed != nullptr ? runtime::icall_unbox_double(boxed) : 0.0;
    }
    return true;
}

} // namespace il2cpp::runtime

//-----------------------------------------------------------------------------
// Wrapper object, enumerator + KVP holders (Phase-1 int_string shape).
//-----------------------------------------------------------------------------
Il2CppClass System_Collections_Generic_Dictionary_2_gen_class = {
    "System.Collections.Generic.Dictionary`2<gen>", // name
    static_cast<int32_t>(sizeof(Il2CppDictGenObject)), // instanceSize
    0b100u,                              // refFieldBitmap: slot 2 = the LINQ root pack (section 78); slot 1 stays raw
    1,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

Il2CppClass System_Collections_Generic_Dictionary_2_Enumerator_gen_int_string_class = {
    "System.Collections.Generic.Dictionary`2<int,string>+Enumerator", // name
    static_cast<int32_t>(sizeof(Il2CppDictGenEnumerator)), // instanceSize
    0b110u,                              // refFieldBitmap: slots 1+2
    2,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

Il2CppClass System_Collections_Generic_KeyValuePair_2_gen_int_string_class = {
    "System.Collections.Generic.KeyValuePair`2<int,string>", // name
    static_cast<int32_t>(sizeof(Il2CppDictKvpGenIS)), // instanceSize
    0b100u,                              // refFieldBitmap: slot 2 (the value)
    1,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

namespace {

bool g_dictGenRegistered = false;

void EnsureDictGenRegistered()
{
    if (g_dictGenRegistered) return;
    il2cpp_class_register(&System_Collections_Generic_Dictionary_2_gen_class);
    il2cpp_class_register(&System_Collections_Generic_Dictionary_2_Enumerator_gen_int_string_class);
    il2cpp_class_register(&System_Collections_Generic_KeyValuePair_2_gen_int_string_class);
    g_dictGenRegistered = true;
}

int32_t EnumeratorLength(const Il2CppDictGenEnumerator* en)
{
    return en->keys != nullptr ? en->keys->length : en->values->length;
}

} // namespace

Il2CppClass* il2cpp_dict_gen_object_class()
{
    EnsureDictGenRegistered();
    return &System_Collections_Generic_Dictionary_2_gen_class;
}

Il2CppClass* il2cpp_dict_gen_enumerator_class()
{
    EnsureDictGenRegistered();
    return &System_Collections_Generic_Dictionary_2_Enumerator_gen_int_string_class;
}

Il2CppClass* il2cpp_kvp_gen_is_class()
{
    EnsureDictGenRegistered();
    return &System_Collections_Generic_KeyValuePair_2_gen_int_string_class;
}

namespace runtime {

void icall_dict_gen_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    reinterpret_cast<Il2CppDictGenObject*>(self)->dict = il2cpp_dict_gen_create(capacity);
}

void icall_dict_gen_add(Il2CppObject* self, int32_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_add(AsDict(self), key, nullptr, value, keyKind, valueKind);
}

void icall_dict_gen_set(Il2CppObject* self, int32_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_set(AsDict(self), key, nullptr, value, keyKind, valueKind);
}

Il2CppObject* icall_dict_gen_get(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, &value, keyKind, valueKind))
    {
        return value;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return nullptr; // unreachable (the factory throws)
}

bool icall_dict_gen_try_get_value(Il2CppObject* self, int32_t key, Il2CppObject** value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_try_get_value(AsDict(self), key, nullptr, value, keyKind, valueKind);
}

bool icall_dict_gen_contains_key(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_contains_key(AsDict(self), key, nullptr, keyKind, valueKind);
}

bool icall_dict_gen_remove(Il2CppObject* self, int32_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_remove(AsDict(self), key, nullptr, keyKind, valueKind);
}

int32_t icall_dict_gen_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_get_count(AsDict(self));
}

void icall_dict_gen_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_clear(AsDict(self));
}

//-----------------------------------------------------------------------------
// Ref-key twins (Phase-2): one twin set for every reference-key pair. Uri
// keys normalize to their string form at the twin entry (the T4 contract),
// then ride the string-kind table path; null keys raise ANE before any
// normalization. Miss contracts mirror the dict_ss arms they absorb:
// get_item -> KeyNotFoundException; try_get_value -> null out + false;
// remove-missing -> false.
//-----------------------------------------------------------------------------
namespace {

int32_t TwinKind(int32_t keyKind)
{
    return keyKind == kGenKind_Uri ? kGenKind_String : keyKind;
}

Il2CppObject* TwinKey(Il2CppObject* key, int32_t keyKind)
{
    // Null bypasses normalization (the ANE below fires first).
    if (keyKind == kGenKind_Uri && key != nullptr)
    {
        return icall_uri_to_string(key);
    }
    return key;
}

} // namespace

void icall_dict_gen_add_o(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    il2cpp_dict_gen_add(AsDict(self), 0, norm, value, TwinKind(keyKind), valueKind);
}

void icall_dict_gen_set_o(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    il2cpp_dict_gen_set(AsDict(self), 0, norm, value, TwinKind(keyKind), valueKind);
}

Il2CppObject* icall_dict_gen_get_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value(AsDict(self), 0, norm, &value, TwinKind(keyKind), valueKind))
    {
        return value;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return nullptr; // unreachable (the factory throws)
}

bool icall_dict_gen_try_get_value_o(Il2CppObject* self, Il2CppObject* key, Il2CppObject** value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    if (il2cpp_dict_gen_try_get_value(AsDict(self), 0, norm, value, TwinKind(keyKind), valueKind))
    {
        return true;
    }
    if (value != nullptr)
    {
        *value = nullptr; // net48: out is default(TValue) on a miss
    }
    return false;
}

bool icall_dict_gen_contains_key_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    return il2cpp_dict_gen_contains_key(AsDict(self), 0, norm, TwinKind(keyKind), valueKind);
}

bool icall_dict_gen_remove_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    return il2cpp_dict_gen_remove(AsDict(self), 0, norm, TwinKind(keyKind), valueKind);
}

Il2CppArray* icall_dict_gen_get_keys_o(Il2CppObject* self, int32_t keyKind)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_get_keys(AsDict(self), TwinKind(keyKind));
}

Il2CppArray* icall_dict_gen_get_values_o(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_get_values(AsDict(self));
}

// b5-gaps section 108: the INT64-KEY instance twins. The key arrives as an int64
// VALUE slot; the value carrier is the boxed int32/int64/reference form the
// generic table already stores.
Il2CppObject* icall_dict_gen_get_kl(Il2CppObject* self, int64_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value_kl(AsDict(self), key, &value,
                                         keyKind, valueKind))
    {
        return value;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return nullptr; // unreachable (the factory throws)
}

// b5-gaps section 108: the INT64-KEY + INT64-VALUE twin (Dictionary<long,long>).
// The key arrives as an int64 VALUE slot and rides the parallel keyI64 scan;
// the value carrier is a BOXED int64 (the same 8-byte payload as the int64-value
// form), so the result is unboxed back to the int64 leaf.
int64_t icall_dict_gen_get_kl_l(Il2CppObject* self, int64_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value_kl(AsDict(self), key, &value,
                                         keyKind, valueKind))
    {
        return value != nullptr ? runtime::icall_unbox_long(value) : 0;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0; // unreachable (the factory throws)
}

void icall_dict_gen_add_kl_l(Il2CppObject* self, int64_t key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_add_kl(AsDict(self), key, runtime::icall_box_long(value),
                           keyKind, valueKind);
}

void icall_dict_gen_set_kl_l(Il2CppObject* self, int64_t key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_set_kl(AsDict(self), key, runtime::icall_box_long(value),
                           keyKind, valueKind);
}

bool icall_dict_gen_try_get_value_kl_l(Il2CppObject* self, int64_t key,
    int64_t* value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = nullptr;
    if (!il2cpp_dict_gen_try_get_value_kl(AsDict(self), key, &boxed,
                                          keyKind, valueKind))
    {
        if (value != nullptr)
        {
            *value = 0; // net48: out is default(TValue) on a miss
        }
        return false;
    }
    if (value != nullptr)
    {
        *value = boxed != nullptr ? runtime::icall_unbox_long(boxed) : 0;
    }
    return true;
}

// b5-gaps section 108: the INT64-KEY + INT32-VALUE twin (Dictionary<long,int>).
// The key rides the parallel keyI64 scan; the value carrier is a BOXED int32.
int32_t icall_dict_gen_get_kl_i(Il2CppObject* self, int64_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value_kl(AsDict(self), key, &value,
                                         keyKind, valueKind))
    {
        return value != nullptr ? il2cpp_dict_gen_unbox_int32(value) : 0;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0; // unreachable (the factory throws)
}

void icall_dict_gen_add_kl_i(Il2CppObject* self, int64_t key, int32_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_add_kl(AsDict(self), key, il2cpp_dict_gen_box_int32(value),
                           keyKind, valueKind);
}

void icall_dict_gen_set_kl_i(Il2CppObject* self, int64_t key, int32_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_set_kl(AsDict(self), key, il2cpp_dict_gen_box_int32(value),
                           keyKind, valueKind);
}

bool icall_dict_gen_try_get_value_kl_i(Il2CppObject* self, int64_t key,
    int32_t* value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = nullptr;
    if (!il2cpp_dict_gen_try_get_value_kl(AsDict(self), key, &boxed,
                                          keyKind, valueKind))
    {
        if (value != nullptr)
        {
            *value = 0; // net48: out is default(TValue) on a miss
        }
        return false;
    }
    if (value != nullptr)
    {
        *value = boxed != nullptr ? il2cpp_dict_gen_unbox_int32(boxed) : 0;
    }
    return true;
}

void icall_dict_gen_add_kl(Il2CppObject* self, int64_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_add_kl(AsDict(self), key, value, keyKind, valueKind);
}

void icall_dict_gen_set_kl(Il2CppObject* self, int64_t key, Il2CppObject* value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    il2cpp_dict_gen_set_kl(AsDict(self), key, value, keyKind, valueKind);
}

bool icall_dict_gen_try_get_value_kl(Il2CppObject* self, int64_t key,
    Il2CppObject** value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (il2cpp_dict_gen_try_get_value_kl(AsDict(self), key, value,
                                         keyKind, valueKind))
    {
        return true;
    }
    if (value != nullptr)
    {
        *value = nullptr; // net48: out is default(TValue) on a miss
    }
    return false;
}

bool icall_dict_gen_contains_key_kl(Il2CppObject* self, int64_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_contains_key_kl(AsDict(self), key, keyKind, valueKind);
}

bool icall_dict_gen_remove_kl(Il2CppObject* self, int64_t key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_remove_kl(AsDict(self), key, keyKind, valueKind);
}

Il2CppArray* icall_dict_gen_get_keys_kl(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_dict_gen_get_keys_kl(AsDict(self));
}

// b5-gaps section 106: the STRING-KEY int64-value twins (the LINQ
// ToDictionary string->int64 shape). TwinKey/TwinKind are declared by the
// anonymous namespace ABOVE this point in the TU (the gen o-form precedent).
int64_t icall_dict_gen_get_l_o(Il2CppObject* self, Il2CppObject* key,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    Il2CppObject* value = nullptr;
    if (il2cpp_dict_gen_try_get_value(AsDict(self), 0, norm, &value,
                                      TwinKind(keyKind), valueKind))
    {
        return value != nullptr ? runtime::icall_unbox_long(value) : 0;
    }
    il2cpp_throw_key_not_found_exception(nullptr);
    return 0; // unreachable (the factory throws)
}

void icall_dict_gen_add_l_o(Il2CppObject* self, Il2CppObject* key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    il2cpp_dict_gen_add(AsDict(self), 0, norm, runtime::icall_box_long(value),
                        TwinKind(keyKind), valueKind);
}

void icall_dict_gen_set_l_o(Il2CppObject* self, Il2CppObject* key, int64_t value,
    int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    il2cpp_dict_gen_set(AsDict(self), 0, norm, runtime::icall_box_long(value),
                        TwinKind(keyKind), valueKind);
}

bool icall_dict_gen_try_get_value_l_o(Il2CppObject* self, Il2CppObject* key,
    int64_t* value, int32_t keyKind, int32_t valueKind)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppObject* norm = TwinKey(key, keyKind);
    Il2CppObject* boxed = nullptr;
    if (!il2cpp_dict_gen_try_get_value(AsDict(self), 0, norm, &boxed,
                                       TwinKind(keyKind), valueKind))
    {
        if (value != nullptr)
        {
            *value = 0; // net48: out is default(TValue) on a miss
        }
        return false;
    }
    if (value != nullptr)
    {
        *value = boxed != nullptr ? runtime::icall_unbox_long(boxed) : 0;
    }
    return true;
}

}} // namespace il2cpp::runtime

namespace il2cpp {

Il2CppObject* il2cpp_dict_gen_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureDictGenRegistered();
    GcRoot<Il2CppObject*> root(self, "il2cpp_dict_gen_get_enumerator");
    Il2CppDictGen* d = AsDict(self);
    Il2CppArray* keys = il2cpp_dict_gen_get_keys(d, kGenKind_Int32);
    GcRoot<Il2CppArray*> keysRoot(keys, "dict_gen_enumerator_keys");
    self = root.Get();
    d = AsDict(self);
    Il2CppArray* values = il2cpp_dict_gen_get_values(d);
    GcRoot<Il2CppArray*> valuesRoot(values, "dict_gen_enumerator_values");
    Il2CppDictGenEnumerator* en = reinterpret_cast<Il2CppDictGenEnumerator*>(
        il2cpp_object_new(&System_Collections_Generic_Dictionary_2_Enumerator_gen_int_string_class));
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

int32_t il2cpp_dict_gen_enumerator_move_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictGenEnumerator* en = reinterpret_cast<Il2CppDictGenEnumerator*>(self);
    ++en->index;
    return en->index < EnumeratorLength(en) ? 1 : 0;
}

Il2CppObject* il2cpp_dict_gen_enumerator_get_current(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppDictGenEnumerator* en = reinterpret_cast<Il2CppDictGenEnumerator*>(self);
    const int32_t i = en->index;
    // net48: before the first MoveNext Current is default(KeyValuePair) --
    // Key = 0, Value = null -- NOT an exception (struct enumerator semantics).
    if (i < 0 || i >= EnumeratorLength(en))
    {
        Il2CppDictKvpGenIS* dflt = reinterpret_cast<Il2CppDictKvpGenIS*>(
            il2cpp_object_new(&System_Collections_Generic_KeyValuePair_2_gen_int_string_class));
        dflt->key = 0;
        dflt->value = nullptr;
        return reinterpret_cast<Il2CppObject*>(dflt);
    }
    const int32_t* keys = reinterpret_cast<const int32_t*>(en->keys->data);
    Il2CppObject* const* values = reinterpret_cast<Il2CppObject* const*>(en->values->data);
    Il2CppDictKvpGenIS* kvp = reinterpret_cast<Il2CppDictKvpGenIS*>(
        il2cpp_object_new(&System_Collections_Generic_KeyValuePair_2_gen_int_string_class));
    kvp->key = keys[i];
    kvp->value = reinterpret_cast<Il2CppString*>(values[i]);
    il2cpp_write_barrier(reinterpret_cast<void**>(&kvp->value),
                         reinterpret_cast<Il2CppObject*>(kvp->value));
    return reinterpret_cast<Il2CppObject*>(kvp);
}

void il2cpp_dict_gen_enumerator_dispose(Il2CppObject* self)
{
    // no-op (the enumerator holds snapshots; nothing to release).
    (void)self;
}

int32_t il2cpp_kvp_gen_int_string_get_key(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppDictKvpGenIS*>(self)->key;
}

Il2CppObject* il2cpp_kvp_gen_int_string_get_value(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(
        reinterpret_cast<Il2CppDictKvpGenIS*>(self)->value);
}

} // namespace il2cpp

