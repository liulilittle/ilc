using System;
using System.Collections.Generic;
using System.Linq;

static class P
{
    static void Main()
    {
        var lk = new[] { 3, 1, 2, 3 }.ToLookup(x => x);
        var g1 = lk[1];   // {1}  -- exactly one element
        var g3 = lk[3];   // {3, 3} -- two elements

        Console.WriteLine("[single1] " + g1.Single());
        try { Console.WriteLine("[single2] " + g3.Single()); }
        catch (InvalidOperationException) { Console.WriteLine("[single2] threw"); }
        Console.WriteLine("[singleord] " + g1.SingleOrDefault());

        Console.WriteLine("[agg] " + g3.Aggregate((a, b) => a + b));
        Console.WriteLine("[agg2] " + g1.Aggregate(10, (a, b) => a + b));

        var gb1 = new[] { 6, 5, 5 }.GroupBy(x => x);
        Console.WriteLine("[gbsingle] " + gb1.First().Single());
        var gb2 = new[] { 5, 6, 5 }.GroupBy(x => x);
        Console.WriteLine("[gbagg] " + gb2.First().Aggregate((a, b) => a * b));
    }
}
