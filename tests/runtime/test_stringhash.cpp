//------------------------------------------------------------------------------
// test_stringhash.cpp -- string.GetHashCode net48 algorithm unit tests
// (il2cpp_value_semantics.h: icall_string_get_hash_code).
//
// The net48 algorithm (COMString::GetHashCode, fixed-seed legacy form -- the
// default for plain console apps; probe-verified against csc.exe
// C:\Windows\Microsoft.NET\Framework64\v4.0.30319 on 2026-08-04):
//
//   hash1 = hash2 = 5381;
//   for (i = 0; i < len; i += 2) {
//       hash1 = ((hash1 << 5) + hash1) ^ chars[i];
//       if (i + 1 < len) hash2 = ((hash2 << 5) + hash2) ^ chars[i + 1];
//   }
//   return hash1 + hash2 * 1566083941;   (32-bit wraps)
//
// The oracle values below were captured from a net48 probe program (Probe.cs
// in %TEMP%\opencode\strhash_probe) and are CROSS-PROCESS STABLE: three
// consecutive runs of the same probe exe produced identical output, so the
// values are pinned here as constants. (The runtime deliberately keeps this
// hash unrandomized: AOT output must be deterministic and this hash is not a
// security boundary.)
//
// Coverage (normal + edge + boundary):
//   1. empty string oracle value.
//   2. single unit; even/odd unit counts (a/ab/abc/abcd/abcde) -- exercises
//      both accumulators and the odd-tail branch of the loop.
//   3. multi-word ASCII (hello / Hello, World!).
//   4. non-BMP-plane-free CJK (ni/ni hao/ni hao ni) and a mixed ASCII+CJK
//      string (helloni hao) -- UTF-16 unit semantics, no surrogate pairs
//      involved.
//   5. long string (1000 x 'x') -- wrap accumulation over many units.
//   6. non-ASCII Latin-1 (5 x U+00E9) -- high-byte units.
//   7. error path: null string -> NullReferenceException.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_value_semantics.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

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

// Plain managed string from an ASCII literal (1:1 transcode).
il2cpp::Il2CppString* Str(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_utf8(ascii);
}

int32_t HashOf(il2cpp::Il2CppString* s)
{
    return il2cpp::runtime::icall_string_get_hash_code(
        reinterpret_cast<il2cpp::Il2CppObject*>(s));
}

// The NRE factory chain (see il2cpp_exception_ext.h): type_name() of a
// caught DotNetException is the whole chain as thrown.
const char* kNreChain =
    "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

void ExpectThrowsNre(il2cpp::Il2CppString* s)
{
    try
    {
        HashOf(s);
        Expect(false, "null string GetHashCode must throw NRE");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(std::strcmp(e.type_name(), kNreChain) == 0,
               "null string GetHashCode throws the NRE chain");
    }
}

// net48 oracle values (probe-verified, cross-process stable -- see header).
void TestNet48OracleValues()
{
    struct Sample
    {
        const char* label;
        il2cpp::Il2CppString* str;
        int32_t expected;
    };

    std::vector<char16_t> buf1000(1000, u'x');
    std::vector<char16_t> buf5(5, u'\u00e9');
    std::vector<Sample> samples;
    samples.push_back({ "empty", Str(""), 371857150 });
    samples.push_back({ "a", Str("a"), 372029373 });
    samples.push_back({ "ab", Str("ab"), 1093630535 });
    samples.push_back({ "abc", Str("abc"), 1099313834 });
    samples.push_back({ "abcd", Str("abcd"), -1664220650 });
    samples.push_back({ "abcde", Str("abcde"), -1476671663 });
    samples.push_back({ "hello", Str("hello"), -327419862 });
    samples.push_back({ "Hello, World!", Str("Hello, World!"), 562640209 });
    samples.push_back({ "CJK hello", il2cpp::il2cpp_string_new(u"\u4f60\u597d", 2),
                        2047063037 });
    samples.push_back({ "mixed helloCJK", il2cpp::il2cpp_string_new(u"hello\u4f60\u597d", 7),
                        1724669551 });
    samples.push_back({ "CJK single", il2cpp::il2cpp_string_new(u"\u4f60", 1),
                        372047038 });
    samples.push_back({ "CJK triple", il2cpp::il2cpp_string_new(u"\u4f60\u597d\u4f60", 3),
                        2053297469 });
    samples.push_back({ "1000 x 'x'", il2cpp::il2cpp_string_new(buf1000.data(), 1000),
                        1082926846 });
    samples.push_back({ "5 x U+00E9", il2cpp::il2cpp_string_new(buf5.data(), 5),
                        -2103616059 });

    for (const auto& s : samples)
    {
        Expect(HashOf(s.str) == s.expected, s.label);
    }
}

void TestConsistencyAndEdges()
{
    // Distinct instances with equal content -> equal hash (the .NET contract
    // that hashtable/dictsi lookups depend on).
    il2cpp::Il2CppString* a1 = Str("hello");
    il2cpp::Il2CppString* a2 = Str("hello");
    Expect(HashOf(a1) == HashOf(a2),
           "distinct instances with equal content hash equal");

    // Same instance, repeated calls -> stable.
    Expect(HashOf(a1) == HashOf(a1),
           "repeated calls on the same instance are stable");

    // Boundary: the empty string is a distinct value (not the 5381 seed).
    Expect(HashOf(Str("")) != HashOf(Str("a")),
           "empty string hash differs from a single-unit hash");

    // Error path: null -> NullReferenceException.
    ExpectThrowsNre(nullptr);
}

} // namespace

int main()
{
    TestNet48OracleValues();
    TestConsistencyAndEdges();

    if (g_failures != 0)
    {
        std::printf("%d stringhash assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringhash tests passed\n");
    return 0;
}
