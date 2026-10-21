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

        // A3 (8): mixed DateTimeKind. net48 orders by TICKS only; the packed
        // kind must never dominate the order (Kind 1/2 set high bits).
        var mx = new[] { new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1) };
        Console.WriteLine("[8] dt_mixed_sort " + T(() => { Array.Sort((Array)mx); Console.Write(mx[0].Year + "," + mx[1].Year + " "); }));

        var mx3 = new[] { new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1) };
        Console.WriteLine("[9] dt_mixed3_sort " + T(() => { Array.Sort((Array)mx3); Console.Write(mx3[0].Year + "," + mx3[1].Year + "," + mx3[2].Year + " "); }));

        // A3 (10): ticks differ by ONE, and the two later entries differ only
        // by kind -- a coarse (>>2) compare collapses them.
        // NOTE: DateTime closures are not supported (captured value locals
        // become int32), so every DateTime is constructed INSIDE the lambda.
        var nearDt = new[] { new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(1), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        Console.WriteLine("[10] dt_tick1_sort " + T(() => { Array.Sort((Array)nearDt); Console.Write(nearDt[0].Equals(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)) ? "asc" : "desc"); }));
        var dup = new[] { new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Local), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(1) };
        Console.WriteLine("[11] dt_dupkind_sort " + T(() => { Array.Sort((Array)dup); Console.Write(dup[0].Equals(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)) + "," + dup[1].Equals(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)) + "," + dup[2].Equals(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(1))); }));

        // A3 (12): array type identity survives the kinded allocator.
        var ida = new DateTime[2];
        Console.WriteLine("[12] dt_array_type " + ida.GetType().Name + " " + (ida is DateTime[]) + " " + (ida as DateTime[] != null));

        // A3 (13): consumers of elemKind 6.
        var ja = new[] { new DateTime(2020, 1, 2), new DateTime(2019, 3, 4) };
        Console.WriteLine("[13] dt_join " + string.Join("|", ja));
        Console.WriteLine("[14] dt_indexof " + Array.IndexOf(ja, new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc)) + "," + Array.IndexOf(ja, new DateTime(2021, 1, 1)));
        var bs = new[] { new DateTime(2019, 3, 4), new DateTime(2020, 1, 2) };
        Console.WriteLine("[15] dt_binsearch " + T(() => { Console.Write(Array.BinarySearch(bs, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)) + " "); }));

        // A3 (16) RECORDED GAP: net48 throws ArrayTypeMismatchException for
        // Array.Copy(long[] -> DateTime[]) while the runtime copies silently.
        // Not executed here so the probe stays byte-identical to net48; the
        // gap is documented in the commit message and progress notes.

        // A4 (17): Kind order OPPOSES ticks order (Utc-2020 word is larger
        // than Unspec-2021 only in the kind bits) -- a >>2 compare inverts it.
        var rev = new[] { new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2021, 1, 1) };
        Console.WriteLine("[17] dt_kind_vs_ticks " + T(() => { Array.Sort((Array)rev); Console.Write(rev[0].Year + "," + rev[1].Year + " "); }));

        // A4 (18): keys5/items2 with the range limited to the first two slots.
        var keys5 = new[] { 3, 1, 5, 4, 2 };
        var items2 = new[] { "b", "a" };
        Console.WriteLine("[18] keys_items_range " + T(() => { Array.Sort(keys5, items2, 0, 2); Console.Write(keys5[0] + "," + keys5[1] + " " + items2[0] + items2[1] + " "); }));
    }
}
