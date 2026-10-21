using System;

// arraydtkind -- DateTime[]/TimeSpan[] element-kind consumers.
//   * Array.IndexOf<DateTime>/BinarySearch<DateTime> compare TICKS only
//     (DateTimeKind is not part of ordering/equality, net48 parity);
//   * String.Join over a DateTime[]/TimeSpan[] formats each element (not the
//     raw packed int64 image);
//   * Array.Sort(keys, items, index, length) requires items.Length >=
//     index + length, not items.Length >= keys.Length;
//   * mixed DateTimeKind values never let the Kind bits dominate the order.
//
// Discrimination notes (audit F): every generic call is exercised with a
// LOCAL / PARAMETER / STATIC-FIELD / ARRAY-ELEMENT / RETURN-VALUE needle as
// well as an inline ctor, so a needle-shape-dependent mapping gap is caught.
// Empty, single-element, index+length == Length and out-of-range forms are
// covered; DateTime.MinValue/MaxValue pin the tick boundary; sorts assert
// .Kind per element; BinarySearch asserts the -2 (middle) and -3/-4 (tail)
// complements so a "miss always -1" implementation cannot pass.
//
// GAPS recorded here (not fixed in this batch):
//   * the NON-generic Array.IndexOf(Array, object)/BinarySearch(Array, object)
//     overloads over DateTime[]/TimeSpan[] stay LOUD -- this runtime has no
//     DateTime/TimeSpan box helper, so a boxed comparand cannot exist. Needed
//     to close the gap: `Il2CppObject* icall_box_datetime(Il2CppDateTime)`
//     and `Il2CppObject* icall_box_timespan(Il2CppTimeSpan)` plus registered
//     "System.DateTime"/"System.TimeSpan" box classes, a CodeGenerator box
//     dispatch arm for both types, and the element-kind 6/7 comparison arms
//     in AnyElementEquals/CompareAnyElement (removed here as unreachable).
class Q
{
    static readonly DateTime SfDt = new DateTime(2019, 3, 4);
    static readonly TimeSpan SfTs = TimeSpan.FromSeconds(3);

    static string T(Action a)
    {
        try { a(); return "ok"; }
        catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    static int IxDt(DateTime[] a, DateTime n) { return Array.IndexOf(a, n); }
    static int BsDt(DateTime[] a, DateTime n) { return Array.BinarySearch(a, n); }
    static int IxTs(TimeSpan[] a, TimeSpan n) { return Array.IndexOf(a, n); }
    static int BsTs(TimeSpan[] a, TimeSpan n) { return Array.BinarySearch(a, n); }

    static DateTime GetDt() { return new DateTime(2019, 3, 4); }
    static TimeSpan GetTs() { return TimeSpan.FromSeconds(3); }

    static void Main()
    {
        var dt = new[] { new DateTime(2019, 3, 4), new DateTime(2020, 1, 2), new DateTime(2021, 5, 6) };
        var ts = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7) };

        // [1] inline ctor / [2] local / [3] parameter / [4] static field
        //     / [5] array element / [6] return value -- IndexOf<DateTime>.
        Console.WriteLine("[1] ix_dt_inline " + Array.IndexOf(dt, new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc)) + "," + Array.IndexOf(dt, new DateTime(2021, 1, 1)));
        var n1 = new DateTime(2020, 1, 2);
        Console.WriteLine("[2] ix_dt_local " + Array.IndexOf(dt, n1));
        Console.WriteLine("[3] ix_dt_param " + IxDt(dt, new DateTime(2021, 5, 6)));
        Console.WriteLine("[4] ix_dt_field " + Array.IndexOf(dt, SfDt));
        Console.WriteLine("[5] ix_dt_elem " + Array.IndexOf(dt, dt[1]));
        Console.WriteLine("[6] ix_dt_return " + Array.IndexOf(dt, GetDt()));

        // [7..12] the same six needle shapes for BinarySearch<DateTime>.
        Console.WriteLine("[7] bs_dt_inline " + Array.BinarySearch(dt, new DateTime(2021, 5, 6)));
        var n2 = new DateTime(2019, 3, 4);
        Console.WriteLine("[8] bs_dt_local " + Array.BinarySearch(dt, n2));
        Console.WriteLine("[9] bs_dt_param " + BsDt(dt, new DateTime(2020, 1, 2)));
        Console.WriteLine("[10] bs_dt_field " + Array.BinarySearch(dt, SfDt));
        Console.WriteLine("[11] bs_dt_elem " + Array.BinarySearch(dt, dt[0]));
        Console.WriteLine("[12] bs_dt_return " + Array.BinarySearch(dt, GetDt()));

        // [13..18] IndexOf<TimeSpan> across the six shapes.
        Console.WriteLine("[13] ix_ts_inline " + Array.IndexOf(ts, TimeSpan.FromSeconds(3)));
        var t1 = TimeSpan.FromSeconds(7);
        Console.WriteLine("[14] ix_ts_local " + Array.IndexOf(ts, t1));
        Console.WriteLine("[15] ix_ts_param " + IxTs(ts, TimeSpan.FromSeconds(1)));
        Console.WriteLine("[16] ix_ts_field " + Array.IndexOf(ts, SfTs));
        Console.WriteLine("[17] ix_ts_elem " + Array.IndexOf(ts, ts[2]));
        Console.WriteLine("[18] ix_ts_return " + Array.IndexOf(ts, GetTs()));

        // [19..24] BinarySearch<TimeSpan> across the six shapes.
        Console.WriteLine("[19] bs_ts_inline " + Array.BinarySearch(ts, TimeSpan.FromSeconds(1)));
        var t2 = TimeSpan.FromSeconds(3);
        Console.WriteLine("[20] bs_ts_local " + Array.BinarySearch(ts, t2));
        Console.WriteLine("[21] bs_ts_param " + BsTs(ts, TimeSpan.FromSeconds(7)));
        Console.WriteLine("[22] bs_ts_field " + Array.BinarySearch(ts, SfTs));
        Console.WriteLine("[23] bs_ts_elem " + Array.BinarySearch(ts, ts[1]));
        Console.WriteLine("[24] bs_ts_return " + Array.BinarySearch(ts, GetTs()));

        // [25..30] empty / single / Kind-only difference / MinValue / MaxValue.
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

        // [31..33] discriminating complements: middle -> -2, tail -> -4, head -> -1.
        var sorted = new[] { new DateTime(2010, 1, 1), new DateTime(2012, 1, 1), new DateTime(2014, 1, 1) };
        Console.WriteLine("[31] bs_mid_complement " + Array.BinarySearch(sorted, new DateTime(2011, 1, 1)));
        Console.WriteLine("[32] bs_tail_complement " + Array.BinarySearch(sorted, new DateTime(2020, 1, 1)));
        Console.WriteLine("[33] bs_head_complement " + Array.BinarySearch(sorted, new DateTime(2000, 1, 1)));

        // [34..37] range forms: index+length == Length is legal, index == Length
        // with length 0 is legal, an out-of-range index throws.
        Console.WriteLine("[34] ix_range_full " + T(() => { Console.Write(Array.IndexOf(dt, new DateTime(2021, 5, 6), 0, 3) + " "); }));
        Console.WriteLine("[35] ix_range_zero " + T(() => { Console.Write(Array.IndexOf(dt, new DateTime(2021, 5, 6), 3, 0) + " "); }));
        Console.WriteLine("[36] ix_range_oob " + T(() => { Console.Write(Array.IndexOf(dt, new DateTime(2021, 5, 6), 4, 0) + " "); }));
        Console.WriteLine("[37] bs_range_full " + T(() => { Console.Write(Array.BinarySearch(dt, 0, 3, new DateTime(2020, 1, 2)) + " "); }));

        // [38..40] sorting keeps Kind per element (ordering is by ticks only).
        var mix = new[] { new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2021, 1, 1) };
        Array.Sort((Array)mix);
        Console.WriteLine("[38] sort_years " + mix[0].Year + "," + mix[1].Year + "," + mix[2].Year);
        Console.WriteLine("[39] sort_kinds " + (int)mix[0].Kind + "," + (int)mix[1].Kind + "," + (int)mix[2].Kind);
        Console.WriteLine("[40] sort_ticks_ok " + (mix[0].Ticks < mix[1].Ticks) + "," + (mix[1].Ticks < mix[2].Ticks));

        // [41..42] TimeSpan boundaries through the generic APIs (negative/zero).
        var tsb = new[] { TimeSpan.FromSeconds(-3), TimeSpan.Zero, TimeSpan.FromDays(1) };
        Console.WriteLine("[41] ts_bounds_ix " + Array.IndexOf(tsb, TimeSpan.Zero) + "," + Array.IndexOf(tsb, TimeSpan.FromSeconds(-3)));
        Console.WriteLine("[42] ts_bounds_bs " + Array.BinarySearch(tsb, TimeSpan.FromDays(1)) + "," + Array.BinarySearch(tsb, TimeSpan.FromSeconds(-1)));

        // [43..45] String.Join over the same arrays (formatted, not raw bits).
        Console.WriteLine("[43] join_dt " + string.Join("|", dt));
        Console.WriteLine("[44] join_ts " + string.Join("|", ts));
        Console.WriteLine("[45] join_empty " + "[" + string.Join("|", empty) + "]");

        // [46..48] keys/items range: the items length contract and the partial
        // sort contents (index+length == keys.Length, items shorter than keys).
        var keys5 = new[] { 3, 1, 5, 4, 2 };
        var items2 = new[] { "b", "a" };
        Console.WriteLine("[46] keys_items_range " + T(() =>
        {
            Array.Sort(keys5, items2, 0, 2);
            Console.Write(keys5[0] + "," + keys5[1] + " " + items2[0] + items2[1] + " ");
        }));
        var keys3 = new[] { 9, 8, 7 };
        var items3 = new[] { "x", "y", "z" };
        Console.WriteLine("[47] keys_items_partial " + T(() =>
        {
            Array.Sort(keys3, items3, 1, 2);
            Console.Write(keys3[0] + "," + keys3[1] + "," + keys3[2] + " " + items3[0] + items3[1] + items3[2] + " ");
        }));
        var keysShort = new[] { 5, 6 };
        var itemsShort = new[] { "p" };
        Console.WriteLine("[48] keys_items_too_short " + T(() => { Array.Sort(keysShort, itemsShort, 0, 2); Console.Write("ok "); }));
    }
}
