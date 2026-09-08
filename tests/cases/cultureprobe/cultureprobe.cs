using System;
using System.Globalization;
class CultureProbe
{
    static void Main()
    {
        var inv = CultureInfo.InvariantCulture;
        var cur = CultureInfo.CurrentCulture;
        Console.WriteLine("inv-null=" + (inv == null));
        Console.WriteLine("inv-name=" + (inv.Name == ""));
        // The CurrentCulture NAME is host-dependent by design (zh-CN on the
        // Windows verification host, "" on non-Windows hosts); everything
        // below is asserted relative to it so all toolchains agree.
        bool ci = cur.Name == ""; // current behaves as invariant?

        // double P: invariant " %" vs current "%" (provider-aware);
        // no-provider (def) is FIXED zh-CN layout (deterministic across hosts)
        Console.WriteLine("dP0-inv=" + (0.5.ToString("P0", inv) == "50 %"));
        Console.WriteLine("dP0-cur=" + (0.5.ToString("P0", cur) == (ci ? "50 %" : "50%")));
        Console.WriteLine("dP0-def=" + (0.5.ToString("P0") == "50%"));
        Console.WriteLine("dP0-nul=" + (0.5.ToString("P0", (IFormatProvider)null) == (ci ? "50 %" : "50%")));
        Console.WriteLine("dP2n-inv=" + ((-0.255).ToString("P2", inv) == "-25.50 %"));

        // double C: invariant U+00A4 + parens vs current U+00A5 + sign-after
        Console.WriteLine("dCp-inv=" + (1234.5.ToString("C", inv) == "\u00A41,234.50"));
        Console.WriteLine("dCp-cur=" + (1234.5.ToString("C", cur) == (ci ? "\u00A41,234.50" : "\u00A51,234.50")));
        Console.WriteLine("dCn-inv=" + ((-1234.5).ToString("C", inv) == "(\u00A41,234.50)"));
        Console.WriteLine("dCn-cur=" + ((-1234.5).ToString("C", cur) == (ci ? "(\u00A41,234.50)" : "\u00A5-1,234.50")));
        Console.WriteLine("dCn-def=" + ((-1234.5).ToString("C") == "\u00A5-1,234.50"));

        // decimal P/C: same divergence surface
        Console.WriteLine("mP-inv=" + (1234.5678m.ToString("P", inv) == "123,456.78 %"));
        Console.WriteLine("mP-cur=" + (1234.5678m.ToString("P", cur) == (ci ? "123,456.78 %" : "123,456.78%")));
        Console.WriteLine("mCp-inv=" + (3.5m.ToString("C", inv) == "\u00A43.50"));
        Console.WriteLine("mCn-inv=" + ((-3.5m).ToString("C", inv) == "(\u00A43.50)"));
        Console.WriteLine("mCn-cur=" + ((-3.5m).ToString("C", cur) == (ci ? "(\u00A43.50)" : "\u00A5-3.50")));
        Console.WriteLine("mCn-def=" + ((-3.5m).ToString("C") == "\u00A5-3.50"));
    }
}
