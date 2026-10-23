using System;
using System.Collections.Generic;

class Prog
{
    static void Main()
    {
        HashSet<int> a = new HashSet<int>(new int[] { 1, 2, 3, 4, 5 });
        HashSet<int> b = new HashSet<int>(new int[] { 4, 5, 6, 7 });
        Console.WriteLine("A=" + a.Count);
        HashSet<int> u = new HashSet<int>(a);
        Console.WriteLine("B=" + u.Count);
        u.UnionWith(b);
        Console.WriteLine("C=" + u.Count);
        HashSet<int> i = new HashSet<int>(a);
        i.IntersectWith(b);
        Console.WriteLine("D=" + i.Count);
        HashSet<int> e = new HashSet<int>(a);
        e.ExceptWith(b);
        Console.WriteLine("E=" + e.Count);
        HashSet<int> x = new HashSet<int>(a);
        x.SymmetricExceptWith(b);
        Console.WriteLine("F=" + x.Count);
        Console.WriteLine("G=" + a.SetEquals(new int[] { 5, 4, 3, 2, 1 }));
        Console.WriteLine("H=" + a.Overlaps(b));
    }
}
