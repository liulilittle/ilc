using System;
using System.Linq;

static class P
{
    static string OK(int x)
    {
        if (x == 3)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return "k" + x;
    }

    static string IK(int x)
    {
        return "k" + x;
    }

    static void Main()
    {
        var r1 = new[] { 1, 2, 3 }.Join(new[] { 0, 1, 1, 2 }, x => x, y => y, (x, y) => x + "-" + y).ToArray();
        Record("i", string.Join(",", r1));

        var r2 = new[] { "a", "b", "c" }.Join(new[] { "a", "x", "b", "a" }, s => s, s => s, (x, y) => x + ":" + y).ToArray();
        Record("s", string.Join(",", r2));

        var r3 = new[] { 1, 2 }.Join(new[] { 1 }, x => x, y => y, (x, y) => x * 1000 + y).ToArray();
        Record("ii", string.Join(",", r3));

        var r4 = new[] { 3, 4 }.Join(new[] { 3 }, x => OK(x), y => IK(y), (x, y) => x + "=" + y).ToArray();
        Record("gc", string.Join(",", r4));

        var r5 = new[] { 1, 2 }.Join(new int[0], x => x, y => y, (x, y) => x + "" + y).ToArray();
        Record("ei", r5.Length);

        var r6 = new int[0].Join(new[] { 1 }, x => x, y => y, (x, y) => x + "" + y).ToArray();
        Record("eo", r6.Length);

        var r7 = new[] { 1, 2 }.Join(new[] { 10, 20, 21 }, x => x, y => y / 10, (x, y) => x + "-" + y).ToArray();
        Record("o", string.Join(",", r7));

        var r8 = new[] { 1.5, 2.5 }.Join(new[] { 2.0, 2.5 }, x => x, y => y, (x, y) => x + "~" + y).ToArray();
        Record("d", string.Join(",", r8));

        Record("count", r1.Length + "," + r2.Length + "," + r3.Length);
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}