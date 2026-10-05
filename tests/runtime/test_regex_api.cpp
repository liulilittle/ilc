//------------------------------------------------------------------------------
// test_regex_api.cpp -- System.Text.RegularExpressions string-API tests.
//
// Coverage (normal + edge + error paths), mirroring the four functions
// declared in regex.h (docs/design/regex-domain.md §5 T3 card). The API takes
// the .NET static argument order -- input first, pattern second (regex.h;
// corrected from the draft pattern-first order so the transpiler's positional
// emission maps 1:1):
//   1. il2cpp_regex_is_match: hit / miss / anchored boundary starts; null
//      pattern & null input -> ArgumentNullException; invalid pattern ->
//      ArgumentException; whitelist-external option bit ->
//      NotSupportedException.
//   2. il2cpp_regex_match: [0] = whole match, [1..n] = group strings,
//      outStart/outLength exact; no match -> EMPTY array + -1/-1; a
//      non-participating group -> NULL element; outStart/outLength may be
//      null.
//   3. il2cpp_regex_replace: literal replacement, $1 group reference,
//      $10 boundary (group 10 exists -> group 10; absent -> group 1 +
//      literal '0'), $$ literal dollar, $0 whole match, every-match
//      replacement, empty-pattern advancement (does not hang), unknown
//      '$x' kept literally, trailing '$' literal.
//   4. il2cpp_regex_split: plain split, consecutive separators
//      ("a,,b" -> ["a","","b"]), leading/trailing (",a," -> ["","a",""]),
//      no match -> single element [original], empty-pattern advancement.
//
// SEMANTIC BASELINE: net48. The engine half (T2 regex_match.h) had not
// landed when this file was written; the pre-engine paths (validation and
// exception mapping) are independently verifiable with a stub engine, the
// match-semantic assertions activate with the real T2 engine.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "regex.h"
#include "il2cpp_exception.h"
#include "il2cpp_string.h"
#include "il2cpp_array.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

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

il2cpp::Il2CppString* S(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

bool StrEq(const il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const int32_t len = static_cast<int32_t>(std::strlen(ascii));
    if (s->length != len)
    {
        return false;
    }
    for (int32_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

il2cpp::Il2CppString* ArrStr(il2cpp::Il2CppArray* arr, int32_t index)
{
    return *reinterpret_cast<il2cpp::Il2CppString**>(
        il2cpp::il2cpp_array_get_elem_ptr(arr, index));
}

// Snapshot of a caught exception (test_arraybounds.cpp idiom): the type
// chain and message are copied out of the exception at catch time.
struct CaughtInfo
{
    bool   caught;
    char   type[160];
    char   msg[256];
};

template <typename ThrowFn>
CaughtInfo CatchIt(ThrowFn fn)
{
    CaughtInfo info = { false, "", "" };
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        info.caught = true;
#if defined(_MSC_VER)
        // C4996: MSVC deprecates strncpy -> strncpy_s (test_exceptionext.cpp
        // idiom); MinGW keeps the plain strncpy path.
        ::strncpy_s(info.type, sizeof(info.type), e.type_name(), sizeof(info.type) - 1);
        ::strncpy_s(info.msg, sizeof(info.msg), e.message_text(), sizeof(info.msg) - 1);
#else
        std::strncpy(info.type, e.type_name(), sizeof(info.type) - 1);
        std::strncpy(info.msg, e.message_text(), sizeof(info.msg) - 1);
#endif
    }
    return info;
}

bool IsType(const char* thrownChain, const char* managedType)
{
    return il2cpp::il2cpp_exception_matches(managedType, thrownChain);
}

} // namespace

int main()
{
    using il2cpp::runtime::il2cpp_regex_is_match;
    using il2cpp::runtime::il2cpp_regex_match;
    using il2cpp::runtime::il2cpp_regex_replace;
    using il2cpp::runtime::il2cpp_regex_split;

    //--------------------------------------------------------------------------
    // 1. il2cpp_regex_is_match
    //--------------------------------------------------------------------------
    Expect(il2cpp_regex_is_match(S("abc123"), S("\\d"), 0) == 1,
           "is_match: \\d hits inside \"abc123\"");
    Expect(il2cpp_regex_is_match(S("abc"), S("\\d"), 0) == 0,
           "is_match: \\d misses \"abc\"");
    Expect(il2cpp_regex_is_match(S("abc"), S("^a"), 0) == 1,
           "is_match: ^a hits at start position");
    Expect(il2cpp_regex_is_match(S("bac"), S("^a"), 0) == 0,
           "is_match: ^a misses when not at the start");
    Expect(il2cpp_regex_is_match(S("abc"), S("c$"), 0) == 1,
           "is_match: c$ hits at the end");
    Expect(il2cpp_regex_is_match(S("abx"), S("c$"), 0) == 0,
           "is_match: c$ misses when not at the end");
    Expect(il2cpp_regex_is_match(S("A"), S("a"), 0) == 0,
           "is_match: ASCII case-fold is off by default");
    Expect(il2cpp_regex_is_match(S("A"), S("a"), 0x01) == 1,
           "is_match: IgnoreCase (0x01) folds ASCII");

    {
        const CaughtInfo nullPattern = CatchIt(
            [&] { il2cpp_regex_is_match(S("abc"), nullptr, 0); });
        Expect(nullPattern.caught &&
                   IsType(nullPattern.type, "System.ArgumentNullException"),
               "is_match: null pattern throws ArgumentNullException");
        Expect(std::strstr(nullPattern.msg, "pattern") != nullptr,
               "is_match: null-pattern message names the parameter");
    }
    {
        const CaughtInfo nullInput = CatchIt(
            [&] { il2cpp_regex_is_match(nullptr, S("a"), 0); });
        Expect(nullInput.caught &&
                   IsType(nullInput.type, "System.ArgumentNullException"),
               "is_match: null input throws ArgumentNullException");
    }
    {
        const CaughtInfo badPattern = CatchIt(
            [&] { il2cpp_regex_is_match(S("abc"), S("["), 0); });
        Expect(badPattern.caught &&
                   IsType(badPattern.type, "System.ArgumentException"),
               "is_match: invalid pattern throws ArgumentException");
    }
    {
        const CaughtInfo badOptions = CatchIt(
            [&] { il2cpp_regex_is_match(S("abc"), S("a"), 0x08); });
        Expect(badOptions.caught &&
                   IsType(badOptions.type, "System.NotSupportedException"),
               "is_match: Compiled (0x08) option bit throws NotSupportedException");
    }

    //--------------------------------------------------------------------------
    // 2. il2cpp_regex_match
    //--------------------------------------------------------------------------
    {
        int32_t start = -99;
        int32_t length = -99;
        il2cpp::Il2CppArray* m = il2cpp_regex_match(S("abc 12-34 def"),
                                                    S("(\\d+)-(\\d+)"), 0,
                                                    &start, &length);
        Expect(m != nullptr && m->length == 3,
               "match: result array holds whole + 2 groups");
        Expect(start == 4 && length == 5,
               "match: outStart/outLength point at the whole match");
        if (m != nullptr)
        {
            Expect(StrEq(ArrStr(m, 0), "12-34"),
                   "match: [0] is the whole match text");
            Expect(StrEq(ArrStr(m, 1), "12"),
                   "match: [1] is group 1");
            Expect(StrEq(ArrStr(m, 2), "34"),
                   "match: [2] is group 2");
        }
    }
    {
        int32_t start = -99;
        int32_t length = -99;
        il2cpp::Il2CppArray* m = il2cpp_regex_match(S("no digits here"),
                                                    S("(\\d+)"), 0,
                                                    &start, &length);
        Expect(m != nullptr && m->length == 0,
               "match: no match yields an EMPTY array");
        Expect(start == -1 && length == -1,
               "match: no match reports -1/-1 positions");
    }
    {
        // Non-participating group: (a(b)?) against "a" -- group 2 never ran.
        il2cpp::Il2CppArray* m = il2cpp_regex_match(S("a"), S("(a(b)?)"), 0,
                                                    nullptr, nullptr);
        Expect(m != nullptr && m->length == 3,
               "match: optional-group pattern yields 3 slots");
        if (m != nullptr)
        {
            Expect(StrEq(ArrStr(m, 0), "a"),
                   "match: whole match is \"a\"");
            Expect(StrEq(ArrStr(m, 1), "a"),
                   "match: group 1 participated");
            Expect(ArrStr(m, 2) == nullptr,
                   "match: non-participating group is a NULL element");
        }
    }

    //--------------------------------------------------------------------------
    // 3. il2cpp_regex_replace
    //--------------------------------------------------------------------------
    Expect(StrEq(il2cpp_regex_replace(S("a1b2"), S("\\d"), S("X"), 0),
                 "aXbX"),
           "replace: every match replaced (\"a1b2\" with \\d -> \"aXbX\")");
    Expect(StrEq(il2cpp_regex_replace(S("abc"), S("a"), S("X"), 0), "Xbc"),
           "replace: literal single-match replacement");
    Expect(StrEq(il2cpp_regex_replace(S("a1b2"), S("(\\d)"), S("[$1]"), 0),
                 "a[1]b[2]"),
           "replace: $1 expands to group 1");
    Expect(StrEq(il2cpp_regex_replace(S("a1"), S("(\\d)"), S("$0$0"), 0),
                 "a11"),
           "replace: $0 expands to the whole match");
    Expect(StrEq(il2cpp_regex_replace(S("a"), S("a"), S("$$"), 0), "$"),
           "replace: $$ is a literal dollar");
    {
        // Group 10 does not exist: $10 = group 1 + literal '0'.
        const il2cpp::Il2CppString* r = il2cpp_regex_replace(
            S("a1"), S("(\\d)"), S("$10"), 0);
        Expect(StrEq(r, "a10"),
               "replace: $10 without group 10 = group 1 + literal '0'");
    }
    {
        // Group 10 exists: $10 expands to group 10.
        const il2cpp::Il2CppString* r = il2cpp_regex_replace(
            S("abcdefghij"), S("(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)"), S("$10"), 0);
        Expect(StrEq(r, "j"),
               "replace: $10 with group 10 present expands to group 10");
    }
    {
        // Unknown '$x' is kept literally: "a" + "$x" + "c".
        const il2cpp::Il2CppString* r = il2cpp_regex_replace(
            S("abc"), S("b"), S("$x"), 0);
        Expect(r != nullptr && r->length == 4 &&
                   r->chars[0] == u'a' && r->chars[1] == u'$' &&
                   r->chars[2] == u'x' && r->chars[3] == u'c',
               "replace: unknown '$x' sequence is kept literally");
    }
    Expect(StrEq(il2cpp_regex_replace(S("abc"), S("b"), S("$"), 0), "a$c"),
           "replace: trailing '$' is kept literally");
    {
        // Empty pattern: replaces at every position including the end
        // (net48 "XaXbXcX"), must not hang (empty-match advancement).
        const il2cpp::Il2CppString* r = il2cpp_regex_replace(S("abc"), S(""),
                                                             S("X"), 0);
        Expect(StrEq(r, "XaXbXcX"),
               "replace: empty pattern advances per match and does not hang");
    }
    {
        const CaughtInfo nullRep = CatchIt(
            [&] { il2cpp_regex_replace(S("abc"), S("a"), nullptr, 0); });
        Expect(nullRep.caught &&
                   IsType(nullRep.type, "System.ArgumentNullException"),
               "replace: null replacement throws ArgumentNullException");
    }

    //--------------------------------------------------------------------------
    // 5. regex2.cs gap fill (2026-08-08): backreferences and lazy quantifiers
    //    now match net48 instead of throwing ArgumentException.
    //--------------------------------------------------------------------------
    {
        // backref: Regex.IsMatch("aa", "(a)\\1") -> True (net48)
        Expect(il2cpp_regex_is_match(S("aa"), S("(a)\\1"), 0) == 1,
               "is_match: backref (a)\\1 hits 'aa' (regex2 gap filled)");
        // backref replace: Regex.Replace("abab", "(ab)\\1", "X") -> "X" (net48)
        Expect(StrEq(il2cpp_regex_replace(S("abab"), S("(ab)\\1"), S("X"), 0),
                     "X"),
               "replace: backref (ab)\\1 replaced (regex2 gap filled)");
        // lazy plus: Regex.Replace("aaa", "a+?", "-") -> "---" (net48)
        Expect(StrEq(il2cpp_regex_replace(S("aaa"), S("a+?"), S("-"), 0),
                     "---"),
               "replace: lazy a+? replaces per-char (regex2 gap filled)");
        // lazy star: Regex.Replace("aaa", "a*?", "-") -> "-a-a-a-" (net48)
        Expect(StrEq(il2cpp_regex_replace(S("aaa"), S("a*?"), S("-"), 0),
                     "-a-a-a-"),
               "replace: lazy a*? inserts between chars (regex2 gap filled)");
    }

    //--------------------------------------------------------------------------
    // 4. il2cpp_regex_split
    //--------------------------------------------------------------------------
    {
        il2cpp::Il2CppArray* parts = il2cpp_regex_split(S("a,b,c"), S(","), 0);
        Expect(parts != nullptr && parts->length == 3,
               "split: \"a,b,c\" by ',' yields 3 pieces");
        if (parts != nullptr)
        {
            Expect(StrEq(ArrStr(parts, 0), "a"), "split: piece 0 is \"a\"");
            Expect(StrEq(ArrStr(parts, 1), "b"), "split: piece 1 is \"b\"");
            Expect(StrEq(ArrStr(parts, 2), "c"), "split: piece 2 is \"c\"");
        }
    }
    {
        il2cpp::Il2CppArray* parts = il2cpp_regex_split(S("a,,b"), S(","), 0);
        Expect(parts != nullptr && parts->length == 3,
               "split: consecutive separators yield an empty piece");
        if (parts != nullptr)
        {
            Expect(StrEq(ArrStr(parts, 0), "a"), "split: \"a,,b\" piece 0");
            Expect(ArrStr(parts, 1) != nullptr && ArrStr(parts, 1)->length == 0,
                   "split: \"a,,b\" middle piece is empty");
            Expect(StrEq(ArrStr(parts, 2), "b"), "split: \"a,,b\" piece 2");
        }
    }
    {
        il2cpp::Il2CppArray* parts = il2cpp_regex_split(S(",a,"), S(","), 0);
        Expect(parts != nullptr && parts->length == 3,
               "split: leading/trailing separators yield empty ends");
        if (parts != nullptr)
        {
            Expect(ArrStr(parts, 0) != nullptr && ArrStr(parts, 0)->length == 0,
                   "split: leading piece is empty");
            Expect(StrEq(ArrStr(parts, 1), "a"), "split: middle piece is \"a\"");
            Expect(ArrStr(parts, 2) != nullptr && ArrStr(parts, 2)->length == 0,
                   "split: trailing piece is empty");
        }
    }
    {
        il2cpp::Il2CppArray* parts = il2cpp_regex_split(S("abc"), S("x"), 0);
        Expect(parts != nullptr && parts->length == 1,
               "split: no match yields a single-element array");
        if (parts != nullptr)
        {
            Expect(StrEq(ArrStr(parts, 0), "abc"),
                   "split: no-match element is the original text");
        }
    }
    {
        // Empty pattern: net48 split at every position (incl. both ends).
        il2cpp::Il2CppArray* parts = il2cpp_regex_split(S("abc"), S(""), 0);
        Expect(parts != nullptr && parts->length == 5,
               "split: empty pattern yields 5 pieces for \"abc\"");
        if (parts != nullptr)
        {
            Expect(ArrStr(parts, 0) != nullptr && ArrStr(parts, 0)->length == 0,
                   "split: empty-pattern first piece is empty");
            Expect(StrEq(ArrStr(parts, 1), "a"), "split: empty-pattern piece 1");
            Expect(StrEq(ArrStr(parts, 2), "b"), "split: empty-pattern piece 2");
            Expect(StrEq(ArrStr(parts, 3), "c"), "split: empty-pattern piece 3");
            Expect(ArrStr(parts, 4) != nullptr && ArrStr(parts, 4)->length == 0,
                   "split: empty-pattern last piece is empty");
        }
    }
    {
        const CaughtInfo nullInput = CatchIt(
            [&] { il2cpp_regex_split(nullptr, S(","), 0); });
        Expect(nullInput.caught &&
                   IsType(nullInput.type, "System.ArgumentNullException"),
               "split: null input throws ArgumentNullException");
    }

    if (g_failures != 0)
    {
        std::printf("test_regex_api: %d failure(s)\n", g_failures);
        return 1;
    }
    std::printf("test_regex_api: all checks passed\n");
    return 0;
}
