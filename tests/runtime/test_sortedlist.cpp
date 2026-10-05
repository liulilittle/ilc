//------------------------------------------------------------------------------
// test_sortedlist.cpp -- non-generic sorted-list runtime unit tests
// (sortedlist.h).
//
// Coverage (normal + edge + error paths, per the domain brief):
//   1. Ctor: capacity = smallest power of two >= argument (min 4), count
//      starts at 0; a negative capacity raises ArgumentOutOfRange.
//   2. INSERT ORDER != ITERATION ORDER: adding "charlie","alpha","bravo"
//      iterates alpha, bravo, charlie (by-key ascending) with the values
//      index-aligned; get_by_key / index_of_key agree with the sort.
//   3. Duplicate Add raises ArgumentException and leaves the list unchanged
//      (count and original value intact).
//   4. get_by_key: present -> value; missing -> null; a stored null value
//      also reads back as null (.NET indexer semantics).
//   5. index_of_key: present -> its sorted index; absent -> -1.
//   6. Index access: get_key_at / get_value_at / get_by_index agree with the
//      sorted order; out-of-range indices (negative, == Count) raise
//      ArgumentOutOfRangeException from every index-taking entry point.
//   7. set_at: replaces the value at an index (count unchanged, key order
//      untouched, lookups see the new value).
//   8. remove_at: removes by index, order preserved, count decrements;
//      removing down to empty and re-adding keeps the list reusable.
//   9. Boxed Int32 keys sort NUMERICALLY (1, 2, 10, 100 -- not lexically);
//      a freshly boxed key resolves entries by value.
//  10. Mixed int + string keys: ints sort before strings (deterministic
//      total order documented in the header).
//  11. Growth: capacity doubles exactly at full (4 -> 8); a 100-key table
//      iterates fully sorted and stays fully findable after multiple
//      doublings.
//  12. clear: count 0, capacity unchanged, lookups miss; reusable.
//  13. GC survival: a rooted list whose keys/values are reachable ONLY
//      through the items array survives a full collection with every pair
//      intact and still sorted. NOTE: the collector COMPACTS Gen0 (survivors
//      move; references are rewritten), so post-collect assertions use fresh
//      lookups + move-safe CONTENT equality, never stale local pointers.
//  Error paths: null key -> ArgumentNullException from add/get_by_key/
//      index_of_key; negative ctor capacity -> ArgumentOutOfRangeException.
//
// Plain executable + minimal assertion harness (see test_hashtable.cpp).
//------------------------------------------------------------------------------

#include "sortedlist.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "sortedlist test fixture assumes 8-byte pointers");

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

// Create a sorted list through the icall path (managed allocation + capacity
// ctor), mirroring what generated newobj/ctor code does.
il2cpp::Il2CppSortedList* MakeSortedList(int32_t capacity)
{
    il2cpp::Il2CppSortedList* sl = reinterpret_cast<il2cpp::Il2CppSortedList*>(
        il2cpp::il2cpp_object_new(il2cpp::il2cpp_sortedlist_class()));
    il2cpp::runtime::il2cpp_sortedlist_create(reinterpret_cast<il2cpp::Il2CppObject*>(sl), capacity);
    return sl;
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
    // --- 1. Ctor: power-of-two capacity >= argument (min 4), count 0 ---
    {
        const int32_t expected[6] = { 4, 4, 4, 8, 8, 16 }; // capacity 0,1,4,5,8,9
        const int32_t capacities[6] = { 0, 1, 4, 5, 8, 9 };
        bool sizesOk = true;
        for (int32_t i = 0; i < 6; ++i)
        {
            il2cpp::Il2CppSortedList* sl = MakeSortedList(capacities[i]);
            il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
            if (il2cpp::runtime::il2cpp_sortedlist_get_capacity(sobj) != expected[i]) sizesOk = false;
            if (il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) != 0) sizesOk = false;
        }
        Expect(sizesOk, "ctor capacity = power of two >= argument (min 4), count starts at 0");

        bool o1 = ThrowsDotNet([]() { MakeSortedList(-1); }, kArgumentOutOfRangeChain);
        bool o2 = ThrowsDotNet([]() { MakeSortedList(-100); }, kArgumentOutOfRangeChain);
        Expect(o1 && o2, "negative capacity raises ArgumentOutOfRangeException");
    }

    // --- 2. Insert order != iteration order: keys iterate ASCENDING ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("charlie"), Str("c-value"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("alpha"), Str("a-value"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("bravo"), Str("b-value"));
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 3,
               "three adds -> count 3");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 0), "alpha") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 1), "bravo") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 2), "charlie"),
               "iteration is by-key ascending (insert order != iteration order)");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_value_at(sobj, 0), "a-value") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_value_at(sobj, 1), "b-value") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_value_at(sobj, 2), "c-value"),
               "values are index-aligned with their sorted keys");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_index(sobj, 1), "b-value"),
               "GetByIndex returns the value at the sorted index");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("charlie")), "c-value"),
               "get_by_key resolves by an equal-content NEW string (ordinal compare)");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("zzz")) == nullptr,
               "get_by_key of an absent key returns null");
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("bravo")) == 1 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("alpha")) == 0 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("charlie")) == 2,
               "index_of_key returns the sorted index");
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("zzz")) == -1,
               "index_of_key of an absent key returns -1");
    }

    // --- 3. Duplicate Add raises ArgumentException; the list is unchanged ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::Il2CppObject* v1 = Str("first");
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("dup"), v1);
        bool dup = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("dup"), Str("second")); },
                                kArgumentChain);
        Expect(dup, "duplicate Add raises ArgumentException");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 1,
               "failed duplicate Add leaves count unchanged");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("dup")) == v1,
               "failed duplicate Add leaves the original value unchanged");
    }

    // --- 4. get_by_key: present -> value; missing -> null; stored null
    //    value -> null (indexer semantics) ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("nv"), nullptr); // null value is legal
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("nv")) == nullptr,
               "a stored null value reads back as null");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("absent")) == nullptr,
               "a missing key reads back as null (same observable)");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 1,
               "null-value pair still counts");
    }

    // --- 5. index_of_key: present -> sorted index; absent -> -1 ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("b"), Str("v-b"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("a"), Str("v-a"));
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("a")) == 0,
               "index_of_key finds a key at its sorted position");
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("b")) == 1,
               "index_of_key finds a key at its sorted position (2)");
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("c")) == -1,
               "index_of_key of an absent key returns -1");
    }

    // --- 6. Index access: get_key_at/get_value_at/get_by_index agree;
    //    out-of-range raises ArgumentOutOfRangeException everywhere ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("k1"), Str("v1"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("k2"), Str("v2"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("k3"), Str("v3"));
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 2), "k3") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_value_at(sobj, 2), "v3") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_index(sobj, 2), "v3"),
               "get_key_at / get_value_at / get_by_index agree at a sorted index");
        bool o1 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, -1); }, kArgumentOutOfRangeChain);
        bool o2 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_get_value_at(sobj, 3); }, kArgumentOutOfRangeChain);
        bool o3 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_get_by_index(sobj, 3); }, kArgumentOutOfRangeChain);
        bool o4 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_set_at(sobj, -1, Str("x")); }, kArgumentOutOfRangeChain);
        bool o5 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_remove_at(sobj, 3); }, kArgumentOutOfRangeChain);
        Expect(o1 && o2 && o3 && o4 && o5,
               "out-of-range index raises ArgumentOutOfRangeException from get_key_at/get_value_at/get_by_index/set_at/remove_at");
    }

    // --- 7. set_at: replace the value at an index (count + order intact) ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::Il2CppObject* rep = Str("replaced");
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("k1"), Str("v1"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("k2"), Str("v2"));
        il2cpp::runtime::il2cpp_sortedlist_set_at(sobj, 1, rep);
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 2,
               "set_at keeps count unchanged");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_value_at(sobj, 1) == rep,
               "set_at replaces the value at the index");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("k2")) == rep,
               "the replaced value is visible through get_by_key");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 0), "k1"),
               "set_at leaves the key order untouched");
    }

    // --- 8. remove_at: remove by index, order preserved; empty + reuse ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("a"), Str("1"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("b"), Str("2"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("c"), Str("3"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("d"), Str("4"));
        il2cpp::runtime::il2cpp_sortedlist_remove_at(sobj, 1); // removes "b"
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 3,
               "remove_at decrements count");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 0), "a") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 1), "c") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 2), "d"),
               "remaining keys stay sorted after remove_at");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("b")) == nullptr,
               "the removed key is gone");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("d")) != nullptr,
               "the tail key survives a head-region removal");
        il2cpp::runtime::il2cpp_sortedlist_remove_at(sobj, 0);
        il2cpp::runtime::il2cpp_sortedlist_remove_at(sobj, 1); // now "c","d" -> "c"
        il2cpp::runtime::il2cpp_sortedlist_remove_at(sobj, 0);
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 0,
               "removing down to empty keeps the list consistent");
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("after"), Str("ok"));
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 1 &&
               il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("after")) != nullptr,
               "the list is reusable after removing everything");
    }

    // --- 9. Boxed Int32 keys sort NUMERICALLY; fresh boxes resolve by value ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 10), Str("ten"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 2), Str("two"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 1), Str("one"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 100), Str("hundred"));
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 4, "four boxed int keys stored");
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 1)) == 0 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 2)) == 1 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 10)) == 2 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 100)) == 3,
               "int keys sort numerically (1,2,10,100 -- not lexically)");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 10)), "ten"),
               "a freshly boxed 10 resolves its entry (value comparison)");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 7)) == nullptr,
               "a boxed 7 is absent (no false hit)");
    }

    // --- 10. Mixed int + string keys: ints sort before strings ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 2), Str("two-int"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("b"), Str("b-str"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 10), Str("ten-int"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("a"), Str("a-str"));
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 2)) == 0 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 10)) == 1 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("a")) == 2 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("b")) == 3,
               "mixed keys sort deterministically: ints first, then strings (ordinal)");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 10)), "ten-int"),
               "mixed-table lookup by boxed int resolves");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("b")), "b-str"),
               "mixed-table lookup by string resolves");
    }

    // --- 11. Growth: capacity doubles exactly at full; 100 keys stay fully
    //    sorted and findable after multiple doublings ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        for (int32_t i = 1; i <= 4; ++i)
        {
            char keyBuf[16];
            std::snprintf(keyBuf, sizeof(keyBuf), "g%02d", i);
            il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str(keyBuf), Str("v"));
        }
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_capacity(sobj) == 4,
               "four inserts fit in the initial capacity 4");
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("g05"), Str("v"));
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_capacity(sobj) == 8,
               "the 5th insert (full table) doubles capacity 4 -> 8");

        // 100 keys, ~100 distinct strings; ~100 distinct values.
        il2cpp::Il2CppSortedList* big = MakeSortedList(0);
        il2cpp::Il2CppObject* bigObj = reinterpret_cast<il2cpp::Il2CppObject*>(big);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&bigObj)); // many allocations ahead
        char keyBuf[32];
        char valBuf[32];
        for (int32_t i = 0; i < 100; ++i)
        {
            std::snprintf(keyBuf, sizeof(keyBuf), "key-%03d", i);
            std::snprintf(valBuf, sizeof(valBuf), "val-%03d", i);
            il2cpp::runtime::il2cpp_sortedlist_add(bigObj, Str(keyBuf), Str(valBuf));
        }
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_capacity(bigObj) == 128,
               "100 entries doubled the capacity to 128 (4,8,16,32,64,128)");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(bigObj) == 100, "100 entries counted");
        bool sortedOk = true;
        for (int32_t i = 0; i < 100; ++i)
        {
            std::snprintf(keyBuf, sizeof(keyBuf), "key-%03d", i);
            if (!IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(bigObj, i), keyBuf)) sortedOk = false;
        }
        Expect(sortedOk, "all 100 keys iterate in ascending order after multiple doublings");
        bool allFound = true;
        for (int32_t i = 0; i < 100; ++i)
        {
            std::snprintf(keyBuf, sizeof(keyBuf), "key-%03d", i);
            if (il2cpp::runtime::il2cpp_sortedlist_get_by_key(bigObj, Str(keyBuf)) == nullptr) allFound = false;
        }
        Expect(allFound, "every key of a 100-entry list is findable after multiple doublings");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&bigObj));
    }

    // --- 12. Clear + reuse ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        for (int32_t i = 0; i < 8; ++i)
        {
            char keyBuf[16];
            std::snprintf(keyBuf, sizeof(keyBuf), "k-%d", i);
            il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str(keyBuf), Str("v"));
        }
        const int32_t cap = il2cpp::runtime::il2cpp_sortedlist_get_capacity(sobj);
        il2cpp::runtime::il2cpp_sortedlist_clear(sobj);
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 0, "clear resets count to 0");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_capacity(sobj) == cap,
               "clear keeps the capacity");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("k-1")) == nullptr &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("k-1")) == -1,
               "cleared keys are gone from get_by_key and index_of_key");
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("after"), Str("ok"));
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 1 &&
               il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("after")) != nullptr,
               "the list is reusable after clear");
    }

    // --- 13. GC survival: keys/values reachable ONLY through the rooted
    //    list survive a full collection, still sorted ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("gc-key-2"), Str("gc-value-2"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 99), Str("gc-boxed"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("gc-key-0"), Str("gc-value-0"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("gc-key-1"), Str("gc-value-1"));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sobj));
        il2cpp::il2cpp_gc_collect();
        // NOTE: the compacting collector MOVES Gen0 survivors and rewrites
        // every reference, so pre-collect local key/value pointers are
        // STALE afterwards; every check below uses FRESH lookups and
        // move-safe CONTENT equality (or value equality for boxed ints)
        // instead of pointer comparison.
        Expect(il2cpp::il2cpp_gc_get_generation(sobj) >= il2cpp::kGcGeneration1,
               "rooted sorted list survives collection (promoted)");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 4,
               "count intact after collection");
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, il2cpp::il2cpp_box_int32(nullptr, 99)) == 0,
               "boxed-int entry survived and still sorts first (ints before strings)");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 1), "gc-key-0") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 2), "gc-key-1") &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_get_key_at(sobj, 3), "gc-key-2"),
               "string keys survived via the items-array bitmap scan, still sorted");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_value_at(sobj, 0), "gc-boxed"),
               "value survived with its key");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("gc-key-2")), "gc-value-2"),
               "lookup resolves after collection");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sobj));
    }

    // --- 14. Null key raises ArgumentNullException from every key-taking
    //    entry point ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::Il2CppObject* v = Str("v");
        bool o1 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_add(sobj, nullptr, v); }, kArgumentNullChain);
        bool o2 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, nullptr); }, kArgumentNullChain);
        bool o3 = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, nullptr); }, kArgumentNullChain);
        Expect(o1 && o2 && o3,
               "null key raises ArgumentNullException from add/get_by_key/index_of_key");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 0,
               "rejected null-key calls leave the list untouched");
    }

    // --- 15. Keys / Values snapshots: ascending (sorted) order, index-aligned
    //    with the keys; empty list -> empty arrays ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("charlie"), Str("c-val"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("alpha"), nullptr); // stored null value
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, il2cpp::il2cpp_box_int32(nullptr, 2), Str("two"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("bravo"), Str("b-val"));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sobj));

        il2cpp::Il2CppArray* keys = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::il2cpp_sortedlist_get_keys(sobj));
        il2cpp::Il2CppArray* vals = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::il2cpp_sortedlist_get_values(sobj));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keys));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&vals));

        Expect(keys->length == 4 && vals->length == 4,
               "Keys and Values snapshots each hold exactly the entry count");
        il2cpp::Il2CppObject* const* kp =
            reinterpret_cast<il2cpp::Il2CppObject* const*>(keys->data);
        il2cpp::Il2CppObject* const* vp =
            reinterpret_cast<il2cpp::Il2CppObject* const*>(vals->data);
        bool sorted = true;
        for (int32_t i = 0; i < 4; ++i)
        {
            if (il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, kp[i]) != i) sorted = false;
        }
        Expect(sorted, "Keys snapshots iterate in ascending (sorted) order");
        Expect(kp[0]->klass != nullptr &&
               std::strcmp(kp[0]->klass->name, "System.Int32") == 0,
               "the boxed-int key sorts first (ints before strings)");
        Expect(IsContent(kp[1], "alpha") && IsContent(kp[2], "bravo") &&
               IsContent(kp[3], "charlie"),
               "the string keys follow in ordinal order");
        bool aligned = true;
        for (int32_t i = 0; i < 4; ++i)
        {
            if (il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, kp[i]) != vp[i]) aligned = false;
        }
        Expect(aligned, "Values[i] is the value of Keys[i] (index-aligned)");
        Expect(vp[1] == nullptr,
               "a stored null value appears as null in the Values snapshot");

        // Stability across a compacting collection: the collector rewrites the
        // snapshot array elements, so fresh reads stay aligned.
        il2cpp::il2cpp_gc_collect();
        bool survived = true;
        for (int32_t i = 0; i < 4; ++i)
        {
            il2cpp::Il2CppObject* const* kp2 =
                reinterpret_cast<il2cpp::Il2CppObject* const*>(keys->data);
            il2cpp::Il2CppObject* const* vp2 =
                reinterpret_cast<il2cpp::Il2CppObject* const*>(vals->data);
            if (il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, kp2[i]) != i) survived = false;
            if (il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, kp2[i]) != vp2[i]) survived = false;
        }
        Expect(survived, "Keys/Values stay sorted and aligned across a collection");

        // Snapshot semantics + empty list: a later Add is not reflected; an
        // empty list yields length-0 arrays.
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("late"), Str("late-val"));
        Expect(keys->length == 4,
               "a Keys snapshot does not reflect a later Add (snapshot, not view)");
        il2cpp::Il2CppSortedList* empty = MakeSortedList(0);
        il2cpp::Il2CppObject* eobj = reinterpret_cast<il2cpp::Il2CppObject*>(empty);
        il2cpp::Il2CppArray* ek = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::il2cpp_sortedlist_get_keys(eobj));
        il2cpp::Il2CppArray* ev = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::il2cpp_sortedlist_get_values(eobj));
        Expect(ek->length == 0 && ev->length == 0,
               "an empty list yields length-0 Keys and Values snapshots");

        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&vals));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keys));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sobj));
    }

    // --- 16. Indexer SET by key (set_by_key): replace existing, insert
    //    missing at its sorted position; null key -> ArgumentNullException
    //    ---
    {
        il2cpp::Il2CppSortedList* sl = MakeSortedList(0);
        il2cpp::Il2CppObject* sobj = reinterpret_cast<il2cpp::Il2CppObject*>(sl);
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("b"), Str("b-original"));
        il2cpp::runtime::il2cpp_sortedlist_add(sobj, Str("d"), Str("d-val"));
        il2cpp::runtime::il2cpp_sortedlist_set_by_key(sobj, Str("b"), Str("b-new")); // replace
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 2,
               "set_by_key on an existing key keeps count unchanged");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("b")), "b-new"),
               "set_by_key replaces the existing key's value");
        il2cpp::runtime::il2cpp_sortedlist_set_by_key(sobj, Str("a"), Str("a-val")); // insert front
        il2cpp::runtime::il2cpp_sortedlist_set_by_key(sobj, Str("c"), Str("c-val")); // insert middle
        il2cpp::runtime::il2cpp_sortedlist_set_by_key(sobj, Str("e"), Str("e-val")); // insert tail
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 5,
               "set_by_key with absent keys inserts (count grows)");
        Expect(il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("a")) == 0 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("b")) == 1 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("c")) == 2 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("d")) == 3 &&
               il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, Str("e")) == 4,
               "set_by_key inserts keep the list sorted (a,b,c,d,e)");
        Expect(IsContent(il2cpp::runtime::il2cpp_sortedlist_get_by_key(sobj, Str("c")), "c-val"),
               "the inserted value is findable by key");

        bool ane = ThrowsDotNet([&]() { il2cpp::runtime::il2cpp_sortedlist_set_by_key(sobj, nullptr, Str("v")); },
                                kArgumentNullChain);
        Expect(ane, "set_by_key with a null key raises ArgumentNullException");
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 5,
               "a rejected null-key set_by_key leaves the list untouched");

        // set_by_key also drives growth: ~21 absent inserts from the 5-entry
        // base force several doublings (4 -> 8 -> 16 -> 32).
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sobj));
        char keyBuf[16];
        for (int32_t i = 0; i < 21; ++i)
        {
            std::snprintf(keyBuf, sizeof(keyBuf), "sk-%02d", i);
            il2cpp::runtime::il2cpp_sortedlist_set_by_key(sobj, Str(keyBuf), Str("skv"));
        }
        Expect(il2cpp::runtime::il2cpp_sortedlist_get_count(sobj) == 26,
               "set_by_key inserts across growth stay counted");
        bool keysSorted = true;
        il2cpp::Il2CppArray* bigKeys = reinterpret_cast<il2cpp::Il2CppArray*>(
            il2cpp::runtime::il2cpp_sortedlist_get_keys(sobj));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&bigKeys));
        for (int32_t i = 0; i < bigKeys->length; ++i)
        {
            il2cpp::Il2CppObject* const* kp =
                reinterpret_cast<il2cpp::Il2CppObject* const*>(bigKeys->data);
            if (il2cpp::runtime::il2cpp_sortedlist_index_of_key(sobj, kp[i]) != i) keysSorted = false;
        }
        Expect(keysSorted, "Keys stay sorted after set_by_key-driven growth");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&bigKeys));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sobj));
    }

    // 16. SNAPSHOT ENUMERATOR surface (D-SL-3): ICollection.get_Count +
    //     IEnumerable.GetEnumerator / IEnumerator.MoveNext / get_Current /
    //     Reset over the Keys / Values snapshots. The snapshot count equals
    //     the list count; the enumerator walks the snapshot in order.
    {
        il2cpp::Il2CppSortedList* sobj = MakeSortedList(4);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sobj));
        il2cpp::runtime::il2cpp_sortedlist_add(reinterpret_cast<il2cpp::Il2CppObject*>(sobj), Str("alpha"), Str("a"));
        il2cpp::runtime::il2cpp_sortedlist_add(reinterpret_cast<il2cpp::Il2CppObject*>(sobj), Str("bravo"), Str("b"));
        il2cpp::runtime::il2cpp_sortedlist_add(reinterpret_cast<il2cpp::Il2CppObject*>(sobj), Str("charlie"), Str("c"));

        // 16a. snapshot get_count == list count (the interface surface a C#
        //      `sl.Keys.Count` routes to).
        il2cpp::Il2CppObject* keys =
            il2cpp::runtime::il2cpp_sortedlist_get_keys(reinterpret_cast<il2cpp::Il2CppObject*>(sobj));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keys));
        Expect(il2cpp::runtime::il2cpp_sortedlist_snapshot_get_count(keys) == 3,
               "snapshot get_count equals the list count");

        // 16b. GetEnumerator then MoveNext/get_Current in order; Reset then
        //      MoveNext restarts from the first element.
        il2cpp::Il2CppObject* en = il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator(keys);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&en));
        Expect(il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(en) == 1 &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current(en), "alpha"),
               "enumerator starts at the first sorted key");
        Expect(il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(en) == 1 &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current(en), "bravo"),
               "enumerator advances to the second key");
        Expect(il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(en) == 1 &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current(en), "charlie"),
               "enumerator advances to the third key");
        Expect(il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(en) == 0,
               "enumerator exhausts after the last element");
        il2cpp::runtime::il2cpp_sortedlist_enumerator_reset(en);
        Expect(il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(en) == 1 &&
               IsContent(il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current(en), "alpha"),
               "reset restarts the enumerator at the first key");

        // 16c. foreach loop equivalence: MoveNext/get_Current across a Values
        //      snapshot yields every value in index order.
        il2cpp::Il2CppObject* vals =
            il2cpp::runtime::il2cpp_sortedlist_get_values(reinterpret_cast<il2cpp::Il2CppObject*>(sobj));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&vals));
        Expect(il2cpp::runtime::il2cpp_sortedlist_snapshot_get_count(vals) == 3,
               "Values snapshot count equals the list count");
        il2cpp::Il2CppObject* ve = il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator(vals);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&ve));
        int32_t seen = 0;
        const char* expected[] = { "a", "b", "c" };
        bool inOrder = true;
        while (il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(ve))
        {
            if (seen >= 3) { inOrder = false; break; }
            if (!IsContent(il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current(ve), expected[seen])) inOrder = false;
            ++seen;
        }
        Expect(inOrder && seen == 3, "Values enumeration yields every value in order");
        Expect(il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(ve) == 0,
               "Values enumerator exhausts after the last value");
        il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose(en);   // no-op, must not crash
        il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose(nullptr); // null-safe no-op

        // 16d. VERSION-CHECK DIVERGENCE (recorded, D-STK-1 family; see
        //      sortedlist.h): net48's SortedList enumerator is a LIVE view and
        //      throws InvalidOperationException when the list is mutated after
        //      GetEnumerator; THIS enumerator wraps a FROZEN snapshot (no list
        //      reference -- GetEnumerator is routed on the snapshot array), so
        //      a list mutation cannot be detected and the walk completes over
        //      the frozen copy. Pinned here so the recorded snapshot semantics
        //      cannot silently regress.
        il2cpp::runtime::il2cpp_sortedlist_add(reinterpret_cast<il2cpp::Il2CppObject*>(sobj), Str("delta"), Str("d"));
        // 'en' was exhausted BEFORE the mutation (3 snapshot keys). After the
        // list grew to 4 pairs, Reset + re-walk must yield EXACTLY the 3
        // frozen keys -- no throw, no leaked 4th entry.
        il2cpp::runtime::il2cpp_sortedlist_enumerator_reset(en);
        bool snapOk = true;
        int32_t seenSnap = 0;
        const char* snapKeys[] = { "alpha", "bravo", "charlie" };
        while (il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(en))
        {
            if (seenSnap >= 3 || !IsContent(il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current(en), snapKeys[seenSnap])) snapOk = false;
            ++seenSnap;
        }
        Expect(snapOk && seenSnap == 3,
               "snapshot enumerator walks the frozen 3 keys after a list mutation (recorded divergence)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&ve));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&vals));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&en));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&keys));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sobj));
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d sortedlist assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime sortedlist tests passed\n");
    return 0;
}

