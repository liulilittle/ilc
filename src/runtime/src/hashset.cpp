//------------------------------------------------------------------------------
// hashset.cpp -- System.Collections.Generic.HashSet`1 runtime domain (see
// hashset.h for the model, the GC contract and the layout).
//
// This file replaces the historical SPLIT storage (HashSet<string> over dictsi,
// HashSet<int/long/...> over the boxed non-generic hashtable). Both of those
// enumerated in BUCKET order, while net48 enumerates its SLOT array in
// insertion order with LIFO free-slot reuse -- probe hseta1 (net48 oracle):
//     add 3,1,2 / remove 2 / add 7,2,5  ->  3,1,7,2,5
//     the old runtime printed               1,2,3,5,7
// The slot model below reproduces the oracle exactly (probe hsetmodel pins the
// reuse discipline: add 0..5, remove 2,4 -> 0,1,3,5; add 99 -> 0,1,3,99,5;
// add 98 -> 0,1,98,3,99,5).
//
// MEMBERSHIP uses the runtime's universal value semantics
// (icall_object_get_hash_code / icall_object_equals), which implement the
// net48 EqualityComparer<T>.Default contract per type (string content,
// boxed-primitive value, decimal/DateTime/TimeSpan/Guid value, reflected user
// override). A CUSTOM IEqualityComparer<T> (ctor overload) replaces both by
// dispatching the comparer's reflected GetHashCode(T) / Equals(T,T) slots.
//------------------------------------------------------------------------------

#include "hashset.h"

#include "arraysort.h"          // il2cpp_ienum_* + FindVirtualSlotByNameAndSignature
#include "decimal.h"            // Il2CppDecimal
#include "dictsi_obj.h"         // the shared keycollection enumerator object
#include "il2cpp_boxing_ext.h"  // runtime::icall_box_int32/unbox helpers
#include "il2cpp_boxing_ext2.h" // runtime::icall_box_long/box_uint/box_decimal
#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_delegate.h"    // Il2CppDelegate (RemoveWhere predicate ABI)
#include "il2cpp_nullable.h"    // icall_box_date_time / time_span / guid
#include "il2cpp_string.h"
#include "il2cpp_value_semantics.h" // icall_object_get_hash_code / icall_object_equals (runtime)
#include "string_compare.h"     // il2cpp_string_compare_ordinal(_ignore_case)

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

namespace il2cpp {

// The universal value semantics live in il2cpp::runtime (il2cpp_value_semantics.h);
// the set code below is in il2cpp, so bind them once here.
using runtime::icall_object_get_hash_code;
using runtime::icall_object_equals;


//------------------------------------------------------------------------------
// Layout pins: the descriptor's refFieldBitmap below is a literal; these
// compile-time checks keep it honest against the struct.
//------------------------------------------------------------------------------
static_assert(offsetof(Il2CppHashSetObject, keys) == 1 * sizeof(void*), "hashset: keys at slot 1");
static_assert(offsetof(Il2CppHashSetObject, hashes) == 2 * sizeof(void*), "hashset: hashes at slot 2");
static_assert(offsetof(Il2CppHashSetObject, nexts) == 3 * sizeof(void*), "hashset: nexts at slot 3");
static_assert(offsetof(Il2CppHashSetObject, buckets) == 4 * sizeof(void*), "hashset: buckets at slot 4");
static_assert(offsetof(Il2CppHashSetObject, comparer) == 5 * sizeof(void*), "hashset: comparer at slot 5");

//------------------------------------------------------------------------------
// The descriptor. ONE descriptor serves every element instantiation (the
// element family rides the icall, not the class), mirroring the hashtable /
// listobj precedent. refFieldBitmap marks slots 1..4 (keys/hashes/nexts/
// buckets) and slot 10 (comparer) -- every stored member is rooted by the set.
//------------------------------------------------------------------------------
Il2CppClass System_Collections_Generic_HashSet_1_string_class = {
    "System.Collections.Generic.HashSet`1", // name
    static_cast<int32_t>(sizeof(Il2CppHashSetObject)), // instanceSize
    0b111110u,                  // refFieldBitmap: bits 1,2,3,4,5 (comparer is a ref)
    5,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

namespace {

bool g_hashSetRegistered = false;

void EnsureHashSetRegistered()
{
    if (g_hashSetRegistered) return;
    il2cpp_class_register(&System_Collections_Generic_HashSet_1_string_class);
    g_hashSetRegistered = true;
}

constexpr int32_t kMinSlots = 4;
constexpr int32_t kMaxCount = 1 << 30;
constexpr int32_t kFreeHash = -1;

Il2CppHashSetObject* AsSet(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppHashSetObject*>(self);
}

void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

int32_t RoundUpPow2(int32_t capacity)
{
    int32_t n = capacity < kMinSlots ? kMinSlots : capacity;
    if (n > kMaxCount) n = kMaxCount;
    int32_t p = 1;
    while (p < n) p <<= 1;
    return p;
}

int32_t* I4Slots(Il2CppArray* a)
{
    return reinterpret_cast<int32_t*>(a->data);
}

Il2CppObject** RefSlots(Il2CppArray* a)
{
    return reinterpret_cast<Il2CppObject**>(a->data);
}

Il2CppArray* NewI4(int32_t length)
{
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(int32_t)), false, length);
}

Il2CppArray* NewRef(int32_t length)
{
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, length);
}

//------------------------------------------------------------------------------
// The comparer dispatch. keyKind selects the reflected signature family:
//   0 = reference carrier (string/object/class)  -> GetHashCode(T) / Equals(T,T)
//                                                   with a reference parameter
//   1 = int32 carrier (int/uint/bool/char/...)   -> int32 parameter slots
// The comparer may also be a StringComparer singleton (no typeinfo) -- those
// two are recognized by their descriptor identity and served locally.
//------------------------------------------------------------------------------
bool IsStringComparerSingleton(const Il2CppObject* c, bool* ignoreCase)
{
    if (c == nullptr || c->klass == nullptr || c->klass->name == nullptr) return false;
    if (std::strcmp(c->klass->name, "System.StringComparer.Ordinal") == 0)
    {
        *ignoreCase = false;
        return true;
    }
    if (std::strcmp(c->klass->name, "System.StringComparer.OrdinalIgnoreCase") == 0)
    {
        *ignoreCase = true;
        return true;
    }
    return false;
}

// Ordinal-ignore-case content hash (only "equal keys hash equal" matters: the
// raw value is never observable).
int32_t HashStringIgnoreCase(const Il2CppString* s)
{
    uint32_t h = 2166136261u;
    for (int32_t i = 0; i < s->length; ++i)
    {
        uint32_t c = static_cast<uint16_t>(s->chars[i]);
        if (c >= 'a' && c <= 'z') c -= 32;
        h ^= c;
        h *= 16777619u;
    }
    return static_cast<int32_t>(h) & 0x7FFFFFFF;
}

int32_t ComparerHash(Il2CppObject* comparer, Il2CppObject* carrier, int32_t keyKind)
{
    bool ignoreCase = false;
    if (IsStringComparerSingleton(comparer, &ignoreCase))
    {
        const Il2CppString* s = reinterpret_cast<const Il2CppString*>(carrier);
        if (ignoreCase) return HashStringIgnoreCase(s);
        return icall_object_get_hash_code(carrier) & 0x7FFFFFFF;
    }
    if (keyKind == 1)
    {
        const int32_t slot = FindVirtualSlotByNameAndSignature(comparer, "GetHashCode", "(i)");
        if (slot >= 0 && comparer->klass != nullptr && comparer->klass->vtable != nullptr &&
            slot < comparer->klass->vtableSize && comparer->klass->vtable[slot] != nullptr)
        {
            using HashFn = int32_t (*)(Il2CppObject*, int32_t);
            const int32_t v = il2cpp_unbox_int32(carrier);
            return reinterpret_cast<HashFn>(comparer->klass->vtable[slot])(comparer, v) & 0x7FFFFFFF;
        }
    }
    else
    {
        const int32_t slot = FindVirtualSlotByNameAndSignature(comparer, "GetHashCode", "(r:");
        if (slot >= 0 && comparer->klass != nullptr && comparer->klass->vtable != nullptr &&
            slot < comparer->klass->vtableSize && comparer->klass->vtable[slot] != nullptr)
        {
            using HashFn = int32_t (*)(Il2CppObject*, Il2CppObject*);
            return reinterpret_cast<HashFn>(comparer->klass->vtable[slot])(comparer, carrier) & 0x7FFFFFFF;
        }
    }
    // No reflected GetHashCode(T): fall back to the universal value hash (the
    // comparer then only customizes equality -- never a silent wrong bucket,
    // since equality remains the comparer's).
    return icall_object_get_hash_code(carrier) & 0x7FFFFFFF;
}

bool ComparerEquals(Il2CppObject* comparer, Il2CppObject* a, Il2CppObject* b, int32_t keyKind)
{
    bool ignoreCase = false;
    if (IsStringComparerSingleton(comparer, &ignoreCase))
    {
        const Il2CppString* sa = reinterpret_cast<const Il2CppString*>(a);
        const Il2CppString* sb = reinterpret_cast<const Il2CppString*>(b);
        return ignoreCase
            ? il2cpp_string_compare_ordinal_ignore_case(sa, sb) == 0
            : il2cpp_string_compare_ordinal(sa, sb) == 0;
    }
    if (keyKind == 1)
    {
        const int32_t slot = FindVirtualSlotByNameAndSignature(comparer, "Equals", "(i,i)");
        if (slot >= 0 && comparer->klass != nullptr && comparer->klass->vtable != nullptr &&
            slot < comparer->klass->vtableSize && comparer->klass->vtable[slot] != nullptr)
        {
            using EqFn = bool (*)(Il2CppObject*, int32_t, int32_t);
            return reinterpret_cast<EqFn>(comparer->klass->vtable[slot])(
                comparer, il2cpp_unbox_int32(a), il2cpp_unbox_int32(b));
        }
    }
    else
    {
        const int32_t slot = FindVirtualSlotByNameAndSignature(comparer, "Equals", "(r:");
        if (slot >= 0 && comparer->klass != nullptr && comparer->klass->vtable != nullptr &&
            slot < comparer->klass->vtableSize && comparer->klass->vtable[slot] != nullptr)
        {
            using EqFn = bool (*)(Il2CppObject*, Il2CppObject*, Il2CppObject*);
            return reinterpret_cast<EqFn>(comparer->klass->vtable[slot])(comparer, a, b);
        }
    }
    return icall_object_equals(a, b);
}

int32_t SetHash(Il2CppHashSetObject* s, Il2CppObject* carrier)
{
    if (s->comparer != nullptr)
    {
        return ComparerHash(s->comparer, carrier, s->keyKind);
    }
    return icall_object_get_hash_code(carrier) & 0x7FFFFFFF;
}

bool SetEquals(Il2CppHashSetObject* s, Il2CppObject* a, Il2CppObject* b)
{
    if (a == b) return true;
    if (s->comparer != nullptr)
    {
        return ComparerEquals(s->comparer, a, b, s->keyKind);
    }
    return icall_object_equals(a, b);
}

//------------------------------------------------------------------------------
// Slot table primitives. Every allocation can compact the heap, so callers
// root 'self' and re-derive the set pointer afterwards.
//------------------------------------------------------------------------------
int32_t LiveCount(Il2CppHashSetObject* s)
{
    return s->count - s->freeCount;
}

int32_t BucketOf(int32_t hash, int32_t bucketCount)
{
    return static_cast<int32_t>(static_cast<uint32_t>(hash) & static_cast<uint32_t>(bucketCount - 1));
}

int32_t FindSlot(Il2CppHashSetObject* s, Il2CppObject* carrier, int32_t hash)
{
    if (s->buckets == nullptr) return -1;
    // SetEquals may dispatch a USER comparer, which can allocate and therefore
    // move the managed arrays; every pointer is re-derived per iteration.
    const int32_t bucket = BucketOf(hash, s->bucketCount);
    int32_t i = I4Slots(s->buckets)[bucket];
    while (i >= 0)
    {
        if (I4Slots(s->hashes)[i] == hash && SetEquals(s, RefSlots(s->keys)[i], carrier))
        {
            return i;
        }
        i = I4Slots(s->nexts)[i];
    }
    return -1;
}

void GrowSlots(Il2CppObject** selfSlot, int32_t needed)
{
    Il2CppHashSetObject* s = AsSet(*selfSlot);
    if (needed <= s->entryCapacity) return;
    int32_t newCap = s->entryCapacity > 0 ? s->entryCapacity : kMinSlots;
    while (newCap < needed) newCap <<= 1;

    Il2CppArray* newKeys = NewRef(newCap);
    Il2CppObject* newKeysObj = reinterpret_cast<Il2CppObject*>(newKeys);
    GcRoot<Il2CppObject*> keysRoot(newKeysObj, "hashset_grow_keys");
    Il2CppArray* newHashes = NewI4(newCap);
    Il2CppObject* newHashesObj = reinterpret_cast<Il2CppObject*>(newHashes);
    GcRoot<Il2CppObject*> hashesRoot(newHashesObj, "hashset_grow_hashes");
    Il2CppArray* newNexts = NewI4(newCap);
    Il2CppObject* newNextsObj = reinterpret_cast<Il2CppObject*>(newNexts);
    GcRoot<Il2CppObject*> nextsRoot(newNextsObj, "hashset_grow_nexts");

    s = AsSet(*selfSlot);
    Il2CppObject** srcKeys = RefSlots(s->keys);
    const int32_t* srcHashes = I4Slots(s->hashes);
    const int32_t* srcNexts = I4Slots(s->nexts);
    Il2CppObject** dstKeys = RefSlots(newKeys);
    int32_t* dstHashes = I4Slots(newHashes);
    int32_t* dstNexts = I4Slots(newNexts);
    for (int32_t i = 0; i < s->count; ++i)
    {
        dstKeys[i] = srcKeys[i];
        dstHashes[i] = srcHashes[i];
        dstNexts[i] = srcNexts[i];
    }
    s->keys = newKeys;
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->keys), reinterpret_cast<Il2CppObject*>(newKeys));
    s->hashes = newHashes;
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->hashes), reinterpret_cast<Il2CppObject*>(newHashes));
    s->nexts = newNexts;
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->nexts), reinterpret_cast<Il2CppObject*>(newNexts));
    s->entryCapacity = newCap;
}

void ResizeBuckets(Il2CppObject** selfSlot)
{
    Il2CppHashSetObject* s = AsSet(*selfSlot);
    const int32_t oldCount = s->bucketCount;
    int32_t newCount = oldCount * 2;
    if (newCount < oldCount || newCount > kMaxCount) return;

    Il2CppArray* newBuckets = NewI4(newCount);
    Il2CppObject* newBucketsObj = reinterpret_cast<Il2CppObject*>(newBuckets);
    GcRoot<Il2CppObject*> bucketsRoot(newBucketsObj, "hashset_resize_buckets");
    s = AsSet(*selfSlot);
    int32_t* dst = I4Slots(newBuckets);
    for (int32_t i = 0; i < newCount; ++i) dst[i] = -1;
    const int32_t* hashes = I4Slots(s->hashes);
    int32_t* nexts = I4Slots(s->nexts);
    for (int32_t i = 0; i < s->count; ++i)
    {
        if (hashes[i] < 0) continue;
        const int32_t b = BucketOf(hashes[i], newCount);
        nexts[i] = dst[b];
        dst[b] = i;
    }
    s->buckets = newBuckets;
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->buckets), reinterpret_cast<Il2CppObject*>(newBuckets));
    s->bucketCount = newCount;
}

bool NeedsResize(Il2CppHashSetObject* s)
{
    const int64_t occupiedAfter = static_cast<int64_t>(LiveCount(s)) + 1;
    return occupiedAfter * 100 > static_cast<int64_t>(s->bucketCount) * 72;
}

int32_t AcquireSlot(Il2CppObject** selfSlot)
{
    Il2CppHashSetObject* s = AsSet(*selfSlot);
    if (s->freeCount > 0)
    {
        const int32_t slot = s->freeList;
        s->freeList = I4Slots(s->nexts)[slot];
        --s->freeCount;
        return slot;
    }
    if (s->count >= s->entryCapacity)
    {
        GrowSlots(selfSlot, s->count + 1);
        s = AsSet(*selfSlot);
    }
    return s->count++;
}

// Insert a carrier that is KNOWN absent.
void LinkNew(Il2CppObject** selfSlot, Il2CppObject* carrier, int32_t hash)
{
    Il2CppHashSetObject* s = AsSet(*selfSlot);
    const int32_t slot = AcquireSlot(selfSlot);
    s = AsSet(*selfSlot);
    RefSlots(s->keys)[slot] = carrier;
    il2cpp_write_barrier(reinterpret_cast<void**>(&RefSlots(s->keys)[slot]), carrier);
    I4Slots(s->hashes)[slot] = hash;
    const int32_t b = BucketOf(hash, s->bucketCount);
    I4Slots(s->nexts)[slot] = I4Slots(s->buckets)[b];
    I4Slots(s->buckets)[b] = slot;
}

bool AddCarrier(Il2CppObject** selfSlot, Il2CppObject* carrier)
{
    // The carrier is a MANAGED reference: the resize/grow below may compact the
    // heap, so it is rooted and re-read after every allocation.
    Il2CppObject* carrierVar = carrier;
    GcRoot<Il2CppObject*> carrierRoot(carrierVar, "hashset_add_carrier");
    Il2CppHashSetObject* s = AsSet(*selfSlot);
    const int32_t hash = SetHash(s, carrierRoot.Get());
    if (FindSlot(s, carrierRoot.Get(), hash) >= 0) return false;
    if (NeedsResize(s))
    {
        ResizeBuckets(selfSlot);
        s = AsSet(*selfSlot);
    }
    LinkNew(selfSlot, carrierRoot.Get(), hash);
    return true;
}

bool RemoveCarrier(Il2CppObject** selfSlot, Il2CppObject* carrier)
{
    Il2CppHashSetObject* s = AsSet(*selfSlot);
    const int32_t hash = SetHash(s, carrier);
    const int32_t b = BucketOf(hash, s->bucketCount);
    // SetEquals may dispatch a user comparer that allocates; re-derive the
    // array pointers on every iteration.
    int32_t prev = -1;
    int32_t i = I4Slots(s->buckets)[b];
    while (i >= 0)
    {
        if (I4Slots(s->hashes)[i] == hash && SetEquals(s, RefSlots(s->keys)[i], carrier))
        {
            if (prev < 0) I4Slots(s->buckets)[b] = I4Slots(s->nexts)[i];
            else I4Slots(s->nexts)[prev] = I4Slots(s->nexts)[i];
            I4Slots(s->hashes)[i] = kFreeHash;
            RefSlots(s->keys)[i] = nullptr;
            il2cpp_write_barrier(reinterpret_cast<void**>(&RefSlots(s->keys)[i]), nullptr);
            I4Slots(s->nexts)[i] = s->freeList;
            s->freeList = i;
            ++s->freeCount;
            return true;
        }
        prev = i;
        i = I4Slots(s->nexts)[i];
    }
    return false;
}

bool ContainsCarrier(Il2CppHashSetObject* s, Il2CppObject* carrier)
{
    return FindSlot(s, carrier, SetHash(s, carrier)) >= 0;
}

// The stored member equal to 'carrier' (TryGetValue), or nullptr on a miss.
Il2CppObject* FindCarrier(Il2CppHashSetObject* s, Il2CppObject* carrier)
{
    const int32_t slot = FindSlot(s, carrier, SetHash(s, carrier));
    return slot < 0 ? nullptr : RefSlots(s->keys)[slot];
}

void ClearAll(Il2CppHashSetObject* s)
{
    if (s->buckets != nullptr)
    {
        int32_t* buckets = I4Slots(s->buckets);
        for (int32_t i = 0; i < s->bucketCount; ++i) buckets[i] = -1;
    }
    if (s->keys != nullptr)
    {
        Il2CppObject** keys = RefSlots(s->keys);
        int32_t* hashes = I4Slots(s->hashes);
        for (int32_t i = 0; i < s->count; ++i)
        {
            hashes[i] = kFreeHash;
            keys[i] = nullptr;
            il2cpp_write_barrier(reinterpret_cast<void**>(&keys[i]), nullptr);
        }
    }
    s->count = 0;
    s->freeList = -1;
    s->freeCount = 0;
}

// The net48 enumeration snapshot: live carriers in SLOT order (insertion order
// with free slots skipped). The caller roots the result.
Il2CppArray* SnapshotCarriers(Il2CppObject** selfSlot)
{
    Il2CppHashSetObject* s = AsSet(*selfSlot);
    const int32_t live = LiveCount(s);
    Il2CppArray* result = NewRef(live);
    Il2CppObject* resultObj = reinterpret_cast<Il2CppObject*>(result);
    GcRoot<Il2CppObject*> resultRoot(resultObj, "hashset_snapshot");
    s = AsSet(*selfSlot);
    Il2CppObject** dst = RefSlots(result);
    Il2CppObject** src = RefSlots(s->keys);
    const int32_t* hashes = I4Slots(s->hashes);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[n] = src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[n]), src[i]);
        ++n;
    }
    return result;
}

void CtorImpl(Il2CppObject* self, int32_t capacity, Il2CppObject* comparer, int32_t keyKind, int32_t family)
{
    ThrowIfNullReceiver(self);
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity", nullptr);
    }
    EnsureHashSetRegistered();
    Il2CppObject* selfRoot = self;
    GcRoot<Il2CppObject*> root(selfRoot, "hashset_ctor_self");

    Il2CppHashSetObject* s = AsSet(selfRoot);
    s->bucketCount = RoundUpPow2(capacity);
    s->count = 0;
    s->freeList = -1;
    s->freeCount = 0;
    s->keyKind = keyKind;
    s->family = family;
    s->comparer = comparer;

    const int32_t slots = capacity > kMinSlots ? RoundUpPow2(capacity) : kMinSlots;
    s->entryCapacity = slots;
    s->keys = NewRef(slots);
    s = AsSet(root.Get());
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->keys), reinterpret_cast<Il2CppObject*>(s->keys));
    s->hashes = NewI4(slots);
    s = AsSet(root.Get());
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->hashes), reinterpret_cast<Il2CppObject*>(s->hashes));
    s->nexts = NewI4(slots);
    s = AsSet(root.Get());
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->nexts), reinterpret_cast<Il2CppObject*>(s->nexts));
    s->buckets = NewI4(s->bucketCount);
    s = AsSet(root.Get());
    il2cpp_write_barrier(reinterpret_cast<void**>(&s->buckets), reinterpret_cast<Il2CppObject*>(s->buckets));

    int32_t* hashes = I4Slots(s->hashes);
    for (int32_t i = 0; i < s->entryCapacity; ++i) hashes[i] = kFreeHash;
    int32_t* buckets = I4Slots(s->buckets);
    for (int32_t i = 0; i < s->bucketCount; ++i) buckets[i] = -1;
}

void CopyToImpl(Il2CppHashSetObject* s, Il2CppArray* array, int32_t arrayIndex, int32_t index, int32_t count)
{
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    if (index < 0 || count < 0 || index > LiveCount(s) - count)
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    if (static_cast<int64_t>(arrayIndex) + count > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");

    Il2CppObject** keys = RefSlots(s->keys);
    const int32_t* hashes = I4Slots(s->hashes);
    int32_t seen = 0;
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (seen++ < index) continue;
        // The element write is family-specific; the per-kind callers below
        // pass their own sink, so this generic form writes CARRIERS (used by
        // the reference families).
        reinterpret_cast<Il2CppObject**>(array->data)[arrayIndex + n] = keys[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&reinterpret_cast<Il2CppObject**>(array->data)[arrayIndex + n]),
                             keys[i]);
        ++n;
    }
}

void CopyToI4(Il2CppHashSetObject* s, Il2CppArray* array, int32_t arrayIndex)
{
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    int32_t* dst = reinterpret_cast<int32_t*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = il2cpp_unbox_int32(keys[i]);
        ++n;
    }
}

//------------------------------------------------------------------------------
// Operand materialisation for the set algebra: turn ANY IEnumerable<T> operand
// into a list of CARRIERS in the receiver's element family. The operand may be
//   * another HashSet (our own descriptor)  -> slot snapshot (no enumeration)
//   * an int32[] (the common literal/array operand) -> element reads
//   * anything else -> the shared IEnumerable<T> surface (il2cpp_ienum_*)
// 'family' is the RECEIVER's family: 0 ref, 1 int32, 2 int64, 3 double,
// 4 uint, 5 decimal.
//------------------------------------------------------------------------------
enum class Family { Ref = 0, I4 = 1, I8 = 2, D = 3, U4 = 4, Dec = 5 };

// Read the CURRENT element of an enumerator in the receiver's family.
Il2CppObject* CurrentAsCarrier(Family f, Il2CppObject* en)
{
    switch (f)
    {
        case Family::I4: return il2cpp_box_int32(nullptr, il2cpp_ienum_get_current_i4(en));
        case Family::I8: return runtime::icall_box_long(il2cpp_ienum_get_current_i8(en));
        case Family::D: return runtime::icall_box_double(il2cpp_ienum_get_current_d(en));
        case Family::U4: return runtime::icall_box_uint(il2cpp_ienum_get_current_i4(en));
        case Family::Dec:
        {
            runtime::Il2CppDecimal v;
            il2cpp_ienum_get_current_dec(&v, en);
            return runtime::icall_box_decimal(&v);
        }
        default: return il2cpp_ienum_get_current_ref(en);
    }
}

// Collect the operand's carriers into a fresh managed ref array (rooted by the
// caller) and report its length.
Il2CppArray* CollectCarriers(Il2CppObject* other, Family f, int32_t* outLength)
{
    if (other == nullptr) il2cpp_throw_argument_null_exception("other");

    // Our own set: slot snapshot (carriers directly).
    if (other->klass == &System_Collections_Generic_HashSet_1_string_class)
    {
        Il2CppObject* root = other;
        GcRoot<Il2CppObject*> r(root, "hashset_collect_self_operand");
        Il2CppArray* snap = SnapshotCarriers(&root);
        *outLength = snap->length;
        return snap;
    }

    // A raw value array of the receiver's family: direct element reads.
    const bool isArray = other->klass != nullptr && other->klass->name != nullptr &&
                         other->klass->name[0] == '[';
    if (isArray)
    {
        // Both the operand array and the result array are MANAGED: every box
        // allocation below may compact the heap and move them, so both are
        // rooted and every raw pointer is re-derived AFTER each allocation
        // (the historical code cached dst/arr once and crashed on the move).
        Il2CppObject* arrRootVar = other;
        GcRoot<Il2CppObject*> arrRoot(arrRootVar, "hashset_collect_array_operand");
        Il2CppArray* result = NewRef(reinterpret_cast<Il2CppArray*>(arrRoot.Get())->length);
        Il2CppObject* resultObj = reinterpret_cast<Il2CppObject*>(result);
        GcRoot<Il2CppObject*> resultRoot(resultObj, "hashset_collect_array");
        const int32_t length = reinterpret_cast<Il2CppArray*>(arrRoot.Get())->length;
        for (int32_t i = 0; i < length; ++i)
        {
            Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(arrRoot.Get());
            Il2CppObject* carrier;
            switch (f)
            {
                case Family::I4:
                case Family::U4:
                    carrier = il2cpp_box_int32(nullptr, reinterpret_cast<int32_t*>(arr->data)[i]);
                    break;
                case Family::I8:
                    carrier = runtime::icall_box_long(reinterpret_cast<int64_t*>(arr->data)[i]);
                    break;
                case Family::D:
                    carrier = runtime::icall_box_double(reinterpret_cast<double*>(arr->data)[i]);
                    break;
                case Family::Dec:
                    carrier = runtime::icall_box_decimal(&reinterpret_cast<runtime::Il2CppDecimal*>(arr->data)[i]);
                    break;
                default:
                    carrier = reinterpret_cast<Il2CppObject**>(arr->data)[i];
                    break;
            }
            // Re-derive the destination AFTER the (possibly collecting) box
            // allocation.
            Il2CppArray* dstArr = reinterpret_cast<Il2CppArray*>(resultRoot.Get());
            Il2CppObject** dst = RefSlots(dstArr);
            dst[i] = carrier;
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), carrier);
        }
        *outLength = length;
        return reinterpret_cast<Il2CppArray*>(resultRoot.Get());
    }

    // Any other IEnumerable<T>: enumerate through the shared surface. The
    // element count is unknown up front, so collect into a rooted managed
    // ref array that grows (rebuilding on each step is O(n^2) but the set
    // algebra operands are small; the correctness contract matters here).
    Il2CppObject* otherRootVar = other;
    GcRoot<Il2CppObject*> otherRoot(otherRootVar, "hashset_collect_operand");
    Il2CppObject* en = il2cpp_ienum_get_enumerator(otherRoot.Get());
    GcRoot<Il2CppObject*> enRoot(en, "hashset_collect_enumerator");
    std::vector<Il2CppObject*> carriers;
    while (il2cpp_ienum_move_next(enRoot.Get()))
    {
        carriers.push_back(CurrentAsCarrier(f, enRoot.Get()));
    }
    il2cpp_ienum_dispose(enRoot.Get());

    Il2CppArray* result = NewRef(static_cast<int32_t>(carriers.size()));
    Il2CppObject* resultObj = reinterpret_cast<Il2CppObject*>(result);
    GcRoot<Il2CppObject*> resultRoot(resultObj, "hashset_collect_result");
    Il2CppObject** dst = RefSlots(result);
    for (size_t i = 0; i < carriers.size(); ++i)
    {
        dst[i] = carriers[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), carriers[i]);
    }
    *outLength = static_cast<int32_t>(carriers.size());
    return result;
}

// Build a temporary set (same descriptor + comparer) from the operand's
// carriers, so every set operation reduces to carrier-set operations.
Il2CppObject* MakeOperandSet(Il2CppObject* self, Il2CppObject* other, Family f)
{
    Il2CppHashSetObject* s = AsSet(self);
    Il2CppObject* comparer = s->comparer;
    const int32_t keyKind = s->keyKind;

    int32_t n = 0;
    Il2CppArray* carriers = CollectCarriers(other, f, &n);
    Il2CppObject* carriersObj = reinterpret_cast<Il2CppObject*>(carriers);
    GcRoot<Il2CppObject*> carriersRoot(carriersObj, "hashset_operand_carriers");

    Il2CppObject* tmp = il2cpp_object_new(&System_Collections_Generic_HashSet_1_string_class);
    GcRoot<Il2CppObject*> tmpRoot(tmp, "hashset_operand_set");
    CtorImpl(tmpRoot.Get(), n, comparer, keyKind, static_cast<int32_t>(f));
    tmp = tmpRoot.Get();
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* carrier = RefSlots(carriers)[i];
        AddCarrier(&tmp, carrier);
        tmp = tmpRoot.Get();
    }
    return tmp;
}

} // namespace

Il2CppClass* il2cpp_hashset_class()
{
    EnsureHashSetRegistered();
    return &System_Collections_Generic_HashSet_1_string_class;
}

//------------------------------------------------------------------------------
// int32 family.
//------------------------------------------------------------------------------
//------------------------------------------------------------------------------
// The carrier primitives (used by the USER VALUE TYPE template wrappers the
// generated code instantiates, and by the DateTime/TimeSpan/Guid families).
//------------------------------------------------------------------------------
bool icall_hashset_add_carrier(Il2CppObject* self, Il2CppObject* carrier)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_add_carrier_self");
    root = r.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_remove_carrier(Il2CppObject* self, Il2CppObject* carrier)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_remove_carrier_self");
    root = r.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_contains_carrier(Il2CppObject* self, Il2CppObject* carrier)
{
    ThrowIfNullReceiver(self);
    return ContainsCarrier(AsSet(self), carrier);
}

void icall_hashset_ctor_carrier(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::Ref));
}

//------------------------------------------------------------------------------
// DateTime family.
//------------------------------------------------------------------------------
namespace {

Il2CppObject* BoxDateTime(runtime::Il2CppDateTime v)
{
    return icall_box_date_time(v);
}

} // namespace

void icall_hashset_dt_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::Ref));
}

bool icall_hashset_dt_add(Il2CppObject* self, runtime::Il2CppDateTime item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_dt_add_self");
    Il2CppObject* carrier = BoxDateTime(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_dt_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_dt_remove(Il2CppObject* self, runtime::Il2CppDateTime item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_dt_remove_self");
    Il2CppObject* carrier = BoxDateTime(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_dt_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_dt_contains(Il2CppObject* self, runtime::Il2CppDateTime item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_dt_contains_self");
    Il2CppObject* carrier = BoxDateTime(item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_dt_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_dt_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

void icall_hashset_dt_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    runtime::Il2CppDateTime* dst = reinterpret_cast<runtime::Il2CppDateTime*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = icall_unbox_date_time(keys[i]);
        ++n;
    }
}

void icall_hashset_dt_enumerator_get_current(runtime::Il2CppDateTime* result, Il2CppObject* self)
{
    if (result == nullptr) return;
    *result = icall_unbox_date_time(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// TimeSpan family.
//------------------------------------------------------------------------------
void icall_hashset_ts_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::Ref));
}

bool icall_hashset_ts_add(Il2CppObject* self, runtime::Il2CppTimeSpan item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_ts_add_self");
    Il2CppObject* carrier = icall_box_time_span(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_ts_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_ts_remove(Il2CppObject* self, runtime::Il2CppTimeSpan item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_ts_remove_self");
    Il2CppObject* carrier = icall_box_time_span(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_ts_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_ts_contains(Il2CppObject* self, runtime::Il2CppTimeSpan item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_ts_contains_self");
    Il2CppObject* carrier = icall_box_time_span(item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_ts_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_ts_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

void icall_hashset_ts_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    runtime::Il2CppTimeSpan* dst = reinterpret_cast<runtime::Il2CppTimeSpan*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = icall_unbox_time_span(keys[i]);
        ++n;
    }
}

void icall_hashset_ts_enumerator_get_current(runtime::Il2CppTimeSpan* result, Il2CppObject* self)
{
    if (result == nullptr) return;
    *result = icall_unbox_time_span(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// Guid family.
//------------------------------------------------------------------------------
void icall_hashset_guid_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::Ref));
}

bool icall_hashset_guid_add(Il2CppObject* self, const Il2CppGuid* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_guid_add_self");
    Il2CppObject* carrier = icall_box_guid(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_guid_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_guid_remove(Il2CppObject* self, const Il2CppGuid* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_guid_remove_self");
    Il2CppObject* carrier = icall_box_guid(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_guid_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_guid_contains(Il2CppObject* self, const Il2CppGuid* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_guid_contains_self");
    Il2CppObject* carrier = icall_box_guid(item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_guid_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_guid_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

void icall_hashset_guid_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    Il2CppGuid* dst = reinterpret_cast<Il2CppGuid*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = icall_unbox_guid(keys[i]);
        ++n;
    }
}

void icall_hashset_guid_enumerator_get_current(Il2CppGuid* result, Il2CppObject* self)
{
    if (result == nullptr) return;
    *result = icall_unbox_guid(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

namespace runtime {

void icall_hashset_int_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 1, static_cast<int32_t>(Family::I4));
}

bool icall_hashset_int_add(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_int_add_self");
    Il2CppObject* carrier = il2cpp_box_int32(nullptr, item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_int_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_int_remove(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_int_remove_self");
    Il2CppObject* carrier = il2cpp_box_int32(nullptr, item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_int_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_int_contains(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_int_contains_self");
    Il2CppObject* carrier = il2cpp_box_int32(nullptr, item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_int_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_int_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

bool icall_hashset_int_try_get_value(Il2CppObject* self, int32_t equalValue, int32_t* actualValue)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_int_tryget_self");
    Il2CppObject* carrier = il2cpp_box_int32(nullptr, equalValue);
    root = r.Get();
    Il2CppObject* found = FindCarrier(AsSet(root), carrier);
    if (found == nullptr)
    {
        // net48 TryGetValue writes default(T) into actualValue on a miss.
        if (actualValue != nullptr) *actualValue = 0;
        return false;
    }
    if (actualValue != nullptr) *actualValue = il2cpp_unbox_int32(found);
    return true;
}

void icall_hashset_int_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    CopyToI4(AsSet(self), array, arrayIndex);
}

void icall_hashset_int_copy_to_range(Il2CppObject* self, Il2CppArray* array,
                                     int32_t index, int32_t arrayIndex, int32_t count)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    if (index < 0 || count < 0 || index > LiveCount(s) - count)
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    if (static_cast<int64_t>(arrayIndex) + count > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    int32_t* dst = reinterpret_cast<int32_t*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t seen = 0, n = 0;
    for (int32_t i = 0; i < s->count && n < count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (seen++ < index) continue;
        dst[arrayIndex + n] = il2cpp_unbox_int32(keys[i]);
        ++n;
    }
}

int32_t icall_hashset_int_enumerator_get_current(Il2CppObject* self)
{
    return il2cpp_unbox_int32(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// int64 family.
//------------------------------------------------------------------------------
void icall_hashset_long_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::I8));
}

bool icall_hashset_long_add(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_long_add_self");
    Il2CppObject* carrier = icall_box_long(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_long_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_long_remove(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_long_remove_self");
    Il2CppObject* carrier = icall_box_long(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_long_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_long_contains(Il2CppObject* self, int64_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_long_contains_self");
    Il2CppObject* carrier = icall_box_long(item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_long_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_long_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

bool icall_hashset_long_try_get_value(Il2CppObject* self, int64_t equalValue, int64_t* actualValue)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_long_tryget_self");
    Il2CppObject* carrier = icall_box_long(equalValue);
    root = r.Get();
    Il2CppObject* found = FindCarrier(AsSet(root), carrier);
    if (found == nullptr)
    {
        if (actualValue != nullptr) *actualValue = 0;
        return false;
    }
    if (actualValue != nullptr) *actualValue = icall_unbox_long(found);
    return true;
}

void icall_hashset_long_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    int64_t* dst = reinterpret_cast<int64_t*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = icall_unbox_long(keys[i]);
        ++n;
    }
}

int64_t icall_hashset_long_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_long(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// double family.
//------------------------------------------------------------------------------
void icall_hashset_double_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::D));
}

bool icall_hashset_double_add(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_double_add_self");
    Il2CppObject* carrier = icall_box_double(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_double_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_double_remove(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_double_remove_self");
    Il2CppObject* carrier = icall_box_double(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_double_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_double_contains(Il2CppObject* self, double item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_double_contains_self");
    Il2CppObject* carrier = icall_box_double(item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_double_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_double_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

bool icall_hashset_double_try_get_value(Il2CppObject* self, double equalValue, double* actualValue)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_double_tryget_self");
    Il2CppObject* carrier = icall_box_double(equalValue);
    root = r.Get();
    Il2CppObject* found = FindCarrier(AsSet(root), carrier);
    if (found == nullptr)
    {
        if (actualValue != nullptr) *actualValue = 0.0;
        return false;
    }
    if (actualValue != nullptr) *actualValue = icall_unbox_double(found);
    return true;
}

void icall_hashset_double_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    double* dst = reinterpret_cast<double*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = icall_unbox_double(keys[i]);
        ++n;
    }
}

double icall_hashset_double_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_double(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// uint32 family.
//------------------------------------------------------------------------------
void icall_hashset_uint_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 1, static_cast<int32_t>(Family::U4));
}

bool icall_hashset_uint_add(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_uint_add_self");
    Il2CppObject* carrier = icall_box_uint(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_uint_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_uint_remove(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_uint_remove_self");
    Il2CppObject* carrier = icall_box_uint(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_uint_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_uint_contains(Il2CppObject* self, int32_t item)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_uint_contains_self");
    Il2CppObject* carrier = icall_box_uint(item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_uint_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_uint_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

bool icall_hashset_uint_try_get_value(Il2CppObject* self, int32_t equalValue, int32_t* actualValue)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_uint_tryget_self");
    Il2CppObject* carrier = icall_box_uint(equalValue);
    root = r.Get();
    Il2CppObject* found = FindCarrier(AsSet(root), carrier);
    if (found == nullptr)
    {
        if (actualValue != nullptr) *actualValue = 0;
        return false;
    }
    if (actualValue != nullptr) *actualValue = icall_unbox_uint(found);
    return true;
}

void icall_hashset_uint_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    int32_t* dst = reinterpret_cast<int32_t*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = icall_unbox_uint(keys[i]);
        ++n;
    }
}

int32_t icall_hashset_uint_enumerator_get_current(Il2CppObject* self)
{
    return icall_unbox_uint(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

//------------------------------------------------------------------------------
// string / reference families.
//------------------------------------------------------------------------------
void icall_hashset_string_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::Ref));
}

bool icall_hashset_string_add(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_string_add_self");
    root = r.Get();
    return AddCarrier(&root, item);
}

bool icall_hashset_string_remove(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_string_remove_self");
    root = r.Get();
    return RemoveCarrier(&root, item);
}

bool icall_hashset_string_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    return ContainsCarrier(AsSet(self), item);
}

int32_t icall_hashset_string_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_string_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

bool icall_hashset_string_try_get_value(Il2CppObject* self, Il2CppObject* equalValue, Il2CppObject** actualValue)
{
    ThrowIfNullReceiver(self);
    if (equalValue == nullptr) il2cpp_throw_argument_null_exception("equalValue");
    Il2CppObject* found = FindCarrier(AsSet(self), equalValue);
    if (found == nullptr)
    {
        if (actualValue != nullptr) *actualValue = nullptr;
        return false;
    }
    if (actualValue != nullptr) *actualValue = found;
    return true;
}

void icall_hashset_string_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    CopyToImpl(AsSet(self), array, arrayIndex, 0, LiveCount(AsSet(self)));
}

Il2CppObject* icall_hashset_string_enumerator_get_current(Il2CppObject* self)
{
    return il2cpp_dict_si_keycollection_enumerator_get_current(self);
}

void icall_hashset_ref_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::Ref));
}

bool icall_hashset_ref_add(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_ref_add_self");
    root = r.Get();
    return AddCarrier(&root, item);
}

bool icall_hashset_ref_remove(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_ref_remove_self");
    root = r.Get();
    return RemoveCarrier(&root, item);
}

bool icall_hashset_ref_contains(Il2CppObject* self, Il2CppObject* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    return ContainsCarrier(AsSet(self), item);
}

int32_t icall_hashset_ref_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_ref_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

bool icall_hashset_ref_try_get_value(Il2CppObject* self, Il2CppObject* equalValue, Il2CppObject** actualValue)
{
    ThrowIfNullReceiver(self);
    if (equalValue == nullptr) il2cpp_throw_argument_null_exception("equalValue");
    Il2CppObject* found = FindCarrier(AsSet(self), equalValue);
    if (found == nullptr)
    {
        if (actualValue != nullptr) *actualValue = nullptr;
        return false;
    }
    if (actualValue != nullptr) *actualValue = found;
    return true;
}

void icall_hashset_ref_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    CopyToImpl(AsSet(self), array, arrayIndex, 0, LiveCount(AsSet(self)));
}

//------------------------------------------------------------------------------
// decimal family.
//------------------------------------------------------------------------------
void icall_hashset_dec_ctor(Il2CppObject* self, int32_t capacity)
{
    CtorImpl(self, capacity, nullptr, 0, static_cast<int32_t>(Family::Dec));
}

bool icall_hashset_dec_add(Il2CppObject* self, const runtime::Il2CppDecimal* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_dec_add_self");
    Il2CppObject* carrier = icall_box_decimal(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_dec_add_item");
    root = r.Get();
    carrier = cr.Get();
    return AddCarrier(&root, carrier);
}

bool icall_hashset_dec_remove(Il2CppObject* self, const runtime::Il2CppDecimal* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_dec_remove_self");
    Il2CppObject* carrier = icall_box_decimal(item);
    GcRoot<Il2CppObject*> cr(carrier, "hashset_dec_remove_item");
    root = r.Get();
    carrier = cr.Get();
    return RemoveCarrier(&root, carrier);
}

bool icall_hashset_dec_contains(Il2CppObject* self, const runtime::Il2CppDecimal* item)
{
    ThrowIfNullReceiver(self);
    if (item == nullptr) il2cpp_throw_argument_null_exception("item");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_dec_contains_self");
    Il2CppObject* carrier = icall_box_decimal(item);
    root = r.Get();
    return ContainsCarrier(AsSet(root), carrier);
}

int32_t icall_hashset_dec_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return LiveCount(AsSet(self));
}

void icall_hashset_dec_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    ClearAll(AsSet(self));
}

void icall_hashset_dec_enumerator_get_current(runtime::Il2CppDecimal* result, Il2CppObject* self)
{
    if (result == nullptr) return;
    *result = icall_unbox_decimal(il2cpp_dict_si_keycollection_enumerator_get_current(self));
}

void icall_hashset_dec_copy_to(Il2CppObject* self, Il2CppArray* array, int32_t arrayIndex)
{
    ThrowIfNullReceiver(self);
    Il2CppHashSetObject* s = AsSet(self);
    if (array == nullptr) il2cpp_throw_argument_null_exception("array");
    if (arrayIndex < 0) il2cpp_throw_argument_out_of_range_exception("arrayIndex", nullptr);
    const int32_t live = LiveCount(s);
    if (static_cast<int64_t>(arrayIndex) + live > array->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    runtime::Il2CppDecimal* dst = reinterpret_cast<runtime::Il2CppDecimal*>(array->data);
    const int32_t* hashes = I4Slots(s->hashes);
    Il2CppObject** keys = RefSlots(s->keys);
    int32_t n = 0;
    for (int32_t i = 0; i < s->count && n < live; ++i)
    {
        if (hashes[i] < 0) continue;
        dst[arrayIndex + n] = icall_unbox_decimal(keys[i]);
        ++n;
    }
}

//------------------------------------------------------------------------------
// Comparer / enumerable constructors.
//------------------------------------------------------------------------------
namespace {

void CtorComparerImpl(Il2CppObject* self, Il2CppObject* comparer, int32_t keyKind, int32_t family)
{
    if (comparer == nullptr) il2cpp_throw_argument_null_exception("comparer");
    CtorImpl(self, 0, comparer, keyKind, family);
}

// .ctor(IEnumerable<T>): enumerate the operand in the receiver's family and
// add every element.
void CtorEnumImpl(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer,
                  int32_t keyKind, Family f)
{
    if (items == nullptr) il2cpp_throw_argument_null_exception("collection");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_ctor_enum_self");
    CtorImpl(root, 0, comparer, keyKind, static_cast<int32_t>(f));
    root = r.Get();

    int32_t n = 0;
    Il2CppArray* carriers = CollectCarriers(items, f, &n);
    Il2CppObject* carriersObj = reinterpret_cast<Il2CppObject*>(carriers);
    GcRoot<Il2CppObject*> carriersRoot(carriersObj, "hashset_ctor_enum_carriers");
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* carrier = RefSlots(carriers)[i];
        AddCarrier(&root, carrier);
        root = r.Get();
    }
}

} // namespace

void icall_hashset_int_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer)
{ CtorComparerImpl(self, comparer, 1, static_cast<int32_t>(Family::I4)); }
void icall_hashset_ref_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer)
{ CtorComparerImpl(self, comparer, 0, static_cast<int32_t>(Family::Ref)); }
void icall_hashset_long_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer)
{ CtorComparerImpl(self, comparer, 0, static_cast<int32_t>(Family::I8)); }
void icall_hashset_double_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer)
{ CtorComparerImpl(self, comparer, 0, static_cast<int32_t>(Family::D)); }
void icall_hashset_uint_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer)
{ CtorComparerImpl(self, comparer, 1, static_cast<int32_t>(Family::U4)); }
void icall_hashset_dec_ctor_comparer(Il2CppObject* self, Il2CppObject* comparer)
{ CtorComparerImpl(self, comparer, 0, static_cast<int32_t>(Family::Dec)); }

void icall_hashset_int_ctor_enum(Il2CppObject* self, Il2CppObject* items)
{ CtorEnumImpl(self, items, nullptr, 1, Family::I4); }
void icall_hashset_ref_ctor_enum(Il2CppObject* self, Il2CppObject* items)
{ CtorEnumImpl(self, items, nullptr, 0, Family::Ref); }
void icall_hashset_long_ctor_enum(Il2CppObject* self, Il2CppObject* items)
{ CtorEnumImpl(self, items, nullptr, 0, Family::I8); }
void icall_hashset_double_ctor_enum(Il2CppObject* self, Il2CppObject* items)
{ CtorEnumImpl(self, items, nullptr, 0, Family::D); }
void icall_hashset_uint_ctor_enum(Il2CppObject* self, Il2CppObject* items)
{ CtorEnumImpl(self, items, nullptr, 1, Family::U4); }
void icall_hashset_dec_ctor_enum(Il2CppObject* self, Il2CppObject* items)
{ CtorEnumImpl(self, items, nullptr, 0, Family::Dec); }

void icall_hashset_int_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer)
{ if (comparer == nullptr) il2cpp_throw_argument_null_exception("comparer"); CtorEnumImpl(self, items, comparer, 1, Family::I4); }
void icall_hashset_ref_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer)
{ if (comparer == nullptr) il2cpp_throw_argument_null_exception("comparer"); CtorEnumImpl(self, items, comparer, 0, Family::Ref); }
void icall_hashset_long_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer)
{ if (comparer == nullptr) il2cpp_throw_argument_null_exception("comparer"); CtorEnumImpl(self, items, comparer, 0, Family::I8); }
void icall_hashset_double_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer)
{ if (comparer == nullptr) il2cpp_throw_argument_null_exception("comparer"); CtorEnumImpl(self, items, comparer, 0, Family::D); }
void icall_hashset_uint_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer)
{ if (comparer == nullptr) il2cpp_throw_argument_null_exception("comparer"); CtorEnumImpl(self, items, comparer, 1, Family::U4); }
void icall_hashset_dec_ctor_enum_comparer(Il2CppObject* self, Il2CppObject* items, Il2CppObject* comparer)
{ if (comparer == nullptr) il2cpp_throw_argument_null_exception("comparer"); CtorEnumImpl(self, items, comparer, 0, Family::Dec); }

//------------------------------------------------------------------------------
// Set algebra. Every operation reduces to carrier-set operations on a
// temporary set built from the operand.
//------------------------------------------------------------------------------
void icall_hashset_union_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_union_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_union_operand");
    Il2CppHashSetObject* o = AsSet(tr.Get());
    const int32_t* hashes = I4Slots(o->hashes);
    for (int32_t i = 0; i < o->count; ++i)
    {
        if (hashes[i] < 0) continue;
        Il2CppObject* carrier = RefSlots(o->keys)[i];
        root = r.Get();
        AddCarrier(&root, carrier);
    }
}

void icall_hashset_except_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_except_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_except_operand");
    Il2CppHashSetObject* o = AsSet(tr.Get());
    const int32_t* hashes = I4Slots(o->hashes);
    for (int32_t i = 0; i < o->count; ++i)
    {
        if (hashes[i] < 0) continue;
        Il2CppObject* carrier = RefSlots(o->keys)[i];
        root = r.Get();
        RemoveCarrier(&root, carrier);
    }
}

void icall_hashset_intersect_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_intersect_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_intersect_operand");

    // Walk the SELF slots and drop every member absent from the operand set.
    std::vector<Il2CppObject*> drop;
    {
        Il2CppHashSetObject* s = AsSet(root);
        Il2CppHashSetObject* o = AsSet(tr.Get());
        const int32_t* hashes = I4Slots(s->hashes);
        for (int32_t i = 0; i < s->count; ++i)
        {
            if (hashes[i] < 0) continue;
            Il2CppObject* carrier = RefSlots(s->keys)[i];
            if (!ContainsCarrier(o, carrier)) drop.push_back(carrier);
        }
    }
    for (Il2CppObject* carrier : drop)
    {
        root = r.Get();
        RemoveCarrier(&root, carrier);
    }
}

void icall_hashset_symmetric_except_with(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_symdiff_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_symdiff_operand");
    Il2CppHashSetObject* o = AsSet(tr.Get());
    const int32_t* hashes = I4Slots(o->hashes);
    for (int32_t i = 0; i < o->count; ++i)
    {
        if (hashes[i] < 0) continue;
        Il2CppObject* carrier = RefSlots(o->keys)[i];
        root = r.Get();
        if (!RemoveCarrier(&root, carrier))
        {
            AddCarrier(&root, carrier);
        }
    }
}

bool icall_hashset_set_equals(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_setequals_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_setequals_operand");
    Il2CppHashSetObject* s = AsSet(root);
    Il2CppHashSetObject* o = AsSet(tr.Get());
    if (LiveCount(s) != LiveCount(o)) return false;
    const int32_t* hashes = I4Slots(s->hashes);
    for (int32_t i = 0; i < s->count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (!ContainsCarrier(o, RefSlots(s->keys)[i])) return false;
    }
    return true;
}

bool icall_hashset_is_subset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_subset_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_subset_operand");
    Il2CppHashSetObject* s = AsSet(root);
    Il2CppHashSetObject* o = AsSet(tr.Get());
    if (LiveCount(s) > LiveCount(o)) return false;
    const int32_t* hashes = I4Slots(s->hashes);
    for (int32_t i = 0; i < s->count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (!ContainsCarrier(o, RefSlots(s->keys)[i])) return false;
    }
    return true;
}

bool icall_hashset_is_proper_subset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_propsubset_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_propsubset_operand");
    Il2CppHashSetObject* s = AsSet(root);
    Il2CppHashSetObject* o = AsSet(tr.Get());
    if (LiveCount(s) >= LiveCount(o)) return false;
    const int32_t* hashes = I4Slots(s->hashes);
    for (int32_t i = 0; i < s->count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (!ContainsCarrier(o, RefSlots(s->keys)[i])) return false;
    }
    return true;
}

bool icall_hashset_is_superset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_superset_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_superset_operand");
    Il2CppHashSetObject* s = AsSet(root);
    Il2CppHashSetObject* o = AsSet(tr.Get());
    if (LiveCount(s) < LiveCount(o)) return false;
    const int32_t* hashes = I4Slots(o->hashes);
    for (int32_t i = 0; i < o->count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (!ContainsCarrier(s, RefSlots(o->keys)[i])) return false;
    }
    return true;
}

bool icall_hashset_is_proper_superset_of(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_propsuperset_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_propsuperset_operand");
    Il2CppHashSetObject* s = AsSet(root);
    Il2CppHashSetObject* o = AsSet(tr.Get());
    if (LiveCount(s) <= LiveCount(o)) return false;
    const int32_t* hashes = I4Slots(o->hashes);
    for (int32_t i = 0; i < o->count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (!ContainsCarrier(s, RefSlots(o->keys)[i])) return false;
    }
    return true;
}

bool icall_hashset_overlaps(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_overlaps_self");
    Il2CppObject* tmp = MakeOperandSet(root, other, static_cast<Family>(AsSet(root)->family));
    GcRoot<Il2CppObject*> tr(tmp, "hashset_overlaps_operand");
    Il2CppHashSetObject* s = AsSet(root);
    Il2CppHashSetObject* o = AsSet(tr.Get());
    const int32_t* hashes = I4Slots(o->hashes);
    for (int32_t i = 0; i < o->count; ++i)
    {
        if (hashes[i] < 0) continue;
        if (ContainsCarrier(s, RefSlots(o->keys)[i])) return true;
    }
    return false;
}

namespace {

// The Predicate<int32> leaf ABIs (the arrayext2 InvokeFindIndexPred contract):
// a STATIC leaf takes the value alone, an INSTANCE-BOUND leaf receives its
// target in the `this` slot.
using PredI4Fn = bool (*)(int32_t);
using PredI4ThisFn = bool (*)(Il2CppObject*, int32_t);

Il2CppDelegate* RequireSetPredicate(Il2CppObject* match, const char* paramName)
{
    if (match == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
    }
    const char* name = match->klass != nullptr ? match->klass->name : nullptr;
    if (name == nullptr || std::strcmp(name, "System.MulticastDelegate") == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate cannot be used as a set callback.");
    }
    if (std::strcmp(name, "System.Delegate") != 0)
    {
        il2cpp_throw_argument_exception("Object is not a delegate.");
    }
    return reinterpret_cast<Il2CppDelegate*>(match);
}

bool InvokePredI4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredI4Fn>(d->methodPtr)(v);
}

} // namespace

int32_t icall_hashset_remove_where(Il2CppObject* self, Il2CppObject* match)
{
    ThrowIfNullReceiver(self);
    Il2CppDelegate* pred = RequireSetPredicate(match, "match");
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_removewhere_self");
    Il2CppObject* matchRoot = match;
    GcRoot<Il2CppObject*> mr(matchRoot, "hashset_removewhere_match");
    (void)pred;

    std::vector<Il2CppObject*> drop;
    {
        Il2CppHashSetObject* s = AsSet(root);
        const int32_t* hashes = I4Slots(s->hashes);
        for (int32_t i = 0; i < s->count; ++i)
        {
            if (hashes[i] < 0) continue;
            Il2CppObject* carrier = RefSlots(s->keys)[i];
            // Re-read the delegate through the rooted slot: the leaf may
            // allocate, and a compaction rewrites the root.
            Il2CppDelegate* live = reinterpret_cast<Il2CppDelegate*>(mr.Get());
            if (InvokePredI4(live, il2cpp_unbox_int32(carrier)))
            {
                drop.push_back(carrier);
            }
        }
    }
    for (Il2CppObject* carrier : drop)
    {
        root = r.Get();
        RemoveCarrier(&root, carrier);
    }
    return static_cast<int32_t>(drop.size());
}

Il2CppObject* icall_hashset_get_comparer(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsSet(self)->comparer;
}

} // namespace runtime

//------------------------------------------------------------------------------
// Enumeration: a slot-ordered carrier snapshot wrapped in the shared
// keycollection enumerator (the historical surface; MoveNext/Dispose reuse
// that domain's icalls).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_hashset_int_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_int_get_enumerator");
    Il2CppArray* snap = SnapshotCarriers(&root);
    Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
    GcRoot<Il2CppObject*> sr(snapObj, "hashset_int_enum_snapshot");
    return il2cpp_dict_si_keycollection_get_enumerator(sr.Get());
}

Il2CppObject* il2cpp_hashset_long_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_long_get_enumerator");
    Il2CppArray* snap = SnapshotCarriers(&root);
    Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
    GcRoot<Il2CppObject*> sr(snapObj, "hashset_long_enum_snapshot");
    return il2cpp_dict_si_keycollection_get_enumerator(sr.Get());
}

Il2CppObject* il2cpp_hashset_double_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_double_get_enumerator");
    Il2CppArray* snap = SnapshotCarriers(&root);
    Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
    GcRoot<Il2CppObject*> sr(snapObj, "hashset_double_enum_snapshot");
    return il2cpp_dict_si_keycollection_get_enumerator(sr.Get());
}

Il2CppObject* il2cpp_hashset_uint_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_uint_get_enumerator");
    Il2CppArray* snap = SnapshotCarriers(&root);
    Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
    GcRoot<Il2CppObject*> sr(snapObj, "hashset_uint_enum_snapshot");
    return il2cpp_dict_si_keycollection_get_enumerator(sr.Get());
}

Il2CppObject* il2cpp_hashset_ref_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_ref_get_enumerator");
    Il2CppArray* snap = SnapshotCarriers(&root);
    Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
    GcRoot<Il2CppObject*> sr(snapObj, "hashset_ref_enum_snapshot");
    return il2cpp_dict_si_keycollection_get_enumerator(sr.Get());
}

Il2CppObject* il2cpp_hashset_string_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_string_get_enumerator");
    Il2CppArray* snap = SnapshotCarriers(&root);
    Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
    GcRoot<Il2CppObject*> sr(snapObj, "hashset_string_enum_snapshot");
    return il2cpp_dict_si_keycollection_get_enumerator(sr.Get());
}

Il2CppObject* il2cpp_hashset_dec_get_enumerator(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* root = self;
    GcRoot<Il2CppObject*> r(root, "hashset_dec_get_enumerator");
    Il2CppArray* snap = SnapshotCarriers(&root);
    Il2CppObject* snapObj = reinterpret_cast<Il2CppObject*>(snap);
    GcRoot<Il2CppObject*> sr(snapObj, "hashset_dec_enum_snapshot");
    return il2cpp_dict_si_keycollection_get_enumerator(sr.Get());
}

} // namespace il2cpp
