using System;
using System.Linq;

static class P
{
    static int collCount;
    static int resCount;

    static int[] InnI(int x)
    {
        collCount++;
        return new[] { x, x + 1 };
    }

    static string[] InnS(int x)
    {
        collCount++;
        if (collCount == 2)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return new[] { "n" + x, "n" + x + "!" };
    }

    static string JoinI(int x, int y) { resCount++; return x + ":" + y; }

    static string JoinMix(int x, string y) { resCount++; return x + "|" + y; }

    static int JoinII(int x, int y) { resCount++; return x * 100 + y; }

    static string JoinSS(string x, string y) { resCount++; return x + "-" + y; }

    static int[] InnEmpty(int x) { return new int[0]; }

    static void Main()
    {
        var r1 = new[] { 1, 2 }.SelectMany<int, int, string>(x => InnI(x), JoinI).ToArray();
        Record("i", string.Join(",", r1));

        var r2 = new[] { 11, 12 }.SelectMany<int, int, int>(x => InnI(x), JoinII).ToArray();
        Record("ii", string.Join(",", r2));

        var r3 = new[] { 5, 6 }.SelectMany<int, string, string>(x => InnS(x), JoinMix).ToArray();
        Record("mix", string.Join(",", r3));

        var r4 = new[] { "a", "b" }.SelectMany<string, string, string>(s => new[] { s + "1", s + "2" }, JoinSS).ToArray();
        Record("s", string.Join(",", r4));

        var r5 = new int[0].SelectMany<int, int, string>(x => InnI(x), JoinI).ToArray();
        Record("empty", r5.Length);

        var r6 = new[] { 7, 8 }.SelectMany<int, int, string>(x => InnEmpty(x), JoinI).ToArray();
        Record("emptyinner", r6.Length);

        Record("calls", collCount + "," + resCount);
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}