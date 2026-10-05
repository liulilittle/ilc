//------------------------------------------------------------------------------
// test_stringalloc.cpp -- the Core FCall String allocation roots
// (il2cpp_string.h, string.cpp): FastAllocateString plus the 8 constructor
// forms of the gStringFuncs FCall table (ecalllist.h).
//
// Coverage (normal + edge + error paths). Every expectation below was
// empirically verified against the .NET Framework 4.8 reference runtime
// (the stralloc net48 probe, 2026-08-07):
//   1.  icall_string_fast_allocate: a zero-init string of the exact length,
//       length 0 -> empty, negative -> empty (defensive clamp).
//   2.  icall_string_ctor_char_array: whole-array copy, null -> EMPTY (not
//       ArgumentNullException -- net48 probe carr_null), empty array ->
//       empty, wide units preserved.
//   3.  icall_string_ctor_char_array_range: windowed copy, (Length, 0) legal
//       -> empty, null -> ANE, negative / window-overflow / beyond-tail ->
//       AOO (startIndex > Length - length).
//   4.  icall_string_ctor_char_ptr: null-terminated UTF-16 copy (wcslen
//       semantics), null -> empty.
//   5.  icall_string_ctor_char_ptr_range: windowed copy WITHOUT a
//       null-terminator scan (net48 copies exactly the window -- even an
//       embedded NUL), null + length 0 -> empty, null + length > 0 -> AOO,
//       negative args -> AOO, address wrap -> AOO.
//   6.  icall_string_ctor_sbyte_ptr: null-terminated byte copy decoded as
//       UTF-8 (net48 uses the ANSI code page -- documented deviation), null
//       -> empty, ASCII exact, multi-byte UTF-8 decoded.
//   7.  icall_string_ctor_sbyte_ptr_range: windowed UTF-8 decode, null ->
//       ArgumentNullException UNCONDITIONALLY (even length 0 -- net48
//       probe sbptrs_null_start0_len0; differs from the char* form),
//       negative -> AOO, address wrap -> AOO.
//   8.  icall_string_ctor_sbyte_ptr_range_encoding: encoding == 0 delegates
//       to the 3-arg form; a code page decodes via the shared codec; null +
//       length 0 -> empty, null + length > 0 -> AOO (non-null encoding
//       path), negative -> AOO.
//   9.  Error path: the argument-less roots never throw; the array/pointer
//       roots throw the exact net48 exception types.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include "il2cpp_array.h"
#include "il2cpp_encoding.h"
#include "il2cpp_exception.h"
#include "il2cpp_string_ext3.h"

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

// char[] array from an ASCII literal (elemSize 2 = char16_t, value array).
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

// Object-view of a char[] array (the ctor takes Il2CppObject*).
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

// Content equality against a raw UTF-16 buffer of 'len' units.
bool UnitsEquals(Il2CppObject* result, const char16_t* units, int32_t len)
{
    if (result == nullptr)
    {
        return false;
    }
    const Il2CppString* s = reinterpret_cast<const Il2CppString*>(result);
    if (s->length != len)
    {
        return false;
    }
    return std::memcmp(s->chars, units, static_cast<size_t>(len) * sizeof(char16_t)) == 0;
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

    // --- 1. fast_allocate ------------------------------------------------
    Il2CppObject* fa = icall_string_fast_allocate(3);
    Expect(fa != nullptr && reinterpret_cast<Il2CppString*>(fa)->length == 3,
           "FastAllocateString(3) allocates a length-3 string");
    Expect(reinterpret_cast<Il2CppString*>(fa)->chars[0] == 0
               && reinterpret_cast<Il2CppString*>(fa)->chars[2] == 0,
           "FastAllocateString content is zero-initialized (no fill)");
    Il2CppObject* fa0 = icall_string_fast_allocate(0);
    Expect(fa0 != nullptr && reinterpret_cast<Il2CppString*>(fa0)->length == 0,
           "FastAllocateString(0) -> empty string");
    Il2CppObject* faneg = icall_string_fast_allocate(-5);
    Expect(faneg != nullptr && reinterpret_cast<Il2CppString*>(faneg)->length == 0,
           "FastAllocateString(-5) clamps to empty (defensive)");

    // --- 2. ctor(char[]) --------------------------------------------------
    Expect(StrEquals(icall_string_ctor_char_array(nullptr, CharsObj("ab")), "ab"),
           "ctor(char[] \"ab\") copies the array");
    Expect(StrEquals(icall_string_ctor_char_array(nullptr, CharsObj("")), ""),
           "ctor(char[] empty) -> empty");
    Expect(StrEquals(icall_string_ctor_char_array(nullptr, nullptr), ""),
           "ctor(char[] null) -> empty (net48: NOT ANE)");
    char16_t wide[] = { u'\u4e2d', u'\u6587', 0 };
    Il2CppArray* wideArr = il2cpp::il2cpp_array_new(nullptr, 2, false, 2);
    for (int32_t i = 0; i < 2; ++i)
    {
        *reinterpret_cast<char16_t*>(il2cpp::il2cpp_array_get_elem_ptr(wideArr, i)) = wide[i];
    }
    Expect(UnitsEquals(icall_string_ctor_char_array(
                           nullptr, reinterpret_cast<Il2CppObject*>(wideArr)),
                       wide, 2),
           "ctor(char[] wide) preserves wide units");

    // --- 3. ctor(char[], int, int) -----------------------------------------
    Expect(StrEquals(icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 1, 2), "bc"),
           "ctor(char[], 1, 2) on \"abcd\" -> \"bc\"");
    Expect(StrEquals(icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 0, 0), ""),
           "ctor(char[], 0, 0) -> empty");
    Expect(StrEquals(icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 2, 2), "cd"),
           "ctor(char[], 2, 2) on \"abcd\" -> \"cd\"");
    Expect(StrEquals(icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 3, 1), "d"),
           "ctor(char[], 3, 1) on \"abcd\" -> \"d\"");
    Expect(StrEquals(icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 4, 0), ""),
           "ctor(char[], 4, 0) (start == Length) legal -> empty");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_ctor_char_array_range(nullptr, nullptr, 0, 0); }),
           "ctor(char[] null, 0, 0) throws ArgumentNullException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 0, 5); }),
           "ctor(char[], 0, 5) throws AOO (window overflow)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 5, 0); }),
           "ctor(char[], 5, 0) throws AOO (start beyond tail)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), -1, 1); }),
           "ctor(char[], -1, 1) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_char_array_range(nullptr, CharsObj("abcd"), 0, -1); }),
           "ctor(char[], 0, -1) throws AOO");

    // --- 4. ctor(char*) ----------------------------------------------------
    char16_t bufZ[] = { u'x', u'y', u'z', 0 };
    Expect(StrEquals(icall_string_ctor_char_ptr(nullptr, bufZ), "xyz"),
           "ctor(char* \"xyz\") copies until the NUL");
    Expect(StrEquals(icall_string_ctor_char_ptr(nullptr, nullptr), ""),
           "ctor(char* null) -> empty");
    char16_t bufE[] = { 0 };
    Expect(StrEquals(icall_string_ctor_char_ptr(nullptr, bufE), ""),
           "ctor(char* empty) -> empty");

    // --- 5. ctor(char*, int, int) ------------------------------------------
    static char16_t bufW[] = { u'a', u'b', u'c', u'd', u'e', 0 };
    Expect(StrEquals(icall_string_ctor_char_ptr_range(nullptr, bufW, 1, 2), "bc"),
           "ctor(char*, 1, 2) -> \"bc\"");
    Expect(StrEquals(icall_string_ctor_char_ptr_range(nullptr, bufW, 0, 0), ""),
           "ctor(char*, 0, 0) -> empty");
    Expect(StrEquals(icall_string_ctor_char_ptr_range(nullptr, bufW, 3, 2), "de"),
           "ctor(char*, 3, 2) -> \"de\"");
    Expect(StrEquals(icall_string_ctor_char_ptr_range(nullptr, bufW, 4, 1), "e"),
           "ctor(char*, 4, 1) -> \"e\"");
    // net48 copies exactly the window -- no null scan, embedded NUL included.
    static char16_t bufNul[] = { u'a', u'b', 0, u'c' };
    Expect(UnitsEquals(icall_string_ctor_char_ptr_range(nullptr, bufNul, 0, 3), bufNul, 3),
           "ctor(char*, 0, 3) copies the embedded NUL (no terminator scan)");
    Expect(StrEquals(icall_string_ctor_char_ptr_range(nullptr, nullptr, 0, 0), ""),
           "ctor(char* null, 0, 0) -> empty (length checked before null)");
    Expect(StrEquals(icall_string_ctor_char_ptr_range(nullptr, nullptr, 1, 0), ""),
           "ctor(char* null, 1, 0) -> empty (length == 0 short-circuits)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_char_ptr_range(nullptr, nullptr, 0, 1); }),
           "ctor(char* null, 0, 1) throws AOO (value)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_char_ptr_range(nullptr, bufW, -1, 1); }),
           "ctor(char*, -1, 1) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_char_ptr_range(nullptr, bufW, 0, -1); }),
           "ctor(char*, 0, -1) throws AOO");

    // --- 6. ctor(sbyte*) ----------------------------------------------------
    static const char sbytes[] = { 'h', 'i', '\0' };
    Expect(StrEquals(icall_string_ctor_sbyte_ptr(nullptr, sbytes), "hi"),
           "ctor(sbyte* \"hi\") decodes ASCII bytes");
    Expect(StrEquals(icall_string_ctor_sbyte_ptr(nullptr, nullptr), ""),
           "ctor(sbyte* null) -> empty");
    const char sbytesE[] = { '\0' };
    Expect(StrEquals(icall_string_ctor_sbyte_ptr(nullptr, sbytesE), ""),
           "ctor(sbyte* empty) -> empty");
    // Two-byte UTF-8 sequence 0xC3 0xA9 = U+00E9 (é) -- the documented UTF-8
    // deviation (net48 uses the ANSI code page for these bytes).
    const char sbytesUtf8[] = { static_cast<char>(0xC3), static_cast<char>(0xA9), '\0' };
    Il2CppObject* sbu = icall_string_ctor_sbyte_ptr(nullptr, sbytesUtf8);
    const char16_t expectE[] = { 0x00E9 };
    Expect(UnitsEquals(sbu, expectE, 1),
           "ctor(sbyte* 0xC3 0xA9) decodes as UTF-8 U+00E9");

    // --- 7. ctor(sbyte*, int, int) ------------------------------------------
    static const char sbRange[] = { 'a', 'b', 'c', 'd', '\0' };
    Expect(StrEquals(icall_string_ctor_sbyte_ptr_range(nullptr, sbRange, 1, 2), "bc"),
           "ctor(sbyte*, 1, 2) -> \"bc\"");
    Expect(StrEquals(icall_string_ctor_sbyte_ptr_range(nullptr, sbRange, 0, 0), ""),
           "ctor(sbyte*, 0, 0) -> empty");
    Expect(StrEquals(icall_string_ctor_sbyte_ptr_range(nullptr, sbRange, 2, 2), "cd"),
           "ctor(sbyte*, 2, 2) -> \"cd\"");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_ctor_sbyte_ptr_range(nullptr, nullptr, 0, 0); }),
           "ctor(sbyte* null, 0, 0) throws ANE (unconditional -- net48)");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_ctor_sbyte_ptr_range(nullptr, nullptr, 0, 1); }),
           "ctor(sbyte* null, 0, 1) throws ANE");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_sbyte_ptr_range(nullptr, sbRange, -1, 1); }),
           "ctor(sbyte*, -1, 1) throws AOO");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_sbyte_ptr_range(nullptr, sbRange, 0, -1); }),
           "ctor(sbyte*, 0, -1) throws AOO");

    // --- 8. ctor(sbyte*, int, int, Encoding) --------------------------------
    // encoding == 0 (null Encoding object) delegates to the 3-arg form.
    Expect(StrEquals(icall_string_ctor_sbyte_ptr_range_encoding(nullptr, sbRange, 1, 1, 0), "b"),
           "ctor(sbyte*, 1, 1, enc=0) delegates to the 3-arg form");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { icall_string_ctor_sbyte_ptr_range_encoding(nullptr, nullptr, 0, 0, 0); }),
           "ctor(sbyte* null, 0, 0, enc=0) delegates -> ANE");
    // Non-null encoding path (UTF-8 page): decodes via the shared codec.
    Expect(StrEquals(icall_string_ctor_sbyte_ptr_range_encoding(
                         nullptr, sbRange, 0, 3,
                         static_cast<int32_t>(il2cpp_encoding_code_page::utf8)),
                     "abc"),
           "ctor(sbyte*, 0, 3, utf8) -> \"abc\"");
    Expect(StrEquals(icall_string_ctor_sbyte_ptr_range_encoding(
                         nullptr, sbRange, 2, 2,
                         static_cast<int32_t>(il2cpp_encoding_code_page::utf8)),
                     "cd"),
           "ctor(sbyte*, 2, 2, utf8) -> \"cd\"");
    Expect(StrEquals(icall_string_ctor_sbyte_ptr_range_encoding(
                         nullptr, nullptr, 0, 0,
                         static_cast<int32_t>(il2cpp_encoding_code_page::utf8)),
                     ""),
           "ctor(sbyte* null, 0, 0, utf8) -> empty (non-null encoding path)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_sbyte_ptr_range_encoding(
                                     nullptr, nullptr, 0, 1,
                                     static_cast<int32_t>(il2cpp_encoding_code_page::utf8)); }),
           "ctor(sbyte* null, 0, 1, utf8) throws AOO (value)");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { icall_string_ctor_sbyte_ptr_range_encoding(
                                     nullptr, sbRange, 0, -1,
                                     static_cast<int32_t>(il2cpp_encoding_code_page::utf8)); }),
           "ctor(sbyte*, 0, -1, utf8) throws AOO");

    // --- Report --------------------------------------------------------------
    if (g_failures != 0)
    {
        std::printf("%d stringalloc assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringalloc tests passed\n");
    return 0;
}
