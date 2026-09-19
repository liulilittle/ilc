//------------------------------------------------------------------------------
// test_string_search.cpp -- String SEARCH icall surface unit tests.
//
// Hygiene rewrite (2026-09-13): the previous revision called the PRIVATE
// string_search.cpp helpers (il2cpp_string_index_of_char & co) which are
// declared in NO header and have zero callers in the runtime -- they could
// never link from the public surface. This revision exercises the PUBLIC
// icall surface the transpiler actually emits for the managed String
// methods (namespace il2cpp::runtime):
//   icall_string_index_of_c / _c_from
//   icall_string_last_index_of_c / _c_from
//   icall_string_contains_char
//   icall_string_startswith / endswith
//   icall_string_index_of_any_range (char[] overload)
// Expectations follow the runtime's documented net48 contracts
// (stringext.cpp / stringext3.cpp comments: strict char-form bounds,
// start==Length allowed for IndexOf(char,int) but NOT for LastIndexOf).
//------------------------------------------------------------------------------
#include "il2cpp_string.h"
#include "il2cpp_string_ext.h"
#include "il2cpp_string_ext3.h"
#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include <cstdio>
#include <cstring>

namespace {
int g_failures = 0;
void Expect(bool c, const char* w) { if (!c) { std::printf("FAIL: %s\n", w); ++g_failures; } }

// Head-prefix match, same idiom as test_arrayext.cpp: the DotNetException
// type_name() is the full chain; only the first segment is significant.
template <typename Fn>
void ExpectThrows(const char* expectedType, const char* what, Fn&& fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        const size_t len = std::strlen(expectedType);
        if (std::strncmp(thrown, expectedType, len) == 0 &&
            (thrown[len] == ',' || thrown[len] == '\0'))
        {
            return;
        }
        std::printf("FAIL: %s (threw %s, expected %s)\n", what, thrown, expectedType);
        ++g_failures;
        return;
    }
    std::printf("FAIL: %s (no exception thrown, expected %s)\n", what, expectedType);
    ++g_failures;
}

il2cpp::Il2CppArray* CharArray(const char16_t* chars, int32_t n)
{
    il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 2, false, n);
    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<char16_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, i)) = chars[i];
    }
    return a;
}
} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime; // the icall surface lives in il2cpp::runtime
    auto snew = [](const char* ascii) { return il2cpp_string_new_from_cs(ascii); };
    // The public icalls take the receiver/argument as Il2CppObject*; the
    // string creation helpers hand back Il2CppString* -- bridge explicitly.
    auto O = [](Il2CppString* s) { return reinterpret_cast<Il2CppObject*>(s); };

    // --- null receiver / null argument error paths -------------------------
    ExpectThrows("System.NullReferenceException",
                 "IndexOf(char) null receiver -> NullReferenceException",
                 [] { (void)icall_string_index_of_c(nullptr, u'a'); });
    ExpectThrows("System.NullReferenceException",
                 "IndexOf(char,start) null receiver -> NullReferenceException",
                 [] { (void)icall_string_index_of_c_from(nullptr, u'a', 0); });
    ExpectThrows("System.NullReferenceException",
                 "LastIndexOf(char) null receiver -> NullReferenceException",
                 [] { (void)icall_string_last_index_of_c(nullptr, u'a'); });
    ExpectThrows("System.NullReferenceException",
                 "LastIndexOf(char,start) null receiver -> NullReferenceException",
                 [] { (void)icall_string_last_index_of_c_from(nullptr, u'a', 0); });
    ExpectThrows("System.ArgumentNullException",
                 "Contains(char) null receiver -> ArgumentNullException",
                 [] { (void)icall_string_contains_char(nullptr, u'a'); });
    ExpectThrows("System.NullReferenceException",
                 "StartsWith null receiver -> NullReferenceException",
                 [&] { (void)icall_string_startswith(nullptr, O(snew("a"))); });
    ExpectThrows("System.ArgumentNullException",
                 "StartsWith null value -> ArgumentNullException",
                 [&] { (void)icall_string_startswith(O(snew("a")), nullptr); });
    ExpectThrows("System.NullReferenceException",
                 "EndsWith null receiver -> NullReferenceException",
                 [&] { (void)icall_string_endswith(nullptr, O(snew("a"))); });
    ExpectThrows("System.ArgumentNullException",
                 "EndsWith null value -> ArgumentNullException",
                 [&] { (void)icall_string_endswith(O(snew("a")), nullptr); });
    ExpectThrows("System.ArgumentNullException",
                 "IndexOfAny null anyOf -> ArgumentNullException",
                 [&] { (void)icall_string_index_of_any_range(O(snew("ababa")), nullptr, 0, 5); });
    ExpectThrows("System.ArgumentOutOfRangeException",
                 "IndexOf(char,-1) -> ArgumentOutOfRangeException",
                 [&] { (void)icall_string_index_of_c_from(O(snew("ababa")), u'a', -1); });
    ExpectThrows("System.ArgumentOutOfRangeException",
                 "IndexOf(char,Length+1) -> ArgumentOutOfRangeException",
                 [&] { (void)icall_string_index_of_c_from(O(snew("ababa")), u'a', 6); });
    ExpectThrows("System.ArgumentOutOfRangeException",
                 "LastIndexOf(char,Length) -> ArgumentOutOfRangeException (char form is strict)",
                 [&] { (void)icall_string_last_index_of_c_from(O(snew("ababa")), u'a', 5); });

    // --- empty string -------------------------------------------------------
    Il2CppString* e = il2cpp_string_new(nullptr, 0);
    Il2CppString* p0 = il2cpp_string_new(nullptr, 0);
    Expect(icall_string_index_of_c(O(e), u'a') == -1, "IndexOf empty -1");
    Expect(icall_string_index_of_c_from(O(e), u'a', 0) == -1,
           "IndexOf(char,0) empty -1 (start == Length is legal)");
    Expect(icall_string_last_index_of_c(O(e), u'a') == -1, "LastIndexOf empty -1");
    Expect(!icall_string_contains_char(e, u'a'), "Contains empty false");
    Expect(icall_string_startswith(O(e), O(p0)), "StartsWith empty+empty true");
    Expect(icall_string_endswith(O(e), O(p0)), "EndsWith empty+empty true");
    Il2CppArray* anyEmpty = CharArray(nullptr, 0);
    Expect(icall_string_index_of_any_range(O(e), reinterpret_cast<Il2CppObject*>(anyEmpty), 0, 0) == -1,
           "IndexOfAny empty anyOf -1");

    // --- "ababa" basic ------------------------------------------------------
    Il2CppString* s = snew("ababa");
    Expect(icall_string_index_of_c(O(s), u'a') == 0, "IndexOf 'a' 0");
    Expect(icall_string_index_of_c(O(s), u'x') == -1, "IndexOf 'x' -1");
    Expect(icall_string_index_of_c_from(O(s), u'a', 1) == 2, "IndexOf('a',1) 2");
    Expect(icall_string_index_of_c_from(O(s), u'a', 3) == 4, "IndexOf('a',3) 4");
    Expect(icall_string_index_of_c_from(O(s), u'a', 5) == -1,
           "IndexOf('a',Length) -1 (no throw)");
    Expect(icall_string_index_of_c_from(O(s), u'b', 0) == 1, "IndexOf('b',0) 1");
    Expect(icall_string_last_index_of_c(O(s), u'a') == 4, "LastIndexOf 'a' 4");
    Expect(icall_string_last_index_of_c(O(s), u'x') == -1, "LastIndexOf 'x' -1");
    Expect(icall_string_last_index_of_c_from(O(s), u'a', 4) == 4, "LastIndexOf('a',4) 4");
    Expect(icall_string_last_index_of_c_from(O(s), u'a', 3) == 2, "LastIndexOf('a',3) 2");
    Expect(icall_string_last_index_of_c_from(O(s), u'a', 0) == 0, "LastIndexOf('a',0) 0");
    Expect(icall_string_contains_char(s, u'b'), "Contains 'b'");
    Expect(!icall_string_contains_char(s, u'z'), "Contains 'z' false");

    // --- 16-char + 32-char scans --------------------------------------------
    Il2CppString* s16 = snew("abcdefghABCDEFGH");
    Expect(icall_string_index_of_c(O(s16), u'A') == 8, "IndexOf 'A' 8");
    Expect(icall_string_index_of_c_from(O(s16), u'H', 0) == 15, "IndexOf('H',0) 15");
    Expect(icall_string_last_index_of_c(O(s16), u'a') == 0, "LastIndexOf 'a' 0");
    Expect(icall_string_last_index_of_c_from(O(s16), u'H', 15) == 15, "LastIndexOf('H',15) 15");
    Il2CppString* s32 = snew("abcdefghijklmnopqrstuvwxyz012345");
    Expect(icall_string_index_of_c(O(s32), u'z') == 25, "IndexOf 'z' 25");
    Expect(icall_string_index_of_c(O(s32), u'5') == 31, "IndexOf '5' 31");
    Expect(icall_string_last_index_of_c_from(O(s32), u'a', 31) == 0, "LastIndexOf('a',31) 0");
    Expect(icall_string_last_index_of_c_from(O(s32), u'5', 31) == 31, "LastIndexOf('5',31) 31");

    // --- StartsWith / EndsWith ----------------------------------------------
    Il2CppString* pre = snew("ab");
    Il2CppString* suf = snew("ba");
    Expect(icall_string_startswith(O(s), O(pre)), "StartsWith 'ab'");
    Expect(!icall_string_startswith(O(s), O(suf)), "StartsWith 'ba' false");
    Expect(icall_string_endswith(O(s), O(suf)), "EndsWith 'ba'");
    Expect(!icall_string_endswith(O(s), O(pre)), "EndsWith 'ab' false");
    Expect(icall_string_startswith(O(s), O(p0)), "StartsWith empty value true");
    Expect(icall_string_endswith(O(s), O(p0)), "EndsWith empty value true");
    Il2CppString* longPre = snew("ababaX");
    Expect(!icall_string_startswith(O(s), O(longPre)), "StartsWith longer needle false");
    Expect(!icall_string_endswith(O(s), O(longPre)), "EndsWith longer needle false");

    // --- IndexOfAny(char[]) public surface ----------------------------------
    char16_t any1c[2] = {u'x', u'b'};
    Il2CppArray* any1 = CharArray(any1c, 2);
    Expect(icall_string_index_of_any_range(O(s), reinterpret_cast<Il2CppObject*>(any1), 0, 5) == 1,
           "IndexOfAny {x,b} 1");
    char16_t any2c[3] = {u'z', u'y', u'x'};
    Il2CppArray* any2 = CharArray(any2c, 3);
    Expect(icall_string_index_of_any_range(O(s), reinterpret_cast<Il2CppObject*>(any2), 0, 5) == -1,
           "IndexOfAny {z,y,x} -1");
    char16_t any3c[1] = {u'a'};
    Il2CppArray* any3 = CharArray(any3c, 1);
    Expect(icall_string_index_of_any_range(O(s), reinterpret_cast<Il2CppObject*>(any3), 2, 1) == 2,
           "IndexOfAny {'a'} window(2,1) 2");
    Expect(icall_string_index_of_any_range(O(s), reinterpret_cast<Il2CppObject*>(any3), 0, 5) == 0,
           "IndexOfAny {'a'} whole-string 0");

    if (g_failures) { std::printf("%d string_search test(s) failed\n", g_failures); return 1; }
    std::printf("all string_search tests passed\n"); return 0;
}
