using System;

// arraydtkind -- DateTime[]/TimeSpan[] element-kind consumers:
//   * Array.IndexOf<DateTime>/BinarySearch<DateTime> compare TICKS only
//     (DateTimeKind is not part of ordering/equality, net48 parity);
//   * String.Join over a DateTime[] formats each element (not the raw
//     packed int64 image);
//   * Array.Sort(keys, items, index, length) requires items.Length >=
//     index + length, not items.Length >= keys.Length;
//   * mixed DateTimeKind values never let the Kind bits dominate the order.
class Q
{
    static string T(Action a)
    {
        try { a(); return "ok"; }
        catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    static void Main()
    {
        var dt = new[] { new DateTime(2020, 1, 2), new DateTime(2019, 3, 4) };
        Console.WriteLine("[1] indexof " + Array.IndexOf(dt, new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc)) + "," + Array.IndexOf(dt, new DateTime(2021, 1, 1)));

        var sorted = new[] { new DateTime(2019, 3, 4), new DateTime(2020, 1, 2) };
        Console.WriteLine("[2] binsearch " + Array.BinarySearch(sorted, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)) + "," + Array.BinarySearch(sorted, new DateTime(2018, 1, 1)));

        Console.WriteLine("[3] join " + string.Join("|", dt));
        Console.WriteLine("[4] join_ts " + string.Join("|", new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1) }));

        var keys5 = new[] { 3, 1, 5, 4, 2 };
        var items2 = new[] { "b", "a" };
        Console.WriteLine("[5] keys_items_range " + T(() =>
        {
            Array.Sort(keys5, items2, 0, 2);
            Console.Write(keys5[0] + "," + keys5[1] + " " + items2[0] + items2[1] + " ");
        }));

        var rev = new[] { new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2021, 1, 1) };
        Console.WriteLine("[6] kind_vs_ticks " + T(() =>
        {
            Array.Sort((Array)rev);
            Console.Write(rev[0].Year + "," + rev[1].Year + " ");
        }));

        var mix = new[] { new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1) };
        Console.WriteLine("[7] mixed_sort " + T(() =>
        {
            Array.Sort((Array)mix);
            Console.Write(mix[0].Year + "," + mix[1].Year + " ");
        }));
    }
}
