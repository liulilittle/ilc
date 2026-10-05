//------------------------------------------------------------------------------
// test_stringops_gcstress.cpp -- GC-stress tests for the System.String
// allocation-heavy OPERATION icalls (M5 BCL). Deep-testing companion to
// test_stringext*.cpp / test_stringext2.cpp: where those suites verify net48
// SEMANTICS, this suite verifies GC SAFETY under collection pressure -- the
// same latent-risk class flagged by the #114 analysis
// (docs/plans/issue-114-analysis.md) for StringBuilder/concat, applied to the
// remaining allocation-heavy string paths: Replace(string,string),
// ToUpper/ToLower, Join, Concat(2/3/4), Substring, Trim and the char[]
// constructors.
//
// The collector under test (gc.cpp) performs full-heap mark-sweep WITH
// young-generation compaction: every surviving Gen0 block is COPIED to a
// fresh Gen1 segment and every reference (static roots, root ranges, object
// fields) is rewritten through the forward map (Phase C). Two consequences
// drive this harness:
//
//   * Object addresses are NOT stable across collections. Every pointer is
//     re-read through a REGISTERED ROOT SLOT after a collection, never
//     through a cached C++ local.
//   * Every operation stages its managed payloads into non-GC scratch BEFORE
//     the single managed allocation (the documented string-domain contract).
//     A collection that moved a source between the staging read and the
//     allocation would corrupt the output -- this harness fires collections
//     right before each call to make any such bug deterministic.
//
// Coverage (normal + edge + error paths):
//   1. Replace(string,string) under GC pressure: rooted source, collections
//      fired before the call; verify the replaced content. Also the
//      no-occurrence identity path (returns the SAME instance).
//   2. Replace chained aliasing: each result feeds the next replace, with
//      collections between; verify the final content (the result of one
//      replace is the source of the next -- a moved result must stay valid).
//   3. ToUpper/ToLower under GC pressure: rooted source, collections fired;
//      verify the case mapping and the unchanged-identity path.
//   4. Join under GC pressure: rooted string[] + separator, collections
//      fired; verify the joined content and the single-element identity path.
//   5. Concat(2/3/4) under GC pressure: rooted operands, collections fired;
//      verify the concatenated content.
//   6. Substring/Trim under GC pressure: rooted source, collections fired;
//      verify the windowed/trimmed content and the identity paths
//      (Substring(0) returns the receiver; nothing-to-trim returns it too).
//   7. char[] constructors under GC pressure: a rooted char[] value array,
//      collections fired; verify the string content (whole-array and window).
//   8. Mixed churn: a deterministic pseudo-random sequence of
//      replace/upper/lower/concat/substring with a collection after every few
//      operations; verify the final content against a std::string model.
//
// Plain executable + minimal assertion harness (see test_string.cpp): any
// failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext.h"
#include "il2cpp_string_ext2.h"

#include "il2cpp_array.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace {

static_assert(sizeof(void*) == 8, "stringops gc-stress fixture assumes 8-byte pointers");

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

// Expect 'obj' (a managed string) to hold exactly the std::string 'model'.
void ExpectStringModel(il2cpp::Il2CppObject* obj, const std::string& model, const char* what)
{
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    if (s == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (static_cast<size_t>(s->length) != model.size())
    {
        Expect(false, what);
        return;
    }
    bool match = true;
    for (size_t i = 0; i < model.size(); ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(model[i])))
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

// Build a rooted char[] value array (elemSize 2, isRefElem false) from an
// ASCII literal.
il2cpp::Il2CppArray* BuildCharArray(const char* ascii)
{
    const size_t len = std::strlen(ascii);
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(
        nullptr, 2, false, static_cast<int32_t>(len));
    char16_t* units = reinterpret_cast<char16_t*>(arr->data);
    for (size_t i = 0; i < len; ++i)
    {
        units[i] = static_cast<char16_t>(static_cast<unsigned char>(ascii[i]));
    }
    return arr;
}

// Deterministic pseudo-random source (LCG; no <random> state, identical
// sequence on every platform -- the stress pattern must be reproducible).
uint32_t g_rng = 0x12345678u;
uint32_t NextRng()
{
    g_rng = g_rng * 1664525u + 1013904223u;
    return g_rng;
}

} // namespace

int main()
{
    using il2cpp::Il2CppObject;
    using il2cpp::Il2CppString;
    using il2cpp::Il2CppArray;
    using il2cpp::runtime::icall_string_replace_s_s;
    using il2cpp::runtime::icall_string_to_upper;
    using il2cpp::runtime::icall_string_to_lower;
    using il2cpp::runtime::icall_string_join;
    using il2cpp::runtime::icall_string_concat_2;
    using il2cpp::runtime::icall_string_concat_3;
    using il2cpp::runtime::icall_string_concat_4;
    using il2cpp::runtime::icall_string_substring;
    using il2cpp::runtime::icall_string_substring_range;
    using il2cpp::runtime::icall_string_trim;
    using il2cpp::runtime::icall_string_ctor_char_array;
    using il2cpp::runtime::icall_string_ctor_char_array_range;

    //--- 1. Replace(string,string) under GC pressure ---
    {
        Il2CppObject* src = NewManagedString("the quick brown fox jumps over the lazy dog");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&src));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Il2CppString* r = icall_string_replace_s_s(
                reinterpret_cast<Il2CppString*>(src),
                reinterpret_cast<Il2CppString*>(NewManagedString("the")),
                reinterpret_cast<Il2CppString*>(NewManagedString("A")));
            ExpectString(reinterpret_cast<Il2CppObject*>(r),
                         "A quick brown fox jumps over A lazy dog",
                         "replace_s_s content survives collections");
        }
        // No-occurrence identity: returns the SAME instance.
        Il2CppObject* before = src;
        il2cpp::il2cpp_gc_collect();
        Il2CppString* r = icall_string_replace_s_s(
            reinterpret_cast<Il2CppString*>(src),
            reinterpret_cast<Il2CppString*>(NewManagedString("zzz")),
            reinterpret_cast<Il2CppString*>(NewManagedString("A")));
        Expect(r == reinterpret_cast<Il2CppString*>(before),
               "replace_s_s no-occurrence returns the same instance");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&src));
    }

    //--- 2. Replace chained aliasing: result feeds the next replace ---
    {
        Il2CppObject* s = NewManagedString("aaa bbb aaa ccc aaa");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s));
        std::string model = "aaa bbb aaa ccc aaa";
        for (int32_t i = 0; i < 15; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            Il2CppString* r = icall_string_replace_s_s(
                reinterpret_cast<Il2CppString*>(s),
                reinterpret_cast<Il2CppString*>(NewManagedString("aaa")),
                reinterpret_cast<Il2CppString*>(NewManagedString("X")));
            s = reinterpret_cast<Il2CppObject*>(r);
            // model: replace every "aaa" with "X" (non-overlapping greedy)
            std::string next;
            size_t pos = 0;
            while (pos < model.size())
            {
                if (model.compare(pos, 3, "aaa") == 0)
                {
                    next += "X";
                    pos += 3;
                }
                else
                {
                    next += model[pos];
                    ++pos;
                }
            }
            model = next;
        }
        ExpectStringModel(s, model, "chained replace aliasing survives collections");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s));
    }

    //--- 3. ToUpper/ToLower under GC pressure ---
    {
        Il2CppObject* src = NewManagedString("Hello, World! 123");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&src));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_string_to_upper(src), "HELLO, WORLD! 123",
                         "to_upper content survives collections");
            ExpectString(icall_string_to_lower(src), "hello, world! 123",
                         "to_lower content survives collections");
        }
        // Unchanged-identity: an all-upper input returns the same instance.
        Il2CppObject* up = NewManagedString("ALREADY");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&up));
        il2cpp::il2cpp_gc_collect();
        Expect(icall_string_to_upper(up) == up,
               "to_upper unchanged returns the same instance");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&up));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&src));
    }

    //--- 4. Join under GC pressure ---
    {
        const char* const elems[] = { "alpha", "beta", "gamma", "delta" };
        Il2CppArray* arr = BuildStringArray(elems, 4);
        Il2CppObject* sep = NewManagedString(", ");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&sep));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_string_join(sep, reinterpret_cast<Il2CppObject*>(arr)),
                         "alpha, beta, gamma, delta",
                         "join content survives collections");
        }
        // Single-element identity: a single string element returns itself.
        const char* const one[] = { "solo" };
        Il2CppArray* arr1 = BuildStringArray(one, 1);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr1));
        il2cpp::il2cpp_gc_collect();
        Il2CppObject* joined = icall_string_join(sep, reinterpret_cast<Il2CppObject*>(arr1));
        ExpectString(joined, "solo", "join single-element identity survives collections");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr1));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&sep));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
    }

    //--- 5. Concat(2/3/4) under GC pressure ---
    {
        Il2CppObject* a = NewManagedString("foo");
        Il2CppObject* b = NewManagedString("bar");
        Il2CppObject* c = NewManagedString("baz");
        Il2CppObject* d = NewManagedString("qux");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&a));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&b));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&c));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&d));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_string_concat_2(a, b), "foobar",
                         "concat_2 content survives collections");
            ExpectString(icall_string_concat_3(a, b, c), "foobarbaz",
                         "concat_3 content survives collections");
            ExpectString(icall_string_concat_4(a, b, c, d), "foobarbazqux",
                         "concat_4 content survives collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&d));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&c));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&b));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&a));
    }

    //--- 6. Substring/Trim under GC pressure ---
    {
        Il2CppObject* src = NewManagedString("  padded text  ");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&src));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_string_substring(src, 2), "padded text  ",
                         "substring(start) content survives collections");
            ExpectString(icall_string_substring_range(src, 2, 6), "padded",
                         "substring(start,len) content survives collections");
            ExpectString(icall_string_trim(src), "padded text",
                         "trim content survives collections");
        }
        // Identity paths: Substring(0) and nothing-to-trim return the receiver.
        Il2CppObject* plain = NewManagedString("plain");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&plain));
        il2cpp::il2cpp_gc_collect();
        Expect(icall_string_substring(plain, 0) == plain,
               "substring(0) returns the same instance");
        Expect(icall_string_trim(plain) == plain,
               "trim nothing-to-trim returns the same instance");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&plain));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&src));
    }

    //--- 7. char[] constructors under GC pressure ---
    {
        Il2CppArray* carr = BuildCharArray("array content");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&carr));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_string_ctor_char_array(nullptr, reinterpret_cast<Il2CppObject*>(carr)),
                         "array content",
                         "ctor(char[]) content survives collections");
            ExpectString(icall_string_ctor_char_array_range(
                             nullptr, reinterpret_cast<Il2CppObject*>(carr), 6, 7),
                         "content",
                         "ctor(char[],start,len) content survives collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&carr));
    }

    //--- 8. Mixed churn: pseudo-random ops + collections, std::string model ---
    {
        Il2CppObject* s = NewManagedString("abc");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s));
        std::string model = "abc";
        for (int32_t step = 0; step < 200; ++step)
        {
            const uint32_t op = NextRng() % 5;
            if (op == 0)
            {
                // replace "a" -> "xy"
                il2cpp::il2cpp_gc_collect();
                Il2CppString* r = icall_string_replace_s_s(
                    reinterpret_cast<Il2CppString*>(s),
                    reinterpret_cast<Il2CppString*>(NewManagedString("a")),
                    reinterpret_cast<Il2CppString*>(NewManagedString("xy")));
                s = reinterpret_cast<Il2CppObject*>(r);
                std::string next;
                for (size_t i = 0; i < model.size(); ++i)
                {
                    if (model[i] == 'a') next += "xy";
                    else next += model[i];
                }
                model = next;
            }
            else if (op == 1)
            {
                // to_upper
                il2cpp::il2cpp_gc_collect();
                s = icall_string_to_upper(s);
                for (size_t i = 0; i < model.size(); ++i)
                {
                    if (model[i] >= 'a' && model[i] <= 'z') model[i] = static_cast<char>(model[i] - 'a' + 'A');
                }
            }
            else if (op == 2)
            {
                // concat_2 with a fixed suffix
                il2cpp::il2cpp_gc_collect();
                s = icall_string_concat_2(s, NewManagedString("!"));
                model += "!";
            }
            else if (op == 3)
            {
                // substring(1) when long enough, else substring(0)
                il2cpp::il2cpp_gc_collect();
                const int32_t start = (model.size() > 1) ? 1 : 0;
                s = icall_string_substring(s, start);
                model = model.substr(static_cast<size_t>(start));
            }
            else
            {
                // to_lower
                il2cpp::il2cpp_gc_collect();
                s = icall_string_to_lower(s);
                for (size_t i = 0; i < model.size(); ++i)
                {
                    if (model[i] >= 'A' && model[i] <= 'Z') model[i] = static_cast<char>(model[i] - 'A' + 'a');
                }
            }
            if (step % 10 == 0)
            {
                ExpectStringModel(s, model, "mixed churn model matches");
            }
        }
        ExpectStringModel(s, model, "mixed churn final content matches");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s));
    }

    if (g_failures == 0)
    {
        std::printf("all stringops gc-stress tests passed\n");
        return 0;
    }
    std::printf("%d stringops gc-stress test(s) FAILED\n", g_failures);
    return 1;
}
