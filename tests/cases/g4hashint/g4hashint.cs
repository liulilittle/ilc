using System;
using System.Collections.Generic;

// g4hashint -- G4 HashSet<int> e2e case (GREEN since the int-member
// expansion landed: boxed-int members over the non-generic hashtable +
// keycollection-style enumeration). Enumeration output is sorted for
// determinism.
class Prog
{
    static void Main()
    {
        HashSet<int> hs = new HashSet<int>();
        Console.WriteLine("add3=" + hs.Add(3));
        Console.WriteLine("add1=" + hs.Add(1));
        Console.WriteLine("add2=" + hs.Add(2));
        Console.WriteLine("add2dup=" + hs.Add(2));
        Console.WriteLine("count=" + hs.Count);

        Console.WriteLine("has2=" + hs.Contains(2));
        Console.WriteLine("has9=" + hs.Contains(9));

        Console.WriteLine("remove2=" + hs.Remove(2));
        Console.WriteLine("remove9=" + hs.Remove(9));
        Console.WriteLine("count2=" + hs.Count);

        int[] arr = new int[hs.Count];
        int n = 0;
        foreach (int v in hs)
            arr[n++] = v;
        Array.Sort(arr);
        Console.WriteLine("arrlen=" + arr.Length);
        for (int i = 0; i < arr.Length; i++)
            Console.WriteLine("item=" + arr[i]);

        Console.WriteLine("done");
    }
}
