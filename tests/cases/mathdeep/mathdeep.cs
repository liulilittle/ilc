using System;
namespace Cases { public static class MathDeep {
    // ---------------------------------------------------------------------
    // mathdeep.cs -- Math-family DEEP-CHAIN e2e case (math2/mathext cover the
    // single calls; this case composes them and pins boundary behaviour).
    //
    // Every line prints "label=True/False": the value under test is compared
    // against a fixed constant or asserted via IsNaN/IsInfinity-style bool
    // helpers. NO raw double is ever printed, so no ∞/NaN byte encoding can
    // leak into the oracle (R5 lesson: net48 console encodes U+221E as GBK
    // while the ilc runtime writes UTF-8 -- a byte-exact diff can never
    // match). The bool helpers below compile to plain IL (x != x, == +Inf)
    // and are therefore bit-deterministic on BOTH the net48 reference run
    // and the transpiled MinGW build.
    //
    // Chain identities are chosen to land on lattice values (0, 1, pi, e,
    // exact integers) so every libm step is correctly rounded identically
    // on MSVCRT and MinGW -- the same discipline mathextra proved green.
    // ---------------------------------------------------------------------
    private static bool IsNaN(double v) { return v != v; }
    private static bool IsFinite(double v) {
        return v == v && v != double.PositiveInfinity && v != double.NegativeInfinity;
    }
    // net48 has no Math.Clamp (added in .NET Core 2.0 / .NET Standard 2.1);
    // emulate its exact semantics so the case still covers the clamp shape.
    private static int Clamp(int value, int min, int max) {
        if (min > max) { throw new ArgumentException("min cannot be greater than max"); }
        if (value < min) { return min; }
        if (value > max) { return max; }
        return value;
    }
    public static void Main() {
        // --- Trig chains (composition lands on exact lattice values) ---
        Console.WriteLine("t_sinAcos1=" + (Math.Sin(Math.Acos(1.0)) == 0.0));
        Console.WriteLine("t_cosAsin0=" + (Math.Cos(Math.Asin(0.0)) == 1.0));
        Console.WriteLine("t_tanAtan0=" + (Math.Tan(Math.Atan(0.0)) == 0.0));
        Console.WriteLine("t_atanTan0=" + (Math.Atan(Math.Tan(0.0)) == 0.0));
        Console.WriteLine("t_acosCos0=" + (Math.Acos(Math.Cos(0.0)) == 0.0));
        Console.WriteLine("t_asinSin0=" + (Math.Asin(Math.Sin(0.0)) == 0.0));
        // 2*acos(0) == 2*(pi/2 double) == pi double bit-exactly; cos(pi) == -1.
        Console.WriteLine("t_cos2acos0=" + (Math.Cos(2.0 * Math.Acos(0.0)) == -1.0));
        // Both sides are sin(pi double) computed by the SAME libm -> always equal.
        Console.WriteLine("t_sin2asin1=" + (Math.Sin(2.0 * Math.Asin(1.0)) == Math.Sin(Math.Acos(-1.0))));
        Console.WriteLine("t_atan2Chain=" + (Math.Atan2(Math.Sin(0.0), Math.Cos(0.0)) == 0.0));
        // --- Log / exp chains ---
        Console.WriteLine("l_expLog1=" + (Math.Exp(Math.Log(1.0)) == 1.0));
        Console.WriteLine("l_logExp0=" + (Math.Log(Math.Exp(0.0)) == 0.0));
        Console.WriteLine("l_logExpE=" + (Math.Log(Math.Exp(1.0)) == 1.0));
        Console.WriteLine("l_expLogE=" + (Math.Exp(Math.Log(Math.E)) == Math.E));
        Console.WriteLine("l_expLog2=" + (Math.Exp(Math.Log(2.0)) == 2.0));
        Console.WriteLine("l_logBaseE=" + (Math.Log(Math.E, Math.E) == 1.0));
        Console.WriteLine("l_log16_2=" + (Math.Log(16.0, 2.0) == 4.0));
        Console.WriteLine("l_logBaseChain=" + (Math.Log(Math.Pow(8.0, 2.0), 2.0) == 6.0));
        Console.WriteLine("l_expLogQ=" + (Math.Exp(Math.Log(0.25)) == 0.25));
        Console.WriteLine("l_log10Chain=" + (Math.Log10(Math.Pow(10.0, 3.0)) == 3.0));
        Console.WriteLine("l_powLog10=" + (Math.Pow(10.0, Math.Log10(100.0)) == 100.0));
        // --- Power / root chains ---
        Console.WriteLine("p_powSqrt=" + (Math.Pow(Math.Sqrt(9.0), 2.0) == 9.0));
        Console.WriteLine("p_sqrtPow=" + (Math.Sqrt(Math.Pow(9.0, 2.0)) == 9.0));
        Console.WriteLine("p_powSqrtChain=" + (Math.Pow(Math.Sqrt(16.0), 0.5) == 2.0));
        Console.WriteLine("p_sqrtPow10=" + (Math.Sqrt(Math.Pow(10.0, 2.0)) == 10.0));
        Console.WriteLine("p_powHalf=" + (Math.Pow(4.0, 0.5) == 2.0));
        Console.WriteLine("p_powNegExp=" + (Math.Pow(2.0, -1.0) == 0.5));
        Console.WriteLine("p_pow0_0=" + (Math.Pow(0.0, 0.0) == 1.0));
        Console.WriteLine("p_powNegBase=" + (Math.Pow(-2.0, 3.0) == -8.0));
        Console.WriteLine("p_powNegEven=" + (Math.Pow(-2.0, 2.0) == 4.0));
        Console.WriteLine("p_sqrtFrac=" + (Math.Sqrt(0.25) == 0.5));
        Console.WriteLine("p_sqrt1=" + (Math.Sqrt(1.0) == 1.0));
        Console.WriteLine("p_sqrtBig=" + (Math.Sqrt(1.0e300) == 1.0e150));
        // --- Hyperbolic chains (icalls wired; lattice and overflow pins) ---
        Console.WriteLine("h_sinh0=" + (Math.Sinh(0.0) == 0.0));
        Console.WriteLine("h_cosh0=" + (Math.Cosh(0.0) == 1.0));
        Console.WriteLine("h_tanh0=" + (Math.Tanh(0.0) == 0.0));
        Console.WriteLine("h_coshChain=" + (Math.Cosh(Math.Exp(0.0)) == Math.Cosh(1.0)));
        Console.WriteLine("h_sinhChain=" + (Math.Sinh(Math.Log(1.0)) == 0.0));
        Console.WriteLine("h_tanhChain=" + (Math.Tanh(Math.Log(1.0)) == 0.0));
        Console.WriteLine("h_logCosh0=" + (Math.Log(Math.Cosh(0.0)) == 0.0));
        Console.WriteLine("h_expTanh=" + (Math.Exp(Math.Tanh(0.0)) == 1.0));
        Console.WriteLine("h_cosh2Sinh2=" + (Math.Cosh(1.0) * Math.Cosh(1.0) - Math.Sinh(1.0) * Math.Sinh(1.0) == 1.0));
        Console.WriteLine("h_tanhBig=" + (Math.Tanh(1000.0) == 1.0));
        Console.WriteLine("h_tanhNegBig=" + (Math.Tanh(-1000.0) == -1.0));
        Console.WriteLine("h_sinhBig=" + (Math.Sinh(1000.0) == double.PositiveInfinity));
        Console.WriteLine("h_sinhNegBig=" + (Math.Sinh(-1000.0) == double.NegativeInfinity));
        Console.WriteLine("h_coshBig=" + (Math.Cosh(1000.0) == double.PositiveInfinity));
        // --- Rounding family chains (Floor/Ceiling/Round/Truncate wired) ---
        Console.WriteLine("r_floorRound=" + (Math.Floor(Math.Round(2.5)) == 2.0));
        Console.WriteLine("r_ceilRound=" + (Math.Ceiling(Math.Round(3.5)) == 4.0));
        Console.WriteLine("r_roundFloor=" + (Math.Round(Math.Floor(2.7)) == 2.0));
        Console.WriteLine("r_truncCeil=" + (Math.Truncate(Math.Ceiling(-2.3)) == -2.0));
        Console.WriteLine("r_floorTrunc=" + (Math.Floor(Math.Truncate(-2.7)) == -2.0));
        Console.WriteLine("r_ceilFloor=" + (Math.Ceiling(Math.Floor(-3.7)) == -4.0));
        Console.WriteLine("r_roundDigits=" + (Math.Round(Math.Truncate(3.14159), 2) == 3.0));
        Console.WriteLine("r_truncRound=" + (Math.Truncate(Math.Round(-1.5)) == -2.0));
        Console.WriteLine("r_round675=" + (Math.Round(2.675, 2) == 2.68));
        Console.WriteLine("r_roundChain=" + (Math.Floor(Math.Round(2.675, 2)) == 2.0));
        Console.WriteLine("r_truncHalf=" + (Math.Truncate(-0.5) == 0.0));
        // --- Max / Min / Clamp (int exact; net48 double comparison quirks) ---
        Console.WriteLine("m_clampMid=" + (Clamp(5, 1, 10) == 5));
        Console.WriteLine("m_clampLow=" + (Clamp(0, 1, 10) == 1));
        Console.WriteLine("m_clampHigh=" + (Clamp(12, 1, 10) == 10));
        Console.WriteLine("m_clampNeg=" + (Clamp(-5, -10, -1) == -5));
        bool clampThrows = false;
        try { Clamp(5, 10, 1); } catch (ArgumentException) { clampThrows = true; }
        Console.WriteLine("m_clampThrows=" + clampThrows);
        Console.WriteLine("m_clampMinMax=" + (Clamp(7, 1, 10) == Math.Max(1, Math.Min(10, 7))));
        Console.WriteLine("m_maxClamp=" + (Math.Max(Clamp(7, 1, 10), 3) == 7));
        Console.WriteLine("m_minClamp=" + (Math.Min(Clamp(4, 1, 5), 3) == 3));
        Console.WriteLine("m_maxMin=" + (Math.Max(Math.Min(5, 9), 3) == 5));
        Console.WriteLine("m_minMax=" + (Math.Min(Math.Max(5, 9), 7) == 7));
        Console.WriteLine("m_maxI=" + (Math.Max(3, 7) == 7));
        Console.WriteLine("m_minI=" + (Math.Min(-3, -7) == -7));
        Console.WriteLine("m_maxL=" + (Math.Max(3L, 7L) == 7L));
        Console.WriteLine("m_minL=" + (Math.Min(3L, 7L) == 3L));
        Console.WriteLine("m_maxD=" + (Math.Max(1.5, 2.5) == 2.5));
        Console.WriteLine("m_minD=" + (Math.Min(-1.5, -2.5) == -2.5));
        Console.WriteLine("m_maxBoundary=" + (Math.Max(int.MaxValue, int.MinValue) == int.MaxValue));
        Console.WriteLine("m_minZero=" + (Math.Min(0.0, -0.0) == 0.0));
        // --- Boundaries: 0 / negative / large / NaN / Infinity (bool only) ---
        Console.WriteLine("b_sin0=" + (Math.Sin(0.0) == 0.0));
        Console.WriteLine("b_cos0=" + (Math.Cos(0.0) == 1.0));
        Console.WriteLine("b_asin0=" + (Math.Asin(0.0) == 0.0));
        Console.WriteLine("b_atan0=" + (Math.Atan(0.0) == 0.0));
        Console.WriteLine("b_sinInf=" + IsNaN(Math.Sin(double.PositiveInfinity)));
        Console.WriteLine("b_cosNegInf=" + IsNaN(Math.Cos(double.NegativeInfinity)));
        Console.WriteLine("b_tanInf=" + IsNaN(Math.Tan(double.PositiveInfinity)));
        Console.WriteLine("b_asin2=" + IsNaN(Math.Asin(2.0)));
        Console.WriteLine("b_acos2=" + IsNaN(Math.Acos(2.0)));
        Console.WriteLine("b_atanInf=" + (Math.Atan(double.PositiveInfinity) == 1.5707963267948966));
        Console.WriteLine("b_atanNegInf=" + (Math.Atan(double.NegativeInfinity) == -1.5707963267948966));
        Console.WriteLine("b_log0=" + (Math.Log(0.0) == double.NegativeInfinity));
        Console.WriteLine("b_logNeg=" + IsNaN(Math.Log(-1.0)));
        Console.WriteLine("b_logInf=" + (Math.Log(double.PositiveInfinity) == double.PositiveInfinity));
        Console.WriteLine("b_log10_0=" + (Math.Log10(0.0) == double.NegativeInfinity));
        Console.WriteLine("b_log10Neg=" + IsNaN(Math.Log10(-1.0)));
        Console.WriteLine("b_logBase0=" + (Math.Log(0.0, 2.0) == double.NegativeInfinity));
        Console.WriteLine("b_expOver=" + (Math.Exp(1000.0) == double.PositiveInfinity));
        Console.WriteLine("b_expUnder=" + (Math.Exp(-1000.0) == 0.0));
        Console.WriteLine("b_expNegInf=" + (Math.Exp(double.NegativeInfinity) == 0.0));
        Console.WriteLine("b_expNaN=" + IsNaN(Math.Exp(double.NaN)));
        Console.WriteLine("b_logNaN=" + IsNaN(Math.Log(double.NaN)));
        Console.WriteLine("b_sinNaN=" + IsNaN(Math.Sin(double.NaN)));
        Console.WriteLine("b_tanhNaN=" + IsNaN(Math.Tanh(double.NaN)));
        Console.WriteLine("b_pow0Neg=" + (Math.Pow(0.0, -1.0) == double.PositiveInfinity));
        Console.WriteLine("b_powInf0=" + (Math.Pow(double.PositiveInfinity, 0.0) == 1.0));
        Console.WriteLine("b_powOver=" + (Math.Pow(10.0, 309.0) == double.PositiveInfinity));
        Console.WriteLine("b_powFinite=" + IsFinite(Math.Pow(10.0, 307.0)));
        Console.WriteLine("b_sqrt0=" + (Math.Sqrt(0.0) == 0.0));
        Console.WriteLine("b_sqrtNeg=" + IsNaN(Math.Sqrt(-1.0)));
        Console.WriteLine("b_sqrtNaN=" + IsNaN(Math.Sqrt(double.NaN)));
        Console.WriteLine("b_floorInf=" + (Math.Floor(double.PositiveInfinity) == double.PositiveInfinity));
        Console.WriteLine("b_floorNaN=" + IsNaN(Math.Floor(double.NaN)));
        Console.WriteLine("b_ceilNegInf=" + (Math.Ceiling(double.NegativeInfinity) == double.NegativeInfinity));
        Console.WriteLine("b_ceilNaN=" + IsNaN(Math.Ceiling(double.NaN)));
        Console.WriteLine("b_roundInf=" + (Math.Round(double.PositiveInfinity) == double.PositiveInfinity));
        Console.WriteLine("b_roundNaN=" + IsNaN(Math.Round(double.NaN)));
        Console.WriteLine("b_truncInf=" + (Math.Truncate(double.PositiveInfinity) == double.PositiveInfinity));
        Console.WriteLine("b_truncNaN=" + IsNaN(Math.Truncate(double.NaN)));
        Console.WriteLine("b_ieee5_2=" + (Math.IEEERemainder(5.0, 2.0) == 1.0));
        Console.WriteLine("b_ieee3_2=" + (Math.IEEERemainder(3.0, 2.0) == -1.0));
        Console.WriteLine("b_ieee0=" + IsNaN(Math.IEEERemainder(3.0, 0.0)));
        Console.WriteLine("b_ieeeInf=" + IsNaN(Math.IEEERemainder(double.PositiveInfinity, 3.0)));
        Console.WriteLine("b_ieeeChain=" + (Math.IEEERemainder(Math.Truncate(5.7), 2.0) == 1.0));
        Console.WriteLine("b_absNeg0=" + (Math.Abs(-0.0) == 0.0));
        bool absMinIntThrows = false;
        try { Math.Abs(int.MinValue); } catch (OverflowException) { absMinIntThrows = true; }
        Console.WriteLine("b_absMinInt=" + absMinIntThrows);
        bool absMinLongThrows = false;
        try { Math.Abs(long.MinValue); } catch (OverflowException) { absMinLongThrows = true; }
        Console.WriteLine("b_absMinLong=" + absMinLongThrows);
        // Bit-level probes (canonical IEEE-754: fabs(-0.0) == +0.0 bits,
        // trunc(-0.5) preserves the sign bit).
        Console.WriteLine("b_bitsAbsNeg0=" + (BitConverter.DoubleToInt64Bits(Math.Abs(-0.0)) == 0L));
        Console.WriteLine("b_bitsTrunc=" + (BitConverter.DoubleToInt64Bits(Math.Truncate(-0.5)) == long.MinValue));
    }
}}
