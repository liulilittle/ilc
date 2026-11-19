using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

class Program
{
    static void Main()
    {
        int[] a = { 7, 8, 9 };
        ReadOnlyCollection<int> ro = Array.AsReadOnly(a);

        // Count and the indexer are the two members user code actually uses.
        Console.WriteLine("count:" + ro.Count);
        Console.WriteLine("idx:" + ro[0] + ":" + ro[2]);

        // The wrapper is a VIEW: a later write through the array is visible.
        a[1] = 42;
        Console.WriteLine("view:" + ro[1]);

        // foreach over the wrapper.
        int sum = 0;
        foreach (int v in ro) sum += v;
        Console.WriteLine("sum:" + sum);

        // Contains / IndexOf / CopyTo.
        Console.WriteLine("contains:" + ro.Contains(42) + ":" + ro.Contains(1));
        Console.WriteLine("indexof:" + ro.IndexOf(9));
        int[] dst = new int[3];
        ro.CopyTo(dst, 0);
        Console.WriteLine("copyto:" + dst[0] + ":" + dst[1] + ":" + dst[2]);

        // A reference-element array.
        string[] s = { "p", "q" };
        ReadOnlyCollection<string> rs = Array.AsReadOnly(s);
        Console.WriteLine("ref:" + rs.Count + ":" + rs[1]);

        // An empty array.
        ReadOnlyCollection<int> re = Array.AsReadOnly(new int[0]);
        Console.WriteLine("empty:" + re.Count);
    }
}
