using System;

// g4arrval -- W7 probe: Array.Sort/IndexOf/BinarySearch/LastIndexOf/Empty/
// Reverse over long[] (+ LastIndexOf over string[]). Only emitter-supported
// newarr kinds (audit C4: double[]/float[] newarr is LOUD-rejected by
// design; owned by A4/main). Deterministic.
class Prog
{
    static void Main()
    {
        long[] l = new long[] { 30L, 10L, 20L, 10L };
        Array.Sort(l);
        Console.WriteLine("lsorted=" + l[0] + "," + l[1] + "," + l[2] + "," + l[3]);
        Console.WriteLine("lindexof10=" + Array.IndexOf(l, 10L));
        Console.WriteLine("llast10=" + Array.LastIndexOf(l, 10L));
        Console.WriteLine("lbsearch20=" + Array.BinarySearch(l, 20L));
        Console.WriteLine("lbsearchmiss=" + Array.BinarySearch(l, 15L));
        Array.Reverse(l);
        Console.WriteLine("lreversed0=" + l[0]);
        long[] le = Array.Empty<long>();
        Console.WriteLine("lempty=" + le.Length);

        string[] s = new string[] { "b", "a", "b" };
        Console.WriteLine("slastb=" + Array.LastIndexOf(s, "b"));
        Console.WriteLine("slasta=" + Array.LastIndexOf(s, "a"));
        Console.WriteLine("slastmiss=" + Array.LastIndexOf(s, "z"));

        Console.WriteLine("done");
    }
}
