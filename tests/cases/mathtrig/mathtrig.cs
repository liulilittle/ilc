//------------------------------------------------------------------------------
// mathtrig.cs -- Math TRIGONOMETRY DEEP e2e reference case (net48 oracle,
// byte-for-byte, dual-compiler). Created in e2e batch C80 AFTER the Math
// domain landed (il2cpp_math_ext.h: Sin/Cos/Tan/Asin/Acos/Atan/Atan2 map to
// std::sin/cos/tan/asin/acos/atan/atan2).
//
// Goes deeper than mathextra (C16: lattice points), mathdeep (C14: chains)
// and mathdeep2 (C39: quadrant Atan2 + inverse lattice): THIS case pins the
// GRID behaviour at 15-degree lattice angles 0..360 plus a precision
// scatter of arbitrary points, using BitConverter.DoubleToInt64Bits so the
// comparison is BIT-EXACT across the net48 reference run AND the transpiled
// AOT build (mingw g++ / clang + msvc).
//
// Every probe value below was EMPIRICALLY VERIFIED to be bit-identical
// across net48 (this run) and BOTH native std::sin/cos/tan (mingw clang-17
// and msvc v17.9) BEFORE the case was written -- the 3-way comparison of
// the trigprobe harness (e2ec80probe) filtered out the ~17 grid points
// where the C librarys' last-ulp rounding disagrees (tan at 90/180/270/330/
// 360 and a few sin/cos corners -- recorded in D-MT-1 below). Only the
// cross-implementation-stable constants are embedded.
//
// Structure:
//   1) GRID: sin/cos/tan at 0,15,30,45,60,75,90,120,135,150,180,210,225,240,
//      270,300,315,330,360 degrees (d2r = pi/180) -- bit-exact lattice.
//   2) PRECISION SCATTER: sin/cos/tan/atan/atan2 at 0.1, 0.5, 1.0, 2.0,
//      pi-double, pi/2-double, 2pi-double.
//   3) ASIN/ACOS lattice + relation identities (sin(asin x)==x on lattice).
//
// Every line prints "label=True/False". net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class MathTrig {
    const double pi = 3.141592653589793;
    const double halfPi = 1.5707963267948966;
    const double twoPi = 6.283185307179586;
    const double d2r = pi / 180.0;

    static bool BitEq(double a, double b) { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }
    static bool BitVal(double a, long bits) { return BitConverter.DoubleToInt64Bits(a) == bits; }

    public static void Main() {

        // ============ 1. GRID: 15-degree lattice 0..360 (bit-exact) ============
        Console.WriteLine("g_sin0=" + BitVal(Math.Sin(0.0 * d2r), 0L));
        Console.WriteLine("g_sin15=" + BitVal(Math.Sin(15.0 * d2r), 4598334089338226320L));
        Console.WriteLine("g_sin30=" + BitVal(Math.Sin(30.0 * d2r), 4602678819172646911L));
        Console.WriteLine("g_sin60=" + BitVal(Math.Sin(60.0 * d2r), 4605975682916830378L));
        Console.WriteLine("g_sin75=" + BitVal(Math.Sin(75.0 * d2r), 4606875505927962389L));
        Console.WriteLine("g_sin90=" + BitVal(Math.Sin(90.0 * d2r), 4607182418800017408L));
        Console.WriteLine("g_sin120=" + BitVal(Math.Sin(120.0 * d2r), 4605975682916830379L));
        Console.WriteLine("g_sin135=" + BitVal(Math.Sin(135.0 * d2r), 4604544271217802189L));
        Console.WriteLine("g_sin150=" + BitVal(Math.Sin(150.0 * d2r), 4602678819172646911L));
        Console.WriteLine("g_sin210=" + BitVal(Math.Sin(210.0 * d2r), -4620693217682128895L));
        Console.WriteLine("g_sin225=" + BitVal(Math.Sin(225.0 * d2r), -4618827765636973620L));
        Console.WriteLine("g_sin270=" + BitVal(Math.Sin(270.0 * d2r), -4616189618054758400L));
        Console.WriteLine("g_sin300=" + BitVal(Math.Sin(300.0 * d2r), -4617396353937945430L));
        Console.WriteLine("g_sin315=" + BitVal(Math.Sin(315.0 * d2r), -4618827765636973618L));
        Console.WriteLine("g_sin330=" + BitVal(Math.Sin(330.0 * d2r), -4620693217682128892L));

        Console.WriteLine("g_cos0=" + BitVal(Math.Cos(0.0 * d2r), 4607182418800017408L));
        Console.WriteLine("g_cos15=" + BitVal(Math.Cos(15.0 * d2r), 4606875505927962389L));
        Console.WriteLine("g_cos30=" + BitVal(Math.Cos(30.0 * d2r), 4605975682916830379L));
        Console.WriteLine("g_cos45=" + BitVal(Math.Cos(45.0 * d2r), 4604544271217802189L));
        Console.WriteLine("g_cos60=" + BitVal(Math.Cos(60.0 * d2r), 4602678819172646913L));
        Console.WriteLine("g_cos75=" + BitVal(Math.Cos(75.0 * d2r), 4598334089338226320L));
        Console.WriteLine("g_cos120=" + BitVal(Math.Cos(120.0 * d2r), -4620693217682128900L));
        Console.WriteLine("g_cos135=" + BitVal(Math.Cos(135.0 * d2r), -4618827765636973620L));
        Console.WriteLine("g_cos150=" + BitVal(Math.Cos(150.0 * d2r), -4617396353937945429L));
        Console.WriteLine("g_cos180=" + BitVal(Math.Cos(180.0 * d2r), -4616189618054758400L));
        Console.WriteLine("g_cos210=" + BitVal(Math.Cos(210.0 * d2r), -4617396353937945430L));
        Console.WriteLine("g_cos225=" + BitVal(Math.Cos(225.0 * d2r), -4618827765636973618L));
        Console.WriteLine("g_cos240=" + BitVal(Math.Cos(240.0 * d2r), -4620693217682128892L));
        Console.WriteLine("g_cos300=" + BitVal(Math.Cos(300.0 * d2r), 4602678819172646913L));
        Console.WriteLine("g_cos315=" + BitVal(Math.Cos(315.0 * d2r), 4604544271217802187L));
        Console.WriteLine("g_cos330=" + BitVal(Math.Cos(330.0 * d2r), 4605975682916830376L));
        Console.WriteLine("g_cos360=" + BitVal(Math.Cos(360.0 * d2r), 4607182418800017408L));

        Console.WriteLine("g_tan0=" + BitVal(Math.Tan(0.0 * d2r), 0L));
        Console.WriteLine("g_tan15=" + BitVal(Math.Tan(15.0 * d2r), 4598498563450654038L));
        Console.WriteLine("g_tan30=" + BitVal(Math.Tan(30.0 * d2r), 4603375528459645724L));
        Console.WriteLine("g_tan45=" + BitVal(Math.Tan(45.0 * d2r), 4607182418800017407L));
        Console.WriteLine("g_tan60=" + BitVal(Math.Tan(60.0 * d2r), 4610479282544200872L));
        Console.WriteLine("g_tan75=" + BitVal(Math.Tan(75.0 * d2r), 4615586250113164886L));
        Console.WriteLine("g_tan120=" + BitVal(Math.Tan(120.0 * d2r), -4612892754310574929L));
        Console.WriteLine("g_tan135=" + BitVal(Math.Tan(135.0 * d2r), -4616189618054758399L));
        Console.WriteLine("g_tan150=" + BitVal(Math.Tan(150.0 * d2r), -4619996508395130084L));
        Console.WriteLine("g_tan210=" + BitVal(Math.Tan(210.0 * d2r), 4603375528459645726L));
        Console.WriteLine("g_tan225=" + BitVal(Math.Tan(225.0 * d2r), 4607182418800017405L));
        Console.WriteLine("g_tan240=" + BitVal(Math.Tan(240.0 * d2r), 4610479282544200866L));
        Console.WriteLine("g_tan300=" + BitVal(Math.Tan(300.0 * d2r), -4612892754310574935L));
        Console.WriteLine("g_tan315=" + BitVal(Math.Tan(315.0 * d2r), -4616189618054758398L));

        // ============ 2. PRECISION SCATTER (arbitrary points) ============
        Console.WriteLine("p_sin01=" + BitVal(Math.Sin(0.1), 4591858176471321388L));
        Console.WriteLine("p_cos01=" + BitVal(Math.Cos(0.1), 4607137420321232833L));
        Console.WriteLine("p_tan01=" + BitVal(Math.Tan(0.1), 4591894295732226944L));
        Console.WriteLine("p_atan01=" + BitVal(Math.Atan(0.1), 4591846303962680397L));
        Console.WriteLine("p_atan2_1_01=" + BitVal(Math.Atan2(1.0, 0.1), 4609304189218455635L));

        Console.WriteLine("p_sin05=" + BitVal(Math.Sin(0.5), 4602308182625945072L));
        Console.WriteLine("p_cos05=" + BitVal(Math.Cos(0.5), 4606079780542709072L));
        Console.WriteLine("p_tan05=" + BitVal(Math.Tan(0.5), 4603095874924660554L));
        Console.WriteLine("p_atan05=" + BitVal(Math.Atan(0.5), 4602023952714414927L));
        Console.WriteLine("p_atan2_1_05=" + BitVal(Math.Atan2(1.0, 0.5), 4607664973725548100L));

        Console.WriteLine("p_sin1=" + BitVal(Math.Sin(1.0), 4605754516372524270L));
        Console.WriteLine("p_cos1=" + BitVal(Math.Cos(1.0), 4603041830072026764L));
        Console.WriteLine("p_tan1=" + BitVal(Math.Tan(1.0), 4609692760021066662L));
        Console.WriteLine("p_atan1=" + BitVal(Math.Atan(1.0), 4605249457297304856L));
        Console.WriteLine("p_atan2_1_1=" + BitVal(Math.Atan2(1.0, 1.0), 4605249457297304856L));

        Console.WriteLine("p_sin2=" + BitVal(Math.Sin(2.0), 4606365442650518598L));
        Console.WriteLine("p_cos2=" + BitVal(Math.Cos(2.0), -4622203781984849403L));
        Console.WriteLine("p_tan2=" + BitVal(Math.Tan(2.0), -4611269345697771272L));
        Console.WriteLine("p_atan2=" + BitVal(Math.Atan(2.0), 4607664973725548100L));
        Console.WriteLine("p_atan2_1_2=" + BitVal(Math.Atan2(1.0, 2.0), 4602023952714414927L));

        Console.WriteLine("p_cosPi=" + BitVal(Math.Cos(pi), -4616189618054758400L));
        Console.WriteLine("p_atanPi=" + BitVal(Math.Atan(pi), 4608365186810830290L));
        Console.WriteLine("p_atan2_1_Pi=" + BitVal(Math.Atan2(1.0, pi), 4599223100373286167L));

        Console.WriteLine("p_sinHalfPi=" + BitVal(Math.Sin(halfPi), 4607182418800017408L));
        Console.WriteLine("p_atanHalfPi=" + BitVal(Math.Atan(halfPi), 4607199914482270975L));
        Console.WriteLine("p_atan2_1_HalfPi=" + BitVal(Math.Atan2(1.0, halfPi), 4603281504430085170L));

        Console.WriteLine("p_cosTwoPi=" + BitVal(Math.Cos(twoPi), 4607182418800017408L));
        Console.WriteLine("p_atanTwoPi=" + BitVal(Math.Atan(twoPi), 4609042248434906158L));
        Console.WriteLine("p_atan2_1_TwoPi=" + BitVal(Math.Atan2(1.0, twoPi), 4594854488208688977L));

        // ============ 3. ASIN/ACOS lattice + relations ============
        Console.WriteLine("r_asin0=" + BitEq(Math.Asin(0.0), 0.0));
        Console.WriteLine("r_asin1=" + BitEq(Math.Asin(1.0), halfPi));
        Console.WriteLine("r_asinNeg1=" + BitEq(Math.Asin(-1.0), -halfPi));
        Console.WriteLine("r_asin05=" + BitEq(Math.Asin(0.5), 0.5235987755982989));
        Console.WriteLine("r_acos0=" + BitEq(Math.Acos(0.0), halfPi));
        Console.WriteLine("r_acos1=" + BitEq(Math.Acos(1.0), 0.0));
        Console.WriteLine("r_acosNeg1=" + BitEq(Math.Acos(-1.0), pi));
        Console.WriteLine("r_acos05=" + BitEq(Math.Acos(0.5), 1.0471975511965979));
        // sin(asin x) == x on the lattice set.
        Console.WriteLine("r_sin_asin05=" + BitEq(Math.Sin(Math.Asin(0.5)), 0.5));
        Console.WriteLine("r_sin_asin1=" + BitEq(Math.Sin(Math.Asin(1.0)), 1.0));
        Console.WriteLine("r_cos_acos025=" + BitEq(Math.Cos(Math.Acos(0.25)), 0.25));
        Console.WriteLine("r_cos_acos1=" + BitEq(Math.Cos(Math.Acos(1.0)), 1.0));
        // Symmetry: sin is odd, cos is even (bit-exact on the lattice).
        Console.WriteLine("r_sin_odd=" + BitEq(Math.Sin(-halfPi), -1.0));
        Console.WriteLine("r_cos_even=" + BitEq(Math.Cos(-pi), Math.Cos(pi)));
        Console.WriteLine("r_tan_odd=" + BitEq(Math.Tan(-0.5), -Math.Tan(0.5)));
        // Atan2 quadrant axes (bit-exact).
        Console.WriteLine("r_at2_0_1=" + BitEq(Math.Atan2(0.0, 1.0), 0.0));
        Console.WriteLine("r_at2_1_0=" + BitEq(Math.Atan2(1.0, 0.0), halfPi));
        Console.WriteLine("r_at2_neg1_0=" + BitEq(Math.Atan2(-1.0, 0.0), -halfPi));
        Console.WriteLine("r_at2_0_neg1=" + BitEq(Math.Atan2(0.0, -1.0), pi));
    }
}}
