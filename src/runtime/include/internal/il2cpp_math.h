#pragma once
//------------------------------------------------------------------------------
// il2cpp_math.h -- System.Math static-method runtime support (icalls).
//
// The transpiler maps System.Math calls to these runtime helpers (the math
// methods are [MethodImplOptions.InternalCall]-style intrinsics: they have
// no IL body of their own and delegate to the platform C math library).
//
// Naming follows the existing icall convention in il2cpp_runtime.h:
//   icall_<owner>_<name>_<type-suffix>  with _i = int32, _l = int64,
//   _d = double.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline for behaviour:
//   * Abs(int/long) of the minimum value throws OverflowException.
//   * Sign never throws (Sign(int.MinValue) == -1).
//   * Clamp requires min <= max (ArgumentException otherwise); net48 itself
//     does NOT ship Math.Clamp (added in .NET Core 2.0), but the runtime
//     helper follows the .NET Core contract so the transpiled code can
//     implement Clamp semantics when the BCL is transpiled.
//   * Round(double) uses the default MidpointRounding.ToEven (banker's
//     rounding) -- std::round rounds halves AWAY from zero and is therefore
//     NOT a drop-in; see icall_math_round_d.
//   * Max/Min(double) use the net48 comparison contract (a >= b / a <= b):
//     with a NaN operand the comparison is false, so NaN loses -- this is
//     the .NET Framework behaviour (NOT the .NET Core 3+ IEEE-754-2019
//     NaN-propagation contract).
//   * Sqrt/Pow delegate to the platform libm (std::sqrt/std::pow); both
//     follow IEEE-754 for NaN/Infinity inputs.
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// Math.Abs(int32): |value|; value == int32.MinValue -> OverflowException.
int32_t icall_math_abs_i(int32_t value);

// Math.Abs(int64): |value|; value == int64.MinValue -> OverflowException.
int64_t icall_math_abs_l(int64_t value);

// Math.Max(int32,int32) / Math.Min(int32,int32): net48 comparison contract.
int32_t icall_math_max_i(int32_t a, int32_t b);
int32_t icall_math_min_i(int32_t a, int32_t b);

// Math.Max(int64,int64) / Math.Min(int64,int64).
int64_t icall_math_max_l(int64_t a, int64_t b);
int64_t icall_math_min_l(int64_t a, int64_t b);

// Math.Sign(int32)/Math.Sign(int64): -1 / 0 / 1 (never throws, min value
// included). The .NET return type is int32 for both overloads.
int32_t icall_math_sign_i(int32_t value);
int32_t icall_math_sign_l(int64_t value);
// net9/10 E2E campaign (math sweep): Clamp for int64/double -- same
// contract as the int32 form (min > max -> ArgumentException).
int64_t icall_math_clamp_l(int64_t value, int64_t min, int64_t max);
double icall_math_clamp_d(double value, double min, double max);
// Math.Sign(double/float): sign of the FRACTIONAL value (the old route via
// sign_l truncated first -- Sign(-0.5) wrongly 0). NaN -> 0 (comparisons
// false both ways), matching .NET.
int32_t icall_math_sign_d(double value);

// Math.BigMul(int32 a, int32 b): the exact int64 product (int64_t)a * b --
// D-ME2-1 (2026-08-12). net48's long BigMul(int, int) returns the WIDE product
// of two int32 operands; every product fits in an int64 (no overflow), so the
// icall is a single widening multiply.
int64_t icall_math_bigmul(int32_t a, int32_t b);

// Math.Clamp(int32,int32,int32): value coerced into [min, max];
// min > max -> ArgumentException.
int32_t icall_math_clamp_i(int32_t value, int32_t min, int32_t max);

// Math.Sqrt(double): std::sqrt (negative input -> NaN, net48 contract).
double icall_math_sqrt_d(double value);

// Math.Pow(double,double): std::pow (net48 contract).
double icall_math_pow_d(double x, double y);

// Math.Floor(double)/Math.Ceiling(double): std::floor / std::ceil.
double icall_math_floor_d(double value);
double icall_math_ceiling_d(double value);

// Math.Round(double): MidpointRounding.ToEven (banker's rounding) -- halves
// round to the nearest EVEN integer: Round(0.5)=0, Round(1.5)=2, Round(2.5)=2,
// Round(-1.5)=-2. std::round is NOT used (it rounds halves away from zero).
double icall_math_round_d(double value);

// Math.Abs(double): std::fabs (NaN -> NaN, -0.0 -> +0.0).
double icall_math_abs_d(double value);

// Math.Max(double,double)/Math.Min(double,double): net48 comparison contract
// (see header: NaN loses to a comparable operand).
double icall_math_max_d(double a, double b);
double icall_math_min_d(double a, double b);

// Math.DivRem(int32,int32,out int32) / Math.DivRem(int64,int64,out int64):
// the net48 public API -- returns the QUOTIENT (truncated toward zero),
// writes the REMAINDER (sign follows the dividend, so a%b semantics) through
// the pointer. This is the corert Math.DivModInt family (DivInt32Internal /
// DivUInt32Internal / DivInt64Internal / DivUInt64Internal / ModInt32Internal
// / ModUInt32Internal / ModInt64Internal / ModUInt64Internal, Math.DivModInt.cs
// + nativeaot/Runtime/MathHelpers.cpp) exposed as a result-first icall:
// dividend/divisor ride the value channel, the remainder result slot is a
// pointer parameter (the M7 addr:* transpiler cast delivers the ldloca
// address). net48 exception semantics (empirically verified, divremprobe):
//   * divisor == 0                 -> DivideByZeroException
//   * dividend == MinValue &&
//     divisor == -1                -> OverflowException (the quotient 2^31 /
//                                     2^63 is not representable; C++ / would
//                                     be UB -- checked BEFORE the division)
//   * otherwise                    -> quotient = dividend / divisor (toward
//     zero), remainder = dividend - quotient*divisor (dividend sign).
int32_t icall_math_divrem_i(int32_t dividend, int32_t divisor, int32_t* remainder);
int64_t icall_math_divrem_l(int64_t dividend, int64_t divisor, int64_t* remainder);

// The corert Math.DivModInt internal helpers (div/mod for the four integer
// widths), mapped from the FCall surface. These are the raw signed/unsigned
// division and remainder primitives (i/j, i%j) with the SAME guarded checks
// as icall_math_divrem_* -- divisor == 0 throws DivideByZeroException and
// the signed minimum/-1 pair throws OverflowException before the C++ division
// executes (C++ / of INT_MIN by -1 is UB). Signed operands truncate toward
// zero; the unsigned forms never overflow.
int32_t icall_math_div_i(int32_t dividend, int32_t divisor);
uint32_t icall_math_div_u(uint32_t dividend, uint32_t divisor);
int64_t icall_math_div_l(int64_t dividend, int64_t divisor);
uint64_t icall_math_div_ul(uint64_t dividend, uint64_t divisor);
int32_t icall_math_mod_i(int32_t dividend, int32_t divisor);
uint32_t icall_math_mod_u(uint32_t dividend, uint32_t divisor);
int64_t icall_math_mod_l(int64_t dividend, int64_t divisor);
uint64_t icall_math_mod_ul(uint64_t dividend, uint64_t divisor);

}} // namespace il2cpp::runtime
