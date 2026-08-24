//------------------------------------------------------------------------------
// mathext.cpp -- System.Math trigonometric / exponential / logarithmic icall
// implementation (declarations in il2cpp_math_ext.h).
//
// Every function delegates to the platform C math library (<cmath>). The
// .NET Framework 4.8 reference baseline is reproduced with NO exception
// path: all domain errors (asin(2), log(-1), IEEERemainder(x, 0), ...)
// yield NaN -- verified empirically against a real net48 process
// (tests/cases/mathext oracle, incl. BitConverter bit probes).
//
// The only non-trivial logic is icall_math_log_base_d: net48's check order
// was reverse-engineered from 25 empirical probes and deviates from both
// the .NET Core source and common expectation (Log(1,1) == NaN, Log(1,0)
// == -0.0, Log(2,inf) == NaN) -- see the function comment.
//------------------------------------------------------------------------------

#include "il2cpp_math_ext.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_math.h"           // icall_math_round_d (the banker's rounder)

#include "il2cpp_math_c99.h"       // GCC 8.1 libstdc++ C99-math <cmath> shim

#include <cmath>
#include <cstring>
#include <limits>

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// Math.Sin / Math.Cos / Math.Tan: direct libm delegation. IEEE-754
// conformant: NaN inputs -> NaN, infinities -> NaN (net48 never throws for
// these inputs). Lattice values are exact for correctly-rounded libm
// (Sin(pi/2) == 1.0, Cos(pi) == -1.0).
//------------------------------------------------------------------------------
double icall_math_sin_d(double value)
{
    return std::sin(value);
}

double icall_math_cos_d(double value)
{
    return std::cos(value);
}

double icall_math_tan_d(double value)
{
    return std::tan(value);
}

//------------------------------------------------------------------------------
// Math.Asin / Math.Acos / Math.Atan: direct libm delegation. |x| > 1 yields
// NaN for asin/acos (domain error -- net48 contract: NaN, no exception).
// Atan(1) == pi/4 exactly for correctly-rounded libm.
//------------------------------------------------------------------------------
double icall_math_asin_d(double value)
{
    return std::asin(value);
}

double icall_math_acos_d(double value)
{
    return std::acos(value);
}

double icall_math_atan_d(double value)
{
    return std::atan(value);
}

//------------------------------------------------------------------------------
// Math.Atan2(y, x): std::atan2 with the .NET argument order (y first).
// IEEE-754 signed-zero handling: Atan2(-0.0, -1) == -pi, Atan2(-0.0, 1) ==
// -0.0 -- bit-verified against net48.
//------------------------------------------------------------------------------
double icall_math_atan2_d(double y, double x)
{
    return std::atan2(y, x);
}

//------------------------------------------------------------------------------
// Math.Exp: std::exp. IEEE-754 overflow -> +inf (Exp(1000)), underflow -> 0
// (Exp(-1000)); both net48-verified.
//------------------------------------------------------------------------------
double icall_math_exp_d(double value)
{
    return std::exp(value);
}

//------------------------------------------------------------------------------
// Math.Log (natural) / Math.Log10: direct libm delegation. Log(0) == -inf,
// Log(<0) -> NaN (net48: NaN, never an exception).
//------------------------------------------------------------------------------
double icall_math_log_d(double value)
{
    return std::log(value);
}

double icall_math_log10_d(double value)
{
    return std::log10(value);
}

//------------------------------------------------------------------------------
// Math.Log(a, newBase): ln(a) / ln(newBase) with the NET48 EMPIRICAL check
// order, reverse-engineered from 33 oracle probes (tests/cases/mathext, run
// against a real .NET Framework 4.8 process, bit-level probes included):
//
//   if (newBase == 1.0)   return NaN;     // Log(1,1) == NaN: newBase==1
//                                          // beats a==1!
//   if (a == 1.0)         return 0.0 / std::log(newBase);
//                                          // REAL division, no +0 shortcut:
//                                          // Log(1,10) == +0 (0/ln10),
//                                          // Log(1,0)  == -0.0 (0/ln0 ==
//                                          // 0/-inf, bit-verified),
//                                          // Log(1,inf) == +0,
//                                          // Log(1,-2) == NaN (0/NaN)
//   if (newBase == 0.0)   return NaN;      // Log(2,0), Log(0.5,0),
//                                          // Log(2,-0.0), Log(0.5,-0.0),
//                                          // Log(-0.0,0) == NaN (-0.0 == 0)
//   if (newBase == +inf)  return NaN;      // Log(2,inf) == NaN (base +inf
//                                          // rejected; base -inf flows
//                                          // naturally to NaN via ln(-inf))
//   return std::log(a) / std::log(newBase);// EVERYTHING ELSE IS NATURAL:
//                                          // NO a<=0 check (Log(0,2) == -inf,
//                                          // Log(0,0.5) == +inf, Log(-0.0,2)
//                                          // == -inf) and NO newBase<0 check
//                                          // (Log(1,-2) == NaN via 0/NaN,
//                                          // Log(10,-1) == NaN via ln(-1));
//                                          // negatives yield NaN through
//                                          // ln(negative) == NaN.
//
// All 33 probes reproduced: Log(1,1)=NaN, Log(10,1)=NaN, Log(1,-2)=NaN,
// Log(1,0)=-0.0, Log(1,-0.0)=-0.0, Log(1,10)=+0, Log(1,inf)=+0, Log(0,2)=-inf,
// Log(0,0.5)=+inf, Log(0,-2)=NaN, Log(-0.0,2)=-inf, Log(-0.0,0)=NaN,
// Log(0,0)=NaN, Log(-2,-2)=NaN, Log(-2,2)=NaN, Log(2,0)=NaN, Log(0.5,0)=NaN,
// Log(2,-0.0)=NaN, Log(0.5,-0.0)=NaN, Log(2,-inf)=NaN, Log(2,inf)=NaN,
// Log(8,2)=3, Log(16,2)=4, Log(100,10)=2, Log(2,2)=1, Log(8,0.5)=-3,
// Log(3,2)=1.5849625007211563, Log(inf,2)=inf, Log(inf,inf)=NaN,
// Log(NaN,2)=NaN, Log(2,NaN)=NaN.
//------------------------------------------------------------------------------
double icall_math_log_base_d(double a, double newBase)
{
    if (newBase == 1.0)
    {
        return std::nan("");
    }
    if (a == 1.0)
    {
        return 0.0 / std::log(newBase);
    }
    if (newBase == 0.0)
    {
        return std::nan("");
    }
    if (newBase == INFINITY)
    {
        return std::nan("");
    }
    return std::log(a) / std::log(newBase);
}

//------------------------------------------------------------------------------
// Math.Truncate: std::trunc -- toward zero. IEEE-754 exact for all inputs:
// Truncate(1.9) == 1, Truncate(-1.9) == -1, Truncate(-0.5) == -0.0 (sign
// preserved -- net48 bit-verified; ToString hides the sign but the value
// carries it), NaN/Infinity passthrough.
//------------------------------------------------------------------------------
double icall_math_truncate_d(double value)
{
    return std::trunc(value);
}

//------------------------------------------------------------------------------
// Math.Cosh / Math.Sinh / Math.Tanh: direct libm delegation.
//------------------------------------------------------------------------------
double icall_math_cosh_d(double value)
{
    return std::cosh(value);
}

double icall_math_sinh_d(double value)
{
    return std::sinh(value);
}

double icall_math_tanh_d(double value)
{
    return std::tanh(value);
}

//------------------------------------------------------------------------------
// Math.IEEERemainder(x, y): std::remainder -- the IEEE-754 remainder
// x - n*y where n is the integral quotient nearest to x/y, ties to the EVEN
// quotient. This is NOT fmod (fmod is x - trunc(x/y)*y) and NOT
// Math.Round-based remainder (which would tie away from zero); std::remainder
// is bit-identical to net48 on every oracle probe:
//   (3,2) == -1, (5,2) == 1 (5/2 == 2.5 ties to even quotient 2 -> 5-4),
//   (-3,2) == 1, (3,-2) == -1, (7,4) == -1,
//   (0,3) == +0, (-0,3) == -0.0 (signed zero preserved),
//   (3,0) == NaN, (3,inf) == 3, (inf,3) == NaN, (NaN,*) == NaN.
//------------------------------------------------------------------------------
double icall_math_ieee_remainder_d(double x, double y)
{
    return std::remainder(x, y);
}

//------------------------------------------------------------------------------
// double % (IL rem on R8): std::fmod -- the .NET/CLR floating-point remainder
// x - trunc(x/y)*y. net48-verified (probe 2026-08-12): 5.5 % 2.0 == 1.5,
// -5.5 % 2.0 == -1.5 (the sign follows the DIVIDEND), 5.5 % -2.0 == 1.5,
// 5.0 % 0.0 -> NaN, +inf % 3.0 -> NaN, 5.0 % +inf == 5.0, NaN % y -> NaN.
// This is NOT Math.IEEERemainder (which rounds the quotient to the NEAREST
// integer, ties to even -- std::remainder). C++ has no `%` operator for
// floating-point operands, so the transpiler lowers double BinaryOp.Rem to
// this icall (D-DBL-1).
//------------------------------------------------------------------------------
double icall_math_fmod_d(double x, double y)
{
    return std::fmod(x, y);
}

//------------------------------------------------------------------------------
// Math.Acosh / Asinh / Atanh: inverse hyperbolic functions -- direct C99 libm
// delegation (std::acosh / std::asinh / std::atanh). Domain errors yield NaN
// (netcore contract: Acosh(x < 1), Atanh(|x| > 1)); Atanh(+/-1) == +/-inf;
// signed zeros preserved in the odd roots (Asinh(-0.0) == -0.0,
// Atanh(-0.0) == -0.0, bit-verified via signbit). These are .NET Core
// gMathFuncs additions -- net48 has NO such public Math members (member scan
// verified), so the semantic baseline is the .NET Core libm contract, which
// the same C99 functions reproduce identically.
//------------------------------------------------------------------------------
double icall_math_acosh_d(double value)
{
    return std::acosh(value);
}

double icall_math_asinh_d(double value)
{
    return std::asinh(value);
}

double icall_math_atanh_d(double value)
{
    return std::atanh(value);
}

//------------------------------------------------------------------------------
// Math.Cbrt -- the REAL cube root (C99). Unlike pow(x, 1/3), cbrt of a
// negative operand yields a negative root (Cbrt(-8) == -2); NaN/Inf
// propagate; signed zero preserved (Cbrt(-0.0) == -0.0).
//
// P26 (bcldeep WSL divergence): a BARE std::cbrt is NOT correctly rounded on
// every libm -- glibc's cbrt returns 27.0^(1/3) = 3.0 + 1ULP (while the
// Windows/oracle run rounds to exactly 3.0), and the deviation is even
// call-site-dependent (an -O3 caller got 3.0 exact, an -O0 caller 1ULP up).
// One Newton iteration in LONG DOUBLE (80-bit x87 = 64-bit mantissa on both
// MinGW and Linux/GCC) refines any within-0.5-ULP cbrt estimate to a
// correctly-rounded result: the residual term (r^3 - x) is computed in the
// extended precision, so the correction lands in the final ULPs and the cast
// to double is deterministic across platforms. For perfect cubes (r3 == x
// exactly) the iteration is a no-op and the exact integer root is preserved.
//------------------------------------------------------------------------------
double icall_math_cbrt_d(double value)
{
    if (value == 0.0 || value != value) // signed zero (r=0 div) / NaN pass through
        return std::cbrt(value);
    // MSVC long double == double: on non-finite input the Newton residual is
    // inf-inf = NaN, corrupting the result -- route all non-finite through libm.
    if (!std::isfinite(value))
        return std::cbrt(value);
    long double r = std::cbrt(value);
    const long double d = r * r * r - static_cast<long double>(value);
    if (d != 0.0L)
        r -= d / (3.0L * r * r);
    return static_cast<double>(r);
}

//------------------------------------------------------------------------------
// Math.FusedMultiplyAdd(x, y, z): x*y + z with a SINGLE rounding. Implemented
// as a PORTABLE correctly-rounded fma (Handbook of Floating-Point Arithmetic,
// Algorithm 5.1 -- Veltkamp split + Knuth TwoProduct/TwoSum) because the
// platform libm fma is NOT reliable across the target compilers: MinGW GCC
// at -O0 emits a non-fused libm fma (fma(1e308,2,-1e308) returns inf instead
// of 1e308 -- verified 2026-08-07), and MSVC/WSL/NDK behavior varies. The
// soft implementation is bit-identical on every compiler and reproduces the
// netcore oracle exactly (dotnet 8 Math.FusedMultiplyAdd bit patterns,
// incl. the single-rounding cases fma(0.1,0.2,0.3)==0.32 and
// fma(1+2^-52,1+2^-52,-(1+2^-51))==2^-104 and the overflow-intermediate
// fma(1e308,2,-1e308)==1e308).
//
// Algorithm: x*y = p + e EXACTLY via the Dekker two-product (p = RN(x*y), e
// the exact rounding error), then the three-term sum s + u + v = p + e + z
// is accumulated with two TwoSums and rounded once (the w==s shortcut leaves
// the already-exact leading part). NaN/Inf propagate by the plain x*y+z path
// (the fused single rounding is irrelevant for non-finite products -- IEEE
// special values are unaffected). Operands with |x| or |y| >= 2^512 scale the
// whole computation by 2^-1026 (exact power of two) so the split constant
// C*x never overflows and the product fits; the result is scaled back in two
// exact steps.
//------------------------------------------------------------------------------
double icall_math_fma_d(double x, double y, double z)
{
    if (std::isinf(x) || std::isnan(x) || std::isinf(y) || std::isnan(y)
        || std::isinf(z) || std::isnan(z))
    {
        return x * y + z;
    }
    // Veltkamp split constant 2^27 + 1 (the product of two 27-bit pieces is
    // exact in a double). |x| >= 2^512 would make C*x overflow -- scale.
    if (std::fabs(x) >= 0x1.0p512 || std::fabs(y) >= 0x1.0p512)
    {
        const double xs = x * 0x1.0p-513;
        const double ys = y * 0x1.0p-513;
        const double zs = z * 0x1.0p-1026;
        const double r = icall_math_fma_d(xs, ys, zs);
        return (r * 0x1.0p513) * 0x1.0p513;
    }
    const double split = 134217729.0; // 2^27 + 1
    const double xh = split * x - (split * x - x);
    const double xl = x - xh;
    const double yh = split * y - (split * y - y);
    const double yl = y - yh;
    const double p = x * y;
    const double e = ((xh * yh - p) + xh * yl + xl * yh) + xl * yl; // p+e == x*y exact
    const double s = p + z;
    const double t = (p - s) + z; // s+t == p+z exact
    const double u = e + t;
    const double v = (e - u) + t; // u+v == e+t exact
    const double w = s + u;
    if (w == s)
    {
        return w;
    }
    return w + v;
}

//------------------------------------------------------------------------------
// Math.Log2: std::log2 -- base-2 logarithm (C99). Log2(1) == 0, Log2(2) == 1,
// Log2(0) == -inf, Log2(x < 0) -> NaN (netcore: no exception).
//------------------------------------------------------------------------------
double icall_math_log2_d(double value)
{
    return std::log2(value);
}

//------------------------------------------------------------------------------
// Math.ModF(value, intPart): std::modf -- splits into integral part (written
// through the pointer, toward zero, input sign) and fractional part (returned,
// input sign). Exact for every input; the netcore oracle (P/Invoke to the
// same CRT modf) confirms the bit patterns: ModF(3.7) -> frac 0.7, int 3;
// ModF(-3.7) -> frac -0.7, int -3; ModF(-0.5) -> frac -0.5, int -0;
// ModF(+inf) -> frac +0, int +inf; ModF(-inf) -> frac -0, int -inf;
// ModF(NaN) -> (NaN, NaN); ModF(0) -> frac +0, int +0.
//------------------------------------------------------------------------------
double icall_math_modf_d(double value, double* intPart)
{
    return std::modf(value, intPart);
}

//------------------------------------------------------------------------------
// Math.SinCos(value, sinOut, cosOut): sin and cos of one input. Implemented as
// TWO separate std::sin / std::cos calls -- the glibc `sincos` fast-path is a
// GNU extension absent from MSVC/UCRT/NDK, so the two-call form is the ONLY
// cross-platform shape (zero platform branch). Every conforming libm returns
// identical results for both functions regardless of whether a fused path
// exists (Sin(0) == 0, Cos(0) == 1, Sin(pi/2) == 1, Cos(pi) == -1; NaN/Inf
// inputs -> NaN).
//------------------------------------------------------------------------------
void icall_math_sincos(double value, double* sinOut, double* cosOut)
{
    *sinOut = std::sin(value);
    *cosOut = std::cos(value);
}

// net48's roundPower10Double: the exact powers 10^0..10^15 (every entry is
// exactly representable as a double). A table mirrors the net48 implementation
// precisely -- std::pow(10.0, n) computes the same values for n <= 15, but
// the table removes any libm dependence from the contract.
static const double kRoundPower10[16] = {
    1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7,
    1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15,
};

//------------------------------------------------------------------------------
// Math.BigMul(int a, int b): the exact int64 product (int64_t)a * b -- net48's
// long BigMul(int, int) returns the WIDE product of the two int32 operands.
// Every int32*int32 product fits in an int64 (the operands are at most 2^31 in
// magnitude, so the product is at most 2^62), so a single widening multiply is
// exact with no overflow branch (D-ME2-1, 2026-08-12).
//------------------------------------------------------------------------------
int64_t icall_math_bigmul(int32_t a, int32_t b)
{
    return static_cast<int64_t>(a) * static_cast<int64_t>(b);
}

//------------------------------------------------------------------------------
// System.Double IEEE-754 predicates (D-ME2-2, 2026-08-12): double.IsNaN /
// IsPositiveInfinity / IsNegativeInfinity / IsInfinity implemented as BIT
// checks on the 64-bit double pattern (sign 1 + exponent 11 + mantissa 52,
// re-read through memcpy -- endian-agnostic, no UB):
//   NaN              exponent == 0x7FF && mantissa != 0
//   +Infinity        exponent == 0x7FF && mantissa == 0 && sign == 0
//   -Infinity        exponent == 0x7FF && mantissa == 0 && sign == 1
//   Inf (either)     exponent == 0x7FF && mantissa == 0
// The bit checks reproduce the net48 managed implementation exactly (IsNaN =
// (d-d) shifted, IsInfinity = (d == +inf || d == -inf)) and, unlike a
// comparison-based check, never depend on the compiler's NaN-equality
// handling. Return int32 0/1 (the bool crosses the int32 ABI slot).
//------------------------------------------------------------------------------
static inline uint64_t DoubleBits(double value)
{
    uint64_t bits;
    std::memcpy(&bits, &value, sizeof(bits));
    return bits;
}

int32_t icall_double_is_nan(double value)
{
    const uint64_t bits = DoubleBits(value);
    const uint64_t exponent = (bits >> 52) & 0x7FFULL;
    const uint64_t mantissa = bits & 0xFFFFFFFFFFFFFULL;
    return (exponent == 0x7FFULL && mantissa != 0) ? 1 : 0;
}

int32_t icall_double_is_positive_infinity(double value)
{
    const uint64_t bits = DoubleBits(value);
    return bits == 0x7FF0000000000000ULL ? 1 : 0;
}

int32_t icall_double_is_negative_infinity(double value)
{
    const uint64_t bits = DoubleBits(value);
    return bits == 0xFFF0000000000000ULL ? 1 : 0;
}

int32_t icall_double_is_infinity(double value)
{
    const uint64_t bits = DoubleBits(value) & 0x7FFFFFFFFFFFFFFFULL;
    return bits == 0x7FF0000000000000ULL ? 1 : 0;
}

//------------------------------------------------------------------------------
// Math.Round(value, digits) -- net48 InternalRound(double, int, ToEven), the
// MULTIPLY-SCALE algorithm (Reference Source mscorlib Math.cs), verified
// EMPIRICALLY against a real net48 process (.opencode/probes/roundprobe):
//
//   if (|value| >= 1e16) return value;   // doubleRoundLimit passthrough
//   value *= 10^digits;                  // NOT decimal-exact scaling
//   value  = banker's round to integer;  // icall_math_round_d (ToEven)
//   value /= 10^digits;
//
// The scale step makes the result depend on the double's BINARY value --
// that is the net48 contract, and it differs from .NET Core 3+ (exact
// decimal arithmetic). Probe-verified cases:
//   Round(3.14159, 2) == 3.14, Round(2.674, 2) == 2.67,
//   Round(2.675, 2) == 2.68 (NOT 2.67! double(2.675) * 100 rounds to the
//     EXACT midpoint 267.5 and banker's picks the EVEN neighbour 268),
//   Round(-2.675, 2) == -2.68,
//   Round(1.005, 2) == 1.0, Round(1.015, 2) == 1.01 (the product lands
//     below the midpoint: no tie exists -- .NET Core 3+ gives 1.02),
//   Round(0.125, 2) == 0.12 (12.5 tie -> even 12), Round(0.135, 2) == 0.14,
//   Round(2.5, 0) == 2 (banker's at 0 digits == Round(double)),
//   Round(1e15, 2) == 1e15, Round(1e16, 2) == 1e16 (passthrough),
//   Round(2.675, 15) == 2.675, Round(NaN, 2) == NaN, Round(inf, 2) == inf,
//   Round(-inf, 2) == -inf.
//   digits < 0 || digits > 15 -> ArgumentOutOfRangeException (probe:
//     ParamName "digits", message "digits must be between 0 and 15,
//     inclusive.").
//------------------------------------------------------------------------------
double icall_math_round_digits_d(double value, int32_t digits)
{
    if (digits < 0 || digits > 15)
    {
        il2cpp_throw_argument_out_of_range_exception("digits",
            "digits must be between 0 and 15, inclusive.");
    }
    if (std::fabs(value) < 1e16)
    {
        const double power10 = kRoundPower10[digits];
        value *= power10;
        value = icall_math_round_d(value);
        value /= power10;
    }
    return value;
}

//------------------------------------------------------------------------------
// icall_math_round_mode_d -- Math.Round(double, MidpointRounding) (see the
// header): ToEven (0) delegates to the banker's rounder; AwayFromZero (1)
// uses C std::round (halfway cases away from zero, the exact .NET
// semantics). Any other mode value -> ArgumentException (net48).
//------------------------------------------------------------------------------
double icall_math_round_mode_d(double value, int32_t mode)
{
    if (mode == 1)
    {
        return std::round(value); // AwayFromZero
    }
    if (mode != 0)
    {
        il2cpp_throw_argument_exception(
            "The value passed in must be a valid MidpointRounding value.");
    }
    return icall_math_round_d(value); // ToEven
}

//------------------------------------------------------------------------------
// icall_math_round_digits_mode_d -- Math.Round(double, int, MidpointRounding)
// (see the header): the digit-scaled round with the mode select.
//------------------------------------------------------------------------------
double icall_math_round_digits_mode_d(double value, int32_t digits, int32_t mode)
{
    if (digits < 0 || digits > 15)
    {
        il2cpp_throw_argument_out_of_range_exception("digits",
            "digits must be between 0 and 15, inclusive.");
    }
    if (std::fabs(value) < 1e16)
    {
        const double power10 = kRoundPower10[digits];
        value *= power10;
        value = icall_math_round_mode_d(value, mode);
        value /= power10;
    }
    return value;
}


// net9/10 E2E campaign (math sweep): the DOUBLE IEEE-754-2019 family --
// mirrors of the mathf.cpp float implementations (MaxMagnitude/MinMagnitude
// semantics: NaN propagates, +0 beats -0 on ties; ILogB: FP_ILOGB0/FP_ILOGBNAN
// pass through; ScaleB: scalbn; CopySign: signbit transfer).
double icall_math_max_magnitude_d(double x, double y)
{
    const double ax = std::fabs(x);
    const double ay = std::fabs(y);
    if (ax > ay || std::isnan(ax)) return x;
    if (ax == ay) return std::signbit(x) ? y : x;
    return y;
}

double icall_math_min_magnitude_d(double x, double y)
{
    const double ax = std::fabs(x);
    const double ay = std::fabs(y);
    if (ax < ay || std::isnan(ax)) return x;
    if (ax == ay) return std::signbit(x) ? x : y;
    return y;
}

int32_t icall_math_ilogb_d(double value)
{
    return std::ilogb(value);
}

double icall_math_scaleb_d(double x, int32_t n)
{
    return std::scalbn(x, n);
}

double icall_math_copy_sign_d(double x, double y)
{
    return std::copysign(x, y);
}

// net9/10 E2E campaign: Math.BitDecrement/BitIncrement -- the next
// representable value toward -inf / +inf (std::nextafter). NaN stays NaN;
// +/-inf stay put (nextafter(inf, x) == inf for finite x).
double icall_math_bit_decrement_d(double x)
{
    return std::nextafter(x, -std::numeric_limits<double>::infinity());
}

double icall_math_bit_increment_d(double x)
{
    return std::nextafter(x, std::numeric_limits<double>::infinity());
}

// net9/10 E2E campaign: Math.ReciprocalEstimate / ReciprocalSqrtEstimate.
// .NET spec explicitly permits a PRECISELY-CORRECTED result on targets
// without the approximate hardware instruction -- so 1/x and 1/sqrt(x)
// are conforming implementations (IEEE semantics: 1/0 -> +inf,
// 1/-0 -> -inf, 1/NaN -> NaN, 1/sqrt(-x) -> NaN).
double icall_math_reciprocal_estimate_d(double x)
{
    return 1.0 / x;
}

double icall_math_reciprocal_sqrt_estimate_d(double x)
{
    return 1.0 / std::sqrt(x);
}

}} // namespace il2cpp::runtime

