//------------------------------------------------------------------------------
// test_mathf.cpp -- System.MathF (float) icall runtime unit tests.
//
// Coverage mirrors the functions declared in il2cpp_mathf.h (the .NET Core
// gMathFFuncs FCall surface + the corert MathF managed members). Semantics =
// the corert MathF contract (net48 has no MathF class):
//   1. Sqrt / Abs: exact squares, magnitudes, signed zero, NaN/Inf.
//   2. Sin/Cos/Tan/Asin/Acos/Atan/Atan2: lattice values, net48-known float
//      references (tol 1e-6 relative), domain NaN, signed zero in Atan2.
//   3. Exp/Log/Log2/Log10/Pow: lattice values, references, overflow/underflow,
//      domain NaN.
//   4. Log(x, y) base: the corert check order (Log(1,1)=NaN, Log(2,0)=NaN,
//      Log(1,0)=-0.0, Log(1,inf)=+0, natural division otherwise).
//   5. Ceiling/Floor/Round/Truncate: exact lattice, banker's Round (ToEven,
//      incl. Round(-0.5) == -0.0), signed zero, NaN/Inf passthrough.
//   6. Round(value, digits): corert multiply-scale (max 6 digits, 1e8 limit),
//      ArgumentOutOfRangeException outside [0,6].
//   7. Cosh/Sinh/Tanh/Acosh/Asinh/Atanh/Cbrt: references, domain NaN,
//      odd-root/signed-zero properties.
//   8. FusedMultiplyAdd: the portable soft fma -- the single-rounding
//      discriminator fma((1+2^-23)^2, -(1+2^-22)) == 2^-46 (a non-fused libm
//      fmaf returns 0), sum-overflow -> inf, the |x| >= 2^64 scaled path.
//   9. Max/Min (IEEE-754-2019 NaN propagation + +0 > -0), MaxMagnitude/
//      MinMagnitude (NaN either position, +0 larger / -0 smaller).
//  10. Sign: -1/0/1; NaN throws ArithmeticException.
//  11. ILogB / ScaleB / IEEERemainder: exponent contract, sentinel values,
//      IEEE remainder ties and signed zeros.
//  12. ModF / SinCos: the pointer-slot forms (dormant mappings).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_mathf.h"
#include "il2cpp_exception.h"

#include "il2cpp_math_c99.h" // GCC 8.1 libstdc++ C99-math <cmath> shim (std::fabsf)

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

// float-precision probe: |actual - expected| <= 1e-6 * max(1, |expected|).
// Admits any correctly-rounded float libm (error < 1 ulp ~ 1.2e-7 near 1.0);
// 1e-6 relative is ~8 ulps, still tight enough to catch wrong functions.
void ExpectNearF(float actual, float expected, const char* what)
{
    const float scale = (std::fabsf(expected) > 1.0f) ? std::fabsf(expected) : 1.0f;
    const bool ok = std::fabsf(actual - expected) <= 1e-6f * scale;
    if (!ok)
    {
        std::printf("FAIL: %s (actual %.9g, expected %.9g, diff %g)\n",
                    what, actual, expected, std::fabsf(actual - expected));
        ++g_failures;
    }
}

// A POSITIVE quiet NaN (0x7FC00000). The NAN macro (0.0f/0.0f) on x86 yields
// a NaN with the SIGN BIT SET, which would break the asymmetric
// MathF.Min(NaN, x) == x contract test (corert's signbit fallback).
float PositiveNan()
{
    const uint32_t bits = 0x7FC00000u;
    float value;
    static_assert(sizeof(value) == sizeof(bits), "float is a 32-bit IEEE-754 value");
    std::memcpy(&value, &bits, sizeof(bits));
    return value;
}

// File-scope positive NaN: a capture-less lambda (void (*)() for the
// exception-probe harness) can reference a static without capturing.
static const float kNan = PositiveNan();

// ExpectDotNetException: run fn(), require it to throw a DotNetException of
// the given type tag carrying a message that contains 'messageFragment'.
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
int32_t NoThrowSignF(float v) { return il2cpp::runtime::icall_mathf_sign_i(v); }
float NoThrowRoundDigitsF(float v, int32_t d) { return il2cpp::runtime::icall_mathf_round_digits_f(v, d); }

constexpr float kPi = 3.14159265f;       // float pi
constexpr float kPiOver2 = 1.57079637f;  // float pi/2

} // namespace

int main()
{
    using il2cpp::runtime::icall_mathf_sqrt_f;
    using il2cpp::runtime::icall_mathf_abs_f;
    using il2cpp::runtime::icall_mathf_sin_f;
    using il2cpp::runtime::icall_mathf_cos_f;
    using il2cpp::runtime::icall_mathf_tan_f;
    using il2cpp::runtime::icall_mathf_asin_f;
    using il2cpp::runtime::icall_mathf_acos_f;
    using il2cpp::runtime::icall_mathf_atan_f;
    using il2cpp::runtime::icall_mathf_atan2_f;
    using il2cpp::runtime::icall_mathf_exp_f;
    using il2cpp::runtime::icall_mathf_log_f;
    using il2cpp::runtime::icall_mathf_log2_f;
    using il2cpp::runtime::icall_mathf_log10_f;
    using il2cpp::runtime::icall_mathf_log_base_f;
    using il2cpp::runtime::icall_mathf_pow_f;
    using il2cpp::runtime::icall_mathf_ceiling_f;
    using il2cpp::runtime::icall_mathf_floor_f;
    using il2cpp::runtime::icall_mathf_round_f;
    using il2cpp::runtime::icall_mathf_round_digits_f;
    using il2cpp::runtime::icall_mathf_truncate_f;
    using il2cpp::runtime::icall_mathf_cosh_f;
    using il2cpp::runtime::icall_mathf_sinh_f;
    using il2cpp::runtime::icall_mathf_tanh_f;
    using il2cpp::runtime::icall_mathf_acosh_f;
    using il2cpp::runtime::icall_mathf_asinh_f;
    using il2cpp::runtime::icall_mathf_atanh_f;
    using il2cpp::runtime::icall_mathf_cbrt_f;
    using il2cpp::runtime::icall_mathf_fma_f;
    using il2cpp::runtime::icall_mathf_max_f;
    using il2cpp::runtime::icall_mathf_min_f;
    using il2cpp::runtime::icall_mathf_max_magnitude_f;
    using il2cpp::runtime::icall_mathf_min_magnitude_f;
    using il2cpp::runtime::icall_mathf_sign_i;
    using il2cpp::runtime::icall_mathf_ilogb_i;
    using il2cpp::runtime::icall_mathf_scalb_f;
    using il2cpp::runtime::icall_mathf_ieee_remainder_f;
    using il2cpp::runtime::icall_mathf_modf_f;
    using il2cpp::runtime::icall_mathf_sincos_f;

    
    // --- 1. Sqrt / Abs: exact squares, signed zero, NaN/Inf. ---
    Expect(icall_mathf_sqrt_f(4.0f) == 2.0f, "sqrt(4) == 2 (exact)");
    Expect(icall_mathf_sqrt_f(9.0f) == 3.0f, "sqrt(9) == 3 (exact)");
    Expect(icall_mathf_sqrt_f(16.0f) == 4.0f, "sqrt(16) == 4 (exact)");
    Expect(icall_mathf_sqrt_f(0.0f) == 0.0f, "sqrt(0) == +0 (exact)");
    Expect(!std::signbit(icall_mathf_sqrt_f(0.0f)), "sqrt(+0) is +0");
    Expect(std::signbit(icall_mathf_sqrt_f(-0.0f)), "sqrt(-0.0) preserves the sign");
    Expect(icall_mathf_sqrt_f(kNan) != icall_mathf_sqrt_f(kNan), "sqrt(NaN) is NaN");
    Expect(icall_mathf_sqrt_f(INFINITY) == INFINITY, "sqrt(+inf) == +inf");
    Expect(icall_mathf_sqrt_f(-1.0f) != icall_mathf_sqrt_f(-1.0f), "sqrt(-1) is NaN (no exception)");
    ExpectNearF(icall_mathf_sqrt_f(2.0f), 1.4142135f, "sqrt(2) == 1.4142135");
    Expect(icall_mathf_abs_f(-2.5f) == 2.5f, "abs(-2.5) == 2.5");
    Expect(icall_mathf_abs_f(2.5f) == 2.5f, "abs(2.5) == 2.5");
    Expect(icall_mathf_abs_f(0.0f) == 0.0f, "abs(0) == 0");
    Expect(!std::signbit(icall_mathf_abs_f(-0.0f)), "abs(-0.0) == +0.0 (signbit cleared)");
    Expect(icall_mathf_abs_f(kNan) != icall_mathf_abs_f(kNan), "abs(NaN) is NaN");
    Expect(icall_mathf_abs_f(-INFINITY) == INFINITY, "abs(-inf) == +inf");

    // --- 2. Trig: lattice values + float references (tol 1e-6). ---
    Expect(icall_mathf_sin_f(0.0f) == 0.0f, "sin(0) == 0 (exact)");
    Expect(icall_mathf_cos_f(0.0f) == 1.0f, "cos(0) == 1 (exact)");
    Expect(icall_mathf_tan_f(0.0f) == 0.0f, "tan(0) == 0 (exact)");
    Expect(icall_mathf_sin_f(kPiOver2) == 1.0f, "sin(pi/2) == 1 (lattice)");
    Expect(icall_mathf_cos_f(kPi) == -1.0f, "cos(pi) == -1 (lattice)");
    ExpectNearF(icall_mathf_sin_f(1.0f), 0.84147096f, "sin(1) == 0.84147096");
    ExpectNearF(icall_mathf_cos_f(1.0f), 0.54030228f, "cos(1) == 0.54030228");
    ExpectNearF(icall_mathf_tan_f(1.0f), 1.5574077f, "tan(1) == 1.5574077");
    Expect(icall_mathf_sin_f(kNan) != icall_mathf_sin_f(kNan), "sin(NaN) is NaN");
    Expect(icall_mathf_cos_f(kNan) != icall_mathf_cos_f(kNan), "cos(NaN) is NaN");
    Expect(icall_mathf_tan_f(kNan) != icall_mathf_tan_f(kNan), "tan(NaN) is NaN");
    Expect(icall_mathf_sin_f(INFINITY) != icall_mathf_sin_f(INFINITY), "sin(+inf) is NaN (no exception)");
    Expect(icall_mathf_cos_f(-INFINITY) != icall_mathf_cos_f(-INFINITY), "cos(-inf) is NaN");
    Expect(icall_mathf_tan_f(INFINITY) != icall_mathf_tan_f(INFINITY), "tan(+inf) is NaN");

    // --- 3. Inverse trig: references, |x| > 1 -> NaN, signed zero in Atan2. ---
    ExpectNearF(icall_mathf_asin_f(0.5f), 0.52359878f, "asin(0.5) == 0.52359878");
    ExpectNearF(icall_mathf_acos_f(0.5f), 1.0471976f, "acos(0.5) == 1.0471976");
    ExpectNearF(icall_mathf_atan_f(1.0f), 0.78539816f, "atan(1) == pi/4");
    ExpectNearF(icall_mathf_atan2_f(1.0f, 1.0f), 0.78539816f, "atan2(1,1) == pi/4");
    ExpectNearF(icall_mathf_atan2_f(-3.0f, -4.0f), -2.4980917f, "atan2(-3,-4) == -2.4980917");
    Expect(icall_mathf_asin_f(2.0f) != icall_mathf_asin_f(2.0f), "asin(2) is NaN (domain, no exception)");
    Expect(icall_mathf_acos_f(-2.0f) != icall_mathf_acos_f(-2.0f), "acos(-2) is NaN (domain)");
    Expect(icall_mathf_atan2_f(-0.0f, -1.0f) == -kPi, "atan2(-0.0,-1) == -pi (signed zero)");
    Expect(std::signbit(icall_mathf_atan2_f(-0.0f, 1.0f)), "atan2(-0.0,1) is -0.0 (signbit)");

    // --- 4. Exp / Log / Log2 / Log10 / Pow. ---
    Expect(icall_mathf_exp_f(0.0f) == 1.0f, "exp(0) == 1 (exact)");
    ExpectNearF(icall_mathf_exp_f(1.0f), 2.7182817f, "exp(1) == e (float)");
    ExpectNearF(icall_mathf_exp_f(-1.0f), 0.36787944f, "exp(-1) == 0.36787944");
    Expect(icall_mathf_exp_f(1000.0f) == INFINITY, "exp(1000) == +inf (overflow)");
    Expect(icall_mathf_exp_f(-1000.0f) == 0.0f, "exp(-1000) == 0 (underflow)");
    Expect(icall_mathf_log_f(1.0f) == 0.0f, "log(1) == 0 (exact)");
    ExpectNearF(icall_mathf_log_f(3.0f), 1.0986123f, "log(3) == 1.0986123");
    Expect(icall_mathf_log_f(0.0f) == -INFINITY, "log(0) == -inf");
    Expect(icall_mathf_log_f(-1.0f) != icall_mathf_log_f(-1.0f), "log(-1) is NaN (no exception)");
    Expect(icall_mathf_log2_f(1.0f) == 0.0f, "log2(1) == 0 (exact)");
    Expect(icall_mathf_log2_f(8.0f) == 3.0f, "log2(8) == 3 (exact)");
    Expect(icall_mathf_log2_f(0.5f) == -1.0f, "log2(0.5) == -1 (exact)");
    Expect(icall_mathf_log2_f(0.0f) == -INFINITY, "log2(0) == -inf");
    Expect(icall_mathf_log2_f(-1.0f) != icall_mathf_log2_f(-1.0f), "log2(-1) is NaN");
    Expect(icall_mathf_log10_f(1.0f) == 0.0f, "log10(1) == 0 (exact)");
    Expect(icall_mathf_log10_f(10.0f) == 1.0f, "log10(10) == 1 (exact)");
    ExpectNearF(icall_mathf_log10_f(7.0f), 0.84509804f, "log10(7) == 0.84509804");
    Expect(icall_mathf_log10_f(0.0f) == -INFINITY, "log10(0) == -inf");
    Expect(icall_mathf_log10_f(-1.0f) != icall_mathf_log10_f(-1.0f), "log10(-1) is NaN");
    Expect(icall_mathf_pow_f(2.0f, 10.0f) == 1024.0f, "pow(2,10) == 1024 (exact)");
    Expect(icall_mathf_pow_f(2.0f, -1.0f) == 0.5f, "pow(2,-1) == 0.5");
    Expect(icall_mathf_pow_f(0.0f, 0.0f) == 1.0f, "pow(0,0) == 1");
    Expect(icall_mathf_pow_f(1.0f, 12345.0f) == 1.0f, "pow(1,any) == 1");
    Expect(icall_mathf_pow_f(4.0f, 0.5f) == 2.0f, "pow(4,0.5) == 2");
    ExpectNearF(icall_mathf_pow_f(1.5f, 2.5f), 2.755676f, "pow(1.5,2.5) == 2.755676");

    // --- 5. Log(x, y) base: the corert MathF.cs check order. ---
    ExpectNearF(icall_mathf_log_base_f(3.0f, 2.0f), 1.5849625f, "log(3,2) == 1.5849625");
    ExpectNearF(icall_mathf_log_base_f(5.0f, 10.0f), 0.69897f, "log(5,10) == 0.69897");
    Expect(icall_mathf_log_base_f(1.0f, 1.0f) != icall_mathf_log_base_f(1.0f, 1.0f),
           "log(1,1) is NaN (y==1 wins over x==1)");
    Expect(icall_mathf_log_base_f(2.0f, 1.0f) != icall_mathf_log_base_f(2.0f, 1.0f),
           "log(2,1) is NaN");
    Expect(icall_mathf_log_base_f(2.0f, 0.0f) != icall_mathf_log_base_f(2.0f, 0.0f),
           "log(2,0) is NaN");
    Expect(icall_mathf_log_base_f(2.0f, INFINITY) != icall_mathf_log_base_f(2.0f, INFINITY),
           "log(2,+inf) is NaN");
    // Log(1, 0) == -0.0 and Log(1, +inf) == +0 (the a==1 path does a REAL
    // division 0 / log(y): 0/-inf == -0.0, 0/+inf == +0).
    Expect(std::signbit(icall_mathf_log_base_f(1.0f, 0.0f)),
           "log(1,0) is -0.0 (signbit; 0/-inf real division)");
    Expect(!std::signbit(icall_mathf_log_base_f(1.0f, INFINITY)),
           "log(1,+inf) is +0 (0/+inf)");
    Expect(icall_mathf_log_base_f(kNan, 2.0f) != icall_mathf_log_base_f(kNan, 2.0f),
           "log(NaN,2) is NaN (x payload wins)");
    Expect(icall_mathf_log_base_f(2.0f, kNan) != icall_mathf_log_base_f(2.0f, kNan),
           "log(2,NaN) is NaN (y payload wins)");
    Expect(icall_mathf_log_base_f(0.0f, 2.0f) == -INFINITY, "log(0,2) == -inf (natural division)");
    Expect(icall_mathf_log_base_f(-1.0f, 2.0f) != icall_mathf_log_base_f(-1.0f, 2.0f),
           "log(-1,2) is NaN (ln(-1))");

    // --- 6. Ceiling / Floor / Round / Truncate. ---
    Expect(icall_mathf_floor_f(3.7f) == 3.0f, "floor(3.7) == 3");
    Expect(icall_mathf_floor_f(-3.7f) == -4.0f, "floor(-3.7) == -4");
    Expect(icall_mathf_floor_f(3.0f) == 3.0f, "floor(3.0) == 3 (integral)");
    Expect(icall_mathf_ceiling_f(3.2f) == 4.0f, "ceiling(3.2) == 4");
    Expect(icall_mathf_ceiling_f(-3.2f) == -3.0f, "ceiling(-3.2) == -3");
    Expect(icall_mathf_ceiling_f(3.0f) == 3.0f, "ceiling(3.0) == 3 (integral)");
    // Round: banker's (ToEven) -- std::roundf would round halves away.
    Expect(icall_mathf_round_f(0.5f) == 0.0f, "round(0.5) == 0 (to even)");
    Expect(icall_mathf_round_f(1.5f) == 2.0f, "round(1.5) == 2 (to even)");
    Expect(icall_mathf_round_f(2.5f) == 2.0f, "round(2.5) == 2 (to even)");
    Expect(icall_mathf_round_f(3.5f) == 4.0f, "round(3.5) == 4 (to even)");
    Expect(icall_mathf_round_f(-1.5f) == -2.0f, "round(-1.5) == -2 (to even)");
    Expect(icall_mathf_round_f(-2.5f) == -2.0f, "round(-2.5) == -2 (to even)");
    Expect(icall_mathf_round_f(1.4f) == 1.0f, "round(1.4) == 1");
    Expect(icall_mathf_round_f(1.6f) == 2.0f, "round(1.6) == 2");
    Expect(icall_mathf_round_f(-1.4f) == -1.0f, "round(-1.4) == -1");
    Expect(std::signbit(icall_mathf_round_f(-0.5f)), "round(-0.5) is -0.0 (signbit)");
    Expect(icall_mathf_round_f(0x1.0p23f) == 0x1.0p23f, "round(2^23) passes through (no fraction)");
    Expect(icall_mathf_round_f(kNan) != icall_mathf_round_f(kNan), "round(NaN) is NaN");
    Expect(icall_mathf_round_f(INFINITY) == INFINITY, "round(+inf) is +inf");
    Expect(icall_mathf_round_f(-INFINITY) == -INFINITY, "round(-inf) is -inf");
    Expect(icall_mathf_truncate_f(2.7f) == 2.0f, "truncate(2.7) == 2");
    Expect(icall_mathf_truncate_f(-2.7f) == -2.0f, "truncate(-2.7) == -2");
    Expect(std::signbit(icall_mathf_truncate_f(-0.5f)), "truncate(-0.5) is -0.0 (signbit)");
    Expect(icall_mathf_truncate_f(3.0f) == 3.0f, "truncate(3.0) == 3 (integral)");

    // --- 7. Round(value, digits): corert multiply-scale contract. ---
    Expect(icall_mathf_round_digits_f(2.675f, 2) == 2.68f,
           "round(2.675,2) == 2.68 (float(2.675)*100 rounds to the exact midpoint 267.5, even neighbour 268)");
    Expect(icall_mathf_round_digits_f(12.55f, 1) == 12.6f,
           "round(12.55,1) == 12.6 (float(12.55)*10 == 125.5, even neighbour 126)");
    Expect(icall_mathf_round_digits_f(2.5f, 0) == 2.0f, "round(2.5,0) == 2 (banker's at 0 digits)");
    Expect(icall_mathf_round_digits_f(2.0e8f, 2) == 2.0e8f,
           "round(2e8,2) == 2e8 (|value| >= 1e8 passthrough)");
    Expect(icall_mathf_round_digits_f(kNan, 2) != icall_mathf_round_digits_f(kNan, 2),
           "round(NaN,2) is NaN");
    Expect(icall_mathf_round_digits_f(INFINITY, 2) == INFINITY, "round(+inf,2) == +inf");
    ExpectDotNetException("round(x,-1) throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception",
                          "Rounding digits",
                          [] { (void)NoThrowRoundDigitsF(1.5f, -1); });
    ExpectDotNetException("round(x,7) throws ArgumentOutOfRangeException",
                          "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception",
                          "Rounding digits",
                          [] { (void)NoThrowRoundDigitsF(1.5f, 7); });

    // --- 8. Hyperbolics + inverse hyperbolics + Cbrt. ---
    Expect(icall_mathf_cosh_f(0.0f) == 1.0f, "cosh(0) == 1 (exact)");
    Expect(icall_mathf_sinh_f(0.0f) == 0.0f, "sinh(0) == 0 (exact)");
    Expect(icall_mathf_tanh_f(0.0f) == 0.0f, "tanh(0) == 0 (exact)");
    ExpectNearF(icall_mathf_cosh_f(2.0f), 3.7621956f, "cosh(2) == 3.7621956");
    ExpectNearF(icall_mathf_sinh_f(2.0f), 3.6268604f, "sinh(2) == 3.6268604");
    ExpectNearF(icall_mathf_tanh_f(2.0f), 0.9640276f, "tanh(2) == 0.9640276");
    Expect(icall_mathf_acosh_f(1.0f) == 0.0f, "acosh(1) == 0 (exact)");
    ExpectNearF(icall_mathf_acosh_f(2.0f), 1.3169579f, "acosh(2) == 1.3169579");
    Expect(icall_mathf_acosh_f(0.5f) != icall_mathf_acosh_f(0.5f), "acosh(0.5) is NaN (domain x < 1)");
    Expect(icall_mathf_asinh_f(0.0f) == 0.0f, "asinh(0) == 0 (exact)");
    ExpectNearF(icall_mathf_asinh_f(1.0f), 0.8813736f, "asinh(1) == 0.8813736");
    Expect(std::signbit(icall_mathf_asinh_f(-0.0f)), "asinh(-0.0) preserves the sign");
    Expect(icall_mathf_atanh_f(0.0f) == 0.0f, "atanh(0) == 0 (exact)");
    ExpectNearF(icall_mathf_atanh_f(0.5f), 0.54930615f, "atanh(0.5) == 0.54930615");
    Expect(icall_mathf_atanh_f(1.0f) == INFINITY, "atanh(1) == +inf");
    Expect(icall_mathf_atanh_f(-1.0f) == -INFINITY, "atanh(-1) == -inf");
    Expect(icall_mathf_atanh_f(2.0f) != icall_mathf_atanh_f(2.0f), "atanh(2) is NaN (domain |x| > 1)");
    Expect(icall_mathf_cbrt_f(8.0f) == 2.0f, "cbrt(8) == 2 (exact)");
    Expect(icall_mathf_cbrt_f(-8.0f) == -2.0f, "cbrt(-8) == -2 (odd root)");
    Expect(icall_mathf_cbrt_f(27.0f) == 3.0f, "cbrt(27) == 3 (exact)");
    Expect(std::signbit(icall_mathf_cbrt_f(-0.0f)), "cbrt(-0.0) preserves the sign");
    Expect(icall_mathf_cbrt_f(-INFINITY) == -INFINITY, "cbrt(-inf) == -inf");

    // --- 9. FusedMultiplyAdd: the portable soft fma. ---
    Expect(icall_mathf_fma_f(2.0f, 3.0f, 4.0f) == 10.0f, "fma(2,3,4) == 10 (exact)");
    Expect(icall_mathf_fma_f(1.0f, 1.0f, 0.0f) == 1.0f, "fma(1,1,0) == 1");
    Expect(icall_mathf_fma_f(0.1f, 0.2f, 0.3f) == 0.3200000226f,
           "fma(0.1,0.2,0.3) == 0.3200000226 (single rounding, float; the float 0.32f = 0.3199999928 is one ulp below the correctly-rounded sum)");
    // The single-rounding discriminator: a NON-fused path computes
    // float(x*x) == 1+2^-22 and the sum collapses to 0; the correctly-rounded
    // fma keeps the exact 2^-46 tail.
    Expect(icall_mathf_fma_f(0x1.000002p0f, 0x1.000002p0f, -0x1.000004p0f) == 0x1.0p-46f,
           "fma((1+2^-23)^2, -(1+2^-22)) == 2^-46 (single rounding; non-fused gives 0)");
    // Sum overflow: p and z are both finite but their exact sum exceeds float
    // range -- the correctly-rounded fma is +inf (the soft path's guard).
    Expect(icall_mathf_fma_f(3.0e38f, 1.0f, 3.0e38f) == INFINITY,
           "fma(3e38,1,3e38) == +inf (exact sum overflows)");
    // The |x| >= 2^64 scaled path (Veltkamp split constant overflow avoidance).
    Expect(icall_mathf_fma_f(1e37f, 1.0f, 0.0f) == 1e37f,
           "fma(1e37,1,0) == 1e37 (scaled soft path)");
    // z dominates: the TwoSum keeps the exact large term.
    Expect(icall_mathf_fma_f(2.0f, 3.0f, 1e38f) == 1e38f, "fma(2,3,1e38) == 1e38 (z dominates)");
    Expect(icall_mathf_fma_f(kNan, 1.0f, 1.0f) != icall_mathf_fma_f(kNan, 1.0f, 1.0f),
           "fma(NaN,x,z) is NaN");
    Expect(icall_mathf_fma_f(INFINITY, 1.0f, 1.0f) == INFINITY, "fma(inf,1,1) == +inf");

    // --- 10. Max/Min: IEEE-754-2019 NaN propagation + +0 > -0. ---
    Expect(icall_mathf_max_f(1.5f, 2.5f) == 2.5f, "max(1.5,2.5) == 2.5");
    Expect(icall_mathf_max_f(2.5f, 1.5f) == 2.5f, "max(2.5,1.5) == 2.5");
    Expect(icall_mathf_min_f(1.5f, 2.5f) == 1.5f, "min(1.5,2.5) == 1.5");
    Expect(icall_mathf_min_f(2.5f, 1.5f) == 1.5f, "min(2.5,1.5) == 1.5");
    Expect(icall_mathf_max_f(3.0f, 3.0f) == 3.0f, "max tie returns the value");
    // IEEE-754-2019: NaN propagates from EITHER position in Max ...
    Expect(icall_mathf_max_f(kNan, 5.0f) != icall_mathf_max_f(kNan, 5.0f),
           "max(NaN,5) is NaN (IEEE-754-2019, unlike net48 double)");
    Expect(icall_mathf_max_f(5.0f, kNan) != icall_mathf_max_f(5.0f, kNan),
           "max(5,NaN) is NaN");
    // ... but Min is the corert asymmetric port: Min(NaN, x) == x (the NaN
    // falls through to the signbit test) while Min(x, NaN) == NaN.
    Expect(icall_mathf_min_f(kNan, 5.0f) == 5.0f,
           "min(NaN,5) == 5 (corert asymmetric: NaN loses as first operand)");
    Expect(icall_mathf_min_f(5.0f, kNan) != icall_mathf_min_f(5.0f, kNan),
           "min(5,NaN) is NaN");
    // Signed-zero ties: +0 is the IEEE-754-2019 larger value.
    Expect(!std::signbit(icall_mathf_max_f(-0.0f, 0.0f)), "max(-0,+0) == +0 (signbit)");
    Expect(!std::signbit(icall_mathf_max_f(0.0f, -0.0f)), "max(+0,-0) == +0 (signbit)");
    Expect(std::signbit(icall_mathf_min_f(-0.0f, 0.0f)), "min(-0,+0) == -0 (signbit)");
    Expect(std::signbit(icall_mathf_min_f(0.0f, -0.0f)), "min(+0,-0) == -0 (signbit)");

    // --- 11. MaxMagnitude / MinMagnitude: magnitude order, NaN either side,
    // +0 larger / -0 smaller. ---
    Expect(icall_mathf_max_magnitude_f(3.0f, -5.0f) == -5.0f, "maxmag(3,-5) == -5 (larger magnitude)");
    Expect(icall_mathf_max_magnitude_f(-5.0f, 3.0f) == -5.0f, "maxmag(-5,3) == -5");
    Expect(icall_mathf_min_magnitude_f(3.0f, -5.0f) == 3.0f, "minmag(3,-5) == 3 (smaller magnitude)");
    Expect(icall_mathf_min_magnitude_f(-5.0f, 3.0f) == 3.0f, "minmag(-5,3) == 3");
    Expect(icall_mathf_max_magnitude_f(3.0f, 3.0f) == 3.0f, "maxmag tie returns the value");
    Expect(icall_mathf_max_magnitude_f(kNan, 5.0f) != icall_mathf_max_magnitude_f(kNan, 5.0f),
           "maxmag(NaN,5) is NaN");
    Expect(icall_mathf_max_magnitude_f(5.0f, kNan) != icall_mathf_max_magnitude_f(5.0f, kNan),
           "maxmag(5,NaN) is NaN");
    Expect(icall_mathf_min_magnitude_f(kNan, 5.0f) != icall_mathf_min_magnitude_f(kNan, 5.0f),
           "minmag(NaN,5) is NaN");
    Expect(icall_mathf_min_magnitude_f(5.0f, kNan) != icall_mathf_min_magnitude_f(5.0f, kNan),
           "minmag(5,NaN) is NaN");
    Expect(!std::signbit(icall_mathf_max_magnitude_f(0.0f, -0.0f)),
           "maxmag(+0,-0) == +0 (larger magnitude)");
    Expect(!std::signbit(icall_mathf_max_magnitude_f(-0.0f, 0.0f)),
           "maxmag(-0,+0) == +0 (larger magnitude)");
    Expect(std::signbit(icall_mathf_min_magnitude_f(0.0f, -0.0f)),
           "minmag(+0,-0) == -0 (smaller magnitude)");
    Expect(std::signbit(icall_mathf_min_magnitude_f(-0.0f, 0.0f)),
           "minmag(-0,+0) == -0 (smaller magnitude)");

    // --- 12. Sign: -1 / 0 / 1; NaN throws ArithmeticException. ---
    Expect(icall_mathf_sign_i(-9.0f) == -1, "sign(-9) == -1");
    Expect(icall_mathf_sign_i(9.0f) == 1, "sign(9) == 1");
    Expect(icall_mathf_sign_i(0.0f) == 0, "sign(+0) == 0");
    Expect(icall_mathf_sign_i(-0.0f) == 0, "sign(-0) == 0");
    ExpectDotNetException("sign(NaN) throws ArithmeticException",
                          "System.ArithmeticException,System.ArithmeticException,System.SystemException,System.Exception",
                          "Not-a-Number",
                          [] { (void)NoThrowSignF(kNan); });

    // --- 13. ILogB / ScaleB. ---
    Expect(icall_mathf_ilogb_i(1.0f) == 0, "ilogb(1) == 0");
    Expect(icall_mathf_ilogb_i(2.0f) == 1, "ilogb(2) == 1");
    Expect(icall_mathf_ilogb_i(8.0f) == 3, "ilogb(8) == 3");
    Expect(icall_mathf_ilogb_i(0.5f) == -1, "ilogb(0.5) == -1");
    Expect(icall_mathf_ilogb_i(0.25f) == -2, "ilogb(0.25) == -2");
    // Platform-adaptive sentinels: ilogb returns the C FP_ILOGBNAN /
    // FP_ILOGB0 values, which differ across libcs (NaN sentinel is INT_MAX
    // on UCRT/MinGW but INT_MIN on glibc). Compare against the target's own
    // macros, never a fixed literal.
    Expect(icall_mathf_ilogb_i(kNan) == FP_ILOGBNAN,
           "ilogb(NaN) == FP_ILOGBNAN (platform sentinel)");
    // +/-Infinity: empirically INT_MAX on every target libc (UCRT, MinGW,
    // glibc -- glibc does NOT route infinity through FP_ILOGBNAN).
    Expect(icall_mathf_ilogb_i(INFINITY) == INT32_MAX, "ilogb(+inf) == INT32_MAX");
    Expect(icall_mathf_ilogb_i(0.0f) == FP_ILOGB0,
           "ilogb(0) == FP_ILOGB0 (INT_MIN on every target)");
    Expect(icall_mathf_scalb_f(3.0f, 2) == 12.0f, "scaleb(3,2) == 12");
    Expect(icall_mathf_scalb_f(1.0f, 0) == 1.0f, "scaleb(1,0) == 1");
    Expect(icall_mathf_scalb_f(1.0f, -1) == 0.5f, "scaleb(1,-1) == 0.5");
    Expect(icall_mathf_scalb_f(2.5f, 3) == 20.0f, "scaleb(2.5,3) == 20");
    Expect(icall_mathf_scalb_f(1.0f, 1000) == INFINITY, "scaleb(1,1000) == +inf (overflow)");
    Expect(icall_mathf_scalb_f(1.0f, -1000) == 0.0f, "scaleb(1,-1000) == 0 (underflow)");
    Expect(icall_mathf_scalb_f(kNan, 5) != icall_mathf_scalb_f(kNan, 5), "scaleb(NaN,5) is NaN");

    // --- 14. IEEERemainder (std::remainderf -- the IEEE remainder). ---
    Expect(icall_mathf_ieee_remainder_f(5.5f, 2.0f) == -0.5f, "ier(5.5,2) == -0.5 (nearest quotient 3)");
    Expect(icall_mathf_ieee_remainder_f(5.0f, 2.0f) == 1.0f, "ier(5,2) == 1 (2.5 ties to quotient 2)");
    Expect(icall_mathf_ieee_remainder_f(3.0f, 2.0f) == -1.0f, "ier(3,2) == -1");
    Expect(icall_mathf_ieee_remainder_f(-3.0f, 2.0f) == 1.0f, "ier(-3,2) == 1");
    Expect(icall_mathf_ieee_remainder_f(3.0f, -2.0f) == -1.0f, "ier(3,-2) == -1");
    Expect(icall_mathf_ieee_remainder_f(0.0f, 3.0f) == 0.0f, "ier(0,3) == +0");
    Expect(std::signbit(icall_mathf_ieee_remainder_f(-0.0f, 3.0f)),
           "ier(-0,3) preserves the sign");
    Expect(icall_mathf_ieee_remainder_f(3.0f, 0.0f) != icall_mathf_ieee_remainder_f(3.0f, 0.0f),
           "ier(3,0) is NaN");
    Expect(icall_mathf_ieee_remainder_f(3.0f, INFINITY) == 3.0f, "ier(3,+inf) == 3");
    Expect(icall_mathf_ieee_remainder_f(INFINITY, 3.0f) != icall_mathf_ieee_remainder_f(INFINITY, 3.0f),
           "ier(+inf,3) is NaN");
    Expect(icall_mathf_ieee_remainder_f(kNan, 2.0f) != icall_mathf_ieee_remainder_f(kNan, 2.0f),
           "ier(NaN,2) is NaN");

    // --- 15. ModF / SinCos (pointer-slot forms, dormant mappings). ---
    {
        double intPart = 0.0;
        const float frac3_7 = icall_mathf_modf_f(3.7f, &intPart);
        Expect(frac3_7 == (3.7f - 3.0f) && intPart == 3.0,
                "modf(3.7) -> frac 3.7f-3 (0.70000005), int 3 (modf is exact)");
        const float fracN3_7 = icall_mathf_modf_f(-3.7f, &intPart);
        Expect(fracN3_7 == -(3.7f - 3.0f) && intPart == -3.0,
                "modf(-3.7) -> frac -(3.7f-3), int -3 (exact)");
        const float fracN0_5 = icall_mathf_modf_f(-0.5f, &intPart);
        Expect(fracN0_5 == -0.5f && std::signbit(intPart), "modf(-0.5) -> frac -0.5, int -0 (sign)");
        const float fracInf = icall_mathf_modf_f(INFINITY, &intPart);
        Expect(fracInf == 0.0f && intPart == INFINITY, "modf(+inf) -> frac +0, int +inf");
        const float fracNan = icall_mathf_modf_f(kNan, &intPart);
        Expect(fracNan != fracNan && intPart != intPart, "modf(NaN) -> (NaN, NaN)");
    }
    {
        double sinOut = 0.0;
        double cosOut = 0.0;
        icall_mathf_sincos_f(0.0f, &sinOut, &cosOut);
        Expect(sinOut == 0.0 && cosOut == 1.0, "sincos(0) -> sin 0, cos 1");
        icall_mathf_sincos_f(kPiOver2, &sinOut, &cosOut);
        Expect(sinOut == 1.0, "sincos(pi/2) sin == 1");
        Expect(std::fabs(cosOut) < 1e-6, "sincos(pi/2) cos is ~0");
        icall_mathf_sincos_f(kNan, &sinOut, &cosOut);
        Expect(sinOut != sinOut && cosOut != cosOut, "sincos(NaN) -> (NaN, NaN)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d mathf assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime mathf tests passed (corert MathF semantics)\n");
    return 0;
}
