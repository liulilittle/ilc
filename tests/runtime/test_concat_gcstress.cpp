//------------------------------------------------------------------------------
// test_concat_gcstress.cpp -- GC-stress tests for the string concatenation
// icall domain (M5 BCL). Deep-testing companion to test_concat.cpp: where
// that suite verifies net48 SEMANTICS, this suite verifies GC SAFETY under
// collection pressure -- the other half of the #114 analysis
// (docs/plans/issue-114-analysis.md), which flagged the concat pipeline
// (concat.cpp ConcatFromArray / il2cpp_string_concat_many /
// il2cpp_string_concat_array) alongside the StringBuilder paths.
//
// The concat pipeline's GC-safety contract (concat.cpp:128-133): element
// pointers are read TWICE (pass 1 lengths, pass 2 payloads) -- both passes
// run BEFORE the single managed allocation at the end, and the only
// allocation between them is the non-GC scratch, which cannot trigger a
// collection. This harness verifies that contract EMPIRICALLY: it roots the
// element array (and the element strings), forces full collections, then
// runs the concat and checks the result. A collection that moved the array
// or its elements between the two passes would corrupt the output.
//
// Coverage (normal + edge + error paths):
//   1. concat_array under GC pressure: a rooted string[] with several
//      elements, collections fired before the call; verify the joined
//      content. Exercises the two-pass read of the rooted array.
//   2. concat_array with null elements under GC pressure: nulls must stay
//      empty segments (net48) across collections.
//   3. concat_array with a single element under GC pressure: the identity
//      fast path (returns the element itself) must survive collections.
//   4. concat_many under GC pressure: variadic segments rooted, collections
//      fired; verify the joined content. Exercises the va_list two-pass walk.
//   5. concat_many identity fast path (count == 1) under GC pressure.
//   6. Mixed churn: interleave concat_array / concat_many with collections
//      and verify every result against the expected model.
//   7. Large concat under GC pressure: many elements totalling thousands of
//      units, collections fired; verify length + first/last units.
//
// Plain executable + minimal assertion harness (see test_string.cpp): any
// failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_concat.h"

#include "il2cpp_array.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace {

static_assert(sizeof(void*) == 8, "concat gc-stress fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Expect 'obj' (a managed string) to hold exactly the ASCII literal 'ascii'.
void ExpectString(il2cpp::Il2CppObject* obj, const char* ascii, const char* what)
{
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (static_cast<size_t>(s->length) != len)
    {
        Expect(false, what);
        return;
    }
    bool match = true;
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            match = false;
            break;
        }
    }
    Expect(match, what);
}

// Allocate a managed string from an ASCII literal, returned as Il2CppObject*
// (Il2CppString embeds Il2CppObject as its first member -- explicit cast).
il2cpp::Il2CppObject* NewManagedString(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Build a rooted string[] from ASCII literals (nullptr element = null).
il2cpp::Il2CppArray* BuildStringArray(const char* const* elems, int32_t count)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(
        il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, count);
    for (int32_t i = 0; i < count; ++i)
    {
        il2cpp::Il2CppObject* elem = (elems[i] != nullptr) ? NewManagedString(elems[i]) : nullptr;
        *reinterpret_cast<il2cpp::Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)) = elem;
    }
    return arr;
}

} // namespace

int main()
{
    using il2cpp::Il2CppObject;
    using il2cpp::Il2CppString;
    using il2cpp::runtime::il2cpp_string_concat_array;
    using il2cpp::runtime::il2cpp_string_concat_many;

    //--- 1. concat_array under GC pressure (the two-pass read of a rooted
    //        array) ---
    {
        const char* const elems[] = { "abc", "def", "ghi", "jkl" };
        il2cpp::Il2CppArray* arr = BuildStringArray(elems, 4);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(reinterpret_cast<Il2CppObject*>(il2cpp_string_concat_array(
                             reinterpret_cast<Il2CppObject*>(arr))),
                         "abcdefghijkl",
                         "concat_array survives collections (two-pass read intact)");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
    }

    //--- 2. concat_array with null elements under GC pressure ---
    {
        const char* const elems[] = { "a", nullptr, "b", nullptr, "c" };
        il2cpp::Il2CppArray* arr = BuildStringArray(elems, 5);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(reinterpret_cast<Il2CppObject*>(il2cpp_string_concat_array(
                             reinterpret_cast<Il2CppObject*>(arr))),
                         "abc",
                         "concat_array null elements stay empty across collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
    }

    //--- 3. concat_array single-element identity fast path under GC pressure ---
    {
        const char* const elems[] = { "solo" };
        il2cpp::Il2CppArray* arr = BuildStringArray(elems, 1);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Il2CppString* joined = il2cpp_string_concat_array(reinterpret_cast<Il2CppObject*>(arr));
            ExpectString(reinterpret_cast<Il2CppObject*>(joined), "solo",
                         "concat_array single-element identity survives collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
    }

    //--- 4. concat_many under GC pressure (the va_list two-pass walk) ---
    {
        Il2CppObject* s1 = NewManagedString("alpha");
        Il2CppObject* s2 = NewManagedString("beta");
        Il2CppObject* s3 = NewManagedString("gamma");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s1));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s2));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s3));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(reinterpret_cast<Il2CppObject*>(il2cpp_string_concat_many(3, s1, s2, s3)),
                         "alphabetagamma",
                         "concat_many survives collections (va_list two-pass walk intact)");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s3));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s2));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s1));
    }

    //--- 5. concat_many identity fast path (count == 1) under GC pressure ---
    {
        Il2CppObject* s1 = NewManagedString("only");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s1));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Il2CppString* joined = il2cpp_string_concat_many(1, s1);
            Expect(joined == reinterpret_cast<Il2CppString*>(s1),
                   "concat_many(1) identity survives collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s1));
    }

    //--- 6. Mixed churn: interleave concat_array / concat_many with
    //        collections and verify every result ---
    {
        const char* const elems[] = { "x", "y", "z" };
        il2cpp::Il2CppArray* arr = BuildStringArray(elems, 3);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppObject* a = NewManagedString("a");
        Il2CppObject* b = NewManagedString("b");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&a));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&b));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(reinterpret_cast<Il2CppObject*>(il2cpp_string_concat_array(
                             reinterpret_cast<Il2CppObject*>(arr))),
                         "xyz",
                         "churn concat_array result intact");
            ExpectString(reinterpret_cast<Il2CppObject*>(il2cpp_string_concat_many(2, a, b)),
                         "ab",
                         "churn concat_many result intact");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&b));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&a));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
    }

    //--- 7. Large concat under GC pressure ---
    {
        const int32_t kCount = 200;
        const char** elems = new const char*[kCount];
        for (int32_t i = 0; i < kCount; ++i)
        {
            elems[i] = "ab";
        }
        il2cpp::Il2CppArray* arr = BuildStringArray(elems, kCount);
        delete[] elems;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        for (int32_t i = 0; i < 10; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Il2CppString* joined = il2cpp_string_concat_array(reinterpret_cast<Il2CppObject*>(arr));
            Expect(joined != nullptr && joined->length == kCount * 2,
                   "large concat_array length survives collections");
            Expect(joined != nullptr && joined->chars[0] == u'a' && joined->chars[1] == u'b' &&
                       joined->chars[kCount * 2 - 2] == u'a' && joined->chars[kCount * 2 - 1] == u'b',
                   "large concat_array first/last units survive collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
    }

    //--- Report ---
    if (g_failures != 0)
    {
        std::printf("%d concat gc-stress assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all concat gc-stress tests passed\n");
    return 0;
}
