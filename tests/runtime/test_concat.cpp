//------------------------------------------------------------------------------
// test_concat.cpp -- single-allocation string concatenation runtime tests.
//
// Coverage (normal + edge + error paths):
//   1.  il2cpp_string_concat_many: 2/3/4 segments, empty segments, null
//       segments (count as empty), zero/negative counts, single-segment
//       identity fast path, empty result.
//   2.  il2cpp_string_concat_array: multi-element array, null elements,
//       null/empty array, single-element identity, large concatenation
//       (200 elements x 100 units = 20000 units).
//   3.  il2cpp_string_builder_join: separator between elements only,
//       null elements, single-element identity, empty array, empty-string
//       elements.
//   4.  il2cpp_string_repeat: zero/negative count -> empty, count 1 ->
//       identity, repeated content, large repetition.
//   5.  Allocation-count assertions: the runtime exposes no GC allocation
//       counter, so the managed-side "exactly one allocation" claim is
//       enforced structurally (the implementation's ONLY managed allocation
//       is the final il2cpp_string_new -- verifiable in concat.cpp). The
//       NON-GC side IS observable: the pool statistics must show exactly one
//       scratch allocation and one scratch free per call (and zero on the
//       identity fast paths), with the exact byte count.
//   6.  GC integration: rooted segments + result survive collection.
//   7.  Error path: a total length beyond Int32.MaxValue raises
//       OverflowException (forged length field -- no 4GB allocation needed).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_concat.h"
#include "il2cpp_allocator.h"
#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string_intern.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

using namespace il2cpp; // unqualified Il2Cpp* types (test_io.cpp convention)

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Build a managed string from an ASCII literal (the tests' creation helper).
il2cpp::Il2CppString* Cs(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

// ASCII literal length (the tests' input is always ASCII).
int32_t AsciiLen(const char* ascii)
{
    return static_cast<int32_t>(std::strlen(ascii));
}

// Content comparison: string vs ASCII literal (unit-accurate).
bool StrEquals(const il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr) return false;
    const int32_t n = AsciiLen(ascii);
    if (s->length != n) return false;
    for (int32_t i = 0; i < n; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
            return false;
    }
    return true;
}

// Build a string[] reference array from ASCII literals (nullptr element ->
// null array slot, exercising the null-element contract).
il2cpp::Il2CppArray* BuildStringArray(const char* const* elems, int32_t count)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(
        il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, count);
    for (int32_t i = 0; i < count; ++i)
    {
        il2cpp::Il2CppObject* elem = (elems[i] != nullptr)
            ? reinterpret_cast<Il2CppObject*>(Cs(elems[i]))
            : nullptr;
        *reinterpret_cast<il2cpp::Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)) = elem;
    }
    return arr;
}

} // namespace

int main()
{
    // --- 1. concat_many: 2 segments ----------------------------------------
    il2cpp::Il2CppString* s1 = Cs("abc");
    il2cpp::Il2CppString* s2 = Cs("def");
    // Root the long-lived segment strings: s1/s2 are reused across sections
    // that trigger collections (the intern-pool allocation for empty results,
    // the large 20000-unit concat), and the collector compacts Gen0 -- an
    // unrooted C++ local would go stale (the same contract generated-code
    // frames satisfy; see the concat header's GC-ROOTING CONTRACT).
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s1));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s2));
    il2cpp::Il2CppString* two = il2cpp::runtime::il2cpp_string_concat_many(2, s1, s2);
    Expect(two != nullptr && two->length == 6 && StrEquals(two, "abcdef"),
           "concat_many(2) yields \"abcdef\"");

    // --- 2. concat_many: 3 segments with an empty middle --------------------
    il2cpp::Il2CppString* empty = Cs("");
    il2cpp::Il2CppString* three = il2cpp::runtime::il2cpp_string_concat_many(3, Cs("a"), empty, Cs("b"));
    Expect(three != nullptr && StrEquals(three, "ab"),
           "concat_many(3) with an empty middle segment yields \"ab\"");

    // --- 3. concat_many: null segment counts as empty ------------------------
    il2cpp::Il2CppString* four = il2cpp::runtime::il2cpp_string_concat_many(4, Cs("a"), nullptr, Cs("b"), Cs("c"));
    Expect(four != nullptr && StrEquals(four, "abc"),
           "concat_many(4) with a null segment yields \"abc\" (null = empty)");

    // --- 4. concat_many: all-empty result ------------------------------------
    il2cpp::Il2CppString* allEmpty = il2cpp::runtime::il2cpp_string_concat_many(2, empty, nullptr);
    Expect(allEmpty != nullptr && allEmpty->length == 0,
           "concat_many of empty/null segments yields a non-null empty string");

    // --- 5/6. concat_many: zero / negative count -> empty --------------------
    il2cpp::Il2CppString* zeroCount = il2cpp::runtime::il2cpp_string_concat_many(0);
    Expect(zeroCount != nullptr && zeroCount->length == 0,
           "concat_many(0) yields an empty string");
    il2cpp::Il2CppString* negCount = il2cpp::runtime::il2cpp_string_concat_many(-3);
    Expect(negCount != nullptr && negCount->length == 0,
           "concat_many(negative) yields an empty string (defensive)");

    // --- 7. concat_many: single non-null segment -> identity (0 allocs) ------
    il2cpp::Il2CppString* single = il2cpp::runtime::il2cpp_string_concat_many(1, s1);
    Expect(single == s1, "concat_many(1) returns the input string unchanged (identity)");

    // --- 8. concat_array: multi-element --------------------------------------
    {
        const char* const elems[] = { "x", "y", "z" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_concat_array(arr);
        Expect(joined != nullptr && StrEquals(joined, "xyz"),
               "concat_array(3 elements) yields \"xyz\"");
    }

    // --- 9. concat_array: null elements -> empty ------------------------------
    {
        const char* const elems[] = { "a", nullptr, "c" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_concat_array(arr);
        Expect(joined != nullptr && StrEquals(joined, "ac"),
               "concat_array with a null element yields \"ac\" (null = empty)");
    }

    // --- 10. concat_array: null / empty array -> empty ------------------------
    {
        il2cpp::Il2CppString* nullArr = il2cpp::runtime::il2cpp_string_concat_array(nullptr);
        Expect(nullArr != nullptr && nullArr->length == 0,
               "concat_array(null) yields an empty string (defensive)");
        // MSVC /W4 C2466: zero-length arrays are an ERROR there (clang/GCC
        // accept them as a GNU extension) -- pass a null pointer with count 0
        // instead; BuildStringArray never dereferences when count is 0.
        il2cpp::Il2CppObject* emptyArr = reinterpret_cast<Il2CppObject*>(BuildStringArray(nullptr, 0));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_concat_array(emptyArr);
        Expect(joined != nullptr && joined->length == 0,
               "concat_array(empty array) yields an empty string");
    }

    // --- 11. concat_array: single element -> identity -------------------------
    {
        const char* const one[] = { "only" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(one, 1));
        il2cpp::Il2CppObject* elem = *reinterpret_cast<il2cpp::Il2CppObject**>(
            il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_concat_array(arr);
        Expect(joined == reinterpret_cast<Il2CppString*>(elem),
               "concat_array(1 element) returns the element unchanged (identity)");
    }

    // --- 12. concat_array: large concatenation (200 x 100 units) --------------
    {
        il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(
            il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 200);
        char buf[101];
        std::memset(buf, 'a', 100);
        buf[100] = '\0';
        for (int32_t i = 0; i < 200; ++i)
        {
            *reinterpret_cast<il2cpp::Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)) =
                reinterpret_cast<Il2CppObject*>(Cs(buf));
        }
        il2cpp::Il2CppString* big = il2cpp::runtime::il2cpp_string_concat_array(
            reinterpret_cast<Il2CppObject*>(arr));
        Expect(big != nullptr && big->length == 200 * 100,
               "concat_array(200 x 100 units) has the full 20000-unit length");
        Expect(big != nullptr && big->chars[0] == u'a' && big->chars[9999] == u'a'
               && big->chars[10000] == u'a' && big->chars[19999] == u'a',
               "large concat payload spot-checks (first/last unit of each block)");
    }

    // --- 13. builder_join: separator between elements only --------------------
    {
        const char* const elems[] = { "a", "b", "c" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_builder_join(u',', arr);
        Expect(joined != nullptr && StrEquals(joined, "a,b,c"),
               "builder_join(3 elements) yields \"a,b,c\"");
        Expect(joined != nullptr && joined->length == 5,
               "builder_join length = 3 units + 2 separators (no leading/trailing)");
    }

    // --- 14. builder_join: null elements -> empty ------------------------------
    {
        const char* const elems[] = { "a", nullptr, "c" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_builder_join(u',', arr);
        Expect(joined != nullptr && StrEquals(joined, "a,,c"),
               "builder_join with a null element yields \"a,,c\" (null = empty)");
    }

    // --- 15. builder_join: single element -> identity --------------------------
    {
        const char* const one[] = { "only" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(one, 1));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_builder_join(u'-', arr);
        Expect(StrEquals(joined, "only"),
               "builder_join(1 element) yields the element with no separator");
        il2cpp::Il2CppObject* elem = *reinterpret_cast<il2cpp::Il2CppObject**>(
            il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0));
        Expect(joined == reinterpret_cast<Il2CppString*>(elem),
               "builder_join(1 element) returns the element unchanged (identity)");
    }

    // --- 16. builder_join: empty array / empty-string elements -----------------
    {
        // MSVC /W4 C2466: zero-length arrays are an ERROR there (clang/GCC
        // accept them as a GNU extension) -- pass a null pointer with count 0.
        il2cpp::Il2CppObject* emptyArr = reinterpret_cast<Il2CppObject*>(BuildStringArray(nullptr, 0));
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_builder_join(u',', emptyArr);
        Expect(joined != nullptr && joined->length == 0,
               "builder_join(empty array) yields an empty string");
        // MSVC /W4 C4456: 'two' would shadow the outer 'two' from section 1.
        const char* const twoElems[] = { "", "a" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(twoElems, 2));
        il2cpp::Il2CppString* joined2 = il2cpp::runtime::il2cpp_string_builder_join(u',', arr);
        Expect(joined2 != nullptr && StrEquals(joined2, ",a"),
               "builder_join with an empty first element yields \",a\"");
    }

    // --- 17. repeat: zero / negative count -> empty ----------------------------
    {
        il2cpp::Il2CppString* zero = il2cpp::runtime::il2cpp_string_repeat(
            reinterpret_cast<Il2CppObject*>(s1), 0);
        Expect(zero != nullptr && zero->length == 0,
               "repeat(s, 0) yields an empty string");
        il2cpp::Il2CppString* neg = il2cpp::runtime::il2cpp_string_repeat(
            reinterpret_cast<Il2CppObject*>(s1), -5);
        Expect(neg != nullptr && neg->length == 0,
               "repeat(s, negative) yields an empty string (defensive)");
    }

    // --- 18. repeat: count 1 -> identity (0 allocations) -----------------------
    {
        il2cpp::Il2CppString* once = il2cpp::runtime::il2cpp_string_repeat(
            reinterpret_cast<Il2CppObject*>(s1), 1);
        Expect(once == s1, "repeat(s, 1) returns the input string unchanged (identity)");
    }

    // --- 19. repeat: content repetition ----------------------------------------
    {
        il2cpp::Il2CppString* rep = il2cpp::runtime::il2cpp_string_repeat(
            reinterpret_cast<Il2CppObject*>(Cs("ab")), 3);
        Expect(rep != nullptr && StrEquals(rep, "ababab"),
               "repeat(\"ab\", 3) yields \"ababab\"");
    }

    // --- 20. repeat: large repetition (1000 x 2 units) -------------------------
    {
        il2cpp::Il2CppString* rep = il2cpp::runtime::il2cpp_string_repeat(
            reinterpret_cast<Il2CppObject*>(Cs("xy")), 1000);
        Expect(rep != nullptr && rep->length == 2000,
               "repeat(\"xy\", 1000) has the full 2000-unit length");
        Expect(rep != nullptr && rep->chars[0] == u'x' && rep->chars[1] == u'y'
               && rep->chars[1998] == u'x' && rep->chars[1999] == u'y',
               "large repeat payload spot-checks (first/last pair)");
    }

    // --- 21. Allocation-count assertions (pool side) ---------------------------
    // Warm up: the first string creation registers the System.String class and
    // may touch lazy state; measure deltas only after that settles.
    (void)Cs("warmup");
    {
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_concat_many(3, s1, s2, s1);
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount - before.allocCount == 1,
               "concat_many performs exactly ONE non-GC scratch allocation");
        Expect(after.freeCount - before.freeCount == 1,
               "concat_many frees its scratch buffer (no pool leak)");
        Expect(after.totalBytes - before.totalBytes ==
               static_cast<uint64_t>(joined->length) * sizeof(char16_t),
               "scratch bytes equal the exact total payload size");
    }
    {
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        il2cpp::runtime::il2cpp_string_concat_many(1, s1); // identity fast path
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount == before.allocCount && after.freeCount == before.freeCount,
               "concat_many identity path performs ZERO scratch allocations");
    }
    {
        const char* const elems[] = { "m", "n" };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 2));
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        il2cpp::runtime::il2cpp_string_builder_join(u';', arr);
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount - before.allocCount == 1,
               "builder_join performs exactly ONE non-GC scratch allocation");
        Expect(after.freeCount - before.freeCount == 1,
               "builder_join frees its scratch buffer (no pool leak)");
    }
    {
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        il2cpp::runtime::il2cpp_string_repeat(reinterpret_cast<Il2CppObject*>(Cs("q")), 4);
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount - before.allocCount == 1,
               "repeat performs exactly ONE non-GC scratch allocation");
        Expect(after.freeCount - before.freeCount == 1,
               "repeat frees its scratch buffer (no pool leak)");
    }
    // Managed-side note: the runtime exposes no GC allocation counter, so the
    // "exactly one managed allocation" claim is enforced structurally -- the
    // implementation's only call into the managed allocator is the final
    // il2cpp_string_new (see concat.cpp), one allocation per result, and the
    // identity fast paths allocate nothing at all.

    // --- 22. GC integration: rooted inputs + result survive collection --------
    {
        il2cpp::Il2CppString* r1 = Cs("root1");
        il2cpp::Il2CppString* r2 = Cs("root2");
        il2cpp::Il2CppString* result = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&r1));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&r2));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&result));
        result = il2cpp::runtime::il2cpp_string_concat_many(2, r1, r2);
        il2cpp::il2cpp_gc_collect();
        Expect(result != nullptr && StrEquals(result, "root1root2"),
               "rooted segments + concat result survive collection intact");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&r1));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&r2));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&result));
    }

    // --- 23. Error path: length beyond Int32.MaxValue -> OverflowException -----
    {
        il2cpp::Il2CppString* forged = Cs("tiny");
        const int32_t savedLength = forged->length;
        forged->length = 2147483647; // forged metadata: no 4GB allocation needed
        bool threwOverflow = false;
        try
        {
            il2cpp::runtime::il2cpp_string_concat_many(2, forged, Cs("x"));
        }
        catch (const il2cpp::DotNetException& e)
        {
            threwOverflow = (std::strcmp(
                e.type_name(),
                "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception") == 0);
        }
        forged->length = savedLength; // restore before the object dies
        Expect(threwOverflow,
               "concat_many with a total length beyond Int32.MaxValue throws OverflowException");
    }

    // --- 24. object[] concat (boxed elements -> net48 ToString) ---------------
    {
        // Build an object[] with a boxed int, a string, a null and a boxed bool
        // (the Concat(params object[]) shape -- boxed elements must be
        // converted with their net48 ToString text, never misread as strings).
        il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(
            il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 4);
        *reinterpret_cast<il2cpp::Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, 0)) =
            il2cpp::il2cpp_box_int32(nullptr, 1);
        *reinterpret_cast<il2cpp::Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, 1)) =
            reinterpret_cast<Il2CppObject*>(Cs("x"));
        *reinterpret_cast<il2cpp::Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, 2)) = nullptr;
        *reinterpret_cast<il2cpp::Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, 3)) =
            il2cpp::runtime::icall_box_bool(true);
        il2cpp::Il2CppString* joined = il2cpp::runtime::il2cpp_string_concat_array(
            reinterpret_cast<Il2CppObject*>(arr));
        Expect(joined != nullptr && StrEquals(joined, "1xTrue"),
               "object[] concat converts boxed elements via ToString (\"1xTrue\")");
    }

    // --- 25. boxed-object concat_many (3-arg object form) ----------------------
    {
        // string.Concat(object, object, object) routes here: a boxed int must
        // format its value between the string segments (net48 "a56").
        il2cpp::Il2CppString* r = il2cpp::runtime::il2cpp_string_concat_many(
            3, Cs("a"), il2cpp::il2cpp_box_int32(nullptr, 5),
            il2cpp::il2cpp_box_int32(nullptr, 6));
        Expect(r != nullptr && StrEquals(r, "a56"),
               "concat_many with boxed int args yields \"a56\"");
    }

    // --- 26. net48 empty-segment identity: exactly one non-empty segment ------
    {
        // net48 Concat("", s) returns s by reference (the IsNullOrEmpty fast
        // path) -- the runtime must not allocate a fresh copy here.
        il2cpp::Il2CppString* s = Cs("abc");
        il2cpp::Il2CppString* r1 = il2cpp::runtime::il2cpp_string_concat_many(2, Cs(""), s);
        Expect(r1 == s, "concat_many(\"\", s) returns s by identity (net48)");
        il2cpp::Il2CppString* r2 = il2cpp::runtime::il2cpp_string_concat_many(3, Cs(""), s, nullptr);
        Expect(r2 == s, "concat_many(\"\", s, null) returns s by identity (net48)");
    }

    // --- 27. all-empty results -> the interned String.Empty (net48 identity) --
    {
        // net48 ReferenceEquals(Concat("", ""), "") == true -- the empty result
        // is the interned "" (String.Empty), NOT a fresh allocation.
        il2cpp::Il2CppString* pooledEmpty = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_string_intern_literal(""));
        il2cpp::Il2CppString* r = il2cpp::runtime::il2cpp_string_concat_many(2, Cs(""), Cs(""));
        Expect(r == pooledEmpty, "all-empty concat_many returns the interned empty (String.Empty)");
        il2cpp::Il2CppString* z = il2cpp::runtime::il2cpp_string_concat_many(0);
        Expect(z == pooledEmpty, "concat_many(0) returns the interned empty (String.Empty)");
        const char* const elems[] = { "", nullptr };
        il2cpp::Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 2));
        il2cpp::Il2CppString* za = il2cpp::runtime::il2cpp_string_concat_array(arr);
        Expect(za == pooledEmpty, "all-empty concat_array returns the interned empty (String.Empty)");
    }

    // --- 28. intern chain after folding: concat yields a FRESH (non-interned) --
    //       instance; String.Intern folds it back to the pool instance.
    {
        il2cpp::Il2CppObject* pooled = il2cpp::il2cpp_string_intern_literal("intern-chain");
        il2cpp::Il2CppString* built = il2cpp::runtime::il2cpp_string_concat_many(
            2, Cs("intern-"), Cs("chain"));
        // The transpiler no longer folds constant Concat into a literal (the
        // StringPoolPass identity transform) -- the runtime concat must produce
        // a fresh, non-interned instance (net48: Concat returns a fresh copy).
        Expect(built != nullptr && reinterpret_cast<Il2CppObject*>(built) != pooled,
               "concat of two literals is a FRESH instance (not the interned pool entry)");
        // String.Intern of the fresh result returns the pool instance.
        Il2CppObject* interned = il2cpp::il2cpp_string_intern(
            reinterpret_cast<Il2CppObject*>(built));
        Expect(interned == pooled,
               "String.Intern(concat result) returns the intern-pool instance");
        // Interning did not rewrite the fresh instance (net48 keeps the copy).
        Expect(reinterpret_cast<Il2CppObject*>(built) != pooled,
               "the runtime concat copy stays non-interned after Intern");
    }

    // --- Report -----------------------------------------------------------------
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s1));
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s2));
    if (g_failures != 0)
    {
        std::printf("%d concat assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime concat tests passed\n");
    return 0;
}
