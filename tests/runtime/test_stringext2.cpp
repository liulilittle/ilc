//------------------------------------------------------------------------------
// test_stringext2.cpp -- System.String STATIC method icall runtime tests.
//
// Coverage (normal + edge + error paths):
//   1.  icall_string_compare_static: null/null, null/non-null (both
//       directions), equal, first-unit-diff (exact value), the net48 RAW
//       prefix values (DEFECT-STR2B-1: odd-common-length prefix pairs return
//       the boundary-unit diff, e.g. CompareOrdinal("", "a") == -97, while
//       even-common-length prefix pairs return the length diff),
//       ordinal casing ("A" vs "a" -> -32), empty vs non-empty.
//   2.  icall_string_equals_static: both null, one null (both directions),
//       equal content, different content, different length, case differs,
//       empty vs empty.
//   2b. icall_string_equals_culture: Equals(string, StringComparison) --
//       invalid mode -> ArgumentException (checked first), null value ->
//       false (net48 null-aware, no throw), null receiver -> NRE,
//       Ordinal / OrdinalIgnoreCase / culture modes (ordinal equivalents)
//       on ASCII inputs.
//   3.  icall_string_concat_2/3/4: basic joins, null-argument-as-empty,
//       all-null -> empty, empty segments.
//   4.  icall_string_concat_int/double/bool: numeric conveniences, null
//       string argument, INT32_MIN formatting, negative double, bool casing.
//   5.  icall_string_is_null_or_empty / is_null_or_whitespace: null, empty,
//       whitespace-only (full .NET set incl. NBSP U+00A0 and U+3000),
//       non-whitespace content.
//   6.  icall_string_join: separator between elements only, empty separator,
//       null separator, null element, multi-unit separator, single-element
//       identity, empty / null array.
//   7.  icall_string_copy: new instance with equal content, empty-string
//       copy is a fresh instance, null -> ArgumentNullException.
//   8.  icall_string_get_chars: valid indexes, index == length, negative
//       index (IndexOutOfRangeException), null receiver (NRE).
//   9.  Length reuse: il2cpp::il2cpp_string_get_length (no duplicate icall).
//   10. Pool statistics: join/copy perform exactly ONE non-GC scratch
//       allocation and free; join identity path performs ZERO.
//   11. GC integration: rooted inputs + join result survive collection.
//   12. Error path: forged element length -> OverflowException.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext2.h"

#include "il2cpp_allocator.h"
#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

using namespace il2cpp; // unqualified Il2Cpp* types (test_io.cpp convention)

namespace {

int g_failures = 0;

// Seam for the captureless overflow-path lambda (see section 16).
Il2CppArray* g_forgedArray = nullptr;

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

// Cast an icall result (Il2CppObject*) back to its string payload view.
Il2CppString* AsString(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppString*>(obj);
}

// ASCII literal length (the tests' input is always ASCII).
int32_t AsciiLen(const char* ascii)
{
    return static_cast<int32_t>(std::strlen(ascii));
}

// Content comparison: string vs ASCII literal (unit-accurate).
bool StrEquals(const Il2CppString* s, const char* ascii)
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
// null array slot, exercising the null-element contract). count 0 with a
// null pointer is legal (MSVC /W4 C2466: no zero-length arrays).
Il2CppArray* BuildStringArray(const char* const* elems, int32_t count)
{
    Il2CppArray* arr = il2cpp::il2cpp_array_new(
        il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, count);
    for (int32_t i = 0; i < count; ++i)
    {
        Il2CppObject* elem = (elems[i] != nullptr)
            ? Obj(elems[i])
            : nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)) = elem;
    }
    return arr;
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

    // --- 1. compare_static: null semantics -----------------------------------
    Expect(icall_string_compare_static(nullptr, nullptr) == 0,
           "compare(null, null) == 0");
    Expect(icall_string_compare_static(nullptr, Obj("x")) < 0,
           "compare(null, \"x\") < 0 (null < non-null)");
    Expect(icall_string_compare_static(Obj("x"), nullptr) > 0,
           "compare(\"x\", null) > 0 (non-null > null)");

    // --- 2. compare_static: ordinal content ----------------------------------
    Expect(icall_string_compare_static(Obj("abc"), Obj("abc")) == 0,
           "compare equal contents == 0");
    Expect(icall_string_compare_static(Obj("abc"), Obj("abd")) == -1,
           "compare(\"abc\", \"abd\") == -1 (first differing unit)");
    Expect(icall_string_compare_static(Obj("abd"), Obj("abc")) == 1,
           "compare(\"abd\", \"abc\") == 1 (mirror)");
    Expect(icall_string_compare_static(Obj("abc"), Obj("ab")) > 0,
           "compare(\"abc\", \"ab\") > 0 (prefix: longer > shorter)");
    Expect(icall_string_compare_static(Obj("ab"), Obj("abc")) < 0,
           "compare(\"ab\", \"abc\") < 0 (mirror)");
    Expect(icall_string_compare_static(Obj("A"), Obj("a")) == -32,
           "compare(\"A\", \"a\") == -32 (ordinal: 65 - 97)");
    Expect(icall_string_compare_static(Obj(""), Obj("a")) < 0,
           "compare(\"\", \"a\") < 0 (empty < non-empty)");
    Expect(icall_string_compare_static(Obj(""), Obj("")) == 0,
           "compare(\"\", \"\") == 0");
    // --- 2b. compare_static: net48 RAW prefix values (DEFECT-STR2B-1 fix).
    // net48 CompareOrdinal reads 2-unit pairs and the unit past the end
    // reads as 0 (null-terminated strings), so odd-common-length prefix
    // pairs return the raw boundary-unit difference while even lengths
    // return the plain length difference. All values csc-oracle-verified.
    Expect(icall_string_compare_static(Obj(""), Obj("a")) == -97,
           "compare(\"\", \"a\") == -97 (raw: 0 - 'a', NOT the length diff)");
    Expect(icall_string_compare_static(Obj("a"), Obj("")) == 97,
           "compare(\"a\", \"\") == 97 (raw mirror)");
    Expect(icall_string_compare_static(Obj("ab"), Obj("a")) == 98,
           "compare(\"ab\", \"a\") == 98 (raw: 'b' - 0)");
    Expect(icall_string_compare_static(Obj("a"), Obj("ab")) == -98,
           "compare(\"a\", \"ab\") == -98 (raw mirror)");
    Expect(icall_string_compare_static(Obj("abc"), Obj("ab")) == 1,
           "compare(\"abc\", \"ab\") == 1 (even common -> length diff)");
    Expect(icall_string_compare_static(Obj("ab"), Obj("abc")) == -1,
           "compare(\"ab\", \"abc\") == -1 (even common -> length diff)");
    Expect(icall_string_compare_static(Obj("abc"), Obj("abcd")) == -100,
           "compare(\"abc\", \"abcd\") == -100 (raw: 0 - 'd')");
    Expect(icall_string_compare_static(Obj("aa"), Obj("a")) == 97,
           "compare(\"aa\", \"a\") == 97 (raw: 'a' - 0)");
    Expect(icall_string_compare_static(Obj("abc"), Obj("abX")) == 11,
           "compare(\"abc\", \"abX\") == 11 (raw: 'c' - 'X')");
    Expect(icall_string_compare_static(Obj("abcd"), Obj("abc")) == 100,
           "compare(\"abcd\", \"abc\") == 100 (raw: 'd' - 0)");

    // --- 3. equals_static: null semantics ------------------------------------
    Expect(icall_string_equals_static(nullptr, nullptr) == true,
           "equals(null, null) == true");
    Expect(icall_string_equals_static(nullptr, Obj("")) == false,
           "equals(null, \"\") == false");
    Expect(icall_string_equals_static(Obj(""), nullptr) == false,
           "equals(\"\", null) == false");

    // --- 4. equals_static: ordinal content -----------------------------------
    Expect(icall_string_equals_static(Obj("abc"), Obj("abc")) == true,
           "equals equal contents == true");
    Expect(icall_string_equals_static(Obj("abc"), Obj("abd")) == false,
           "equals different contents == false");
    Expect(icall_string_equals_static(Obj("abc"), Obj("ab")) == false,
           "equals different lengths == false");
    Expect(icall_string_equals_static(Obj("abc"), Obj("ABC")) == false,
           "equals case differs == false (ordinal)");
    Expect(icall_string_equals_static(Obj(""), Obj("")) == true,
           "equals(\"\", \"\") == true");

    // --- 4b. equals_culture: Equals(string, StringComparison) (net48) --------
    // Fresh-oracle verified: the mode is checked FIRST (outside 0..5 ->
    // ArgumentException), a null 'value' returns FALSE (the comparison form
    // of Equals is null-aware for 'value' -- unlike Compare/StartsWith/
    // EndsWith), the culture modes are the ordinal equivalents, and the
    // ignore-case modes fold ASCII.
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { icall_string_equals_culture(Obj("abc"), Obj("x"), 99); }),
           "equals_culture mode 99 throws ArgumentException (net48)");
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { icall_string_equals_culture(Obj("abc"), nullptr, 99); }),
           "equals_culture null value + mode 99 throws ArgumentException (mode checked first)");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_equals_culture(nullptr, Obj("abc"), 4); }),
           "equals_culture(null receiver) throws NullReferenceException (defensive)");
    Expect(icall_string_equals_culture(Obj("abc"), nullptr, 4) == false,
           "equals_culture(\"abc\", null, Ordinal) == false (net48: null-aware, no throw)");
    Expect(icall_string_equals_culture(Obj("abc"), Obj("abc"), 4) == true,
           "equals_culture(\"abc\", \"abc\", Ordinal) == true");
    Expect(icall_string_equals_culture(Obj("abc"), Obj("ABC"), 4) == false,
           "equals_culture Ordinal is case-sensitive");
    Expect(icall_string_equals_culture(Obj("abc"), Obj("ABC"), 5) == true,
           "equals_culture OrdinalIgnoreCase folds ASCII case");
    Expect(icall_string_equals_culture(Obj("abc"), Obj("ABC"), 0) == false,
           "equals_culture CurrentCulture == ordinal (ASCII-domain deviation)");
    Expect(icall_string_equals_culture(Obj("abc"), Obj("ABC"), 1) == true,
           "equals_culture CurrentCultureIgnoreCase folds ASCII case");
    Expect(icall_string_equals_culture(Obj("abc"), Obj("ab"), 5) == false,
           "equals_culture length differs == false");
    Expect(icall_string_equals_culture(Obj(""), Obj(""), 4) == true,
           "equals_culture(\"\", \"\", Ordinal) == true");

    // --- 5. concat_2: fixed arity --------------------------------------------
    {
        Il2CppString* r = AsString(icall_string_concat_2(Obj("a"), Obj("b")));
        Expect(StrEquals(r, "ab"), "concat_2(\"a\", \"b\") == \"ab\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_2(nullptr, Obj("x")));
        Expect(StrEquals(r, "x"), "concat_2(null, \"x\") == \"x\" (null = empty)");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_2(Obj("x"), nullptr));
        Expect(StrEquals(r, "x"), "concat_2(\"x\", null) == \"x\" (null = empty)");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_2(nullptr, nullptr));
        Expect(r != nullptr && r->length == 0,
               "concat_2(null, null) is a non-null empty string");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_2(Obj(""), Obj("")));
        Expect(r != nullptr && r->length == 0,
               "concat_2(\"\", \"\") is a non-null empty string");
    }

    // --- 6. concat_3 / concat_4 -----------------------------------------------
    {
        Il2CppString* r = AsString(icall_string_concat_3(Obj("a"), nullptr, Obj("c")));
        Expect(StrEquals(r, "ac"), "concat_3 with a null middle == \"ac\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_3(nullptr, nullptr, nullptr));
        Expect(r != nullptr && r->length == 0,
               "concat_3(null, null, null) is a non-null empty string");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_4(Obj("a"), Obj("b"), Obj("c"), Obj("d")));
        Expect(StrEquals(r, "abcd"), "concat_4(\"a\", \"b\", \"c\", \"d\") == \"abcd\"");
    }

    // --- 7. concat_int / concat_double / concat_bool --------------------------
    {
        Il2CppString* r = AsString(icall_string_concat_int(Obj("x"), 42));
        Expect(StrEquals(r, "x42"), "concat_int(\"x\", 42) == \"x42\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_int(nullptr, -7));
        Expect(StrEquals(r, "-7"), "concat_int(null, -7) == \"-7\" (null = empty)");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_int(Obj(""), INT32_MIN));
        Expect(StrEquals(r, "-2147483648"), "concat_int(\"\", INT32_MIN) == \"-2147483648\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_double(Obj("x"), 1.5));
        Expect(StrEquals(r, "x1.5"), "concat_double(\"x\", 1.5) == \"x1.5\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_double(nullptr, 2.0));
        Expect(StrEquals(r, "2"), "concat_double(null, 2.0) == \"2\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_double(Obj("x"), -0.5));
        Expect(StrEquals(r, "x-0.5"), "concat_double(\"x\", -0.5) == \"x-0.5\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_bool(Obj("x"), true));
        Expect(StrEquals(r, "xTrue"), "concat_bool(\"x\", true) == \"xTrue\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_bool(nullptr, false));
        Expect(StrEquals(r, "False"), "concat_bool(null, false) == \"False\"");
    }

    // --- 7b. PERF1 fast-path boundaries (direct-format concat_int/bool) -------
    {
        Il2CppString* r = AsString(icall_string_concat_int(Obj(""), 0));
        Expect(StrEquals(r, "0"), "concat_int(\"\", 0) == \"0\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_int(nullptr, INT32_MAX));
        Expect(StrEquals(r, "2147483647"), "concat_int(null, INT32_MAX) == \"2147483647\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_int(Obj("v"), -1234567890));
        Expect(StrEquals(r, "v-1234567890"), "concat_int(\"v\", -1234567890) == \"v-1234567890\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_int(Obj("abcdefghijklmnopqrstuvwxyz"), 7));
        Expect(StrEquals(r, "abcdefghijklmnopqrstuvwxyz7"),
               "concat_int long string + digit == payload + digit");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_bool(Obj(""), true));
        Expect(StrEquals(r, "True"), "concat_bool(\"\", true) == \"True\"");
    }
    {
        Il2CppString* r = AsString(icall_string_concat_2(Obj("abcde"), Obj("fghij")));
        Expect(StrEquals(r, "abcdefghij"), "concat_2 multi-unit payload");
    }
    {
        // identity: exactly one non-empty segment returns THAT segment by
        // reference (zero allocations -- the PERF1 ConcatFixed fast path).
        Il2CppString* x = AsString(Obj("keepme"));
        Il2CppString* r = AsString(icall_string_concat_2(Obj(""), reinterpret_cast<Il2CppObject*>(x)));
        Expect(r == x, "concat_2(\"\", \"keepme\") returns the input by reference");
    }

    // --- 8. is_null_or_empty --------------------------------------------------
    Expect(icall_string_is_null_or_empty(nullptr) == true,
           "IsNullOrEmpty(null) == true");
    Expect(icall_string_is_null_or_empty(Obj("")) == true,
           "IsNullOrEmpty(\"\") == true");
    Expect(icall_string_is_null_or_empty(Obj(" ")) == false,
           "IsNullOrEmpty(\" \") == false");

    // --- 9. is_null_or_whitespace (full .NET Char.IsWhiteSpace set) ----------
    Expect(icall_string_is_null_or_whitespace(nullptr) == true,
           "IsNullOrWhiteSpace(null) == true");
    Expect(icall_string_is_null_or_whitespace(Obj("")) == true,
           "IsNullOrWhiteSpace(\"\") == true");
    Expect(icall_string_is_null_or_whitespace(Obj(" \t\r\n\v\f")) == true,
           "IsNullOrWhiteSpace(ASCII control whitespace) == true");
    {
        const char16_t nbsp[] = { u'\x00A0', u'\x3000', u'\x2003', u'\x2028', u'\0' };
        Il2CppString* uni = il2cpp::il2cpp_string_new(nbsp, 4);
        Expect(icall_string_is_null_or_whitespace(reinterpret_cast<Il2CppObject*>(uni)) == true,
               "IsNullOrWhiteSpace(NBSP + U+3000 + EM SPACE + LS) == true");
    }
    Expect(icall_string_is_null_or_whitespace(Obj(" x ")) == false,
           "IsNullOrWhiteSpace(\" x \") == false");
    Expect(icall_string_is_null_or_whitespace(Obj("x")) == false,
           "IsNullOrWhiteSpace(\"x\") == false");

    // --- 10. join: separator between elements only ----------------------------
    {
        const char* const elems[] = { "a", "b", "c" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        Expect(StrEquals(r, "a,b,c"), "join(\",\", {a,b,c}) == \"a,b,c\"");
        Expect(r != nullptr && r->length == 5,
               "join length = 3 units + 2 separators (no leading/trailing)");
    }
    {
        const char* const elems[] = { "a", nullptr, "c" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        Il2CppString* r = AsString(icall_string_join(Obj("-"), arr));
        Expect(StrEquals(r, "a--c"), "join(\"-\", {a,null,c}) == \"a--c\" (null element = empty)");
    }
    {
        const char* const elems[] = { "a", "b" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 2));
        Il2CppString* r = AsString(icall_string_join(Obj(""), arr));
        Expect(StrEquals(r, "ab"), "join(\"\", {a,b}) == \"ab\" (empty separator)");
    }
    {
        const char* const elems[] = { "a", "b" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 2));
        Il2CppString* r = AsString(icall_string_join(nullptr, arr));
        Expect(StrEquals(r, "ab"), "join(null separator, {a,b}) == \"ab\" (null sep = empty)");
    }
    {
        const char* const elems[] = { "a", "b" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 2));
        Il2CppString* r = AsString(icall_string_join(Obj("--"), arr));
        Expect(StrEquals(r, "a--b"), "join(\"--\", {a,b}) == \"a--b\" (multi-unit separator)");
    }
    {
        const char* const elems[] = { "a", "b", "c" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        Il2CppString* r = AsString(icall_string_join(Obj(", "), arr));
        Expect(StrEquals(r, "a, b, c"), "join(\", \", {a,b,c}) == \"a, b, c\"");
    }
    {
        const char* const one[] = { "only" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(one, 1));
        Il2CppObject* elem = *reinterpret_cast<Il2CppObject**>(
            il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0));
        Il2CppObject* r = icall_string_join(Obj("-"), arr);
        Expect(r == elem, "join(single element) returns the element unchanged (identity)");
    }
    {
        Il2CppObject* emptyArr = reinterpret_cast<Il2CppObject*>(BuildStringArray(nullptr, 0));
        Il2CppString* r = AsString(icall_string_join(Obj(","), emptyArr));
        Expect(r != nullptr && r->length == 0,
               "join(empty array) == \"\"");
    }
    {
        // .NET Join throws ArgumentNullException for a null values array
        // (net48 probe: string[] overload param name "value"). The runtime
        // join is reached by BOTH the string[] and the object[] overload
        // (same signature shape), so the array cannot be null here.
        Expect(ThrowsWithType("System.ArgumentNullException",
                              []() { icall_string_join(Obj(","), nullptr); }),
               "join(null array) throws ArgumentNullException (.NET)");
    }
    // --- 10b. join over object[]: .NET Join(object[]) calls ToString() on
    // each element -- boxed values format their text, strings pass through,
    // null elements count as empty (net48 probe: "1,x,,True") ----------------
    {
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(void*)), true, 4));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) =
            il2cpp::il2cpp_box_int32(nullptr, 1);
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) =
            reinterpret_cast<Il2CppObject*>(Cs("x"));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 2)) =
            nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 3)) =
            icall_box_bool(true);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, "1,x,,True"),
               "join(\",\", object[]{1,\"x\",null,true}) == \"1,x,,True\"");
    }
    {
        // All-boxed-int object[]: the exact stringext2 e2e L19 shape.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(void*)), true, 3));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) =
            il2cpp::il2cpp_box_int32(nullptr, 1);
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) =
            il2cpp::il2cpp_box_int32(nullptr, 2);
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 2)) =
            il2cpp::il2cpp_box_int32(nullptr, 3);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, "1,2,3"),
               "join(\",\", object[]{1,2,3}) == \"1,2,3\" (boxed ints format)");
        Expect(r != nullptr && r->length == 5, "object[] join length = 5 units");
    }
    {
        // Single non-string element: .NET returns its ToString() text -- a
        // NEW string, never the boxed element itself.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(void*)), true, 1));
        Il2CppObject* box = il2cpp::il2cpp_box_int32(nullptr, 42);
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = box;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppObject* r = icall_string_join(Obj(","), arr);
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(r != box,
               "join(single boxed element) returns a NEW string (not the box)");
        Expect(StrEquals(AsString(r), "42"), "join(single boxed element) == \"42\"");
    }
    {
        // Boxed double/char format their VALUES (G15 / the code unit), not
        // their type names.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(void*)), true, 2));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) =
            icall_box_double(1.5);
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) =
            icall_box_char(u'A');
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, "1.5,A"),
               "join(\",\", object[]{1.5,'A'}) == \"1.5,A\" (values, not type names)");
    }

    // --- 10b2. object[] FIRST-element-null (D-STR2-1): net48's object[]
    // Join returns String.Empty when the FIRST element is null
    // (`if (values.Length == 0 || values[0] == null) return String.Empty;`)
    // while the string[] overload joins a null first element as an empty
    // segment (",x"). The element type is classified by the array's element
    // descriptor (System.Object -> object[]); the transpiler emits reference
    // arrays WITHOUT a descriptor, so the content fallback (any non-string
    // element proves object[]) covers the generated shape. ------------------
    {
        // The exact GAPS.md D-STR2-1 probe, with the object[] descriptor:
        // object[]{null,"x"} -> String.Empty, NOT ",x".
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            &System_Object_class, static_cast<int32_t>(sizeof(void*)), true, 2));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) = Obj("x");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(r != nullptr && r->length == 0,
               "join(\",\", object[]{null,\"x\"}) == \"\" (D-STR2-1 first-null shortcut)");
    }
    {
        // object[]{null,42}: first null + a non-string element -> "" both via
        // the descriptor AND via the transpiler-shaped descriptor-less array.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            &System_Object_class, static_cast<int32_t>(sizeof(void*)), true, 2));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) = il2cpp::il2cpp_box_int32(nullptr, 42);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(r != nullptr && r->length == 0,
               "join(\",\", object[]{null,42}) == \"\" (descriptor-classified object[])");
    }
    {
        // The same shape WITHOUT the element descriptor (the transpiler's
        // emission): the non-string element 42 proves object[] by content.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(void*)), true, 2));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) = il2cpp::il2cpp_box_int32(nullptr, 42);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(r != nullptr && r->length == 0,
               "join(\",\", object[]{null,42}) == \"\" (content-classified object[])");
    }
    {
        // object[]{null,2.5,true}: first null + boxed non-strings -> "".
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            &System_Object_class, static_cast<int32_t>(sizeof(void*)), true, 3));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) = icall_box_double(2.5);
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 2)) = icall_box_bool(true);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(r != nullptr && r->length == 0,
               "join(\",\", object[]{null,2.5,true}) == \"\" (object[] first-null)");
    }
    {
        // Single null element in an object[]: net48 object[]{null} -> "".
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            &System_Object_class, static_cast<int32_t>(sizeof(void*)), true, 1));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(r != nullptr && r->length == 0,
               "join(\",\", object[]{null}) == \"\" (object[] single-null)");
    }
    {
        // string[]{null,"x"}: the string[] overload has NO first-null
        // shortcut (fresh-oracle: net48 returns ",x"). BuildStringArray sets
        // the string element descriptor, so the string[] classification wins.
        const char* const elems[2] = { nullptr, "x" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 2));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, ",x"),
               "join(\",\", string[]{null,\"x\"}) == \",x\" (string[] has NO shortcut)");
    }
    {
        // object[] first-null with mid string: still "" (net48
        // object[]{null,"x",42} -> ""), and a mid-null with a non-null FIRST
        // element keeps the empty-segment join (object[]{"a",null,"b"} ->
        // "a,,b") -- the shortcut is strictly first-element.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            &System_Object_class, static_cast<int32_t>(sizeof(void*)), true, 3));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) = Obj("x");
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 2)) = il2cpp::il2cpp_box_int32(nullptr, 42);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(r != nullptr && r->length == 0,
               "join(\",\", object[]{null,\"x\",42}) == \"\" (first-null, later strings)");
    }
    {
        // Mid-null in an object[] with a NON-NULL first element: empty-segment
        // join unchanged (net48 object[]{"a",null,"b"} -> "a,,b").
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            &System_Object_class, static_cast<int32_t>(sizeof(void*)), true, 3));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = Obj("a");
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) = nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 2)) = Obj("b");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, "a,,b"),
               "join(\",\", object[]{\"a\",null,\"b\"}) == \"a,,b\" (mid-null unchanged)");
    }
    {
        // D-STR2-1 RESIDUE: an object[]{null,"x"} WITHOUT the element
        // descriptor (the transpiler's emission) is indistinguishable from a
        // string[] -- every non-null element is a string, so the runtime keeps
        // the string[] empty-segment semantics (",x"). Recorded: the
        // transpiler loses the element type, so this shape cannot be exact.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(void*)), true, 2));
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 0)) = nullptr;
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(arr), 1)) = Obj("x");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, ",x"),
               "join(\",\", object[]{null,\"x\"}) descriptor-less == \",x\" (D-STR2-1 residue)");
    }

    // --- 10c. join over VALUE arrays (generic string.Join<T>, net48): the
    // byte[] shape the randomcase e2e exercises. Each element formats its
    // value via ToString (byte -> decimal, char16_t -> its unit, int32 ->
    // decimal). ------------------------------------------------
    {
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, 1, false, 4));
        char* p = reinterpret_cast<Il2CppArray*>(arr)->data;
        p[0] = static_cast<char>(205);
        p[1] = static_cast<char>(59);
        p[2] = static_cast<char>(153);
        p[3] = static_cast<char>(134);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj(","), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, "205,59,153,134"),
               "join(\",\", byte[]{205,59,153,134}) == \"205,59,153,134\" (generic Join<T>)");
    }
    {
        // int32[] value join: each element formats as decimal.
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(
            nullptr, 4, false, 3));
        int32_t* p = reinterpret_cast<int32_t*>(reinterpret_cast<Il2CppArray*>(arr)->data);
        p[0] = 1; p[1] = 22; p[2] = 333;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
        Il2CppString* r = AsString(icall_string_join(Obj("-"), arr));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        Expect(StrEquals(r, "1-22-333"),
               "join(\"-\", int32[]{1,22,333}) == \"1-22-333\" (generic Join<T>)");
    }

    // --- 11. copy: always a new instance --------------------------------------
    {
        Il2CppObject* src = Obj("abc");
        Il2CppObject* r = icall_string_copy(src);
        Expect(r != src, "copy(\"abc\") returns a DIFFERENT instance");
        Expect(StrEquals(AsString(r), "abc"), "copy content equals the source");
    }
    {
        Il2CppObject* src = Obj("");
        Il2CppObject* r = icall_string_copy(src);
        Expect(r != src, "copy(\"\") returns a DIFFERENT instance (fresh empty)");
        Expect(AsString(r) != nullptr && AsString(r)->length == 0,
               "copy(\"\") content is empty");
    }
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_copy(nullptr); }),
           "copy(null) throws ArgumentNullException");

    // --- 12. get_chars: indexer semantics -------------------------------------
    {
        Il2CppObject* s = Obj("abc");
        Expect(icall_string_get_chars(s, 0) == 97, "get_chars(\"abc\", 0) == 97");
        Expect(icall_string_get_chars(s, 2) == 99, "get_chars(\"abc\", 2) == 99");
    }
    Expect(ThrowsWithType("System.IndexOutOfRangeException",
                          []() { icall_string_get_chars(Obj("abc"), 3); }),
           "get_chars index == length throws IndexOutOfRangeException");
    Expect(ThrowsWithType("System.IndexOutOfRangeException",
                          []() { icall_string_get_chars(Obj("abc"), -1); }),
           "get_chars negative index throws IndexOutOfRangeException");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { icall_string_get_chars(nullptr, 0); }),
           "get_chars(null, 0) throws NullReferenceException (defensive)");

    // --- 13. Length: REUSE of il2cpp_string_get_length (no duplicate icall) --
    Expect(il2cpp::il2cpp_string_get_length(Obj("abc")) == 3,
           "il2cpp_string_get_length(\"abc\") == 3 (reused, not re-implemented)");
    Expect(il2cpp::il2cpp_string_get_length(Obj("")) == 0,
           "il2cpp_string_get_length(\"\") == 0");
    Expect(ThrowsWithType("System.NullReferenceException",
                          []() { (void)il2cpp::il2cpp_string_get_length(nullptr); }),
           "il2cpp_string_get_length(null) throws NullReferenceException");

    // --- 14. Pool statistics: exactly one scratch alloc + free per call ------
    (void)Cs("warmup"); // let lazy registration settle before measuring
    {
        const char* const elems[] = { "m", "n", "o" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 3));
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        Il2CppString* r = AsString(icall_string_join(Obj("-"), arr));
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount - before.allocCount == 1,
               "join performs exactly ONE non-GC scratch allocation");
        Expect(after.freeCount - before.freeCount == 1,
               "join frees its scratch buffer (no pool leak)");
        Expect(after.totalBytes - before.totalBytes ==
               static_cast<uint64_t>(r->length) * sizeof(char16_t),
               "join scratch bytes equal the exact total payload size");
    }
    {
        Il2CppObject* src = Obj("copy-me");
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        icall_string_copy(src);
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount - before.allocCount == 1,
               "copy performs exactly ONE non-GC scratch allocation");
        Expect(after.freeCount - before.freeCount == 1,
               "copy frees its scratch buffer (no pool leak)");
    }
    {
        const char* const one[] = { "only" };
        Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(one, 1));
        const il2cpp::alloc::AllocStats before = il2cpp::alloc::il2cpp_alloc_stats();
        icall_string_join(Obj("-"), arr); // identity fast path
        const il2cpp::alloc::AllocStats after = il2cpp::alloc::il2cpp_alloc_stats();
        Expect(after.allocCount == before.allocCount && after.freeCount == before.freeCount,
               "join identity path performs ZERO scratch allocations");
    }
    // Managed-side note: no GC allocation counter exists; the "one managed
    // allocation" claim is structural -- the only managed allocator call per
    // result is the final il2cpp_string_new (see stringext2.cpp), and the
    // identity fast paths allocate nothing at all.

    // --- 15. GC integration: rooted inputs + join result survive collection --
    {
        Il2CppString* r1 = Cs("root1");
        Il2CppString* r2 = Cs("root2");
        Il2CppString* result = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&r1));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&r2));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&result));
        {
            const char* const elems[] = { "root1", "root2" };
            Il2CppObject* arr = reinterpret_cast<Il2CppObject*>(BuildStringArray(elems, 2));
            il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&arr));
            result = AsString(icall_string_join(Obj("-"), arr));
            il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&arr));
        }
        il2cpp::il2cpp_gc_collect();
        Expect(result != nullptr && StrEquals(result, "root1-root2"),
               "rooted inputs + join result survive collection intact");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&r1));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&r2));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&result));
    }

    // --- 16. Error path: total length beyond Int32.MaxValue -> Overflow ------
    {
        // Forge a huge length on a SECOND element so pass 1 overflows: build a
        // fresh array by hand with a forged-length string (no 4GB allocation).
        Il2CppArray* big = il2cpp::il2cpp_array_new(
            il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 2);
        Il2CppString* forged = Cs("tiny");
        const int32_t savedLength = forged->length;
        forged->length = 2147483647; // forged metadata: no 4GB allocation needed
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(big, 0)) =
            reinterpret_cast<Il2CppObject*>(forged);
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(big, 1)) =
            reinterpret_cast<Il2CppObject*>(forged);
        g_forgedArray = big; // captureless-lambda seam (ThrowsWithType takes void(*)())
        bool threwOverflow = ThrowsWithType("System.OverflowException", []() {
            icall_string_join(Obj(","), reinterpret_cast<Il2CppObject*>(g_forgedArray));
        });
        g_forgedArray = nullptr;
        forged->length = savedLength; // restore before the object dies
        Expect(threwOverflow,
               "join with a total length beyond Int32.MaxValue throws OverflowException");
    }

    // --- Report ---------------------------------------------------------------
    if (g_failures != 0)
    {
        std::printf("%d stringext2 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringext2 tests passed\n");
    return 0;
}
