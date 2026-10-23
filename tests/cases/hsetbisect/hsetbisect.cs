using System;
using System.Collections.Generic;

class Prog
{
    static void Main()
    {
        Console.WriteLine("A");
        HashSet<int> a = new HashSet<int>(new int[] { 1, 2, 3 });
        Console.WriteLine("B=" + a.Count);
        Console.WriteLine("C=" + a.Contains(2));
        int[] arr = new int[a.Count];
        a.CopyTo(arr);
        Console.WriteLine("D=" + arr.Length + ":" + arr[0] + "," + arr[1] + "," + arr[2]);
        HashSet<int> u = new HashSet<int>();
        u.UnionWith(a);
        Console.WriteLine("E=" + u.Count);
    }
}
