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
    if (keyKind != kGenKind_Int32)
    {
        ThrowIfNullCarrier(keyRef);
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
    if (keyKind != kGenKind_Int32)
    {
        ThrowIfNullCarrier(keyRef);
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
    if (keyKind != kGenKind_Int32)
    {
        ThrowIfNullCarrier(keyRef);
    }
    const int32_t i = FindEntryIndex(d, keyInt, keyRef, keyKind,
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
    if (keyKind != kGenKind_Int32)
    {
        ThrowIfNullCarrier(keyRef);
    }
    return FindEntryIndex(d, keyInt, keyRef, keyKind,
        IncomingHash(keyInt, keyRef, keyKind)) >= 0;
}

bool il2cpp_dict_gen_remove(Il2CppDictGen* d, int32_t keyInt, Il2CppObject* keyRef,
    int32_t keyKind, int32_t valueKind)
{
    (void)valueKind;
    if (keyKind != kGenKind_Int32)
    {
        ThrowIfNullCarrier(keyRef);
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

