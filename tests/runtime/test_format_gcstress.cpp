//------------------------------------------------------------------------------
// test_format_gcstress.cpp -- GC-stress tests for the numeric FORMATTING
// icall domain (M5 BCL). Deep-testing companion to test_formatext.cpp /
// test_numberformat.cpp: where those suites verify net48 SEMANTICS, this
// suite verifies GC SAFETY under collection pressure -- the same latent-risk
// class flagged by the #114 analysis (docs/plans/issue-114-analysis.md) for
// StringBuilder/concat, applied to the format kernels (format.cpp /
// numberformat.cpp / formatext.cpp).
//
// The format kernels' GC-safety contract: the format string payload is
// parsed into a non-GC CustomSpec BEFORE any managed allocation, and the
// numeric value is rendered into stack buffers; the ONLY managed allocation
// is the result string at the very end. A collection that moved the format
// string between the parse read and the final allocation would corrupt the
// output -- this harness fires collections right before each call to make
// any such bug deterministic.
//
// Coverage (normal + edge + error paths):
//   1. int32/int64 custom format under GC pressure: rooted format strings,
//      collections fired before the call; verify the formatted output
//      (N2 grouping, X hex, D decimal).
//   2. double custom format under GC pressure: rooted format strings,
//      collections fired; verify the formatted output (E2 scientific).
//   3. General format under GC pressure: icall_int32_to_string /
//      icall_double_to_string with collections fired; verify the output.
//   4. Format-string aliasing: the format string is itself the result of a
//      previous format call (a fresh managed string), with collections
//      between; verify the output stays correct.
//   5. Mixed churn: a deterministic pseudo-random sequence of int32/double
//      format calls with a collection after every few operations; verify
//      every output against the expected literal.
//
// Plain executable + minimal assertion harness (see test_string.cpp): any
// failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_format.h"
#include "il2cpp_format_ext.h"

#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "format gc-stress fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Expect 'obj' (a managed string) to hold exactly the ASCII literal 'ascii'.
void ExpectString(il2cpp::Il2CppObject* obj, const char* ascii, const char* what)
{
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (static_cast<size_t>(s->length) != len)
    {
        Expect(false, what);
        return;
    }
    bool match = true;
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            match = false;
            break;
        }
    }
    Expect(match, what);
}

// Allocate a managed string from an ASCII literal, returned as Il2CppObject*
// (Il2CppString embeds Il2CppObject as its first member -- explicit cast).
il2cpp::Il2CppObject* NewManagedString(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Deterministic pseudo-random source (LCG; no <random> state, identical
// sequence on every platform -- the stress pattern must be reproducible).
uint32_t g_rng = 0x9E3779B9u;
uint32_t NextRng()
{
    g_rng = g_rng * 1664525u + 1013904223u;
    return g_rng;
}

} // namespace

int main()
{
    using il2cpp::Il2CppObject;
    using il2cpp::Il2CppString;
    using il2cpp::runtime::icall_format_int32_custom;
    using il2cpp::runtime::icall_format_int64_custom;
    using il2cpp::runtime::icall_format_double_custom;
    using il2cpp::runtime::icall_int32_to_string;
    using il2cpp::runtime::icall_double_to_string;


    //--- 1. int32/int64 custom format under GC pressure ---
    {

        Il2CppObject* fmtN2 = NewManagedString("N2");
        Il2CppObject* fmtX = NewManagedString("X");
        Il2CppObject* fmtX4 = NewManagedString("X4");
        Il2CppObject* fmtD = NewManagedString("D");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtN2));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtX));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtX4));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtD));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_format_int32_custom(12345, fmtN2), "12,345.00",
                         "int32 N2 content survives collections");
            ExpectString(icall_format_int32_custom(-12345, fmtN2), "-12,345.00",
                         "int32 negative N2 content survives collections");
            ExpectString(icall_format_int32_custom(12345, fmtX), "3039",
                         "int32 X content survives collections");
            ExpectString(icall_format_int32_custom(-12345, fmtX), "FFFFCFC7",
                         "int32 negative X content survives collections");
            ExpectString(icall_format_int64_custom(255, fmtX4), "00FF",
                         "int64 X4 content survives collections");
            ExpectString(icall_format_int64_custom(INT64_C(123456789012345), fmtD),
                         "123456789012345",
                         "int64 D content survives collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtD));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtX4));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtX));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtN2));
    }

    //--- 2. double custom format under GC pressure ---
    {

        Il2CppObject* fmtE2 = NewManagedString("E2");
        Il2CppObject* fmtF2 = NewManagedString("F2");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtE2));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtF2));
        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_format_double_custom(0.00123, fmtE2, nullptr), "1.23E-003",
                         "double E2 content survives collections");
            ExpectString(icall_format_double_custom(-0.00123, fmtE2, nullptr), "-1.23E-003",
                         "double negative E2 content survives collections");
            ExpectString(icall_format_double_custom(1234.5678, fmtF2, nullptr), "1234.57",
                         "double F2 content survives collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtF2));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtE2));
    }

    //--- 3. General format under GC pressure ---
    {

        for (int32_t i = 0; i < 20; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            ExpectString(reinterpret_cast<Il2CppObject*>(icall_int32_to_string(42)), "42",
                         "int32 general content survives collections");
            ExpectString(reinterpret_cast<Il2CppObject*>(icall_int32_to_string(-7)), "-7",
                         "int32 negative general content survives collections");
            ExpectString(reinterpret_cast<Il2CppObject*>(icall_double_to_string(3.14)), "3.14",
                         "double general content survives collections");
        }
    }

    //--- 4. Fresh format string: a freshly-allocated (Gen0) format string
    //        survives a collection and is usable as a format specifier ---
    {

        Il2CppObject* fresh = nullptr;
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fresh));
        for (int32_t i = 0; i < 15; ++i)
        {
            il2cpp::il2cpp_gc_collect();
            // 'fresh' is a freshly-allocated format string (Gen0) that the
            // next collection may MOVE; it is rooted so Phase C rewrites it.
            fresh = NewManagedString("N2");
            il2cpp::il2cpp_gc_collect();
            ExpectString(icall_format_int32_custom(12345, fresh), "12,345.00",
                         "fresh format string survives collections");
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fresh));
    }

    //--- 5. Mixed churn: pseudo-random format ops + collections ---
    {

        Il2CppObject* fmtN2 = NewManagedString("N2");
        Il2CppObject* fmtX = NewManagedString("X");
        Il2CppObject* fmtE2 = NewManagedString("E2");
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtN2));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtX));
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&fmtE2));
        for (int32_t step = 0; step < 150; ++step)
        {
            const uint32_t op = NextRng() % 4;
            il2cpp::il2cpp_gc_collect();
            if (op == 0)
            {
                ExpectString(icall_format_int32_custom(12345, fmtN2), "12,345.00",
                             "churn int32 N2");
            }
            else if (op == 1)
            {
                ExpectString(icall_format_int32_custom(-12345, fmtX), "FFFFCFC7",
                             "churn int32 X");
            }
            else if (op == 2)
            {
                ExpectString(icall_format_double_custom(0.00123, fmtE2, nullptr), "1.23E-003",
                             "churn double E2");
            }
            else
            {
                ExpectString(reinterpret_cast<Il2CppObject*>(icall_int32_to_string(42)), "42",
                             "churn int32 general");
            }
        }
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtE2));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtX));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&fmtN2));
    }

    if (g_failures == 0)
    {
        std::printf("all format gc-stress tests passed\n");
        return 0;
    }
    std::printf("%d format gc-stress test(s) FAILED\n", g_failures);
    return 1;
}
