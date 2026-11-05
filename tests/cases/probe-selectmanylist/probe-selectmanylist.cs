using System;
using System.Collections.Generic;
using System.Linq;

static class P
{
    static int selCount;

    static int[] Inner(int x)
    {
        selCount++;
        if (selCount == 2)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return new[] { x, x * 2 };
    }

    static void Main()
    {
        var r1 = new List<int> { 10, 20 }.SelectMany(x => Inner(x)).ToArray();
        Record("int", string.Join(",", r1));

        var r2 = new List<string> { "ab", "c" }.SelectMany(s => new[] { s + "1", s + "2" }).ToArray();
        Record("str", string.Join(",", r2));

        var r3 = new List<long> { 5, 6 }.SelectMany(x => new[] { x, x + 10 }).ToArray();
        Record("long", string.Join(",", r3));

        var r4 = new List<double> { 1.5, 2.5 }.SelectMany(x => new[] { x, x + 0.5 }).ToArray();
        Record("double", string.Join(",", r4));

        var r5 = new List<int>().SelectMany(x => new[] { x }).ToArray();
        Record("empty", r5.Length);

        var r6 = new List<int> { 7, 8 }.SelectMany(x => new int[0]).ToArray();
        Record("emptyinner", r6.Length);

        Record("selcount", selCount);
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}