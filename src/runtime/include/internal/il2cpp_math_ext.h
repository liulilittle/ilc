#pragma once
//------------------------------------------------------------------------------
// il2cpp_math_ext.h -- System.Math trigonometric / exponential / logarithmic
// runtime support (icalls), the extension of il2cpp_math.h.
//
// The transpiler maps System.Math calls to these runtime helpers (the math
// methods are [MethodImplOptions.InternalCall]-style intrinsics: they have
// no IL body of their own and delegate to the platform C math library).
//
// Naming follows the existing icall convention in il2cpp_math.h:
//   icall_<owner>_<name>_<type-suffix>  with _d = double.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline for behaviour. Every non-obvious contract below was EMPIRICALLY
// verified against a real net48 process via tests/cases/mathext
// (BitConverter.DoubleToInt64Bits probes included):
//   * Domain errors NEVER throw: asin(2), acos(2), sin(inf), log(-1),
//     log10(-1), IEEERemainder(x, 0) all return NaN (net48 contract).
//   * IEEERemainder(x, y) is the IEEE-754 remainder x - n*y where n is the
//     nearest integral quotient, ties to the EVEN quotient -- std::remainder
//     matches net48 bit-for-bit (IEEERemainder(5,2) == 1: 5/2 == 2.5 ties to
//     quotient 2). Signed zero is preserved (IEEERemainder(-0.0, 3) == -0.0),
//     IEEERemainder(3, inf) == 3, IEEERemainder(inf, 3) == NaN.
//   * Log(a, newBase) has a CHECK-ORDER quirk verified by the net48 oracle
//     (all 33 probes; see icall_math_log_base_d for the exact truth table):
//     Log(1, 1) == NaN (newBase==1 wins over a==1), Log(1, 0) == -0.0 (the
//     a==1 path performs a REAL division 0/ln(newBase) -- no +0 shortcut),
//     Log(2, 0) == NaN, Log(2, inf) == NaN -- and there is NO a<=0 check:
//     Log(0, 2) == -inf (natural division), Log(-1, 2) == NaN (natural NaN
//     from ln(-1)).
//   * Truncate(-0.5) == -0.0 (toward zero, sign preserved; net48's
//     ToString prints "0" but the bit pattern carries the sign).
//   * Atan2 follows IEEE-754 signed-zero rules (Atan2(-0.0, -1) == -pi).
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// Math.Sin(double): std::sin. Sin(0) == 0, Sin(pi/2) == 1;
// Sin(+-inf) -> NaN (net48: no exception).
double icall_math_sin_d(double value);

// Math.Cos(double): std::cos. Cos(0) == 1, Cos(pi) == -1;
// Cos(+-inf) -> NaN.
double icall_math_cos_d(double value);

// Math.Tan(double): std::tan. Tan(0) == 0; Tan(+-inf) -> NaN.
double icall_math_tan_d(double value);

// Math.Asin(double): std::asin. Asin(1) == pi/2; |value| > 1 -> NaN
// (domain error, net48 never throws).
double icall_math_asin_d(double value);

// Math.Acos(double): std::acos. Acos(-1) == pi; |value| > 1 -> NaN.
double icall_math_acos_d(double value);

// Math.Atan(double): std::atan. Atan(1) == pi/4.
double icall_math_atan_d(double value);

// Math.Atan2(double y, double x): std::atan2(y, x) -- argument order y first,
// matching the .NET signature. IEEE-754 signed-zero handling
// (Atan2(-0.0, -1) == -pi, Atan2(-0.0, 1) == -0.0).
double icall_math_atan2_d(double y, double x);

// Math.Exp(double): std::exp. Exp(0) == 1, Exp(1) == e; overflow -> +inf,
// underflow -> 0.
double icall_math_exp_d(double value);

// Math.Log(double) natural logarithm: std::log. Log(1) == 0, Log(e) == 1;
// Log(0) == -inf, Log(<0) -> NaN (net48: no exception).
double icall_math_log_d(double value);

// Math.Log10(double): std::log10. Log10(100) == 2; Log10(0) == -inf,
// Log10(<0) -> NaN.
double icall_math_log10_d(double value);

// Math.Log(double a, double newBase): ln(a) / ln(newBase) with the net48
// empirical check order (all 33 oracle probes in tests/cases/mathext):
//   newBase == 1    -> NaN   (even when a == 1: Log(1,1) == NaN)
//   a == 1          -> 0 / ln(newBase) -- a REAL division: net48 has no
//                      +0 shortcut, so Log(1,0) == -0.0 (0/ln(0) == 0/-inf)
//   newBase == 0    -> NaN   (Log(2,0), Log(0.5,0), Log(2,-0.0) == NaN)
//   newBase == +inf -> NaN   (Log(2,inf) == NaN)
//   otherwise       -> ln(a) / ln(newBase) -- NATURAL, no a<=0 / newBase<0
//                      checks: Log(0,2) == -inf, Log(-1,2) == NaN (via
//                      ln(-1) == NaN), Log(1,-2) == NaN (via 0/NaN)
double icall_math_log_base_d(double a, double newBase);

// Math.Truncate(double): std::trunc -- toward zero. Truncate(1.9) == 1,
// Truncate(-1.9) == -1; Truncate(-0.5) == -0.0 (sign preserved); NaN/Inf
// passthrough.
double icall_math_truncate_d(double value);

// Math.Cosh(double): std::cosh. Cosh(0) == 1; Cosh(+-inf) == +inf.
double icall_math_cosh_d(double value);

// Math.Sinh(double): std::sinh. Sinh(0) == 0.
double icall_math_sinh_d(double value);

// Math.Tanh(double): std::tanh. Tanh(0) == 0; Tanh(+-inf) == +-1.
double icall_math_tanh_d(double value);

// Math.IEEERemainder(double x, double y): std::remainder -- the IEEE-754
// remainder x - n*y with n the nearest integral quotient, ties to the EVEN
// quotient (NOT away from zero). Bit-identical to net48 on every oracle
// probe: IEEERemainder(3,2) == -1, IEEERemainder(5,2) == 1 (2.5 ties to
// quotient 2), signed zero preserved, IEEERemainder(3, 0) == NaN,
// IEEERemainder(3, inf) == 3, IEEERemainder(inf, y) == NaN.
double icall_math_ieee_remainder_d(double x, double y);

// double % (IL rem on R8): std::fmod -- the .NET/CLR floating-point remainder
// x - trunc(x/y)*y (net48-verified: 5.5 % 2.0 == 1.5, -5.5 % 2.0 == -1.5 --
// sign follows the DIVIDEND, 5.5 % -2.0 == 1.5, 5.0 % 0.0 -> NaN,
// +inf % 3.0 -> NaN, 5.0 % +inf == 5.0). NOT IEEERemainder (nearest-quotient,
// ties-to-even -- std::remainder). The transpiler lowers double BinaryOp.Rem
// to this icall because C++ has no `%` for floating-point operands (D-DBL-1).
double icall_math_fmod_d(double x, double y);

// Math.Acosh(double): std::acosh -- inverse hyperbolic cosine, C99 standard.
// Domain: x < 1 -> NaN (netcore: no exception); Acosh(1) == 0,
// Acosh(+inf) == +inf, Acosh(-inf)/Acosh(NaN) -> NaN.
// NOTE: these inverse-hyperbolic / root / fused / log2 icalls are .NET Core
// gMathFuncs additions (net48 has no such public Math members -- the net48
// member scan returns MISSING for all of them), so the semantic baseline is
// the .NET Core libm contract. The platform libm delivers them identically.
double icall_math_acosh_d(double value);

// Math.Asinh(double): std::asinh -- inverse hyperbolic sine. Asinh(0) == 0,
// Asinh(-0.0) == -0.0 (signbit preserved), Asinh(+/-inf) == +/-inf.
double icall_math_asinh_d(double value);

// Math.Atanh(double): std::atanh -- inverse hyperbolic tangent. Domain:
// |x| > 1 -> NaN; Atanh(+/-1) == +/-inf; Atanh(0) == 0, Atanh(-0.0) == -0.0.
double icall_math_atanh_d(double value);

// Math.Cbrt(double): std::cbrt -- real cube root, C99. Cbrt(+/-1) == +/-1,
// Cbrt(+/-inf) == +/-inf, Cbrt(-8) == -2 (odd root: negative input yields
// a negative result -- NOT std::pow(x, 1/3), which returns NaN for x < 0).
double icall_math_cbrt_d(double value);

// Math.FusedMultiplyAdd(double x, double y, double z): std::fma -- the fused
// multiply-add x*y + z with a SINGLE rounding at the end (no intermediate
// rounding of x*y). C99; on hardware with FMA (x86 FMA3, ARM NEON v8) this
// compiles to one instruction; on software fallback the result is still the
// correctly-rounded fma. NaN/Inf propagate per IEEE-754.
double icall_math_fma_d(double x, double y, double z);

// net9/10 E2E campaign (math sweep): the DOUBLE IEEE-754-2019 family --
// mirrors of the mathf.cpp float implementations (mathext.cpp).
// MaxMagnitude/MinMagnitude: NaN propagates; +0 beats -0 on magnitude ties.
double icall_math_max_magnitude_d(double x, double y);
double icall_math_min_magnitude_d(double x, double y);
// ILogB(double): std::ilogb -- base-2 exponent as int32 (FP_ILOGB0/FP_ILOGBNAN
// pass through per the target libc).
int32_t icall_math_ilogb_d(double value);
// ScaleB(double x, int n): std::scalbn -- x * 2^n.
double icall_math_scaleb_d(double x, int32_t n);
// CopySign(double x, double y): |x| with y's sign bit (std::copysign).
double icall_math_copy_sign_d(double x, double y);
// BitDecrement/BitIncrement(double): the next representable value toward
// -inf / +inf (std::nextafter). NaN stays NaN; +/-inf stay put.
double icall_math_bit_decrement_d(double x);
double icall_math_bit_increment_d(double x);
// ReciprocalEstimate/ReciprocalSqrtEstimate: precisely-corrected fallback
// (spec-permitted) -- 1/x and 1/sqrt(x) with IEEE edge semantics.
double icall_math_reciprocal_estimate_d(double x);
double icall_math_reciprocal_sqrt_estimate_d(double x);

// Math.Log2(double): std::log2 -- base-2 logarithm, C99. Log2(1) == 0,
// Log2(2) == 1, Log2(0) == -inf, Log2(x < 0) -> NaN (netcore: no exception).
double icall_math_log2_d(double value);

// Math.ModF(double value, double* intPart): std::modf -- splits 'value' into
// an integral part (written to *intPart, same sign as the input) and a
// fractional part (the return value). The integral part is the value toward
// zero, exactly like Truncate; the fractional part keeps the input sign
// (ModF(-3.7) -> frac -0.7, int -3). NaN -> (NaN, NaN); +/-inf -> (+/-0,
// +/-inf); signed zero preserves its sign in BOTH slots (ModF(-0.0) -> frac
// -0.0, int -0.0).
//
// RESULT-SLOT CONTRACT (dormant, .NET Core private shape): this is the C
// modf form -- the fractional part rides the return channel, the integral
// part is written through the pointer. The public net48 member
// SplitFractionDouble(double*) wraps the same operation; the transpiler has
// no pointer-typed argument emission (ref/out milestone), so no MethodMap
// mapping routes to this icall yet.
double icall_math_modf_d(double value, double* intPart);

// Math.SinCos(double value, double* sinOut, double* cosOut): computes sin and
// cos of the SAME input. Implemented as two separate std::sin/std::cos calls
// (ZERO platform branch): the glibc `sincos` fast-path is a GNU extension
// absent from MSVC/NDK/UCRT, and a double call is exact on every conforming
// libm (Sin(0) == 0, Cos(0) == 1, Sin/Cos(+-inf) -> NaN).
//
// RESULT-SLOT CONTRACT (dormant, .NET Core public shape): .NET Core's
// Math.SinCos(double) returns a (double Sin, double Cos) ValueTuple -- the
// transpiler has no tuple-return emission, so no MethodMap mapping routes to
// this icall yet (the runtime surface exists for the future milestone).
void icall_math_sincos(double value, double* sinOut, double* cosOut);

// System.Double static predicates (D-ME2-2, 2026-08-12): double.IsNaN /
// IsPositiveInfinity / IsNegativeInfinity / IsInfinity are IEEE-754 bit
// checks on the 64-bit double pattern (sign 1 + exponent 11 + mantissa 52):
//   IsNaN(x)             exponent == 0x7FF && mantissa != 0
//   IsPositiveInfinity   exponent == 0x7FF && mantissa == 0 && sign == 0
//   IsNegativeInfinity   exponent == 0x7FF && mantissa == 0 && sign == 1
//   IsInfinity(x)        exponent == 0x7FF && mantissa == 0 (either sign)
// All return int32 0/1 (the bool crosses the int32 ABI slot, matching the
// char domain's icall_char_is_digit).
int32_t icall_double_is_nan(double value);
int32_t icall_double_is_positive_infinity(double value);
int32_t icall_double_is_negative_infinity(double value);
int32_t icall_double_is_infinity(double value);

// Math.Round(double value, int digits): the net48 InternalRound(value,
// digits, ToEven) MULTIPLY-SCALE algorithm -- NOT decimal arithmetic:
//
//   * digits outside [0, 15] -> ArgumentOutOfRangeException("digits").
//   * |value| >= 1e16 (net48's doubleRoundLimit) -> value UNCHANGED
//     (scaling would round away digits the double cannot carry; NaN and
//     infinities therefore pass through untouched).
//   * otherwise: value * 10^digits, banker's round to integer (ToEven,
//     same rule as icall_math_round_d), then / 10^digits.
//
// The scale multiplication makes the result depend on the double's BINARY
// value -- that is the net48 contract, empirically verified:
// Round(2.675, 2) == 2.68 (NOT 2.67: double(2.675) * 100 rounds to the
// exact midpoint 267.5, banker's picks the EVEN neighbour 268) and
// Round(1.015, 2) == 1.01 (the product falls below the midpoint, so no
// tie exists). .NET Core 3+ exact-decimal rounding differs (1.02) and is
// deliberately NOT reproduced.
double icall_math_round_digits_d(double value, int32_t digits);

//------------------------------------------------------------------------------
// icall_math_round_mode_d -- Math.Round(double, MidpointRounding) (batch
// 149): mode 0 = ToEven (the banker's rounder, icall_math_round_d), mode 1 =
// AwayFromZero (C std::round semantics). Any other mode -> ArgumentException
// (net48 validates the enum value).
//------------------------------------------------------------------------------
double icall_math_round_mode_d(double value, int32_t mode);

//------------------------------------------------------------------------------
// icall_math_round_digits_mode_d -- Math.Round(double, int, MidpointRounding)
// (batch 149): the digit-scaled variant with the mode select (see the two
// primitives above).
//------------------------------------------------------------------------------
double icall_math_round_digits_mode_d(double value, int32_t digits, int32_t mode);

}} // namespace il2cpp::runtime
