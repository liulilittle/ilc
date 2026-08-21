//------------------------------------------------------------------------------
// test_hashtable.cpp -- non-generic hashtable runtime unit tests
// (il2cpp_hashtable.h).
//
// Coverage (normal + edge + error paths, per the domain brief):
//   1. Ctor: bucket count is the smallest power of two >= capacity (min 4),
//      count starts at 0; a negative capacity raises ArgumentOutOfRange.
//   2. Add + get with string keys; two DISTINCT instances with equal content
//      resolve to the same entry (ordinal hash + equality consistency).
//   3. Indexer set: replaces an existing key's value (count unchanged) and
//      inserts when the key is absent.
//   4. Get of a missing key returns null; a STORED null value also returns
//      null (.NET indexer semantics -- both cases are indistinguishable).
//   5. ContainsKey true/false.
//   6. Remove: unlinks an existing key (count decrements); a missing key is
//      a silent success.
//   7. Clear: count 0, lookups miss; the table is reusable afterwards.
//   8. Resize: with the 0.72 load factor the bucket array doubles exactly at
//      the 3rd insert (4 -> 8) and the 6th (8 -> 16); every entry of a
//      100-key table is still findable after multiple doublings.
//   9. Duplicate Add raises ArgumentException and leaves the table
//      unchanged; set() on the same key replaces instead.
//  10. A null key raises ArgumentNullException from add/set/get/
//      contains/remove.
//  11. Boxed Int32 keys: value equality (a freshly boxed 5 finds an entry
//      stored under another boxed 5); distinct values stay distinct.
//  12. Address-hash keys (non-string, non-boxed objects): same pointer
//      resolves, a different pointer does not (reference semantics).
//  13. GC survival: a rooted hashtable whose keys/values are reachable ONLY
//      through the table survives a full collection with every pair still
//      findable -- proves the refFieldBitmap (buckets slot) and the
//      reference-array scan keep the whole contents alive. NOTE: the
//      collector COMPACTS Gen0 (survivors move; references are rewritten),
//      so post-collect assertions use fresh lookups + move-safe CONTENT
//      equality, never stale local pointers.
//  14. Address-hashed key across a compacting collection: the moved key
//      leaves its entry in a stale bucket; the next lookup misses the
//      primary bucket and heals the entry via FindEntry's repair scan.
//  15. Keys/Values snapshots: same enumeration order (Keys[i] and Values[i]
//      describe the same entry), stable across a collection; empty table ->
//      empty arrays; a later Add is not reflected.
//  16-19. Boxed primitive keys (D-HT-1): System.Double / System.Char /
//      System.Boolean / System.Single all hash + compare BY VALUE -- a fresh
//      box resolves an entry stored under another box of the same value;
//      distinct values stay distinct; +-0.0 hash+equal as one key; NaN equals
//      NaN; a boxed Int32 65 and a boxed Char 'A' are DISTINCT keys (different
//      runtime types, .NET object equality).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_hashtable.h"

#include "il2cpp_boxing_ext.h"  // icall_box_bool / icall_box_char / icall_box_double
#include "il2cpp_boxing_ext2.h" // icall_box_float
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>

namespace {

static_assert(sizeof(void*) == 8, "hashtable test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// The exception-factory chains (see il2cpp_exception_ext.h): the type_name()
// of a caught DotNetException is the whole chain as thrown.
const char* kArgumentNullChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char* kArgumentChain =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
const char* kArgumentOutOfRangeChain =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";

// Raise-and-catch helper: run 'fn', capture the managed exception carrier.
// Returns true when a DotNetException with the expected chain was thrown.
template <typename Fn>
bool ThrowsDotNet(Fn fn, const char* expectedChain)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
}

// Create a hashtable through the icall path (managed allocation + capacity
// ctor), mirroring what generated newobj/ctor code does.
il2cpp::Il2CppHashtable* MakeHashtable(int32_t capacity)
{
    il2cpp::Il2CppHashtable* h = reinterpret_cast<il2cpp::Il2CppHashtable*>(
        il2cpp::il2cpp_object_new(il2cpp::il2cpp_hashtable_class()));
    il2cpp::runtime::icall_hashtable_ctor(reinterpret_cast<il2cpp::Il2CppObject*>(h), capacity);
    return h;
}

il2cpp::Il2CppObject* Str(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Move-safe content check: does 'obj' hold a managed string whose UTF-16
// units equal the ASCII text? Used after collections, where local pointers
// are STALE (the compacting collector moves Gen0 survivors) -- content
// equality is the only comparison that stays valid across a move.
bool IsContent(il2cpp::Il2CppObject* obj, const char* ascii)
{
    if (obj == nullptr || obj->klass == nullptr ||
        std::strcmp(obj->klass->name, "System.String") != 0)
    {
        return false;
    }
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    // --- 1. Ctor: power-of-two bucket count >= capacity (min 4) ---
    {
        const int32_t expected[6] = { 4, 4, 4, 8, 8, 16 }; // capacity 0,1,4,5,8,9
        const int32_t capacities[6] = { 0, 1, 4, 5, 8, 9 };
        bool sizesOk = true;
        for (int32_t i = 0; i < 6; ++i)
        {
            il2cpp::Il2CppHashtable* h = MakeHashtable(capacities[i]);
            if (h->buckets->length != expected[i]) sizesOk = false;
            if (il2cpp::runtime::icall_hashtable_get_count(reinterpret_cast<il2cpp::Il2CppObject*>(h)) != 0)
            {
                sizesOk = false;
            }
        }
        Expect(sizesOk, "ctor bucket count = power of two >= capacity (min 4), count starts at 0");

        bool o1 = ThrowsDotNet([]() { MakeHashtable(-1); }, kArgumentOutOfRangeChain);
        bool o2 = ThrowsDotNet([]() { MakeHashtable(-100); }, kArgumentOutOfRangeChain);
        Expect(o1 && o2, "negative capacity raises ArgumentOutOfRangeException");
    }

    // --- 2. Add + get with string keys; equal content resolves (hash
    //    consistency: the lookup key is a DIFFERENT instance) ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* v1 = Str("alpha-value");
        il2cpp::runtime::icall_hashtable_add(hobj, Str("alpha"), v1);
        il2cpp::runtime::icall_hashtable_add(hobj, Str("beta"), Str("beta-value"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 2, "two adds -> count 2");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("alpha")) == v1,
               "get with an equal-content NEW string finds the entry (ordinal hash)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("beta")) != nullptr,
               "get finds the second key");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("gamma")) == nullptr,
               "get of an absent key returns null");
    }

    // --- 3. Indexer set: replace (count unchanged) + insert when absent ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* rep = Str("replaced");
        il2cpp::runtime::icall_hashtable_add(hobj, Str("k"), Str("original"));
        il2cpp::runtime::icall_hashtable_set(hobj, Str("k"), rep);
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 1,
               "set on an existing key keeps count at 1");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("k")) == rep,
               "set replaces the stored value");
        il2cpp::runtime::icall_hashtable_set(hobj, Str("new"), Str("inserted"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 2,
               "set with an absent key inserts (count 2)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("new")) != nullptr,
               "inserted key is findable");
    }

    // --- 4. Get: missing -> null; stored null value -> null (indexer
    //    semantics) ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::runtime::icall_hashtable_add(hobj, Str("nv"), nullptr); // null value is legal
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("nv")) == nullptr,
               "a stored null value reads back as null");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("absent")) == nullptr,
               "a missing key reads back as null (same observable)");
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 1,
               "null-value pair still counts");
    }

    // --- 5. ContainsKey ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::runtime::icall_hashtable_add(hobj, Str("yes"), Str("v"));
        Expect(il2cpp::runtime::icall_hashtable_contains_key(hobj, Str("yes")), "ContainsKey true for a present key");
        Expect(!il2cpp::runtime::icall_hashtable_contains_key(hobj, Str("no")), "ContainsKey false for an absent key");
    }

    // --- 6. Remove: unlink existing; missing key is a silent success ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::runtime::icall_hashtable_add(hobj, Str("a"), Str("1"));
        il2cpp::runtime::icall_hashtable_add(hobj, Str("b"), Str("2"));
        il2cpp::runtime::icall_hashtable_remove(hobj, Str("a"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 1, "remove decrements count");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("a")) == nullptr, "removed key is gone");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("b")) != nullptr,
               "the sibling key survives the unlink (chain integrity)");
        il2cpp::runtime::icall_hashtable_remove(hobj, Str("missing")); // must not throw
        il2cpp::runtime::icall_hashtable_remove(hobj, Str("b"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 0,
               "removing the chain head and a missing key keeps the table consistent");
    }

    // --- 7. Clear + reuse ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        for (int32_t i = 0; i < 8; ++i)
        {
            il2cpp::runtime::icall_hashtable_set(hobj, Str("k-i"), Str("v-i"));
        }
        il2cpp::runtime::icall_hashtable_clear(hobj);
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 0, "clear resets count to 0");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("k-i")) == nullptr,
               "cleared keys are gone");
        il2cpp::runtime::icall_hashtable_add(hobj, Str("after"), Str("ok"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 1 &&
               il2cpp::runtime::icall_hashtable_get(hobj, Str("after")) != nullptr,
               "the table is reusable after clear");
    }

    // --- 8. Resize: load factor 0.72 doubles 4->8 on the 3rd insert and
    //    8->16 on the 6th; a 100-key table stays fully findable ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::runtime::icall_hashtable_add(hobj, Str("n1"), Str("v1"));
        il2cpp::runtime::icall_hashtable_add(hobj, Str("n2"), Str("v2"));
        Expect(h->buckets->length == 4, "two entries fit in the initial 4 buckets");
        il2cpp::runtime::icall_hashtable_add(hobj, Str("n3"), Str("v3"));
        Expect(h->buckets->length == 8, "3rd insert (3*100 > 4*72) doubles to 8 buckets");
        il2cpp::runtime::icall_hashtable_add(hobj, Str("n4"), Str("v4"));
        il2cpp::runtime::icall_hashtable_add(hobj, Str("n5"), Str("v5"));
        Expect(h->buckets->length == 8, "fourth and fifth inserts fit in 8 buckets");
        il2cpp::runtime::icall_hashtable_add(hobj, Str("n6"), Str("v6"));
        Expect(h->buckets->length == 16, "6th insert (6*100 > 8*72) doubles to 16 buckets");
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 6, "count tracks inserts across resizes");

        // 100 keys, ~100 distinct strings (collisions force real rehashing).
        il2cpp::Il2CppHashtable* big = MakeHashtable(0);
        il2cpp::Il2CppObject* bigObj = reinterpret_cast<il2cpp::Il2CppObject*>(big);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&bigObj)); // many allocations ahead
        char keyBuf[32];
        char valBuf[32];
        for (int32_t i = 0; i < 100; ++i)
        {
            std::snprintf(keyBuf, sizeof(keyBuf), "key-%03d", i);
            std::snprintf(valBuf, sizeof(valBuf), "val-%03d", i);
            il2cpp::runtime::icall_hashtable_add(bigObj, Str(keyBuf), Str(valBuf));
        }
        Expect(big->buckets->length > 16, "100 entries grew the bucket array well past 16");
        Expect(il2cpp::runtime::icall_hashtable_get_count(bigObj) == 100, "100 entries counted");
        bool allFound = true;
        for (int32_t i = 0; i < 100; ++i)
        {
            std::snprintf(keyBuf, sizeof(keyBuf), "key-%03d", i);
            if (il2cpp::runtime::icall_hashtable_get(bigObj, Str(keyBuf)) == nullptr) allFound = false;
        }
        Expect(allFound, "every key of a 100-entry table is findable after multiple resizes");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&bigObj));
    }

    // --- 9. Duplicate Add raises ArgumentException; set replaces instead ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* v1 = Str("first");
        il2cpp::runtime::icall_hashtable_add(hobj, Str("dup"), v1);
        bool dup = ThrowsDotNet([&]() { il2cpp::runtime::icall_hashtable_add(hobj, Str("dup"), Str("second")); },
                                kArgumentChain);
        Expect(dup, "duplicate Add raises ArgumentException");
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 1,
               "failed duplicate Add leaves count unchanged");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("dup")) == v1,
               "failed duplicate Add leaves the original value unchanged");
        il2cpp::runtime::icall_hashtable_set(hobj, Str("dup"), Str("second"));
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, Str("dup")) != v1,
               "set() on the duplicate key replaces instead of throwing");
    }

    // --- 10. Null key raises ArgumentNullException from every entry point ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* v = Str("v");
        bool o1 = ThrowsDotNet([&]() { il2cpp::runtime::icall_hashtable_add(hobj, nullptr, v); }, kArgumentNullChain);
        bool o2 = ThrowsDotNet([&]() { il2cpp::runtime::icall_hashtable_set(hobj, nullptr, v); }, kArgumentNullChain);
        bool o3 = ThrowsDotNet([&]() { il2cpp::runtime::icall_hashtable_get(hobj, nullptr); }, kArgumentNullChain);
        bool o4 = ThrowsDotNet([&]() { il2cpp::runtime::icall_hashtable_contains_key(hobj, nullptr); }, kArgumentNullChain);
        bool o5 = ThrowsDotNet([&]() { il2cpp::runtime::icall_hashtable_remove(hobj, nullptr); }, kArgumentNullChain);
        Expect(o1 && o2 && o3 && o4 && o5,
               "null key raises ArgumentNullException from add/set/get/contains/remove");
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 0,
               "rejected null-key calls leave the table untouched");
    }

    // --- 11. Boxed Int32 keys: VALUE equality (a fresh box finds the
    //    stored box) ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* fiveVal = Str("five");
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::il2cpp_box_int32(nullptr, 5), fiveVal);
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::il2cpp_box_int32(nullptr, 6), Str("six"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 2, "two boxed int keys stored");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::il2cpp_box_int32(nullptr, 5)) == fiveVal,
               "a freshly boxed 5 resolves the entry stored under another boxed 5 (value hash)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::il2cpp_box_int32(nullptr, 6)) != nullptr,
               "a freshly boxed 6 resolves its entry");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::il2cpp_box_int32(nullptr, 7)) == nullptr,
               "a boxed 7 is a distinct key (no false hit)");
        il2cpp::runtime::icall_hashtable_set(hobj, il2cpp::il2cpp_box_int32(nullptr, 6), Str("six-2"));
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::il2cpp_box_int32(nullptr, 6)) != fiveVal,
               "set replaces a boxed-int entry");
    }

    // --- 12. Address-hash keys (plain managed objects): reference identity ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj)); // allocations follow
        il2cpp::Il2CppObject* keyObj = il2cpp::il2cpp_object_new(il2cpp::il2cpp_hashtable_class());
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keyObj)); // no collect may reclaim it
        il2cpp::Il2CppObject* addrVal = Str("addr-value");
        il2cpp::runtime::icall_hashtable_add(hobj, keyObj, addrVal);
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, keyObj) == addrVal,
               "the same object pointer resolves its entry (address hash)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::il2cpp_object_new(il2cpp::il2cpp_hashtable_class())) == nullptr,
               "a different object pointer is a different key (reference semantics)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keyObj));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 13. GC survival: keys/values reachable ONLY through the rooted
    //    table survive a full collection ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::runtime::icall_hashtable_add(hobj, Str("gc-key-1"), Str("gc-value-1"));
        il2cpp::runtime::icall_hashtable_add(hobj, Str("gc-key-2"), Str("gc-value-2"));
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::il2cpp_box_int32(nullptr, 99), Str("gc-boxed"));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));
        il2cpp::il2cpp_gc_collect();
        // NOTE: the compacting collector MOVES Gen0 survivors and rewrites
        // every reference, so pre-collect local key/value pointers are
        // STALE afterwards; every check below uses FRESH lookups and
        // move-safe CONTENT equality instead of pointer comparison.
        Expect(il2cpp::il2cpp_gc_get_generation(hobj) >= il2cpp::kGcGeneration1,
               "rooted hashtable survives collection (promoted)");
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 3,
               "count intact after collection");
        Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, Str("gc-key-1")), "gc-value-1"),
               "string key/value survived via the bucket-array bitmap scan");
        Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, Str("gc-key-2")), "gc-value-2"),
               "second pair survived (entry chain scanned)");
        Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::il2cpp_box_int32(nullptr, 99)), "gc-boxed"),
               "boxed-int entry survived (value hash still resolves)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 14. Address-hashed key across a compacting collection: the moved
    //    key leaves its entry in a STALE bucket; the next lookup misses the
    //    primary bucket and heals the entry via FindEntry's repair scan ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* keyObj = il2cpp::il2cpp_object_new(il2cpp::il2cpp_hashtable_class());
        il2cpp::Il2CppObject* addrVal = Str("addr-gc-value");
        il2cpp::runtime::icall_hashtable_add(hobj, keyObj, addrVal);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keyObj));
        il2cpp::il2cpp_gc_collect();
        // keyObj was rewritten to its new address by the collector; the
        // entry still sits in the bucket of the OLD address. The lookup
        // below misses the primary bucket, finds the entry in the stale
        // bucket, and relinks it -- the table heals on access.
        Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, keyObj), "addr-gc-value"),
               "address-hashed key stays findable across compaction (repair-on-miss)");
        Expect(il2cpp::runtime::icall_hashtable_contains_key(hobj, keyObj),
               "ContainsKey finds the healed entry (already relinked)");
        Expect(IsContent(il2cpp::runtime::icall_hashtable_get(hobj, keyObj), "addr-gc-value"),
               "repeated lookups resolve the healed entry");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keyObj));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 15. Keys / Values snapshots: same enumeration order (net48
    //    invariant), stable across a collection; empty table -> empty arrays
    //    ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::runtime::icall_hashtable_add(hobj, Str("zebra"), Str("z-val"));
        il2cpp::runtime::icall_hashtable_add(hobj, Str("alpha"), Str("a-val"));
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::il2cpp_box_int32(nullptr, 7), Str("seven"));
        il2cpp::runtime::icall_hashtable_add(hobj, Str("nullv"), nullptr); // stored null value
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hobj));

        il2cpp::Il2CppArray* keys = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_hashtable_get_keys(hobj));
        il2cpp::Il2CppArray* vals = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_hashtable_get_values(hobj));
        // Root the snapshots: between the two icall allocations a collection
        // could otherwise reclaim a snapshot that only a local references.
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keys));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&vals));

        Expect(keys->length == 4 && vals->length == 4,
               "Keys and Values snapshots each hold exactly the entry count");
        bool aligned = true;
        for (int32_t i = 0; i < keys->length; ++i)
        {
            il2cpp::Il2CppObject* const* kp =
                reinterpret_cast<il2cpp::Il2CppObject* const*>(keys->data);
            il2cpp::Il2CppObject* const* vp =
                reinterpret_cast<il2cpp::Il2CppObject* const*>(vals->data);
            // .NET invariant: Keys[i] and Values[i] describe the same entry.
            if (il2cpp::runtime::icall_hashtable_get(hobj, kp[i]) != vp[i]) aligned = false;
        }
        Expect(aligned, "Keys[i] resolves to Values[i] (same enumeration order)");

        il2cpp::Il2CppObject* const* kp =
            reinterpret_cast<il2cpp::Il2CppObject* const*>(keys->data);
        il2cpp::Il2CppObject* const* vp =
            reinterpret_cast<il2cpp::Il2CppObject* const*>(vals->data);
        Expect(IsContent(kp[0], "alpha") && IsContent(kp[1], "nullv") &&
               IsContent(kp[2], "zebra") && kp[3] != nullptr,
               "Keys holds every key in the table (bucket-chain order)");
        Expect(vp[1] == nullptr,
               "a stored null value appears as null in the Values snapshot");

        // Snapshot semantics: a mutation AFTER the call is not reflected.
        il2cpp::runtime::icall_hashtable_add(hobj, Str("late"), Str("late-val"));
        Expect(keys->length == 4,
               "a Keys snapshot does not reflect a later Add (snapshot, not view)");

        // Stability across a compacting collection: the collector rewrites the
        // snapshot array elements, so fresh reads stay aligned.
        il2cpp::il2cpp_gc_collect();
        bool survived = true;
        for (int32_t i = 0; i < keys->length; ++i)
        {
            il2cpp::Il2CppObject* const* kp2 =
                reinterpret_cast<il2cpp::Il2CppObject* const*>(keys->data);
            il2cpp::Il2CppObject* const* vp2 =
                reinterpret_cast<il2cpp::Il2CppObject* const*>(vals->data);
            if (il2cpp::runtime::icall_hashtable_get(hobj, kp2[i]) != vp2[i]) survived = false;
        }
        Expect(survived, "Keys/Values stay aligned across a compacting collection");

        // Empty-table snapshots: a ctor'd empty table and a defensively-empty
        // (ctor-skipped) object both yield length-0 arrays.
        il2cpp::Il2CppHashtable* empty = MakeHashtable(0);
        il2cpp::Il2CppObject* eobj = reinterpret_cast<il2cpp::Il2CppObject*>(empty);
        il2cpp::Il2CppArray* ek = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_hashtable_get_keys(eobj));
        il2cpp::Il2CppArray* ev = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_hashtable_get_values(eobj));
        Expect(ek->length == 0 && ev->length == 0,
               "an empty table yields length-0 Keys and Values snapshots");
        il2cpp::Il2CppObject* raw = il2cpp::il2cpp_object_new(il2cpp::il2cpp_hashtable_class());
        il2cpp::Il2CppArray* rk = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::icall_hashtable_get_keys(raw));
        Expect(rk != nullptr && rk->length == 0,
               "a ctor-skipped hashtable (null buckets) yields a length-0 Keys snapshot");

        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&vals));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keys));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hobj));
    }

    // --- 16. Boxed Double keys: VALUE equality (D-HT-1; net48
    //    Double.GetHashCode + Double.Equals) ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* fiveVal = Str("d-five");
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_double(5.0), fiveVal);
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_double(-2.5), Str("d-neg"));
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_double(0.0), Str("d-zero"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 3, "three boxed double keys stored");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(5.0)) == fiveVal,
               "a freshly boxed 5.0 resolves the entry stored under another boxed 5.0 (value hash)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(-2.5)) != nullptr,
               "a freshly boxed -2.5 resolves its entry");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(7.25)) == nullptr,
               "a distinct double value is a distinct key (no false hit)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(-0.0)) != nullptr,
               "boxed -0.0 resolves the entry stored under +0.0 (Double.Equals)");
        const double nanKey = std::numeric_limits<double>::quiet_NaN();
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_double(nanKey), Str("d-nan"));
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(nanKey)) != nullptr,
               "boxed NaN resolves the entry stored under the same NaN value (Double.Equals)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(1.0)) == nullptr,
               "NaN and a finite value are distinct keys");
        il2cpp::runtime::icall_hashtable_remove(hobj, il2cpp::runtime::icall_box_double(5.0));
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(5.0)) == nullptr &&
               il2cpp::runtime::icall_hashtable_get_count(hobj) == 3,
               "Remove with a freshly boxed 5.0 unlinks the boxed-double entry");
    }

    // --- 17. Boxed Char keys: VALUE equality (D-HT-1) ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* aVal = Str("char-a");
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_char('A'), aVal);
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_char('B'), Str("char-b"));
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_char('A')) == aVal,
               "a freshly boxed 'A' resolves the entry stored under another boxed 'A' (value hash)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_char('B')) != nullptr,
               "a freshly boxed 'B' resolves its entry");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_char('a')) == nullptr,
               "a different char is a distinct key (ordinal)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_char(0x41)) == aVal,
               "the UTF-16 code unit is the char key (65 == 'A')");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::il2cpp_box_int32(nullptr, 65)) == nullptr,
               "boxed Int32 65 and boxed Char 'A' are distinct keys (different runtime types)");
    }

    // --- 18. Boxed Boolean keys: VALUE equality (D-HT-1) ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* tVal = Str("bool-t");
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_bool(true), tVal);
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_bool(true)) == tVal,
               "a freshly boxed true resolves the entry stored under another boxed true (value hash)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_bool(false)) == nullptr,
               "boxed false is a distinct key from boxed true");
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_bool(false), Str("bool-f"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 2 &&
               il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_bool(false)) != nullptr,
               "boxed false stores and resolves as its own key");
    }

    // --- 19. Boxed Single (float) keys: VALUE equality (D-HT-1; the box
    //    stores the float as a double, boxingext2.cpp icall_box_float) ---
    {
        il2cpp::Il2CppHashtable* h = MakeHashtable(0);
        il2cpp::Il2CppObject* hobj = reinterpret_cast<il2cpp::Il2CppObject*>(h);
        il2cpp::Il2CppObject* f5 = Str("f-five");
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_float(5.0), f5);
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_float(-3.5), Str("f-neg"));
        il2cpp::runtime::icall_hashtable_add(hobj, il2cpp::runtime::icall_box_float(0.0), Str("f-zero"));
        Expect(il2cpp::runtime::icall_hashtable_get_count(hobj) == 3, "three boxed float keys stored");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_float(5.0)) == f5,
               "a freshly boxed 5.0f resolves the entry stored under another boxed 5.0f (Single value hash)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_float(-3.5)) != nullptr,
               "a freshly boxed -3.5f resolves its entry");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_float(9.9)) == nullptr,
               "a distinct float value is a distinct key (no false hit)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_float(-0.0)) != nullptr,
               "boxed -0.0f resolves the entry stored under +0.0f (Single.Equals)");
        Expect(il2cpp::runtime::icall_hashtable_get(hobj, il2cpp::runtime::icall_box_double(5.0)) == nullptr,
               "boxed Double 5.0 and boxed Single 5.0f are distinct keys (different runtime types)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d hashtable assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime hashtable tests passed\n");
    return 0;
}
