using System;
using System.Globalization;

class ProbeSingle
{
    static void W(string k, object v) { Console.WriteLine(k + "=" + v); }

    static void Main()
    {
        Console.WriteLine("=== IsNaN / IsInfinity ===");
        W("nan", float.IsNaN(float.NaN));
        W("inf", float.IsInfinity(float.PositiveInfinity));
        W("posinf", float.IsPositiveInfinity(float.PositiveInfinity));
        W("neginf", float.IsNegativeInfinity(float.NegativeInfinity));
        W("nan_isinf", float.IsInfinity(float.NaN));
        W("nan_ispos", float.IsPositiveInfinity(float.NaN));
        W("nan_isneg", float.IsNegativeInfinity(float.NaN));
        W("one_isinf", float.IsInfinity(1f));
        W("one_ispos", float.IsPositiveInfinity(1f));
        W("one_isneg", float.IsNegativeInfinity(1f));
        W("zero_isnan", float.IsNaN(0f));

        Console.WriteLine("=== Parse ===");
        W("p1", float.Parse("1.5").ToString("R"));
        W("p2", float.Parse("1.5", NumberStyles.Float).ToString("R"));
        W("p3", float.Parse("1.5", CultureInfo.InvariantCulture).ToString("R"));
        W("p4", float.Parse("1.5", NumberStyles.Float, CultureInfo.InvariantCulture).ToString("R"));
        W("p5", float.Parse("-0").ToString("R"));
        try { float.Parse("abc"); } catch (Exception e) { W("perr", e.GetType().Name); }
        try { float.Parse(null); } catch (Exception e) { W("pnull", e.GetType().Name); }
        try { float.Parse("1e999"); } catch (Exception e) { W("povf", e.GetType().Name); }

        Console.WriteLine("=== TryParse ===");
        float r;
        W("t1", float.TryParse("1.5", out r) + ":" + r.ToString("R"));
        W("t2", float.TryParse("abc", out r) + ":" + r.ToString("R"));
        W("t3", float.TryParse("1.5", NumberStyles.Float, CultureInfo.InvariantCulture, out r) + ":" + r.ToString("R"));
        W("t4", float.TryParse("abc", NumberStyles.Float, CultureInfo.InvariantCulture, out r) + ":" + r.ToString("R"));

        Console.WriteLine("=== operators ===");
        W("eq", 1f == 1f);
        W("ne", 1f != 2f);
        W("lt", 1f < 2f);
        W("gt", 2f > 1f);
        W("le", 1f <= 1f);
        W("ge", 1f >= 1f);
        W("eqnan", float.NaN == float.NaN);
        W("ltnan", float.NaN < 1f);
    }
}