//------------------------------------------------------------------------------
// test_math_extra.cpp -- System.Math precision-against-net48 verification.
//
// The icall implementations themselves live in il2cpp_math.h (Abs/Max/Min/
// Sign/Clamp/Sqrt/Pow/Floor/Ceiling/Round) and il2cpp_math_ext.h
// (Sin/Cos/Tan/Asin/Acos/Atan/Atan2/Exp/Log/Log10/Log-base/Truncate/Cosh/
// Sinh/Tanh/IEEERemainder) -- this file adds what test_math.cpp /
// test_mathext.cpp do not cover: arbitrary-value PRECISION probes against
// known .NET Framework 4.8 reference values (the exact doubles net48
// returns, captured via its "R" round-trip formatting), tolerance 1e-15
// (absolute for |expected| <= 1, scaled by |expected| above that).
//
// Reference values used below (all ToString("R") outputs of a real .NET
// Framework 4.8 process):
//   sin(1.0)  = 0.8414709848078965
//   cos(1.0)  = 0.5403023058681398
//   tan(1.0)  = 1.5574077246549023
//   asin(0.5) = 0.5235987755982989
//   acos(0.5) = 1.0471975511965979
//   atan(1.5) = 0.982793723247329
//   atan2(3.0, 4.0) = 0.6435011087932844
//   exp(0.5)  = 1.6487212707001282
//   log(3.0)  = 1.0986122886681098
//   log10(7.0) = 0.8450980400142568
//   sqrt(2.0) = 1.4142135623730951
//   pow(1.5, 2.5) = 2.7556759606310752
//   log(3.0, 2.0) = 1.5849625007211563
//   cosh(2.0) = 3.7621956910836314
//   sinh(2.0) = 3.6268604078470188
//   tanh(2.0) = 0.9640275800758169
//
// Tolerances: 1e-15 absolute (values with |expected| <= 1), scaled by
// |expected| otherwise. This admits any correctly-rounded libm; the net48
// values are correct to 1 ulp, so a conforming libm lands within ~2 ulps
// (5e-16) -- well inside the bound.
//------------------------------------------------------------------------------

#include "il2cpp_math.h"
#include "il2cpp_math_ext.h"

#include <cmath>
#include <cstdint>
#include <cstdio>

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

// net48-precision probe: |actual - expected| <= 1e-15 * max(1, |expected|).
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
    using il2cpp::runtime::icall_math_sqrt_d;
    using il2cpp::runtime::icall_math_pow_d;
    using il2cpp::runtime::icall_math_floor_d;
    using il2cpp::runtime::icall_math_ceiling_d;
    using il2cpp::runtime::icall_math_round_d;
    using il2cpp::runtime::icall_math_abs_d;
    using il2cpp::runtime::icall_math_max_d;
    using il2cpp::runtime::icall_math_min_d;

    // --- Trig: arbitrary-value net48 references, tol 1e-15. ---
    ExpectNear(icall_math_sin_d(1.0), 0.8414709848078965, "sin(1.0) == 0.8414709848078965");
    ExpectNear(icall_math_sin_d(0.5), 0.479425538604203, "sin(0.5) == 0.479425538604203");
    ExpectNear(icall_math_cos_d(1.0), 0.5403023058681398, "cos(1.0) == 0.5403023058681398");
    ExpectNear(icall_math_cos_d(0.5), 0.8775825618903728, "cos(0.5) == 0.8775825618903728");
    ExpectNear(icall_math_tan_d(1.0), 1.5574077246549023, "tan(1.0) == 1.5574077246549023");
    ExpectNear(icall_math_tan_d(0.5), 0.5463024898437905, "tan(0.5) == 0.5463024898437905");
    Expect(icall_math_sin_d(0.0) == 0.0, "sin(0.0) == 0 (exact)");
    Expect(icall_math_cos_d(0.0) == 1.0, "cos(0.0) == 1 (exact)");
    Expect(icall_math_tan_d(0.0) == 0.0, "tan(0.0) == 0 (exact)");

    // --- Inverse trig. ---
    ExpectNear(icall_math_asin_d(0.5), 0.5235987755982989, "asin(0.5) == 0.5235987755982989");
    ExpectNear(icall_math_asin_d(0.3), 0.3046926540153975, "asin(0.3) == 0.3046926540153975");
    ExpectNear(icall_math_acos_d(0.5), 1.0471975511965979, "acos(0.5) == 1.0471975511965979");
    ExpectNear(icall_math_acos_d(0.3), 1.2661036727794992, "acos(0.3) == 1.2661036727794992");
    ExpectNear(icall_math_atan_d(1.5), 0.982793723247329, "atan(1.5) == 0.982793723247329");
    ExpectNear(icall_math_atan_d(2.0), 1.1071487177940904, "atan(2.0) == 1.1071487177940904");
    ExpectNear(icall_math_atan2_d(3.0, 4.0), 0.6435011087932844, "atan2(3,4) == 0.6435011087932844");
    ExpectNear(icall_math_atan2_d(-3.0, -4.0), -2.498091544796509, "atan2(-3,-4) == -2.498091544796509");

    // --- Exp / Log / Log10. ---
    ExpectNear(icall_math_exp_d(0.5), 1.6487212707001282, "exp(0.5) == 1.6487212707001282");
    ExpectNear(icall_math_exp_d(-0.5), 0.6065306597126334, "exp(-0.5) == 0.6065306597126334");
    Expect(icall_math_exp_d(0.0) == 1.0, "exp(0.0) == 1 (exact)");
    ExpectNear(icall_math_log_d(3.0), 1.0986122886681098, "log(3.0) == 1.0986122886681098");
    ExpectNear(icall_math_log_d(0.5), -0.6931471805599453, "log(0.5) == -0.6931471805599453");
    Expect(icall_math_log_d(1.0) == 0.0, "log(1.0) == 0 (exact)");
    ExpectNear(icall_math_log10_d(7.0), 0.8450980400142568, "log10(7.0) == 0.8450980400142568");
    ExpectNear(icall_math_log10_d(0.5), -0.3010299956639812, "log10(0.5) == -0.3010299956639812");
    Expect(icall_math_log10_d(1.0) == 0.0, "log10(1.0) == 0 (exact)");
    ExpectNear(icall_math_log_base_d(3.0, 2.0), 1.5849625007211563, "log(3,2) == 1.5849625007211563");
    ExpectNear(icall_math_log_base_d(5.0, 10.0), 0.6989700043360188, "log(5,10) == 0.6989700043360188");

    // --- Sqrt / Pow (declared in il2cpp_math.h). ---
    ExpectNear(icall_math_sqrt_d(2.0), 1.4142135623730951, "sqrt(2.0) == 1.4142135623730951");
    Expect(icall_math_sqrt_d(4.0) == 2.0, "sqrt(4.0) == 2 (exact)");
    ExpectNear(icall_math_pow_d(1.5, 2.5), 2.7556759606310752, "pow(1.5,2.5) == 2.7556759606310752");
    ExpectNear(icall_math_pow_d(2.0, 0.5), 1.4142135623730951, "pow(2,0.5) == sqrt(2)");
    Expect(icall_math_pow_d(2.0, 10.0) == 1024.0, "pow(2,10) == 1024 (exact)");

    // --- Floor / Ceiling / Round / Abs / Max / Min. ---
    Expect(icall_math_floor_d(2.7182818284590451) == 2.0, "floor(e) == 2");
    Expect(icall_math_floor_d(-2.7182818284590451) == -3.0, "floor(-e) == -3");
    Expect(icall_math_ceiling_d(2.7182818284590451) == 3.0, "ceiling(e) == 3");
    Expect(icall_math_ceiling_d(-2.7182818284590451) == -2.0, "ceiling(-e) == -2");
    Expect(icall_math_round_d(2.5) == 2.0, "round(2.5) == 2 (banker's)");
    Expect(icall_math_round_d(3.5) == 4.0, "round(3.5) == 4 (banker's)");
    Expect(icall_math_round_d(-2.5) == -2.0, "round(-2.5) == -2 (banker's)");
    Expect(icall_math_abs_d(-2.7182818284590451) == 2.7182818284590451, "abs(-e) == e (exact)");
    Expect(icall_math_max_d(1.5, 2.7182818284590451) == 2.7182818284590451, "max(1.5,e) == e");
    Expect(icall_math_min_d(1.5, 2.7182818284590451) == 1.5, "min(1.5,e) == 1.5");

    // --- Hyperbolics + Truncate + IEEERemainder (ext domain). ---
    ExpectNear(icall_math_cosh_d(2.0), 3.7621956910836314, "cosh(2.0) == 3.7621956910836314");
    ExpectNear(icall_math_sinh_d(2.0), 3.6268604078470188, "sinh(2.0) == 3.6268604078470188");
    ExpectNear(icall_math_tanh_d(2.0), 0.9640275800758169, "tanh(2.0) == 0.9640275800758169");
    Expect(icall_math_truncate_d(2.7182818284590451) == 2.0, "truncate(e) == 2");
    Expect(icall_math_truncate_d(-2.7182818284590451) == -2.0, "truncate(-e) == -2");
    ExpectNear(icall_math_ieee_remainder_d(5.5, 2.0), -0.5, "ier(5.5,2) == -0.5 (nearest quotient 3)");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d math_extra assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime math_extra tests passed (net48 precision, tol 1e-15)\n");
    return 0;
}
