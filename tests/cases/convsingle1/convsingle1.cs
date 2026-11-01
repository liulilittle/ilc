// convsingle1 -- Convert.ToString(float) fidelity (the Single overload must
// format at the FLOAT width, not the widened-double width) plus the
// Single.ToString()/ToString(format[, provider]) controls it must agree with.
//
// Oracle (net48 4.8 on the zh-CN host, measured in the seat journal):
//   Convert.ToString(0.1f)          -> "0.1"       (not "0.100000001490116")
//   Convert.ToString(1f / 3f)       -> "0.3333333"
//   Convert.ToString(float.MaxValue)-> "3.402823E+38"
//   float.MaxValue.ToString("R")    -> "3.40282347E+38"  (shortest FLOAT round-trip)
//   float 0.1f      .ToString("G9") -> "0.100000001"
//   float 0.1f      .ToString("F2") -> "0.10"
//   finite values: the provider makes no difference; NaN / +-Infinity follow
//   the provider spelling ("Infinity" invariant, U+221E default zh-CN).
// Every record is tagged and compared byte-for-byte by scripts/difftest.ps1.
using System;
using System.Globalization;

internal static class ConvSingle1
{
    private static void Matrix(float v, string name)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        Console.WriteLine("[conv." + name + "] " + Convert.ToString(v));
        Console.WriteLine("[conv.inv." + name + "] " + Convert.ToString(v, inv));
        Console.WriteLine("[ts." + name + "] " + v.ToString());
        Console.WriteLine("[ts.null.inv." + name + "] " + v.ToString(null, inv));
        Console.WriteLine("[ts.R.inv." + name + "] " + v.ToString("R", inv));
        Console.WriteLine("[ts.G.inv." + name + "] " + v.ToString("G", inv));
        Console.WriteLine("[ts.G9.inv." + name + "] " + v.ToString("G9", inv));
        Console.WriteLine("[ts.F2.inv." + name + "] " + v.ToString("F2", inv));
    }

    private static void Main()
    {
        Matrix(0.1f, "0.1f");
        Matrix(1f / 3f, "third");
        Matrix(float.MaxValue, "Max");
        Matrix(float.MinValue, "Min");
        Matrix(float.Epsilon, "Epsilon");
        Matrix(-0.0f, "negzero");
        Matrix(float.NaN, "NaN");
        Matrix(float.PositiveInfinity, "PosInf");
        Matrix(float.NegativeInfinity, "NegInf");
        Matrix(1e-7f, "1e-7f");
        Matrix(123456789f, "123456789f");
        Matrix(16777216f, "2^24");
        Matrix(0.5f, "0.5f");
        Matrix(1.5f, "1.5f");
        Matrix(100000.0f, "100000f");
        Matrix(3.4028235E+38f, "MaxLit");
    }
}