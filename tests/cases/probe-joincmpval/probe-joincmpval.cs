using System;
using System.Collections.Generic;
using System.Linq;

sealed class Parity : IEqualityComparer<int>
{
    public bool Equals(int a, int b)
    {
        return (a & 1) == (b & 1);
    }

    public int GetHashCode(int x)
    {
        return x & 1;
    }
}

sealed class AbsDiff : IEqualityComparer<long>
{
    private static long F(long x)
    {
        return (x ^ (x >> 32)) % 4;
    }

    public bool Equals(long a, long b)
    {
        return F(a) == F(b);
    }

    public int GetHashCode(long x)
    {
        return (int)F(x);
    }
}

sealed class Halves : IEqualityComparer<double>
{
    public bool Equals(double a, double b)
    {
        return System.Math.Floor(a) == System.Math.Floor(b);
    }

    public int GetHashCode(double x)
    {
        return (int)System.Math.Floor(x);
    }
}

static class P
{
    static string R(string x, string y)
    {
        if (x == "BB")
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return x + "<" + y;
    }

    static void Main()
    {
        var r1 = new[] { "A", "BB", "C", "D" }
            .Join(new[] { "a", "bb", "x" }, s => s.Length, s => s.Length, R, new Parity())
            .ToArray();
        Record("int", string.Join(",", r1));

        var r2 = new[] { 4294967301L, 1L, 4294967296L }
            .Join(new[] { 4294967305L, 8589934596L, 4294967300L, 5L }, x => x, x => x, (a, b) => a + ":" + b, new AbsDiff())
            .ToArray();
        Record("long", string.Join(",", r2));

        var r3 = new[] { 1.5, 2.5 }
            .Join(new[] { 1.9, 9.9, 2.1 }, x => x, x => x, (a, b) => a + "|" + b, new Halves())
            .ToArray();
        Record("double", string.Join(",", r3));

        var r4 = new[] { "A", "B", "CC", "DDD" }
            .GroupJoin(new[] { "x", "yy", "zzz", "w" }, s => s.Length, s => s.Length,
                       (x, g) => x + ":" + string.Join(".", g), new Parity())
            .ToArray();
        Record("gj", string.Join(",", r4));

        var r5 = new[] { "M", "N" }
            .Join(new[] { "m", "n", "m2" }, s => s.Length, s => s.Length,
                  (a, b) => a + "." + b, null)
            .ToArray();
        Record("nullcmp", string.Join(",", r5));

        var r6 = new[] { 1, 3, 4 }
            .Join(new[] { 1, 3, 4, 5 }, x => x, x => x, (a, b) => a + "~" + b, new Parity())
            .ToArray();
        Record("oddeven", string.Join(",", r6));

        Record("count", r1.Length + "," + r2.Length + "," + r3.Length);
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}