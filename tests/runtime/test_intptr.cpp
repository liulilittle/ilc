//------------------------------------------------------------------------------
// test_intptr.cpp -- System.IntPtr/UIntPtr pointer-arithmetic runtime tests.
//
// Coverage (normal + edge paths), mirroring the functions declared in
// intptr.h. All expectations are portable across 32-bit and 64-bit targets
// (intptr_t is the native pointer width; width-dependent expectations are
// guarded by INTPTR_MAX checks):
//   1. il2cpp_intptr_add: small offsets, negative offsets, zero identity,
//      large offsets (0x40000000), INT32_MAX / INT32_MIN offsets (the
//      int32 offset must be sign-extended to the pointer width).
//   2. il2cpp_intptr_subtract: same matrix; Subtract(p, int.MinValue)
//      moves the pointer UP by 0x80000000.
//   3. il2cpp_intptr_to_int64: sign-extension contract (identity on
//      64-bit; a high-bit value becomes negative on 32-bit).
//   4. il2cpp_intptr_to_int32: CHECKED conversion -- in-range values pass
//      unchanged, out-of-int32-range values throw OverflowException (net48
//      semantics, probe-verified).
//   5. il2cpp_intptr_op_equality: equal / unequal / zero / high-bit.
//   6. il2cpp_intptr_compare: -1/0/1 ordering as UNSIGNED addresses
//      (a high-bit value sorts above any positive value).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "intptr.h"
#include "il2cpp_exception.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(intptr_t) == 4 || sizeof(intptr_t) == 8,
              "intptr_t must be 4 or 8 bytes");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Runs 'fn' expecting a managed DotNetException whose type chain HEAD exactly
// equals 'expectedType' (the test_buffer.cpp convention).
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

} // namespace

int main()
{
    // --- 1. Add: pointer + int32 offset (sign-extended). ---
    Expect(il2cpp::runtime::il2cpp_intptr_add(0x1000, 4) == 0x1004,
           "add(0x1000, 4) == 0x1004");
    Expect(il2cpp::runtime::il2cpp_intptr_add(0x1000, -4) == 0xFFC,
           "add(0x1000, -4) == 0xFFC");
    Expect(il2cpp::runtime::il2cpp_intptr_add(0x1000, 0) == 0x1000,
           "add(0x1000, 0) is the identity");
    Expect(il2cpp::runtime::il2cpp_intptr_add(0, 0) == 0,
           "add(0, 0) == 0");
    Expect(il2cpp::runtime::il2cpp_intptr_add(0x1000, 0x40000000) == 0x40001000,
           "add large positive offset");
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0x1000, 0x40000000)
               == static_cast<intptr_t>(INT64_C(-1073737728)),
           "subtract large positive offset wraps below zero");
    // INT32_MAX offset: 0x1000 + 0x7FFFFFFF = 0x80000FFF. The expectation is
    // computed in the uint32 domain so the same expression is exact on both
    // widths (0x80000FFF fits uint32; on 32-bit the result wraps to the
    // same bit pattern, on 64-bit it is the positive value 2147487743).
    Expect(il2cpp::runtime::il2cpp_intptr_add(0x1000, INT32_MAX)
               == static_cast<intptr_t>(static_cast<uint32_t>(INT32_MAX) + UINT32_C(0x1000)),
           "add INT32_MAX offset (sign-extension boundary)");
    // INT32_MIN offset: sign-extension moves the pointer DOWN by 0x80000000.
    // 0x1000 - 0x80000000 == -2147479552, exact on both widths.
    Expect(il2cpp::runtime::il2cpp_intptr_add(0x1000, INT32_MIN)
               == static_cast<intptr_t>(INT64_C(-2147479552)),
           "add INT32_MIN offset moves down 0x80000000 (sign extension)");
    Expect(il2cpp::runtime::il2cpp_intptr_add(0, INT32_MIN)
               == static_cast<intptr_t>(INT64_C(-2147483648)),
           "add(0, INT32_MIN) == INT32_MIN as a pointer");

    // --- 2. Subtract: pointer - int32 offset (sign-extended). ---
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0x1000, 4) == 0xFFC,
           "subtract(0x1000, 4) == 0xFFC");
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0x1000, -4) == 0x1004,
           "subtract(0x1000, -4) == 0x1004");
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0x1000, 0) == 0x1000,
           "subtract(0x1000, 0) is the identity");
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0, 0) == 0,
           "subtract(0, 0) == 0");
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0x1000, INT32_MAX)
               == static_cast<intptr_t>(INT64_C(-2147479551)),
           "subtract INT32_MAX offset moves down 0x7FFFFFFF");
    // INT32_MIN offset: Subtract(p, int.MinValue) moves the pointer UP by
    // 0x80000000. Result 0x80001000: +2147485696 on 64-bit, but the wrapped
    // int32 pattern -2147479552 on 32-bit (the bit patterns agree).
#if INTPTR_MAX == INT64_MAX
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0x1000, INT32_MIN)
               == static_cast<intptr_t>(INT64_C(2147487744)),
           "subtract INT32_MIN offset moves up 0x80000000 (64-bit)");
#else
    Expect(il2cpp::runtime::il2cpp_intptr_subtract(0x1000, INT32_MIN)
               == static_cast<intptr_t>(INT64_C(-2147479552)),
           "subtract INT32_MIN offset wraps (32-bit)");
#endif

    // --- 3. ToInt64: sign-extension of the pointer value. ---
    Expect(il2cpp::runtime::il2cpp_intptr_to_int64(0) == 0,
           "to_int64(0) == 0");
    Expect(il2cpp::runtime::il2cpp_intptr_to_int64(1) == 1,
           "to_int64(1) == 1");
    Expect(il2cpp::runtime::il2cpp_intptr_to_int64(static_cast<intptr_t>(-1)) == -1,
           "to_int64(-1) == -1 (sign extension)");
    Expect(il2cpp::runtime::il2cpp_intptr_to_int64(static_cast<intptr_t>(INT32_MAX))
               == static_cast<int64_t>(INT32_MAX),
           "to_int64(INT32_MAX bit pattern) sign-extends to int64");
#if INTPTR_MAX == INT64_MAX
    // 64-bit: intptr_t is already int64 -- high bit preserved (identity).
    Expect(il2cpp::runtime::il2cpp_intptr_to_int64(static_cast<intptr_t>(INT64_MIN)) == INT64_MIN,
           "to_int64 preserves the high bit (64-bit identity)");
#else
    // 32-bit: a high-bit address becomes negative through sign extension.
    Expect(il2cpp::runtime::il2cpp_intptr_to_int64(static_cast<intptr_t>(INT32_MIN))
               == static_cast<int64_t>(INT32_MIN),
           "to_int64(INT32_MIN) sign-extends to int64 (32-bit)");
#endif

    // --- 4. ToInt32: CHECKED conversion (net48: OverflowException out of
    // int32 range; probe-verified on csc net48 -- no silent truncation). ---
    Expect(il2cpp::runtime::il2cpp_intptr_to_int32(0) == 0,
           "to_int32(0) == 0");
    Expect(il2cpp::runtime::il2cpp_intptr_to_int32(static_cast<intptr_t>(INT32_MAX)) == INT32_MAX,
           "to_int32(INT32_MAX) fits unchanged");
    Expect(il2cpp::runtime::il2cpp_intptr_to_int32(static_cast<intptr_t>(-1)) == -1,
           "to_int32(-1) == -1 (all-ones fits)");
#if INTPTR_MAX == INT64_MAX
    // 64-bit: values outside the int32 range throw OverflowException.
    ExpectThrows("System.OverflowException",
                 "to_int32(0x1234567890) throws OverflowException (net48 checked conversion)",
                 [] { (void)il2cpp::runtime::il2cpp_intptr_to_int32(
                          static_cast<intptr_t>(INT64_C(0x1234567890))); });
    ExpectThrows("System.OverflowException",
                 "to_int32(INT64_MAX) throws OverflowException",
                 [] { (void)il2cpp::runtime::il2cpp_intptr_to_int32(
                          static_cast<intptr_t>(INT64_MAX)); });
    ExpectThrows("System.OverflowException",
                 "to_int32(INT64_MIN) throws OverflowException",
                 [] { (void)il2cpp::runtime::il2cpp_intptr_to_int32(
                          static_cast<intptr_t>(INT64_MIN)); });
    // A value whose LOW 32 bits fit but whose full value does not (e.g.
    // 0x7FFFFFFF00000001, low 32 = 1) still throws -- the check is on the
    // full pointer value (net48 probe: toi32_wide_lowbits1 = OverflowException).
    ExpectThrows("System.OverflowException",
                 "to_int32(0x7FFFFFFF00000001) throws OverflowException (full-value check)",
                 [] { (void)il2cpp::runtime::il2cpp_intptr_to_int32(
                          static_cast<intptr_t>(INT64_C(0x7FFFFFFF00000001))); });
#else
    // 32-bit: intptr_t is int32, so every value fits -- no overflow possible.
    Expect(il2cpp::runtime::il2cpp_intptr_to_int32(static_cast<intptr_t>(INT32_MIN)) == INT32_MIN,
           "to_int32(INT32_MIN) fits unchanged (32-bit)");
#endif

    // --- 5. op_Equality: bit-pattern comparison. ---
    Expect(il2cpp::runtime::il2cpp_intptr_op_equality(5, 5),
           "equality(5, 5) is true");
    Expect(!il2cpp::runtime::il2cpp_intptr_op_equality(5, 6),
           "equality(5, 6) is false");
    Expect(il2cpp::runtime::il2cpp_intptr_op_equality(0, 0),
           "equality(0, 0) is true");
    Expect(!il2cpp::runtime::il2cpp_intptr_op_equality(0, 1),
           "equality(0, 1) is false");
    Expect(il2cpp::runtime::il2cpp_intptr_op_equality(static_cast<intptr_t>(-1),
                                                      static_cast<intptr_t>(-1)),
           "equality(-1, -1) is true");
#if INTPTR_MAX == INT64_MAX
    Expect(il2cpp::runtime::il2cpp_intptr_op_equality(static_cast<intptr_t>(INT64_MIN),
                                                      static_cast<intptr_t>(INT64_MIN)),
           "equality(high-bit, high-bit) is true");
    Expect(!il2cpp::runtime::il2cpp_intptr_op_equality(static_cast<intptr_t>(INT64_MIN), 0),
           "equality(high-bit, 0) is false");
#endif

    // --- 6. Compare: unsigned address ordering, -1 / 0 / 1. ---
    Expect(il2cpp::runtime::il2cpp_intptr_compare(5, 5) == 0,
           "compare(5, 5) == 0");
    Expect(il2cpp::runtime::il2cpp_intptr_compare(3, 7) == -1,
           "compare(3, 7) == -1");
    Expect(il2cpp::runtime::il2cpp_intptr_compare(7, 3) == 1,
           "compare(7, 3) == 1");
    Expect(il2cpp::runtime::il2cpp_intptr_compare(0, 0) == 0,
           "compare(0, 0) == 0");
    Expect(il2cpp::runtime::il2cpp_intptr_compare(0, 1) == -1,
           "compare(0, 1) == -1");
    Expect(il2cpp::runtime::il2cpp_intptr_compare(1, 0) == 1,
           "compare(1, 0) == 1");
    // Unsigned address semantics: 0xFFFF... (as an address) sorts above 0.
    Expect(il2cpp::runtime::il2cpp_intptr_compare(static_cast<intptr_t>(-1), 0) == 1,
           "compare(-1, 0) == 1 (unsigned address ordering)");
    Expect(il2cpp::runtime::il2cpp_intptr_compare(0, static_cast<intptr_t>(-1)) == -1,
           "compare(0, -1) == -1 (unsigned address ordering)");
#if INTPTR_MAX == INT64_MAX
    // 64-bit: a high-bit address sorts above the maximum positive address.
    Expect(il2cpp::runtime::il2cpp_intptr_compare(static_cast<intptr_t>(INT64_MIN),
                                                  static_cast<intptr_t>(INT64_MAX)) == 1,
           "compare(INT64_MIN, INT64_MAX) == 1 (unsigned address ordering)");
#else
    // 32-bit: same property on the 32-bit pattern.
    Expect(il2cpp::runtime::il2cpp_intptr_compare(static_cast<intptr_t>(INT32_MIN), 0) == 1,
           "compare(INT32_MIN, 0) == 1 (unsigned address ordering)");
#endif

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d intptr assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime intptr tests passed\n");
    return 0;
}
