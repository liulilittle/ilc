//------------------------------------------------------------------------------
// doubleops.cs -- double OPERATOR deep probe (e2e batch C54): IEEE-754
// precision, division, rounding, and the numeric boundaries -- byte-exact vs
// the net48 oracle, dual-compiler (mingw + msvc).
//
// Scope: the double OPERATORS and casts/rounding -- NOT the Math family
// transcendental surface (mathdeep/mathdeep2/mathext own trig/log/exp).
// This case exercises the plain IL arithmetic (add/sub/mul/div/rem on R8),
// explicit casts, Math.Round/Floor/Ceiling/Truncate, and the boundary
// lattice. MethodMap.Math.cs: Round(double) and Round(double, int) are the
// only wired Round shapes (ToEven); the MidpointRounding overload LOUDs.
//
// OUTPUT DISCIPLINE (mathdeep2/mathext precedent): NO raw double is ever
// printed -- every probe is a labeled bool comparing against a bit-exact
// constant (or another double), so no NaN/Infinity byte encoding can leak
// into the oracle (net48 console encodes U+221E as GBK while the ilc
// runtime writes UTF-8). NaN prints are avoided; -0.0 sign is probed via
// BitConverter.DoubleToInt64Bits (wired, mathext precedent). All values
// below are correctly-rounded IEEE-754 results, deterministic on any
// conforming implementation.
//
// RUNTIME-OPERAND rule: division-by-zero / overflow / underflow / modulo
// probes use NAMED LOCALS (a constant expression would fold at compile time
// and never exercise the runtime op).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class DoubleOps {
    public static void Main() {
        double d1 = 0.1;
        double d2 = 0.2;
        double d3 = 0.7;

        // ============ S1: IEEE addition/multiplication precision ============
        Console.WriteLine("p_add12=" + (d1 + d2 == 0.30000000000000004));   // True (0.1+0.2)
        Console.WriteLine("p_add12eq03=" + (d1 + d2 == 0.3));               // False
        Console.WriteLine("p_add17=" + (d1 + d3 == 0.7999999999999999));    // True (0.1+0.7)
        Console.WriteLine("p_addsame=" + (d1 + d1 == d2));                  // True (0.1+0.1 == 0.2)
        Console.WriteLine("p_mul=" + (d1 * d2 == 0.020000000000000004));    // True (0.1*0.2)
        Console.WriteLine("p_mul10=" + (d1 * 10.0 == 1.0));                 // True (0.1*10 rounds to 1)
        Console.WriteLine("p_mul3=" + (d1 * 3.0 == 0.30000000000000004));   // True (0.1*3)

        // large-ulp addition: 1e20+1 and 1e16+1 collapse into the ulp
        double e1 = 1e20;
        Console.WriteLine("p_bigadd=" + (e1 + 1.0 == e1));                  // True (ulp >> 1)
        double g1 = 1e16;
        Console.WriteLine("p_ulp=" + (g1 + 1.0 == g1));                     // True (ulp at 1e16 is 2)
        Console.WriteLine("p_ulp2=" + ((g1 + 2.0) - g1 == 2.0));            // True (1e16+2 exact)
        Console.WriteLine("p_cancel=" + ((g1 + 1.0) - g1 == 0.0));          // True (the +1 was lost)

        // non-associativity of IEEE addition
        double aa = 0.1, bb = 0.2, cc = 0.3;
        Console.WriteLine("p_assocA=" + ((aa + bb) + cc == 0.6000000000000001)); // True
        Console.WriteLine("p_assocB=" + (aa + (bb + cc) == 0.6));                // True
        Console.WriteLine("p_nassoc=" + ((aa + bb) + cc == aa + (bb + cc)));     // False

        // ============ S2: division ============
        double one = 1.0;
        double three = 3.0;
        double two = 2.0;
        double zero = 0.0;
        Console.WriteLine("d_third=" + (one / three == 0.3333333333333333));  // True
        Console.WriteLine("d_rt=" + ((one / three) * three == 1.0));          // True (round trip)
        Console.WriteLine("d_twoThird=" + (two / three == 0.6666666666666666));// True
        Console.WriteLine("d_half=" + (one / two == 0.5));                    // True
        Console.WriteLine("d_25=" + (7.0 / 2.0 == 3.5));                      // True
        Console.WriteLine("d_div0=" + (one / zero == double.PositiveInfinity));       // True (+Inf)
        double negzero = -0.0;
        Console.WriteLine("d_divNeg0=" + (one / negzero == double.NegativeInfinity)); // True (-Inf)
        Console.WriteLine("d_div0Neg=" + ((-one) / zero == double.NegativeInfinity)); // True (-Inf)
        Console.WriteLine("d_0div0=" + (zero / zero != zero));                // True (NaN)
        Console.WriteLine("d_0divInf=" + (zero / double.PositiveInfinity == 0.0));    // True

        // ============ S2.5: modulo (double %) -- D-DBL-1 fix ============
        // IL rem on R8 lowers to the runtime fmod icall (std::fmod == the
        // net48 double-% contract, oracle-verified: sign follows the
        // dividend, x%0 -> NaN, inf%y -> NaN, x%inf -> x). RUNTIME operands
        // only (named locals): a both-constants `%` folds at C# compile time
        // and never reaches the emitter, so it would exercise nothing.
        double seven = 7.0;
        double four = 4.0;
        double ten = 10.0;
        double m1 = 5.5;
        double mneg = -5.5;
        Console.WriteLine("m_1=" + (m1 % two == 1.5));                      // True (5.5 % 2.0)
        Console.WriteLine("m_2=" + (seven % four == 3.0));                  // True
        Console.WriteLine("m_3=" + (mneg % two == -1.5));                   // True (sign follows dividend)
        Console.WriteLine("m_4=" + (m1 % -2.0 == 1.5));                     // True
        Console.WriteLine("m_5=" + (mneg % -2.0 == -1.5));                  // True
        Console.WriteLine("m_6=" + (ten % three == 1.0));                   // True
        Console.WriteLine("m_7=" + (m1 % 0.5 == 0.0));                      // True
        Console.WriteLine("m_8=" + (m1 % zero != m1));                      // True (5.5%0 -> NaN)
        Console.WriteLine("m_9=" + (double.PositiveInfinity % three != three)); // True (inf%3 -> NaN)
        Console.WriteLine("m_10=" + (m1 % double.PositiveInfinity == m1));  // True (5.5%inf -> 5.5)
        Console.WriteLine("m_11=" + (double.NaN % three != double.NaN));    // True (NaN%3 -> NaN)
        double mz = -0.0;
        Console.WriteLine("m_12=" + (BitConverter.DoubleToInt64Bits(mz % three) == long.MinValue)); // True (-0.0%3 keeps sign)
        Console.WriteLine("m_13=" + (m1 % 2.5 == 0.5));                     // True

        // ============ S3: numeric boundaries ============
        Console.WriteLine("b_max=" + (double.MaxValue == 1.7976931348623157E+308)); // True
        Console.WriteLine("b_min=" + (double.MinValue == -1.7976931348623157E+308)); // True
        Console.WriteLine("b_minneg=" + (double.MinValue == -double.MaxValue));   // True
        Console.WriteLine("b_eps=" + (double.Epsilon == 4.9406564584124654E-324)); // True (2^-1074)
        Console.WriteLine("b_epsPos=" + (double.Epsilon > 0.0));                  // True
        Console.WriteLine("b_epsAbsorb=" + (1.0 + double.Epsilon == 1.0));        // True
        Console.WriteLine("b_epsHalf=" + (double.Epsilon / 2.0 == 0.0));          // True (underflow)
        double big = 1e308;
        Console.WriteLine("b_ovf=" + (big * 10.0 == double.PositiveInfinity));    // True
        double max2 = double.MaxValue;
        Console.WriteLine("b_ovf2=" + (max2 * 2.0 == double.PositiveInfinity));   // True
        Console.WriteLine("b_ovf3=" + (max2 + max2 == double.PositiveInfinity));  // True
        double tiny = 1e-320;
        Console.WriteLine("b_unf=" + (tiny * 1e-10 == 0.0));                      // True (1e-330 < 4.9e-324)
        Console.WriteLine("b_subnorm=" + (1e-308 * 0.1 > 0.0));                   // True (1e-309 denormal)

        // NaN comparison lattice + propagation
        Console.WriteLine("b_nanSelf=" + (double.NaN == double.NaN));            // False
        Console.WriteLine("b_nanNe=" + (double.NaN != double.NaN));              // True
        Console.WriteLine("b_nanLt=" + (double.NaN < one));                      // False
        Console.WriteLine("b_nanGt=" + (double.NaN > one));                      // False
        Console.WriteLine("b_nanLe=" + (double.NaN <= double.NaN));              // False
        Console.WriteLine("b_nanGe=" + (double.NaN >= double.NaN));              // False
        Console.WriteLine("b_nanProp=" + (double.NaN * two != two));             // True
        double nanFromOp = zero / zero;
        Console.WriteLine("b_nanFromOp=" + (nanFromOp != nanFromOp));            // True

        // Infinity arithmetic
        Console.WriteLine("b_infAdd=" + (double.PositiveInfinity + one == double.PositiveInfinity)); // True
        Console.WriteLine("b_infSubInf=" + (double.PositiveInfinity + double.NegativeInfinity != 0.0)); // True (NaN)
        Console.WriteLine("b_infMul0=" + (double.PositiveInfinity * 0.0 != 0.0)); // True (NaN)
        Console.WriteLine("b_infMul2=" + (double.PositiveInfinity * two == double.PositiveInfinity)); // True
        Console.WriteLine("b_infNeg=" + (-double.PositiveInfinity == double.NegativeInfinity)); // True

        // signed zero
        Console.WriteLine("b_nzEq=" + (negzero == 0.0));                         // True
        Console.WriteLine("b_nzLt=" + (negzero < 0.0));                          // False
        Console.WriteLine("b_nzBits=" + (BitConverter.DoubleToInt64Bits(negzero) == long.MinValue)); // True
        Console.WriteLine("b_pzBits=" + (BitConverter.DoubleToInt64Bits(0.0) == 0L)); // True

        // ============ S4: rounding (casts + Math.Round/Floor/Ceiling/Truncate) ============
        Console.WriteLine("c_trunc=" + ((int)3.9 == 3));           // True (cast truncates)
        Console.WriteLine("c_truncn=" + ((int)(-3.9) == -3));      // True (toward zero)
        Console.WriteLine("c_trunc75=" + ((int)7.5 == 7));         // True
        Console.WriteLine("c_truncn75=" + ((int)(-7.5) == -7));    // True
        Console.WriteLine("c_intbound=" + ((int)2147483647.0 == int.MaxValue)); // True
        Console.WriteLine("c_long=" + ((long)9.5 == 9L));          // True
        Console.WriteLine("c_longn=" + ((long)(-9.5) == -9L));     // True

        // Math.Round banker's rounding (ToEven)
        Console.WriteLine("r_05=" + (Math.Round(0.5) == 0.0));     // True (tie to even)
        Console.WriteLine("r_15=" + (Math.Round(1.5) == 2.0));     // True
        Console.WriteLine("r_25=" + (Math.Round(2.5) == 2.0));     // True
        Console.WriteLine("r_35=" + (Math.Round(3.5) == 4.0));     // True
        Console.WriteLine("r_n15=" + (Math.Round(-1.5) == -2.0));  // True
        Console.WriteLine("r_n25=" + (Math.Round(-2.5) == -2.0));  // True
        Console.WriteLine("r_n35=" + (Math.Round(-3.5) == -4.0));  // True
        Console.WriteLine("r_24=" + (Math.Round(2.4) == 2.0));     // True
        Console.WriteLine("r_26=" + (Math.Round(2.6) == 3.0));     // True
        Console.WriteLine("r_0=" + (Math.Round(0.0) == 0.0));      // True
        Console.WriteLine("r_1=" + (Math.Round(1.0) == 1.0));      // True

        // Math.Round(double, digits) -- ToEven digit rounding (net48 semantics:
        // Math.Round(2.675, 2) is 2.68 on net48, unlike .NET Core's 2.67)
        Console.WriteLine("r_dig=" + (Math.Round(3.14159, 2) == 3.14));     // True
        Console.WriteLine("r_dig675=" + (Math.Round(2.675, 2) == 2.68));    // True (net48 oracle)
        Console.WriteLine("r_dig125=" + (Math.Round(1.25, 1) == 1.2));      // True (tie to even)
        Console.WriteLine("r_digNeg=" + (Math.Round(-2.5, 0) == -2.0));     // True
        Console.WriteLine("r_digWide=" + (Math.Round(12345.6789, 0) == 12346.0)); // True

        // Floor / Ceiling / Truncate
        Console.WriteLine("fl_27=" + (Math.Floor(2.7) == 2.0));      // True
        Console.WriteLine("fl_n23=" + (Math.Floor(-2.3) == -3.0));   // True (toward -inf)
        Console.WriteLine("cl_23=" + (Math.Ceiling(2.3) == 3.0));    // True
        Console.WriteLine("cl_n27=" + (Math.Ceiling(-2.7) == -2.0)); // True (toward +inf)
        Console.WriteLine("tr_27=" + (Math.Truncate(2.7) == 2.0));   // True
        Console.WriteLine("tr_n27=" + (Math.Truncate(-2.7) == -2.0));// True (toward zero)
        Console.WriteLine("fl_05=" + (Math.Floor(0.5) == 0.0));      // True
        Console.WriteLine("cl_05=" + (Math.Ceiling(0.5) == 1.0));    // True

        // rounding chains
        Console.WriteLine("r_chain=" + (Math.Round(Math.Round(1.5) + 0.4) == 2.0)); // 2.0+0.4 -> 2
        Console.WriteLine("r_chain2=" + ((int)Math.Round(7.5) == 8));              // 8 then cast
    }
}}
