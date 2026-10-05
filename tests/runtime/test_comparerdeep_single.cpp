//------------------------------------------------------------------------------
// test_comparerdeep_single.cpp -- comparerdeep regression spec (g4/P1.4).
//
// Pins the runtime default comparer (Comparer.Default -> ComparerCompareCore)
// on System.Single boxes: three-way sign, boundaries, symmetry, plus the
// cross-type ArgumentException negative and the Int32/Double neighbors.
//
// Regression context: P1.4's dedicated System.Single arm read a 4-byte float
// out of the 8-byte DOUBLE payload slot the M4 box writes (boxingext2.cpp
// icall_box_float/AllocBoxed64), collapsing -1.5f/1.5f to +0.0f and every
// comparerdeep sign probe to false. This executable fails on the pre-fix
// runtime and passes on the fixed one.
//------------------------------------------------------------------------------
#include "arraysort.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "il2cpp_exception.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

using il2cpp::Il2CppObject;

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

void ExpectI32(int32_t actual, int32_t expected, const char* what)
{
    if (actual != expected)
    {
        std::printf("FAIL: %s (expected %d, got %d)\n", what, expected, actual);
        ++g_failures;
    }
}

template <typename F>
void ExpectThrows(const char* expectedType, const char* what, F&& fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        const size_t len = std::strlen(expectedType);
        const bool headMatches = std::strncmp(thrown, expectedType, len) == 0 &&
                                 (thrown[len] == ',' || thrown[len] == '\0');
        if (headMatches)
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

const char kArgumentType[] = "System.ArgumentException";

} // namespace

int main()
{
    Il2CppObject* cd = il2cpp::il2cpp_comparer_default();

    Il2CppObject* fNeg = il2cpp::runtime::icall_box_float(-1.5);
    Il2CppObject* fPos = il2cpp::runtime::icall_box_float(1.5);
    Il2CppObject* fSame = il2cpp::runtime::icall_box_float(9.5);
    Il2CppObject* fSame2 = il2cpp::runtime::icall_box_float(9.5);
    Il2CppObject* fMin = il2cpp::runtime::icall_box_float(-3.4028234663852886e+38);
    Il2CppObject* fMax = il2cpp::runtime::icall_box_float(3.4028234663852886e+38);
    Il2CppObject* fZero = il2cpp::runtime::icall_box_float(0.0);
    Il2CppObject* i5 = il2cpp::il2cpp_box_int32(&::System_Int32_class, 5);
    Il2CppObject* dHalf = il2cpp::runtime::icall_box_double(0.5);
    Il2CppObject* dOneHalf = il2cpp::runtime::icall_box_double(1.5);

    // Three-way sign (the comparerdeep grid).
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, fNeg, fPos), -1, "single ascending is -1");
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, fPos, fNeg), 1, "single descending is 1");
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, fSame, fSame2), 0, "single equal is 0");
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, fZero, fPos), -1, "single zero < positive");

    // Boundaries (float.MinValue < float.MaxValue).
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, fMin, fMax), -1, "single min < max");
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, fMax, fMin), 1, "single max > min");

    // Symmetry.
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, fNeg, fPos),
              -il2cpp::il2cpp_comparer_compare(cd, fPos, fNeg), "single compare is sign-symmetric");

    // Cross-type negatives (net48 Single.CompareTo(object) / Int32.CompareTo(object)).
    ExpectThrows(kArgumentType, "single vs int32 throws ArgumentException",
                 [&] { il2cpp::il2cpp_comparer_compare(cd, fNeg, i5); });
    ExpectThrows(kArgumentType, "int32 vs single throws ArgumentException",
                 [&] { il2cpp::il2cpp_comparer_compare(cd, i5, fNeg); });

    // Neighbor regressions: Int32 and Double arms stay correct.
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, dHalf, dOneHalf), -1, "double ascending is -1");
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, dOneHalf, dHalf), 1, "double descending is 1");
    ExpectThrows(kArgumentType, "double vs single throws ArgumentException",
                 [&] { il2cpp::il2cpp_comparer_compare(cd, dHalf, fPos); });
    Il2CppObject* i3 = il2cpp::il2cpp_box_int32(&::System_Int32_class, 3);
    ExpectI32(il2cpp::il2cpp_comparer_compare(cd, i3, i5), -1, "int32 ascending is -1");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("FAILURES: %d\n", g_failures);
    return 1;
}
