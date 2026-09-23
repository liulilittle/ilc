using System;
using System.Collections.Generic;

// g4sortedsetval -- T2 W2 probe: SortedSet<string/long/double> + empty Min/Max.
// net48 oracle byte-exact; enumeration is ascending (deterministic).
class Prog
{
    static void Main()
    {
        SortedSet<string> ss = new SortedSet<string>();
        Console.WriteLine("sadd_b=" + ss.Add("b"));
        Console.WriteLine("sadd_a=" + ss.Add("a"));
        Console.WriteLine("sadd_c=" + ss.Add("c"));
        Console.WriteLine("sadd_adup=" + ss.Add("a"));
        Console.WriteLine("scount=" + ss.Count);
        Console.WriteLine("smin=" + ss.Min);
        Console.WriteLine("smax=" + ss.Max);
        Console.WriteLine("shas_a=" + ss.Contains("a"));
        Console.WriteLine("shas_z=" + ss.Contains("z"));
        Console.WriteLine("srem_a=" + ss.Remove("a"));
        Console.WriteLine("scount2=" + ss.Count);
        foreach (string v in ss)
            Console.WriteLine("sitem=" + v);

        SortedSet<long> sl = new SortedSet<long>();
        Console.WriteLine("ladd3=" + sl.Add(3L));
        Console.WriteLine("ladd1=" + sl.Add(1L));
        Console.WriteLine("ladd2=" + sl.Add(2L));
        Console.WriteLine("ladd2dup=" + sl.Add(2L));
        Console.WriteLine("lcount=" + sl.Count);
        Console.WriteLine("lmin=" + sl.Min);
        Console.WriteLine("lmax=" + sl.Max);
        foreach (long v in sl)
            Console.WriteLine("litem=" + v);

        SortedSet<double> sd = new SortedSet<double>();
        Console.WriteLine("dadd2=" + sd.Add(2.5));
        Console.WriteLine("dadd1=" + sd.Add(1.5));
        Console.WriteLine("dadd1dup=" + sd.Add(1.5));
        Console.WriteLine("dcount=" + sd.Count);
        Console.WriteLine("dmin=" + sd.Min);
        Console.WriteLine("dmax=" + sd.Max);
        foreach (double v in sd)
            Console.WriteLine("ditem=" + v);

        SortedSet<int> se = new SortedSet<int>();
        Console.WriteLine("empty_min=" + se.Min);
        Console.WriteLine("empty_max=" + se.Max);

        Console.WriteLine("done");
    }
}
