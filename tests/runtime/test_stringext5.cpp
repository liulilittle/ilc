//------------------------------------------------------------------------------
// test_stringext5.cpp -- System.String.Replace(string, string) runtime unit
// tests (icall_string_replace_s_s, il2cpp_string_ext.h).
//
// Coverage (normal + edge + error + GC paths). Every expectation below was
// empirically verified against the .NET Framework 4.8 reference runtime (the
// replprobe csc v4.0.30319 run, 2026-08-09) -- the assertions ARE the
// oracle:
//   1.  Normal: single match, ALL occurrences ("aXbXc" -> "a1b1c").
//   2.  No match -> the SAME instance (net48 ReferenceEquals identity) with
//       unchanged content; oldValue longer than the receiver -> same
//       instance.
//   3.  oldValue == newValue WITH occurrences -> a NEW instance (net48 has
//       no identity fast path), content equal to the receiver.
//   4.  null 'newValue' AND empty 'newValue' -> occurrences REMOVED.
//   5.  Empty 'oldValue' -> ArgumentException; null 'oldValue' ->
//       ArgumentNullException -- both even when the receiver is empty.
//   6.  Overlapping matches are NOT re-matched: greedy left-to-right scan,
//       "aaa".Replace("aa","b") == "ba", "aaaa" -> "bb".
//   7.  Replacement growth ("ab" -> "axyz") and shrinkage ("aXb" -> "ab").
//   8.  Empty receiver + empty oldValue still throws ArgumentException;
//       empty receiver + valid oldValue -> same instance ("" identity).
//   9.  Non-ASCII payload (U+00E9) survives bytewise.
//   10. GC sanity: the result string keeps its content across a collection
//       cycle (the fill roots the result; the result pointer is re-derived
//       from the rooted slot).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext.h"

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

// Exception-chain prefix check (same shape as test_stringext4.cpp).
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
    using il2cpp::runtime::icall_string_replace_s_s;

    // --- 1. normal: single match and ALL occurrences -------------------------
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("aXb"), Cs("X"), Cs("1"));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "a1b"),
               "Replace(\"aXb\", \"X\", \"1\") == \"a1b\"");
    }
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("aXbXc"), Cs("X"), Cs("1"));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "a1b1c"),
               "Replace(\"aXbXc\", \"X\", \"1\") replaces ALL occurrences");
    }

    // --- 2. no match -> SAME instance, unchanged ------------------------------
    {
        Il2CppString* s = Cs("abc");
        Il2CppString* r = icall_string_replace_s_s(s, Cs("zz"), Cs("1"));
        Expect(r == s, "Replace no-match returns the SAME instance (net48 identity)");
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "abc"),
               "Replace no-match keeps the content");
    }
    {
        // oldValue longer than the receiver -> no match possible.
        Il2CppString* s = Cs("abc");
        Il2CppString* r = icall_string_replace_s_s(s, Cs("abcd"), Cs("1"));
        Expect(r == s, "Replace with oldValue longer than the receiver is identity");
    }

    // --- 3. oldValue == newValue WITH occurrences -> NEW instance -------------
    {
        Il2CppString* s = Cs("abc");
        Il2CppString* r = icall_string_replace_s_s(s, Cs("a"), Cs("a"));
        Expect(r != s, "Replace(old==new) with occurrences returns a NEW instance");
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "abc"),
               "Replace(old==new) content stays equal");
    }

    // --- 4. null newValue AND empty newValue REMOVE the occurrences ----------
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("aXbXc"), Cs("X"), nullptr);
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "abc"),
               "Replace with null newValue removes ALL occurrences");
    }
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("aXbXc"), Cs("X"), Cs(""));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "abc"),
               "Replace with empty newValue removes ALL occurrences");
    }

    // --- 5. error paths: empty oldValue -> ArgumentException; null oldValue ->
    //         ArgumentNullException --------------------------------------------
    Expect(ThrowsWithType("System.ArgumentException",
                          []() { (void)icall_string_replace_s_s(Cs("abc"), Cs(""), Cs("1")); }),
           "Replace with empty oldValue throws ArgumentException");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { (void)icall_string_replace_s_s(Cs("abc"), nullptr, Cs("1")); }),
           "Replace with null oldValue throws ArgumentNullException");

    // --- 6. overlapping matches are NOT re-matched (greedy scan) --------------
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("aaa"), Cs("aa"), Cs("b"));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "ba"),
               "Replace(\"aaa\", \"aa\", \"b\") == \"ba\" (non-overlapping)");
    }
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("aaaa"), Cs("aa"), Cs("b"));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "bb"),
               "Replace(\"aaaa\", \"aa\", \"b\") == \"bb\" (non-overlapping)");
    }

    // --- 7. growth and shrinkage ----------------------------------------------
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("ab"), Cs("b"), Cs("xyz"));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "axyz"),
               "Replace(\"ab\", \"b\", \"xyz\") grows the result");
    }
    {
        Il2CppString* r = icall_string_replace_s_s(Cs("aXb"), Cs("X"), Cs(""));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "ab"),
               "Replace(\"aXb\", \"X\", \"\") shrinks the result");
    }

    // --- 8. empty receiver ------------------------------------------------------
    {
        Expect(ThrowsWithType("System.ArgumentException",
                              []() { (void)icall_string_replace_s_s(Cs(""), Cs(""), Cs("1")); }),
               "Replace on the empty receiver with empty oldValue throws ArgumentException");
    }
    {
        Il2CppString* s = Cs("");
        Il2CppString* r = icall_string_replace_s_s(s, Cs("a"), Cs("b"));
        Expect(r == s, "Replace on the empty receiver (oldValue absent) is identity");
    }
    {
        Il2CppString* r = icall_string_replace_s_s(Cs(""), Cs("a"), nullptr);
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), ""),
               "Replace on the empty receiver with null newValue content is empty");
    }

    // --- 9. non-ASCII payload survives bytewise (U+00E9) ----------------------
    {
        // Build "caf\u00E9" from explicit UTF-16 units (new_from_cs skips
        // bytes >= 0x80, so the accented literal cannot cross through it).
        const char16_t cafeUnits[] = { u'c', u'a', u'f', u'\u00E9' };
        Il2CppString* cafe = il2cpp::il2cpp_string_new(cafeUnits, 4);
        const char16_t eAcute = u'\u00E9';
        Il2CppString* oldUnit = il2cpp::il2cpp_string_new(&eAcute, 1);
        Il2CppString* r = icall_string_replace_s_s(cafe, oldUnit, Cs("e"));
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(r), "cafe"),
               "Replace replaces U+00E9 with 'e' bytewise");
    }

    // --- 10. GC sanity: the result survives a collection -----------------------
    {
        Il2CppString* rooted = icall_string_replace_s_s(Cs("aXbXc"), Cs("X"), Cs("12"));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rooted));
        il2cpp::il2cpp_gc_collect();
        Expect(StrEquals(reinterpret_cast<Il2CppObject*>(rooted), "a12b12c"),
               "Replace result survives a collection (rooted)");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&rooted));
    }

    // --- Report ---------------------------------------------------------------
    if (g_failures != 0)
    {
        std::printf("%d stringext5 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringext5 tests passed\n");
    return 0;
}
