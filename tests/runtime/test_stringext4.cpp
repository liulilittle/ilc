//------------------------------------------------------------------------------
// test_stringext4.cpp -- System.String.Split(char[]) runtime unit tests
// (icall_string_split, il2cpp_string_ext.h).
//
// Coverage (normal + edge + error paths). Every expectation below was
// empirically verified against the .NET Framework 4.8 reference runtime
// (the splitprobe csc v4.0.30319 run, 2026-08-09) -- the assertions ARE the
// oracle:
//   1.  Split((char[])null) == the whitespace overload: "a b c" -> 3
//       segments, no-whitespace "abc" -> 1 segment, mixed whitespace with
//       runs -> empty segments retained, all-whitespace -> 4 empty segments,
//       empty string -> [""], leading/trailing whitespace -> empty segments.
//   2.  Split(new char[0]) == the whitespace overload (same result set).
//   3.  Split(char[] separator) explicit: single separator, multiple
//       distinct separators, consecutive separators -> empty segment,
//       leading separator -> leading empty segment, trailing separator ->
//       TRAILING empty segment retained (StringSplitOptions.None),
//       all-separator input, separator-only single char, separator absent,
//       empty string with explicit separators.
//   4.  Empty segments are String.Empty (never null).
//   5.  The whitespace set includes NBSP (U+00A0) -- split on it.
//   6.  Error path: null receiver -> NullReferenceException.
//   7.  GC sanity: the returned string[] keeps its segment strings across a
//       collection cycle (the fill roots the array; per-segment allocations
//       can never reclaim it).
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

// char[] array from an ASCII literal (the separator argument; elemSize 2 =
// char16_t, a value array, not a reference array). Empty literal -> length 0.
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
// comma-joined ASCII literals in 'expects' (which is 'n' entries).
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
    using il2cpp::runtime::icall_string_split;

    // --- 1. null separator = the whitespace overload ------------------------
    {
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(icall_string_split(Cs("a b c"), nullptr), 3, e,
                    "Split((char[])null) on \"a b c\" == [a|b|c]");
    }
    {
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split(Cs("abc"), nullptr), 1, e,
                    "Split((char[])null) on \"abc\" (no whitespace) == [abc]");
    }
    {
        // "a  b\tc\r\nd": runs + tabs + CRLF -> empty segments between them.
        const char* e[] = { "a", "", "b", "c", "", "d" };
        ExpectSplit(icall_string_split(Cs("a  b\tc\r\nd"), nullptr), 6, e,
                    "Split((char[])null) on mixed whitespace keeps empty segments");
    }
    {
        // "   " -> three separator units -> 4 empty segments.
        const char* e[] = { "", "", "", "" };
        ExpectSplit(icall_string_split(Cs("   "), nullptr), 4, e,
                    "Split((char[])null) on all-whitespace == 4 empty segments");
    }
    {
        const char* e[] = { "" };
        ExpectSplit(icall_string_split(Cs(""), nullptr), 1, e,
                    "Split((char[])null) on the empty string == [\"\"]");
    }
    {
        // "  a b  " -> leading AND trailing empty segments retained.
        const char* e[] = { "", "", "a", "b", "", "" };
        ExpectSplit(icall_string_split(Cs("  a b  "), nullptr), 6, e,
                    "Split((char[])null) keeps leading/trailing empty segments");
    }

    // --- 2. empty separator array == the whitespace overload -----------------
    {
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(icall_string_split(Cs("a b c"), Chars("")), 3, e,
                    "Split(new char[0]) on \"a b c\" == [a|b|c] (whitespace overload)");
    }
    {
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split(Cs("abc"), Chars("")), 1, e,
                    "Split(new char[0]) on \"abc\" == [abc] (whitespace overload)");
    }

    // --- 3. explicit separators ----------------------------------------------
    {
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(icall_string_split(Cs("a,b,c"), Chars(",")), 3, e,
                    "Split(\"a,b,c\", ',') == [a|b|c]");
    }
    {
        // consecutive separators -> empty segment between them.
        const char* e[] = { "a", "", "b" };
        ExpectSplit(icall_string_split(Cs("a,,b"), Chars(",")), 3, e,
                    "Split(\"a,,b\", ',') keeps the empty segment (None)");
    }
    {
        // leading separator -> leading empty segment.
        const char* e[] = { "", "a", "b" };
        ExpectSplit(icall_string_split(Cs(",a,b"), Chars(",")), 3, e,
                    "Split(\",a,b\", ',') keeps the leading empty segment");
    }
    {
        // trailing separator -> TRAILING empty segment retained (None).
        const char* e[] = { "a", "b", "" };
        ExpectSplit(icall_string_split(Cs("a,b,"), Chars(",")), 3, e,
                    "Split(\"a,b,\", ',') keeps the trailing empty segment (None)");
    }
    {
        // all separators -> 3 empty segments.
        const char* e[] = { "", "", "" };
        ExpectSplit(icall_string_split(Cs(",,"), Chars(",")), 3, e,
                    "Split(\",,\", ',') == 3 empty segments");
    }
    {
        // multiple distinct separators.
        const char* e[] = { "a", "b", "c", "d" };
        ExpectSplit(icall_string_split(Cs("a,b;c d"), Chars(",; ")), 4, e,
                    "Split(\"a,b;c d\", {',', ';', ' '}) == [a|b|c|d]");
    }
    {
        const char* e[] = { "" };
        ExpectSplit(icall_string_split(Cs(""), Chars(",")), 1, e,
                    "Split(\"\", ',') == [\"\"]");
    }
    {
        // single separator char -> two empty segments.
        const char* e[] = { "", "" };
        ExpectSplit(icall_string_split(Cs(","), Chars(",")), 2, e,
                    "Split(\",\", ',') == [\"\",\"\"]");
    }
    {
        // separator absent -> the whole string is the only segment.
        const char* e[] = { "abc" };
        ExpectSplit(icall_string_split(Cs("abc"), Chars(",")), 1, e,
                    "Split(\"abc\", ',') (no separator) == [abc]");
    }

    // --- 4. empty segments are String.Empty, never null ----------------------
    {
        Il2CppArray* arr = icall_string_split(Cs("a,,b"), Chars(","));
        Expect(arr != nullptr && arr->length == 3, "Split(\"a,,b\", ',') length is 3");
        if (arr != nullptr && arr->length == 3)
        {
            Il2CppObject* mid = Elem(arr, 1);
            Expect(mid != nullptr, "the middle segment is NOT null");
            Expect(StrEquals(mid, ""), "the middle segment equals String.Empty");
        }
    }

    // --- 5. whitespace set includes NBSP (U+00A0) ----------------------------
    {
        // Build "a\u00A0b" from explicit UTF-16 units (new_from_cs skips
        // bytes >= 0x80, so the NBSP literal cannot cross through it).
        const char16_t nbspUnits[] = { u'a', u'\u00A0', u'b' };
        Il2CppString* nbsp = il2cpp::il2cpp_string_new(nbspUnits, 3);
        const char* e[] = { "a", "b" };
        ExpectSplit(icall_string_split(nbsp, nullptr), 2, e,
                    "Split((char[])null) splits on NBSP (U+00A0)");
    }

    // --- 6. null receiver -> NRE ----------------------------------------------
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { (void)icall_string_split(nullptr, Chars(",")); }),
           "Split on a null receiver throws NullReferenceException");

    // --- 7. GC sanity: segments survive a collection --------------------------
    {
        Il2CppArray* rooted = icall_string_split(Cs("a,b,c"), Chars(","));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rooted));
        il2cpp::il2cpp_gc_collect();
        const char* e[] = { "a", "b", "c" };
        ExpectSplit(rooted, 3, e, "split segments survive a collection (rooted array)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&rooted));
    }

    // --- Report ---------------------------------------------------------------
    if (g_failures != 0)
    {
        std::printf("%d stringext4 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringext4 tests passed\n");
    return 0;
}
