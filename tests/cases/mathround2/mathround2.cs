// mathround2 -- Math.Round digits/mode boundary lattice, printed as raw
// IEEE-754 bit patterns (long) so the comparison is culture- and
// console-encoding-proof. Enum-typed helper parameters are avoided (the
// transpiler emits the raw enum name for user-method enum params); the mode
// travels as int and is cast at the call site.
using System;

internal static class MathRound2
{
    private static long B(double v) { return BitConverter.DoubleToInt64Bits(v); }

    private static void R1(string k, double v)
    {
        Console.WriteLine("[" + k + "] " + B(Math.Round(v)));
    }

    private static void R2(string k, double v, int d)
    {
        Console.WriteLine("[" + k + "] " + B(Math.Round(v, d)));
    }

    private static void R3(string k, double v, int m)
    {
        Console.WriteLine("[" + k + "] " + B(Math.Round(v, (MidpointRounding)m)));
    }

    private static void R4(string k, double v, int d, int m)
    {
        Console.WriteLine("[" + k + "] " + B(Math.Round(v, d, (MidpointRounding)m)));
    }

    private static void Main()
    {
        // default ToEven at zero digits
        R1("r1_05", 0.5); R1("r1_15", 1.5); R1("r1_25", 2.5); R1("r1_35", 3.5);
        R1("r1_n05", -0.5); R1("r1_n15", -1.5); R1("r1_n25", -2.5);
        R1("r1_149", 1.4999999999999998); R1("r1_15down", 1.4999999999999999);
        R1("r1_zero", 0.0); R1("r1_nzero", -0.0); R1("r1_neg", -2.4);
        // specials through the ToEven path
        R1("r1_nan", double.NaN);
        R1("r1_inf", double.PositiveInfinity);
        R1("r1_ninf", double.NegativeInfinity);
        R1("r1_max", double.MaxValue);
        R1("r1_min", double.MinValue);
        R1("r1_eps", double.Epsilon);
        R1("r1_neps", -double.Epsilon);

        // digits 0..15 with the net48 multiply-scale algorithm
        for (int d = 0; d <= 15; d++)
        {
            R2("r2_314159_" + d, 3.141592653589793, d);
        }
        R2("r2_2675_2", 2.675, 2);
        R2("r2_1005_2", 1.005, 2);
        R2("r2_1015_2", 1.015, 2);
        R2("r2_0125_2", 0.125, 2);
        R2("r2_0135_2", 0.135, 2);
        R2("r2_25_0", 2.5, 0);
        R2("r2_35_0", 3.5, 0);
        R2("r2_1e15_2", 1e15, 2);
        R2("r2_1e16_2", 1e16, 2);
        R2("r2_nan_2", double.NaN, 2);
        R2("r2_inf_2", double.PositiveInfinity, 2);
        R2("r2_ninf_2", double.NegativeInfinity, 2);
        R2("r2_nzero_2", -0.0, 2);

        // MidpointRounding forms (0 = ToEven, 1 = AwayFromZero)
        R3("r3_25_even", 2.5, 0);
        R3("r3_25_away", 2.5, 1);
        R3("r3_n25_away", -2.5, 1);
        R3("r3_35_away", 3.5, 1);
        R3("r3_14_away", 1.4, 1);
        R3("r3_nzero_away", -0.0, 1);
        R3("r3_nan_away", double.NaN, 1);
        R3("r3_inf_away", double.PositiveInfinity, 1);
        R4("r4_245_1_away", 2.45, 1, 1);
        R4("r4_244_1_away", 2.44, 1, 1);
        R4("r4_245_1_even", 2.45, 1, 0);
        R4("r4_235_1_even", 2.35, 1, 0);
        R4("r4_n245_1_away", -2.45, 1, 1);
        R4("r4_314159_3_away", 3.14159, 3, 1);
        R4("r4_314159_3_even", 3.14159, 3, 0);
        R4("r4_2675_2_even", 2.675, 2, 0);
        R4("r4_2675_2_away", 2.675, 2, 1);

        // argument validation
        try { Math.Round(1.5, -1); Console.WriteLine("[bad_digits_neg] ok"); }
        catch (Exception e) { Console.WriteLine("[bad_digits_neg] " + e.GetType().FullName); }
        try { Math.Round(1.5, 16); Console.WriteLine("[bad_digits_16] ok"); }
        catch (Exception e) { Console.WriteLine("[bad_digits_16] " + e.GetType().FullName); }
        try { Math.Round(1.5, 99); Console.WriteLine("[bad_digits_99] ok"); }
        catch (Exception e) { Console.WriteLine("[bad_digits_99] " + e.GetType().FullName); }
        try { Math.Round(1.5, -1, MidpointRounding.AwayFromZero); Console.WriteLine("[bad_digits_m_neg] ok"); }
        catch (Exception e) { Console.WriteLine("[bad_digits_m_neg] " + e.GetType().FullName); }
        try { Math.Round(1.5, 16, MidpointRounding.ToEven); Console.WriteLine("[bad_digits_m_16] ok"); }
        catch (Exception e) { Console.WriteLine("[bad_digits_m_16] " + e.GetType().FullName); }
        try { Math.Round(1.5, (MidpointRounding)5); Console.WriteLine("[bad_mode] ok"); }
        catch (Exception e) { Console.WriteLine("[bad_mode] " + e.GetType().FullName); }
    }
}