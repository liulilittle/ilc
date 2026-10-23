using System;
using System.Collections.Generic;

class Prog
{
    static void Main()
    {
        HashSet<int> a = new HashSet<int>(new int[] { 1, 2, 3, 4, 5 });
        Console.Error.WriteLine("STEP1");
        HashSet<int> u = new HashSet<int>(a);
        Console.Error.WriteLine("STEP2 count=" + u.Count);
        u.UnionWith(new int[] { 6, 7 });
        Console.Error.WriteLine("STEP3 count=" + u.Count);
        HashSet<int> i = new HashSet<int>(a);
        i.IntersectWith(new int[] { 4, 5, 6 });
        Console.Error.WriteLine("STEP4 count=" + i.Count);
        Console.WriteLine("done=" + u.Count + "/" + i.Count);
    }
}
