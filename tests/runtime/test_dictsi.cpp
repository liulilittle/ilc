//------------------------------------------------------------------------------
// test_dictsi.cpp -- string->int32 dictionary runtime unit tests (dictsi.h).
//
// Coverage (normal + edge + error paths, per the domain brief):
//   1. create + destroy: count starts at 0; a negative capacity raises
//      ArgumentOutOfRangeException.
//   2. add + try_get_value: 3 pairs insert and read back; a miss returns
//      false and leaves the out-param untouched.
//   3. Duplicate add raises ArgumentException and leaves the dictionary
//      unchanged -- including a SECOND DISTINCT string instance with equal
//      content (ordinal equality, mirroring Dictionary<string,int>).
//   4. contains_key: true on a hit, false on a miss.
//   5. remove: true on a hit (count decrements, key gone); false when absent
//      (dictionary unchanged).
//   6. clear: count -> 0, every key gone, reusable afterwards.
//   7. Growth: with the 0.72 load factor the bucket array doubles exactly at
//      the 3rd insert (4 -> 8) and the 6th (8 -> 16); a 100-key dictionary
//      reads every pair back after multiple doublings.
//   8. Hash collision: '000' and '20r' hash identically (0x063AF2CE --
//      the net48 string.GetHashCode oracle, probe-verified against
//      csc.exe net48 on 2026-08-08; the runtime value is pinned below);
//      both coexist in the same chain, each resolves to its own value, and
//      removing one leaves the other findable.
//   9. get_keys / get_values: length == count; keys[i] <-> values[i] are
//      positionally consistent (insertion order, net48 R1); empty dictionary
//      -> length-0 arrays.
//  10. A null key raises ArgumentNullException from add/try_get_value/
//      remove/contains_key.
//  11. Boundary capacities: capacity 1 -> 4 buckets; capacity 5 -> 8.
//  12. Enumeration order determinism: get_keys is a pure function of the
//      table state -- two consecutive calls return identical arrays, and the
//      order follows the net48 Dictionary<string,int> contract (spec §8
//      R1-R7): INSERTION order -- entries[0..count) ascending, skipping free
//      slots; overwrites keep their position; remove-then-readd returns to
//      the original slot. Expected order is the literal ADD SEQUENCE (no
//      removals/resizes in this test), so it verifies the CONTRACT against
//      the net48 oracle rather than a stale hash-bucket sequence.
//
// Plain executable + minimal assertion harness (see test_listi4.cpp).
//------------------------------------------------------------------------------

#include "dictsi.h"
#include "dictsi_obj.h" // rtdict-b23 (DICT-GC): the Dictionary<string,int> wrapper icalls

#include "il2cpp_exception.h"
#include "il2cpp_string.h"
#include "il2cpp_value_semantics.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "dictsi test fixture assumes 8-byte pointers");

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
const char* kKeyNotFoundChain =
    "System.Collections.Generic.KeyNotFoundException,System.Collections.Generic.KeyNotFoundException,System.SystemException,System.Exception";

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

il2cpp::Il2CppString* S(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

// The wrapper icalls take keys as Il2CppObject* (the generated code's uniform
// managed-reference representation); O() is S() widened to that slot type.
il2cpp::Il2CppObject* O(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

} // namespace

int main()
{
    // --- 1. create + destroy: count 0; negative capacity raises ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 0, "fresh dictionary: count 0");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_dict_si_create(-1); }, kArgumentOutOfRangeChain),
               "create(-1) raises ArgumentOutOfRangeException");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 2. add + try_get_value: hits and a miss that leaves the out-param
    //    untouched ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        il2cpp::il2cpp_dict_si_add(d, S("one"), 1);
        il2cpp::il2cpp_dict_si_add(d, S("two"), 2);
        il2cpp::il2cpp_dict_si_add(d, S("three"), 3);
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 3, "three adds -> count 3");
        int32_t v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("one"), &v) && v == 1, "try_get_value(\"one\") == 1");
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("two"), &v) && v == 2, "try_get_value(\"two\") == 2");
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("three"), &v) && v == 3, "try_get_value(\"three\") == 3");
        v = 777;
        Expect(!il2cpp::il2cpp_dict_si_try_get_value(d, S("missing"), &v), "try_get_value(miss) returns false");
        Expect(v == 777, "miss leaves the out-param untouched");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 3. duplicate add: ArgumentException, dictionary unchanged; equal
    //    CONTENT (distinct instances) is the same key ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        il2cpp::il2cpp_dict_si_add(d, S("k"), 10);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_dict_si_add(d, S("k"), 20); }, kArgumentChain),
               "duplicate add raises ArgumentException");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 1, "failed duplicate add leaves count unchanged");
        int32_t v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("k"), &v) && v == 10, "failed duplicate add keeps the original value");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 4. contains_key ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        il2cpp::il2cpp_dict_si_add(d, S("present"), 5);
        Expect(il2cpp::il2cpp_dict_si_contains_key(d, S("present")), "contains_key on a hit is true");
        Expect(!il2cpp::il2cpp_dict_si_contains_key(d, S("absent")), "contains_key on a miss is false");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 5. remove: hit unlinks (count decrements, key gone); a miss is a
    //    silent false ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        il2cpp::il2cpp_dict_si_add(d, S("a"), 1);
        il2cpp::il2cpp_dict_si_add(d, S("b"), 2);
        Expect(il2cpp::il2cpp_dict_si_remove(d, S("a")), "remove on a hit returns true");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 1, "remove hit -> count 1");
        Expect(!il2cpp::il2cpp_dict_si_contains_key(d, S("a")), "removed key is gone");
        int32_t v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("b"), &v) && v == 2, "unremoved key still resolves");
        Expect(!il2cpp::il2cpp_dict_si_remove(d, S("a")), "remove on an absent key returns false");
        Expect(!il2cpp::il2cpp_dict_si_remove(d, S("never")), "remove on a never-added key returns false");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 1, "failed removes leave count unchanged");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 6. clear: count -> 0, keys gone, reusable afterwards ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        il2cpp::il2cpp_dict_si_add(d, S("x"), 1);
        il2cpp::il2cpp_dict_si_add(d, S("y"), 2);
        il2cpp::il2cpp_dict_si_clear(d);
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 0, "clear -> count 0");
        Expect(!il2cpp::il2cpp_dict_si_contains_key(d, S("x")), "cleared keys are gone");
        il2cpp::il2cpp_dict_si_add(d, S("z"), 9);
        int32_t v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("z"), &v) && v == 9, "dictionary is reusable after clear");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 7. Growth: with the 0.72 load factor the bucket array doubles
    //    exactly at the 3rd insert (4 -> 8) and the 6th (8 -> 16); a
    //    100-key dictionary reads every pair back ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        Expect(d->bucketCount == 4, "fresh dictionary starts with 4 buckets");
        il2cpp::il2cpp_dict_si_add(d, S("k0"), 0);
        il2cpp::il2cpp_dict_si_add(d, S("k1"), 1);
        Expect(d->bucketCount == 4, "2 adds fit in 4 buckets (2 < 4 * 0.72)");
        il2cpp::il2cpp_dict_si_add(d, S("k2"), 2);
        Expect(d->bucketCount == 8, "3rd add exceeds 0.72 * 4 -> doubles to 8");
        il2cpp::il2cpp_dict_si_add(d, S("k3"), 3);
        il2cpp::il2cpp_dict_si_add(d, S("k4"), 4);
        Expect(d->bucketCount == 8, "5 elements still fit in 8 buckets (5 < 8 * 0.72)");
        il2cpp::il2cpp_dict_si_add(d, S("k5"), 5);
        Expect(d->bucketCount == 16, "6th add exceeds 0.72 * 8 -> doubles to 16");

        il2cpp::Il2CppDictSI* big = il2cpp::il2cpp_dict_si_create(0);
        for (int32_t i = 0; i < 100; ++i)
        {
            char buf[16];
            std::snprintf(buf, sizeof(buf), "key-%d", i);
            il2cpp::il2cpp_dict_si_add(big, S(buf), i * 7);
        }
        Expect(il2cpp::il2cpp_dict_si_get_count(big) == 100, "100 adds -> count 100 (multiple doublings)");
        bool allFound = true;
        for (int32_t i = 0; i < 100; ++i)
        {
            char buf[16];
            std::snprintf(buf, sizeof(buf), "key-%d", i);
            int32_t v = -1;
            if (!il2cpp::il2cpp_dict_si_try_get_value(big, S(buf), &v) || v != i * 7) allFound = false;
        }
        Expect(allFound, "all 100 pairs read back after multiple doublings");
        il2cpp::il2cpp_dict_si_destroy(d);
        il2cpp::il2cpp_dict_si_destroy(big);
    }

    // --- 8. Hash collision: '000' and '20r' hash identically (verified
    //    below); both live in one chain without shadowing each other ---
    {
        il2cpp::Il2CppString* k1 = S("000");
        il2cpp::Il2CppString* k2 = S("20r");
        const int32_t h1 = il2cpp::runtime::icall_string_get_hash_code(reinterpret_cast<il2cpp::Il2CppObject*>(k1));
        const int32_t h2 = il2cpp::runtime::icall_string_get_hash_code(reinterpret_cast<il2cpp::Il2CppObject*>(k2));
        Expect(h1 == h2, "collision precondition: '000' and '20r' hash identically");
        Expect(h1 == static_cast<int32_t>(0x063AF2CE),
               "collision hashes match the net48 oracle (0x063AF2CE)");

        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        il2cpp::il2cpp_dict_si_add(d, k1, 111);
        il2cpp::il2cpp_dict_si_add(d, k2, 222);
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 2, "two colliding keys coexist (count 2)");
        int32_t v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("000"), &v) && v == 111, "collision key 1 resolves to its own value");
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("20r"), &v) && v == 222, "collision key 2 resolves to its own value");
        Expect(il2cpp::il2cpp_dict_si_contains_key(d, S("000")), "collision key 1 is contained");
        Expect(il2cpp::il2cpp_dict_si_contains_key(d, S("20r")), "collision key 2 is contained");

        Expect(il2cpp::il2cpp_dict_si_remove(d, k1), "removing collision key 1 returns true");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 1, "removing collision key 1 -> count 1");
        Expect(!il2cpp::il2cpp_dict_si_contains_key(d, S("000")), "collision key 1 is gone");
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("20r"), &v) && v == 222, "collision key 2 survives the removal");
        Expect(il2cpp::il2cpp_dict_si_remove(d, k2), "removing collision key 2 returns true");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 0, "both collision keys removed -> count 0");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 9. get_keys / get_values: length == count, positionally
    //    consistent, empty dictionary -> length-0 arrays ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        il2cpp::il2cpp_dict_si_add(d, S("alpha"), 10);
        il2cpp::il2cpp_dict_si_add(d, S("beta"), 20);
        il2cpp::il2cpp_dict_si_add(d, S("gamma"), 30);
        il2cpp::Il2CppArray* keys = il2cpp::il2cpp_dict_si_get_keys(d);
        il2cpp::Il2CppArray* values = il2cpp::il2cpp_dict_si_get_values(d);
        Expect(keys->length == 3 && values->length == 3, "get_keys/get_values lengths == count");
        Expect(keys->elemSize == static_cast<int32_t>(sizeof(void*)) && keys->isRefElem,
               "get_keys returns a reference array");
        Expect(values->elemSize == 4 && !values->isRefElem, "get_values returns an int32 value array");
        const il2cpp::Il2CppObject** keyElems =
            reinterpret_cast<const il2cpp::Il2CppObject**>(keys->data);
        const int32_t* valueElems = reinterpret_cast<const int32_t*>(values->data);
        auto KeyIs = [](const il2cpp::Il2CppString* s, const char16_t* text, int32_t len)
        {
            return s->length == len &&
                   std::memcmp(s->chars, text, static_cast<size_t>(len) * sizeof(char16_t)) == 0;
        };
        bool paired = true;
        for (int32_t i = 0; i < 3; ++i)
        {
            const il2cpp::Il2CppString* ks =
                reinterpret_cast<const il2cpp::Il2CppString*>(keyElems[i]);
            const int32_t expected =
                KeyIs(ks, u"alpha", 5) ? 10 : KeyIs(ks, u"beta", 4) ? 20 : KeyIs(ks, u"gamma", 5) ? 30 : -9999;
            if (valueElems[i] != expected)
            {
                paired = false;
            }
        }
        Expect(paired, "keys[i] <-> values[i] are positionally consistent");
        il2cpp::il2cpp_dict_si_destroy(d);

        il2cpp::Il2CppDictSI* empty = il2cpp::il2cpp_dict_si_create(0);
        Expect(il2cpp::il2cpp_dict_si_get_keys(empty)->length == 0, "empty dictionary -> length-0 keys array");
        Expect(il2cpp::il2cpp_dict_si_get_values(empty)->length == 0, "empty dictionary -> length-0 values array");
        il2cpp::il2cpp_dict_si_destroy(empty);
    }

    // --- 10. null key raises ArgumentNullException from every key-taking
    //    entry point ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_dict_si_add(d, nullptr, 1); }, kArgumentNullChain),
               "add with a null key raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_dict_si_try_get_value(d, nullptr, nullptr); }, kArgumentNullChain),
               "try_get_value with a null key raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_dict_si_remove(d, nullptr); }, kArgumentNullChain),
               "remove with a null key raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_dict_si_contains_key(d, nullptr); }, kArgumentNullChain),
               "contains_key with a null key raises ArgumentNullException");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 0, "null-key failures leave the dictionary empty");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 11. boundary capacities round up to the next power of two ---
    {
        il2cpp::Il2CppDictSI* tiny = il2cpp::il2cpp_dict_si_create(1);
        Expect(tiny->bucketCount == 4, "capacity 1 -> 4 buckets (minimum)");
        il2cpp::il2cpp_dict_si_destroy(tiny);
        il2cpp::Il2CppDictSI* five = il2cpp::il2cpp_dict_si_create(5);
        Expect(five->bucketCount == 8, "capacity 5 -> 8 buckets");
        il2cpp::il2cpp_dict_si_destroy(five);
        il2cpp::Il2CppDictSI* exact = il2cpp::il2cpp_dict_si_create(8);
        Expect(exact->bucketCount == 8, "capacity 8 -> 8 buckets (exact power of two)");
        il2cpp::il2cpp_dict_si_destroy(exact);
    }

    // --- 12. Enumeration-order determinism: get_keys is a pure function of
    //    the table state. Two consecutive calls return identical arrays, and
    //    the order matches the documented hash-bucket contract (buckets
    //    ascending 0..bucketCount-1; within a bucket, chain head-first --
    //    most recently added first). Expected order is computed HERE from the
    //    net48 hash oracle + the power-of-two bucket mask, so the test
    //    verifies the CONTRACT rather than a stale hardcoded sequence. ---
    {
        const char* const texts[] = { "one", "two", "three", "alpha", "beta", "gamma" };
        const int32_t vals[] = { 1, 2, 3, 10, 20, 30 };
        const int32_t n = 6;

        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(32); // 32 buckets: no resize at 6 adds
        Expect(d->bucketCount == 32, "capacity 32 -> 32 buckets (no resize during the test)");
        for (int32_t i = 0; i < n; ++i)
        {
            il2cpp::il2cpp_dict_si_add(d, S(texts[i]), vals[i]);
        }

        // Determinism: two calls, identical arrays (same length, same order).
        il2cpp::Il2CppArray* keys1 = il2cpp::il2cpp_dict_si_get_keys(d);
        il2cpp::Il2CppArray* keys2 = il2cpp::il2cpp_dict_si_get_keys(d);
        il2cpp::Il2CppArray* values1 = il2cpp::il2cpp_dict_si_get_values(d);
        const il2cpp::Il2CppObject** k1 =
            reinterpret_cast<const il2cpp::Il2CppObject**>(keys1->data);
        const il2cpp::Il2CppObject** k2 =
            reinterpret_cast<const il2cpp::Il2CppObject**>(keys2->data);
        bool deterministic = keys1->length == n && keys2->length == n && values1->length == n;
        for (int32_t i = 0; deterministic && i < n; ++i)
        {
            if (k1[i] != k2[i]) deterministic = false;
        }
        Expect(deterministic, "two consecutive get_keys calls return identical arrays");

        auto KeyOf = [&](int32_t i) { return reinterpret_cast<const il2cpp::Il2CppString*>(k1[i]); };
        auto TextIs = [&](const il2cpp::Il2CppString* s, int32_t i)
        {
            const int32_t len = static_cast<int32_t>(std::strlen(texts[i]));
            if (s->length != len) return false;
            for (int32_t u = 0; u < len; ++u)
            {
                if (s->chars[u] != static_cast<char16_t>(static_cast<unsigned char>(texts[i][u])))
                {
                    return false;
                }
            }
            return true;
        };

        // Contract order (net48 Dictionary<string,int>, spec §8 R1): the
        // INSERTION order -- entries[0..count) ascending, skipping free slots.
        // No removal / resize in this test, so the expected sequence is the
        // literal add sequence texts[0..n), and values[] must line up
        // positionally (keys[i] <-> values[i]).
        const int32_t* v1 = reinterpret_cast<const int32_t*>(values1->data);
        bool orderOk = keys1->length == n;
        for (int32_t i = 0; orderOk && i < n; ++i)
        {
            if (!TextIs(KeyOf(i), i)) orderOk = false;
            if (v1[i] != vals[i]) orderOk = false;
        }
        Expect(orderOk, "get_keys/get_values order follows the net48 insertion-order contract (R1)");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 13. GC-OBJECT WRAPPER icalls (dictsi_obj.h, rtdict-b23 DICT-GC) ---
    // The managed Dictionary<string,int> object holds the raw Il2CppDictSI*
    // behind an object header; the icall_dict_si_* wrappers operate on it.
    {
        // ctor: the wrapper descriptor allocates + the ctor creates the table.
        il2cpp::Il2CppObject* w = il2cpp::il2cpp_object_new(
            il2cpp::il2cpp_dict_si_object_class());
        Expect(w != nullptr, "wrapper object allocates through the dict descriptor");
        il2cpp::runtime::icall_dict_si_ctor(w, 0);
        Expect(il2cpp::runtime::icall_dict_si_get_count(w) == 0, "wrapper ctor -> count 0");

        // Add / TryGetValue / ContainsKey / indexer GET.
        il2cpp::runtime::icall_dict_si_add(w, O("one"), 1);
        il2cpp::runtime::icall_dict_si_add(w, O("two"), 2);
        il2cpp::runtime::icall_dict_si_add(w, O("three"), 3);
        Expect(il2cpp::runtime::icall_dict_si_get_count(w) == 3, "wrapper: 3 adds -> count 3");
        int32_t v = 0;
        Expect(il2cpp::runtime::icall_dict_si_try_get_value(w, O("two"), &v) && v == 2,
               "wrapper TryGetValue(\"two\") == 2");
        Expect(!il2cpp::runtime::icall_dict_si_try_get_value(w, O("nope"), &v),
               "wrapper TryGetValue(miss) returns false");
        Expect(il2cpp::runtime::icall_dict_si_contains_key(w, O("one")),
               "wrapper ContainsKey(\"one\") true");
        Expect(!il2cpp::runtime::icall_dict_si_contains_key(w, O("ONE")),
               "wrapper ContainsKey(\"ONE\") false (ordinal keys)");
        Expect(il2cpp::runtime::icall_dict_si_get(w, O("two")) == 2,
               "wrapper indexer GET \"two\" == 2");

        // indexer SET: replace existing + insert new (never throws).
        il2cpp::runtime::icall_dict_si_set(w, O("two"), 22);
        Expect(il2cpp::runtime::icall_dict_si_get_count(w) == 3,
               "wrapper set_Item(existing) keeps count");
        Expect(il2cpp::runtime::icall_dict_si_get(w, O("two")) == 22,
               "wrapper set_Item(existing) replaces the value");
        il2cpp::runtime::icall_dict_si_set(w, O("four"), 4);
        Expect(il2cpp::runtime::icall_dict_si_get_count(w) == 4,
               "wrapper set_Item(new) inserts");
        Expect(il2cpp::runtime::icall_dict_si_get(w, O("four")) == 4,
               "wrapper indexer GET after insert == 4");

        // indexer GET miss -> KeyNotFoundException.
        Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_dict_si_get(w, O("ghost")); },
                            kKeyNotFoundChain),
               "wrapper indexer GET miss raises KeyNotFoundException");

        // null key -> ArgumentNullException.
        Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_dict_si_add(w, nullptr, 5); },
                            kArgumentNullChain),
               "wrapper Add(null, v) raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_dict_si_get(w, nullptr); },
                            kArgumentNullChain),
               "wrapper indexer GET(null) raises ArgumentNullException");

        // Remove / Clear.
        Expect(il2cpp::runtime::icall_dict_si_remove(w, O("three")),
               "wrapper Remove(\"three\") true");
        Expect(!il2cpp::runtime::icall_dict_si_remove(w, O("ghost")),
               "wrapper Remove(miss) false");
        Expect(il2cpp::runtime::icall_dict_si_get_count(w) == 3,
               "wrapper remove -> count 3");
        il2cpp::runtime::icall_dict_si_clear(w);
        Expect(il2cpp::runtime::icall_dict_si_get_count(w) == 0,
               "wrapper Clear -> count 0");
        il2cpp::runtime::icall_dict_si_add(w, O("again"), 9);
        Expect(il2cpp::runtime::icall_dict_si_get(w, O("again")) == 9,
               "wrapper reusable after Clear");

        // Keys/Values snapshots: length == count, positionally consistent.
        il2cpp::runtime::icall_dict_si_add(w, O("x"), 10);
        il2cpp::runtime::icall_dict_si_add(w, O("y"), 20);
        il2cpp::Il2CppArray* kw = il2cpp::runtime::icall_dict_si_get_keys(w);
        il2cpp::Il2CppArray* vw = il2cpp::runtime::icall_dict_si_get_values(w);
        Expect(kw->length == 3 && vw->length == 3, "wrapper Keys/Values lengths match count");
        Expect(il2cpp::runtime::icall_dict_si_get_count(w) == 3, "wrapper count after snapshots");
    }

    // --- 14. add_unique: insert-only Add that reports whether it inserted.
    //    The HashSet<T>.Add contract in dictsi terms: true on insert (value
    //    stored), false on a duplicate (NO write -- the original value and the
    //    slot position survive, R2), ArgumentNullException on a null key. ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        Expect(il2cpp::il2cpp_dict_si_add_unique(d, S("u"), 1) == true,
               "add_unique(new key) returns true");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 1, "add_unique(new) count 1");
        int32_t v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("u"), &v) && v == 1,
               "add_unique(new) stored the value");
        // Duplicate: false, and the stored value must NOT be overwritten.
        Expect(il2cpp::il2cpp_dict_si_add_unique(d, S("u"), 99) == false,
               "add_unique(duplicate) returns false");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 1, "add_unique(duplicate) leaves count at 1");
        v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("u"), &v) && v == 1,
               "add_unique(duplicate) does NOT overwrite the stored value (no set semantics)");
        // Content equality: a distinct instance with equal content is a duplicate.
        Expect(il2cpp::il2cpp_dict_si_add_unique(d, S("u"), 7) == false,
               "add_unique(content-equal distinct instance) returns false");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 1, "content-equal duplicate: count still 1");
        // Insertion order / position: a second key appends after the first.
        Expect(il2cpp::il2cpp_dict_si_add_unique(d, S("v"), 2) == true,
               "add_unique(second key) returns true");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 2, "add_unique(second key) count 2");
        // Null key: ArgumentNullException, table unchanged.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_dict_si_add_unique(d, nullptr, 3); },
                            kArgumentNullChain),
               "add_unique(null) raises ArgumentNullException");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 2, "add_unique(null) leaves count unchanged");
        // Remove then add_unique: the freed slot is reused and true is returned.
        Expect(il2cpp::il2cpp_dict_si_remove(d, S("u")) == true, "add_unique fixture: remove true");
        Expect(il2cpp::il2cpp_dict_si_add_unique(d, S("u"), 4) == true,
               "add_unique after remove returns true (free-list reuse)");
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 2, "add_unique after remove: count 2");
        v = 0;
        Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S("u"), &v) && v == 4,
               "add_unique after remove stored the NEW value (slot was free, not occupied)");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- 15. add_unique growth: the 0.72 load-factor resize runs on the
    //    insert path only, and every member reads back afterwards ---
    {
        il2cpp::Il2CppDictSI* d = il2cpp::il2cpp_dict_si_create(0);
        char buf[24];
        for (int32_t i = 0; i < 200; ++i)
        {
            std::snprintf(buf, sizeof(buf), "g%03d", i);
            Expect(il2cpp::il2cpp_dict_si_add_unique(d, S(buf), i) == true,
                   "add_unique growth: every new key returns true");
        }
        Expect(il2cpp::il2cpp_dict_si_get_count(d) == 200, "add_unique growth: count 200");
        for (int32_t i = 0; i < 200; i += 13)
        {
            std::snprintf(buf, sizeof(buf), "g%03d", i);
            int32_t v = 0;
            Expect(il2cpp::il2cpp_dict_si_try_get_value(d, S(buf), &v) && v == i,
                   "add_unique growth: value reads back after bucket doublings");
        }
        Expect(il2cpp::il2cpp_dict_si_add_unique(d, S("g000"), 999) == false,
               "add_unique growth: existing key still false after resizes");
        il2cpp::il2cpp_dict_si_destroy(d);
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d dictsi assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime dictsi tests passed\n");
    return 0;
}