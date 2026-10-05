//------------------------------------------------------------------------------
// test_checked64.cpp -- i64ck-b23: the 64-bit checked arithmetic helpers
// (il2cpp_checked_{add,sub,mul}[_un]_64 / rem_un_64, exception.cpp).
//
// The int32 D-INT-1 helpers LOUD-rejected 64-bit operands at transpile time;
// the 64-bit family routes add.ovf/sub.ovf/mul.ovf (long) and their _un
// forms (ulong) to width-aware helpers. .NET checked semantics: in-range
// passes, wrap/overflow throws OverflowException; rem by zero throws
// DivideByZeroException. The _un forms compare UNSIGNED and take/return
// int64_t bit patterns (the transpiler carries ulong in int64_t slots).
//
// Plain executable + minimal assertion harness (see test_exception.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_exception.h"

#include <cstdio>
#include <cstdint>

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

// Run fn and report whether it threw the managed OverflowException carrier.
template <typename Fn>
bool ThrowsOverflow(Fn fn)
{
    int thrown = 0;
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException&)
    {
        thrown = 1;
    }
    return thrown == 1;
}

} // namespace

int main()
{
    // --- 1. il2cpp_checked_add_64 (signed int64 add) ---
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_add_64(INT64_MAX, 1); }),
           "checked add_64 INT64_MAX + 1 throws OverflowException");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_add_64(INT64_MIN, -1); }),
           "checked add_64 INT64_MIN + -1 throws OverflowException");
    Expect(il2cpp::il2cpp_checked_add_64(INT64_MAX, 0) == INT64_MAX,
           "checked add_64 in-range unchanged");
    Expect(il2cpp::il2cpp_checked_add_64(INT64_MAX, -1) == INT64_MAX - 1,
           "checked add_64 boundary passes (MAX + -1)");
    Expect(il2cpp::il2cpp_checked_add_64(INT64_MIN, 1) == INT64_MIN + 1,
           "checked add_64 boundary passes (MIN + 1)");

    // --- 2. il2cpp_checked_sub_64 (signed int64 sub) ---
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_sub_64(INT64_MAX, -1); }),
           "checked sub_64 INT64_MAX - -1 throws OverflowException");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_sub_64(INT64_MIN, 1); }),
           "checked sub_64 INT64_MIN - 1 throws OverflowException");
    Expect(il2cpp::il2cpp_checked_sub_64(INT64_MIN, 0) == INT64_MIN,
           "checked sub_64 in-range unchanged");
    Expect(il2cpp::il2cpp_checked_sub_64(INT64_MIN, -1) == INT64_MIN + 1,
           "checked sub_64 boundary passes (MIN - -1)");

    // --- 3. il2cpp_checked_mul_64 (signed int64 mul) ---
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_mul_64(INT64_MAX, 2); }),
           "checked mul_64 INT64_MAX * 2 throws OverflowException");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_mul_64(INT64_MIN, -1); }),
           "checked mul_64 INT64_MIN * -1 throws OverflowException");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_mul_64(-1, INT64_MIN); }),
           "checked mul_64 -1 * INT64_MIN throws OverflowException");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_mul_64(INT64_MAX / 2, 3); }),
           "checked mul_64 (MAX/2) * 3 throws OverflowException");
    Expect(il2cpp::il2cpp_checked_mul_64(INT64_MAX, 1) == INT64_MAX,
           "checked mul_64 in-range unchanged");
    Expect(il2cpp::il2cpp_checked_mul_64(INT64_MAX, -1) == INT64_MIN + 1,
           "checked mul_64 of MAX by -1 passes");
    Expect(il2cpp::il2cpp_checked_mul_64(INT64_MIN, 1) == INT64_MIN,
           "checked mul_64 MIN * 1 passes");
    Expect(il2cpp::il2cpp_checked_mul_64(0, INT64_MAX) == 0,
           "checked mul_64 by zero passes");
    Expect(il2cpp::il2cpp_checked_mul_64(2, 2) == 4,
           "checked mul_64 plain product passes");

    // --- 4. il2cpp_checked_add_un_64 (uint64 add, unsigned compare) ---
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_add_un_64(-1, 1); }),
           "checked add_un_64 UINT64_MAX + 1 throws OverflowException");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_add_un_64(INT64_MIN, INT64_MIN); }),
           "checked add_un_64 2^63 + 2^63 throws OverflowException");
    Expect(il2cpp::il2cpp_checked_add_un_64(-1, 0) == -1,
           "checked add_un_64 at UINT64_MAX boundary passes (all-one bits)");
    Expect(il2cpp::il2cpp_checked_add_un_64(INT64_MIN, INT64_MAX) == -1,
           "checked add_un_64 2^63 + (2^63-1) == UINT64_MAX passes");

    // --- 5. il2cpp_checked_sub_un_64 (uint64 sub, unsigned compare) ---
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_sub_un_64(0, 1); }),
           "checked sub_un_64 0 - 1 unsigned underflow throws OverflowException");
    Expect(il2cpp::il2cpp_checked_sub_un_64(-1, 1) == -2,
           "checked sub_un_64 in-range passes (0xFFFFFFFFFFFFFFFE)");
    Expect(il2cpp::il2cpp_checked_sub_un_64(0, 0) == 0,
           "checked sub_un_64 zero passes");

    // --- 6. il2cpp_checked_mul_un_64 (uint64 mul, unsigned compare) ---
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_mul_un_64(INT64_MIN, 2); }),
           "checked mul_un_64 2^63 * 2 throws OverflowException");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_mul_un_64(-1, -1); }),
           "checked mul_un_64 UINT64_MAX^2 throws OverflowException");
    Expect(il2cpp::il2cpp_checked_mul_un_64(0xFFFFFFFFLL, 0xFFFFFFFFLL) == static_cast<int64_t>(0xFFFFFFFE00000001ULL),
           "checked mul_un_64 (2^32-1)^2 passes (0xFFFFFFFE00000001)");
    Expect(il2cpp::il2cpp_checked_mul_un_64(0, INT64_MIN) == 0,
           "checked mul_un_64 by zero passes");

    // --- 7. il2cpp_checked_rem_un_64 (uint64 remainder + zero check) ---
    Expect(il2cpp::il2cpp_checked_rem_un_64(7, 3) == 1,
           "checked rem_un_64 plain remainder");
    Expect(il2cpp::il2cpp_checked_rem_un_64(-1, 4) == 3,
           "checked rem_un_64 unsigned modulo (0xFFFFFFFFFFFFFFFF % 4)");
    Expect(ThrowsOverflow([] { (void)il2cpp::il2cpp_checked_rem_un_64(5, 0); }),
           "checked rem_un_64 by zero throws DivideByZeroException");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("%d FAILURE(S)\n", g_failures);
    return 1;
}
