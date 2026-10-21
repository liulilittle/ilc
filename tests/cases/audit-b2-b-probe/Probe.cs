using System;
using System.Globalization;

class AuditProbeB
{
    private static void W(string id, string v) { Console.WriteLine("[b2] " + id + "=" + v); }
    private static string D(decimal d) { return d.ToString(CultureInfo.InvariantCulture); }
    private static string S(double d) { return d.ToString(CultureInfo.InvariantCulture); }
    private static string S(long v) { return v.ToString(CultureInfo.InvariantCulture); }
    private static string S(int v) { return v.ToString(CultureInfo.InvariantCulture); }

    private static void Main()
    {
        try { W("abs-min", D(Math.Abs(decimal.MinValue))); } catch (Exception e) { W("abs-min", "EX:" + e.GetType().FullName); }
        try { W("abs-negzero", D(Math.Abs(-0.0m))); } catch (Exception e) { W("abs-negzero", "EX:" + e.GetType().FullName); }

        try { W("max-10-100", D(Math.Max(1.0m, 1.00m))); } catch (Exception e) { W("max-10-100", "EX:" + e.GetType().FullName); }
        try { W("max-100-10", D(Math.Max(1.00m, 1.0m))); } catch (Exception e) { W("max-100-10", "EX:" + e.GetType().FullName); }
        try { W("min-10-100", D(Math.Min(1.0m, 1.00m))); } catch (Exception e) { W("min-10-100", "EX:" + e.GetType().FullName); }
        try { W("min-100-10", D(Math.Min(1.00m, 1.0m))); } catch (Exception e) { W("min-100-10", "EX:" + e.GetType().FullName); }
        try { W("min-scale", D(Math.Min(1.0m, 2.00m))); } catch (Exception e) { W("min-scale", "EX:" + e.GetType().FullName); }
        try { W("max-format", D(Math.Max(1.0m, 2m))); } catch (Exception e) { W("max-format", "EX:" + e.GetType().FullName); }

        try { W("round-25", D(Math.Round(2.5m))); } catch (Exception e) { W("round-25", "EX:" + e.GetType().FullName); }
        try { W("round-35", D(Math.Round(3.5m))); } catch (Exception e) { W("round-35", "EX:" + e.GetType().FullName); }
        try { W("round-neg25", D(Math.Round(-2.5m))); } catch (Exception e) { W("round-neg25", "EX:" + e.GetType().FullName); }
        try { W("round-1005-2", D(Math.Round(1.005m, 2))); } catch (Exception e) { W("round-1005-2", "EX:" + e.GetType().FullName); }
        try { W("round-1015-2", D(Math.Round(1.015m, 2))); } catch (Exception e) { W("round-1015-2", "EX:" + e.GetType().FullName); }
        try { W("round-29", D(Math.Round(1.234m, 29))); } catch (Exception e) { W("round-29", "EX:" + e.GetType().FullName); }
        try { W("round-neg1", D(Math.Round(1.234m, -1))); } catch (Exception e) { W("round-neg1", "EX:" + e.GetType().FullName); }
        try { W("round-28", D(Math.Round(1.234m, 28))); } catch (Exception e) { W("round-28", "EX:" + e.GetType().FullName); }

        try { W("floor-neg15", D(Math.Floor(-1.5m))); } catch (Exception e) { W("floor-neg15", "EX:" + e.GetType().FullName); }
        try { W("ceil-neg15", D(Math.Ceiling(-1.5m))); } catch (Exception e) { W("ceil-neg15", "EX:" + e.GetType().FullName); }
        try { W("trunc-neg15", D(Math.Truncate(-1.5m))); } catch (Exception e) { W("trunc-neg15", "EX:" + e.GetType().FullName); }
        try { W("sign-neg05", S(Math.Sign(-0.5m))); } catch (Exception e) { W("sign-neg05", "EX:" + e.GetType().FullName); }
        try { W("sign-zero", S(Math.Sign(0m))); } catch (Exception e) { W("sign-zero", "EX:" + e.GetType().FullName); }
        try { W("sign-negzero", S(Math.Sign(-0.0m))); } catch (Exception e) { W("sign-negzero", "EX:" + e.GetType().FullName); }

        try { W("abs-nan", S(Math.Abs(double.NaN))); } catch (Exception e) { W("abs-nan", "EX:" + e.GetType().FullName); }
        try { W("abs-longmin", S(Math.Abs(long.MinValue))); } catch (Exception e) { W("abs-longmin", "EX:" + e.GetType().FullName); }
        try { W("abs-intmin", S(Math.Abs(int.MinValue))); } catch (Exception e) { W("abs-intmin", "EX:" + e.GetType().FullName); }
        try { W("sign-dnegzero", S(Math.Sign(-0.0))); } catch (Exception e) { W("sign-dnegzero", "EX:" + e.GetType().FullName); }
        try { W("max-double", S(Math.Max(1.0, 2.0))); } catch (Exception e) { W("max-double", "EX:" + e.GetType().FullName); }
        try { W("floor-double", S(Math.Floor(-1.5))); } catch (Exception e) { W("floor-double", "EX:" + e.GetType().FullName); }
    }
}
