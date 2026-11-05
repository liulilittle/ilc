using System;
using System.Collections.Generic;
using System.Linq;

sealed class LowCase : IEqualityComparer<string>
{
    public bool Equals(string? a, string? b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(string s)
    {
        return (s ?? "").ToLowerInvariant().GetHashCode();
    }
}

static class P
{
    static string GJ(string x, IEnumerable<string> g)
    {
        if (x == "B")
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return x + ":" + string.Join(".", g);
    }

    static void Main()
    {
        var r1 = new[] { "a", "B", "c", "d" }
            .GroupJoin(new[] { "A", "b", "C", "x" }, s => s, s => s, GJ, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Record("ic", string.Join(",", r1));

        var r2 = new[] { "a", "B", "c" }
            .GroupJoin(new[] { "A", "b", "C" }, s => s, s => s, (x, g) => x + "|" + g.Count(), new LowCase())
            .ToArray();
        Record("cust", string.Join(",", r2));

        var r3 = new[] { "A" }.GroupJoin(new[] { "a", "A" }, s => s, s => s, (x, g) => x + "." + string.Join(".", g), StringComparer.OrdinalIgnoreCase).ToArray();
        Record("dup", string.Join(",", r3));

        var r4 = new[] { "q" }.GroupJoin(new[] { "r" }, s => s, s => s, (x, g) => x + ":" + g.Count(), StringComparer.OrdinalIgnoreCase).ToArray();
        Record("empty", string.Join(",", r4));

        Record("count", r1.Length + "," + r2.Length + "," + r3.Length);
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}