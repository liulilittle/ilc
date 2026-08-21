//------------------------------------------------------------------------------
// test_stringext3.cpp -- System.String positional-search and comparison-mode
// icall runtime tests (il2cpp_string_ext3.h / stringext3.cpp).
//
// Coverage (normal + edge + error paths). Every numeric expectation below
// was empirically verified against the .NET Framework 4.8 reference runtime
// (the stringoracle csc v4.0.30319 runs) -- the assertions ARE the oracle:
//   1.  icall_string_index_of_c_from: first match after a start, start ==
//       Length -> -1, out-of-range both directions -> AOO.
//   2.  icall_string_index_of_c_range: windowed match, count == 0 -> -1,
//       startIndex + count == Length legal, overflow of the window -> AOO.
//   3.  icall_string_index_of_s_from: positional string search, empty value
//       -> startIndex, startIndex == Length legal with empty value, needle
//       longer than the remaining span -> -1, null value -> ANE.
//   4.  icall_string_index_of_s_range: windowed string search, empty value
//       -> startIndex, needle longer than the window -> -1, window
//       overflow -> AOO.
//   5.  icall_string_last_index_of_c_from: last match before a start,
//       startIndex == Length THROWS (char form), negative -> AOO.
//   6.  icall_string_last_index_of_c_range: backward window, count == 0
//       -> -1, count > startIndex + 1 -> AOO (the net48 quirk).
//   7.  icall_string_last_index_of_s: last occurrence, empty value ->
//       Length - 1, empty string + empty value -> 0, null value -> ANE.
//   8.  icall_string_last_index_of_s_from: startIndex == Length LEGAL
//       (string form differs from char form), empty-value quirk
//       (startIndex, Length - 1, or 0), out-of-range -> AOO.
//   9.  icall_string_last_index_of_s_range: backward window, count == 0
//       -> -1, count > startIndex + 1 -> AOO, empty-value quirk preserved.
//   10. icall_string_startswith_culture: Ordinal exact, OrdinalIgnoreCase
//       ASCII fold, empty value -> true, invalid mode (6, -1, 99) ->
//       ArgumentException, null value -> ANE, culture modes equal their
//       ordinal counterparts (documented ASCII-domain deviation).
//   11. icall_string_endswith_culture: symmetric coverage.
//   12. Error path: null receiver -> NRE for every family.
//   13. icall_string_pad_left: char-pad, totalWidth < Length returns the
//       SAME instance, totalWidth == Length creates a NEW string with equal
//       content (net48 quirk, csc-probe-verified -- differs from .NET 8),
//       default space (2-arg form), empty string, wide pad char, negative
//       width -> AOO, null receiver -> NRE.
//   14. icall_string_pad_right: symmetric coverage.
//   15. icall_string_ctor_repeat: 'x' repeated, count == 0 -> empty, wide
//       char, NUL char, count < 0 -> AOO.
//   16. icall_string_index_of_any_range: first anyOf element in the window,
//       no match -> -1, empty anyOf -> -1, (start, count) == (Length, 0) ->
//       -1, null anyOf -> ANE, startIndex > Length / count beyond the tail /
//       negative args -> AOO, empty string with (0, 0) -> -1.
//   17. icall_string_last_index_of_any_range: last anyOf element in the
//       backward window, window not including a match -> -1, empty anyOf ->
//       -1, empty string with ANY start/count -> -1 (short-circuit BEFORE the
//       bounds checks), startIndex >= Length -> AOO, count < 0 or
//       count - 1 > startIndex -> AOO, null anyOf -> ANE.
//   18. Error path: null receiver -> NRE for the three new families.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext3.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
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

// Object-view of an ASCII literal: the icalls take Il2CppObject*.
Il2CppObject* Obj(const char* ascii)
{
    return reinterpret_cast<Il2CppObject*>(Cs(ascii));
}

// char[] array from an ASCII literal (the IndexOfAny/LastIndexOfAny anyOf
// argument; elemSize 2 = char16_t, a value array, not a reference array).
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

// Object-view of a char[] array (the icalls take Il2CppObject*).
Il2CppObject* CharsObj(const char* ascii)
{
    return reinterpret_cast<Il2CppObject*>(Chars(ascii));
}

// Content equality of a managed string result against an ASCII literal.
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

// Exception-chain prefix check: the thrown chain starts with the exact
// managed full name (first element; the remaining segments are the base
// classes -- the matcher walks from the second element).
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
    using namespace il2cpp::runtime;

    // --- 1. index_of_c_from ------------------------------------------------
    Expect(icall_string_index_of_c_from(Obj("abcba"), u'b', 1) == 1,
           "IndexOf('b', 1) on \"abcba\" == 1");
    Expect(icall_string_index_of_c_from(Obj("abcba"), u'b', 3) == 3,
           "IndexOf('b', 3) on \"abcba\" == 3");
    Expect(icall_string_index_of_c_from(Obj("abcba"), u'b', 5) == -1,
           "IndexOf('b', 5) on \"abcba\" == -1 (start == Length, legal)");
    Expect(icall_string_index_of_c_from(Obj("abcba"), u'x', 0) == -1,
           "IndexOf('x', 0) on \"abcba\" == -1 (absent)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_c_from(Obj("abcba"), u'b', -1); }),
           "IndexOf('b', -1) throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_c_from(Obj("abcba"), u'b', 6); }),
           "IndexOf('b', 6) throws ArgumentOutOfRangeException (start > Length)");

    // --- 2. index_of_c_range ------------------------------------------------
    Expect(icall_string_index_of_c_range(Obj("abcba"), u'b', 1, 2) == 1,
           "IndexOf('b', 1, 2) on \"abcba\" == 1");
    Expect(icall_string_index_of_c_range(Obj("abcba"), u'b', 3, 2) == 3,
           "IndexOf('b', 3, 2) on \"abcba\" == 3");
    Expect(icall_string_index_of_c_range(Obj("abcba"), u'b', 5, 0) == -1,
           "IndexOf('b', 5, 0) on \"abcba\" == -1 (count == 0)");
    Expect(icall_string_index_of_c_range(Obj("abcba"), u'c', 3, 2) == -1,
           "IndexOf('c', 3, 2) on \"abcba\" == -1 (outside the window)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_c_range(Obj("abcba"), u'b', 5, 1); }),
           "IndexOf('b', 5, 1) throws AOO (start + count > Length)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_c_range(Obj("abcba"), u'b', 4, 2); }),
           "IndexOf('b', 4, 2) throws AOO (window overflow)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_c_range(Obj("abcba"), u'b', -1, 0); }),
           "IndexOf('b', -1, 0) throws AOO");

    // --- 3. index_of_s_from -------------------------------------------------
    Expect(icall_string_index_of_s_from(Obj("abcba"), Obj("bc"), 1) == 1,
           "IndexOf(\"bc\", 1) on \"abcba\" == 1");
    Expect(icall_string_index_of_s_from(Obj("abcba"), Obj("bc"), 3) == -1,
           "IndexOf(\"bc\", 3) on \"abcba\" == -1 (cannot fit)");
    Expect(icall_string_index_of_s_from(Obj("abc"), Obj(""), 3) == 3,
           "IndexOf(\"\", 3) on \"abc\" == 3 (empty value -> startIndex)");
    Expect(icall_string_index_of_s_from(Obj("abc"), Obj(""), 0) == 0,
           "IndexOf(\"\", 0) on \"abc\" == 0");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_s_from(Obj("abc"), Obj(""), 4); }),
           "IndexOf(\"\", 4) on \"abc\" throws AOO (start > Length)");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_index_of_s_from(Obj("abc"), nullptr, 0); }),
           "IndexOf(null, 0) throws ArgumentNullException");

    // --- 4. index_of_s_range ------------------------------------------------
    Expect(icall_string_index_of_s_range(Obj("abcba"), Obj("bc"), 1, 3) == 1,
           "IndexOf(\"bc\", 1, 3) on \"abcba\" == 1");
    Expect(icall_string_index_of_s_range(Obj("abcba"), Obj("bc"), 1, 2) == 1,
           "IndexOf(\"bc\", 1, 2) on \"abcba\" == 1 (window == needle length)");
    Expect(icall_string_index_of_s_range(Obj("abc"), Obj(""), 1, 2) == 1,
           "IndexOf(\"\", 1, 2) on \"abc\" == 1 (empty value -> startIndex)");
    Expect(icall_string_index_of_s_range(Obj("abcba"), Obj("bc"), 2, 2) == -1,
           "IndexOf(\"bc\", 2, 2) on \"abcba\" == -1 (needle longer than window)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_s_range(Obj("abcba"), Obj("b"), 0, 6); }),
           "IndexOf(\"b\", 0, 6) throws AOO (window overflow)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_s_range(Obj("abcba"), Obj("b"), -1, 0); }),
           "IndexOf(\"b\", -1, 0) throws AOO");

    // --- 5. last_index_of_c_from ---------------------------------------------
    Expect(icall_string_last_index_of_c_from(Obj("abcba"), u'b', 3) == 3,
           "LastIndexOf('b', 3) on \"abcba\" == 3");
    Expect(icall_string_last_index_of_c_from(Obj("abcba"), u'a', 3) == 0,
           "LastIndexOf('a', 3) on \"abcba\" == 0 (last at index 0)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_c_from(Obj("abcba"), u'b', 5); }),
           "LastIndexOf('b', 5) throws AOO (char form: start == Length)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_c_from(Obj("abcba"), u'b', -1); }),
           "LastIndexOf('b', -1) throws AOO");

    // --- 6. last_index_of_c_range --------------------------------------------
    Expect(icall_string_last_index_of_c_range(Obj("abcba"), u'b', 1, 1) == 1,
           "LastIndexOf('b', 1, 1) on \"abcba\" == 1");
    Expect(icall_string_last_index_of_c_range(Obj("abcba"), u'b', 3, 2) == 3,
           "LastIndexOf('b', 3, 2) on \"abcba\" == 3");
    Expect(icall_string_last_index_of_c_range(Obj("abcba"), u'b', 3, 1) == 3,
           "LastIndexOf('b', 3, 1) on \"abcba\" == 3");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_c_range(Obj("abcba"), u'b', 1, 3); }),
           "LastIndexOf('b', 1, 3) throws AOO (count > startIndex + 1)");

    // --- 7. last_index_of_s ---------------------------------------------------
    Expect(icall_string_last_index_of_s(Obj("abcba"), Obj("bc")) == 1,
           "LastIndexOf(\"bc\") on \"abcba\" == 1");
    Expect(icall_string_last_index_of_s(Obj("abcba"), Obj("ba")) == 3,
           "LastIndexOf(\"ba\") on \"abcba\" == 3 (last occurrence)");
    Expect(icall_string_last_index_of_s(Obj("abcba"), Obj("")) == 4,
           "LastIndexOf(\"\") on \"abcba\" == 4 (Length - 1)");
    Expect(icall_string_last_index_of_s(Obj(""), Obj("")) == 0,
           "LastIndexOf(\"\") on \"\" == 0 (empty string quirk)");
    Expect(icall_string_last_index_of_s(Obj("abc"), Obj("xyz")) == -1,
           "LastIndexOf(\"xyz\") on \"abc\" == -1 (cannot fit)");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_last_index_of_s(Obj("abc"), nullptr); }),
           "LastIndexOf(null) throws ArgumentNullException");

    // --- 8. last_index_of_s_from ----------------------------------------------
    Expect(icall_string_last_index_of_s_from(Obj("abcba"), Obj("bc"), 3) == 1,
           "LastIndexOf(\"bc\", 3) on \"abcba\" == 1");
    Expect(icall_string_last_index_of_s_from(Obj("abcba"), Obj("bc"), 4) == 1,
           "LastIndexOf(\"bc\", 4) on \"abcba\" == 1");
    Expect(icall_string_last_index_of_s_from(Obj("abcba"), Obj("bc"), 5) == 1,
           "LastIndexOf(\"bc\", 5) on \"abcba\" == 1 (start == Length LEGAL)");
    Expect(icall_string_last_index_of_s_from(Obj("abcba"), Obj("b"), 5) == 3,
           "LastIndexOf(\"b\", 5) on \"abcba\" == 3");
    Expect(icall_string_last_index_of_s_from(Obj("abcba"), Obj("a"), 2) == 0,
           "LastIndexOf(\"a\", 2) on \"abcba\" == 0");
    Expect(icall_string_last_index_of_s_from(Obj("abcab"), Obj("ab"), 3) == 0,
           "LastIndexOf(\"ab\", 3) on \"abcab\" == 0 (net48: match at 3 ends at 4 > startIndex -> invalid)");
    Expect(icall_string_last_index_of_s_from(Obj("abcab"), Obj("bc"), 1) == -1,
           "LastIndexOf(\"bc\", 1) on \"abcab\" == -1 (net48: match at 1 ends at 2 > startIndex)");
    Expect(icall_string_last_index_of_s_from(Obj("abcab"), Obj("ab"), 0) == -1,
           "LastIndexOf(\"ab\", 0) on \"abcab\" == -1 (net48: match at 0 ends at 1 > startIndex)");
    Expect(icall_string_last_index_of_s_from(Obj("aaab"), Obj("ab"), 3) == 2,
           "LastIndexOf(\"ab\", 3) on \"aaab\" == 2 (match ends at startIndex)");
    Expect(icall_string_last_index_of_s_from(Obj("abc"), Obj(""), 3) == 2,
           "LastIndexOf(\"\", 3) on \"abc\" == 2 (empty quirk: Length - 1)");
    Expect(icall_string_last_index_of_s_from(Obj("abc"), Obj(""), 2) == 2,
           "LastIndexOf(\"\", 2) on \"abc\" == 2 (empty quirk: startIndex)");
    Expect(icall_string_last_index_of_s_from(Obj("abc"), Obj(""), 0) == 0,
           "LastIndexOf(\"\", 0) on \"abc\" == 0");
    Expect(icall_string_last_index_of_s_from(Obj(""), Obj(""), 0) == 0,
           "LastIndexOf(\"\", 0) on \"\" == 0 (empty string quirk)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_s_from(Obj("abcba"), Obj("bc"), 6); }),
           "LastIndexOf(\"bc\", 6) throws AOO (start > Length)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_s_from(Obj("abcba"), Obj("bc"), -1); }),
           "LastIndexOf(\"bc\", -1) throws AOO");

    // --- 9. last_index_of_s_range ---------------------------------------------
    Expect(icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), 3, 3) == 1,
           "LastIndexOf(\"bc\", 3, 3) on \"abcba\" == 1");
    Expect(icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), 3, 2) == -1,
           "LastIndexOf(\"bc\", 3, 2) on \"abcba\" == -1 (outside the window)");
    Expect(icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), 2, 2) == 1,
           "LastIndexOf(\"bc\", 2, 2) on \"abcba\" == 1");
    Expect(icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), 5, 6) == 1,
           "LastIndexOf(\"bc\", 5, 6) on \"abcba\" == 1 (start == Length legal)");
    Expect(icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), 2, 0) == -1,
           "LastIndexOf(\"bc\", 2, 0) on \"abcba\" == -1 (count == 0)");
    Expect(icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), 5, 0) == -1,
           "LastIndexOf(\"bc\", 5, 0) on \"abcba\" == -1 (count == 0)");
    Expect(icall_string_last_index_of_s_range(Obj("abcab"), Obj("ab"), 3, 3) == -1,
           "LastIndexOf(\"ab\", 3, 3) on \"abcab\" == -1 (net48: only match starts outside the window)");
    Expect(icall_string_last_index_of_s_range(Obj("abcab"), Obj("ab"), 3, 1) == -1,
           "LastIndexOf(\"ab\", 3, 1) on \"abcab\" == -1 (net48: match at 3 ends past startIndex)");
    Expect(icall_string_last_index_of_s_range(Obj("abcab"), Obj("ab"), 2, 2) == -1,
           "LastIndexOf(\"ab\", 2, 2) on \"abcab\" == -1 (net48: match at 0 starts outside the window)");
    Expect(icall_string_last_index_of_s_range(Obj("abcab"), Obj("ab"), 2, 3) == 0,
           "LastIndexOf(\"ab\", 2, 3) on \"abcab\" == 0 (window [0, 2], match ends at 1)");
    Expect(icall_string_last_index_of_s_range(Obj("abc"), Obj(""), 3, 1) == 2,
           "LastIndexOf(\"\", 3, 1) on \"abc\" == 2 (empty quirk preserved)");
    Expect(icall_string_last_index_of_s_range(Obj("abc"), Obj(""), 0, 0) == 0,
           "LastIndexOf(\"\", 0, 0) on \"abc\" == 0 (empty quirk, count == 0)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), 0, 2); }),
           "LastIndexOf(\"bc\", 0, 2) throws AOO (count > startIndex + 1)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_s_range(Obj("abcba"), Obj("bc"), -1, 1); }),
           "LastIndexOf(\"bc\", -1, 1) throws AOO");

    // --- 10. startswith_culture ----------------------------------------------
    Expect(icall_string_startswith_culture(Obj("abc"), Obj("Ab"), 4) == false,
           "StartsWith(\"Ab\", Ordinal) on \"abc\" == false");
    Expect(icall_string_startswith_culture(Obj("abc"), Obj("Ab"), 5) == true,
           "StartsWith(\"Ab\", OrdinalIgnoreCase) on \"abc\" == true");
    Expect(icall_string_startswith_culture(Obj("abc"), Obj("abcD"), 4) == false,
           "StartsWith(\"abcD\", Ordinal) on \"abc\" == false (needle longer)");
    Expect(icall_string_startswith_culture(Obj("abc"), Obj(""), 4) == true,
           "StartsWith(\"\", Ordinal) on \"abc\" == true");
    Expect(icall_string_startswith_culture(Obj("aBc"), Obj("aBc"), 5) == true,
           "StartsWith(\"aBc\", OrdinalIgnoreCase) on \"aBc\" == true");
    Expect(icall_string_startswith_culture(Obj("aBc"), Obj("aBcD"), 5) == false,
           "StartsWith(\"aBcD\", OrdinalIgnoreCase) on \"aBc\" == false");
    Expect(icall_string_startswith_culture(Obj("abc"), Obj("aB"), 0) == false,
           "StartsWith(\"aB\", CurrentCulture) == false (ASCII == ordinal)");
    Expect(icall_string_startswith_culture(Obj("abc"), Obj("aB"), 1) == true,
           "StartsWith(\"aB\", CurrentCultureIgnoreCase) == true");
    Expect(icall_string_startswith_culture(Obj("a"), Obj("x"), 2) == false,
           "StartsWith(\"x\", InvariantCulture) on \"a\" == false");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { icall_string_startswith_culture(Obj("abc"), Obj("ab"), 99); }),
           "StartsWith with mode 99 throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { icall_string_startswith_culture(Obj("abc"), Obj("ab"), -1); }),
           "StartsWith with mode -1 throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { icall_string_startswith_culture(Obj("abc"), Obj("ab"), 6); }),
           "StartsWith with mode 6 throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_startswith_culture(Obj("abc"), nullptr, 4); }),
           "StartsWith(null, Ordinal) throws ArgumentNullException");

    // --- 11. endswith_culture -------------------------------------------------
    Expect(icall_string_endswith_culture(Obj("abc"), Obj("Bc"), 5) == true,
           "EndsWith(\"Bc\", OrdinalIgnoreCase) on \"abc\" == true");
    Expect(icall_string_endswith_culture(Obj("abc"), Obj("bC"), 4) == false,
           "EndsWith(\"bC\", Ordinal) on \"abc\" == false (case-sensitive)");
    Expect(icall_string_endswith_culture(Obj("abc"), Obj(""), 4) == true,
           "EndsWith(\"\", Ordinal) on \"abc\" == true");
    Expect(icall_string_endswith_culture(Obj("abc"), Obj("Bc"), 1) == true,
           "EndsWith(\"Bc\", CurrentCultureIgnoreCase) on \"abc\" == true");
    Expect(icall_string_endswith_culture(Obj("abc"), Obj("BcD"), 5) == false,
           "EndsWith(\"BcD\", OrdinalIgnoreCase) on \"abc\" == false (needle longer)");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { icall_string_endswith_culture(Obj("abc"), Obj("bc"), 42); }),
           "EndsWith with mode 42 throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_endswith_culture(Obj("abc"), nullptr, 4); }),
           "EndsWith(null, Ordinal) throws ArgumentNullException");

    // --- 12. Error path: null receiver -> NRE for every family ----------------
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_index_of_c_from(nullptr, u'a', 0); }),
           "IndexOf(char, start) on null throws NullReferenceException");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_index_of_s_range(nullptr, Obj("a"), 0, 1); }),
           "IndexOf(string, start, count) on null throws NullReferenceException");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_last_index_of_c_range(nullptr, u'a', 0, 1); }),
           "LastIndexOf(char, start, count) on null throws NullReferenceException");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_last_index_of_s(nullptr, Obj("a")); }),
           "LastIndexOf(string) on null throws NullReferenceException");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_startswith_culture(nullptr, Obj("a"), 4); }),
           "StartsWith(culture) on null throws NullReferenceException");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_endswith_culture(nullptr, Obj("a"), 4); }),
           "EndsWith(culture) on null throws NullReferenceException");

    // --- 13. pad_left ----------------------------------------------------------
    Expect(StrEquals(icall_string_pad_left(Obj("5"), 3, u'0'), "005"),
           "PadLeft(\"5\", 3, '0') == \"005\"");
    {
        Il2CppObject* src = Obj("abc");
        Expect(icall_string_pad_left(src, 2, u'0') == src,
               "PadLeft(\"abc\", 2, '0') returns the SAME instance (totalWidth < Length, net48)");
    }
    {
        Il2CppObject* src = Obj("abc");
        Expect(StrEquals(icall_string_pad_left(src, 3, u'0'), "abc"),
               "PadLeft(\"abc\", 3, '0') content unchanged (totalWidth == Length)");
        Expect(icall_string_pad_left(src, 3, u'0') != src,
               "PadLeft(\"abc\", 3, '0') is NOT the same instance (net48 quirk: totalWidth == Length allocates)");
    }
    Expect(StrEquals(icall_string_pad_left(Obj("5"), 3), "  5"),
           "PadLeft(\"5\", 3) == \"  5\" (default space, 2-arg form)");
    Expect(StrEquals(icall_string_pad_left(Obj(""), 3, u'x'), "xxx"),
           "PadLeft(\"\", 3, 'x') == \"xxx\" (empty string)");
    {
        Il2CppObject* src = Obj("ab");
        Expect(StrEquals(icall_string_pad_left(src, 2), "ab"),
               "PadLeft(\"ab\", 2) content unchanged (2-arg form, totalWidth == Length)");
    }
    {
        Il2CppObject* r = icall_string_pad_left(Obj("5"), 3, u'\u4e2d');
        const Il2CppString* s = reinterpret_cast<const Il2CppString*>(r);
        Expect(s != nullptr && s->length == 3 && s->chars[0] == u'\u4e2d'
               && s->chars[1] == u'\u4e2d' && s->chars[2] == u'5',
               "PadLeft(\"5\", 3, '\\u4e2d') == \"\\u4e2d\\u4e2d5\" (wide pad char)");
    }
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_pad_left(Obj("5"), -1, u'0'); }),
           "PadLeft(\"5\", -1, '0') throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_pad_left(Obj("5"), -1); }),
           "PadLeft(\"5\", -1) throws ArgumentOutOfRangeException (2-arg form)");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_pad_left(nullptr, 3, u'0'); }),
           "PadLeft(null, 3, '0') throws NullReferenceException");

    // --- 14. pad_right ---------------------------------------------------------
    Expect(StrEquals(icall_string_pad_right(Obj("5"), 3, u'0'), "500"),
           "PadRight(\"5\", 3, '0') == \"500\"");
    {
        Il2CppObject* src = Obj("abc");
        Expect(icall_string_pad_right(src, 2, u'0') == src,
               "PadRight(\"abc\", 2, '0') returns the SAME instance (totalWidth < Length, net48)");
    }
    {
        Il2CppObject* src = Obj("abc");
        Expect(StrEquals(icall_string_pad_right(src, 3, u'0'), "abc"),
               "PadRight(\"abc\", 3, '0') content unchanged (totalWidth == Length)");
        Expect(icall_string_pad_right(src, 3, u'0') != src,
               "PadRight(\"abc\", 3, '0') is NOT the same instance (net48 quirk: totalWidth == Length allocates)");
    }
    Expect(StrEquals(icall_string_pad_right(Obj("5"), 3), "5  "),
           "PadRight(\"5\", 3) == \"5  \" (default space, 2-arg form)");
    Expect(StrEquals(icall_string_pad_right(Obj(""), 3, u'x'), "xxx"),
           "PadRight(\"\", 3, 'x') == \"xxx\" (empty string)");
    {
        Il2CppObject* src = Obj("ab");
        Expect(StrEquals(icall_string_pad_right(src, 2), "ab"),
               "PadRight(\"ab\", 2) content unchanged (2-arg form, totalWidth == Length)");
    }
    {
        Il2CppObject* r = icall_string_pad_right(Obj("5"), 3, u'\u4e2d');
        const Il2CppString* s = reinterpret_cast<const Il2CppString*>(r);
        Expect(s != nullptr && s->length == 3 && s->chars[0] == u'5'
               && s->chars[1] == u'\u4e2d' && s->chars[2] == u'\u4e2d',
               "PadRight(\"5\", 3, '\\u4e2d') == \"5\\u4e2d\\u4e2d\" (wide pad char)");
    }
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_pad_right(Obj("5"), -1, u'0'); }),
           "PadRight(\"5\", -1, '0') throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_pad_right(Obj("5"), -1); }),
           "PadRight(\"5\", -1) throws ArgumentOutOfRangeException (2-arg form)");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_pad_right(nullptr, 3, u'0'); }),
           "PadRight(null, 3, '0') throws NullReferenceException");

    // --- 15. ctor_repeat ----------------------------------------------------
    Expect(StrEquals(icall_string_ctor_repeat(nullptr, u'*', 3), "***"),
           "new string('*', 3) == \"***\"");
    Expect(StrEquals(icall_string_ctor_repeat(nullptr, u'x', 0), ""),
           "new string('x', 0) == \"\" (count == 0 -> empty)");
    {
        Il2CppObject* r = icall_string_ctor_repeat(nullptr, u'\u4e2d', 2);
        const Il2CppString* s = reinterpret_cast<const Il2CppString*>(r);
        Expect(s != nullptr && s->length == 2 && s->chars[0] == u'\u4e2d'
               && s->chars[1] == u'\u4e2d',
               "new string('\\u4e2d', 2) == \"\\u4e2d\\u4e2d\" (wide char)");
    }
    {
        Il2CppObject* r = icall_string_ctor_repeat(nullptr, u'\0', 2);
        const Il2CppString* s = reinterpret_cast<const Il2CppString*>(r);
        Expect(s != nullptr && s->length == 2 && s->chars[0] == 0
               && s->chars[1] == 0,
               "new string('\\0', 2) preserves NUL units (len 2)");
    }
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_repeat(nullptr, u'*', -1); }),
           "new string('*', -1) throws ArgumentOutOfRangeException");

    // --- 16. index_of_any_range ---------------------------------------------
    Expect(icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), 0, 5) == 1,
           "IndexOfAny(\"xb\", 0, 5) on \"abcba\" == 1");
    Expect(icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), 2, 2) == 3,
           "IndexOfAny(\"xb\", 2, 2) on \"abcba\" == 3");
    Expect(icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), 2, 1) == -1,
           "IndexOfAny(\"xb\", 2, 1) on \"abcba\" == -1 (outside the window)");
    Expect(icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), 5, 0) == -1,
           "IndexOfAny(\"xb\", 5, 0) on \"abcba\" == -1 (empty window at Length)");
    Expect(icall_string_index_of_any_range(Obj("abcba"), CharsObj(""), 0, 5) == -1,
           "IndexOfAny(empty anyOf) == -1 (never matches)");
    Expect(icall_string_index_of_any_range(Obj(""), CharsObj("xb"), 0, 0) == -1,
           "IndexOfAny on the empty string (0, 0) == -1");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_index_of_any_range(Obj("abcba"), nullptr, 0, 1); }),
           "IndexOfAny(null anyOf) throws ArgumentNullException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), 6, 0); }),
           "IndexOfAny(startIndex > Length) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), -1, 0); }),
           "IndexOfAny(startIndex < 0) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), 4, 2); }),
           "IndexOfAny(count beyond the tail) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_any_range(Obj("abcba"), CharsObj("xb"), 0, -1); }),
           "IndexOfAny(count < 0) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_index_of_any_range(Obj(""), CharsObj("xb"), 0, 1); }),
           "IndexOfAny on the empty string with count > 0 throws AOO");

    // --- 17. last_index_of_any_range ----------------------------------------
    Expect(icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), 4, 5) == 3,
           "LastIndexOfAny(\"xb\", 4, 5) on \"abcba\" == 3");
    Expect(icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), 2, 2) == 1,
           "LastIndexOfAny(\"xb\", 2, 2) on \"abcba\" == 1");
    Expect(icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), 2, 3) == 1,
           "LastIndexOfAny(\"xb\", 2, 3) on \"abcba\" == 1");
    Expect(icall_string_last_index_of_any_range(Obj("abxb"), CharsObj("xb"), 3, 2) == 3,
           "LastIndexOfAny(\"xb\", 3, 2) on \"abxb\" == 3");
    Expect(icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), 2, 1) == -1,
           "LastIndexOfAny(\"xb\", 2, 1) on \"abcba\" == -1 (window has no match)");
    Expect(icall_string_last_index_of_any_range(Obj("abcba"), CharsObj(""), 4, 5) == -1,
           "LastIndexOfAny(empty anyOf) == -1");
    Expect(icall_string_last_index_of_any_range(Obj(""), CharsObj("xb"), 0, 0) == -1,
           "LastIndexOfAny on the empty string (0, 0) == -1");
    Expect(icall_string_last_index_of_any_range(Obj(""), CharsObj("xb"), 1, 1) == -1,
           "LastIndexOfAny on the empty string (1, 1) == -1 (Length==0 short-circuits)");
    Expect(icall_string_last_index_of_any_range(Obj(""), CharsObj("xb"), 0, 1) == -1,
           "LastIndexOfAny on the empty string (0, 1) == -1 (Length==0 short-circuits)");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_last_index_of_any_range(Obj("abcba"), nullptr, 2, 2); }),
           "LastIndexOfAny(null anyOf) throws ArgumentNullException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), 5, 1); }),
           "LastIndexOfAny(startIndex == Length) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), -1, 1); }),
           "LastIndexOfAny(startIndex < 0) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), 2, 4); }),
           "LastIndexOfAny(count - 1 > startIndex) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_last_index_of_any_range(Obj("abcba"), CharsObj("xb"), 2, -1); }),
           "LastIndexOfAny(count < 0) throws AOO");

    // --- 18. null receiver -> NRE for the three new families -----------------
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_index_of_any_range(nullptr, CharsObj("xb"), 0, 1); }),
           "IndexOfAny(null receiver) throws NullReferenceException");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_last_index_of_any_range(nullptr, CharsObj("xb"), 0, 1); }),
           "LastIndexOfAny(null receiver) throws NullReferenceException");

    // --- Report ---------------------------------------------------------------
    if (g_failures != 0)
    {
        std::printf("%d stringext3 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringext3 tests passed\n");
    return 0;
}
