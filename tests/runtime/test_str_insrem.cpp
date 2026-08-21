//------------------------------------------------------------------------------
// test_str_insrem.cpp -- System.String.Insert / String.Remove runtime unit
// tests (icall_string_insert / icall_string_remove, il2cpp_string_ext.h).
//
// Coverage (normal + edge + error paths), pinned to the net48 contract the
// icalls implement (pure UTF-16 unit surgery; both return a NEW string and
// never modify the source):
//   1.  Insert at 0 / middle / end (index == Length); single-unit value at
//       every boundary of a 2-unit receiver.
//   2.  Insert into the EMPTY receiver; EMPTY value insert (content-equal,
//       still a NEW instance per the header contract).
//   3.  Remove head / middle / tail / FULL (count == Length); count == 0
//       (content-equal copy, NEW instance); empty receiver Remove(0, 0).
//   4.  Source-unmodified + new-instance identity checks for both icalls.
//   5.  Error paths -> System.ArgumentOutOfRangeException: Insert index < 0 /
//       index > Length (also on the empty receiver); Remove startIndex < 0 /
//       startIndex > Length; count < 0; count overflowing the remaining
//       span (startIndex + count > Length).
//   6.  Null receiver / null value -> System.ArgumentNullException.
//   7.  GC sanity: results survive a collection cycle when rooted.
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
bool StrEquals(const Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
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

// Exception-type check (same shape as test_stringext6.cpp): 'fn' must throw
// a DotNetException whose type name starts with 'expectedType'.
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
    using il2cpp::runtime::icall_string_insert;
    using il2cpp::runtime::icall_string_remove;

    // --- 1. Insert: 0 / middle / end ----------------------------------------
    {
        Il2CppString* r = icall_string_insert(Cs("World"), 0, Cs("Hello "));
        Expect(StrEquals(r, "Hello World"), "Insert(\"World\", 0, \"Hello \") == \"Hello World\" (at 0)");
    }
    {
        Il2CppString* r = icall_string_insert(Cs("abcd"), 2, Cs("--"));
        Expect(StrEquals(r, "ab--cd"), "Insert(\"abcd\", 2, \"--\") == \"ab--cd\" (middle)");
    }
    {
        Il2CppString* r = icall_string_insert(Cs("abc"), 3, Cs("def"));
        Expect(StrEquals(r, "abcdef"), "Insert(\"abc\", 3, \"def\") == \"abcdef\" (index == Length)");
    }
    {
        // every boundary of a 2-unit receiver
        Il2CppString* r0 = icall_string_insert(Cs("ab"), 0, Cs("x"));
        Il2CppString* r1 = icall_string_insert(Cs("ab"), 1, Cs("x"));
        Il2CppString* r2 = icall_string_insert(Cs("ab"), 2, Cs("x"));
        Expect(StrEquals(r0, "xab") && StrEquals(r1, "axb") && StrEquals(r2, "abx"),
               "Insert(\"ab\", {0,1,2}, \"x\") == {xab|axb|abx}");
    }

    // --- 2. Insert: empty receiver / empty value ----------------------------
    {
        Il2CppString* r = icall_string_insert(Cs(""), 0, Cs("xy"));
        Expect(StrEquals(r, "xy"), "Insert(\"\", 0, \"xy\") == \"xy\"");
    }
    {
        Il2CppString* self = Cs("abc");
        Il2CppString* r = icall_string_insert(self, 1, Cs(""));
        Expect(StrEquals(r, "abc"), "Insert(\"abc\", 1, \"\") == \"abc\" (empty value)");
        Expect(r != self, "Insert with empty value returns a NEW instance");
        Expect(StrEquals(self, "abc"), "source unmodified after empty-value insert");
    }
    {
        Il2CppString* r = icall_string_insert(Cs(""), 0, Cs(""));
        Expect(StrEquals(r, ""), "Insert(\"\", 0, \"\") == \"\"");
    }

    // --- 3. Insert: new instance + source unmodified ------------------------
    {
        Il2CppString* self = Cs("abc");
        Il2CppString* r = icall_string_insert(self, 0, Cs("z"));
        Expect(r != self, "Insert returns a NEW instance (never the receiver)");
        Expect(StrEquals(self, "abc"), "source unmodified after insert");
    }

    // --- 4. Remove: head / middle / tail / full ------------------------------
    {
        Il2CppString* r = icall_string_remove(Cs("abc"), 0, 1);
        Expect(StrEquals(r, "bc"), "Remove(\"abc\", 0, 1) == \"bc\" (head)");
    }
    {
        Il2CppString* r = icall_string_remove(Cs("abcd"), 1, 2);
        Expect(StrEquals(r, "ad"), "Remove(\"abcd\", 1, 2) == \"ad\" (middle)");
    }
    {
        Il2CppString* r = icall_string_remove(Cs("abc"), 2, 1);
        Expect(StrEquals(r, "ab"), "Remove(\"abc\", 2, 1) == \"ab\" (tail)");
    }
    {
        Il2CppString* r = icall_string_remove(Cs("abc"), 0, 3);
        Expect(StrEquals(r, ""), "Remove(\"abc\", 0, 3) == \"\" (full)");
    }
    {
        Il2CppString* r = icall_string_remove(Cs("abcd"), 2, 2);
        Expect(StrEquals(r, "ab"), "Remove(\"abcd\", 2, 2) == \"ab\" (tail-to-end)");
    }

    // --- 5. Remove: count == 0 / empty receiver ------------------------------
    {
        Il2CppString* self = Cs("abc");
        Il2CppString* r = icall_string_remove(self, 1, 0);
        Expect(StrEquals(r, "abc"), "Remove(\"abc\", 1, 0) == \"abc\" (count 0)");
        Expect(r != self, "Remove with count 0 returns a NEW instance");
        Expect(StrEquals(self, "abc"), "source unmodified after count-0 remove");
    }
    {
        Il2CppString* r = icall_string_remove(Cs(""), 0, 0);
        Expect(StrEquals(r, ""), "Remove(\"\", 0, 0) == \"\"");
    }
    {
        Il2CppString* self = Cs("abc");
        Il2CppString* r = icall_string_remove(self, 0, 2);
        Expect(r != self, "Remove returns a NEW instance (never the receiver)");
        Expect(StrEquals(self, "abc"), "source unmodified after remove");
    }

    // --- 6. Insert error paths -> ArgumentOutOfRangeException -----------------
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_insert(Cs("abc"), -1, Cs("x")); }),
            "Insert index -1 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_insert(Cs("abc"), 4, Cs("x")); }),
            "Insert index Length+1 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_insert(Cs(""), 1, Cs("x")); }),
            "Insert index 1 on the empty receiver throws ArgumentOutOfRangeException");

    // --- 7. Remove error paths -> ArgumentOutOfRangeException ------------------
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_remove(Cs("abc"), -1, 0); }),
            "Remove startIndex -1 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_remove(Cs("abc"), 4, 0); }),
            "Remove startIndex Length+1 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_remove(Cs("abc"), 0, -1); }),
            "Remove count -1 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_remove(Cs("abc"), 1, 3); }),
            "Remove count overflowing the remaining span throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType("System.ArgumentOutOfRangeException",
                          []() { (void)icall_string_remove(Cs("abc"), 0, 4); }),
            "Remove count Length+1 throws ArgumentOutOfRangeException");

    // --- 8. Null receiver / null value -> ArgumentNullException ---------------
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { (void)icall_string_insert(nullptr, 0, Cs("x")); }),
            "Insert on a null receiver throws ArgumentNullException");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { (void)icall_string_insert(Cs("abc"), 0, nullptr); }),
            "Insert with a null value throws ArgumentNullException");
    Expect(ThrowsWithType("System.ArgumentNullException",
                          []() { (void)icall_string_remove(nullptr, 0, 0); }),
            "Remove on a null receiver throws ArgumentNullException");

    // --- 9. GC sanity: rooted results survive a collection --------------------
    {
        Il2CppString* ins = icall_string_insert(Cs("World"), 0, Cs("Hello "));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&ins));
        Il2CppString* rem = icall_string_remove(Cs("abc"), 1, 1);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rem));
        il2cpp::il2cpp_gc_collect();
        Expect(StrEquals(ins, "Hello World"), "insert result survives a collection");
        Expect(StrEquals(rem, "ac"), "remove result survives a collection");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&ins));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&rem));
    }

    // --- Report ---------------------------------------------------------------
    if (g_failures != 0)
    {
        std::printf("%d str_insrem assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime str_insrem tests passed\n");
    return 0;
}
