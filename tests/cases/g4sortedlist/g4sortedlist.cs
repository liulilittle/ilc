using System;
using System.Collections.Generic;

// g4sortedlist -- G4 gap probe: SortedList<string,int>. The wired sortedlist
// domain is the NON-generic System.Collections.SortedList only; the generic
// SortedList`2 has no owner arm and no alloc entry -- expected LOUD/red.
// Keys enumerate in SORTED order (net48), so the output is deterministic.
class Prog
{
    static void Main()
    {
        SortedList<string, int> sl = new SortedList<string, int>();
        sl.Add("b", 2);
        sl.Add("a", 1);
        sl.Add("c", 3);
        Console.WriteLine("count=" + sl.Count);
        Console.WriteLine("geta=" + sl["a"]);
        sl["b"] = 20;
        Console.WriteLine("setb=" + sl["b"]);
        Console.WriteLine("hasb=" + sl.ContainsKey("b"));
        Console.WriteLine("hasz=" + sl.ContainsKey("z"));

        string[] ks = new string[sl.Count];
        int n = 0;
        foreach (string k in sl.Keys)
            ks[n++] = k;
        for (int i = 0; i < ks.Length; i++)
            Console.WriteLine("key=" + ks[i]);

        Console.WriteLine("removed_a=" + sl.Remove("a"));
        Console.WriteLine("count2=" + sl.Count);
        Console.WriteLine("done");
    }
}
