//------------------------------------------------------------------------------
// test_urihash.cpp -- net48 string-culture hash + Uri.GetHashCode unit tests
// (il2cpp_value_semantics.h: il2cpp_culture_hash; versionuri.h:
// icall_uri_get_hash_code).
//
// ORACLE PROVENANCE: every integer below was captured from a real net48
// program (Windows .NET Framework 4.8, this host, 2026-11-01) that compared
// `StringComparer.InvariantCultureIgnoreCase.GetHashCode(s)` /
// `StringComparer.InvariantCulture.GetHashCode(s)` / `uri.GetHashCode()`
// against the implementation shape this runtime uses. The probe swept 83
// strings plus 7 long strings (1..500000 units) and 657 absolute URIs; the
// construction reproduced ALL of them, so these pins are representative
// regressions, not a curve fit.
//
// The construction (see the header for the full rationale):
//   key = LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_SORTKEY|NORM_IGNORECASE,
//                       chars, len, ...)          // cchResult counts the NUL
//   fold the first cchResult-1 BYTES with the two-accumulator (5381, *33)
//   combiner and combine as h1 + h2 * 1566083941.
//
// Platform note: the NLS sort key only exists on Windows. On POSIX hosts the
// runtime falls back to an ASCII fold (documented, NOT net48-exact), so the
// exact-integer sections are compiled only when the sort-key path is
// available; the structural contracts (empty=0, equal-ignore-case equal,
// Ordinal vs ignore-case differ, Uri equals => equal hash) run everywhere.
//------------------------------------------------------------------------------

#include "il2cpp_value_semantics.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "versionuri.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

#if defined(_WIN32)
#include <windows.h> // GetUserDefaultLCID (current-culture pins are locale-specific)
#endif

extern il2cpp::Il2CppClass System_Uri_class;

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

void ExpectEq(int32_t got, int32_t want, const char* what)
{
    if (got != want)
    {
        std::printf("FAIL: %s (got %d, want %d)\n",
                    what, static_cast<int>(got), static_cast<int>(want));
        ++g_failures;
    }
}

il2cpp::Il2CppString* Str(const char* utf8)
{
    return il2cpp::il2cpp_string_new_from_utf8(utf8);
}

int32_t InvIC(const char* utf8)
{
    return il2cpp::runtime::il2cpp_culture_hash(Str(utf8), "", true);
}

int32_t InvCS(const char* utf8)
{
    return il2cpp::runtime::il2cpp_culture_hash(Str(utf8), "", false);
}

// Build a Uri through the ctor icall (the same path generated code uses).
il2cpp::Il2CppObject* NewUri(const char* text)
{
    il2cpp::Il2CppObject* obj = il2cpp::il2cpp_object_new(&System_Uri_class);
    il2cpp::runtime::icall_uri_ctor(
        obj, reinterpret_cast<il2cpp::Il2CppObject*>(Str(text)));
    return obj;
}

int32_t UriHash(const char* text)
{
    il2cpp::Il2CppObject* u = NewUri(text);
    return il2cpp::runtime::icall_uri_get_hash_code(u);
}

// ---------------- culture hash: net48 oracle integers ----------------
struct HashSample
{
    const char* label;
    const char* text;
    int32_t expected;
};

void TestInvariantIgnoreCaseOracle()
{
    static const HashSample kIgnoreCase[] = {
        { "empty", "", 0 },
        { "a", "a", -356107186 },
        { "A", "A", -356107186 },
        { "abc", "abc", -934082881 },
        { "ABC (same as abc)", "ABC", -934082881 },
        { "http://example.com/a", "http://example.com/a", -296100340 },
        { "http://example.com/A", "http://example.com/A", -296100340 },
        { "CJK", "\u4e2d\u6587", 290948172 },
        { "Stra\u00dfe", "Stra\u00dfe", -1345604545 },
        { "strasse (== Stra\u00dfe under IC)", "strasse", -1345604545 },
        { "caf\u00e9", "caf\u00e9", 1847896276 },
        { "http://x/caf\u00e9", "http://x/caf\u00e9", 883152344 },
        { "a-b", "a-b", -6036542 },
        { "a b", "a b", 394700062 },
    };
    for (const auto& s : kIgnoreCase)
    {
        ExpectEq(InvIC(s.text), s.expected, s.label);
    }

    // Raw UTF-16 inputs that cannot ride the UTF-8 helper: a lone surrogate
    // and an embedded NUL. net48 hashes the raw code units (no replacement).
    {
        const char16_t lone[] = { 0xD800 };
        ExpectEq(il2cpp::runtime::il2cpp_culture_hash(
                     il2cpp::il2cpp_string_new(lone, 1), "", true),
                 1176525194, "lone high surrogate (raw UTF-16)");
        const char16_t withNul[] = { u'a', 0x0000, u'b' };
        ExpectEq(il2cpp::runtime::il2cpp_culture_hash(
                     il2cpp::il2cpp_string_new(withNul, 3), "", true),
                 -1820530101, "embedded NUL (raw UTF-16)");
    }
}

void TestInvariantCaseSensitiveOracle()
{
    // CultureInfo.InvariantCulture.CompareInfo.GetHashCode(s, CompareOptions.None)
    static const HashSample kCaseSensitive[] = {
        { "empty", "", 0 },
        { "abc", "abc", -934082881 },
        { "ABC", "ABC", -1009883527 },
        { "http://example.com/a", "http://example.com/a", -296100340 },
        { "CJK", "\u4e2d\u6587", 290948172 },
        { "Stra\u00dfe", "Stra\u00dfe", 676167501 },
    };
    for (const auto& s : kCaseSensitive)
    {
        ExpectEq(InvCS(s.text), s.expected, s.label);
    }
}

// ---------------- culture hash: current-culture (user default) integers ----
// net48 StringComparer.CurrentCulture[IgnoreCase] on this host (zh-CN, LCID
// 2052). The user-default locale name (nullptr) must reproduce them.
void TestCurrentCultureOracle()
{
    struct CurrentSample
    {
        const char* label;
        const char* text;
        bool ignoreCase;
        int32_t expected;
    };
    static const CurrentSample kCurrent[] = {
        { "current IC abc", "abc", true, -934082881 },
        { "current IC ABC", "ABC", true, -934082881 },
        { "current CS abc", "abc", false, -934082881 },
        { "current CS ABC", "ABC", false, -1009883527 },
        { "current IC Stra\u00dfe", "Stra\u00dfe", true, -1345604545 },
        { "current CS Stra\u00dfe", "Stra\u00dfe", false, 676167501 },
    };
    // These integers are the zh-CN (LCID 0x0804) ones; net48's CurrentCulture
    // follows the OS user locale, so a host on another locale legitimately
    // produces other numbers. Skip rather than assert a host-dependent value.
    if (::GetUserDefaultLCID() != 0x0804u)
    {
        std::printf("skip: current-culture pins are zh-CN (LCID 0x0804) only\n");
        return;
    }
    for (const auto& s : kCurrent)
    {
        ExpectEq(il2cpp::runtime::il2cpp_culture_hash(Str(s.text), nullptr, s.ignoreCase),
                 s.expected, s.label);
    }
}

void TestCultureHashContracts()
{    // net48: String.Empty hashes to 0 for EVERY comparer.
    ExpectEq(InvIC(""), 0, "empty ignore-case hash is 0");
    ExpectEq(InvCS(""), 0, "empty case-sensitive hash is 0");

    // Ignore-case collapses case; case-sensitive does not.
    Expect(InvIC("abc") == InvIC("ABC"), "ignore-case: abc == ABC");
    Expect(InvCS("abc") != InvCS("ABC"), "case-sensitive: abc != ABC");

    // The culture hash is NOT the ordinal hash (the old degradation bug).
    Expect(InvIC("abc") != il2cpp::runtime::icall_string_get_hash_code(
                                reinterpret_cast<il2cpp::Il2CppObject*>(Str("abc"))),
           "invariant ignore-case hash differs from ordinal string.GetHashCode");

    // Null -> ArgumentNullException (net48 StringComparer contract).
    bool threw = false;
    try
    {
        il2cpp::runtime::il2cpp_culture_hash(nullptr, "", true);
    }
    catch (const il2cpp::DotNetException& e)
    {
        threw = std::strstr(e.type_name(), "System.ArgumentNullException") != nullptr;
    }
    Expect(threw, "null string -> ArgumentNullException");

    // Long strings stay stable and case-collapsing (no length cliff).
    std::vector<char16_t> big(40000, u'a');
    il2cpp::Il2CppString* lower = il2cpp::il2cpp_string_new(big.data(), 40000);
    std::vector<char16_t> bigUpper(40000, u'A');
    il2cpp::Il2CppString* upper = il2cpp::il2cpp_string_new(bigUpper.data(), 40000);
    Expect(il2cpp::runtime::il2cpp_culture_hash(lower, "", true) ==
               il2cpp::runtime::il2cpp_culture_hash(upper, "", true),
           "40000-unit strings collapse case identically");
}

// ---------------- string.GetHashCode(StringComparison) routing ----------------
void TestGetHashCodeModeRouting()
{
    il2cpp::Il2CppObject* s = reinterpret_cast<il2cpp::Il2CppObject*>(Str("ABC"));

    // 0 Ordinal keeps the legacy ordinal hash.
    ExpectEq(il2cpp::runtime::icall_string_get_hash_code_mode(s, 0),
             il2cpp::runtime::icall_string_get_hash_code(s),
             "StringComparison.Ordinal == string.GetHashCode()");
    // 5 InvariantCultureIgnoreCase routes to the culture hash (net48 number).
    ExpectEq(il2cpp::runtime::icall_string_get_hash_code_mode(s, 5), -934082881,
             "InvariantCultureIgnoreCase uses the NLS sort-key hash");
    // 4 InvariantCulture is case-sensitive.
    ExpectEq(il2cpp::runtime::icall_string_get_hash_code_mode(s, 4), -1009883527,
             "InvariantCulture uses the case-sensitive sort-key hash");
    // Ignore-case equality through the StringComparison overload.
    il2cpp::Il2CppObject* lower = reinterpret_cast<il2cpp::Il2CppObject*>(Str("abc"));
    ExpectEq(il2cpp::runtime::icall_string_get_hash_code_mode(lower, 5),
             il2cpp::runtime::icall_string_get_hash_code_mode(s, 5),
             "mode 5: abc and ABC hash equally");
}

// ---------------- Uri.GetHashCode: net48 oracle integers ----------------
void TestUriHashOracle()
{
    static const HashSample kUris[] = {
        { "http://example.com/a", "http://example.com/a", -296100340 },
        { "HTTP://Example.COM/A", "HTTP://Example.COM/A", -296100340 },
        { "http://example.com:80/a", "http://example.com:80/a", -296100340 },
        { "dot segments", "http://example.com/a/../b/./c", 1193486537 },
        { "%20 unescapes", "http://example.com/a%20b", -459752964 },
        { "fragment stripped", "https://x.y/z?q=1#f", -1675271880 },
        { "CJK path (escaped)", "http://x/%E4%B8%AD%E6%96%87", 1432133052 },
        { "sharp-s path (escaped)", "http://x/Stra%C3%9Fe", 1782931135 },
        { "cafe path (escaped)", "http://x/caf%C3%A9", 883152344 },
        { "reserved escape kept", "http://x/a%2Fb", -320649405 },
        { "query escape kept", "http://x/a%3Fb", -797962099 },
        { "double escape", "http://x/a%2520b", -684336018 },
        { "%7E decodes", "http://x/%7E", -1505598341 },
        { "query %41 decodes", "http://x/?a=%41", 1179605301 },
        { "bare fragment", "http://x/#frag", 677392330 },
        { "query then fragment", "http://x/?q=1#frag", 84222093 },
        { "empty path becomes /", "http://x", 677392330 },
        { "%2E folds in the hash", "http://x/%2E%2E/a", 1799468602 },
        { "userinfo dropped", "http://user:pw@x/a", 1799468602 },
        { "non-default port kept", "http://x:8080/a", 495889088 },
        { "default https port dropped", "https://x:443/a", -1573061749 },
        { "relative path double slash", "http://h//p", 1263460060 },
        { "mailto drops userinfo", "mailto:a@b", 1140828415 },
        { "opaque urn verbatim", "urn:isbn:123", 770256 },
        { "opaque custom keeps @", "custom:x@y", 1772376470 },
    };
    for (const auto& s : kUris)
    {
        ExpectEq(UriHash(s.text), s.expected, s.label);
    }
}

void TestUriHashContracts()
{
    // Equals => equal hash (the .NET contract; the only hard requirement).
    struct Pair { const char* a; const char* b; };
    static const Pair kEqual[] = {
        { "HTTP://Example.COM/A", "http://example.com/a" },
        { "http://example.com:80/a", "http://example.com/a" },
        { "http://example.com/a/../b/./c", "http://example.com/b/c" },
        { "http://example.com/a%20b", "http://example.com/a%20b" },
        { "https://x.y/z?q=1#f", "https://x.y/z?q=1" },
        { "http://x/Stra%C3%9Fe", "http://x/strasse" },
        { "http://x/%E4%B8%AD%E6%96%87", "http://x/%e4%b8%ad%e6%96%87" },
    };
    for (const auto& p : kEqual)
    {
        il2cpp::Il2CppObject* a = NewUri(p.a);
        il2cpp::Il2CppObject* b = NewUri(p.b);
        ExpectEq(il2cpp::runtime::icall_uri_get_hash_code(a),
                 il2cpp::runtime::icall_uri_get_hash_code(b), p.a);
    }

    // Repeated calls are stable.
    il2cpp::Il2CppObject* u = NewUri("http://example.com/a");
    ExpectEq(il2cpp::runtime::icall_uri_get_hash_code(u),
             il2cpp::runtime::icall_uri_get_hash_code(u),
             "repeated Uri.GetHashCode is stable");

    // Relative URIs hash their OriginalString the SAME way.
    il2cpp::Il2CppObject* rel = il2cpp::il2cpp_object_new(&System_Uri_class);
    il2cpp::runtime::icall_uri_ctor_2(
        rel, reinterpret_cast<il2cpp::Il2CppObject*>(Str("a/b")), 2);
    ExpectEq(il2cpp::runtime::icall_uri_get_hash_code(rel), -1847586231,
             "relative Uri hashes OriginalString");
}

} // namespace

int main()
{
    TestCultureHashContracts();
    TestGetHashCodeModeRouting();
    TestUriHashContracts();
#if defined(_WIN32)
    // The exact integers require the Windows NLS sort-key path.
    TestInvariantIgnoreCaseOracle();
    TestInvariantCaseSensitiveOracle();
    TestCurrentCultureOracle();
    TestUriHashOracle();
#endif

    if (g_failures != 0)
    {
        std::printf("%d urihash assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime urihash tests passed\n");
    return 0;
}
