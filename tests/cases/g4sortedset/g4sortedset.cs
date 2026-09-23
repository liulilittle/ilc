using System;
using System.Collections.Generic;

// g4sortedset -- G4 gap probe: SortedSet<int>. No sorted-set domain exists
// -- expected LOUD/red. Enumeration is ascending (net48), so the output is
// deterministic.
class Prog
{
    static void Main()
    {
        SortedSet<int> ss = new SortedSet<int>();
        Console.WriteLine("add3=" + ss.Add(3));
        Console.WriteLine("add1=" + ss.Add(1));
        Console.WriteLine("add2=" + ss.Add(2));
        Console.WriteLine("add2dup=" + ss.Add(2));
        Console.WriteLine("count=" + ss.Count);
        Console.WriteLine("min=" + ss.Min);
        Console.WriteLine("max=" + ss.Max);
        Console.WriteLine("has2=" + ss.Contains(2));
        Console.WriteLine("has9=" + ss.Contains(9));
        Console.WriteLine("remove2=" + ss.Remove(2));
        Console.WriteLine("count2=" + ss.Count);

        int[] arr = new int[ss.Count];
        int n = 0;
        foreach (int v in ss)
            arr[n++] = v;
        for (int i = 0; i < arr.Length; i++)
            Console.WriteLine("item=" + arr[i]);

        Console.WriteLine("done");
    }
}
