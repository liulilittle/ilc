//------------------------------------------------------------------------------
// hashtable.cpp -- non-generic hashtable runtime (see il2cpp_hashtable.h for
// the full layout, GC bitmap, hash and growth contracts).
//
// Data structure:
//   Il2CppHashtable { buckets: Il2CppArray* (reference array), count }
//   buckets[i] = head of collision chain i; chains are singly linked lists of
//   Il2CppHashEntry nodes (key/value/next/hash). The bucket array is a
//   REFERENCE array, so the collector scans every chain head; every entry's
//   key/value/next are scanned through its class bitmap (bits 1,2,3). A
//   rooted hashtable therefore keeps its entire contents alive.
//
// Hash contract (implemented here, documented in the header):
//   * System.String keys   -> ordinal string hash (simplified .NET Framework
//     string.GetHashCode, unrandomized for deterministic AOT output);
//   * boxed primitives (D-HT-1): System.Int32 / System.Double / System.Char /
//     System.Boolean / System.Single -> the boxed VALUE (mirrors the net48
//     value-type GetHashCode; see BoxedValueHash);
//   * every other key      -> address hash (pointer >> 3, so bucket indices
//     use the well-distributed bits above the 8-byte alignment zeros).
//   Hashes are ALWAYS derived from the LIVE key (nothing cached): the M3
//   collector COMPACTS Gen0 (survivors are copied; every reference is
//   rewritten -- gc.cpp Collect), so an address-hashed key hashes to a new
//   bucket after a move. FindEntry self-heals such entries (repair-on-miss,
//   see below); resize re-derives from the live keys, so it is always
//   correct even right after a collection.
//
// Growth: load factor 0.72 (integer-exact 72/100). Before an insert that
// would exceed it, the bucket array doubles (power of two, capped at 2^30)
// and every chain is rehashed from the live keys into the new array. Entry
// nodes are reused.
//
// GC-safety notes for the code below:
//   * All reference stores go through il2cpp_write_barrier first (old ->
//     young tracking, same convention as arrayext.cpp).
//   * Resize allocates ONE new array, then performs pure pointer writes
//     (no further allocation), so no collection can run between the array's
//     creation and its publication into ht->buckets.
//   * Insert allocates the entry node AFTER the resize check and links it
//     with pure writes; the node is fresh (post-collection), so it cannot be
//     reclaimed mid-insert.
//   * Exception factories come from il2cpp_exception_ext.h (chains carry the
//     type + base classes, so generated catch clauses follow .NET
//     inheritance semantics).
//------------------------------------------------------------------------------

#include "il2cpp_hashtable.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cmath>   // std::isnan (boxed double/float key equality)
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Layout verification: the class descriptors' refFieldBitmap bits below are
// literal constants; these compile-time checks pin them to the struct
// layout, so a layout change with a stale bitmap cannot link silently.
//------------------------------------------------------------------------------
static_assert(offsetof(Il2CppHashtable, buckets) == 1 * sizeof(void*), "Il2CppHashtable: buckets must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppHashtable, count) == 2 * sizeof(void*), "Il2CppHashtable: count sits at slot 2 (NOT in the bitmap)");
static_assert(offsetof(Il2CppHashEntry, key) == 1 * sizeof(void*), "Il2CppHashEntry: key must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppHashEntry, value) == 2 * sizeof(void*), "Il2CppHashEntry: value must sit at slot 2 (bitmap bit 2)");
static_assert(offsetof(Il2CppHashEntry, next) == 3 * sizeof(void*), "Il2CppHashEntry: next must sit at slot 3 (bitmap bit 3)");

// Minimum bucket count: avoids a degenerate single-bucket table for tiny
// capacities (a 1-bucket table would resize on the very first insert).
constexpr int32_t kMinBuckets = 4;

// Doubling cap: keeps the power-of-two loop inside int32 (no overflow) and
// the bucket-index mask well below 2^31.
constexpr int32_t kMaxBucketCount = 1 << 30;

// The System.Collections.Hashtable runtime descriptor. Registered lazily
// (constraint #12: no global-constructor allocation), exactly like the
// string / random / box classes.
//   instanceSize 24 (3 slots), refFieldBitmap bit 1: 'buckets' is a GC ref.
Il2CppClass g_hashtableClass = {
    "System.Collections.Hashtable", // name
    static_cast<int32_t>(sizeof(Il2CppHashtable)), // instanceSize
    0b00010u,                       // refFieldBitmap: bit 1 = buckets
    1,                              // refFieldCount
    nullptr,                        // baseClass
    nullptr,                        // vtable
    0,                              // vtableSize
    nullptr,                        // interfaces
    0,                              // interfaceCount
};

// The collision-node descriptor (one key/value pair):
//   instanceSize 32 (4 slots), refFieldBitmap bits 1,2,3: key/value/next.
Il2CppClass g_entryClass = {
    "System.Collections.Hashtable+Entry", // name
    static_cast<int32_t>(sizeof(Il2CppHashEntry)), // instanceSize
    0b01110u,                       // refFieldBitmap: bits 1,2,3 = key, value, next
    3,                              // refFieldCount
    nullptr,                        // baseClass
    nullptr,                        // vtable
    0,                              // vtableSize
    nullptr,                        // interfaces
    0,                              // interfaceCount
};

bool g_hashtableRegistered = false;

void EnsureHashtableRegistered()
{
    if (g_hashtableRegistered) return;
    il2cpp_class_register(&g_hashtableClass);
    il2cpp_class_register(&g_entryClass);
    g_hashtableRegistered = true;
}

//------------------------------------------------------------------------------
// Small helpers.
//------------------------------------------------------------------------------

Il2CppHashtable* AsHashtable(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppHashtable*>(self);
}

Il2CppHashEntry* AsEntry(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppHashEntry*>(obj);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors stringext.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// Allocate a reference bucket array (Il2CppObject* elements, isRefElem =
// true -> the collector scans every element as a GC reference).
Il2CppArray* AllocBuckets(int32_t bucketCount)
{
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, bucketCount);
}

// Defensive: a hashtable whose ctor icall was skipped has a null bucket
// array; lazily allocate the minimum table so no icall dereferences null
// (generated code always calls the ctor -- this is misuse hardening only).
// NOTE: the allocation here can compact the table, so this MUST be inlined
// at a call site that has rooted 'ht' (GcRoot) and re-read it BEFORE the
// store -- a by-value helper's store would go to the moved-out old address
// and be lost (stackqueue2 e2e, 2026-08-13).

// Bucket index from a hash: bucketCount is always a power of two, so the
// exact mask is (bucketCount - 1) applied in unsigned arithmetic (a negative
// hash wraps to a well-defined unsigned pattern, e.g. -1 -> all-ones).
int32_t BucketIndex(int32_t hash, int32_t bucketCount)
{
    return static_cast<int32_t>(static_cast<uint32_t>(hash) & static_cast<uint32_t>(bucketCount - 1));
}

//------------------------------------------------------------------------------
// Key classification (drives the hash + equality contract).
// The runtime's own descriptors own these names ("System.String" from
// string.cpp; every boxed primitive -- Int32/Double/Char/Boolean/Single and
// the rest of the family -- from metadata.cpp), so klass-name comparison is
// stable for every managed string / boxed primitive the runtime produces;
// the transpiled BCL's descriptors for the same types carry the same names.
//------------------------------------------------------------------------------

bool IsStringKey(const Il2CppObject* key)
{
    return key->klass != nullptr && std::strcmp(key->klass->name, "System.String") == 0;
}

// The boxed primitives the hash contract covers BY VALUE (mirroring net48's
// per-type GetHashCode; see BoxedValueHash). Recognition is by the box
// CLASS NAME -- exactly the descriptors boxing.cpp / boxingext.cpp /
// boxingext2.cpp stamp their boxes with -- so every box the runtime (or the
// transpiled BCL) produces is classified consistently with boxingext's
// klass-name-keyed dispatch.
bool IsBoxedValueKey(const Il2CppObject* key)
{
    if (key->klass == nullptr)
    {
        return false;
    }
    const char* n = key->klass->name;
    return std::strcmp(n, "System.Int32") == 0 ||
           std::strcmp(n, "System.Double") == 0 ||
           std::strcmp(n, "System.Char") == 0 ||
           std::strcmp(n, "System.Boolean") == 0 ||
           std::strcmp(n, "System.Single") == 0 ||
           std::strcmp(n, "System.Int64") == 0 ||
           std::strcmp(n, "System.UInt32") == 0 ||
           std::strcmp(n, "System.UInt64") == 0;
}

// The boxed-value payload slot: the aligned 8-byte slot after the object
// header -- byte-identical to boxing.cpp / boxingext.cpp / boxingext2.cpp's
// kBoxPayloadOffset (never change one without the other).
constexpr int32_t kBoxPayloadOffset = 8;

// Read the boxed payloads. Only called after IsBoxedValueKey (or a string
// comparison that implies it), so the klass is one of the five box classes.
int32_t BoxedInt32Value(const Il2CppObject* boxed)
{
    return *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset);
}

int64_t BoxedInt64Value(const Il2CppObject* boxed)
{
    return *reinterpret_cast<const int64_t*>(reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset);
}

double BoxedDoubleValue(const Il2CppObject* boxed)
{
    return *reinterpret_cast<const double*>(reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset);
}

//------------------------------------------------------------------------------
// Ordinal string hash -- EXACTLY the net48 string.GetHashCode algorithm
// (COMString::GetHashCode, fixed-seed legacy form; probe-verified against
// csc.exe net48 on 2026-08-04): two DJBR-style accumulators (base 5381, step
// *33) walking the even and odd units independently, combined with the
// 0x5D588B65 secondary multiplier. Written with unsigned arithmetic so the
// wraps are well-defined (no signed overflow UB). The seed is the plain
// 5381, NOT (5381<<16)+5381 -- a shifted seed breaks byte-compatibility with
// net48 (see tests/runtime/test_stringhash.cpp for the pinned oracle
// values). DELIBERATELY unrandomized: AOT output must be deterministic, and
// this hash is not a security boundary.
//------------------------------------------------------------------------------
int32_t HashStringOrdinal(const Il2CppString* s)
{
    uint32_t hash1 = 5381u;
    uint32_t hash2 = hash1;
    for (int32_t i = 0; i < s->length; i += 2)
    {
        hash1 = ((hash1 << 5) + hash1) ^ static_cast<uint32_t>(s->chars[i]);
        if (i + 1 < s->length)
        {
            hash2 = ((hash2 << 5) + hash2) ^ static_cast<uint32_t>(s->chars[i + 1]);
        }
    }
    return static_cast<int32_t>(hash1 + hash2 * 1566083941u);
}

//------------------------------------------------------------------------------
// Boxed-primitive hashes -- EXACTLY the net48 value-type GetHashCode
// implementations (referencesource; the value-key probe is byte-verified):
//   * System.Int32    -> the value itself
//   * System.Char     -> the UTF-16 code unit
//   * System.Boolean  -> 0/1
//   * System.Double   -> 0 for +0.0/-0.0 (Double.Equals makes them one key),
//     otherwise (int)bits ^ (int)(bits >> 32) over the IEEE bits -- net48's
//     Double.GetHashCode. NOTE the net48 quirk this preserves bit-for-bit:
//     two NaN values with DIFFERENT bit patterns are Equals-equal yet hash
//     differently (GetHashCode hashes the raw bits). Equal NaN KEYS carry
//     identical bits on both runtimes (double.NaN is the canonical
//     0xFFF8000000000000), so real use is consistent; exotic NaN patterns
//     reproduce net48's exact observable behavior.
//   * System.Single   -> the box stores the float AS A DOUBLE (boxingext2.cpp
//     icall_box_float; float -> double is exact). net48's Single.GetHashCode
//     hashes the 32-bit float bits, so the stored double is narrowed back to
//     float (an exact round-trip) and its bits hashed; 0.0f/-0.0f -> 0.
// Equal keys MUST hash equal (see KeyHash).
//------------------------------------------------------------------------------
int32_t BoxedDoubleHash(const Il2CppObject* boxed)
{
    const double value = BoxedDoubleValue(boxed);
    if (value == 0.0)
    {
        return 0; // net48: +0.0 and -0.0 hash equal (Double.Equals is also true)
    }
    uint64_t bits;
    std::memcpy(&bits, &value, sizeof(bits));
    return static_cast<int32_t>(bits) ^ static_cast<int32_t>(bits >> 32);
}

int32_t BoxedFloatHash(const Il2CppObject* boxed)
{
    const double value = BoxedDoubleValue(boxed);
    if (value == 0.0)
    {
        return 0; // net48: 0.0f and -0.0f hash equal (Single.Equals is also true)
    }
    const float f = static_cast<float>(value); // exact: the box stored a float widened to double
    uint32_t bits;
    std::memcpy(&bits, &f, sizeof(bits));
    return static_cast<int32_t>(bits);
}

// The value hash of a boxed primitive, dispatched on the box class name.
// Int32 / Char / Boolean share the int32 payload slot (boxing.cpp / boxingext.cpp).
int32_t BoxedValueHash(const Il2CppObject* boxed)
{
    const char* n = boxed->klass->name;
    if (std::strcmp(n, "System.Double") == 0)
    {
        return BoxedDoubleHash(boxed);
    }
    if (std::strcmp(n, "System.Single") == 0)
    {
        return BoxedFloatHash(boxed);
    }
    if (std::strcmp(n, "System.Int64") == 0 || std::strcmp(n, "System.UInt64") == 0)
    {
        // net48 Int64/UInt64.GetHashCode: (int)value ^ (int)(value >> 32).
        const int64_t v = BoxedInt64Value(boxed);
        return static_cast<int32_t>(v) ^ static_cast<int32_t>(static_cast<uint64_t>(v) >> 32);
    }
    return BoxedInt32Value(boxed);
}

//------------------------------------------------------------------------------
// KeyHash -- the hash contract (see the header). Equal keys MUST hash equal:
//   * strings            -> ordinal content hash (equal content -> equal hash);
//   * boxed primitives   -> the boxed VALUE (equal values -> equal hash);
//   * everything else    -> address hash (same object -> same address). The
//     address is shifted right by the alignment shift (3) so the low bucket
//     index bits are not pinned to zero by 8-byte alignment.
//------------------------------------------------------------------------------
int32_t KeyHash(Il2CppObject* key)
{
    if (IsStringKey(key))
    {
        return HashStringOrdinal(reinterpret_cast<const Il2CppString*>(key));
    }
    if (IsBoxedValueKey(key))
    {
        return BoxedValueHash(key);
    }
    return static_cast<int32_t>(reinterpret_cast<uintptr_t>(key) >> 3);
}

//------------------------------------------------------------------------------
// KeyEquals -- the equality contract, consistent with KeyHash:
//   * reference identity wins for every key kind (same instance -> equal);
//   * strings compare by ordinal content (equal content -> equal);
//   * boxed primitives of the SAME runtime type compare by value (equal
//     values -> equal); different boxed types are never equal (.NET object
//     equality);
//   * every other key kind is reference-only (a != b here -> not equal).
//------------------------------------------------------------------------------
bool KeyEquals(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b)
    {
        return true;
    }
    if (IsStringKey(a) && IsStringKey(b))
    {
        const Il2CppString* sa = reinterpret_cast<const Il2CppString*>(a);
        const Il2CppString* sb = reinterpret_cast<const Il2CppString*>(b);
        return sa->length == sb->length &&
               std::memcmp(sa->chars, sb->chars,
                           static_cast<size_t>(sa->length) * sizeof(char16_t)) == 0;
    }
    if (IsBoxedValueKey(a) && IsBoxedValueKey(b))
    {
        // Different boxed runtime types are never equal (.NET object
        // equality: a boxed Int32 65 != a boxed Char 'A').
        if (a->klass != b->klass)
        {
            return false;
        }
        const char* n = a->klass->name;
        if (std::strcmp(n, "System.Double") == 0 || std::strcmp(n, "System.Single") == 0)
        {
            // .NET Double/Single.Equals semantics: NaN equals NaN, +0.0 equals
            // -0.0 (IEEE == already yields true for equal magnitudes incl.
            // -0.0/+0.0).
            const double va = BoxedDoubleValue(a);
            const double vb = BoxedDoubleValue(b);
            return va == vb || (std::isnan(va) && std::isnan(vb));
        }
        if (std::strcmp(n, "System.Int64") == 0 || std::strcmp(n, "System.UInt64") == 0)
        {
            return BoxedInt64Value(a) == BoxedInt64Value(b);
        }
        return BoxedInt32Value(a) == BoxedInt32Value(b);
    }
    return false;
}

// Stable-hash keys: strings and boxed primitives hash from CONTENT/VALUE, so
// their hash is move-stable -- an entry placed under one never sits in a stale
// bucket. A primary-bucket miss for them is a genuine absence. Address-hashed
// keys are the only candidates for a stale bucket (see FindEntry).
bool IsStableHashKey(const Il2CppObject* key)
{
    return IsStringKey(key) || IsBoxedValueKey(key);
}

//------------------------------------------------------------------------------
// FindEntry -- locate the entry holding 'key' (see the header's hash
// contract; KeyEquals is the only equality authority).
//
// Primary path: bucket from the FRESH hash of the LIVE key, chain walk with
// KeyEquals. This finds every entry whose placement is current -- all
// string/boxed-value entries always (their hashes are move-stable) and every
// address-key entry whose key has not moved since it was placed.
//
// Repair path (self-healing): when the primary bucket misses AND the key is
// an ADDRESS-hashed key AND the table has survived at least one compacting
// collection (its bucket array is Gen1+ -- a Gen0 array proves no collection
// ran since the table was built, so no placement can be stale), a full-table
// scan looks for the key. A hit means the entry sat in a STALE bucket (the
// compacting collection moved the key; Phase C rewrote entry->key but cannot
// re-bucket the table); the entry is unlinked and RELINKED into the fresh
// bucket, so the table heals on access. Genuinely absent address keys cost
// one O(n) scan per miss on old tables -- documented in the header.
//------------------------------------------------------------------------------
Il2CppHashEntry* FindEntry(Il2CppHashtable* ht, Il2CppObject* key, int32_t hash)
{
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(ht->buckets->data);
    const int32_t bucketCount = ht->buckets->length;
    const int32_t index = BucketIndex(hash, bucketCount);
    Il2CppObject* cur = slots[index];
    while (cur != nullptr)
    {
        Il2CppHashEntry* entry = AsEntry(cur);
        if (KeyEquals(entry->key, key))
        {
            return entry;
        }
        cur = entry->next;
    }
    // Repair path -- only for address-hashed keys in old tables (see above).
    if (IsStableHashKey(key))
    {
        return nullptr; // stable hash: a primary miss is a genuine absence
    }
    if (il2cpp_gc_get_generation(reinterpret_cast<Il2CppObject*>(ht->buckets)) < kGcGeneration1)
    {
        return nullptr; // no collection ran since the table was built
    }
    Il2CppObject** slotsW = reinterpret_cast<Il2CppObject**>(ht->buckets->data);
    for (int32_t i = 0; i < bucketCount; ++i)
    {
        if (i == index)
        {
            continue; // the primary walk already proved the entry is not here
        }
        Il2CppObject* scan = slotsW[i];
        Il2CppHashEntry* prev = nullptr;
        while (scan != nullptr)
        {
            Il2CppHashEntry* entry = AsEntry(scan);
            if (KeyEquals(entry->key, key))
            {
                // Unlink from the stale chain...
                if (prev == nullptr)
                {
                    slotsW[i] = entry->next;
                    il2cpp_write_barrier(reinterpret_cast<void**>(&slotsW[i]), slotsW[i]);
                }
                else
                {
                    prev->next = entry->next;
                    il2cpp_write_barrier(reinterpret_cast<void**>(&prev->next), prev->next);
                }
                // ...and relink at the head of the fresh chain (the entry is
                // not in the fresh chain -- the primary walk missed it).
                entry->next = slotsW[index];
                il2cpp_write_barrier(reinterpret_cast<void**>(&entry->next), entry->next);
                slotsW[index] = reinterpret_cast<Il2CppObject*>(entry);
                il2cpp_write_barrier(reinterpret_cast<void**>(&slotsW[index]), slotsW[index]);
                return entry;
            }
            prev = entry;
            scan = entry->next;
        }
    }
    return nullptr;
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

//------------------------------------------------------------------------------
// Resize -- double the bucket array (capped) and rehash every entry from its
// LIVE key pointer. Entry nodes are reused; only the array is rebuilt.
//
// GC safety: the new array is allocated first (any collection triggered by
// that allocation runs before the array exists); the rehash loop below is
// pure pointer writes with no allocation, so no collection can run between
// the array's birth and its publication into ht->buckets. The old array is
// still reachable through ht while the loop reads it.
//------------------------------------------------------------------------------
void Resize(Il2CppHashtable* ht)
{
    // AllocBuckets below can trigger a compacting collection that MOVES the
    // table: root it and re-read the (possibly moved) address before any
    // store into it (the rehash loop reads through the re-rooted slot too).
    GcRoot<Il2CppHashtable*> root(ht, "icall_hashtable_resize");
    const int32_t oldCount = ht->buckets->length;
    int32_t newCount = oldCount * 2;
    if (newCount < oldCount || newCount > kMaxBucketCount)
    {
        newCount = oldCount; // already at the cap: rehash into the same size
    }
    if (newCount != oldCount)
    {
        Il2CppArray* newBuckets = AllocBuckets(newCount);
        ht = root.Get();
        Il2CppObject** newSlots = reinterpret_cast<Il2CppObject**>(newBuckets->data);
        Il2CppObject* const* oldSlots = reinterpret_cast<Il2CppObject* const*>(ht->buckets->data);
        for (int32_t i = 0; i < oldCount; ++i)
        {
            Il2CppObject* cur = oldSlots[i];
            while (cur != nullptr)
            {
                Il2CppHashEntry* entry = AsEntry(cur);
                cur = entry->next; // capture the successor BEFORE relinking
                // Fresh derivation from the live key: correct even when a
                // compacting collection moved the key (Phase C already
                // rewrote entry->key to the new address).
                const int32_t index = BucketIndex(KeyHash(entry->key), newCount);
                entry->next = newSlots[index];
                il2cpp_write_barrier(reinterpret_cast<void**>(&entry->next), entry->next);
                newSlots[index] = reinterpret_cast<Il2CppObject*>(entry);
                il2cpp_write_barrier(reinterpret_cast<void**>(&newSlots[index]), newSlots[index]);
            }
        }
        il2cpp_write_barrier(reinterpret_cast<void**>(&ht->buckets),
                             reinterpret_cast<Il2CppObject*>(newBuckets));
        ht->buckets = newBuckets;
    }
}

//------------------------------------------------------------------------------
// Insert -- the shared body of Add (replaceExisting == false: a duplicate
// key raises ArgumentException) and the indexer set (replaceExisting ==
// true: an existing key is overwritten in place).
//------------------------------------------------------------------------------
void Insert(Il2CppHashtable* ht, Il2CppObject* key, Il2CppObject* value, bool replaceExisting)
{
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    // Resize and the entry-node allocation below can each compact the table:
    // root it for the whole body and re-read it after every allocation.
    GcRoot<Il2CppHashtable*> root(ht, "icall_hashtable_insert");
    if (ht->buckets == nullptr)
    {
        // Defensive ensure, inlined: allocate, re-read the (possibly moved)
        // table, then publish.
        Il2CppArray* arr = AllocBuckets(kMinBuckets);
        ht = root.Get();
        ht->buckets = arr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&ht->buckets),
                             reinterpret_cast<Il2CppObject*>(ht->buckets));
    }
    const int32_t hash = KeyHash(key);
    Il2CppHashEntry* existing = FindEntry(ht, key, hash);
    if (existing != nullptr)
    {
        if (!replaceExisting)
        {
            il2cpp_throw_argument_exception("Item has already been added.");
        }
        existing->value = value;
        il2cpp_write_barrier(reinterpret_cast<void**>(&existing->value), value);
        return;
    }
    if (NeedsResize(ht->count + 1, ht->buckets->length))
    {
        Resize(ht);
        ht = root.Get(); // re-derive after the (possibly collecting) resize
    }
    // The fresh entry node is allocated AFTER the (possibly collecting)
    // resize, so it exists only post-collection and cannot be reclaimed
    // while the pure-write linking below runs.
    Il2CppHashEntry* entry = reinterpret_cast<Il2CppHashEntry*>(il2cpp_object_new(&g_entryClass));
    ht = root.Get(); // re-derive after the entry allocation too
    entry->key = key;
    il2cpp_write_barrier(reinterpret_cast<void**>(&entry->key), key);
    entry->value = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&entry->value), value);
    const int32_t index = BucketIndex(hash, ht->buckets->length);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(ht->buckets->data);
    entry->next = slots[index];
    il2cpp_write_barrier(reinterpret_cast<void**>(&entry->next), entry->next);
    slots[index] = reinterpret_cast<Il2CppObject*>(entry);
    il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index]), slots[index]);
    ++ht->count;
}

//------------------------------------------------------------------------------
// SnapshotEntries -- build the Keys / Values snapshot array. 'collectValues'
// selects the value slot instead of the key slot. Both snapshots traverse
// the SAME order (bucket index ascending, then chain head -> tail), so the
// i-th key and the i-th value always describe the same entry -- the .NET
// "Keys and Values enumerate in the same order" invariant.
//
// GC safety: exactly ONE allocation (the snapshot), then a pure pointer-write
// fill with no further allocation, so no collection can reclaim the snapshot
// between its birth and the return. 'ht' stays reachable through the caller's
// 'self', and ht->buckets is re-read AFTER the allocation (a compacting
// collection may have moved it -- same convention as Resize).
//------------------------------------------------------------------------------
Il2CppArray* SnapshotEntries(Il2CppHashtable* ht, bool collectValues)
{
    // The snapshot allocation can compact and MOVE the table: root it and
    // re-read the rooted slot before touching any of its fields.
    GcRoot<Il2CppHashtable*> root(ht, "icall_hashtable_snapshot");
    if (ht->buckets == nullptr)
    {
        return AllocBuckets(0); // empty table (ctor skipped defensively)
    }
    Il2CppArray* result = AllocBuckets(ht->count);
    ht = root.Get();
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(result->data);
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(ht->buckets->data);
    const int32_t bucketCount = ht->buckets->length;
    int32_t n = 0;
    for (int32_t i = 0; i < bucketCount; ++i)
    {
        Il2CppObject* cur = slots[i];
        while (cur != nullptr)
        {
            Il2CppHashEntry* entry = AsEntry(cur);
            dst[n] = collectValues ? entry->value : entry->key;
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[n]), dst[n]);
            ++n;
            cur = entry->next;
        }
    }
    return result;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_hashtable_class -- the System.Collections.Hashtable descriptor
// (lazily registered; entry nodes use their own private descriptor).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_hashtable_class()
{
    EnsureHashtableRegistered();
    return &g_hashtableClass;
}

namespace runtime {

//------------------------------------------------------------------------------
// icall_hashtable_ctor -- Hashtable(int capacity) (see header).
//------------------------------------------------------------------------------
void icall_hashtable_ctor(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    if (capacity < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("capacity", nullptr);
    }
    Il2CppHashtable* ht = AsHashtable(self);
    // AllocBuckets below may compact and MOVE 'self' (only reachable through
    // the caller's frame slot, which is rewritten): root it and re-read the
    // (possibly moved) container before writing its fields.
    GcRoot<Il2CppObject*> root(self, "icall_hashtable_ctor");
    Il2CppArray* arr = AllocBuckets(RoundUpPow2(capacity));
    self = root.Get();
    ht = AsHashtable(self);
    ht->buckets = arr;
    il2cpp_write_barrier(reinterpret_cast<void**>(&ht->buckets),
                         reinterpret_cast<Il2CppObject*>(ht->buckets));
    ht->count = 0;
}

//------------------------------------------------------------------------------
// icall_hashtable_add -- Add(key, value) (see header).
//------------------------------------------------------------------------------
void icall_hashtable_add(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Insert(AsHashtable(self), key, value, false);
}

//------------------------------------------------------------------------------
// icall_hashtable_set -- indexer SET (see header): replace or insert.
//------------------------------------------------------------------------------
void icall_hashtable_set(Il2CppObject* self, Il2CppObject* key, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Insert(AsHashtable(self), key, value, true);
}

//------------------------------------------------------------------------------
// icall_hashtable_get -- indexer GET (see header): value or null.
//------------------------------------------------------------------------------
Il2CppObject* icall_hashtable_get(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppHashtable* ht = AsHashtable(self);
    if (ht->buckets == nullptr)
    {
        return nullptr; // empty table (ctor skipped defensively)
    }
    const Il2CppHashEntry* entry = FindEntry(ht, key, KeyHash(key));
    return (entry != nullptr) ? entry->value : nullptr;
}

//------------------------------------------------------------------------------
// icall_hashtable_contains_key -- ContainsKey(key) (see header).
//------------------------------------------------------------------------------
bool icall_hashtable_contains_key(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppHashtable* ht = AsHashtable(self);
    if (ht->buckets == nullptr)
    {
        return false;
    }
    return FindEntry(ht, key, KeyHash(key)) != nullptr;
}

//------------------------------------------------------------------------------
// icall_hashtable_contains_value -- ContainsValue(value): the O(n) linear
// scan over every entry chain comparing values with object equality (the
// .NET contract; null matches a stored null).
//------------------------------------------------------------------------------
bool icall_hashtable_contains_value(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppHashtable* ht = AsHashtable(self);
    if (ht->buckets == nullptr)
    {
        return false;
    }
    Il2CppObject* const* slots = reinterpret_cast<Il2CppObject* const*>(ht->buckets->data);
    const int32_t bucketCount = ht->buckets->length;
    for (int32_t i = 0; i < bucketCount; ++i)
    {
        for (Il2CppObject* cur = slots[i]; cur != nullptr;)
        {
            Il2CppHashEntry* entry = AsEntry(cur);
            if (value == nullptr ? entry->value == nullptr
                                 : KeyEquals(entry->value, value))
            {
                return true;
            }
            cur = entry->next;
        }
    }
    return false;
}

//------------------------------------------------------------------------------
// icall_hashtable_remove -- Remove(key) (see header): unlink the pair; a
// missing key is a silent success.
//------------------------------------------------------------------------------
void icall_hashtable_remove(Il2CppObject* self, Il2CppObject* key)
{
    ThrowIfNullReceiver(self);
    if (key == nullptr)
    {
        il2cpp_throw_argument_null_exception("key");
    }
    Il2CppHashtable* ht = AsHashtable(self);
    if (ht->buckets == nullptr)
    {
        return; // empty table: nothing to remove
    }
    const int32_t hash = KeyHash(key);
    const int32_t index = BucketIndex(hash, ht->buckets->length);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(ht->buckets->data);
    Il2CppHashEntry* prev = nullptr;
    Il2CppObject* cur = slots[index];
    while (cur != nullptr)
    {
        Il2CppHashEntry* entry = AsEntry(cur);
        if (KeyEquals(entry->key, key))
        {
            if (prev == nullptr)
            {
                slots[index] = entry->next;
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[index]), slots[index]);
            }
            else
            {
                prev->next = entry->next;
                il2cpp_write_barrier(reinterpret_cast<void**>(&prev->next), prev->next);
            }
            entry->next = nullptr; // detach: the node becomes unreachable
            il2cpp_write_barrier(reinterpret_cast<void**>(&entry->next), nullptr);
            --ht->count;
            return;
        }
        prev = entry;
        cur = entry->next;
    }
    // Absent key: .NET Remove is a silent success -- nothing to do.
}

//------------------------------------------------------------------------------
// icall_hashtable_get_count -- Count (see header).
//------------------------------------------------------------------------------
int32_t icall_hashtable_get_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsHashtable(self)->count;
}

//------------------------------------------------------------------------------
// icall_hashtable_get_keys -- Keys (see header): a snapshot of every key in
// the table's enumeration order.
//------------------------------------------------------------------------------
Il2CppObject* icall_hashtable_get_keys(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(SnapshotEntries(AsHashtable(self), false));
}

//------------------------------------------------------------------------------
// icall_hashtable_get_values -- Values (see header): a snapshot of every
// value (stored nulls included) in the SAME order as Keys.
//------------------------------------------------------------------------------
Il2CppObject* icall_hashtable_get_values(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(SnapshotEntries(AsHashtable(self), true));
}

//------------------------------------------------------------------------------
// icall_hashtable_clear -- Clear() (see header): drop every pair.
//------------------------------------------------------------------------------
void icall_hashtable_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppHashtable* ht = AsHashtable(self);
    if (ht->buckets != nullptr)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(ht->buckets->data);
        const int32_t bucketCount = ht->buckets->length;
        for (int32_t i = 0; i < bucketCount; ++i)
        {
            if (slots[i] != nullptr)
            {
                slots[i] = nullptr;
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), nullptr);
            }
        }
    }
    ht->count = 0;
}

} // namespace runtime
} // namespace il2cpp
