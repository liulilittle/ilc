using System;
using System.Linq;

// wpa-a3-toarray -- audit A3/B3: Enumerable.ToArray must PROPAGATE the source
// array's element kind, and DateTime[]/TimeSpan[] must sort by ticks (net48
// DateTime.CompareTo ignores Kind) instead of being rejected or compared as a
// raw int64 (the 2-bit kind sits in the top bits and flips the sign).
public struct S8 { public long a; }

class P
{
    static string T(Action a)
    {
        try { a(); return "ok"; }
        catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    static void Main()
    {
        long[] la = { 3, 1, 2 };
        var lc = la.ToArray();
        Console.WriteLine("[1] long_toarray_sort " + T(() => { Array.Sort((Array)lc); Console.Write(lc[0] + "," + lc[2] + " "); }));

        double[] da = { 3.5, 1.5, 2.5 };
        var dc = da.ToArray();
        Console.WriteLine("[2] double_toarray_sort " + T(() => { Array.Sort((Array)dc); Console.Write(dc[0] + "," + dc[2] + " "); }));

        Console.WriteLine("[3] double_toarray_join " + string.Join(",", da.ToArray()));

        float[] fa = { 1.5f, 2.25f };
        Console.WriteLine("[4] float_toarray_join " + string.Join(",", fa.ToArray()));

        // DateTime: 2020 Local (kind=2 -> raw has bit63 set) vs 2019 Unspecified.
        // net48 orders by TICKS: 2019 first. A raw-int64 compare puts the Local
        // value first because its sign bit is set.
        var dt = new[] { new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local), new DateTime(2019, 1, 1) };
        Console.WriteLine("[5] datetime_sort " + T(() => { Array.Sort((Array)dt); Console.Write(dt[0].Year + "," + dt[1].Year + " "); }));

        var ts = new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1) };
        Console.WriteLine("[6] timespan_sort " + T(() => { Array.Sort((Array)ts); Console.Write(ts[0].TotalSeconds + "," + ts[1].TotalSeconds + " "); }));

        var s8 = new S8[2]; s8[0].a = 2; s8[1].a = 1;
        Console.WriteLine("[7] struct8_sort " + T(() => { Array.Sort((Array)s8); Console.Write(s8[0].a + " "); }));
    }
}
