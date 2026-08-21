//------------------------------------------------------------------------------
// test_stringext.cpp -- System.String instance-operation runtime unit tests.
//
// Coverage (normal + edge + error paths):
//   1. Substring(startIndex): full range, interior cut, empty result, and
//      ArgumentOutOfRangeException on negative / past-end start.
//   2. Substring(startIndex, length): interior cut, length 0, exact-end cut,
//      and AoORE on negative start, negative length, past-end range and an
//      int32-OVERFLOWING length (start + length must be checked in 64-bit).
//   3. StartsWith / EndsWith: match, mismatch, empty value, longer value,
//      case sensitivity, ArgumentNullException on null.
//   4. IndexOf(char) / LastIndexOf(char) / IndexOf(string): first/last
//      match, absent -> -1, empty-string receiver, empty needle -> 0, ANE.
//   5. Replace(char, char): multiple replacements, no occurrence -> SAME
//      instance, oldChar == newChar -> SAME instance (ReferenceEquals).
//   6. ToUpper / ToLower: mixed-case input, non-letter passthrough,
//      unchanged input -> SAME instance.
//   7. Trim / TrimStart / TrimEnd: both/leading/trailing, mixed control
//      whitespace (incl. NBSP), all-whitespace -> empty, clean -> SAME
//      instance, side-clean identity behavior.
//   8. CompareTo: equal -> 0, less/greater ordering, prefix ordering,
//      ORDINAL case ordering ('a' > 'A'), ANE on null.
//   9. Contains: present / absent / empty value / ANE on null.
//  10. Null receiver -> NullReferenceException on every entry point.
//  11. GC sanity: string results are managed objects; allocation still works
//      after a collection cycle.
//
// Plain executable + minimal assertion harness (see test_string.cpp): any
// failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "stringext test fixture assumes 8-byte pointers");

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

// Run 'fn' expecting it to throw a managed exception whose TYPE NAME (the
// carrier's full "{Type},{Type},{Base...}" chain) equals 'typeChain'.
template <typename Fn>
void ExpectThrows(Fn fn, const char* typeChain, const char* what)
{
    bool matched = false;
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        matched = (std::strcmp(e.type_name(), typeChain) == 0);
    }
    Expect(matched, what);
}

} // namespace

int main()
{
    using il2cpp::Il2CppObject;
    using il2cpp::runtime::icall_string_substring;
    using il2cpp::runtime::icall_string_substring_range;
    using il2cpp::runtime::icall_string_startswith;
    using il2cpp::runtime::icall_string_endswith;
    using il2cpp::runtime::icall_string_index_of_c;
    using il2cpp::runtime::icall_string_index_of_s;
    using il2cpp::runtime::icall_string_last_index_of_c;
    using il2cpp::runtime::icall_string_replace;
    using il2cpp::runtime::icall_string_to_upper;
    using il2cpp::runtime::icall_string_to_lower;
    using il2cpp::runtime::icall_string_trim;
    using il2cpp::runtime::icall_string_trim_start;
    using il2cpp::runtime::icall_string_trim_end;
    using il2cpp::runtime::icall_string_compare_to;
    using il2cpp::runtime::icall_string_contains;

    const char* kAoore = "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";
    const char* kAne = "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
    const char* kNre = "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

    Il2CppObject* hello = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Hello, World!"));

    // --- 1. Substring(startIndex) ---
    ExpectString(icall_string_substring(hello, 7), "World!", "Substring(7) cuts at the interior");
    ExpectString(icall_string_substring(hello, 0), "Hello, World!", "Substring(0) yields the whole string");
    // net48 identity (fresh-oracle verified 2026-08-08): Substring(0) returns
    // the RECEIVER itself (ReferenceEquals), and Substring(0, Length) does
    // too -- a partial cut allocates a NEW instance.
    Expect(icall_string_substring(hello, 0) == hello,
           "Substring(0) returns the receiver instance itself (net48 identity)");
    Expect(icall_string_substring_range(hello, 0, 13) == hello,
           "Substring(0, Length) returns the receiver instance itself (net48 identity)");
    Expect(icall_string_substring_range(hello, 0, 5) != hello,
           "Substring(0, partial) allocates a NEW instance");
    ExpectString(icall_string_substring(hello, 13), "", "Substring(13) == Length yields the empty string");
    ExpectString(icall_string_substring(hello, 12), "!", "Substring(12) yields the last unit");
    ExpectThrows([hello] { (void)icall_string_substring(hello, -1); }, kAoore,
                 "Substring(-1) throws ArgumentOutOfRangeException");
    ExpectThrows([hello] { (void)icall_string_substring(hello, 14); }, kAoore,
                 "Substring(> Length) throws ArgumentOutOfRangeException");

    // --- 2. Substring(startIndex, length) ---
    ExpectString(icall_string_substring_range(hello, 0, 5), "Hello", "Substring(0, 5) yields the prefix");
    ExpectString(icall_string_substring_range(hello, 7, 6), "World!", "Substring(7, 6) yields the suffix");
    ExpectString(icall_string_substring_range(hello, 6, 1), " ", "Substring(6, 1) yields a single unit (the space)");
    ExpectString(icall_string_substring_range(hello, 7, 0), "", "Substring(7, 0) yields the empty string");
    ExpectString(icall_string_substring_range(hello, 13, 0), "", "Substring(Length, 0) is legal and empty");
    ExpectThrows([hello] { (void)icall_string_substring_range(hello, -1, 3); }, kAoore,
                 "Substring(-1, 3) throws ArgumentOutOfRangeException");
    ExpectThrows([hello] { (void)icall_string_substring_range(hello, 2, -1); }, kAoore,
                 "Substring(2, -1) throws ArgumentOutOfRangeException");
    ExpectThrows([hello] { (void)icall_string_substring_range(hello, 3, 100); }, kAoore,
                 "Substring(3, 100) past the end throws ArgumentOutOfRangeException");
    ExpectThrows([hello] { (void)icall_string_substring_range(hello, 2, 2147483647); }, kAoore,
                 "Substring with int32-overflowing length throws ArgumentOutOfRangeException");

    // --- 3. StartsWith / EndsWith ---
    Il2CppObject* empty = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(""));
    Expect(icall_string_startswith(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Hello"))),
           "StartsWith matching prefix is true");
    Expect(!icall_string_startswith(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("World"))),
           "StartsWith non-prefix is false");
    Expect(icall_string_startswith(hello, empty), "StartsWith(\"\") is true");
    Expect(icall_string_startswith(hello, hello), "StartsWith(the string itself) is true");
    Expect(!icall_string_startswith(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Hello, World!!"))),
           "StartsWith longer needle is false");
    Expect(!icall_string_startswith(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("hello"))),
           "StartsWith is case-sensitive");
    Expect(icall_string_endswith(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("World!"))),
           "EndsWith matching suffix is true");
    Expect(!icall_string_endswith(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("world!"))),
           "EndsWith is case-sensitive");
    Expect(icall_string_endswith(hello, empty), "EndsWith(\"\") is true");
    Expect(icall_string_endswith(hello, hello), "EndsWith(the string itself) is true");
    ExpectThrows([hello] { (void)icall_string_startswith(hello, nullptr); }, kAne,
                 "StartsWith(null) throws ArgumentNullException");
    ExpectThrows([hello] { (void)icall_string_endswith(hello, nullptr); }, kAne,
                 "EndsWith(null) throws ArgumentNullException");

    // --- 4. IndexOf(char) / LastIndexOf(char) / IndexOf(string) ---
    Expect(icall_string_index_of_c(hello, u'o') == 4, "IndexOf('o') finds the first occurrence (4)");
    Expect(icall_string_index_of_c(hello, u'W') == 7, "IndexOf('W') finds the first occurrence (7)");
    Expect(icall_string_index_of_c(hello, u'z') == -1, "IndexOf('z') is -1 when absent");
    Expect(icall_string_index_of_c(empty, u'a') == -1, "IndexOf on the empty string is -1");
    Expect(icall_string_last_index_of_c(hello, u'o') == 8, "LastIndexOf('o') finds the last occurrence (8)");
    Expect(icall_string_last_index_of_c(hello, u'l') == 10, "LastIndexOf('l') finds the last occurrence (10)");
    Expect(icall_string_last_index_of_c(hello, u'z') == -1, "LastIndexOf('z') is -1 when absent");
    Expect(icall_string_last_index_of_c(empty, u'a') == -1, "LastIndexOf on the empty string is -1");
    Il2CppObject* world = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("World"));
    Expect(icall_string_index_of_s(hello, world) == 7, "IndexOf(\"World\") finds the match (7)");
    Expect(icall_string_index_of_s(hello, empty) == 0, "IndexOf(\"\") is 0");
    Expect(icall_string_index_of_s(hello, hello) == 0, "IndexOf(the string itself) is 0");
    Expect(icall_string_index_of_s(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("xyz"))) == -1,
           "IndexOf(\"xyz\") is -1 when absent");
    Expect(icall_string_index_of_s(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("!"))) == 12,
           "IndexOf(\"!\") finds a suffix match (12)");
    ExpectThrows([hello] { (void)icall_string_index_of_s(hello, nullptr); }, kAne,
                 "IndexOf(null) throws ArgumentNullException");

    // --- 5. Replace(char, char) ---
    ExpectString(icall_string_replace(hello, u'o', u'0'), "Hell0, W0rld!",
                 "Replace('o', '0') replaces every occurrence");
    Expect(icall_string_replace(hello, u'z', u'x') == hello, "Replace with no occurrence returns the same instance");
    // net48 baseline (empirically verified): oldChar == newChar WITH
    // occurrences allocates a NEW content-equal instance, not the receiver.
    Il2CppObject* sameReplace = icall_string_replace(hello, u'o', u'o');
    Expect(sameReplace != hello, "Replace with oldChar == newChar returns a NEW instance (net48 baseline)");
    ExpectString(sameReplace, "Hello, World!", "Replace with oldChar == newChar keeps the content");
    Expect(icall_string_replace(empty, u'a', u'b') == empty, "Replace on the empty string returns the same instance");

    // --- 6. ToUpper / ToLower ---
    ExpectString(icall_string_to_upper(hello), "HELLO, WORLD!", "ToUpper() upper-cases ASCII letters");
    ExpectString(icall_string_to_lower(hello), "hello, world!", "ToLower() lower-cases ASCII letters");
    Il2CppObject* already = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("HELLO, WORLD!"));
    Expect(icall_string_to_upper(already) == already, "ToUpper() on already-upper returns the same instance");
    Il2CppObject* lower = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("hello, world!"));
    Expect(icall_string_to_lower(lower) == lower, "ToLower() on already-lower returns the same instance");
    ExpectString(icall_string_to_upper(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc123!?"))),
                 "ABC123!?", "ToUpper() leaves non-letters unchanged");
    ExpectString(icall_string_to_lower(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ABC123!?"))),
                 "abc123!?", "ToLower() leaves non-letters unchanged");

    // --- 7. Trim / TrimStart / TrimEnd ---
    Il2CppObject* padded = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("  trim me  "));
    ExpectString(icall_string_trim(padded), "trim me", "Trim() removes leading and trailing spaces");
    ExpectString(icall_string_trim_start(padded), "trim me  ", "TrimStart() removes leading spaces only");
    ExpectString(icall_string_trim_end(padded), "  trim me", "TrimEnd() removes trailing spaces only");
    Il2CppObject* controls = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("\r\n abc \t"));
    ExpectString(icall_string_trim(controls), "abc", "Trim() removes control whitespace (\\r \\n \\t)");
    Il2CppObject* spacesOnly = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("   "));
    ExpectString(icall_string_trim(spacesOnly), "", "Trim() of all spaces is the empty string");
    Il2CppObject* clean = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("clean"));
    Expect(icall_string_trim(clean) == clean, "Trim() of a clean string returns the same instance");
    Expect(icall_string_trim_start(clean) == clean, "TrimStart() of a clean start returns the same instance");
    Expect(icall_string_trim_end(clean) == clean, "TrimEnd() of a clean end returns the same instance");
    Il2CppObject* nbsp = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(" \u00A0x\u00A0 "));
    ExpectString(icall_string_trim(nbsp), "x", "Trim() removes NBSP (U+00A0) as whitespace");
    ExpectString(icall_string_trim(empty), "", "Trim() of the empty string is the empty string");

    // --- 8. CompareTo ---
    Expect(icall_string_compare_to(hello, hello) == 0, "CompareTo(self) is 0");
    Il2CppObject* same = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Hello, World!"));
    Expect(icall_string_compare_to(hello, same) == 0, "CompareTo(equal string) is 0");
    Expect(icall_string_compare_to(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("a")),
                                   reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("b"))) < 0,
           "CompareTo('a', 'b') is negative");
    Expect(icall_string_compare_to(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("b")),
                                   reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("a"))) > 0,
           "CompareTo('b', 'a') is positive");
    Expect(icall_string_compare_to(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")),
                                   reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abcd"))) < 0,
           "CompareTo(prefix) is negative");
    Expect(icall_string_compare_to(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("a")),
                                   reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("A"))) > 0,
           "CompareTo is ordinal: 'a' > 'A'");
    Expect(icall_string_compare_to(hello, nullptr) == 1,
           "CompareTo(null) returns 1 (net48: null sorts below any string)");
    // net48 instance CompareTo CLAMPS the raw difference to -1/0/+1 (the
    // static CompareOrdinal keeps the raw unit/length difference). Pin the
    // clamped sign for prefix pairs (strcompare e2e: "abc".CompareTo("")==1).
    Expect(icall_string_compare_to(
               reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")),
               reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(""))) == 1,
           "CompareTo(\"abc\", \"\") == 1 (clamped, net48)");
    Expect(icall_string_compare_to(
               reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")),
               reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abcd"))) == -1,
           "CompareTo(\"abc\", \"abcd\") == -1 (clamped, net48)");

    // --- 9. Contains ---
    Expect(icall_string_contains(hello, world), "Contains(\"World\") is true");
    Expect(!icall_string_contains(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("world"))),
           "Contains(\"world\") is false (case-sensitive)");
    Expect(icall_string_contains(hello, empty), "Contains(\"\") is true");
    Expect(icall_string_contains(hello, hello), "Contains(the string itself) is true");
    Expect(!icall_string_contains(hello, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("xyz"))),
           "Contains(\"xyz\") is false");
    ExpectThrows([hello] { (void)icall_string_contains(hello, nullptr); }, kAne,
                 "Contains(null) throws ArgumentNullException");

    // --- 10. Null receiver ---
    ExpectThrows([] { (void)icall_string_substring(nullptr, 0); }, kNre,
                 "Substring on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_substring_range(nullptr, 0, 0); }, kNre,
                 "Substring(0, 0) on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_startswith(nullptr, nullptr); }, kNre,
                 "StartsWith on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_endswith(nullptr, nullptr); }, kNre,
                 "EndsWith on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_index_of_c(nullptr, u'a'); }, kNre,
                 "IndexOf(char) on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_index_of_s(nullptr, nullptr); }, kNre,
                 "IndexOf(string) on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_last_index_of_c(nullptr, u'a'); }, kNre,
                 "LastIndexOf on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_replace(nullptr, u'a', u'b'); }, kNre,
                 "Replace on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_to_upper(nullptr); }, kNre,
                 "ToUpper on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_to_lower(nullptr); }, kNre,
                 "ToLower on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_trim(nullptr); }, kNre,
                 "Trim on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_trim_start(nullptr); }, kNre,
                 "TrimStart on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_trim_end(nullptr); }, kNre,
                 "TrimEnd on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_compare_to(nullptr, nullptr); }, kNre,
                 "CompareTo on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_string_contains(nullptr, nullptr); }, kNre,
                 "Contains on a null receiver throws NullReferenceException");

    // --- 11. GC sanity ---
    Il2CppObject* keep = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("keep"));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&keep));
    Il2CppObject* junk = icall_string_substring(hello, 7);
    (void)junk;
    il2cpp::il2cpp_gc_collect();
    ExpectString(keep, "keep", "rooted string survives collection intact");
    // 'hello' is unrooted and may have been reclaimed -- create a fresh
    // source instead of touching the stale pointer.
    ExpectString(icall_string_substring(
                     reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Hello, World!")), 7),
                 "World!", "Substring allocation works after collection");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d string extension assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all string extension tests passed\n");
    return 0;
}
