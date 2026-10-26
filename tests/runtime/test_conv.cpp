//------------------------------------------------------------------------------
// test_conv.cpp -- numeric conversion (conv family) runtime unit tests.
//
// The assertions encode the EMPIRICALLY VERIFIED net48 semantics (the
// convcase e2e program was run on .NET Framework 4.8 first; its output is
// the oracle -- see tests/cases/convcase/convcase.cs):
//
//   1. icall_conv_i8_i4      int32 -> int64 sign extension.
//   2. icall_conv_i4_i8      int64 -> int32 low-32-bit wrap (unchecked
//                            conv.i4). Oracle: (int)0x123456789ABCDEF0 ==
//                            -1698898192, (int)long.MaxValue == -1,
//                            (int)long.MinValue == 0.
//   3. icall_conv_i8_d       double -> int64 truncation toward zero; NaN,
//                            +/-Infinity and out-of-range |value| >= 2^63
//                            yield int64.MinValue (the x64 cvttsd2si
//                            "integer indefinite" -- net48 RUNTIME path,
//                            verified via variables: (long)2^63 == long.Min,
//                            (long)NaN == long.Min, ...).
//   4. icall_conv_d_i8       int64 -> double (exact below 2^53, correctly
//                            rounded above; hardware round-to-nearest-even
//                            on every targeted compiler).
//   5. icall_conv_d_i4       int32 -> double (exact: int32 needs 32 bits,
//                            double has 53).
//   6. icall_conv_i4_d       double -> int32 truncation toward zero; NaN,
//                            +/-Infinity and out-of-range |value| >= 2^31
//                            yield int32.MinValue (runtime path oracle:
//                            (int)2^31 == int.Min, (int)NaN == int.Min).
//   7/8. icall_conv_u4_i4 / icall_conv_i4_u4  int32 <-> uint32 bit patterns.
//   9/10. icall_conv_u8_i8 / icall_conv_i8_u8 int64 <-> uint64 bit patterns.
//  11. icall_conv_ovf_i8_i4  checked int32 -> int64 (never overflows).
//  12. icall_conv_ovf_i4_i8  checked int64 -> int32; outside [int32.Min,
//                            int32.Max] throws OverflowException (net48
//                            checked((int)long.MaxValue) throws).
//  13. icall_conv_bool_i4    int32 -> bool: any non-zero -> 1.
//  14. icall_conv_u4_i8 / icall_conv_u4_u8 / icall_conv_i4_u8  the low 32
//                            bits of the 64-bit value (conv.u4/conv.i4 wrap).
//  15. icall_conv_i8_u4 / icall_conv_u8_u4  uint32 -> 64-bit ZERO extension
//                            ((long)uint.MaxValue == 4294967295 -- the
//                            net48 "u4 negative" semantics).
//  16. icall_conv_u8_d       double -> uint64, x64 cvttsd2si sequence:
//                            NaN / >= 2^64 -> 0; < -2^63 -> 2^63; negative
//                            wraps; [0, 2^64) truncates (net48 empirical).
//  17. icall_conv_u4_d       double -> uint32, the low 32 bits of the int64
//                            conversion ((uint)2^32 == 0, (uint)NaN == 0).
//  18. icall_conv_d_u4 / icall_conv_d_u8  uint32 exact / uint64 correctly
//                            rounded (round-to-nearest-even) to double.
//  19. icall_conv_r4_*       int/uint/double -> float round-to-nearest-even
//                            (conv.r4): (float)16777217 == 16777216.
//  20. icall_conv_ovf_u4_i8 / ovf_u4_u8 / ovf_i4_u8 / ovf_i8_u8 /
//      ovf_u8_i8 / ovf_u8_i4 / ovf_u4_i4  checked integer conversions
//                            (destination-range test; unsigned targets throw
//                            on negatives).
//  21. icall_conv_ovf_i4_d / ovf_i8_d / ovf_u4_d / ovf_u8_d  checked
//                            floating -> integer: the check is on the
//                            TRUNCATED result fitting (net48:
//                            checked((int)-2147483648.5) == -2147483648 no
//                            throw; checked((int)-2147483649.5) throws;
//                            checked((uint)-0.5) == 0 no throw).
//
// Round trips close the loop: i8_i4 o i4_i8, u4_i4 o i4_u4, u8_i8 o i8_u8,
// d_i4 o i4_d (in-range) and d_i8 o i8_d (representable inputs).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_conv.h"
#include "il2cpp_exception.h"

#include <cmath>
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

// ExpectDotNetException: run fn(), require it to throw a DotNetException of
// the given type tag carrying a message that contains 'messageFragment'.
// Any other exception carrier (or no throw at all) fails the assertion.
void ExpectDotNetException(const char* what, const char* typeTag,
                           const char* messageFragment,
                           void (*fn)())
{
    bool caught = false;
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        caught = true;
        Expect(std::strcmp(e.type_name(), typeTag) == 0,
               "exception type tag matches the managed type");
        Expect(std::strstr(e.what(), messageFragment) != nullptr,
               "exception message fragment present");
    }
    catch (...)
    {
        caught = true;
        Expect(false, "wrong exception carrier type (not DotNetException)");
    }
    Expect(caught, what);
}

// Wrap the throw-expecting calls so their values are consumed (the helpers
// return their result only on the non-throwing path).
int32_t NoThrowConvI4I8(int64_t v) { return il2cpp::runtime::icall_conv_ovf_i4_i8(v); }
int32_t NoThrowConvI4U8(uint64_t v) { return il2cpp::runtime::icall_conv_ovf_i4_u8(v); }
int64_t NoThrowConvI8U8(uint64_t v) { return il2cpp::runtime::icall_conv_ovf_i8_u8(v); }
uint32_t NoThrowConvU4I8(int64_t v) { return il2cpp::runtime::icall_conv_ovf_u4_i8(v); }
uint32_t NoThrowConvU4U8(uint64_t v) { return il2cpp::runtime::icall_conv_ovf_u4_u8(v); }
uint32_t NoThrowConvU4I4(int32_t v) { return il2cpp::runtime::icall_conv_ovf_u4_i4(v); }
uint64_t NoThrowConvU8I8(int64_t v) { return il2cpp::runtime::icall_conv_ovf_u8_i8(v); }
uint64_t NoThrowConvU8I4(int32_t v) { return il2cpp::runtime::icall_conv_ovf_u8_i4(v); }
int32_t NoThrowConvOvfI4D(double v) { return il2cpp::runtime::icall_conv_ovf_i4_d(v); }
int64_t NoThrowConvOvfI8D(double v) { return il2cpp::runtime::icall_conv_ovf_i8_d(v); }
uint32_t NoThrowConvOvfU4D(double v) { return il2cpp::runtime::icall_conv_ovf_u4_d(v); }
uint64_t NoThrowConvOvfU8D(double v) { return il2cpp::runtime::icall_conv_ovf_u8_d(v); }

} // namespace

int main()
{
    // --- 1. int32 -> int64: sign extension ---
    Expect(il2cpp::runtime::icall_conv_i8_i4(0) == 0LL, "conv.i8(0) == 0");
    Expect(il2cpp::runtime::icall_conv_i8_i4(1) == 1LL, "conv.i8(1) == 1");
    Expect(il2cpp::runtime::icall_conv_i8_i4(-1) == -1LL, "conv.i8(-1) == -1");
    Expect(il2cpp::runtime::icall_conv_i8_i4(INT32_MAX) == 2147483647LL,
           "conv.i8(INT32_MAX) sign-extends");
    Expect(il2cpp::runtime::icall_conv_i8_i4(INT32_MIN) == -2147483648LL,
           "conv.i8(INT32_MIN) sign-extends");

    // --- 2. int64 -> int32: low 32 bits, unchecked wrap ---
    Expect(il2cpp::runtime::icall_conv_i4_i8(0LL) == 0, "conv.i4(0L) == 0");
    Expect(il2cpp::runtime::icall_conv_i4_i8(1LL) == 1, "conv.i4(1L) == 1");
    Expect(il2cpp::runtime::icall_conv_i4_i8(-1LL) == -1, "conv.i4(-1L) == -1");
    Expect(il2cpp::runtime::icall_conv_i4_i8(static_cast<int64_t>(INT32_MAX)) == INT32_MAX,
           "conv.i4(INT32_MAX as long) identity");
    Expect(il2cpp::runtime::icall_conv_i4_i8(static_cast<int64_t>(INT32_MIN)) == INT32_MIN,
           "conv.i4(INT32_MIN as long) identity");
    Expect(il2cpp::runtime::icall_conv_i4_i8(static_cast<int64_t>(0x123456789ABCDEF0LL)) == -1698898192,
           "conv.i4(0x123456789ABCDEF0) == 0x9ABCDEF0 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_i8(INT64_MAX) == -1,
           "conv.i4(INT64_MAX) == -1 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_i8(INT64_MIN) == 0,
           "conv.i4(INT64_MIN) == 0 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_i8(0x100000000LL) == 0,
           "conv.i4(2^32) == 0");

    // --- 3. double -> int64: truncate toward zero; NaN/Inf/out-of-range ->
    // int64.MinValue (net48 runtime path, "integer indefinite") ---
    Expect(il2cpp::runtime::icall_conv_i8_d(0.9) == 0LL, "conv.i8(0.9) == 0");
    Expect(il2cpp::runtime::icall_conv_i8_d(1.5) == 1LL, "conv.i8(1.5) == 1");
    Expect(il2cpp::runtime::icall_conv_i8_d(-0.5) == 0LL, "conv.i8(-0.5) == 0");
    Expect(il2cpp::runtime::icall_conv_i8_d(-1.5) == -1LL, "conv.i8(-1.5) == -1");
    Expect(il2cpp::runtime::icall_conv_i8_d(5.7) == 5LL, "conv.i8(5.7) == 5");
    Expect(il2cpp::runtime::icall_conv_i8_d(-5.7) == -5LL, "conv.i8(-5.7) == -5");
    Expect(il2cpp::runtime::icall_conv_i8_d(-0.0) == 0LL, "conv.i8(-0.0) == 0");
    Expect(il2cpp::runtime::icall_conv_i8_d(9223372036854774784.0) == 9223372036854774784LL,
           "conv.i8(2^63 - 1024) in range (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_d(9223372036854775808.0) == INT64_MIN,
           "conv.i8(2^63) == int64.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_d(-9223372036854775808.0) == INT64_MIN,
           "conv.i8(-2^63) == int64.Min (boundary in range)");
    Expect(il2cpp::runtime::icall_conv_i8_d(-9223372036854777856.0) == INT64_MIN,
           "conv.i8(-(2^63 + 2048)) == int64.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_d(std::nan("")) == INT64_MIN,
           "conv.i8(NaN) == int64.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_d(INFINITY) == INT64_MIN,
           "conv.i8(+inf) == int64.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_d(-INFINITY) == INT64_MIN,
           "conv.i8(-inf) == int64.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_d(1.0e300) == INT64_MIN,
           "conv.i8(1e300) == int64.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_d(-1.0e300) == INT64_MIN,
           "conv.i8(-1e300) == int64.Min");

    // --- 4. int64 -> double: exact below 2^53, correctly rounded above ---
    Expect(il2cpp::runtime::icall_conv_d_i8(5LL) == 5.0, "conv.r8(5L) == 5.0");
    Expect(il2cpp::runtime::icall_conv_d_i8(-123456789LL) == -123456789.0,
           "conv.r8(-123456789L) exact");
    Expect(il2cpp::runtime::icall_conv_d_i8(INT64_MAX) == 9223372036854775808.0,
           "conv.r8(INT64_MAX) rounds to 2^63 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_d_i8(INT64_MIN) == -9223372036854775808.0,
           "conv.r8(INT64_MIN) == -2^63 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_d_i8(9007199254740992LL) == 9007199254740992.0,
           "conv.r8(2^53) exact");
    Expect(il2cpp::runtime::icall_conv_d_i8(9007199254740993LL) == 9007199254740992.0,
           "conv.r8(2^53 + 1) rounds down to 2^53 (not representable)");

    // --- 5. int32 -> double: exact for every int32 ---
    Expect(il2cpp::runtime::icall_conv_d_i4(0) == 0.0, "conv.r8(0) == 0.0");
    Expect(il2cpp::runtime::icall_conv_d_i4(-1) == -1.0, "conv.r8(-1) == -1.0");
    Expect(il2cpp::runtime::icall_conv_d_i4(INT32_MAX) == 2147483647.0,
           "conv.r8(INT32_MAX) exact");
    Expect(il2cpp::runtime::icall_conv_d_i4(INT32_MIN) == -2147483648.0,
           "conv.r8(INT32_MIN) exact");

    // --- 6. double -> int32: truncate toward zero; NaN/Inf/out-of-range ->
    // int32.MinValue (net48 runtime path, "integer indefinite") ---
    Expect(il2cpp::runtime::icall_conv_i4_d(0.9) == 0, "conv.i4(0.9) == 0");
    Expect(il2cpp::runtime::icall_conv_i4_d(1.5) == 1, "conv.i4(1.5) == 1");
    Expect(il2cpp::runtime::icall_conv_i4_d(-0.5) == 0, "conv.i4(-0.5) == 0");
    Expect(il2cpp::runtime::icall_conv_i4_d(-1.5) == -1, "conv.i4(-1.5) == -1");
    Expect(il2cpp::runtime::icall_conv_i4_d(2147483647.0) == INT32_MAX,
           "conv.i4(2^31 - 1) in range");
    Expect(il2cpp::runtime::icall_conv_i4_d(2147483647.9) == INT32_MAX,
           "conv.i4(2147483647.9) truncates to INT32_MAX");
    Expect(il2cpp::runtime::icall_conv_i4_d(2147483648.0) == INT32_MIN,
           "conv.i4(2^31) == int32.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_d(-2147483648.0) == INT32_MIN,
           "conv.i4(-2^31) == int32.Min (boundary in range)");
    Expect(il2cpp::runtime::icall_conv_i4_d(-2147483649.0) == INT32_MIN,
           "conv.i4(-(2^31 + 1) rounded to -2^31) == int32.Min (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_d(std::nan("")) == INT32_MIN,
           "conv.i4(NaN) == int32.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_d(INFINITY) == INT32_MIN,
           "conv.i4(+inf) == int32.Min (net48 runtime oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_d(-INFINITY) == INT32_MIN,
           "conv.i4(-inf) == int32.Min");
    Expect(il2cpp::runtime::icall_conv_i4_d(1.0e300) == INT32_MIN,
           "conv.i4(1e300) == int32.Min");
    Expect(il2cpp::runtime::icall_conv_i4_d(-0.0) == 0, "conv.i4(-0.0) == 0");

    // --- 7. int32 -> uint32: bit pattern (signed -> unsigned is defined) ---
    Expect(il2cpp::runtime::icall_conv_u4_i4(0) == 0u, "conv.u4(0) == 0u");
    Expect(il2cpp::runtime::icall_conv_u4_i4(-1) == 0xFFFFFFFFu,
           "conv.u4(-1) == 0xFFFFFFFFu");
    Expect(il2cpp::runtime::icall_conv_u4_i4(INT32_MIN) == 0x80000000u,
           "conv.u4(INT32_MIN) == 0x80000000u");
    Expect(il2cpp::runtime::icall_conv_u4_i4(12345) == 12345u,
           "conv.u4(12345) == 12345u");

    // --- 8. uint32 -> int32: bit pattern (two's complement wrap) ---
    Expect(il2cpp::runtime::icall_conv_i4_u4(0u) == 0, "conv.i4(0u) == 0");
    Expect(il2cpp::runtime::icall_conv_i4_u4(0xFFFFFFFFu) == -1,
           "conv.i4(0xFFFFFFFFu) == -1");
    Expect(il2cpp::runtime::icall_conv_i4_u4(0x80000000u) == INT32_MIN,
           "conv.i4(0x80000000u) == INT32_MIN");
    Expect(il2cpp::runtime::icall_conv_i4_u4(12345u) == 12345,
           "conv.i4(12345u) == 12345");

    // --- 9. int64 -> uint64: bit pattern (signed -> unsigned is defined) ---
    Expect(il2cpp::runtime::icall_conv_u8_i8(0LL) == 0ULL, "conv.u8(0L) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_i8(-1LL) == 0xFFFFFFFFFFFFFFFFULL,
           "conv.u8(-1L) == 0xFFFFFFFFFFFFFFFF");
    Expect(il2cpp::runtime::icall_conv_u8_i8(INT64_MIN) == 0x8000000000000000ULL,
           "conv.u8(INT64_MIN) == 0x8000000000000000");
    Expect(il2cpp::runtime::icall_conv_u8_i8(123456789LL) == 123456789ULL,
           "conv.u8(123456789L) == 123456789");

    // --- 9b. int32 -> uint64: ZERO extension (valdeep L26 -- the int32 slot
    // holds the uint32 bit pattern; 0xFFFFFFFF widens to 4294967295, NOT -1)
    Expect(il2cpp::runtime::icall_conv_u8_i4(0) == 0ULL, "conv.u8(0) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_i4(-1) == 4294967295ULL,
           "conv.u8(-1) == 4294967295 (UInt32.MaxValue, not -1)");
    Expect(il2cpp::runtime::icall_conv_u8_i4(INT32_MIN) == 2147483648ULL,
           "conv.u8(INT32_MIN) == 2147483648");
    Expect(il2cpp::runtime::icall_conv_u8_i4(123456789) == 123456789ULL,
           "conv.u8(123456789) == 123456789");

    // --- 10. uint64 -> int64: bit pattern (two's complement wrap) ---
    Expect(il2cpp::runtime::icall_conv_i8_u8(0ULL) == 0LL, "conv.i8(0UL) == 0");
    Expect(il2cpp::runtime::icall_conv_i8_u8(0xFFFFFFFFFFFFFFFFULL) == -1LL,
           "conv.i8(0xFFFFFFFFFFFFFFFF) == -1");
    Expect(il2cpp::runtime::icall_conv_i8_u8(0x8000000000000000ULL) == INT64_MIN,
           "conv.i8(0x8000000000000000) == INT64_MIN");
    Expect(il2cpp::runtime::icall_conv_i8_u8(123456789ULL) == 123456789LL,
           "conv.i8(123456789UL) == 123456789");

    // --- 11. checked int32 -> int64: never overflows ---
    Expect(il2cpp::runtime::icall_conv_ovf_i8_i4(0) == 0LL, "conv.ovf.i8(0) == 0");
    Expect(il2cpp::runtime::icall_conv_ovf_i8_i4(-1) == -1LL, "conv.ovf.i8(-1) == -1");
    Expect(il2cpp::runtime::icall_conv_ovf_i8_i4(INT32_MAX) == 2147483647LL,
           "conv.ovf.i8(INT32_MAX) sign-extends");
    Expect(il2cpp::runtime::icall_conv_ovf_i8_i4(INT32_MIN) == -2147483648LL,
           "conv.ovf.i8(INT32_MIN) sign-extends");

    // --- 12. checked int64 -> int32: in-range passthrough; out-of-range
    // throws OverflowException (net48 checked((int)long.MaxValue) throws) ---
    Expect(il2cpp::runtime::icall_conv_ovf_i4_i8(0LL) == 0,
           "conv.ovf.i4(0L) == 0");
    Expect(il2cpp::runtime::icall_conv_ovf_i4_i8(12345LL) == 12345,
           "conv.ovf.i4(12345L) == 12345");
    Expect(il2cpp::runtime::icall_conv_ovf_i4_i8(static_cast<int64_t>(INT32_MAX)) == INT32_MAX,
           "conv.ovf.i4(INT32_MAX) in range");
    Expect(il2cpp::runtime::icall_conv_ovf_i4_i8(static_cast<int64_t>(INT32_MIN)) == INT32_MIN,
           "conv.ovf.i4(INT32_MIN) in range");
    ExpectDotNetException("conv.ovf.i4(INT64_MAX) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvI4I8(INT64_MAX); });
    ExpectDotNetException("conv.ovf.i4(INT64_MIN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvI4I8(INT64_MIN); });
    ExpectDotNetException("conv.ovf.i4(INT32_MAX + 1) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvI4I8(static_cast<int64_t>(INT32_MAX) + 1LL); });
    ExpectDotNetException("conv.ovf.i4(INT32_MIN - 1) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvI4I8(static_cast<int64_t>(INT32_MIN) - 1LL); });

    // --- 13. int32 -> bool: non-zero -> 1 ---
    Expect(il2cpp::runtime::icall_conv_bool_i4(0) == 0, "conv.bool(0) == 0");
    Expect(il2cpp::runtime::icall_conv_bool_i4(1) == 1, "conv.bool(1) == 1");
    Expect(il2cpp::runtime::icall_conv_bool_i4(-1) == 1, "conv.bool(-1) == 1");
    Expect(il2cpp::runtime::icall_conv_bool_i4(INT32_MIN) == 1,
           "conv.bool(INT32_MIN) == 1");
    Expect(il2cpp::runtime::icall_conv_bool_i4(INT32_MAX) == 1,
           "conv.bool(INT32_MAX) == 1");

    // --- 14. 64-bit -> 32-bit, unchecked (conv.u4 / conv.i4): low 32 bits ---
    Expect(il2cpp::runtime::icall_conv_u4_i8(0LL) == 0u, "conv.u4(0L) == 0u");
    Expect(il2cpp::runtime::icall_conv_u4_i8(1LL) == 1u, "conv.u4(1L) == 1u");
    Expect(il2cpp::runtime::icall_conv_u4_i8(-1LL) == 4294967295u,
           "conv.u4(-1L) == 4294967295 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_i8(4294967295LL) == 4294967295u,
           "conv.u4(4294967295L) == 4294967295");
    Expect(il2cpp::runtime::icall_conv_u4_i8(4294967296LL) == 0u,
           "conv.u4(4294967296L) == 0");
    Expect(il2cpp::runtime::icall_conv_u4_i8(INT64_MAX) == 4294967295u,
           "conv.u4(INT64_MAX) == 4294967295 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_i8(INT64_MIN) == 0u,
           "conv.u4(INT64_MIN) == 0 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_u8(0ULL) == 0u, "conv.u4(0UL) == 0u");
    Expect(il2cpp::runtime::icall_conv_u4_u8(4294967295ULL) == 4294967295u,
           "conv.u4(4294967295UL) == 4294967295");
    Expect(il2cpp::runtime::icall_conv_u4_u8(4294967296ULL) == 0u,
           "conv.u4(4294967296UL) == 0");
    Expect(il2cpp::runtime::icall_conv_u4_u8(UINT64_MAX) == 4294967295u,
           "conv.u4(ULONG_MAX) == 4294967295 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_u8(0x8000000000000000ULL) == 0u,
           "conv.u4(2^63) == 0 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_u8(0ULL) == 0, "conv.i4(0UL) == 0");
    Expect(il2cpp::runtime::icall_conv_i4_u8(4294967295ULL) == -1,
           "conv.i4(4294967295UL) == -1 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_u8(4294967296ULL) == 0,
           "conv.i4(4294967296UL) == 0");
    Expect(il2cpp::runtime::icall_conv_i4_u8(4294967297ULL) == 1,
           "conv.i4(4294967297UL) == 1");
    Expect(il2cpp::runtime::icall_conv_i4_u8(UINT64_MAX) == -1,
           "conv.i4(ULONG_MAX) == -1 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i4_u8(0x8000000000000000ULL) == 0,
           "conv.i4(2^63) == 0 (net48 oracle)");

    // --- 15. uint32 -> int64/uint64: ZERO extension (conv.i8/conv.u8 from a
    // u4 -- the net48 "u4 negative" semantics: (long)uint.MaxValue ==
    // 4294967295, NOT -1) ---
    Expect(il2cpp::runtime::icall_conv_i8_u4(0u) == 0LL, "conv.i8(0u) == 0");
    Expect(il2cpp::runtime::icall_conv_i8_u4(1u) == 1LL, "conv.i8(1u) == 1");
    Expect(il2cpp::runtime::icall_conv_i8_u4(123456789u) == 123456789LL,
           "conv.i8(123456789u) == 123456789");
    Expect(il2cpp::runtime::icall_conv_i8_u4(2147483648u) == 2147483648LL,
           "conv.i8(2147483648u) == 2147483648 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_i8_u4(4294967295u) == 4294967295LL,
           "conv.i8(4294967295u) == 4294967295, NOT -1 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u8_u4(0u) == 0ULL, "conv.u8(0u) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_u4(4294967295u) == 4294967295ULL,
           "conv.u8(4294967295u) == 4294967295 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u8_u4(2147483648u) == 2147483648ULL,
           "conv.u8(2147483648u) == 2147483648");

    // --- 16. double -> uint64, unchecked (conv.u8): x64 cvttsd2si sequence
    // (net48 EMPIRICAL). NaN / >= 2^64 -> 0; < -2^63 -> 2^63; [-2^63, 0)
    // wraps; [0, 2^64) truncates. ---
    Expect(il2cpp::runtime::icall_conv_u8_d(1.5) == 1ULL, "conv.u8(1.5) == 1");
    Expect(il2cpp::runtime::icall_conv_u8_d(0.0) == 0ULL, "conv.u8(0.0) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_d(-0.5) == 0ULL, "conv.u8(-0.5) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_d(-1.0) == 18446744073709551615ULL,
           "conv.u8(-1.0) == UINT64_MAX (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u8_d(-1.5) == 18446744073709551615ULL,
           "conv.u8(-1.5) == UINT64_MAX");
    Expect(il2cpp::runtime::icall_conv_u8_d(-2.0) == 18446744073709551614ULL,
           "conv.u8(-2.0) == UINT64_MAX - 1");
    Expect(il2cpp::runtime::icall_conv_u8_d(-1000.0) == 18446744073709550616ULL,
           "conv.u8(-1000.0) == 2^64 - 1000");
    Expect(il2cpp::runtime::icall_conv_u8_d(-2147483648.0) == 18446744071562067968ULL,
           "conv.u8(-2^31) == 2^64 - 2^31 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u8_d(4294967296.0) == 4294967296ULL,
           "conv.u8(2^32) == 2^32");
    Expect(il2cpp::runtime::icall_conv_u8_d(4294967295.0) == 4294967295ULL,
           "conv.u8(2^32 - 1) == 2^32 - 1");
    Expect(il2cpp::runtime::icall_conv_u8_d(9223372036854775808.0) == 9223372036854775808ULL,
           "conv.u8(2^63) == 2^63");
    Expect(il2cpp::runtime::icall_conv_u8_d(-9223372036854775808.0) == 9223372036854775808ULL,
           "conv.u8(-2^63) == 2^63 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u8_d(-9223372036854777856.0) == 9223372036854775808ULL,
           "conv.u8(-(2^63 + 2048)) == 2^63 (below -2^63, indefinite)");
    Expect(il2cpp::runtime::icall_conv_u8_d(-18446744073709551616.0) == 9223372036854775808ULL,
           "conv.u8(-2^64) == 2^63 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u8_d(18446744073709549568.0) == 18446744073709549568ULL,
           "conv.u8(2^64 - 2048) == 2^64 - 2048");
    Expect(il2cpp::runtime::icall_conv_u8_d(18446744073709551616.0) == 0ULL,
           "conv.u8(2^64) == 0 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u8_d(18446744073709555712.0) == 0ULL,
           "conv.u8(2^64 + 4096) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_d(1.0e300) == 0ULL, "conv.u8(1e300) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_d(-1.0e300) == 9223372036854775808ULL,
           "conv.u8(-1e300) == 2^63");
    Expect(il2cpp::runtime::icall_conv_u8_d(std::nan("")) == 0ULL, "conv.u8(NaN) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_d(INFINITY) == 0ULL, "conv.u8(+inf) == 0");
    Expect(il2cpp::runtime::icall_conv_u8_d(-INFINITY) == 9223372036854775808ULL,
           "conv.u8(-inf) == 2^63");

    // --- 17. double -> uint32, unchecked (conv.u4): the low 32 bits of the
    // int64 conversion (net48 EMPIRICAL) ---
    Expect(il2cpp::runtime::icall_conv_u4_d(2.9) == 2u, "conv.u4(2.9) == 2");
    Expect(il2cpp::runtime::icall_conv_u4_d(-0.5) == 0u, "conv.u4(-0.5) == 0");
    Expect(il2cpp::runtime::icall_conv_u4_d(-1.0) == 4294967295u,
           "conv.u4(-1.0) == 4294967295 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_d(-2.0) == 4294967294u,
           "conv.u4(-2.0) == 4294967294");
    Expect(il2cpp::runtime::icall_conv_u4_d(-2147483648.0) == 2147483648u,
           "conv.u4(-2^31) == 2^31 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_d(-2147483649.0) == 2147483647u,
           "conv.u4(-(2^31 + 1)) == 2^31 - 1 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_d(4294967295.0) == 4294967295u,
           "conv.u4(2^32 - 1) == 2^32 - 1");
    Expect(il2cpp::runtime::icall_conv_u4_d(4294967295.9) == 4294967295u,
           "conv.u4(2^32 - 1 + 0.9) == 2^32 - 1");
    Expect(il2cpp::runtime::icall_conv_u4_d(4294967296.0) == 0u,
           "conv.u4(2^32) == 0 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_u4_d(1.0e300) == 0u, "conv.u4(1e300) == 0");
    Expect(il2cpp::runtime::icall_conv_u4_d(std::nan("")) == 0u, "conv.u4(NaN) == 0");
    Expect(il2cpp::runtime::icall_conv_u4_d(INFINITY) == 0u, "conv.u4(+inf) == 0");

    // --- 18. uint32/uint64 -> double (conv.r8): exact / correctly rounded ---
    Expect(il2cpp::runtime::icall_conv_d_u4(0u) == 0.0, "conv.r8(0u) == 0");
    Expect(il2cpp::runtime::icall_conv_d_u4(4294967295u) == 4294967295.0,
           "conv.r8(uint.MaxValue) exact (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_d_u4(2147483648u) == 2147483648.0,
           "conv.r8(2^31u) exact");
    Expect(il2cpp::runtime::icall_conv_d_u8(0ULL) == 0.0, "conv.r8(0UL) == 0");
    Expect(il2cpp::runtime::icall_conv_d_u8(9007199254740992ULL) == 9007199254740992.0,
           "conv.r8(2^53) exact");
    Expect(il2cpp::runtime::icall_conv_d_u8(9007199254740993ULL) == 9007199254740992.0,
           "conv.r8(2^53 + 1) rounds down (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_d_u8(9007199254740995ULL) == 9007199254740996.0,
           "conv.r8(2^53 + 3) rounds up to even (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_d_u8(9223372036854775808ULL) == 9223372036854775808.0,
           "conv.r8(2^63) exact");
    Expect(il2cpp::runtime::icall_conv_d_u8(UINT64_MAX) == 18446744073709551616.0,
           "conv.r8(ulong.MaxValue) == 2^64 (net48 oracle)");

    // --- 19. int/uint/double -> float, RNE (conv.r4) ---
    Expect(il2cpp::runtime::icall_conv_r4_i4(0) == 0.0f, "conv.r4(0) == 0.0f");
    Expect(il2cpp::runtime::icall_conv_r4_i4(16777216) == 16777216.0f,
           "conv.r4(2^24) exact");
    Expect(il2cpp::runtime::icall_conv_r4_i4(16777217) == 16777216.0f,
           "conv.r4(2^24 + 1) == 2^24 (RNE, net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_r4_i4(16777218) == 16777218.0f,
           "conv.r4(2^24 + 2) exact");
    Expect(il2cpp::runtime::icall_conv_r4_i4(INT32_MAX) == 2147483648.0f,
           "conv.r4(INT32_MAX) == 2^31 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_r4_i4(-1) == -1.0f, "conv.r4(-1) == -1.0f");
    Expect(il2cpp::runtime::icall_conv_r4_u4(4294967295u) == 4294967296.0f,
           "conv.r4(uint.MaxValue) == 2^32 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_r4_i8(9007199254740993LL) == 9007199254740992.0f,
           "conv.r4(2^53 + 1) == 2^53 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_r4_u8(UINT64_MAX) == 18446744073709551616.0f,
           "conv.r4(ulong.MaxValue) == 2^64 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_r4_d(16777217.0) == 16777216.0f,
           "conv.r4((double)2^24 + 1) == 2^24 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_r4_d(16777218.0) == 16777218.0f,
           "conv.r4((double)2^24 + 2) == 2^24 + 2");
    Expect(il2cpp::runtime::icall_conv_r4_d(2.9) == 2.9f, "conv.r4(2.9) == 2.9f");
    Expect(std::isnan(il2cpp::runtime::icall_conv_r4_d(std::nan(""))),
           "conv.r4(NaN) is NaN");
    Expect(il2cpp::runtime::icall_conv_r4_d(-0.0) == -0.0f, "conv.r4(-0.0) == -0.0f");

    // --- 20. checked integer conversions (conv.ovf.*) ---
    Expect(il2cpp::runtime::icall_conv_ovf_u4_i8(0LL) == 0u, "conv.ovf.u4(0L) == 0");
    Expect(il2cpp::runtime::icall_conv_ovf_u4_i8(4294967295LL) == 4294967295u,
           "conv.ovf.u4(4294967295L) == 4294967295");
    ExpectDotNetException("conv.ovf.u4(4294967296L) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvU4I8(4294967296LL); });
    ExpectDotNetException("conv.ovf.u4(-1L) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvU4I8(-1LL); });
    Expect(il2cpp::runtime::icall_conv_ovf_u4_u8(4294967295ULL) == 4294967295u,
           "conv.ovf.u4(4294967295UL) == 4294967295");
    ExpectDotNetException("conv.ovf.u4(4294967296UL) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvU4U8(4294967296ULL); });
    Expect(il2cpp::runtime::icall_conv_ovf_i4_u8(2147483647ULL) == INT32_MAX,
           "conv.ovf.i4(2147483647UL) == INT32_MAX");
    ExpectDotNetException("conv.ovf.i4(2^31) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvI4U8(2147483648ULL); });
    Expect(il2cpp::runtime::icall_conv_ovf_i8_u8(9223372036854775807ULL) == INT64_MAX,
           "conv.ovf.i8(2^63 - 1) == INT64_MAX");
    ExpectDotNetException("conv.ovf.i8(2^63) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvI8U8(9223372036854775808ULL); });
    Expect(il2cpp::runtime::icall_conv_ovf_u8_i8(0LL) == 0ULL, "conv.ovf.u8(0L) == 0");
    Expect(il2cpp::runtime::icall_conv_ovf_u8_i8(123LL) == 123ULL, "conv.ovf.u8(123L) == 123");
    ExpectDotNetException("conv.ovf.u8(-1L) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvU8I8(-1LL); });
    Expect(il2cpp::runtime::icall_conv_ovf_u8_i4(0) == 0ULL, "conv.ovf.u8(0) == 0");
    Expect(il2cpp::runtime::icall_conv_ovf_u8_i4(123) == 123ULL, "conv.ovf.u8(123) == 123");
    ExpectDotNetException("conv.ovf.u8(-1) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvU8I4(-1); });
    Expect(il2cpp::runtime::icall_conv_ovf_u4_i4(0) == 0u, "conv.ovf.u4(0) == 0");
    Expect(il2cpp::runtime::icall_conv_ovf_u4_i4(2147483647) == 2147483647u,
           "conv.ovf.u4(2147483647) == 2147483647");
    ExpectDotNetException("conv.ovf.u4(-1) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvU4I4(-1); });

    // --- 21. checked double -> integer: the check is on the TRUNCATED result
    // (net48 empirical -- the fractional-boundary probe) ---
    Expect(il2cpp::runtime::icall_conv_ovf_i4_d(12345.0) == 12345,
           "conv.ovf.i4(12345.0) == 12345");
    Expect(il2cpp::runtime::icall_conv_ovf_i4_d(2147483647.0) == INT32_MAX,
           "conv.ovf.i4(2^31 - 1) == INT32_MAX");
    Expect(il2cpp::runtime::icall_conv_ovf_i4_d(-2147483648.0) == INT32_MIN,
           "conv.ovf.i4(-2^31) == INT32_MIN");
    Expect(il2cpp::runtime::icall_conv_ovf_i4_d(-2147483648.5) == INT32_MIN,
           "conv.ovf.i4(-2147483648.5) == INT32_MIN (truncation fits, NO throw -- net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_ovf_i4_d(2147483647.9) == INT32_MAX,
           "conv.ovf.i4(2147483647.9) == INT32_MAX");
    ExpectDotNetException("conv.ovf.i4(2^31) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfI4D(2147483648.0); });
    ExpectDotNetException("conv.ovf.i4(-2147483649.5) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfI4D(-2147483649.5); });
    ExpectDotNetException("conv.ovf.i4(NaN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfI4D(std::nan("")); });
    ExpectDotNetException("conv.ovf.i4(+inf) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfI4D(INFINITY); });
    Expect(il2cpp::runtime::icall_conv_ovf_i8_d(9223372036854774784.0) == 9223372036854774784LL,
           "conv.ovf.i8(2^63 - 1024) in range (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_ovf_i8_d(-9223372036854775808.0) == INT64_MIN,
           "conv.ovf.i8(-2^63) == INT64_MIN");
    ExpectDotNetException("conv.ovf.i8(2^63) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfI8D(9223372036854775808.0); });
    ExpectDotNetException("conv.ovf.i8(NaN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfI8D(std::nan("")); });
    Expect(il2cpp::runtime::icall_conv_ovf_u4_d(4294967295.0) == 4294967295u,
           "conv.ovf.u4(2^32 - 1) == 2^32 - 1");
    Expect(il2cpp::runtime::icall_conv_ovf_u4_d(4294967295.9) == 4294967295u,
           "conv.ovf.u4(2^32 - 1 + 0.9) == 2^32 - 1");
    Expect(il2cpp::runtime::icall_conv_ovf_u4_d(-0.5) == 0u,
           "conv.ovf.u4(-0.5) == 0 (truncation 0, NO throw -- net48 oracle)");
    ExpectDotNetException("conv.ovf.u4(2^32) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfU4D(4294967296.0); });
    ExpectDotNetException("conv.ovf.u4(-1.0) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfU4D(-1.0); });
    ExpectDotNetException("conv.ovf.u4(NaN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfU4D(std::nan("")); });
    Expect(il2cpp::runtime::icall_conv_ovf_u8_d(4294967295.0) == 4294967295ULL,
           "conv.ovf.u8(2^32 - 1) == 2^32 - 1");
    Expect(il2cpp::runtime::icall_conv_ovf_u8_d(9223372036854775808.0) == 9223372036854775808ULL,
           "conv.ovf.u8(2^63) == 2^63");
    Expect(il2cpp::runtime::icall_conv_ovf_u8_d(18446744073709549568.0) == 18446744073709549568ULL,
           "conv.ovf.u8(2^64 - 2048) == 2^64 - 2048 (net48 oracle)");
    Expect(il2cpp::runtime::icall_conv_ovf_u8_d(-0.5) == 0ULL,
           "conv.ovf.u8(-0.5) == 0 (truncation 0, NO throw -- net48 oracle)");
    ExpectDotNetException("conv.ovf.u8(2^64) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfU8D(18446744073709551616.0); });
    ExpectDotNetException("conv.ovf.u8(-1.0) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfU8D(-1.0); });
    ExpectDotNetException("conv.ovf.u8(NaN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "overflow",
                          [] { (void)NoThrowConvOvfU8D(std::nan("")); });

    // --- Round trips ---
    Expect(il2cpp::runtime::icall_conv_i8_i4(
               il2cpp::runtime::icall_conv_i4_i8(static_cast<int64_t>(INT32_MIN))) == INT32_MIN,
           "round trip i8_i4 o i4_i8 (INT32_MIN)");
    Expect(il2cpp::runtime::icall_conv_i8_i4(
               il2cpp::runtime::icall_conv_i4_i8(static_cast<int64_t>(INT32_MAX))) == INT32_MAX,
           "round trip i8_i4 o i4_i8 (INT32_MAX)");
    Expect(il2cpp::runtime::icall_conv_i4_u4(il2cpp::runtime::icall_conv_u4_i4(-987654321)) == -987654321,
           "round trip u4_i4 o i4_u4");
    Expect(il2cpp::runtime::icall_conv_i8_u8(il2cpp::runtime::icall_conv_u8_i8(-987654321987654321LL)) == -987654321987654321LL,
           "round trip u8_i8 o i8_u8");
    Expect(il2cpp::runtime::icall_conv_d_i4(il2cpp::runtime::icall_conv_i4_d(1234567.89)) == 1234567.0,
           "round trip d_i4 o i4_d (truncated)");
    Expect(il2cpp::runtime::icall_conv_d_i8(il2cpp::runtime::icall_conv_i8_d(1234567.89)) == 1234567.0,
           "round trip d_i8 o i8_d (truncated)");
    Expect(il2cpp::runtime::icall_conv_i8_d(il2cpp::runtime::icall_conv_d_i8(-123456789LL)) == -123456789.0,
           "round trip i8_d o d_i8 (exact)");
    Expect(il2cpp::runtime::icall_conv_i4_d(il2cpp::runtime::icall_conv_d_i4(-2147483647)) == -2147483647.0,
            "round trip i4_d o d_i4 (exact)");

    // --- B7: double -> double identity (Convert.ToDouble(float)) ---
    // The float source rides an already-widened double slot, so the
    // conversion is a bit-exact pass-through (no rounding, no throw).
    Expect(il2cpp::runtime::icall_conv_d_d(0.0) == 0.0, "conv.d.d(0.0) == 0.0");
    Expect(il2cpp::runtime::icall_conv_d_d(1.5) == 1.5, "conv.d.d(1.5) == 1.5");
    Expect(il2cpp::runtime::icall_conv_d_d(-0.1) == -0.1,
            "conv.d.d(-0.1) == -0.1 (bit-exact, no rounding)");
    const double kD = il2cpp::runtime::icall_conv_d_d(0.1);
    Expect(il2cpp::runtime::icall_conv_d_d(kD) == kD,
            "conv.d.d is idempotent (round trip)");
    // B7: conv.r4.u4 zero-extends the uint32 pattern (uint >= 2^31 stays
    // positive -- a sign-extending route would give a negative float).
    Expect(il2cpp::runtime::icall_conv_r4_u4(0xFFFFFFFFu) == static_cast<float>(4294967295u),
            "conv.r4.u4(uint.MaxValue) == (float)4294967295u (positive, zero-extended)");
    Expect(il2cpp::runtime::icall_conv_r4_u4(0x80000000u) == static_cast<float>(2147483648u),
            "conv.r4.u4(0x80000000) == (float)2147483648u (positive, zero-extended)");


    // --- D-I64D-1: int64 unchecked multiply wraps mod 2^64 ---
    // il2cpp_unchecked_mul_64 (IL mul on i8 operands, the C# unchecked
    // context): the raw C++ `a * b` is signed-overflow UB on int64 overflow;
    // the helper's unsigned intermediate defines the wrap (net48 wrap oracle).
    Expect(il2cpp::il2cpp_unchecked_mul_64(0LL, 0LL) == 0LL,
           "unchecked_mul_64(0, 0) == 0");
    Expect(il2cpp::il2cpp_unchecked_mul_64(0LL, INT64_MAX) == 0LL,
           "unchecked_mul_64(0, Max) == 0");
    Expect(il2cpp::il2cpp_unchecked_mul_64(INT64_MAX, 2LL) == -2LL,
           "unchecked_mul_64(Max, 2) == -2 (wrap)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(INT64_MIN, 2LL) == 0LL,
           "unchecked_mul_64(Min, 2) == 0 (wrap -- D-I64D-1 UB case)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(INT64_MIN, -1LL) == INT64_MIN,
           "unchecked_mul_64(Min, -1) == Min (wrap)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(INT64_MAX, -1LL) == -INT64_MAX,
           "unchecked_mul_64(Max, -1) == -Max (exact)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(INT64_MAX, INT64_MAX) == 1LL,
           "unchecked_mul_64(Max, Max) == 1 (wrap)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(INT64_MIN, INT64_MIN) == 0LL,
           "unchecked_mul_64(Min, Min) == 0 (wrap)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(INT64_MIN, INT64_MAX) == INT64_MIN,
           "unchecked_mul_64(Min, Max) == Min (wrap)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(4611686018427387904LL, 4LL) == 0LL,
           "unchecked_mul_64(2^62, 4) == 0 (wrap: 2^64)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(4611686018427387904LL, 3LL) == -4611686018427387904LL,
           "unchecked_mul_64(2^62, 3) == -2^62 (0xC000000000000000 wrap)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(4294967296LL, 4294967296LL) == 0LL,
           "unchecked_mul_64(2^32, 2^32) == 0 (wrap: 2^64)");
    Expect(il2cpp::il2cpp_unchecked_mul_64(123456789LL, 987654321LL) == 121932631112635269LL,
           "unchecked_mul_64(123456789, 987654321) exact in-range product");
    Expect(il2cpp::il2cpp_unchecked_mul_64(-3LL, 4LL) == -12LL,
           "unchecked_mul_64(-3, 4) == -12");
    Expect(il2cpp::il2cpp_unchecked_mul_64(7LL, -7LL) == -49LL,
           "unchecked_mul_64(7, -7) == -49");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d conv assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime conv tests passed\n");
    return 0;
}
