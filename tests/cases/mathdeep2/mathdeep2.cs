using System;
namespace Cases { public static class MathDeep2 {
    // ---------------------------------------------------------------------
    // mathdeep2.cs -- Math-family DEEP-CHAIN e2e case #2 (batch C42): the
    // trig / log / rounding BOUNDARY surfaces (mathdeep #1 covers the
    // composition chains; this case pins the boundary lattice).
    //
    // Same discipline as mathdeep #1 / mathextra: EVERY line prints
    // "label=True/False", comparing against a fixed constant chosen to be
    // bit-exact on ANY IEEE-754 libm (lattice values, correctly-rounded
    // results, exact powers/roots, exact integer ratios). NO raw double is
    // ever printed, so no NaN/Infinity byte encoding can leak into the
    // oracle (R5: net48 console encodes U+221E as GBK while the ilc runtime
    // writes UTF-8). All Math members used are wired in MethodMap.Math.cs /
    // MethodMap.MathExtra.cs (Clamp).
    //
    // Excluded on purpose (LOUD in MethodMap.Math.cs): Round(double,
    // MidpointRounding) -- only the ToEven default paths exist; Sign(double)
    // -- int/long only; BigMul -- no runtime icall.
    // ---------------------------------------------------------------------
    private static bool IsNaN(double v) { return v != v; }
    private static bool IsFinite(double v) {
        return v == v && v != double.PositiveInfinity && v != double.NegativeInfinity;
    }
    // net48 has no Math.Clamp (added in .NET Core 2.0 / .NET Standard 2.1);
    // emulate its exact semantics so the case still covers the clamp shape.
    private static int Clamp(int value, int min, int max) {
        if (value < min) { return min; }
        if (value > max) { return max; }
        return value;
    }

    public static void Main() {
        // --- Atan2 quadrant lattice (correctly-rounded halves of pi) ---
        // atan2(1,1) = pi/4 double; atan2(-1,-1) = -3pi/4; the +3pi/4 and
        // -pi/4 arms are the bit-exact negations/quadrant mirrors.
        Console.WriteLine("q_at2_11=" + (Math.Atan2(1.0, 1.0) == 0.7853981633974483));
        Console.WriteLine("q_at2_1m1=" + (Math.Atan2(1.0, -1.0) == 2.356194490192345));
        Console.WriteLine("q_at2_m1m1=" + (Math.Atan2(-1.0, -1.0) == -2.356194490192345));
        Console.WriteLine("q_at2_m11=" + (Math.Atan2(-1.0, 1.0) == -0.7853981633974483));
        Console.WriteLine("q_at2_01=" + (Math.Atan2(0.0, 1.0) == 0.0));
        Console.WriteLine("q_at2_0m1=" + (Math.Atan2(0.0, -1.0) == 3.141592653589793));
        Console.WriteLine("q_at2_10=" + (Math.Atan2(1.0, 0.0) == 1.5707963267948966));
        Console.WriteLine("q_at2_m10=" + (Math.Atan2(-1.0, 0.0) == -1.5707963267948966));
        // Ratio-invariant: atan2(k,k) is independent of k (same quadrant).
        Console.WriteLine("q_at2_ratio=" + (Math.Atan2(2.0, 2.0) == Math.Atan2(1.0, 1.0)));
        Console.WriteLine("q_at2_negratio=" + (Math.Atan2(-2.0, -2.0) == Math.Atan2(-1.0, -1.0)));
        // Atan2 of exact lattice inputs feeding back into trig (identity land).
        Console.WriteLine("q_sin_at2=" + (Math.Sin(Math.Atan2(1.0, 0.0)) == 1.0));
        Console.WriteLine("q_cos_at2=" + (Math.Cos(Math.Atan2(0.0, -1.0)) == -1.0));
        Console.WriteLine("q_tan_at2=" + (Math.Tan(Math.Atan2(0.0, 1.0)) == 0.0));

        // --- Asin/Acos boundary lattice (arcs of exact doubles) ---
        Console.WriteLine("a_asin_half=" + (Math.Asin(0.5) == 0.5235987755982989));
        Console.WriteLine("a_acos_half=" + (Math.Acos(0.5) == 1.0471975511965979));
        Console.WriteLine("a_asin_neg1=" + (Math.Asin(-1.0) == -1.5707963267948966));
        Console.WriteLine("a_acos_neg1=" + (Math.Acos(-1.0) == 3.141592653589793));
        Console.WriteLine("a_sin_asin=" + (Math.Sin(Math.Asin(0.25)) == 0.25));
        Console.WriteLine("a_cos_acos=" + (Math.Cos(Math.Acos(0.25)) == 0.25));

        // --- Log family boundaries: exact powers land on exact integers ---
        Console.WriteLine("l_log32_2=" + (Math.Log(32.0, 2.0) == 5.0));
        Console.WriteLine("l_log16_4=" + (Math.Log(16.0, 4.0) == 2.0));
        Console.WriteLine("l_log256_16=" + (Math.Log(256.0, 16.0) == 2.0));
        Console.WriteLine("l_log0625_2=" + (Math.Log(0.0625, 2.0) == -4.0));
        Console.WriteLine("l_log025_05=" + (Math.Log(0.25, 0.5) == 2.0));
        Console.WriteLine("l_log05_05=" + (Math.Log(0.5, 0.5) == 1.0));
        Console.WriteLine("l_log1_base=" + (Math.Log(1.0, 2.0) == 0.0));
        Console.WriteLine("l_log_1over16=" + (Math.Log(1.0 / 16.0, 2.0) == -4.0));
        Console.WriteLine("l_log10_1e6=" + (Math.Log10(1000000.0) == 6.0));
        Console.WriteLine("l_log10_1e9=" + (Math.Log10(1000000000.0) == 9.0));
        Console.WriteLine("l_log10_0001=" + (Math.Log10(0.0001) == -4.0));
        Console.WriteLine("l_log10_pow=" + (Math.Log10(Math.Pow(10.0, 5.0)) == 5.0));
        Console.WriteLine("l_exp_loghalf=" + (Math.Exp(Math.Log(0.5)) == 0.5));
        Console.WriteLine("l_log_exp3=" + (Math.Log(Math.Exp(3.0)) == 3.0));
        Console.WriteLine("l_log_nat2=" + (Math.Log(0.5) == -0.6931471805599453));
        // Log(a, base) sub-1 base: ln a / ln base flips sign.
        Console.WriteLine("l_log8_half=" + (Math.Log(8.0, 0.5) == -3.0));
        Console.WriteLine("l_log2_half=" + (Math.Log(2.0, 0.5) == -1.0));
        // Overflow / underflow boundaries of Exp (e^709 finite, e^710 -> Inf).
        Console.WriteLine("l_exp709=" + (Math.Exp(709.0) != double.PositiveInfinity));
        Console.WriteLine("l_exp710=" + (Math.Exp(710.0) == double.PositiveInfinity));
        Console.WriteLine("l_exp_neg709=" + (Math.Exp(-709.0) != 0.0));
        Console.WriteLine("l_exp_neg1000=" + (Math.Exp(-1000.0) == 0.0));
        Console.WriteLine("l_log_max=" + IsFinite(Math.Log(1.0e300)));
        Console.WriteLine("l_log10_max=" + (Math.Log10(1.0e300) == 300.0));

        // --- Rounding boundaries: banker's (ToEven) lattice table ---
        Console.WriteLine("r_round05=" + (Math.Round(0.5) == 0.0));
        Console.WriteLine("r_round15=" + (Math.Round(1.5) == 2.0));
        Console.WriteLine("r_round25=" + (Math.Round(2.5) == 2.0));
        Console.WriteLine("r_round35=" + (Math.Round(3.5) == 4.0));
        Console.WriteLine("r_round45=" + (Math.Round(4.5) == 4.0));
        Console.WriteLine("r_round55=" + (Math.Round(5.5) == 6.0));
        Console.WriteLine("r_round65=" + (Math.Round(6.5) == 6.0));
        Console.WriteLine("r_round75=" + (Math.Round(7.5) == 8.0));
        Console.WriteLine("r_roundm05=" + (Math.Round(-0.5) == 0.0));
        Console.WriteLine("r_roundm15=" + (Math.Round(-1.5) == -2.0));
        Console.WriteLine("r_roundm25=" + (Math.Round(-2.5) == -2.0));
        Console.WriteLine("r_roundm35=" + (Math.Round(-3.5) == -4.0));
        Console.WriteLine("r_roundm45=" + (Math.Round(-4.5) == -4.0));
        Console.WriteLine("r_roundm55=" + (Math.Round(-5.5) == -6.0));
        // Round(double, digits) -- ToEven at the digit; exact decimal lattice.
        Console.WriteLine("r_round125_1=" + (Math.Round(1.25, 1) == 1.2));
        Console.WriteLine("r_round15_0=" + (Math.Round(1.5, 0) == 2.0));
        Console.WriteLine("r_round25_0=" + (Math.Round(2.5, 0) == 2.0));
        Console.WriteLine("r_roundm15_0=" + (Math.Round(-1.5, 0) == -2.0));
        Console.WriteLine("r_round2675_2=" + (Math.Round(2.675, 2) == 2.68));
        Console.WriteLine("r_round314_2=" + (Math.Round(3.14159, 2) == 3.14));
        Console.WriteLine("r_round314_3=" + (Math.Round(3.14159, 3) == 3.142));
        Console.WriteLine("r_roundChain=" + (Math.Floor(Math.Round(3.14159, 3)) == 3.0));
        Console.WriteLine("r_roundCeil=" + (Math.Ceiling(Math.Round(0.5)) == 0.0));
        Console.WriteLine("r_roundTrunc=" + (Math.Truncate(Math.Round(-1.5)) == -2.0));

        // --- Floor / Ceiling / Truncate boundary lattice ---
        Console.WriteLine("f_floor2=" + (Math.Floor(2.0) == 2.0));
        Console.WriteLine("f_floor25=" + (Math.Floor(2.5) == 2.0));
        Console.WriteLine("f_floor_25=" + (Math.Floor(-2.5) == -3.0));
        Console.WriteLine("f_floor05=" + (Math.Floor(0.5) == 0.0));
        Console.WriteLine("f_floor_05=" + (Math.Floor(-0.5) == -1.0));
        Console.WriteLine("f_ceil2=" + (Math.Ceiling(2.0) == 2.0));
        Console.WriteLine("f_ceil25=" + (Math.Ceiling(2.5) == 3.0));
        Console.WriteLine("f_ceil_25=" + (Math.Ceiling(-2.5) == -2.0));
        Console.WriteLine("f_ceil05=" + (Math.Ceiling(0.5) == 1.0));
        Console.WriteLine("f_ceil_05=" + (Math.Ceiling(-0.5) == 0.0));
        Console.WriteLine("f_trunc25=" + (Math.Truncate(2.5) == 2.0));
        Console.WriteLine("f_trunc_25=" + (Math.Truncate(-2.5) == -2.0));
        Console.WriteLine("f_trunc_05=" + (Math.Truncate(-0.5) == 0.0));
        Console.WriteLine("f_floorbig=" + (Math.Floor(123456.789) == 123456.0));
        Console.WriteLine("f_ceilbig=" + (Math.Ceiling(123456.789) == 123457.0));
        Console.WriteLine("f_truncbig=" + (Math.Truncate(-123456.789) == -123456.0));
        // Floor/Ceiling at 2^53 (exactly representable; no fraction bits).
        Console.WriteLine("f_floor2p53=" + (Math.Floor(9007199254740992.0) == 9007199254740992.0));
        Console.WriteLine("f_ceil2p53=" + (Math.Ceiling(9007199254740992.0) == 9007199254740992.0));

        // --- Pow / Sqrt exact-power boundaries ---
        Console.WriteLine("p_pow32=" + (Math.Pow(2.0, 5.0) == 32.0));
        Console.WriteLine("p_pow81=" + (Math.Pow(3.0, 4.0) == 81.0));
        Console.WriteLine("p_pow16half=" + (Math.Pow(16.0, 0.5) == 4.0));
        Console.WriteLine("p_pow2neg5=" + (Math.Pow(2.0, -5.0) == 0.03125));
        Console.WriteLine("p_pow10_6=" + (Math.Pow(10.0, 6.0) == 1000000.0));
        Console.WriteLine("p_pow0neg=" + (Math.Pow(0.0, -1.0) == double.PositiveInfinity));
        Console.WriteLine("p_pownegodd=" + (Math.Pow(-3.0, 3.0) == -27.0));
        Console.WriteLine("p_pownegeven=" + (Math.Pow(-3.0, 2.0) == 9.0));
        Console.WriteLine("p_pow1any=" + (Math.Pow(1.0, 1e10) == 1.0));
        Console.WriteLine("p_powany0=" + (Math.Pow(7.0, 0.0) == 1.0));
        Console.WriteLine("p_sqrt25=" + (Math.Sqrt(25.0) == 5.0));
        Console.WriteLine("p_sqrt169=" + (Math.Sqrt(169.0) == 13.0));
        Console.WriteLine("p_sqrt_pow=" + (Math.Sqrt(Math.Pow(2.0, 10.0)) == 32.0));
        Console.WriteLine("p_pow_sqrt=" + (Math.Pow(Math.Sqrt(1024.0), 2.0) == 1024.0));

        // --- Clamp boundary lattice (emulated; same shape as the wired
        //     icall_math_clamp_i) ---
        Console.WriteLine("c_clamp_mid=" + (Clamp(5, 1, 10) == 5));
        Console.WriteLine("c_clamp_lo=" + (Clamp(0, 1, 10) == 1));
        Console.WriteLine("c_clamp_hi=" + (Clamp(11, 1, 10) == 10));
        Console.WriteLine("c_clamp_neg=" + (Clamp(-5, -10, -1) == -5));
        Console.WriteLine("c_clamp_minmax=" + (Clamp(5, 5, 5) == 5));

        // --- NaN / Infinity boundary consistency (bool helpers only) ---
        Console.WriteLine("b_atan2_nan=" + IsNaN(Math.Atan2(double.NaN, 1.0)));
        Console.WriteLine("b_atan2_inf=" + (Math.Atan2(double.PositiveInfinity, 1.0) == 1.5707963267948966));
        Console.WriteLine("b_asin_inf=" + IsNaN(Math.Asin(double.PositiveInfinity)));
        Console.WriteLine("b_acos_neg2=" + IsNaN(Math.Acos(-2.0)));
        Console.WriteLine("b_loghalf=" + (Math.Log(0.5) == -0.6931471805599453));
        Console.WriteLine("b_log_neg=" + IsNaN(Math.Log(-2.0)));
        Console.WriteLine("b_log10_neg=" + IsNaN(Math.Log10(-0.5)));
        Console.WriteLine("b_round_inf=" + (Math.Round(double.PositiveInfinity) == double.PositiveInfinity));
        Console.WriteLine("b_round_nan=" + IsNaN(Math.Round(double.NaN)));
        Console.WriteLine("b_floor_nan=" + IsNaN(Math.Floor(double.NaN)));
        Console.WriteLine("b_ceil_neg0=" + (Math.Ceiling(-0.0) == 0.0));
        Console.WriteLine("b_floor_neg0=" + (Math.Floor(-0.0) == 0.0));
        Console.WriteLine("b_trunc_neg0=" + (Math.Truncate(-0.0) == 0.0));
        Console.WriteLine("b_abs_neg=" + (Math.Abs(-123.456) == 123.456));
        Console.WriteLine("b_abs_maxl=" + (Math.Abs(9223372036854775806L) == 9223372036854775806L));
        Console.WriteLine("b_max_minbound=" + (Math.Max(int.MinValue, int.MaxValue) == int.MaxValue));
        Console.WriteLine("b_min_maxbound=" + (Math.Min(int.MinValue, int.MaxValue) == int.MinValue));
        Console.WriteLine("b_sign_maxl=" + (Math.Sign(9223372036854775807L) == 1));
        Console.WriteLine("b_sign_minl=" + (Math.Sign(-9223372036854775808L) == -1));
    }
}}
