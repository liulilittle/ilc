using System;
using System.Linq;

static class P
{
    static void Main()
    {
        var a = Enumerable.Repeat(7, 3).ToArray();
        Record("int", string.Join(",", a));

        var b = Enumerable.Repeat("x", 2).ToArray();
        Record("str", string.Join(",", b));

        var c = Enumerable.Repeat(9, 0).ToArray();
        Record("zero", "n=" + c.Length);

        var d = Enumerable.Empty<int>().ToArray();
        Record("emptyint", "n=" + d.Length);

        var e = Enumerable.Empty<string>().ToArray();
        Record("emptystr", "n=" + e.Length);

        try
        {
            Enumerable.Repeat(1, -1);
            Record("negcount", "no-throw");
        }
        catch (Exception ex)
        {
            Record("negcount", ex.GetType().Name + ":" + ex.Message);
        }

        var f = Enumerable.Repeat(2, 2)
            .SelectMany(x => Enumerable.Range(x, 2))
            .ToArray();
        Record("sm", string.Join(",", f));

        var g = Enumerable.Repeat(new[] { 1, 2 }, 2)
            .SelectMany(x => x)
            .ToArray();
        Record("multi", string.Join(",", g));

        var h = Enumerable.Repeat(5, 3).ToArray();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Record("gc", string.Join(",", h));
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}