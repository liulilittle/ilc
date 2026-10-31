// mathmisc1 -- the Math surface the other math cases do not pin:
// IEEERemainder (sign lattice + NaN/infinity arms), BigMul(int,int),
// DivRem(int/long, negative operands, MinValue/-1 and divide-by-zero),
// Sign(double/float/decimal, incl. -0.0 and the NaN ArithmeticException),
// Min/Max NaN propagation, Truncate (incl. the -0.0 result for -0.5) and
// Abs on every extreme (long.MinValue/int.MinValue/sbyte/short overflow,
// double/decimal specials).
//
// Doubles are never printed as text: they are compared or reduced to their
// IEEE-754 bit pattern via BitConverter.DoubleToInt64Bits, so no NaN/infinity
// glyph or culture-dependent decimal separator can reach the byte stream.
using System;
using System.Globalization;

internal static class MathMisc1
{
    private static long B(double v) { return BitConverter.DoubleToInt64Bits(v); }

    private static void Try(string tag, Func<object> f)
    {
        try { Console.WriteLine("[" + tag + "] " + Show(f())); }
        catch (Exception ex) { Console.WriteLine("[" + tag + "] " + ex.GetType().Name); }
    }

    private static string Show(object o)
    {
        if (o is bool b) return b ? "True" : "False";
        if (o is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (o is long l) return l.ToString(CultureInfo.InvariantCulture);
        if (o is decimal m) return m.ToString(CultureInfo.InvariantCulture);
        if (o is double d) return B(d).ToString(CultureInfo.InvariantCulture);
        return o.ToString();
    }

    private static bool IsNaN(double v) { return v != v; }

    private static void Main()
    {
        // ---- A. Math.IEEERemainder ---------------------------------------
        // x - y * round(x / y), round = half-to-even.
        Try("ieee.3_2", () => Math.IEEERemainder(3.0, 2.0));
        Try("ieee.4_2", () => Math.IEEERemainder(4.0, 2.0));
        Try("ieee.5_3", () => Math.IEEERemainder(5.0, 3.0));
        Try("ieee.5_2", () => Math.IEEERemainder(5.0, 2.0));
        Try("ieee.n5_3", () => Math.IEEERemainder(-5.0, 3.0));
        Try("ieee.5_n3", () => Math.IEEERemainder(5.0, -3.0));
        Try("ieee.n5_n3", () => Math.IEEERemainder(-5.0, -3.0));
        Try("ieee.0_3", () => Math.IEEERemainder(0.0, 3.0));
        Try("ieee.7_2", () => Math.IEEERemainder(7.0, 2.0));
        Try("ieee.7_4", () => Math.IEEERemainder(7.0, 4.0));
        Try("ieee.9_4", () => Math.IEEERemainder(9.0, 4.0));
        Try("ieee.3_0_isnan", () => IsNaN(Math.IEEERemainder(3.0, 0.0)));
        Try("ieee.inf_3_isnan", () => IsNaN(Math.IEEERemainder(double.PositiveInfinity, 3.0)));
        Try("ieee.3_inf", () => Math.IEEERemainder(3.0, double.PositiveInfinity));
        Try("ieee.nan_3_isnan", () => IsNaN(Math.IEEERemainder(double.NaN, 3.0)));

        // ---- B. Math.BigMul(int, int) ------------------------------------
        Try("bigmul.65536", () => Math.BigMul(65536, 65536));
        Try("bigmul.maxmax", () => Math.BigMul(int.MaxValue, int.MaxValue));
        Try("bigmul.m1m1", () => Math.BigMul(-1, -1));
        Try("bigmul.maxmin", () => Math.BigMul(int.MaxValue, int.MinValue));
        Try("bigmul.46341", () => Math.BigMul(46341, 46341));
        Try("bigmul.zero", () => Math.BigMul(0, int.MinValue));

        // ---- C. Math.DivRem(int, int, out int) ----------------------------
        Try("divrem.7_3", () => { int r; int q = Math.DivRem(7, 3, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divrem.n7_3", () => { int r; int q = Math.DivRem(-7, 3, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divrem.7_n3", () => { int r; int q = Math.DivRem(7, -3, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divrem.n7_n3", () => { int r; int q = Math.DivRem(-7, -3, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divrem.0_5", () => { int r; int q = Math.DivRem(0, 5, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divrem.min_m1", () => { int r; int q = Math.DivRem(int.MinValue, -1, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divrem.1_0", () => { int r; int q = Math.DivRem(1, 0, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });

        // ---- D. Math.DivRem(long, long, out long) -------------------------
        Try("divreml.7_3", () => { long r; long q = Math.DivRem(7L, 3L, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divreml.n7_3", () => { long r; long q = Math.DivRem(-7L, 3L, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divreml.7_n3", () => { long r; long q = Math.DivRem(7L, -3L, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divreml.min_m1", () => { long r; long q = Math.DivRem(long.MinValue, -1L, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });
        Try("divreml.1_0", () => { long r; long q = Math.DivRem(1L, 0L, out r); return q.ToString(CultureInfo.InvariantCulture) + "," + r.ToString(CultureInfo.InvariantCulture); });

        // ---- E. Math.Sign -------------------------------------------------
        Try("sign.i.5", () => Math.Sign(5));
        Try("sign.i.m5", () => Math.Sign(-5));
        Try("sign.i.0", () => Math.Sign(0));
        Try("sign.l.m5", () => Math.Sign(-5L));
        Try("sign.d.55", () => Math.Sign(5.5));
        Try("sign.d.m05", () => Math.Sign(-0.5));
        Try("sign.d.0", () => Math.Sign(0.0));
        Try("sign.d.nzero", () => Math.Sign(-0.0));
        Try("sign.d.nan", () => Math.Sign(double.NaN));
        Try("sign.d.inf", () => Math.Sign(double.PositiveInfinity));
        Try("sign.d.ninf", () => Math.Sign(double.NegativeInfinity));
        Try("sign.f.55", () => Math.Sign(5.5f));
        Try("sign.f.m05", () => Math.Sign(-0.5f));
        Try("sign.f.0", () => Math.Sign(0.0f));
        Try("sign.f.nzero", () => Math.Sign(-0.0f));
        Try("sign.f.nan", () => Math.Sign(float.NaN));
        Try("sign.m.55", () => Math.Sign(5.5m));
        Try("sign.m.m05", () => Math.Sign(-0.5m));
        Try("sign.m.0", () => Math.Sign(0m));

        // ---- F. Min/Max NaN propagation -----------------------------------
        Try("max.1_nan_isnan", () => IsNaN(Math.Max(1.0, double.NaN)));
        Try("max.nan_1_isnan", () => IsNaN(Math.Max(double.NaN, 1.0)));
        Try("min.1_nan_isnan", () => IsNaN(Math.Min(1.0, double.NaN)));
        Try("max.nan_nan_isnan", () => IsNaN(Math.Max(double.NaN, double.NaN)));
        Try("max.m1_m2", () => Math.Max(-1.0, -2.0));
        Try("min.m1_m2", () => Math.Min(-1.0, -2.0));
        Try("max.i", () => Math.Max(-2147483648, 7));
        Try("min.l", () => Math.Min(long.MinValue, 7L));
        Try("max.m", () => Math.Max(-1.5m, 2.5m));

        // ---- G. Math.Truncate ---------------------------------------------
        Try("trunc.29", () => Math.Truncate(2.9));
        Try("trunc.n29", () => Math.Truncate(-2.9));
        Try("trunc.05", () => Math.Truncate(0.5));
        Try("trunc.n05_bits", () => Math.Truncate(-0.5));
        Try("trunc.nan_isnan", () => IsNaN(Math.Truncate(double.NaN)));
        Try("trunc.inf", () => Math.Truncate(double.PositiveInfinity));
        Try("trunc.ninf", () => Math.Truncate(double.NegativeInfinity));

        // ---- H. Math.Abs extremes -----------------------------------------
        Try("abs.i.m5", () => Math.Abs(-5));
        Try("abs.i.min", () => Math.Abs(int.MinValue));
        Try("abs.l.m5", () => Math.Abs(-5L));
        Try("abs.l.min", () => Math.Abs(long.MinValue));
        Try("abs.sb.min", () => Math.Abs((sbyte)-128));
        Try("abs.sh.min", () => Math.Abs((short)-32768));
        Try("abs.d.m15", () => Math.Abs(-1.5));
        Try("abs.d.nan_isnan", () => IsNaN(Math.Abs(double.NaN)));
        Try("abs.d.nzero_bits", () => Math.Abs(-0.0));
        Try("abs.d.inf", () => Math.Abs(double.NegativeInfinity));
        Try("abs.m.m15", () => Math.Abs(-1.5m));
        Try("abs.m.min", () => Math.Abs(decimal.MinValue));
    }
}
