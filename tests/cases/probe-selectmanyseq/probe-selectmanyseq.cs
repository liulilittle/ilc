using System;
using System.Collections.Generic;
using System.Linq;

static class P
{
    static IEnumerable<string> InnerSeq(int n)
    {
        if (n == 2)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        for (int i = 0; i < n; ++i)
        {
            yield return "s" + (i + 1);
        }
    }

    static void Main()
    {
        var r1 = new[] { 2, 1, 0 }
            .SelectMany(x => InnerSeq(x))
            .ToArray();
        Record("yield", string.Join(",", r1));

        var r2 = new[] { 1, 2, 0 }
            .SelectMany(x => Wrap(x))
            .ToArray();
        Record("wrap", string.Join(",", r2));

        var r3 = new[] { 2, 1 }.SelectMany(x => new List<int> { x, x * 10 }).ToArray();
        Record("list", string.Join(",", r3));

        var r4 = new[] { "ab", "c" }.SelectMany(x => x.ToCharArray()).ToArray();
        Record("string", string.Join(",", r4));

        var r5 = new[] { 1, 2, 3 }.SelectMany(x => new int[] { x, -x }).ToArray();
        Record("array", string.Join(",", r5));

        var r6 = new[] { 3.5, 1.0 }.SelectMany(x => Doubles(x)).ToArray();
        Record("double", string.Join(",", r6));

        var r7 = new[] { 1, 2 }
            .SelectMany(x => new long[] { (long)x << 40, x })
            .ToArray();
        Record("long", string.Join(",", r7));

        var r8 = new[] { 2, 1 }
            .SelectMany(x => InnerSeq(x), (y, s) => y + ":" + s)
            .ToArray();
        Record("many3", string.Join(",", r8));

        var r9 = new[] { 1, 3 }
            .SelectMany(x => new List<int> { x, x * 10 }, (a, b) => a * 100 + b)
            .ToArray();
        Record("many3list", string.Join(",", r9));

        Record("count", r1.Length + "," + r2.Length + "," + r3.Length);
    }

    static IEnumerable<int> Wrap(int v)
    {
        yield return v;
        yield return v + 100;
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }

    static IEnumerable<double> Doubles(double v)
    {
        yield return v;
        yield return v + 1.0;
    }
}