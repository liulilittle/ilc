using System;
using System.Collections.Generic;
using System.Linq;

static class P
{
    static string GJ(int x, IEnumerable<int> g2)
    {
        if (x == 2)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return x + ":" + string.Join(".", g2);
    }

    static void Main()
    {
        var r1 = new[] { 1, 2, 3 }.GroupJoin(new[] { 1, 1, 2, 2, 2 }, x => x, y => y, GJ).ToArray();
        Record("i", string.Join(",", r1));

        var r2 = new[] { "a", "b", "c" }.GroupJoin(new[] { "b", "b", "a" }, s => s, s => s, (x, g2) => x + "|" + g2.Count()).ToArray();
        Record("s", string.Join(",", r2));

        var r3 = new[] { 10, 20 }.GroupJoin(new[] { 5 }, x => x, y => y, (x, g2) => x * 100 + g2.Count()).ToArray();
        Record("ii", string.Join(",", r3));

        var r4 = new[] { 7, 8 }.GroupJoin(new int[0], x => x, y => y, (x, g2) => x + ":" + g2.Count()).ToArray();
        Record("ei", string.Join(",", r4));

        var r5 = new int[0].GroupJoin(new[] { 1 }, x => x, y => y, (x, g2) => x + ":" + g2.Count()).ToArray();
        Record("eo", r5.Length);

        var r6 = new[] { 1 }.GroupJoin(new[] { 11, 12, 13 }, x => x, y => y / 10, (x, g2) => x + ":" + string.Join(".", g2)).ToArray();
        Record("o", string.Join(",", r6));

        Record("count", r1.Length + "," + r2.Length + "," + r3.Length);
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}