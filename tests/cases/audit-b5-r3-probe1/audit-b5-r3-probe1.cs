using System;
using System.Collections.Generic;
using System.Linq;
public static class P
{
    public static void Main()
    {
        long[] la = { 3, 1, 2 };
        Console.WriteLine("[l-where]=" + string.Join(",", la.Where(x => x > 1)));
        double[] da = { 1.5, 2.5, 3.5 };
        Console.WriteLine("[d-where]=" + string.Join(",", da.Where(x => x > 2.0)));
        float[] fa = { 1.5f, 2.5f };
        Console.WriteLine("[f-where]=" + string.Join(",", fa.Where(x => x > 1.0f)));
        Console.WriteLine("[l-first]=" + la.First());
        Console.WriteLine("[d-last]=" + da.Last());
        Console.WriteLine("[l-select]=" + string.Join(",", la.Select(x => x * 2)));
        List<double> dl = new List<double> { 1.5, 2.5 };
        Console.WriteLine("[d-except]=" + string.Join(",", dl.Except(new double[] { })));
        Console.WriteLine("[d-distinct]=" + string.Join(",", dl.Distinct()));
    }
}