//------------------------------------------------------------------------------
// test_mathext.cpp -- System.Math trig/exponential/logarithm icall tests.
//
// Coverage, mirroring the functions declared in il2cpp_math_ext.h:
//   1. icall_math_sin_d / cos_d / tan_d: lattice values (Sin(0)==0,
//      Sin(pi/2)==1, Cos(0)==1, Cos(pi)==-1, Tan(0)==0); infinities -> NaN.
//   2. icall_math_asin_d / acos_d / atan_d: lattice values (Asin(1)==pi/2,
//      Acos(-1)==pi, Atan(1)==pi/4); |x| > 1 -> NaN (net48: no exception).
//   3. icall_math_atan2_d(y, x): Atan2(1,1)==pi/4, Atan2(1,0)==pi/2,
//      Atan2(0,1)==+0; signed zero handling (Atan2(-0.0,1)==-0.0,
//      Atan2(-0.0,-1)==-pi) verified at the bit level.
//   4. icall_math_exp_d: Exp(0)==1, Exp(1)==e, Exp(-1)==1/e, overflow -> +inf,
//      underflow -> 0.
//   5. icall_math_log_d: Log(1)==0, Log(e)==1, Log(2)==ln2, Log(100)==ln100;
//      Log(0)==-inf, Log(-1)==NaN (net48: no exception).
//   6. icall_math_log10_d: Log10(100)==2, Log10(1000)==3, Log10(0.1)==-1;
//      Log10(0)==-inf, Log10(-1)==NaN.
//   7. icall_math_log_base_d(a, newBase): the NET48 EMPIRICAL contract (all
//      33 oracle probes from tests/cases/mathext against a real .NET
//      Framework 4.8 process, captured 2026-08-03):
//        newBase == 1         -> NaN   (even when a == 1: Log(1,1)==NaN)
//        a == 1               -> 0 / ln(newBase) -- a REAL division: the
//                                oracle shows Log(1,0) == -0.0 at the bit
//                                level (0/ln(0) == 0/-inf), i.e. net48 has
//                                NO +0 shortcut for a==1.
//        newBase == 0         -> NaN   (Log(2,0), Log(0.5,0), Log(2,-0.0))
//        newBase == +inf      -> NaN   (Log(2,inf)==NaN!)
//        otherwise            -> ln(a) / ln(newBase) with NO a<=0 and NO
//                                newBase<0 checks: Log(0,2) == -inf and
//                                Log(0,0.5) == +inf (natural division),
//                                Log(-1,10) == NaN (natural NaN from
//                                ln(-1)), Log(1,-2) == NaN (0/NaN).
//   8. icall_math_truncate_d: toward zero (Truncate(1.9)==1,
//      Truncate(-1.9)==-1); Truncate(-0.5) == -0.0 (sign preserved, net48
//      bit-verified); NaN/Inf passthrough.
//   9. icall_math_cosh_d / sinh_d / tanh_d: Cosh(0)==1, Sinh(0)==0,
//      Tanh(0)==0, Cosh(1)/Sinh(1)/Tanh(1) known values.
//  10. icall_math_ieee_remainder_d(x, y): IEEE-754 remainder (nearest
//      integral quotient, ties to EVEN quotient -- std::remainder): every
//      net48 oracle case bit-verified, incl. IEEERemainder(5,2)==1 (the
//      2.5 tie goes to the even quotient 2), signed zero in/out, NaN/Inf.
//  11. icall_math_round_digits_d(value, digits): net48 InternalRound
//      multiply-scale semantics (every value below is net48-oracle-verified
//      via .opencode/probes/roundprobe, run against a real .NET Framework
//      4.8 process): Round(3.14159,2)==3.14, Round(2.675,2)==2.68 (NOT
//      2.67 -- the product hits the exact midpoint 267.5, banker's picks
//      268), Round(1.015,2)==1.01 (multiply-scale, NOT .NET Core 1.02),
//      banker's at digit scale (0.125->0.12, 0.135->0.14), digits==0 ==
//      Round(double), |value| >= 1e16 passthrough, NaN/Inf passthrough,
//      digits outside [0,15] throws ArgumentOutOfRangeException.
//  12. icall_math_fmod_d(x, y): the double % (IL rem on R8) lowering --
//      std::fmod (x - trunc(x/y)*y), net48-oracle-verified (probe 2026-08-12):
//      fmod(5.5,2)==1.5, sign follows the DIVIDEND (fmod(-5.5,2)==-1.5,
//      fmod(5.5,-2)==1.5, fmod(-5.5,-2)==-1.5), fmod(5,2.5)==0,
//      fmod(-0.0,3)==-0.0 (signbit), fmod(5,0)==NaN, fmod(inf,3)==NaN,
//      fmod(5,inf)==5, fmod(NaN,3)==NaN. DISTINCT from IEEERemainder
//      (section 10: nearest-quotient ties-to-even -- fmod(5,2)==1 while
//      IEEERemainder(5,2)==1 by coincidence; fmod(7,4)==3 while
//      IEEERemainder(7,4)==-1).
//
// NOTE: the round_digits domain is the ONLY exception path in this file
// (net48 Round(value, digits) validates the digit count).
//  13. icall_math_bigmul(int32,int32) -> the exact int64 product (D-ME2-1,
//      2026-08-12): net48 BigMul value pins, no overflow branch (the product
//      of two int32 always fits an int64).
//  14. System.Double IEEE-754 predicates (D-ME2-2, 2026-08-12):
//      icall_double_is_nan / is_positive_infinity / is_negative_infinity /
//      is_infinity -- bit checks on the double pattern (exponent 0x7FF),
//      returning int32 0/1 (the bool ABI slot).
//------------------------------------------------------------------------------

#include "il2cpp_math_ext.h"

#include "il2cpp_exception.h" // il2cpp::DotNetException (round_digits range throws)

#include "il2cpp_math.h" // D-ME2-1: icall_math_bigmul (Math.BigMul int32->int64)

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
// (Same harness as test_math.cpp:57-80.)
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
void NoThrowRoundDigits(double v, int32_t d)
{
    (void)il2cpp::runtime::icall_math_round_digits_d(v, d);
}

// Reference constants (nearest doubles to the mathematical values; net48
// oracle prints the same 15-digit strings, see tests/cases/mathext).
const double kPi = 3.1415926535897931;
const double kPiHalf = 1.5707963267948966;
const double kPiQuarter = 0.7853981633974483;
const double kE = 2.7182818284590451;
const double kLn2 = 0.6931471805599453;
const double kLn10 = 4.6051701859880914;

// oracle-derived value of Math.Log(3, 2): net48's exact double (bit pattern
// 0x3ff95c01a39fbd69 == 4609816855700290921, printed via BitConverter in the
// oracle case). NOTE: the 15-digit printout "1.58496250072116" parses to a
// DIFFERENT double (11 ulps away) -- the bit-level probe is authoritative.
const double kLog2Of3 = 1.5849625007211563;

// net48-derived library values (15 significant digits, oracle-verified).
const double kCosh1 = 1.5430806348152438;
const double kSinh1 = 1.1752011936438014;
const double kTanh1 = 0.76159415595576489;

// netcore-oracle-precision probe: |actual - expected| <= 1e-15 * max(1,
// |expected|). Transcendental super-function values differ by <= 1 ulp
// between conforming libms (MinGW vs UCRT -- the dotnet 8 oracle runs on
// UCRT), so exact equality is only asserted for lattice values (0, +/-1,
// +/-inf, NaN, powers); everything else uses this tolerance, exactly like
// test_math_extra.cpp's ExpectNear.
void ExpectNear(double actual, double expected, const char* what)
{
    const double scale = (std::fabs(expected) > 1.0) ? std::fabs(expected) : 1.0;
    const bool ok = std::fabs(actual - expected) <= 1e-15 * scale;
    if (!ok)
    {
        std::printf("FAIL: %s (actual %.17g, expected %.17g, diff %g)\n",
                    what, actual, expected, std::fabs(actual - expected));
        ++g_failures;
    }
}

// Exact IEEE-754 bit pattern of a double (bit-level oracle assertions).
uint64_t DoubleBits(double v)
{
    uint64_t bits = 0;
    std::memcpy(&bits, &v, sizeof(bits));
    return bits;
}

} // namespace

int main()
{
    using il2cpp::runtime::icall_math_sin_d;
    using il2cpp::runtime::icall_math_cos_d;
    using il2cpp::runtime::icall_math_tan_d;
    using il2cpp::runtime::icall_math_asin_d;
    using il2cpp::runtime::icall_math_acos_d;
    using il2cpp::runtime::icall_math_atan_d;
    using il2cpp::runtime::icall_math_atan2_d;
    using il2cpp::runtime::icall_math_exp_d;
    using il2cpp::runtime::icall_math_log_d;
    using il2cpp::runtime::icall_math_log10_d;
    using il2cpp::runtime::icall_math_log_base_d;
    using il2cpp::runtime::icall_math_truncate_d;
    using il2cpp::runtime::icall_math_cosh_d;
    using il2cpp::runtime::icall_math_sinh_d;
    using il2cpp::runtime::icall_math_tanh_d;
    using il2cpp::runtime::icall_math_ieee_remainder_d;
    using il2cpp::runtime::icall_math_fmod_d;
    using il2cpp::runtime::icall_math_round_digits_d;
    using il2cpp::runtime::icall_math_acosh_d;
    using il2cpp::runtime::icall_math_asinh_d;
    using il2cpp::runtime::icall_math_atanh_d;
    using il2cpp::runtime::icall_math_cbrt_d;
    using il2cpp::runtime::icall_math_fma_d;
    using il2cpp::runtime::icall_math_log2_d;
    using il2cpp::runtime::icall_math_modf_d;
    using il2cpp::runtime::icall_math_sincos;
    using il2cpp::runtime::icall_math_sin_d;
    using il2cpp::runtime::icall_math_cos_d;

    // --- 1. Sin / Cos / Tan: lattice values (exactly representable
    // results of correctly-rounded libm for these inputs). ---
    Expect(icall_math_sin_d(0.0) == 0.0, "sin(0) == 0");
    Expect(icall_math_sin_d(kPiHalf) == 1.0, "sin(pi/2) == 1");
    Expect(icall_math_cos_d(0.0) == 1.0, "cos(0) == 1");
    Expect(icall_math_cos_d(kPi) == -1.0, "cos(pi) == -1");
    Expect(icall_math_tan_d(0.0) == 0.0, "tan(0) == 0");
    const double sinInf = icall_math_sin_d(INFINITY);
    Expect(sinInf != sinInf, "sin(+inf) is NaN (net48: no exception)");
    const double cosInf = icall_math_cos_d(INFINITY);
    Expect(cosInf != cosInf, "cos(+inf) is NaN");
    const double tanInf = icall_math_tan_d(INFINITY);
    Expect(tanInf != tanInf, "tan(+inf) is NaN");
    // net48 delegates Sin/Cos/Tan to the x87 fsin/fcos/fptan instructions
    // (verified: the mathfunc1 oracle matches the instruction results bit for
    // bit, NOT the UCRT/glibc libm). The x87 reduction returns its input
    // unchanged for |x| >= 2^63 and fptan returns NaN there. Assert the
    // oracle-pinned patterns on the x86 GCC builds that carry the asm path.
#if defined(__GNUC__) && (defined(__i386__) || defined(__x86_64__))
    Expect(DoubleBits(icall_math_sin_d(1e300)) == 0x7E37E43C8800759CULL,
           "sin(1e300) matches net48 x87 fsin");
    Expect(DoubleBits(icall_math_cos_d(1e300)) == 0x7E37E43C8800759CULL,
           "cos(1e300) matches net48 x87 fcos");
    Expect(DoubleBits(icall_math_sin_d(1.7976931348623157e308)) == 0x7FEFFFFFFFFFFFFFULL,
           "sin(double.MaxValue) matches net48 x87 fsin (returns the input)");
    Expect(DoubleBits(icall_math_sin_d(-1.7976931348623157e308)) == 0xFFEFFFFFFFFFFFFFULL,
           "sin(double.MinValue) matches net48 x87 fsin (returns the input)");
    Expect(DoubleBits(icall_math_tan_d(1e10)) == 0xBFE1DE000F41ED21ULL,
           "tan(1e10) matches net48 x87 fptan");
    Expect(DoubleBits(icall_math_tan_d(100.0)) == 0xBFE2CA74D62B5D38ULL,
           "tan(100) matches net48 x87 fptan");
    Expect(DoubleBits(icall_math_tan_d(kPi)) == 0xBCA1A60000000000ULL,
           "tan(pi) matches net48 x87 fptan");
    Expect(DoubleBits(icall_math_tan_d(1.7976931348623157e308)) == 0xFFF8000000000000ULL,
           "tan(double.MaxValue) is the net48 x87 NaN");
#endif

    // --- 2. Asin / Acos / Atan: lattice values + domain errors -> NaN. ---
    Expect(icall_math_asin_d(1.0) == kPiHalf, "asin(1) == pi/2");
    Expect(icall_math_asin_d(-1.0) == -kPiHalf, "asin(-1) == -pi/2");
    Expect(icall_math_acos_d(1.0) == 0.0, "acos(1) == 0");
    Expect(icall_math_acos_d(-1.0) == kPi, "acos(-1) == pi");
    Expect(icall_math_atan_d(1.0) == kPiQuarter, "atan(1) == pi/4");
    Expect(icall_math_atan_d(0.0) == 0.0, "atan(0) == 0");
    const double asin2 = icall_math_asin_d(2.0);
    Expect(asin2 != asin2, "asin(2) is NaN (domain error, no exception)");
    const double acos2 = icall_math_acos_d(2.0);
    Expect(acos2 != acos2, "acos(2) is NaN (domain error, no exception)");
    const double asinNeg2 = icall_math_asin_d(-2.0);
    Expect(asinNeg2 != asinNeg2, "asin(-2) is NaN");

    // --- 3. Atan2(y, x): quadrants + signed zeros (bit-verified vs net48). ---
    Expect(icall_math_atan2_d(1.0, 1.0) == kPiQuarter, "atan2(1,1) == pi/4");
    Expect(icall_math_atan2_d(1.0, 0.0) == kPiHalf, "atan2(1,0) == pi/2");
    Expect(icall_math_atan2_d(0.0, 1.0) == 0.0, "atan2(0,1) == +0");
    Expect(icall_math_atan2_d(-1.0, -1.0) == -2.3561944901923448,
           "atan2(-1,-1) == -3*pi/4");
    Expect(!std::signbit(icall_math_atan2_d(0.0, 1.0)),
           "atan2(+0,1) has clear sign bit");
    Expect(std::signbit(icall_math_atan2_d(-0.0, 1.0)),
           "atan2(-0,1) == -0.0 (sign bit set)");
    Expect(std::signbit(icall_math_atan2_d(-0.0, -1.0)),
           "atan2(-0,-1) == -pi (sign bit set)");
    // net48 returns NaN when BOTH operands are infinite (mathfunc1 oracle:
    // atan2(+inf,+inf), atan2(+inf,-inf), atan2(-inf,+inf) and
    // atan2(-inf,-inf) are all the negative quiet NaN). One infinite operand
    // with a finite other still follows the IEEE quadrants (verified above).
    const double atan2InfInf = icall_math_atan2_d(INFINITY, INFINITY);
    Expect(atan2InfInf != atan2InfInf, "atan2(+inf,+inf) is NaN (net48)");
    Expect(DoubleBits(atan2InfInf) == 0xFFF8000000000000ULL,
           "atan2(+inf,+inf) is the net48 negative NaN");
    const double atan2InfNegInf = icall_math_atan2_d(INFINITY, -INFINITY);
    Expect(atan2InfNegInf != atan2InfNegInf, "atan2(+inf,-inf) is NaN (net48)");
    const double atan2NegInfInf = icall_math_atan2_d(-INFINITY, INFINITY);
    Expect(atan2NegInfInf != atan2NegInfInf, "atan2(-inf,+inf) is NaN (net48)");
    const double atan2NegInfNegInf = icall_math_atan2_d(-INFINITY, -INFINITY);
    Expect(atan2NegInfNegInf != atan2NegInfNegInf, "atan2(-inf,-inf) is NaN (net48)");
    Expect(icall_math_atan2_d(INFINITY, 7.0) == kPiHalf,
           "atan2(+inf,7) == pi/2 (finite other operand keeps the quadrant)");

    // --- 4. Exp: exact values, overflow -> +inf, underflow -> 0. ---
    Expect(icall_math_exp_d(0.0) == 1.0, "exp(0) == 1");
    Expect(icall_math_exp_d(1.0) == kE, "exp(1) == e");
    Expect(icall_math_exp_d(-1.0) == 0.36787944117144233, "exp(-1) == 1/e");
    Expect(icall_math_exp_d(1000.0) == INFINITY, "exp(1000) == +inf (overflow)");
    Expect(icall_math_exp_d(-1000.0) == 0.0, "exp(-1000) == 0 (underflow)");
    const double expNaN = icall_math_exp_d(std::nan(""));
    Expect(expNaN != expNaN, "exp(NaN) is NaN");

    // --- 5. Log (natural): exact values, Log(0) == -inf, Log(-1) == NaN. ---
    Expect(icall_math_log_d(1.0) == 0.0, "log(1) == 0");
    Expect(icall_math_log_d(kE) == 1.0, "log(e) == 1");
    Expect(icall_math_log_d(2.0) == kLn2, "log(2) == ln2");
    Expect(icall_math_log_d(100.0) == kLn10, "log(100) == ln100");
    Expect(icall_math_log_d(0.0) == -INFINITY, "log(0) == -inf");
    Expect(std::signbit(icall_math_log_d(0.0)), "log(0) sign bit set");
    const double logNeg = icall_math_log_d(-1.0);
    Expect(logNeg != logNeg, "log(-1) is NaN (net48: no exception)");
    const double logNaN = icall_math_log_d(std::nan(""));
    Expect(logNaN != logNaN, "log(NaN) is NaN");
    // net48's log domain errors carry the NEGATIVE quiet NaN bit pattern
    // (0xFFF8000000000000); glibc's std::log(-x) produces the positive one,
    // so the icall normalizes it (mathfunc1 log_3/log_5/log_12/log_23/log_25).
    Expect(DoubleBits(logNeg) == 0xFFF8000000000000ULL,
           "log(-1) is the net48 negative NaN");
    Expect(DoubleBits(icall_math_log_d(-0.5)) == 0xFFF8000000000000ULL,
           "log(-0.5) is the net48 negative NaN");
    Expect(DoubleBits(icall_math_log_d(-INFINITY)) == 0xFFF8000000000000ULL,
           "log(-inf) is the net48 negative NaN");

    // --- 6. Log10: exact values, Log10(0) == -inf, Log10(-1) == NaN. ---
    Expect(icall_math_log10_d(100.0) == 2.0, "log10(100) == 2");
    Expect(icall_math_log10_d(1000.0) == 3.0, "log10(1000) == 3");
    Expect(icall_math_log10_d(10.0) == 1.0, "log10(10) == 1");
    Expect(icall_math_log10_d(0.1) == -1.0, "log10(0.1) == -1");
    Expect(icall_math_log10_d(0.0) == -INFINITY, "log10(0) == -inf");
    const double log10Neg = icall_math_log10_d(-1.0);
    Expect(log10Neg != log10Neg, "log10(-1) is NaN");

    // --- 7. Log(a, newBase): net48 empirical contract (see file header for
    // the full truth table; every case below is oracle-verified). ---
    Expect(icall_math_log_base_d(8.0, 2.0) == 3.0, "log(8,2) == 3 (net48 exact)");
    Expect(icall_math_log_base_d(16.0, 2.0) == 4.0, "log(16,2) == 4");
    Expect(icall_math_log_base_d(100.0, 10.0) == 2.0, "log(100,10) == 2");
    Expect(icall_math_log_base_d(2.0, 2.0) == 1.0, "log(2,2) == 1");
    Expect(icall_math_log_base_d(8.0, 0.5) == -3.0, "log(8,0.5) == -3");
    Expect(icall_math_log_base_d(3.0, 2.0) == kLog2Of3, "log(3,2) == 1.58496250072116");
    Expect(icall_math_log_base_d(1.0, 10.0) == 0.0, "log(1,10) == +0");
    Expect(!std::signbit(icall_math_log_base_d(1.0, 10.0)),
           "log(1,10) has clear sign bit");
    Expect(std::signbit(icall_math_log_base_d(1.0, 0.0)),
           "log(1,0) == -0.0 (0/ln(0): net48 has no +0 shortcut!)");
    Expect(std::signbit(icall_math_log_base_d(1.0, -0.0)),
           "log(1,-0.0) == -0.0");
    const double log11 = icall_math_log_base_d(1.0, 1.0);
    Expect(log11 != log11, "log(1,1) is NaN (newBase==1 wins over a==1)");
    const double log101 = icall_math_log_base_d(10.0, 1.0);
    Expect(log101 != log101, "log(10,1) is NaN");
    const double log1Neg2 = icall_math_log_base_d(1.0, -2.0);
    Expect(log1Neg2 != log1Neg2, "log(1,-2) is NaN (newBase<0 beats a==1)");
    const double logNeg110 = icall_math_log_base_d(-1.0, 10.0);
    Expect(logNeg110 != logNeg110, "log(-1,10) is NaN (natural: ln(-1) == NaN)");
    const double log10Neg1 = icall_math_log_base_d(10.0, -1.0);
    Expect(log10Neg1 != log10Neg1, "log(10,-1) is NaN (natural: ln(-1) == NaN)");
    Expect(icall_math_log_base_d(0.0, 2.0) == -INFINITY,
           "log(0,2) == -inf (NO a<=0 check: natural ln0/ln2)");
    Expect(icall_math_log_base_d(0.0, 0.5) == INFINITY,
           "log(0,0.5) == +inf (natural: -inf/-ln2)");
    Expect(std::signbit(icall_math_log_base_d(0.0, 2.0)), "log(0,2) sign bit set");
    const double log0Neg2 = icall_math_log_base_d(0.0, -2.0);
    Expect(log0Neg2 != log0Neg2, "log(0,-2) is NaN (natural: -inf/NaN)");
    Expect(icall_math_log_base_d(-0.0, 2.0) == -INFINITY,
           "log(-0.0,2) == -inf (natural: ln(-0.0) == -inf)");
    const double log00 = icall_math_log_base_d(0.0, 0.0);
    Expect(log00 != log00, "log(0,0) is NaN (newBase==0 intercept)");
    const double logNeg0_0 = icall_math_log_base_d(-0.0, 0.0);
    Expect(logNeg0_0 != logNeg0_0, "log(-0.0,0) is NaN (newBase==0)");
    const double logNeg2Neg2 = icall_math_log_base_d(-2.0, -2.0);
    Expect(logNeg2Neg2 != logNeg2Neg2, "log(-2,-2) is NaN");
    const double log20 = icall_math_log_base_d(2.0, 0.0);
    Expect(log20 != log20, "log(2,0) is NaN (newBase==0 for a!=1)");
    const double log05_0 = icall_math_log_base_d(0.5, 0.0);
    Expect(log05_0 != log05_0, "log(0.5,0) is NaN");
    const double log2Neg0 = icall_math_log_base_d(2.0, -0.0);
    Expect(log2Neg0 != log2Neg0, "log(2,-0.0) is NaN (-0.0 == 0.0)");
    const double log05Neg0 = icall_math_log_base_d(0.5, -0.0);
    Expect(log05Neg0 != log05Neg0, "log(0.5,-0.0) is NaN");
    const double log2NegInf = icall_math_log_base_d(2.0, -INFINITY);
    Expect(log2NegInf != log2NegInf, "log(2,-inf) is NaN (natural: ln(-inf))");
    const double log2Inf = icall_math_log_base_d(2.0, INFINITY);
    Expect(log2Inf != log2Inf, "log(2,+inf) is NaN (net48 rejects base inf)");
    Expect(icall_math_log_base_d(INFINITY, 2.0) == INFINITY, "log(inf,2) == +inf");
    const double logInfInf = icall_math_log_base_d(INFINITY, INFINITY);
    Expect(logInfInf != logInfInf, "log(inf,inf) is NaN");
    const double logNaN2 = icall_math_log_base_d(std::nan(""), 2.0);
    Expect(logNaN2 != logNaN2, "log(NaN,2) is NaN");
    const double log2NaN = icall_math_log_base_d(2.0, std::nan(""));
    Expect(log2NaN != log2NaN, "log(2,NaN) is NaN");

    // --- 8. Truncate: toward zero; -0.5 -> -0.0 (sign preserved, net48
    // bit-verified: ToString hides the sign but the bits carry it). ---
    Expect(icall_math_truncate_d(1.9) == 1.0, "truncate(1.9) == 1");
    Expect(icall_math_truncate_d(-1.9) == -1.0, "truncate(-1.9) == -1");
    Expect(icall_math_truncate_d(1.0) == 1.0, "truncate(1.0) == 1");
    Expect(icall_math_truncate_d(0.5) == 0.0, "truncate(0.5) == 0");
    Expect(!std::signbit(icall_math_truncate_d(0.5)), "truncate(0.5) is +0");
    Expect(std::signbit(icall_math_truncate_d(-0.5)),
           "truncate(-0.5) == -0.0 (sign preserved)");
    const double truncNaN = icall_math_truncate_d(std::nan(""));
    Expect(truncNaN != truncNaN, "truncate(NaN) is NaN");
    Expect(icall_math_truncate_d(INFINITY) == INFINITY, "truncate(+inf) == +inf");
    Expect(icall_math_truncate_d(-INFINITY) == -INFINITY, "truncate(-inf) == -inf");

    // --- 9. Cosh / Sinh / Tanh: lattice + net48 library values. ---
    Expect(icall_math_cosh_d(0.0) == 1.0, "cosh(0) == 1");
    Expect(icall_math_sinh_d(0.0) == 0.0, "sinh(0) == 0");
    Expect(icall_math_tanh_d(0.0) == 0.0, "tanh(0) == 0");
    Expect(icall_math_cosh_d(1.0) == kCosh1, "cosh(1) == 1.54308063481524");
    Expect(icall_math_sinh_d(1.0) == kSinh1, "sinh(1) == 1.1752011936438");
    Expect(icall_math_tanh_d(1.0) == kTanh1, "tanh(1) == 0.761594155955765");
    Expect(icall_math_cosh_d(INFINITY) == INFINITY, "cosh(+inf) == +inf");

    // --- 10. IEEERemainder: IEEE-754 remainder, ties to EVEN quotient --
    // every case below is a net48 oracle bit-verified value. ---
    Expect(icall_math_ieee_remainder_d(3.0, 2.0) == -1.0, "ier(3,2) == -1");
    Expect(icall_math_ieee_remainder_d(5.0, 2.0) == 1.0,
           "ier(5,2) == 1 (2.5 tie -> EVEN quotient 2: 5-4=1)");
    Expect(icall_math_ieee_remainder_d(-3.0, 2.0) == 1.0, "ier(-3,2) == 1");
    Expect(icall_math_ieee_remainder_d(3.0, -2.0) == -1.0, "ier(3,-2) == -1");
    Expect(icall_math_ieee_remainder_d(7.0, 4.0) == -1.0, "ier(7,4) == -1");
    Expect(icall_math_ieee_remainder_d(0.0, 3.0) == 0.0, "ier(0,3) == +0");
    Expect(!std::signbit(icall_math_ieee_remainder_d(0.0, 3.0)), "ier(0,3) is +0");
    Expect(std::signbit(icall_math_ieee_remainder_d(-0.0, 3.0)),
           "ier(-0.0,3) == -0.0 (sign preserved)");
    const double ier30 = icall_math_ieee_remainder_d(3.0, 0.0);
    Expect(ier30 != ier30, "ier(3,0) is NaN");
    Expect(icall_math_ieee_remainder_d(3.0, INFINITY) == 3.0, "ier(3,inf) == 3");
    const double ierInf3 = icall_math_ieee_remainder_d(INFINITY, 3.0);
    Expect(ierInf3 != ierInf3, "ier(inf,3) is NaN");
    const double ierNaN3 = icall_math_ieee_remainder_d(std::nan(""), 3.0);
    Expect(ierNaN3 != ierNaN3, "ier(NaN,3) is NaN");
    const double ier3NaN = icall_math_ieee_remainder_d(3.0, std::nan(""));
    Expect(ier3NaN != ier3NaN, "ier(3,NaN) is NaN");

    // --- 11. Fmod: the double % (IL rem on R8) lowering -- std::fmod
    // (x - trunc(x/y)*y). Every value below is a net48-oracle-verified probe
    // (dbl1probe, 2026-08-12): sign follows the DIVIDEND; x%0 -> NaN;
    // inf%y -> NaN; x%inf -> x; NaN propagates. DISTINCT from IEEERemainder
    // above (truncated vs nearest-even quotient: fmod(7,4)==3,
    // IEEERemainder(7,4)==-1). ---
    Expect(icall_math_fmod_d(5.5, 2.0) == 1.5, "fmod(5.5,2) == 1.5 (the double % oracle)");
    Expect(icall_math_fmod_d(7.0, 4.0) == 3.0, "fmod(7,4) == 3 (truncated quotient)");
    Expect(icall_math_fmod_d(-5.5, 2.0) == -1.5, "fmod(-5.5,2) == -1.5 (sign follows dividend)");
    Expect(icall_math_fmod_d(5.5, -2.0) == 1.5, "fmod(5.5,-2) == 1.5");
    Expect(icall_math_fmod_d(-5.5, -2.0) == -1.5, "fmod(-5.5,-2) == -1.5");
    Expect(icall_math_fmod_d(10.0, 3.0) == 1.0, "fmod(10,3) == 1");
    Expect(icall_math_fmod_d(5.0, 2.5) == 0.0, "fmod(5,2.5) == 0");
    Expect(icall_math_fmod_d(5.5, 2.5) == 0.5, "fmod(5.5,2.5) == 0.5");
    Expect(icall_math_fmod_d(5.0, 2.0) == 1.0, "fmod(5,2) == 1");
    Expect(icall_math_fmod_d(0.0, 3.0) == 0.0, "fmod(0,3) == +0");
    Expect(!std::signbit(icall_math_fmod_d(0.0, 3.0)), "fmod(+0,3) has clear sign bit");
    Expect(std::signbit(icall_math_fmod_d(-0.0, 3.0)),
           "fmod(-0.0,3) == -0.0 (sign bit set, net48 probe)");
    const double fmod50 = icall_math_fmod_d(5.0, 0.0);
    Expect(fmod50 != fmod50, "fmod(5,0) is NaN (net48: no exception)");
    const double fmodInf3 = icall_math_fmod_d(INFINITY, 3.0);
    Expect(fmodInf3 != fmodInf3, "fmod(+inf,3) is NaN");
    Expect(icall_math_fmod_d(5.0, INFINITY) == 5.0, "fmod(5,+inf) == 5");
    const double fmodNaN3 = icall_math_fmod_d(std::nan(""), 3.0);
    Expect(fmodNaN3 != fmodNaN3, "fmod(NaN,3) is NaN");
    const double fmod3NaN = icall_math_fmod_d(3.0, std::nan(""));
    Expect(fmod3NaN != fmod3NaN, "fmod(3,NaN) is NaN");
    // The distinguishing case vs IEEERemainder: truncated vs nearest-even.
    Expect(icall_math_fmod_d(7.0, 4.0) == 3.0 && icall_math_ieee_remainder_d(7.0, 4.0) == -1.0,
           "fmod(7,4)==3 vs IEEERemainder(7,4)==-1 (truncated vs nearest-even quotient)");

    // --- 11. Round(value, digits): net48 multiply-scale semantics (every
    // case oracle-verified against a real net48 process via
    // .opencode/probes/roundprobe). ---    Expect(icall_math_round_digits_d(3.14159, 2) == 3.14, "round_digits(3.14159, 2) == 3.14");
    Expect(icall_math_round_digits_d(2.674, 2) == 2.67, "round_digits(2.674, 2) == 2.67");
    Expect(icall_math_round_digits_d(2.675, 2) == 2.68,
           "round_digits(2.675, 2) == 2.68 (product hits the exact midpoint 267.5 -> banker's picks 268)");
    Expect(icall_math_round_digits_d(-2.675, 2) == -2.68, "round_digits(-2.675, 2) == -2.68");
    Expect(icall_math_round_digits_d(1.005, 2) == 1.0, "round_digits(1.005, 2) == 1.0 (binary quirk)");
    Expect(icall_math_round_digits_d(1.015, 2) == 1.01,
           "round_digits(1.015, 2) == 1.01 (multiply-scale: no tie -- NOT the .NET Core 1.02)");
    Expect(icall_math_round_digits_d(0.125, 2) == 0.12, "round_digits(0.125, 2) == 0.12 (12.5 tie -> even 12)");
    Expect(icall_math_round_digits_d(0.135, 2) == 0.14, "round_digits(0.135, 2) == 0.14 (13.5 tie -> even 14)");
    // digits == 0 must equal Round(double) -- the banker's rule.
    Expect(icall_math_round_digits_d(2.5, 0) == 2.0, "round_digits(2.5, 0) == 2 (to even)");
    Expect(icall_math_round_digits_d(-2.5, 0) == -2.0, "round_digits(-2.5, 0) == -2 (to even)");
    Expect(icall_math_round_digits_d(3.5, 0) == 4.0, "round_digits(3.5, 0) == 4 (to even)");
    // |value| >= 1e16 (net48 doubleRoundLimit): unchanged passthrough.
    Expect(icall_math_round_digits_d(1e15, 2) == 1e15, "round_digits(1e15, 2) == 1e15");
    Expect(icall_math_round_digits_d(1e16, 2) == 1e16, "round_digits(1e16, 2) == 1e16 (passthrough)");
    Expect(icall_math_round_digits_d(2.675, 15) == 2.675, "round_digits(2.675, 15) == 2.675");
    // NaN / Infinity passthrough.
    const double rdNaN = icall_math_round_digits_d(std::nan(""), 2);
    Expect(rdNaN != rdNaN, "round_digits(NaN, 2) is NaN");
    Expect(icall_math_round_digits_d(INFINITY, 2) == INFINITY, "round_digits(+inf, 2) == +inf");
    Expect(icall_math_round_digits_d(-INFINITY, 2) == -INFINITY, "round_digits(-inf, 2) == -inf");
    // digits outside [0, 15] -> ArgumentOutOfRangeException (net48 throws;
    // the probe shows ParamName "digits").
    ExpectDotNetException("round_digits(2.675, 16) throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception",
                          "digits must be between 0 and 15",
                          [] { NoThrowRoundDigits(2.675, 16); });
    ExpectDotNetException("round_digits(2.675, -1) throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception",
                          "digits must be between 0 and 15",
                          [] { NoThrowRoundDigits(2.675, -1); });

    // --- 12. .NET Core gMathFuncs super-functions (net48 member scan: these
    // are Core-only additions, so the semantic baseline is the netcore libm
    // contract -- oracle = dotnet 8 Math.* bit patterns, captured 2026-08-07
    // via BitConverter.DoubleToInt64Bits in %TEMP%\opencode\mathprobe). ---

    // Acosh: lattice + domain edges. Acosh(1) == 0 exact; x < 1 -> NaN;
    // Acosh(2) == 1.3169578969248166, Acosh(10) == 2.993222846126381.
    Expect(icall_math_acosh_d(1.0) == 0.0, "acosh(1) == 0");
    ExpectNear(icall_math_acosh_d(2.0), 1.3169578969248166, "acosh(2) == 1.3169578969248166");
    ExpectNear(icall_math_acosh_d(10.0), 2.993222846126381, "acosh(10) == 2.993222846126381");
    Expect(icall_math_acosh_d(0.0) != icall_math_acosh_d(0.0), "acosh(0) is NaN (x < 1)");
    Expect(icall_math_acosh_d(-1.0) != icall_math_acosh_d(-1.0), "acosh(-1) is NaN");
    Expect(icall_math_acosh_d(INFINITY) == INFINITY, "acosh(+inf) == +inf");
    Expect(icall_math_acosh_d(-INFINITY) != icall_math_acosh_d(-INFINITY), "acosh(-inf) is NaN");
    Expect(icall_math_acosh_d(std::nan("")) != icall_math_acosh_d(std::nan("")), "acosh(NaN) is NaN");

    // Asinh: signed zeros preserved; +/-inf map to +/-inf.
    Expect(icall_math_asinh_d(0.0) == 0.0, "asinh(0) == 0");
    Expect(std::signbit(icall_math_asinh_d(-0.0)), "asinh(-0.0) == -0.0 (signbit set)");
    ExpectNear(icall_math_asinh_d(1.0), 0.881373587019543, "asinh(1) == 0.881373587019543");
    ExpectNear(icall_math_asinh_d(-1.0), -0.881373587019543, "asinh(-1) == -0.881373587019543");
    ExpectNear(icall_math_asinh_d(2.0), 1.4436354751788103, "asinh(2) == 1.4436354751788103");
    Expect(icall_math_asinh_d(INFINITY) == INFINITY, "asinh(+inf) == +inf");
    Expect(icall_math_asinh_d(-INFINITY) == -INFINITY, "asinh(-inf) == -inf");
    Expect(icall_math_asinh_d(std::nan("")) != icall_math_asinh_d(std::nan("")), "asinh(NaN) is NaN");

    // Atanh: domain |x| > 1 -> NaN; +/-1 -> +/-inf; signed zero preserved.
    Expect(icall_math_atanh_d(0.0) == 0.0, "atanh(0) == 0");
    Expect(std::signbit(icall_math_atanh_d(-0.0)), "atanh(-0.0) == -0.0 (signbit set)");
    ExpectNear(icall_math_atanh_d(0.5), 0.5493061443340549, "atanh(0.5) == 0.5493061443340549");
    ExpectNear(icall_math_atanh_d(-0.5), -0.5493061443340549, "atanh(-0.5) == -0.5493061443340549");
    Expect(icall_math_atanh_d(1.0) == INFINITY, "atanh(1) == +inf");
    Expect(icall_math_atanh_d(-1.0) == -INFINITY, "atanh(-1) == -inf");
    Expect(icall_math_atanh_d(2.0) != icall_math_atanh_d(2.0), "atanh(2) is NaN (|x| > 1)");
    Expect(icall_math_atanh_d(INFINITY) != icall_math_atanh_d(INFINITY), "atanh(+inf) is NaN");
    Expect(icall_math_atanh_d(std::nan("")) != icall_math_atanh_d(std::nan("")), "atanh(NaN) is NaN");

    // Cbrt: real cube root -- negatives yield negative roots (NOT pow(x,1/3)).
    Expect(icall_math_cbrt_d(0.0) == 0.0, "cbrt(0) == 0");
    Expect(std::signbit(icall_math_cbrt_d(-0.0)), "cbrt(-0.0) == -0.0 (signbit set)");
    Expect(icall_math_cbrt_d(1.0) == 1.0, "cbrt(1) == 1");
    Expect(icall_math_cbrt_d(-1.0) == -1.0, "cbrt(-1) == -1");
    Expect(icall_math_cbrt_d(8.0) == 2.0, "cbrt(8) == 2");
    Expect(icall_math_cbrt_d(-8.0) == -2.0, "cbrt(-8) == -2 (odd root)");
    Expect(icall_math_cbrt_d(27.0) == 3.0, "cbrt(27) == 3 (exact; WSL-Divergence guard)");
    ExpectNear(icall_math_cbrt_d(2.0), 1.259921049894873, "cbrt(2) == 1.259921049894873");
    Expect(icall_math_cbrt_d(INFINITY) == INFINITY, "cbrt(+inf) == +inf");
    Expect(icall_math_cbrt_d(-INFINITY) == -INFINITY, "cbrt(-inf) == -inf");
    Expect(icall_math_cbrt_d(std::nan("")) != icall_math_cbrt_d(std::nan("")), "cbrt(NaN) is NaN");

    // FusedMultiplyAdd: the portable soft-fma (single rounding). Exact
    // lattice: 1.5*2.5+0.5 == 4.25; the single-rounding case 0.1*0.2+0.3 ==
    // 0.32 EXACT (no double-rounding -- this is the canonical fma vs mul-add
    // discriminator); overflow-intermediate fma(1e308,2,-1e308) == 1e308
    // (the product alone overflows, the fused sum is finite).
    Expect(icall_math_fma_d(1.5, 2.5, 0.5) == 4.25, "fma(1.5,2.5,0.5) == 4.25");
    Expect(icall_math_fma_d(0.1, 0.2, 0.3) == 0.32, "fma(0.1,0.2,0.3) == 0.32 (single rounding)");
    Expect(icall_math_fma_d(2.0, 3.0, 1.0) == 7.0, "fma(2,3,1) == 7");
    Expect(icall_math_fma_d(1e308, 2.0, -1e308) == 1e308,
           "fma(1e308,2,-1e308) == 1e308 (no intermediate overflow)");
    Expect(icall_math_fma_d(-1e308, 2.0, 1e308) == -1e308, "fma(-1e308,2,1e308) == -1e308");
    Expect(icall_math_fma_d(std::nan(""), 1.0, 1.0) != icall_math_fma_d(std::nan(""), 1.0, 1.0),
           "fma(NaN,1,1) is NaN");
    Expect(icall_math_fma_d(1.0, INFINITY, 1.0) == INFINITY, "fma(1,+inf,1) == +inf");
    Expect(icall_math_fma_d(INFINITY, 0.0, std::nan("")) != icall_math_fma_d(INFINITY, 0.0, std::nan("")),
           "fma(inf,0,NaN) is NaN (inf*0 invalid per IEEE-754-2008)");
    Expect(!std::signbit(icall_math_fma_d(-0.0, 3.0, 0.0)), "fma(-0.0,3,0) == +0 (signbit cleared)");
    Expect(icall_math_fma_d(0x1.fffffffffffffp1023, 1.0, -0x1.fffffffffffffp1023) == 0.0,
           "fma(max,1,-max) == 0 (split-safe scaling)");

    // Log2: base-2 logarithm. Log2(1) == 0, Log2(2) == 1, Log2(0) == -inf.
    Expect(icall_math_log2_d(1.0) == 0.0, "log2(1) == 0");
    Expect(icall_math_log2_d(2.0) == 1.0, "log2(2) == 1");
    Expect(icall_math_log2_d(0.5) == -1.0, "log2(0.5) == -1");
    Expect(icall_math_log2_d(8.0) == 3.0, "log2(8) == 3");
    ExpectNear(icall_math_log2_d(10.0), 3.321928094887362, "log2(10) == 3.321928094887362");
    Expect(icall_math_log2_d(0.0) == -INFINITY, "log2(0) == -inf");
    Expect(std::signbit(icall_math_log2_d(0.0)), "log2(0) sign bit set");
    Expect(icall_math_log2_d(-1.0) != icall_math_log2_d(-1.0), "log2(-1) is NaN");
    Expect(icall_math_log2_d(INFINITY) == INFINITY, "log2(+inf) == +inf");
    Expect(icall_math_log2_d(std::nan("")) != icall_math_log2_d(std::nan("")), "log2(NaN) is NaN");

    // ModF: splits value into integral part (*intPart, toward zero, input
    // sign) and fractional part (return, input sign). netcore oracle
    // bit-verified (ucrtbase modf): ModF(3.7) -> frac 0.7, int 3;
    // ModF(-3.7) -> frac -0.7, int -3; ModF(-0.5) -> frac -0.5, int -0.
    {
        double intPart = 0.0;
        const double frac37 = icall_math_modf_d(3.7, &intPart);
        Expect(frac37 == 0.7000000000000002, "modf(3.7) frac == 0.7000000000000002");
        Expect(intPart == 3.0, "modf(3.7) int == 3");
        const double fracNeg37 = icall_math_modf_d(-3.7, &intPart);
        Expect(fracNeg37 == -0.7000000000000002, "modf(-3.7) frac == -0.7000000000000002");
        Expect(intPart == -3.0, "modf(-3.7) int == -3");
        const double fracNeg05 = icall_math_modf_d(-0.5, &intPart);
        Expect(fracNeg05 == -0.5, "modf(-0.5) frac == -0.5");
        Expect(intPart == -0.0, "modf(-0.5) int == -0");
        Expect(std::signbit(intPart), "modf(-0.5) int -0 signbit set");
        const double fracInf = icall_math_modf_d(INFINITY, &intPart);
        Expect(fracInf == 0.0 && intPart == INFINITY, "modf(+inf) frac +0, int +inf");
        const double fracNegInf = icall_math_modf_d(-INFINITY, &intPart);
        Expect(fracNegInf == -0.0 && intPart == -INFINITY, "modf(-inf) frac -0, int -inf");
        const double fracNaN = icall_math_modf_d(std::nan(""), &intPart);
        Expect(fracNaN != fracNaN && intPart != intPart, "modf(NaN) frac NaN, int NaN");
    }

    // SinCos: two-call implementation (zero platform branch -- no glibc-only
    // sincos). The CONTRACT is "identical to the individual sin/cos icalls"
    // (that is what the two-call form guarantees on every libm), so the
    // assertions compare the pair against icall_math_sin_d/icall_math_cos_d
    // on the same input -- NOT hard-coded literals (the near-zero
    // cos(pi/2) value differs between libms by argument-reduction accuracy).
    {
        double sinOut = 0.0, cosOut = 0.0;
        icall_math_sincos(0.0, &sinOut, &cosOut);
        Expect(sinOut == 0.0 && cosOut == 1.0, "sincos(0) == (0, 1)");
        icall_math_sincos(kPi, &sinOut, &cosOut);
        Expect(cosOut == -1.0, "sincos(pi).cos == -1");
        icall_math_sincos(1.0, &sinOut, &cosOut);
        Expect(sinOut == icall_math_sin_d(1.0), "sincos(1).sin == sin(1)");
        Expect(cosOut == icall_math_cos_d(1.0), "sincos(1).cos == cos(1)");
        icall_math_sincos(kPiHalf, &sinOut, &cosOut);
        Expect(sinOut == icall_math_sin_d(kPiHalf), "sincos(pi/2).sin == sin(pi/2)");
        Expect(cosOut == icall_math_cos_d(kPiHalf), "sincos(pi/2).cos == cos(pi/2)");
        icall_math_sincos(INFINITY, &sinOut, &cosOut);
        Expect(sinOut != sinOut && cosOut != cosOut, "sincos(+inf) == (NaN, NaN)");
        icall_math_sincos(-INFINITY, &sinOut, &cosOut);
        Expect(sinOut != sinOut && cosOut != cosOut, "sincos(-inf) == (NaN, NaN)");
        icall_math_sincos(std::nan(""), &sinOut, &cosOut);
        Expect(sinOut != sinOut && cosOut != cosOut, "sincos(NaN) == (NaN, NaN)");
        icall_math_sincos(-0.0, &sinOut, &cosOut);
        Expect(std::signbit(sinOut), "sincos(-0.0).sin == -0.0 (signbit set)");
        Expect(cosOut == 1.0, "sincos(-0.0).cos == 1");
    }

    // --- 13. Math.BigMul(int,int) -> the exact int64 product (D-ME2-1,
    // 2026-08-12): icall_math_bigmul. Every net48 oracle pin below comes from
    // the mathext2 case's BigMul section (now calling the wired API direct). ---
    Expect(il2cpp::runtime::icall_math_bigmul(2, 3) == 6LL, "bigmul(2,3) == 6");
    Expect(il2cpp::runtime::icall_math_bigmul(INT32_MAX, INT32_MAX) == 4611686014132420609LL,
           "bigmul(INT32_MAX, INT32_MAX) == 4611686014132420609");
    Expect(il2cpp::runtime::icall_math_bigmul(INT32_MIN, INT32_MIN) == 4611686018427387904LL,
           "bigmul(INT32_MIN, INT32_MIN) == 4611686018427387904");
    Expect(il2cpp::runtime::icall_math_bigmul(INT32_MAX, INT32_MIN) == -4611686016279904256LL,
           "bigmul(INT32_MAX, INT32_MIN) == -4611686016279904256");
    Expect(il2cpp::runtime::icall_math_bigmul(0, INT32_MAX) == 0LL, "bigmul(0, INT32_MAX) == 0");
    Expect(il2cpp::runtime::icall_math_bigmul(-123456789, 987654321) == -121932631112635269LL,
           "bigmul(-123456789, 987654321) == -121932631112635269");
    Expect(il2cpp::runtime::icall_math_bigmul(46340, 46341) == 2147441940LL, "bigmul(46340, 46341)");
    Expect(il2cpp::runtime::icall_math_bigmul(46341, 46341) == 2147488281LL, "bigmul(46341, 46341)");
    Expect(il2cpp::runtime::icall_math_bigmul(100000, 100000) == 10000000000LL, "bigmul(1e5, 1e5)");
    Expect(il2cpp::runtime::icall_math_bigmul(INT32_MAX, -1) == -2147483647LL, "bigmul(INT32_MAX, -1)");
    Expect(il2cpp::runtime::icall_math_bigmul(INT32_MIN, 1) == -2147483648LL, "bigmul(INT32_MIN, 1)");
    Expect(il2cpp::runtime::icall_math_bigmul(-1, -1) == 1LL, "bigmul(-1, -1) == 1");

    // --- 14. System.Double IEEE-754 predicates (D-ME2-2, 2026-08-12):
    // icall_double_is_nan / is_positive_infinity / is_negative_infinity /
    // is_infinity -- bit checks on the 64-bit double pattern, returning int32
    // 0/1. Every pin matches the net48 managed implementation (IsNaN(d) = the
    // d-d exponent probe, IsInfinity = (d == +inf || d == -inf)). ---
    using il2cpp::runtime::icall_double_is_nan;
    using il2cpp::runtime::icall_double_is_positive_infinity;
    using il2cpp::runtime::icall_double_is_negative_infinity;
    using il2cpp::runtime::icall_double_is_infinity;
    Expect(icall_double_is_nan(std::nan("")) == 1, "double.IsNaN(NaN) == true");
    Expect(icall_double_is_nan(1.0) == 0, "double.IsNaN(1.0) == false");
    Expect(icall_double_is_nan(0.0) == 0, "double.IsNaN(0.0) == false");
    Expect(icall_double_is_nan(-0.0) == 0, "double.IsNaN(-0.0) == false");
    Expect(icall_double_is_nan(INFINITY) == 0, "double.IsNaN(+inf) == false");
    Expect(icall_double_is_nan(-INFINITY) == 0, "double.IsNaN(-inf) == false");
    Expect(icall_double_is_positive_infinity(INFINITY) == 1, "double.IsPositiveInfinity(+inf) == true");
    Expect(icall_double_is_positive_infinity(-INFINITY) == 0, "double.IsPositiveInfinity(-inf) == false");
    Expect(icall_double_is_positive_infinity(std::nan("")) == 0, "double.IsPositiveInfinity(NaN) == false");
    Expect(icall_double_is_positive_infinity(1e300) == 0, "double.IsPositiveInfinity(1e300) == false");
    Expect(icall_double_is_positive_infinity(-1e300) == 0, "double.IsPositiveInfinity(-1e300) == false");
    Expect(icall_double_is_negative_infinity(-INFINITY) == 1, "double.IsNegativeInfinity(-inf) == true");
    Expect(icall_double_is_negative_infinity(INFINITY) == 0, "double.IsNegativeInfinity(+inf) == false");
    Expect(icall_double_is_negative_infinity(std::nan("")) == 0, "double.IsNegativeInfinity(NaN) == false");
    Expect(icall_double_is_negative_infinity(-1e300) == 0, "double.IsNegativeInfinity(-1e300) == false");
    Expect(icall_double_is_infinity(INFINITY) == 1, "double.IsInfinity(+inf) == true");
    Expect(icall_double_is_infinity(-INFINITY) == 1, "double.IsInfinity(-inf) == true");
    Expect(icall_double_is_infinity(std::nan("")) == 0, "double.IsInfinity(NaN) == false");
    Expect(icall_double_is_infinity(0.0) == 0, "double.IsInfinity(0.0) == false");
    Expect(icall_double_is_infinity(-0.0) == 0, "double.IsInfinity(-0.0) == false");
    Expect(icall_double_is_infinity(42.0) == 0, "double.IsInfinity(42.0) == false");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d mathext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime mathext tests passed\n");
    return 0;
}
