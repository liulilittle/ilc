using System;
using System.Collections.Generic;
using System.Linq;
public static class P
{
    public static void Main()
    {
        List<double> dl = new List<double> { 1.5, 2.5 };
        Console.WriteLine("[d-except]=" + string.Join(",", dl.Except(new double[] { })));
        Console.WriteLine("[d-distinct]=" + string.Join(",", dl.Distinct()));
        Console.WriteLine("[d-toarray]=" + string.Join(",", dl.ToArray()));
        List<float> fl = new List<float> { 1.5f, 2.5f };
        Console.WriteLine("[f-except]=" + string.Join(",", fl.Except(new float[] { })));
        List<long> ll = new List<long> { 3000000000L, 2L };
        Console.WriteLine("[l-except]=" + string.Join(",", ll.Except(new long[] { })));
        List<int> il = new List<int> { 1, 2 };
        Console.WriteLine("[i-except]=" + string.Join(",", il.Except(new int[] { })));
        List<string> sl = new List<string> { "a", "b" };
        Console.WriteLine("[r-except]=" + string.Join(",", sl.Except(new string[] { })));
    }
}