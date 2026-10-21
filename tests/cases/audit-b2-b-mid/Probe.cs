using System;
using System.Globalization;

class AuditProbeBMid
{
    private static void W(string id, string v) { Console.WriteLine("[b2mid] " + id + "=" + v); }
    private static string D(decimal d) { return d.ToString(CultureInfo.InvariantCulture); }

    private static void Main()
    {
        W("away-25", D(Math.Round(2.5m, MidpointRounding.AwayFromZero)));
        W("away-neg25", D(Math.Round(-2.5m, MidpointRounding.AwayFromZero)));
        W("even-25", D(Math.Round(2.5m, MidpointRounding.ToEven)));
        W("even-neg25", D(Math.Round(-2.5m, MidpointRounding.ToEven)));
        W("away-12345-3", D(Math.Round(1.2345m, 3, MidpointRounding.AwayFromZero)));
        W("even-12345-3", D(Math.Round(1.2345m, 3, MidpointRounding.ToEven)));
        W("away-1005-2", D(Math.Round(1.005m, 2, MidpointRounding.AwayFromZero)));
        W("even-1005-2", D(Math.Round(1.005m, 2, MidpointRounding.ToEven)));
        W("away-25-0", D(Math.Round(2.5m, 0, MidpointRounding.AwayFromZero)));
        W("even-25-0", D(Math.Round(2.5m, 0, MidpointRounding.ToEven)));

        try { W("mode5-2arg", "no-throw:" + D(Math.Round(1.5m, (MidpointRounding)5))); }
        catch (Exception e) { W("mode5-2arg", e.GetType().FullName); }
        try { W("mode5-3arg", "no-throw:" + D(Math.Round(1.5m, 0, (MidpointRounding)5))); }
        catch (Exception e) { W("mode5-3arg", e.GetType().FullName); }
        try { W("mode5-3argdigits", "no-throw:" + D(Math.Round(1.234m, 2, (MidpointRounding)5))); }
        catch (Exception e) { W("mode5-3argdigits", e.GetType().FullName); }
    }
}
