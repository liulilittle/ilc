#pragma once
//------------------------------------------------------------------------------
// il2cpp_mathf.h -- System.MathF (System.Single / float) static-method runtime
// support (icalls), the single-precision companion of il2cpp_math.h +
// il2cpp_math_ext.h.
//
// SEMANTIC BASELINE: .NET Core MathF. net48 has NO MathF class (member scan
// verified 2026-08-07; icall-inventory-core.md lists MathF as a Core-only
// domain, gMathFFuncs -- 24 single-precision entries), so the reference is
// the corert MathF implementation (MathF.CoreRT.cs + shared MathF.cs). The
// single-precision functions delegate to the FLOAT (<cmath>) libm entry
// points (std::sqrtf, std::sinf, ... -- the RuntimeImports acosf...tanhf
// surface of MathF.CoreRT.cs), i.e. the "float direct" leg of the
// float->double->float vs float-direct design decision: no function goes
// through a double intermediate, so the result is the float-rounded value of
// the platform float libm (every MSVC / MinGW / NDK target ships these C99
// float functions).
//
// The non-transcendental members (Max/Min/MaxMagnitude/MinMagnitude/Round/
// Sign/Log-base/IEEERemainder/Round-with-digits) are NOT FCalls in corert --
// they are managed implementations in MathF.cs. Each is ported here VERBATIM
// (C# -> C++, float arithmetic) so the runtime surface can serve a
// transpiled MathF reference. Round uses the corert multiply-scale
// algorithm (not the .NET Core 3+ exact-decimal variant -- same deliberate
// divergence as icall_math_round_digits_d).
//
// Naming follows the existing icall convention:
//   icall_<owner>_<name>_<type-suffix>  with _f = float, _i = int32.
//
// Cross-platform contract: every entry is a C99/C++11 <cmath> standard
// function (or a portable soft implementation -- icall_mathf_fma_f, which
// mirrors the double soft fma because MinGW's libm fmaf is not fused at -O0).
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// MathF.Sqrt(float): std::sqrtf. Sqrt(4) == 2; Sqrt(-1) -> NaN;
// Sqrt(0) == +0, Sqrt(-0.0) == -0.0 (IEEE-754 root keeps the sign).
float icall_mathf_sqrt_f(float value);

// MathF.Abs(float): std::fabsf. Abs(-x) == |x|; Abs(NaN) == NaN;
// Abs(-0.0) == +0.0.
float icall_mathf_abs_f(float value);

// MathF.Sin / Cos / Tan (float): std::sinf / cosf / tanf. Sin(0) == 0,
// Cos(0) == 1, Tan(0) == 0, Sin(pi/2) == 1, Cos(pi) == -1;
// +/-inf -> NaN.
float icall_mathf_sin_f(float value);
float icall_mathf_cos_f(float value);
float icall_mathf_tan_f(float value);

// MathF.Asin / Acos / Atan (float): std::asinf / acosf / atanf.
// |value| > 1 -> NaN for asin/acos (no exception); Atan(1) == pi/4.
float icall_mathf_asin_f(float value);
float icall_mathf_acos_f(float value);
float icall_mathf_atan_f(float value);

// MathF.Atan2(float y, float x): std::atan2f(y, x) -- argument order y first,
// matching the .NET signature. IEEE-754 signed-zero handling
// (Atan2(-0.0, -1) == -pi, Atan2(-0.0, 1) == -0.0).
float icall_mathf_atan2_f(float y, float x);

// MathF.Exp(float): std::expf. Exp(0) == 1, Exp(1) == e; overflow -> +inf,
// underflow -> 0.
float icall_mathf_exp_f(float value);

// MathF.Log(float): std::logf -- natural logarithm. Log(1) == 0;
// Log(0) == -inf; Log(<0) -> NaN (no exception).
float icall_mathf_log_f(float value);

// MathF.Log2(float): std::log2f. Log2(1) == 0, Log2(2) == 1, Log2(8) == 3;
// Log2(0) == -inf; Log2(<0) -> NaN.
float icall_mathf_log2_f(float value);

// MathF.Log10(float): std::log10f. Log10(1) == 0, Log10(10) == 1;
// Log10(0) == -inf; Log10(<0) -> NaN.
float icall_mathf_log10_f(float value);

// MathF.Log(float x, float y): the corert MathF.cs managed implementation
// ported verbatim (NOT the net48 Math.Log check order -- MathF's baseline is
// .NET Core). Check order:
//   IsNaN(x)           -> x
//   IsNaN(y)           -> y
//   y == 1             -> NaN        (Log(1,1) == NaN)
//   (x != 1) && (y == 0 || +inf) -> NaN   (Log(2,0), Log(2,inf) == NaN)
//   otherwise          -> Log(x) / Log(y) -- Log(1,0) == -0.0 (0/-inf),
//                                           Log(1,inf) == +0 (0/inf),
//                                           Log(0,2) == -inf (ln0/ln2),
//                                           Log(-1,2) == NaN (ln(-1)).
float icall_mathf_log_base_f(float x, float y);

// MathF.Pow(float, float): std::powf. Pow(2,10) == 1024; Pow(0,0) == 1;
// IEEE-754 for NaN/Infinity operands.
float icall_mathf_pow_f(float x, float y);

// MathF.Ceiling / Floor (float): std::ceilf / floorf. Ceiling(3.2) == 4,
// Ceiling(-3.2) == -3; Floor(3.7) == 3, Floor(-3.7) == -4.
float icall_mathf_ceiling_f(float value);
float icall_mathf_floor_f(float value);

// MathF.Round(float): MidpointRounding.ToEven (banker's rounding) -- the
// float analogue of icall_math_round_d (std::roundf rounds halves AWAY from
// zero and is therefore NOT a drop-in). Round(0.5) == 0, Round(1.5) == 2,
// Round(2.5) == 2, Round(3.5) == 4, Round(-0.5) == -0.0 (sign preserved).
// Values with |x| >= 2^23 have no fractional part and pass through;
// NaN/Infinity pass through.
float icall_mathf_round_f(float value);

// MathF.Round(float value, int digits): the corert MathF.cs multiply-scale
// algorithm (maxRoundingDigits = 6, singleRoundLimit = 1e8f):
//   digits outside [0, 6]   -> ArgumentOutOfRangeException("digits")
//   |value| >= 1e8          -> value UNCHANGED
//   otherwise               -> value * 10^digits, banker's round, / 10^digits
// The scale multiplication makes the result depend on the float's BINARY
// value (the corert / net48-style contract; .NET Core 3+ exact-decimal
// rounding is deliberately NOT reproduced -- same as icall_math_round_digits_d).
float icall_mathf_round_digits_f(float value, int32_t digits);

// MathF.Truncate(float): std::truncf -- toward zero. Truncate(2.7) == 2,
// Truncate(-2.7) == -2; Truncate(-0.5) == -0.0 (sign preserved);
// NaN/Infinity passthrough.
float icall_mathf_truncate_f(float value);

// MathF.Cosh / Sinh / Tanh (float): std::coshf / sinhf / tanhf.
// Cosh(0) == 1, Sinh(0) == 0, Tanh(0) == 0; Cosh(+-inf) == +inf,
// Tanh(+-inf) == +-1.
float icall_mathf_cosh_f(float value);
float icall_mathf_sinh_f(float value);
float icall_mathf_tanh_f(float value);

// MathF.Acosh / Asinh / Atanh (float): std::acoshf / asinhf / atanhf.
// Domain errors yield NaN (Acosh(x < 1), Atanh(|x| > 1)); Atanh(+/-1) ==
// +/-inf; signed zeros preserved in the odd roots (Asinh(-0.0) == -0.0).
float icall_mathf_acosh_f(float value);
float icall_mathf_asinh_f(float value);
float icall_mathf_atanh_f(float value);

// MathF.Cbrt(float): std::cbrtf -- the REAL cube root. Cbrt(8) == 2,
// Cbrt(-8) == -2 (odd root), Cbrt(-0.0) == -0.0.
float icall_mathf_cbrt_f(float value);

// MathF.FusedMultiplyAdd(float x, float y, float z): x*y + z with a SINGLE
// rounding. Implemented as a PORTABLE correctly-rounded soft fma (the float
// analogue of icall_math_fma_d -- Veltkamp split 2^12 + 1 + Knuth TwoSum)
// because the platform libm fmaf is NOT reliable across the target
// compilers (MinGW GCC at -O0 emits a non-fused libm fmaf, and MSVC/NDK
// behavior varies). Bit-identical on every compiler; reproduces the corert
// fmaf contract (incl. the intermediate-cancellation case
// fmaf((1+2^-23)^2, -(1+2^-22)) == 2^-46, which a non-fused path loses).
float icall_mathf_fma_f(float x, float y, float z);

// MathF.Max(float a, float b) / Min(float a, float b): the corert Math.cs
// IEEE-754-2019 `maximum` / `minimum` semantics (MathF.Max delegates to
// Math.Max) -- NaN PROPAGATES and +0 beats -0. This deliberately differs
// from the net48 double contract (NaN loses to a comparable operand) because
// MathF has no net48 baseline. Faithful C# port:
//   Max: a != b -> (b < a) ? a : b with a-NaN propagated; ties return +0.
//   Min: (a != b && !IsNaN(a)) -> (a < b) ? a : b; a-NaN falls to
//        signbit(a) ? a : b -- so Min(NaN, x) == x while Min(x, NaN) == NaN
//        (the asymmetric corert behaviour).
float icall_mathf_max_f(float a, float b);
float icall_mathf_min_f(float a, float b);

// MathF.MaxMagnitude(float x, float y) / MinMagnitude(float x, float y):
// IEEE-754-2019 `maximumMagnitude` / `minimumMagnitude`, the corert MathF.cs
// implementation ported verbatim. NaN propagates from EITHER operand (the
// |x| of a NaN is NaN, and IsNaN(ax) returns x); +0 is the larger magnitude
// (MaxMagnitude(+0, -0) == +0), -0 the smaller (MinMagnitude(-0, +0) == -0).
float icall_mathf_max_magnitude_f(float x, float y);
float icall_mathf_min_magnitude_f(float x, float y);

// MathF.Sign(float): corert Math.Sign(float) -- -1 / 0 / 1; a NaN operand
// throws System.ArithmeticException ("Function does not accept floating point
// Not-a-Number values."). The .NET return type is int32.
int32_t icall_mathf_sign_i(float value);

// MathF.ILogB(float): std::ilogbf -- the base-2 exponent as int32.
// ILogB(8) == 3, ILogB(1) == 0, ILogB(0.5) == -1; ILogB(0) == FP_ILOGB0
// (INT_MIN on the target libcs), ILogB(NaN) / ILogB(inf) == FP_ILOGBNAN
// (INT_MAX).
int32_t icall_mathf_ilogb_i(float value);

// MathF.ScaleB(float x, int n): std::scalbnf -- x * 2^n. ScaleB(3, 2) == 12;
// overflow -> +/-inf, underflow -> +/-0 (IEEE-754).
float icall_mathf_scalb_f(float x, int32_t n);

// MathF.IEEERemainder(float x, float y): std::remainderf -- the IEEE-754
// remainder x - n*y with n the nearest integral quotient, ties to the EVEN
// quotient. The float analogue of icall_math_ieee_remainder_d:
// IER(5,2) == 1 (2.5 ties to quotient 2), signed zero preserved,
// IER(3,0) == NaN, IER(3,inf) == 3, IER(inf,3) == NaN.
float icall_mathf_ieee_remainder_f(float x, float y);

// MathF.ModF(float value, float* intPart): std::modff -- splits into integral
// part (written through the pointer, toward zero, input sign) and fractional
// part (returned, input sign). The float FCall ModF (gMathFFuncs) / corert
// private unsafe MathF.ModF shape.
//
// RESULT-SLOT CONTRACT: the fractional part rides the return channel (a
// float widened to the double result slot), the integral part is written
// through the pointer. The out pointer is `double*` to match the transpiler's
// float-in-double slot model (a float local is a C++ double; the emitter
// passes its address). Mapped by MethodMap.MathF.cs ModF.
float icall_mathf_modf_f(float value, double* intPart);

// MathF.SinCos(float value, float* sinOut, float* cosOut): sin and cos of one
// input. Implemented as TWO separate std::sinf / std::cosf calls (ZERO
// platform branch -- the glibc `sincosf` fast-path is a GNU extension absent
// from MSVC/UCRT/NDK; a double call is exact on every conforming libm).
// This is the float FCall SinCos (gMathFFuncs).
//
// RESULT-SLOT CONTRACT: the netcore public shape is `void SinCos(float value,
// out float sin, out float cos)`. The two out pointers are `double*` to match
// the transpiler's float-in-double slot model (float locals are C++ doubles;
// the emitter passes their addresses). Mapped by MethodMap.MathF.cs SinCos.
void icall_mathf_sincos_f(float value, double* sinOut, double* cosOut);

}} // namespace il2cpp::runtime
