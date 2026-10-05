//------------------------------------------------------------------------------
// test_stringext6.cpp -- System.String.Split(char[]/string[],
// StringSplitOptions) runtime unit tests (icall_string_split_options,
// il2cpp_string_ext.h).
//
// Coverage (normal + edge + error paths). Every expectation below was
// empirically verified against the .NET Framework 4.8 reference runtime (the
// rtspl2 csc v4.0.30319 probe, 2026-08-09) -- the assertions ARE the oracle:
//   1.  char[] separators + None: empty segments RETAINED (consecutive,
//       leading, trailing, all-separator input).
//   2.  char[] separators + RemoveEmptyEntries: empty segments DROPPED; an
//       all-empty split yields a 0-LENGTH array (",," -> [], "" -> []).
//   3.  null / empty separator array (char[] OR string[]) -> the whitespace
//       overload, with options applied.
//   4.  string[] separators: multi-unit separators, non-overlapping greedy,
//       FIRST match in array order at each position (the {"a","aa"} vs
//       {"aa","a"} order probe: "xaaay" -> 4 vs 3 segments).
//   5.  string[] null / EMPTY elements are SKIPPED (never separators).
//   6.  Invalid StringSplitOptions values (anything other than 0 / 1) throw
//       System.ArgumentException -- before any splitting.
//   7.  Error path: null receiver -> NullReferenceException.
//   8.  GC sanity: the returned string[] keeps its segment strings across a
//       collection cycle (the fill roots the array).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_string.h"

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
Il2CppString* Cs(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

// char[] array from an ASCII literal (elemSize 2 = char16_t, a value array).
Il2CppArray* Chars(const char* ascii)
{
    const size_t len = std::strlen(ascii);
    Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 2, false, static_cast<int32_t>(len));
    for (size_t i = 0; i < len; ++i)
    {
        *reinterpret_cast<char16_t*>(il2cpp::il2cpp_array_get_elem_ptr(arr, static_cast<int32_t>(i))) =
            static_cast<char16_t>(static_cast<unsigned char>(ascii[i]));
    }
    return arr;
}

// string[] array from a list of ASCII literals (a null entry -> a null
// element; elemSize sizeof(void*), a reference array).
Il2CppArray* Strs(const char* const* items, int32_t n)
{
    Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, n);
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* s = items[i] == nullptr
            ? nullptr
            : reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(items[i]));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)) = s;
    }
    return arr;
}

// Content equality of a managed string against an ASCII literal.
bool StrEquals(Il2CppObject* result, const char* ascii)
{
    if (result == nullptr)
    {
        return false;
    }
    const Il2CppString* s = reinterpret_cast<const Il2CppString*>(result);
    const size_t len = std::strlen(ascii);
    if (s->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// Read element 'i' of a string[] result (Il2CppObject* reference slot).
Il2CppObject* Elem(Il2CppArray* arr, int32_t i)
{
    return *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, i));
}

// Verify the split result: 'arr' must be a string[] whose elements equal the
// ASCII literals in 'expects' (which is 'n' entries).
void ExpectSplit(Il2CppArray* arr, int32_t n, const char* const* expects, const char* what)
{
    if (arr == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (arr->length != n)
    {
        Expect(false, what);
        return;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        if (!StrEquals(Elem(arr, i), expects[i]))
        {
            Expect(false, what);
            return;
        }
    }
    Expect(true, what);
}

// Exception-chain prefix check (same shape as test_stringext3.cpp).
bool ThrowsWithType(const char* expectedType, void (*fn)())
{
    try
    {
        fn();
    }
    catch (const DotNetException& e)
    {
        return std::strncmp(e.type_name(), expectedType, std::strlen(expectedType)) == 0;
    }
    return false;
}

} // namespace

int main()
{
    using il2cpp::runtime::icall_string_split_options;

    // --- 1. char[] + None: empty segments RETAINED --------------------------
    {
        const char* e[] = { "a", "", "b" };
        ExpectSplit(icall_string_split_options(Cs("a,,b"), Chars(","), 0), 3, e,
                    "Split(\"a,,b\", ',', None) keeps the empty segment");
    }
    {
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(icall_string_split_options(Cs("a,b,c"), Chars(","), 0), 3, e,
                    "Split(\"a,b,c\", ',', None) == [a|b|c]");
    }
    {
        const char* e[] = { "" };
        ExpectSplit(icall_string_split_options(Cs(""), Chars(","), 0), 1, e,
                    "Split(\"\", ',', None) == [\"\"]");
    }
    {
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split_options(Cs("abc"), Chars("x"), 0), 1, e,
                    "Split(\"abc\", 'x', None) (no separator) == [abc]");
    }

    // --- 2. char[] + RemoveEmptyEntries: empties DROPPED --------------------
    {
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs("a,,b"), Chars(","), 1), 2, e,
                    "Split(\"a,,b\", ',', RemoveEmptyEntries) == [a|b]");
    }
    {
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs(",a,b,"), Chars(","), 1), 2, e,
                    "Split(\",a,b,\", ',', RemoveEmptyEntries) == [a|b]");
    }
    {
        ExpectSplit(icall_string_split_options(Cs(",,"), Chars(","), 1), 0, nullptr,
                    "Split(\",,\", ',', RemoveEmptyEntries) == [] (0-length)");
    }
    {
        ExpectSplit(icall_string_split_options(Cs(""), Chars(","), 1), 0, nullptr,
                    "Split(\"\", ',', RemoveEmptyEntries) == [] (0-length)");
    }
    {
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split_options(Cs("abc"), Chars(","), 1), 1, e,
                    "Split(\"abc\", ',', RemoveEmptyEntries) == [abc]");
    }

    // --- 3. null / empty separator array -> the whitespace overload ---------
    {
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs("  a b  "), nullptr, 1), 2, e,
                    "Split((char[])null, RemoveEmptyEntries) == [a|b]");
    }
    {
        ExpectSplit(icall_string_split_options(Cs(""), nullptr, 1), 0, nullptr,
                    "Split(\"\", (char[])null, RemoveEmptyEntries) == []");
    }
    {
        ExpectSplit(icall_string_split_options(Cs(""), Chars(""), 1), 0, nullptr,
                    "Split(\"\", new char[0], RemoveEmptyEntries) == []");
    }
    {
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs("a b"), Strs(nullptr, 0), 0), 2, e,
                    "Split(\"a b\", new string[0], None) == whitespace overload [a|b]");
    }
    {
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs("a b"), nullptr, 0), 2, e,
                    "Split(\"a b\", (string[])null, None) == whitespace overload [a|b]");
    }
    {
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs("a b"), nullptr, 1), 2, e,
                    "Split(\"a b\", (string[])null, RemoveEmptyEntries) == [a|b]");
    }

    // --- 4. string[] separators: multi-unit, first-match-in-order -----------
    {
        const char* items[] = { "--" };
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(icall_string_split_options(Cs("a--b--c"), Strs(items, 1), 0), 3, e,
                    "Split(\"a--b--c\", {\"--\"}, None) == [a|b|c]");
    }
    {
        const char* items[] = { "--" };
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(icall_string_split_options(Cs("a--b--c"), Strs(items, 1), 1), 3, e,
                    "Split(\"a--b--c\", {\"--\"}, RemoveEmptyEntries) == [a|b|c]");
    }
    {
        const char* items[] = { "--" };
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs("--a--b--"), Strs(items, 1), 1), 2, e,
                    "Split(\"--a--b--\", {\"--\"}, RemoveEmptyEntries) == [a|b]");
    }
    {
        const char* items[] = { "--" };
        const char* e[] = { "a", "" };
        ExpectSplit(icall_string_split_options(Cs("a--"), Strs(items, 1), 0), 2, e,
                    "Split(\"a--\", {\"--\"}, None) == [a|\"\"] (trailing)");
    }
    {
        const char* items[] = { "--" };
        const char* e[] = { "a" };
        ExpectSplit(icall_string_split_options(Cs("a--"), Strs(items, 1), 1), 1, e,
                    "Split(\"a--\", {\"--\"}, RemoveEmptyEntries) == [a]");
    }
    {
        // non-overlapping greedy: "aaaa" on {"aa"} -> "", "", "".
        const char* items[] = { "aa" };
        const char* e[] = { "", "", "" };
        ExpectSplit(icall_string_split_options(Cs("aaaa"), Strs(items, 1), 0), 3, e,
                    "Split(\"aaaa\", {\"aa\"}, None) == [\"\"|\"\"|\"\"] (greedy)");
    }
    {
        const char* items[] = { "aa" };
        ExpectSplit(icall_string_split_options(Cs("aaaa"), Strs(items, 1), 1), 0, nullptr,
                    "Split(\"aaaa\", {\"aa\"}, RemoveEmptyEntries) == []");
    }
    {
        // "ab" on {"ab"}: None -> two empty segments; RemoveEmptyEntries -> [].
        const char* items[] = { "ab" };
        const char* e[] = { "", "" };
        ExpectSplit(icall_string_split_options(Cs("ab"), Strs(items, 1), 0), 2, e,
                    "Split(\"ab\", {\"ab\"}, None) == [\"\"|\"\"]");
    }
    {
        const char* items[] = { "ab" };
        ExpectSplit(icall_string_split_options(Cs("ab"), Strs(items, 1), 1), 0, nullptr,
                    "Split(\"ab\", {\"ab\"}, RemoveEmptyEntries) == []");
    }
    {
        // FIRST match in array order: {"aa","a"} on "xaaay" -> x,"",y (3).
        const char* items[] = { "aa", "a" };
        const char* e[] = { "x", "", "y" };
        ExpectSplit(icall_string_split_options(Cs("xaaay"), Strs(items, 2), 0), 3, e,
                    "Split(\"xaaay\", {\"aa\",\"a\"}, None) == [x|\"\"|y] (longest first)");
    }
    {
        // {"a","aa"} on "xaaay" -> x,"","",y (4): the FIRST array element wins.
        const char* items[] = { "a", "aa" };
        const char* e[] = { "x", "", "", "y" };
        ExpectSplit(icall_string_split_options(Cs("xaaay"), Strs(items, 2), 0), 4, e,
                    "Split(\"xaaay\", {\"a\",\"aa\"}, None) == [x|\"\"|\"\"|y] (array order)");
    }
    {
        const char* items[] = { ";", "," };
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(icall_string_split_options(Cs("a;b;c"), Strs(items, 2), 0), 3, e,
                    "Split(\"a;b;c\", {\";\",\",\"}, None) == [a|b|c]");
    }
    {
        const char* items[] = { "--" };
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split_options(Cs("abc"), Strs(items, 1), 1), 1, e,
                    "Split(\"abc\", {\"--\"}, RemoveEmptyEntries) == [abc] (no match)");
    }
    {
        // a separator longer than the remaining receiver never matches.
        const char* items[] = { "abc" };
        const char* e[] = { "ab" };
        ExpectSplit(icall_string_split_options(Cs("ab"), Strs(items, 1), 1), 1, e,
                    "Split(\"ab\", {\"abc\"}, RemoveEmptyEntries) == [ab] (sep too long)");
    }

    // --- 5. string[] null / empty elements are SKIPPED ----------------------
    {
        const char* items[] = { nullptr };
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split_options(Cs("abc"), Strs(items, 1), 0), 1, e,
                    "Split(\"abc\", {null}, None) == [abc] (null element skipped)");
    }
    {
        const char* items[] = { "" };
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split_options(Cs("abc"), Strs(items, 1), 0), 1, e,
                    "Split(\"abc\", {\"\"}, None) == [abc] (empty element skipped)");
    }
    {
        const char* items[] = { "" };
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split_options(Cs("abc"), Strs(items, 1), 1), 1, e,
                    "Split(\"abc\", {\"\"}, RemoveEmptyEntries) == [abc] (empty element skipped)");
    }
    {
        const char* items[] = { nullptr, "," };
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split_options(Cs("a,b"), Strs(items, 2), 0), 2, e,
                    "Split(\"a,b\", {null,\",\"}, None) == [a|b]");
    }

    // --- 6. invalid StringSplitOptions -> ArgumentException -----------------
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { (void)icall_string_split_options(Cs("a,b"), Chars(","), 5); }),
           "Split with options=5 throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { (void)icall_string_split_options(Cs("a,b"), Chars(","), 2); }),
           "Split with options=2 throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { (void)icall_string_split_options(Cs("a,b"), Chars(","), -1); }),
           "Split with options=-1 throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { (void)icall_string_split_options(Cs("a b"), nullptr, 5); }),
           "Split whitespace overload with options=5 throws ArgumentException");

    // --- 7. null receiver -> NRE --------------------------------------------
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { (void)icall_string_split_options(nullptr, Chars(","), 0); }),
           "Split on a null receiver throws NullReferenceException");

    // --- 8. GC sanity: segments survive a collection ------------------------
    {
        const char* items[] = { "--" };
        const char* e[] = { "a", "b" };
        Il2CppArray* rooted = icall_string_split_options(Cs("a--b"), Strs(items, 1), 1);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rooted));
        il2cpp::il2cpp_gc_collect();
        ExpectSplit(rooted, 2, e, "string[]-options split segments survive a collection");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&rooted));
    }

    // --- Report ---------------------------------------------------------------
    if (g_failures != 0)
    {
        std::printf("%d stringext6 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringext6 tests passed\n");
    return 0;
}
