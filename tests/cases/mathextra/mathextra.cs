using System;
namespace Cases { public static class MathExtraCase {
    // ---------------------------------------------------------------------
    // mathextra.cs -- full Math-family call-chain e2e case.
    //
    // Covers every Math member the M5 resolver wires (see MathDispatch in
    // MethodRefResolver.cs): Sin/Cos/Tan/Asin/Acos/Atan/Atan2/Exp/Log/Log10/
    // Pow/Sqrt/Abs/Min/Max/Floor/Ceiling/Round/Truncate/Sign.
    //
    // Every line prints "label=True/False": the value under test is compared
    // against a fixed constant chosen to be bit-exact on ANY IEEE-754 libm
    // (lattice values, correctly-rounded results, exact integer/division
    // results), so the reference run (net48) and the transpiled AOT build
    // (MinGW libm) must agree byte-for-byte. Only overloads the M5 resolver
    // actually wires are exercised: Sign takes int/long only, Abs/Min/Max
    // take int/long/double (uint/float overloads are NOT wired and would
    // abort transpilation).
    // ---------------------------------------------------------------------
    public static void Main() {
        // --- Sin / Cos / Tan (lattice: 0, pi/2, pi are bit-exact) ---
        Console.WriteLine("sin0=" + (Math.Sin(0.0) == 0.0));
        Console.WriteLine("sinHalfPi=" + (Math.Sin(1.5707963267948966) == 1.0));
        Console.WriteLine("cos0=" + (Math.Cos(0.0) == 1.0));
        Console.WriteLine("cosPi=" + (Math.Cos(3.141592653589793) == -1.0));
        Console.WriteLine("tan0=" + (Math.Tan(0.0) == 0.0));
        // --- Asin / Acos / Atan / Atan2 (correctly rounded lattice) ---
        Console.WriteLine("asin0=" + (Math.Asin(0.0) == 0.0));
        Console.WriteLine("asin1=" + (Math.Asin(1.0) == 1.5707963267948966));
        Console.WriteLine("acos1=" + (Math.Acos(1.0) == 0.0));
        Console.WriteLine("acosNeg1=" + (Math.Acos(-1.0) == 3.141592653589793));
        Console.WriteLine("atan0=" + (Math.Atan(0.0) == 0.0));
        Console.WriteLine("atan1=" + (Math.Atan(1.0) == 0.7853981633974483));
        Console.WriteLine("atan2_0_1=" + (Math.Atan2(0.0, 1.0) == 0.0));
        Console.WriteLine("atan2_1_0=" + (Math.Atan2(1.0, 0.0) == 1.5707963267948966));
        Console.WriteLine("atan2_1_1=" + (Math.Atan2(1.0, 1.0) == 0.7853981633974483));
        Console.WriteLine("atan2_m1_m1=" + (Math.Atan2(-1.0, -1.0) == -2.356194490192345));
        // --- Exp / Log / Log10 (correctly rounded; log(e-double) == 1.0) ---
        Console.WriteLine("exp0=" + (Math.Exp(0.0) == 1.0));
        Console.WriteLine("exp1=" + (Math.Exp(1.0) == 2.718281828459045));
        Console.WriteLine("expNeg1=" + (Math.Exp(-1.0) == 0.36787944117144233));
        Console.WriteLine("log1=" + (Math.Log(1.0) == 0.0));
        Console.WriteLine("logE=" + (Math.Log(2.718281828459045) == 1.0));
        Console.WriteLine("log2=" + (Math.Log(2.0) == 0.6931471805599453));
        Console.WriteLine("log10_100=" + (Math.Log10(100.0) == 2.0));
        Console.WriteLine("log10_1000=" + (Math.Log10(1000.0) == 3.0));
        Console.WriteLine("log10_01=" + (Math.Log10(0.1) == -1.0));
        // --- Pow / Sqrt (IEEE correctly rounded; exact powers) ---
        Console.WriteLine("pow2_10=" + (Math.Pow(2.0, 10.0) == 1024.0));
        Console.WriteLine("pow0_0=" + (Math.Pow(0.0, 0.0) == 1.0));
        Console.WriteLine("pow2_half=" + (Math.Pow(2.0, 0.5) == 1.4142135623730951));
        Console.WriteLine("pow05_2=" + (Math.Pow(0.5, 2.0) == 0.25));
        Console.WriteLine("sqrt4=" + (Math.Sqrt(4.0) == 2.0));
        Console.WriteLine("sqrt2=" + (Math.Sqrt(2.0) == 1.4142135623730951));
        // --- Abs / Min / Max (int / long / double; exact) ---
        Console.WriteLine("absI=" + (Math.Abs(-5) == 5));
        Console.WriteLine("absL=" + (Math.Abs(-5L) == 5L));
        Console.WriteLine("absD=" + (Math.Abs(-2.5) == 2.5));
        Console.WriteLine("maxI=" + (Math.Max(3, 7) == 7));
        Console.WriteLine("minI=" + (Math.Min(3, 7) == 3));
        Console.WriteLine("maxL=" + (Math.Max(3L, 7L) == 7L));
        Console.WriteLine("minL=" + (Math.Min(3L, 7L) == 3L));
        Console.WriteLine("maxD=" + (Math.Max(3.5, 2.5) == 3.5));
        Console.WriteLine("minD=" + (Math.Min(3.5, 2.5) == 2.5));
        // --- Sign (int / long only -- the double overload is not wired) ---
        Console.WriteLine("signN=" + (Math.Sign(-9) == -1));
        Console.WriteLine("sign0=" + (Math.Sign(0) == 0));
        Console.WriteLine("signP=" + (Math.Sign(9) == 1));
        Console.WriteLine("signNL=" + (Math.Sign(-9L) == -1));
        Console.WriteLine("signPL=" + (Math.Sign(9L) == 1));
        // --- Floor / Ceiling / Round / Truncate (exact; Round = banker's) ---
        Console.WriteLine("floorP=" + (Math.Floor(2.7) == 2.0));
        Console.WriteLine("floorN=" + (Math.Floor(-2.7) == -3.0));
        Console.WriteLine("ceilP=" + (Math.Ceiling(2.3) == 3.0));
        Console.WriteLine("ceilN=" + (Math.Ceiling(-2.3) == -2.0));
        Console.WriteLine("round25=" + (Math.Round(2.5) == 2.0));
        Console.WriteLine("round35=" + (Math.Round(3.5) == 4.0));
        Console.WriteLine("roundN25=" + (Math.Round(-2.5) == -2.0));
        Console.WriteLine("round24=" + (Math.Round(2.4) == 2.0));
        Console.WriteLine("truncP=" + (Math.Truncate(2.9) == 2.0));
        Console.WriteLine("truncN=" + (Math.Truncate(-2.9) == -2.0));
    }
}}
