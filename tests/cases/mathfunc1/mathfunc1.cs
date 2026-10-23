// mathfunc1 -- transcendental / power / log bit-exactness against net48.
// Every result is printed as its raw IEEE-754 pattern.
using System;

internal static class MathFunc1
{
    private static long B(double v) { return BitConverter.DoubleToInt64Bits(v); }

    private static void U(string k, double v) { Console.WriteLine("[" + k + "] " + B(v)); }

    private static readonly double[] Vals = new double[]
    {
        0.0, -0.0, 0.5, -0.5, 1.0, -1.0, 2.0, 3.0, 10.0, 0.1, 1.5, 2.5,
        -2.5, 100.0, 1e-10, 1e10, 1e300, 1e-300, 0.25, 4.0, 7.0,
        double.NaN, double.PositiveInfinity, double.NegativeInfinity,
        double.MaxValue, double.MinValue, double.Epsilon, Math.PI, Math.E,
    };

    private static void Main()
    {
        int i = 0;
        foreach (double v in Vals)
        {
            string t = i.ToString();
            U("sqrt_" + t, Math.Sqrt(v));
            U("exp_" + t, Math.Exp(v));
            U("log_" + t, Math.Log(v));
            U("log10_" + t, Math.Log10(v));
            U("sin_" + t, Math.Sin(v));
            U("cos_" + t, Math.Cos(v));
            U("tan_" + t, Math.Tan(v));
            U("asin_" + t, Math.Asin(v));
            U("acos_" + t, Math.Acos(v));
            U("atan_" + t, Math.Atan(v));
            U("sinh_" + t, Math.Sinh(v));
            U("cosh_" + t, Math.Cosh(v));
            U("tanh_" + t, Math.Tanh(v));
            U("floor_" + t, Math.Floor(v));
            U("ceil_" + t, Math.Ceiling(v));
            U("trunc_" + t, Math.Truncate(v));
            U("abs_" + t, Math.Abs(v));
            i++;
        }

        // Pow: full cross product over a compact base/exponent set
        double[] bases = new double[] { 0.0, -0.0, 1.0, -1.0, 2.0, -2.0, 3.0, 10.0, 0.5, -0.5, 1.5, 100.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 7.0 };
        double[] exps = new double[] { 0.0, -0.0, 1.0, -1.0, 2.0, 3.0, 0.5, -0.5, 10.0, 23.0, 0.1, -3.0, 1000.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
        for (int a = 0; a < bases.Length; a++)
        {
            for (int b = 0; b < exps.Length; b++)
            {
                U("pow_" + a + "_" + b, Math.Pow(bases[a], exps[b]));
            }
        }

        // Atan2 cross product
        for (int a = 0; a < bases.Length; a++)
        {
            for (int b = 0; b < exps.Length; b++)
            {
                U("atan2_" + a + "_" + b, Math.Atan2(bases[a], exps[b]));
            }
        }

        // Log(a, base) cross product
        for (int a = 0; a < bases.Length; a++)
        {
            for (int b = 0; b < exps.Length; b++)
            {
                U("logb_" + a + "_" + b, Math.Log(bases[a], exps[b]));
            }
        }

        // IEEERemainder cross product
        for (int a = 0; a < bases.Length; a++)
        {
            for (int b = 0; b < exps.Length; b++)
            {
                U("rem_" + a + "_" + b, Math.IEEERemainder(bases[a], exps[b]));
            }
        }
    }
}