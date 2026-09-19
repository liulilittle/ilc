//------------------------------------------------------------------------------
// mathf.cpp -- System.MathF (System.Single / float) icall implementation
// (declarations in il2cpp_mathf.h).
//
// Every transcendental / rounding function delegates to the FLOAT entry of
// the platform C math library (<cmath>: std::sqrtf, std::sinf, ...). These
// are the exact corert RuntimeImports (acosf ... tanhf) surfaces -- the
// FCall gMathFFuncs family (icall-inventory-core.md). The float overloads
// are preferred over double (std::sin) deliberately: a float->double->float
// round trip can differ from the float function by one ulp on the correctly-
// rounded boundary, and the .NET Core MathF contract is the float libm.
//
// The non-libm members are VERBATIM C# ports of the corert managed
// implementations (MathF.cs / Math.cs): Max/Min/MaxMagnitude/MinMagnitude
// (IEEE-754-2019), Round (banker's, floor/ceil form of the SoftFloat
// algorithm), Round-with-digits (multiply-scale), Sign (ArithmeticException
// on NaN), Log(x, y) base, IEEERemainder (std::remainderf). The only
// hand-written algorithm is icall_mathf_fma_f -- the portable soft fma,
// mirroring icall_math_fma_d (the platform fmaf is not fused under MinGW at
// -O0).
//
// Exception contract: Sign(NaN) throws ArithmeticException (the corert SR
// text) and Round(x, digits) validates digits against [0, 6] with
// ArgumentOutOfRangeException -- both through the standard DotNetException
// carrier. Every other domain error (asin(2), log(-1), Sqrt(-1), ...) yields
// NaN and NEVER throws.
//------------------------------------------------------------------------------

#include "il2cpp_mathf.h"

#include "il2cpp_exception.h"       // il2cpp_throw_new (ArithmeticException)
#include "il2cpp_exception_ext.h"   // il2cpp_throw_argument_out_of_range_exception

#include "il2cpp_math_c99.h"        // GCC 8.1 libstdc++ C99-math <cmath> shim

#include <cmath>
#include <cstdint>

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// MathF.Sqrt: std::sqrtf. IEEE-754 exact for squares (Sqrt(4) == 2);
// Sqrt(-1) -> NaN (MathF: no exception); Sqrt(0) == +0, Sqrt(-0.0) == -0.0.
//------------------------------------------------------------------------------
float icall_mathf_sqrt_f(float value)
{
    return static_cast<float>(std::sqrt(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Abs: std::fabs. Abs(-0.0) == +0.0 (signbit cleared); Abs(NaN) == NaN.
//------------------------------------------------------------------------------
float icall_mathf_abs_f(float value)
{
    return static_cast<float>(std::fabs(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Sin / Cos / Tan: direct float libm delegation. IEEE-754 conformant:
// NaN inputs -> NaN, infinities -> NaN (MathF never throws). Lattice values
// are exact for correctly-rounded libm (Sin(0) == 0, Cos(0) == 1,
// Sin(pi/2) == 1, Cos(pi) == -1 -- the float pi/2 / pi inputs land within
// half an ulp of the lattice value, so the correctly-rounded result IS the
// lattice float).
// Note: std::sinf/cosf/tanf are C99 functions, not guaranteed in std:: on
// GCC/Linux. Use std::sin/cos/tan (double overload) for portability.
//------------------------------------------------------------------------------
float icall_mathf_sin_f(float value)
{
    return static_cast<float>(std::sin(static_cast<double>(value)));
}

float icall_mathf_cos_f(float value)
{
    return static_cast<float>(std::cos(static_cast<double>(value)));
}

float icall_mathf_tan_f(float value)
{
    return static_cast<float>(std::tan(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Asin / Acos / Atan: direct float libm delegation. |x| > 1 yields
// NaN for asin/acos (domain error -- MathF contract: NaN, no exception).
// Atan(1) == pi/4 exactly for correctly-rounded libm.
//------------------------------------------------------------------------------
float icall_mathf_asin_f(float value)
{
    return static_cast<float>(std::asin(static_cast<double>(value)));
}

float icall_mathf_acos_f(float value)
{
    return static_cast<float>(std::acos(static_cast<double>(value)));
}

float icall_mathf_atan_f(float value)
{
    return static_cast<float>(std::atan(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Atan2(y, x): std::atan2f with the .NET argument order (y first).
// IEEE-754 signed-zero handling: Atan2(-0.0, -1) == -pi, Atan2(-0.0, 1) ==
// -0.0.
//------------------------------------------------------------------------------
float icall_mathf_atan2_f(float y, float x)
{
    return static_cast<float>(std::atan2(static_cast<double>(y), static_cast<double>(x)));
}

//------------------------------------------------------------------------------
// MathF.Exp: std::expf. IEEE-754 overflow -> +inf (Exp(1000)), underflow -> 0
// (Exp(-1000)).
//------------------------------------------------------------------------------
float icall_mathf_exp_f(float value)
{
    return static_cast<float>(std::exp(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Log (natural) / Log2 / Log10: direct float libm delegation.
// Log(0) == -inf, Log(<0) -> NaN (MathF: NaN, never an exception).
// Log2(8) == 3 and Log10(10) == 1 are exact for correctly-rounded libm
// (power-of-two / power-of-ten inputs whose logarithm is exactly the lattice
// float).
//------------------------------------------------------------------------------
float icall_mathf_log_f(float value)
{
    return static_cast<float>(std::log(static_cast<double>(value)));
}

float icall_mathf_log2_f(float value)
{
    return static_cast<float>(std::log2(static_cast<double>(value)));
}

float icall_mathf_log10_f(float value)
{
    return static_cast<float>(std::log10(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Log(float x, float y) -- the corert MathF.cs managed implementation
// ported VERBATIM (this is the MathF baseline, NOT the net48 Math.Log check
// order -- MathF has no net48 reference):
//
//   if (IsNaN(x))            return x;     // NaN payload wins
//   if (IsNaN(y))            return y;
//   if (y == 1)              return NaN;   // Log(1,1) == NaN: y==1 beats x==1
//   if ((x != 1) && (y == 0 || IsPositiveInfinity(y))) return NaN;
//                                     // Log(2,0), Log(2,inf) == NaN, but
//                                     // Log(1,0) == -0.0 and Log(1,inf) == +0
//   return Log(x) / Log(y);   // EVERYTHING ELSE IS NATURAL: Log(0,2) == -inf
//                             // (ln0/ln2), Log(-1,2) == NaN (ln(-1)),
//                             // Log(1,-2) == NaN (0/NaN).
//
// Note the real division: Log(1,0) computes 0/ln(0) == 0/-inf == -0.0 and
// Log(1,inf) computes 0/+inf == +0 (bit-visible signed zeros, same as the
// corert C# float division).
//------------------------------------------------------------------------------
float icall_mathf_log_base_f(float x, float y)
{
    if (std::isnan(x))
    {
        return x;
    }
    if (std::isnan(y))
    {
        return y;
    }
    if (y == 1.0f)
    {
        return NAN;
    }
    if ((x != 1.0f) && (y == 0.0f || (std::isinf(y) && !std::signbit(y))))
    {
        return NAN;
    }
    return icall_mathf_log_f(x) / icall_mathf_log_f(y);
}

//------------------------------------------------------------------------------
// MathF.Pow: std::powf. IEEE-754 for NaN/Infinity operands; satisfies
// Pow(0,0) == 1 (MathF contract).
//------------------------------------------------------------------------------
float icall_mathf_pow_f(float x, float y)
{
    return static_cast<float>(std::pow(static_cast<double>(x), static_cast<double>(y)));
}

//------------------------------------------------------------------------------
// MathF.Ceiling / Floor: direct float libm delegation (std::ceilf / floorf).
//------------------------------------------------------------------------------
float icall_mathf_ceiling_f(float value)
{
    return static_cast<float>(std::ceil(static_cast<double>(value)));
}

float icall_mathf_floor_f(float value)
{
    return static_cast<float>(std::floor(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Round(float) -- MidpointRounding.ToEven (banker's rounding), the
// float analogue of icall_math_round_d. std::roundf rounds halves AWAY from
// zero (Round(2.5) would give 3), which is NOT the .NET default. Explicit
// floor/ceil implementation:
//
//   fl = floor(value), cl = ceil(value).
//   fl == cl                  -> value is integral; return it unchanged.
//   value - fl <  cl - value  -> nearer to fl.
//   value - fl >  cl - value  -> nearer to cl.
//   equal (exact midpoint)    -> pick the EVEN of the two neighbours
//                                (fmod(fl, 2) == 0 -> fl, else cl).
//
// Exactness for floats: for |value| >= 1 the subtractions satisfy Sterbenz's
// lemma (floor(value) <= value <= 2*floor(value)), and midpoints n + 0.5
// with |n| < 2^23 are exactly representable, so "equal" detects true halves
// -- never a round-off coincidence. The float grid at |value| >= 2^23 has
// spacing 1.0, so fl == cl there (no fractional part) and the value passes
// through. NaN fails every comparison and returns ceil(NaN) == NaN;
// infinities hit fl == cl and return the infinity. Signed zeros survive:
// Round(-0.5) == -0.0 (fl == -1, cl == -0.0, exact midpoint, fmod(-1, 2) != 0
// -> cl) -- the corert soft-float CopySign(-0, ...) contract.
//------------------------------------------------------------------------------
float icall_mathf_round_f(float value)
{
    const float fl = static_cast<float>(std::floor(static_cast<double>(value)));
    const float cl = static_cast<float>(std::ceil(static_cast<double>(value)));
    if (fl == cl)
    {
        return fl;
    }
    const float lower = value - fl;
    const float upper = cl - value;
    if (lower < upper)
    {
        return fl;
    }
    if (lower > upper)
    {
        return cl;
    }
    // Exact midpoint: banker's rule -- round to the even neighbour.
    return (static_cast<float>(std::fmod(static_cast<double>(fl), static_cast<double>(2.0f))) == 0.0f) ? fl : cl;
}

// corert MathF's roundPower10Single: the exact powers 10^0..10^6 (every entry
// is exactly representable as a float). A table mirrors the corert
// implementation precisely -- static_cast<float>(std::pow(static_cast<double>(10.0f), static_cast<double>(n))) computes the same values
// for n <= 6, but the table removes any libm dependence from the contract.
static const float kRoundPower10[7] = {
    1e0f, 1e1f, 1e2f, 1e3f, 1e4f, 1e5f, 1e6f,
};

//------------------------------------------------------------------------------
// MathF.Round(value, digits) -- corert MathF.cs InternalRound(float, int,
// ToEven), the MULTIPLY-SCALE algorithm (NOT decimal arithmetic):
//
//   if (|value| >= 1e8f) return value;  // singleRoundLimit passthrough
//   value *= 10^digits;                 // corert roundPower10Single
//   value  = banker's round to integer; // icall_mathf_round_f (ToEven)
//   value /= 10^digits;
//
// The scale step makes the result depend on the float's BINARY value -- that
// is the corert (and net48-style) contract, deliberately NOT the .NET Core
// 3+ exact-decimal variant (same divergence as icall_math_round_digits_d):
// Round(2.675f, 2) == 2.68f (float(2.675f) * 100 rounds to the exact midpoint
// 267.5 and banker's picks the EVEN neighbour 268) and Round(1.005f, 2) ==
// 1.0f (float(1.005f) * 100 lands BELOW the midpoint -- no tie exists).
//
// digits is validated against [0, 6] (corert maxRoundingDigits); outside the
// range -> ArgumentOutOfRangeException("digits") with the corert SR message
// ("Rounding digits must be between 0 and 15, inclusive." -- the shared
// RoundingDigits resource text, even though MathF caps at 6).
//------------------------------------------------------------------------------
float icall_mathf_round_digits_f(float value, int32_t digits)
{
    if (digits < 0 || digits > 6)
    {
        il2cpp_throw_argument_out_of_range_exception("digits",
            "Rounding digits must be between 0 and 15, inclusive.");
    }
    if (static_cast<float>(std::fabs(static_cast<double>(value))) < 1e8f)
    {
        const float power10 = kRoundPower10[digits];
        value *= power10;
        value = icall_mathf_round_f(value);
        value /= power10;
    }
    return value;
}

//------------------------------------------------------------------------------
// MathF.Truncate: std::truncf -- toward zero. IEEE-754 exact for all inputs:
// Truncate(2.7) == 2, Truncate(-2.7) == -2, Truncate(-0.5) == -0.0 (sign
// preserved), NaN/Infinity passthrough.
//------------------------------------------------------------------------------
float icall_mathf_truncate_f(float value)
{
    return std::truncf(value);
}

//------------------------------------------------------------------------------
// MathF.Cosh / Sinh / Tanh: direct float libm delegation.
//------------------------------------------------------------------------------
float icall_mathf_cosh_f(float value)
{
    return static_cast<float>(std::cosh(static_cast<double>(value)));
}

float icall_mathf_sinh_f(float value)
{
    return static_cast<float>(std::sinh(static_cast<double>(value)));
}

float icall_mathf_tanh_f(float value)
{
    return static_cast<float>(std::tanh(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Acosh / Asinh / Atanh: direct float libm delegation. Domain errors
// yield NaN (Acosh(x < 1), Atanh(|x| > 1)); Atanh(+/-1) == +/-inf; signed
// zeros preserved in the odd roots (Asinh(-0.0) == -0.0, Atanh(-0.0) ==
// -0.0). Same .NET Core libm contract as the double mathext.cpp family.
//------------------------------------------------------------------------------
float icall_mathf_acosh_f(float value)
{
    return static_cast<float>(std::acosh(static_cast<double>(value)));
}

float icall_mathf_asinh_f(float value)
{
    return static_cast<float>(std::asinh(static_cast<double>(value)));
}

float icall_mathf_atanh_f(float value)
{
    return static_cast<float>(std::atanh(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.Cbrt: std::cbrtf -- the REAL cube root (C99). Unlike powf(x, 1/3),
// cbrt of a negative operand yields a negative root (Cbrt(-8) == -2); NaN/Inf
// propagate; signed zero preserved (Cbrt(-0.0) == -0.0).
//------------------------------------------------------------------------------
float icall_mathf_cbrt_f(float value)
{
    return static_cast<float>(std::cbrt(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.FusedMultiplyAdd(x, y, z): x*y + z with a SINGLE rounding.
// Implemented as a PORTABLE correctly-rounded soft fma -- the binary32
// analogue of icall_math_fma_d (Handbook of Floating-Point Arithmetic,
// Algorithm 5.1, Veltkamp split at C = 2^12 + 1 for p = 24 + Knuth
// TwoProduct/TwoSum) -- because the platform libm fmaf is NOT reliable
// across the target compilers: MinGW GCC at -O0 emits a non-fused libm fmaf
// (the double fma was verified non-fused on the same toolchain 2026-08-07),
// and MSVC/WSL/NDK behavior varies. The soft implementation is bit-identical
// on every compiler and reproduces the corert fmaf contract exactly,
// INCLUDING the single-rounding case that a non-fused path loses:
//   fmaf(1+2^-23, 1+2^-23, -(1+2^-22)) == 2^-46
//   (non-fused float(x*x) rounds to 1+2^-22 and the sum collapses to 0).
//
// Algorithm: x*y = p + e EXACTLY via the Dekker two-product (p = RN(x*y), e
// the exact rounding error; the split pieces xh/xl have at most 12
// significant bits, so every cross product is exact in binary32), then the
// three-term sum s + u + v = p + e + z is accumulated with two TwoSums and
// rounded once (the w == s shortcut leaves the already-exact leading part).
//
// Deviation from icall_math_fma_d: the NaN/Inf fast path covers only the
// INPUT operands (same as the double version), but this version ALSO guards
// the computed SUM: when p + z overflows float range (both finite, dominant
// same-sign), the correctly-rounded fma IS +/-inf -- the double version's
// w == s test would mis-evaluate there (inf + NaN != inf -> NaN); the guard
// returns the infinity directly. The PRODUCT never overflows in the soft
// path: operands with |x| or |y| >= 2^64 (float ~= the double version's
// 2^512, conservative vs the true split-overflow bound 2^116) scale the whole
// computation by 2^-130 (exact power of two) so the split constant C*x never
// overflows and the intermediate product stays finite; the result is scaled
// back in two exact steps.
//------------------------------------------------------------------------------
float icall_mathf_fma_f(float x, float y, float z)
{
    if (std::isinf(x) || std::isnan(x) || std::isinf(y) || std::isnan(y)
        || std::isinf(z) || std::isnan(z))
    {
        return x * y + z;
    }
    // Veltkamp split constant 2^12 + 1 (the product of two 12-bit pieces is
    // exact in a float). |x| >= 2^64 would make C*x overflow -- scale.
    if (static_cast<float>(std::fabs(static_cast<double>(x))) >= 0x1.0p64f || static_cast<float>(std::fabs(static_cast<double>(y))) >= 0x1.0p64f)
    {
        const float xs = x * 0x1.0p-65f;
        const float ys = y * 0x1.0p-65f;
        const float zs = z * 0x1.0p-130f;
        const float r = icall_mathf_fma_f(xs, ys, zs);
        return (r * 0x1.0p65f) * 0x1.0p65f;
    }
    const float split = 4097.0f; // 2^12 + 1
    const float xh = split * x - (split * x - x);
    const float xl = x - xh;
    const float yh = split * y - (split * y - y);
    const float yl = y - yh;
    const float p = x * y;
    const float e = ((xh * yh - p) + xh * yl + xl * yh) + xl * yl; // p+e == x*y exact
    const float s = p + z;
    if (std::isinf(s))
    {
        // The exact sum x*y + z overflows float range (p and z finite with a
        // dominant same sign); the correctly-rounded fma is the infinity.
        return s;
    }
    const float t = (p - s) + z; // s+t == p+z exact
    const float u = e + t;
    const float v = (e - u) + t; // u+v == e+t exact
    const float w = s + u;
    if (w == s)
    {
        return w;
    }
    return w + v;
}

//------------------------------------------------------------------------------
// MathF.Max / Min -- the corert Math.cs IEEE-754-2019 `maximum` / `minimum`
// semantics, ported VERBATIM from the C# (MathF.Max delegates to Math.Max):
//
//   Max(a, b):
//     a != b         -> (!IsNaN(a)) ? ((b < a) ? a : b) : a   // NaN: a wins
//     a == b         -> IsNegative(b) ? a : b                 // +0 beats -0
//
//   Min(a, b):
//     (a != b && !IsNaN(a)) -> (a < b) ? a : b                // plain order
//     else                   -> IsNegative(a) ? a : b         // NaN: sign path
//
// NaN handling is asymmetric by design (the corert C#): Max propagates NaN
// from EITHER position (Max(NaN,5) == NaN, Max(5,NaN) == NaN), while Min only
// propagates a NaN in the SECOND position (Min(NaN,5) == 5 because a NaN
// falls through to the signbit test, Min(5,NaN) == NaN via 5 < NaN == false).
// Signed-zero ties follow IEEE-754-2019: Max(-0.0, +0.0) == +0.0 and
// Min(-0.0, +0.0) == -0.0 (the signbit selects the higher/lower zero).
//------------------------------------------------------------------------------
float icall_mathf_max_f(float a, float b)
{
    if (a != b)
    {
        if (!std::isnan(a))
        {
            return (b < a) ? a : b;
        }
        return a;
    }
    return std::signbit(b) ? a : b;
}

float icall_mathf_min_f(float a, float b)
{
    if (a != b && !std::isnan(a))
    {
        return (a < b) ? a : b;
    }
    return std::signbit(a) ? a : b;
}

//------------------------------------------------------------------------------
// MathF.MaxMagnitude / MinMagnitude -- IEEE-754-2019 `maximumMagnitude` /
// `minimumMagnitude`, the corert MathF.cs implementation ported verbatim:
//
//   MaxMagnitude: ax = |x|, ay = |y|.
//     (ax > ay) || IsNaN(ax) -> x
//     ax == ay               -> IsNegative(x) ? y : x   // +0 is the larger
//     otherwise              -> y
//
//   MinMagnitude:
//     (ax < ay) || IsNaN(ax) -> x
//     ax == ay               -> IsNegative(x) ? x : y   // -0 is the smaller
//     otherwise              -> y
//
// NaN propagates from EITHER operand: a NaN in the first position is caught
// by IsNaN(ax) (the magnitude of a NaN is NaN), and a NaN in the second
// position fails every comparison and falls to `return y`. Ties between +0
// and -0 follow the IEEE rule (MaxMagnitude(+0, -0) == +0,
// MinMagnitude(-0, +0) == -0).
//------------------------------------------------------------------------------
float icall_mathf_max_magnitude_f(float x, float y)
{
    const float ax = static_cast<float>(std::fabs(static_cast<double>(x)));
    const float ay = static_cast<float>(std::fabs(static_cast<double>(y)));
    if (ax > ay || std::isnan(ax))
    {
        return x;
    }
    if (ax == ay)
    {
        return std::signbit(x) ? y : x;
    }
    return y;
}

float icall_mathf_min_magnitude_f(float x, float y)
{
    const float ax = static_cast<float>(std::fabs(static_cast<double>(x)));
    const float ay = static_cast<float>(std::fabs(static_cast<double>(y)));
    if (ax < ay || std::isnan(ax))
    {
        return x;
    }
    if (ax == ay)
    {
        return std::signbit(x) ? x : y;
    }
    return y;
}

//------------------------------------------------------------------------------
// MathF.Sign(float): corert Math.Sign(float) -- -1 for negatives, 1 for
// positives, 0 for +/-0. A NaN operand throws System.ArithmeticException
// with the corert SR text ("Function does not accept floating point
// Not-a-Number values."). The .NET return type is int32.
//------------------------------------------------------------------------------
int32_t icall_mathf_sign_i(float value)
{
    if (value < 0)
    {
        return -1;
    }
    if (value > 0)
    {
        return 1;
    }
    if (value == 0)
    {
        return 0;
    }
    // The only remaining input is NaN (a NaN fails every comparison).
    il2cpp_throw_new("System.ArithmeticException,System.ArithmeticException,System.SystemException,System.Exception",
                     "Function does not accept floating point Not-a-Number values.");
}

//------------------------------------------------------------------------------
// MathF.ILogB: std::ilogbf -- the base-2 unbiased exponent as int32.
// ILogB(1) == 0, ILogB(2) == 1, ILogB(0.5) == -1, ILogB(8) == 3. Zero,
// infinities and NaN return the C FP_ILOGB0 / FP_ILOGBNAN sentinels
// (INT_MIN / INT_MAX on the target libcs) -- the same contract as the corert
// RuntimeImports.ilogbf.
//------------------------------------------------------------------------------
int32_t icall_mathf_ilogb_i(float value)
{
    return static_cast<int32_t>(std::ilogb(static_cast<double>(value)));
}

//------------------------------------------------------------------------------
// MathF.ScaleB: std::scalbnf -- x * 2^n. Exact for representable products;
// overflow -> +/-inf, underflow -> +/-0 (IEEE-754). ScaleB(3, 2) == 12.
//------------------------------------------------------------------------------
float icall_mathf_scalb_f(float x, int32_t n)
{
    return static_cast<float>(std::scalbn(static_cast<double>(x), static_cast<int>(n)));
}

//------------------------------------------------------------------------------
// MathF.IEEERemainder(x, y): std::remainderf -- the IEEE-754 remainder
// x - n*y where n is the integral quotient nearest to x/y, ties to the EVEN
// quotient. This is NOT fmodf (fmod is x - trunc(x/y)*y). The float analogue
// of icall_math_ieee_remainder_d, matching the corert MathF.IEEERemainder
// contract:
//   (3,2) == -1, (5,2) == 1 (5/2 == 2.5 ties to even quotient 2 -> 5-4),
//   (-3,2) == 1, (3,-2) == -1, (7,4) == -1,
//   (0,3) == +0, (-0,3) == -0.0 (signed zero preserved),
//   (3,0) == NaN, (3,inf) == 3, (inf,3) == NaN, (NaN,*) == NaN.
//------------------------------------------------------------------------------
float icall_mathf_ieee_remainder_f(float x, float y)
{
    return static_cast<float>(std::remainder(static_cast<double>(x), static_cast<double>(y)));
}

//------------------------------------------------------------------------------
// MathF.ModF(value, intPart): std::modff -- splits into integral part (written
// through the pointer, toward zero, input sign) and fractional part (returned,
// input sign). Exact for every input; the corert oracle (same CRT modff)
// confirms the bit patterns: ModF(3.7f) -> frac 0.7f, int 3; ModF(-3.7f) ->
// frac -0.7f, int -3; ModF(-0.5f) -> frac -0.5f, int -0; ModF(+inf) -> frac
// +0, int +inf; ModF(NaN) -> (NaN, NaN); ModF(0) -> frac +0, int +0.
//------------------------------------------------------------------------------
float icall_mathf_modf_f(float value, double* intPart)
{
    double ip = 0.0;
    const float frac = static_cast<float>(std::modf(static_cast<double>(value), &ip));
    *intPart = ip;
    return frac;
}

//------------------------------------------------------------------------------
// MathF.SinCos(value, sinOut, cosOut): sin and cos of one input. Implemented
// as TWO separate std::sinf / std::cosf calls -- the glibc `sincosf` fast-path
// is a GNU extension absent from MSVC/UCRT/NDK, so the two-call form is the
// ONLY cross-platform shape (zero platform branch). Every conforming libm
// returns identical results for both functions (Sin(0) == 0, Cos(0) == 1,
// Sin(pi/2) == 1, Cos(pi) == -1; NaN/Inf inputs -> NaN).
//------------------------------------------------------------------------------
void icall_mathf_sincos_f(float value, double* sinOut, double* cosOut)
{
    // std::sinf/cosf are C99 functions, not guaranteed in std:: on GCC/Linux.
    // Use std::sin/cos (C++ overloaded for float) which handle float via
    // implicit conversion. The float overload returns float, cast to double.
    // IMPORTANT: must use the float overload (not double) to preserve the
    // exact float-precision results that the unit tests expect.
    *sinOut = static_cast<double>(std::sin(value));
    *cosOut = static_cast<double>(std::cos(value));
}

}} // namespace il2cpp::runtime



