//------------------------------------------------------------------------------
// test_math.cpp -- System.Math icall runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in il2cpp_math.h:
//   1. icall_math_abs_i / icall_math_abs_l / icall_math_abs_sb /
//      icall_math_abs_sh: magnitudes, zero, int32/int64/sbyte/short extrema;
//      the MINIMUM value throws OverflowException (net48 message).
//   2. icall_math_max_i / icall_math_min_i (and _l/_u/_ul): both orders,
//      negatives, ties (second operand wins), signed/unsigned extrema.
//   3. icall_math_sign_i / icall_math_sign_l / icall_math_sign_d: -1/0/1;
//      the integer minimum value yields -1 and never throws (unlike Abs);
//      int64 input still yields int32; Sign(double.NaN) throws
//      ArithmeticException (net48).
//   4. icall_math_clamp_i: in-range passthrough, below-min, above-max,
//      min == value, max == value, min == max; min > max throws
//      ArgumentException.
//   5. icall_math_sqrt_d: exact squares, zero, negative -> NaN, NaN -> NaN.
//   6. icall_math_pow_d: 2^10, 2^-1, 0^0 == 1, 1^x == 1; net48 NaN
//      propagation (Pow(NaN, 0) == NaN, Pow(1, NaN) == NaN,
//      Pow(-1, +/-inf) == NaN).
//   7. icall_math_floor_d / icall_math_ceiling_d: positive/negative operands
//      (floor(-3.7) == -4, ceiling(-3.2) == -3), integral passthrough;
//      Ceiling(-0.5) == +0.0 (net48 floor+increment, signbit cleared).
//   8. icall_math_round_d: banker's rounding (ToEven) -- 0.5 -> 0,
//      1.5 -> 2, 2.5 -> 2, 3.5 -> 4, -0.5 -> 0, -1.5 -> -2, -2.5 -> -2;
//      non-halves round to the nearer integer; NaN/Infinity passthrough.
//   9. icall_math_abs_d: magnitude, NaN -> NaN, -0.0 -> +0.0 (signbit).
//  10. icall_math_max_d / icall_math_min_d: net48 reference-source contract
//      -- a NaN FIRST operand is returned (Max(NaN,5)==NaN, Max(5,NaN)==NaN),
//      and signed-zero ties return the SECOND operand
//      (Max(-0.0,+0.0)==+0.0, Min(+0.0,-0.0)==-0.0).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_math.h"
#include "il2cpp_exception.h"

#include "il2cpp_math_c99.h" // GCC 8.1 libstdc++ C99-math <cmath> shim (std::nan)

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
int32_t NoThrowAbsI(int32_t v) { return il2cpp::runtime::icall_math_abs_i(v); }
int64_t NoThrowAbsL(int64_t v) { return il2cpp::runtime::icall_math_abs_l(v); }
int32_t NoThrowAbsSb(int32_t v) { return il2cpp::runtime::icall_math_abs_sb(v); }
int32_t NoThrowAbsSh(int32_t v) { return il2cpp::runtime::icall_math_abs_sh(v); }
int32_t NoThrowClampI(int32_t v, int32_t lo, int32_t hi) { return il2cpp::runtime::icall_math_clamp_i(v, lo, hi); }
int32_t NoThrowSignD(double v) { return il2cpp::runtime::icall_math_sign_d(v); }

// Bit-level double probe (signbit-safe; the ternary folds in some compilers
// would otherwise hide a -0.0 vs +0.0 difference).
uint64_t DoubleBits(double v)
{
    uint64_t bits = 0;
    std::memcpy(&bits, &v, sizeof(bits));
    return bits;
}

} // namespace

int main()
{
    // --- 1a. Abs(int32) ---
    Expect(il2cpp::runtime::icall_math_abs_i(5) == 5, "abs_i(5) == 5");
    Expect(il2cpp::runtime::icall_math_abs_i(-5) == 5, "abs_i(-5) == 5");
    Expect(il2cpp::runtime::icall_math_abs_i(0) == 0, "abs_i(0) == 0");
    Expect(il2cpp::runtime::icall_math_abs_i(INT32_MAX) == INT32_MAX,
           "abs_i(INT32_MAX) == INT32_MAX");
    ExpectDotNetException("abs_i(INT32_MIN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "\xE5\xAF\xB9", // net48 zh-CN "Negating the minimum..." message prefix (UTF-8 "对")
                          [] { (void)NoThrowAbsI(INT32_MIN); });

    // --- 1b. Abs(int64) ---
    Expect(il2cpp::runtime::icall_math_abs_l(7LL) == 7LL, "abs_l(7) == 7");
    Expect(il2cpp::runtime::icall_math_abs_l(-7LL) == 7LL, "abs_l(-7) == 7");
    Expect(il2cpp::runtime::icall_math_abs_l(0LL) == 0LL, "abs_l(0) == 0");
    Expect(il2cpp::runtime::icall_math_abs_l(INT64_MAX) == INT64_MAX,
           "abs_l(INT64_MAX) == INT64_MAX");
    ExpectDotNetException("abs_l(INT64_MIN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "\xE5\xAF\xB9", // net48 zh-CN "Negating the minimum..." message prefix (UTF-8 "对")
                          [] { (void)NoThrowAbsL(INT64_MIN); });

    // --- 1c. Abs(sbyte) / Abs(short): the net48 narrow overloads sign-extend
    // to int32, take the magnitude, and throw the SAME OverflowException for
    // their minimum values (mathabs1 oracle). ---
    Expect(il2cpp::runtime::icall_math_abs_sb(-127) == 127, "abs_sb(-127) == 127");
    Expect(il2cpp::runtime::icall_math_abs_sb(127) == 127, "abs_sb(127) == 127");
    Expect(il2cpp::runtime::icall_math_abs_sb(0) == 0, "abs_sb(0) == 0");
    ExpectDotNetException("abs_sb(INT8_MIN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "\xE5\xAF\xB9", // net48 zh-CN "Negating the minimum..." message prefix (UTF-8 "对")
                          [] { (void)NoThrowAbsSb(INT8_MIN); });
    Expect(il2cpp::runtime::icall_math_abs_sh(-32767) == 32767, "abs_sh(-32767) == 32767");
    Expect(il2cpp::runtime::icall_math_abs_sh(32767) == 32767, "abs_sh(32767) == 32767");
    Expect(il2cpp::runtime::icall_math_abs_sh(0) == 0, "abs_sh(0) == 0");
    ExpectDotNetException("abs_sh(INT16_MIN) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "\xE5\xAF\xB9", // net48 zh-CN "Negating the minimum..." message prefix (UTF-8 "对")
                          [] { (void)NoThrowAbsSh(INT16_MIN); });

    // --- 2a. Max/Min(int32) ---
    Expect(il2cpp::runtime::icall_math_max_i(3, 7) == 7, "max_i(3,7) == 7");
    Expect(il2cpp::runtime::icall_math_max_i(7, 3) == 7, "max_i(7,3) == 7");
    Expect(il2cpp::runtime::icall_math_min_i(3, 7) == 3, "min_i(3,7) == 3");
    Expect(il2cpp::runtime::icall_math_min_i(7, 3) == 3, "min_i(7,3) == 3");
    Expect(il2cpp::runtime::icall_math_max_i(-5, -3) == -3, "max_i(-5,-3) == -3");
    Expect(il2cpp::runtime::icall_math_min_i(-5, -3) == -5, "min_i(-5,-3) == -5");
    Expect(il2cpp::runtime::icall_math_max_i(4, 4) == 4, "max_i tie returns the value");
    Expect(il2cpp::runtime::icall_math_min_i(4, 4) == 4, "min_i tie returns the value");
    Expect(il2cpp::runtime::icall_math_max_i(INT32_MIN, INT32_MAX) == INT32_MAX,
           "max_i extrema");
    Expect(il2cpp::runtime::icall_math_min_i(INT32_MIN, INT32_MAX) == INT32_MIN,
           "min_i extrema");

    // --- 2b. Max/Min(int64) ---
    Expect(il2cpp::runtime::icall_math_max_l(3LL, 7LL) == 7LL, "max_l(3,7) == 7");
    Expect(il2cpp::runtime::icall_math_max_l(7LL, 3LL) == 7LL, "max_l(7,3) == 7");
    Expect(il2cpp::runtime::icall_math_min_l(3LL, 7LL) == 3LL, "min_l(3,7) == 3");
    Expect(il2cpp::runtime::icall_math_min_l(7LL, 3LL) == 3LL, "min_l(7,3) == 3");
    Expect(il2cpp::runtime::icall_math_max_l(-5LL, -3LL) == -3LL, "max_l(-5,-3) == -3");
    Expect(il2cpp::runtime::icall_math_min_l(-5LL, -3LL) == -5LL, "min_l(-5,-3) == -5");
    Expect(il2cpp::runtime::icall_math_max_l(INT64_MIN, INT64_MAX) == INT64_MAX,
           "max_l extrema");
    Expect(il2cpp::runtime::icall_math_min_l(INT64_MIN, INT64_MAX) == INT64_MIN,
           "min_l extrema");

    // --- 2c. Max/Min(uint32/uint64): UNSIGNED comparison on the int32/int64
    // bit-pattern carriers (net48: Max(0u, uint.MaxValue) == uint.MaxValue). ---
    Expect(il2cpp::runtime::icall_math_max_u(0, -1) == -1,
           "max_u(0, UINT32_MAX) == UINT32_MAX (unsigned)");
    Expect(il2cpp::runtime::icall_math_min_u(0, -1) == 0,
           "min_u(0, UINT32_MAX) == 0 (unsigned)");
    Expect(il2cpp::runtime::icall_math_max_ul(0LL, -1LL) == -1LL,
           "max_ul(0, UINT64_MAX) == UINT64_MAX (unsigned)");
    Expect(il2cpp::runtime::icall_math_min_ul(0LL, -1LL) == 0LL,
           "min_ul(0, UINT64_MAX) == 0 (unsigned)");

    // --- 3a. Sign(int32): -1 / 0 / 1; the minimum value yields -1, NOT an
    // exception (Sign differs from Abs here). ---
    Expect(il2cpp::runtime::icall_math_sign_i(-9) == -1, "sign_i(-9) == -1");
    Expect(il2cpp::runtime::icall_math_sign_i(0) == 0, "sign_i(0) == 0");
    Expect(il2cpp::runtime::icall_math_sign_i(9) == 1, "sign_i(9) == 1");
    Expect(il2cpp::runtime::icall_math_sign_i(INT32_MIN) == -1,
           "sign_i(INT32_MIN) == -1 (no overflow)");
    Expect(il2cpp::runtime::icall_math_sign_i(INT32_MAX) == 1,
           "sign_i(INT32_MAX) == 1");

    // --- 3b. Sign(int64): same contract, int64 input ---
    Expect(il2cpp::runtime::icall_math_sign_l(-9LL) == -1, "sign_l(-9) == -1");
    Expect(il2cpp::runtime::icall_math_sign_l(0LL) == 0, "sign_l(0) == 0");
    Expect(il2cpp::runtime::icall_math_sign_l(9LL) == 1, "sign_l(9) == 1");
    Expect(il2cpp::runtime::icall_math_sign_l(INT64_MIN) == -1,
           "sign_l(INT64_MIN) == -1 (no overflow)");
    Expect(il2cpp::runtime::icall_math_sign_l(INT64_MAX) == 1,
           "sign_l(INT64_MAX) == 1");

    // --- 3c. Sign(double): fraction sign (not truncated), signed zero -> 0,
    // NaN -> ArithmeticException (net48 reference source). ---
    Expect(il2cpp::runtime::icall_math_sign_d(-2.5) == -1, "sign_d(-2.5) == -1");
    Expect(il2cpp::runtime::icall_math_sign_d(-0.5) == -1, "sign_d(-0.5) == -1");
    Expect(il2cpp::runtime::icall_math_sign_d(0.0) == 0, "sign_d(+0.0) == 0");
    Expect(il2cpp::runtime::icall_math_sign_d(-0.0) == 0, "sign_d(-0.0) == 0");
    Expect(il2cpp::runtime::icall_math_sign_d(2.5) == 1, "sign_d(2.5) == 1");
    ExpectDotNetException("sign_d(NaN) throws ArithmeticException",
                          "System.ArithmeticException,System.ArithmeticException,System.SystemException,System.Exception",
                          "Not-a-Number",
                          [] { (void)NoThrowSignD(std::nan("")); });

    // --- 4. Clamp(int32): in-range, below-min, above-max, boundary ties,
    // min == max, and the min > max error path. ---
    Expect(il2cpp::runtime::icall_math_clamp_i(5, 1, 10) == 5, "clamp_i in-range passthrough");
    Expect(il2cpp::runtime::icall_math_clamp_i(0, 1, 10) == 1, "clamp_i below min -> min");
    Expect(il2cpp::runtime::icall_math_clamp_i(12, 1, 10) == 10, "clamp_i above max -> max");
    Expect(il2cpp::runtime::icall_math_clamp_i(1, 1, 10) == 1, "clamp_i value == min");
    Expect(il2cpp::runtime::icall_math_clamp_i(10, 1, 10) == 10, "clamp_i value == max");
    Expect(il2cpp::runtime::icall_math_clamp_i(7, 7, 7) == 7, "clamp_i min == max");
    Expect(il2cpp::runtime::icall_math_clamp_i(INT32_MIN, INT32_MIN, INT32_MAX) == INT32_MIN,
           "clamp_i full-range lower bound");
    Expect(il2cpp::runtime::icall_math_clamp_i(INT32_MAX, INT32_MIN, INT32_MAX) == INT32_MAX,
           "clamp_i full-range upper bound");
    ExpectDotNetException("clamp_i(min > max) throws ArgumentException",
                          "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception",
                          "greater",
                          [] { (void)NoThrowClampI(5, 10, 1); });

    // --- 5. Sqrt(double): exact squares, zero, negative -> NaN (net48:
    // no exception), NaN passthrough. ---
    Expect(il2cpp::runtime::icall_math_sqrt_d(9.0) == 3.0, "sqrt(9) == 3");
    Expect(il2cpp::runtime::icall_math_sqrt_d(4.0) == 2.0, "sqrt(4) == 2");
    Expect(il2cpp::runtime::icall_math_sqrt_d(0.0) == 0.0, "sqrt(0) == 0");
    const double sqrtNeg = il2cpp::runtime::icall_math_sqrt_d(-1.0);
    Expect(sqrtNeg != sqrtNeg, "sqrt(-1) is NaN (net48 contract)");
    const double sqrtNaN = il2cpp::runtime::icall_math_sqrt_d(std::nan(""));
    Expect(sqrtNaN != sqrtNaN, "sqrt(NaN) is NaN");
    const double sqrtInf = il2cpp::runtime::icall_math_sqrt_d(INFINITY);
    Expect(sqrtInf == INFINITY, "sqrt(+inf) is +inf");

    // --- 6. Pow(double,double): exact powers and edge values. ---
    Expect(il2cpp::runtime::icall_math_pow_d(2.0, 10.0) == 1024.0, "pow(2,10) == 1024");
    Expect(il2cpp::runtime::icall_math_pow_d(2.0, -1.0) == 0.5, "pow(2,-1) == 0.5");
    Expect(il2cpp::runtime::icall_math_pow_d(0.0, 0.0) == 1.0, "pow(0,0) == 1 (net48)");
    Expect(il2cpp::runtime::icall_math_pow_d(1.0, 12345.0) == 1.0, "pow(1, any) == 1");
    Expect(il2cpp::runtime::icall_math_pow_d(10.0, 0.0) == 1.0, "pow(10,0) == 1");
    // net48 NaN propagation: the reference source checks NaN BEFORE the
    // y==0 / x==1 shortcuts, and rejects x==-1 with an infinite exponent.
    const double powNaN0 = il2cpp::runtime::icall_math_pow_d(std::nan(""), 0.0);
    Expect(powNaN0 != powNaN0, "pow(NaN,0) is NaN (net48, not IEEE 1)");
    const double pow1NaN = il2cpp::runtime::icall_math_pow_d(1.0, std::nan(""));
    Expect(pow1NaN != pow1NaN, "pow(1,NaN) is NaN (net48)");
    const double powNeg1Inf = il2cpp::runtime::icall_math_pow_d(-1.0, INFINITY);
    Expect(powNeg1Inf != powNeg1Inf, "pow(-1,+inf) is NaN (net48)");
    const double powNeg1NegInf = il2cpp::runtime::icall_math_pow_d(-1.0, -INFINITY);
    Expect(powNeg1NegInf != powNeg1NegInf, "pow(-1,-inf) is NaN (net48)");
    // net48 delegates Pow to the Windows CRT pow (not MinGW's libmsvcrt
    // exp/log emulation): these two oracle values differ by 1 ulp from
    // glibc/std::pow and pin the CRT delegation on Windows builds.
#if defined(_WIN32)
    Expect(DoubleBits(il2cpp::runtime::icall_math_pow_d(10.0, 23.0)) == 0x44B52D02C7E14AF7ULL,
           "pow(10,23) matches the net48 CRT value");
    Expect(DoubleBits(il2cpp::runtime::icall_math_pow_d(1.5, 1000.0)) == 0x647F2DD011353699ULL,
           "pow(1.5,1000) matches the net48 CRT value");
#endif

    // --- 7. Floor / Ceiling: positive and negative operands. ---
    Expect(il2cpp::runtime::icall_math_floor_d(3.7) == 3.0, "floor(3.7) == 3");
    Expect(il2cpp::runtime::icall_math_floor_d(-3.7) == -4.0, "floor(-3.7) == -4");
    Expect(il2cpp::runtime::icall_math_floor_d(3.0) == 3.0, "floor(3.0) == 3");
    Expect(il2cpp::runtime::icall_math_ceiling_d(3.2) == 4.0, "ceiling(3.2) == 4");
    Expect(il2cpp::runtime::icall_math_ceiling_d(-3.2) == -3.0, "ceiling(-3.2) == -3");
    Expect(il2cpp::runtime::icall_math_ceiling_d(3.0) == 3.0, "ceiling(3.0) == 3");
    // net48 Ceiling is floor(x) + (floor(x) != x ? 1 : 0): a negative input
    // that rounds up to zero yields +0.0, NOT std::ceil's -0.0.
    Expect(DoubleBits(il2cpp::runtime::icall_math_ceiling_d(-0.5)) == 0,
           "ceiling(-0.5) == +0.0 (net48 floor+increment)");
    Expect(DoubleBits(il2cpp::runtime::icall_math_ceiling_d(-0.0)) == 0x8000000000000000ULL,
           "ceiling(-0.0) == -0.0 (integral passthrough)");

    // --- 8. Round(double): banker's rounding (ToEven) -- the .NET default.
    // Halves: 0.5 -> 0, 1.5 -> 2, 2.5 -> 2, 3.5 -> 4 (even neighbour);
    // -0.5 -> 0, -1.5 -> -2, -2.5 -> -2. Non-halves: nearest integer. ---
    Expect(il2cpp::runtime::icall_math_round_d(0.5) == 0.0, "round(0.5) == 0 (to even)");
    Expect(il2cpp::runtime::icall_math_round_d(1.5) == 2.0, "round(1.5) == 2 (to even)");
    Expect(il2cpp::runtime::icall_math_round_d(2.5) == 2.0, "round(2.5) == 2 (to even)");
    Expect(il2cpp::runtime::icall_math_round_d(3.5) == 4.0, "round(3.5) == 4 (to even)");
    Expect(il2cpp::runtime::icall_math_round_d(-0.5) == 0.0, "round(-0.5) == 0 (to even)");
    Expect(il2cpp::runtime::icall_math_round_d(-1.5) == -2.0, "round(-1.5) == -2 (to even)");
    Expect(il2cpp::runtime::icall_math_round_d(-2.5) == -2.0, "round(-2.5) == -2 (to even)");
    Expect(il2cpp::runtime::icall_math_round_d(1.4) == 1.0, "round(1.4) == 1");
    Expect(il2cpp::runtime::icall_math_round_d(1.6) == 2.0, "round(1.6) == 2");
    Expect(il2cpp::runtime::icall_math_round_d(-1.4) == -1.0, "round(-1.4) == -1");
    Expect(il2cpp::runtime::icall_math_round_d(1000000000000000.5) == 1000000000000000.0,
           "round(large half) keeps even magnitude");
    const double roundNaN = il2cpp::runtime::icall_math_round_d(std::nan(""));
    Expect(roundNaN != roundNaN, "round(NaN) is NaN");
    Expect(il2cpp::runtime::icall_math_round_d(INFINITY) == INFINITY, "round(+inf) is +inf");

    // --- 9. Abs(double): magnitude, NaN, -0.0 -> +0.0 (signbit). ---
    Expect(il2cpp::runtime::icall_math_abs_d(2.5) == 2.5, "abs_d(2.5) == 2.5");
    Expect(il2cpp::runtime::icall_math_abs_d(-2.5) == 2.5, "abs_d(-2.5) == 2.5");
    const double absNaN = il2cpp::runtime::icall_math_abs_d(std::nan(""));
    Expect(absNaN != absNaN, "abs_d(NaN) is NaN");
    Expect(!std::signbit(il2cpp::runtime::icall_math_abs_d(-0.0)),
           "abs_d(-0.0) == +0.0 (signbit cleared)");

    // --- 10. Max/Min(double): the net48 Reference Source contract:
    //     Max(a,b) { if (a > b) return a; if (IsNaN(a)) return a; return b; }
    //     Min(a,b) { if (a < b) return a; if (IsNaN(a)) return a; return b; }
    // A NaN FIRST operand is returned; a NaN second operand is returned by
    // the fallthrough. Ties (including signed zeros) return the SECOND
    // operand -- Max(-0.0,+0.0)==+0.0, Min(+0.0,-0.0)==-0.0. ---
    Expect(il2cpp::runtime::icall_math_max_d(1.5, 2.5) == 2.5, "max_d(1.5,2.5) == 2.5");
    Expect(il2cpp::runtime::icall_math_max_d(2.5, 1.5) == 2.5, "max_d(2.5,1.5) == 2.5");
    Expect(il2cpp::runtime::icall_math_min_d(1.5, 2.5) == 1.5, "min_d(1.5,2.5) == 1.5");
    Expect(il2cpp::runtime::icall_math_min_d(2.5, 1.5) == 1.5, "min_d(2.5,1.5) == 1.5");
    const double nanValue = std::nan("");
    const double maxFirstNaN = il2cpp::runtime::icall_math_max_d(nanValue, 5.0);
    Expect(maxFirstNaN != maxFirstNaN, "max_d(NaN,5) is NaN (net48 returns a NaN first operand)");
    const double maxSecondNaN = il2cpp::runtime::icall_math_max_d(5.0, nanValue);
    Expect(maxSecondNaN != maxSecondNaN, "max_d(5,NaN) is NaN (fallthrough returns b)");
    const double minFirstNaN = il2cpp::runtime::icall_math_min_d(nanValue, 5.0);
    Expect(minFirstNaN != minFirstNaN, "min_d(NaN,5) is NaN (net48 returns a NaN first operand)");
    const double minSecondNaN = il2cpp::runtime::icall_math_min_d(5.0, nanValue);
    Expect(minSecondNaN != minSecondNaN, "min_d(5,NaN) is NaN (fallthrough returns b)");
    // Bit-level signed-zero assertions (value == 0 cannot distinguish the
    // signs; the icalls are out-of-line so no inlining fold can hide them).
    Expect(DoubleBits(il2cpp::runtime::icall_math_max_d(-0.0, 0.0)) == 0,
           "max_d(-0.0,+0.0) == +0.0 (tie returns second operand)");
    Expect(DoubleBits(il2cpp::runtime::icall_math_max_d(0.0, -0.0)) == 0x8000000000000000ULL,
           "max_d(+0.0,-0.0) == -0.0 (tie returns second operand)");
    Expect(DoubleBits(il2cpp::runtime::icall_math_min_d(0.0, -0.0)) == 0x8000000000000000ULL,
           "min_d(+0.0,-0.0) == -0.0 (tie returns second operand)");
    Expect(DoubleBits(il2cpp::runtime::icall_math_min_d(-0.0, 0.0)) == 0,
           "min_d(-0.0,+0.0) == +0.0 (tie returns second operand)");

    // --- 11. DivRem / DivModInt: net48 public API + corert internal helpers.
    // C# truncating division: quotient toward zero, remainder takes the
    // dividend sign (net48 divremprobe oracle: DivRem(-7,3) -> q -2 r -1,
    // DivRem(7,-3) -> q -2 r +1). div/mod by zero -> DivideByZeroException;
    // the signed min/-1 pair -> OverflowException (C++ / would be UB).
    {
        int32_t rem = 0;
        Expect(il2cpp::runtime::icall_math_divrem_i(7, 3, &rem) == 2 && rem == 1,
               "divrem_i(7,3) == q2 r1");
        Expect(il2cpp::runtime::icall_math_divrem_i(-7, 3, &rem) == -2 && rem == -1,
               "divrem_i(-7,3) == q-2 r-1 (truncate toward zero)");
        Expect(il2cpp::runtime::icall_math_divrem_i(7, -3, &rem) == -2 && rem == 1,
               "divrem_i(7,-3) == q-2 r+1");
        Expect(il2cpp::runtime::icall_math_divrem_i(-7, -3, &rem) == 2 && rem == -1,
               "divrem_i(-7,-3) == q2 r-1");
        Expect(il2cpp::runtime::icall_math_divrem_i(0, 5, &rem) == 0 && rem == 0,
               "divrem_i(0,5) == q0 r0");
        Expect(il2cpp::runtime::icall_math_divrem_i(INT32_MAX, 2, &rem) == 1073741823 && rem == 1,
               "divrem_i(INT32_MAX,2) == q1073741823 r1");

        int64_t remL = 0;
        Expect(il2cpp::runtime::icall_math_divrem_l(7LL, 3LL, &remL) == 2LL && remL == 1LL,
               "divrem_l(7,3) == q2 r1");
        Expect(il2cpp::runtime::icall_math_divrem_l(-7LL, 3LL, &remL) == -2LL && remL == -1LL,
               "divrem_l(-7,3) == q-2 r-1");
        Expect(il2cpp::runtime::icall_math_divrem_l(-9223372036854775807LL, 2LL, &remL)
                   == -4611686018427387903LL && remL == -1LL,
               "divrem_l(long.min+1,2) == q-4611686018427387903 r-1");
    }
    ExpectDotNetException("divrem_i(5,0) throws DivideByZeroException",
                          "System.DivideByZeroException,System.DivideByZeroException,System.ArithmeticException,System.SystemException,System.Exception",
                          "divide by zero",
                          [] { int32_t r; (void)il2cpp::runtime::icall_math_divrem_i(5, 0, &r); });
    ExpectDotNetException("divrem_i(INT32_MIN,-1) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "Arithmetic operation resulted in an overflow",
                          [] { int32_t r; (void)il2cpp::runtime::icall_math_divrem_i(INT32_MIN, -1, &r); });
    ExpectDotNetException("divrem_l(INT64_MIN,-1) throws OverflowException",
                          "System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                          "Arithmetic operation resulted in an overflow",
                          [] { int64_t r; (void)il2cpp::runtime::icall_math_divrem_l(INT64_MIN, -1LL, &r); });

    // corert DivModInt internals (div/mod for the four widths).
    Expect(il2cpp::runtime::icall_math_div_i(7, 3) == 2, "div_i(7,3) == 2");
    Expect(il2cpp::runtime::icall_math_div_i(-7, 3) == -2, "div_i(-7,3) == -2");
    Expect(il2cpp::runtime::icall_math_mod_i(7, 3) == 1, "mod_i(7,3) == 1");
    Expect(il2cpp::runtime::icall_math_mod_i(-7, 3) == -1, "mod_i(-7,3) == -1");
    Expect(il2cpp::runtime::icall_math_div_u(7u, 3u) == 2u, "div_u(7,3) == 2");
    Expect(il2cpp::runtime::icall_math_mod_u(7u, 3u) == 1u, "mod_u(7,3) == 1");
    Expect(il2cpp::runtime::icall_math_div_l(7LL, 3LL) == 2LL, "div_l(7,3) == 2");
    Expect(il2cpp::runtime::icall_math_mod_l(-7LL, 3LL) == -1LL, "mod_l(-7,3) == -1");
    Expect(il2cpp::runtime::icall_math_div_ul(7ULL, 3ULL) == 2ULL, "div_ul(7,3) == 2");
    Expect(il2cpp::runtime::icall_math_mod_ul(7ULL, 3ULL) == 1ULL, "mod_ul(7,3) == 1");
    Expect(il2cpp::runtime::icall_math_mod_u(UINT32_MAX, 7u) == (UINT32_MAX % 7u), "mod_u(UINT32_MAX,7) == UINT32_MAX%7");
    ExpectDotNetException("div_u(1,0) throws DivideByZeroException",
                          "System.DivideByZeroException,System.DivideByZeroException,System.ArithmeticException,System.SystemException,System.Exception",
                          "divide by zero",
                          [] { (void)il2cpp::runtime::icall_math_div_u(1u, 0u); });
    ExpectDotNetException("mod_ul(1,0) throws DivideByZeroException",
                          "System.DivideByZeroException,System.DivideByZeroException,System.ArithmeticException,System.SystemException,System.Exception",
                          "divide by zero",
                          [] { (void)il2cpp::runtime::icall_math_mod_ul(1ULL, 0ULL); });

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d math assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime math tests passed\n");
    return 0;
}
