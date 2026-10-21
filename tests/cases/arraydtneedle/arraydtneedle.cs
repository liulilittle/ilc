using System;

// arraydtneedle -- the needle SHAPE must not matter for the generic
// IndexOf<DateTime>/BinarySearch<DateTime>/IndexOf<TimeSpan>/BinarySearch<TimeSpan>
// instantiations. Six shapes per API: inline ctor, local variable, method
// parameter, static field, array element, method return value.
// Also covers empty/single arrays, index+length == Length, out-of-range index,
// DateTime.MinValue/MaxValue, DateTimeKind preservation after a sort, and the
// BinarySearch complement for middle (-2) and past-the-end (-3) insertion
// points (a "miss is always -1" implementation is distinguishable).
class Q
{
    static readonly DateTime SfDt = new DateTime(2019, 3, 4);
    static readonly TimeSpan SfTs = TimeSpan.FromSeconds(3);

    static DateTime GetDt() { return new DateTime(2019, 3, 4); }
    static TimeSpan GetTs() { return TimeSpan.FromSeconds(3); }

    static string T(Action a)
    {
        try { a(); return "ok"; }
        catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    static int IxDt(DateTime[] a, DateTime n) { return Array.IndexOf(a, n); }
    static int BsDt(DateTime[] a, DateTime n) { return Array.BinarySearch(a, n); }
    static int IxTs(TimeSpan[] a, TimeSpan n) { return Array.IndexOf(a, n); }
    static int BsTs(TimeSpan[] a, TimeSpan n) { return Array.BinarySearch(a, n); }

    static void Main()
    {
        var dts = new[] { new DateTime(2019, 3, 4), new DateTime(2020, 1, 2), new DateTime(2021, 5, 6) };
        var tss = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7) };

        // [1..6] IndexOf<DateTime> across the six needle shapes.
        Console.WriteLine("[1] ix_dt_inline " + Array.IndexOf(dts, new DateTime(2019, 3, 4)));
        var n1 = new DateTime(2020, 1, 2);
        Console.WriteLine("[2] ix_dt_local " + Array.IndexOf(dts, n1));
        Console.WriteLine("[3] ix_dt_param " + IxDt(dts, new DateTime(2021, 5, 6)));
        Console.WriteLine("[4] ix_dt_field " + Array.IndexOf(dts, SfDt));
        Console.WriteLine("[5] ix_dt_elem " + Array.IndexOf(dts, dts[1]));
        Console.WriteLine("[6] ix_dt_return " + Array.IndexOf(dts, GetDt()));

        // [7..12] BinarySearch<DateTime> across the same six shapes.
        Console.WriteLine("[7] bs_dt_inline " + Array.BinarySearch(dts, new DateTime(2021, 5, 6)));
        var n2 = new DateTime(2019, 3, 4);
        Console.WriteLine("[8] bs_dt_local " + Array.BinarySearch(dts, n2));
        Console.WriteLine("[9] bs_dt_param " + BsDt(dts, new DateTime(2020, 1, 2)));
        Console.WriteLine("[10] bs_dt_field " + Array.BinarySearch(dts, SfDt));
        Console.WriteLine("[11] bs_dt_elem " + Array.BinarySearch(dts, dts[0]));
        Console.WriteLine("[12] bs_dt_return " + Array.BinarySearch(dts, GetDt()));

        // [13..18] IndexOf<TimeSpan> across the six shapes.
        Console.WriteLine("[13] ix_ts_inline " + Array.IndexOf(tss, TimeSpan.FromSeconds(3)));
        var t1 = TimeSpan.FromSeconds(7);
        Console.WriteLine("[14] ix_ts_local " + Array.IndexOf(tss, t1));
        Console.WriteLine("[15] ix_ts_param " + IxTs(tss, TimeSpan.FromSeconds(1)));
        Console.WriteLine("[16] ix_ts_field " + Array.IndexOf(tss, SfTs));
        Console.WriteLine("[17] ix_ts_elem " + Array.IndexOf(tss, tss[2]));
        Console.WriteLine("[18] ix_ts_return " + Array.IndexOf(tss, GetTs()));

        // [19..24] BinarySearch<TimeSpan> across the six shapes.
        Console.WriteLine("[19] bs_ts_inline " + Array.BinarySearch(tss, TimeSpan.FromSeconds(1)));
        var t2 = TimeSpan.FromSeconds(3);
        Console.WriteLine("[20] bs_ts_local " + Array.BinarySearch(tss, t2));
        Console.WriteLine("[21] bs_ts_param " + BsTs(tss, TimeSpan.FromSeconds(7)));
        Console.WriteLine("[22] bs_ts_field " + Array.BinarySearch(tss, SfTs));
        Console.WriteLine("[23] bs_ts_elem " + Array.BinarySearch(tss, tss[1]));
        Console.WriteLine("[24] bs_ts_return " + Array.BinarySearch(tss, GetTs()));

        // [25..30] empty / single / kind-only-difference / MinValue / MaxValue.
        var empty = new DateTime[0];
        Console.WriteLine("[25] empty_ix " + Array.IndexOf(empty, new DateTime(2019, 3, 4)) + "," + Array.BinarySearch(empty, new DateTime(2019, 3, 4)));
        var single = new[] { new DateTime(2020, 1, 2) };
        Console.WriteLine("[26] single_ix " + Array.IndexOf(single, new DateTime(2020, 1, 2)) + "," + Array.IndexOf(single, new DateTime(2020, 1, 3)));
        Console.WriteLine("[27] single_bs " + Array.BinarySearch(single, new DateTime(2020, 1, 2)) + "," + Array.BinarySearch(single, new DateTime(2020, 1, 3)));
        var kindOnly = new[] { new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Local) };
        Console.WriteLine("[28] kind_ignored " + Array.IndexOf(kindOnly, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
        var bounds = new[] { DateTime.MinValue, DateTime.MaxValue };
        Console.WriteLine("[29] minmax_ix " + Array.IndexOf(bounds, DateTime.MinValue) + "," + Array.IndexOf(bounds, DateTime.MaxValue));
        Console.WriteLine("[30] minmax_bs " + Array.BinarySearch(bounds, DateTime.MinValue) + "," + Array.BinarySearch(bounds, DateTime.MaxValue));

        // [31..33] BinarySearch complements: middle insertion -> -2, tail -> -3.
        var sorted = new[] { new DateTime(2010, 1, 1), new DateTime(2012, 1, 1), new DateTime(2014, 1, 1) };
        Console.WriteLine("[31] bs_mid_complement " + Array.BinarySearch(sorted, new DateTime(2011, 1, 1)));
        Console.WriteLine("[32] bs_tail_complement " + Array.BinarySearch(sorted, new DateTime(2020, 1, 1)));
        Console.WriteLine("[33] bs_head_complement " + Array.BinarySearch(sorted, new DateTime(2000, 1, 1)));

        // [34..37] range forms: index+length == Length is legal, index == Length
        // with length 0 is legal, out-of-range index throws.
        Console.WriteLine("[34] ix_range_full " + T(() => { Console.Write(Array.IndexOf(dts, new DateTime(2021, 5, 6), 0, 3) + " "); }));
        Console.WriteLine("[35] ix_range_zero " + T(() => { Console.Write(Array.IndexOf(dts, new DateTime(2021, 5, 6), 3, 0) + " "); }));
        Console.WriteLine("[36] ix_range_oob " + T(() => { Console.Write(Array.IndexOf(dts, new DateTime(2021, 5, 6), 4, 0) + " "); }));
        Console.WriteLine("[37] bs_range_full " + T(() => { Console.Write(Array.BinarySearch(dts, 0, 3, new DateTime(2020, 1, 2)) + " "); }));

        // [38..40] sort keeps Kind (ordering is by ticks; the kind bits survive).
        var mix = new[] { new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2021, 1, 1) };
        Array.Sort((Array)mix);
        Console.WriteLine("[38] sort_years " + mix[0].Year + "," + mix[1].Year + "," + mix[2].Year);
        Console.WriteLine("[39] sort_kinds " + (int)mix[0].Kind + "," + (int)mix[1].Kind + "," + (int)mix[2].Kind);
        Console.WriteLine("[40] sort_ticks_ok " + (mix[0].Ticks < mix[1].Ticks) + "," + (mix[1].Ticks < mix[2].Ticks));

        // [41..42] TimeSpan negative / zero boundaries through the generic APIs.
        var tsb = new[] { TimeSpan.FromSeconds(-3), TimeSpan.Zero, TimeSpan.FromDays(1) };
        Console.WriteLine("[41] ts_bounds_ix " + Array.IndexOf(tsb, TimeSpan.Zero) + "," + Array.IndexOf(tsb, TimeSpan.FromSeconds(-3)));
        Console.WriteLine("[42] ts_bounds_bs " + Array.BinarySearch(tsb, TimeSpan.FromDays(1)) + "," + Array.BinarySearch(tsb, TimeSpan.FromSeconds(-1)));
    }
}
