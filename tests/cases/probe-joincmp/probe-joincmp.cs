using System;
using System.Collections.Generic;
using System.Linq;

sealed class NoCase : IEqualityComparer<string>
{
    public bool Equals(string? a, string? b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(string s)
    {
        return (s ?? "").ToUpperInvariant().GetHashCode();
    }
}

static class P
{
    static string Res(string x, string y)
    {
        if (x == "b")
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return x + ":" + y;
    }

    static void Main()
    {
        var r1 = new[] { "a", "B", "c", "d" }
            .Join(new[] { "A", "b", "C", "x" }, s => s, s => s, Res, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Record("ic", string.Join(",", r1));

        var r2 = new[] { "a", "B", "c" }
            .Join(new[] { "A", "b", "C" }, s => s, s => s, (x, y) => x + "|" + y, new NoCase())
            .ToArray();
        Record("cust", string.Join(",", r2));

        var r3 = new[] { "p", "q" }.Join(new[] { "Q", "r" }, s => s, s => s, (x, y) => x + "-" + y, StringComparer.OrdinalIgnoreCase).ToArray();
        Record("n", string.Join(",", r3));

        var r4 = new[] { "A" }.Join(new[] { "a", "a" }, s => s, s => s, (x, y) => x + "." + y, StringComparer.OrdinalIgnoreCase).ToArray();
        Record("dup", string.Join(",", r4));

        Record("count", r1.Length + "," + r2.Length + "," + r3.Length);
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}