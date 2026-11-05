using System;
using System.Linq;

static class P
{
    static void Main()
    {
        var a = Enumerable.Range(2, 3).ToArray();
        Record("basic", string.Join(",", a));

        var b = Enumerable.Range(5, 0).ToArray();
        Record("empty", "n=" + b.Length);

        var c = Enumerable.Range(-2, 4).ToArray();
        Record("neg", string.Join(",", c));

        var d = Enumerable.Range(1, 3)
            .SelectMany(x => Enumerable.Range(x, 2))
            .ToArray();
        Record("selectmany", string.Join(",", d));

        try
        {
            Enumerable.Range(1, -1);
            Record("negcount", "no-throw");
        }
        catch (Exception ex)
        {
            Record("negcount", ex.GetType().Name + ":" + ex.Message);
        }

        try
        {
            Enumerable.Range(int.MaxValue, 2);
            Record("overflow", "no-throw");
        }
        catch (Exception ex)
        {
            Record("overflow", ex.GetType().Name + ":" + ex.Message);
        }

        var h = Enumerable.Range(0, 3).ToArray();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Record("gc", string.Join(",", h));
    }

    static void Record(string id, object v)
    {
        Console.WriteLine("[" + id + "]=" + v);
    }
}