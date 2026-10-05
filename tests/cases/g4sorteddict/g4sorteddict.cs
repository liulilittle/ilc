using System;
using System.Collections.Generic;

// g4sorteddict -- G4 gap probe: SortedDictionary<string,int>. No generic
// sorted-dictionary domain exists -- expected LOUD/red. Enumeration is in
// sorted key order (net48), so the output is deterministic.
class Prog
{
    static void Main()
    {
        SortedDictionary<string, int> sd = new SortedDictionary<string, int>();
        sd.Add("b", 2);
        sd.Add("a", 1);
        sd.Add("c", 3);
        Console.WriteLine("count=" + sd.Count);
        Console.WriteLine("geta=" + sd["a"]);
        sd["b"] = 20;
        Console.WriteLine("setb=" + sd["b"]);
        Console.WriteLine("hasb=" + sd.ContainsKey("b"));
        Console.WriteLine("hasz=" + sd.ContainsKey("z"));

        string[] ks = new string[sd.Count];
        int n = 0;
        foreach (string k in sd.Keys)
            ks[n++] = k;
        for (int i = 0; i < ks.Length; i++)
            Console.WriteLine("key=" + ks[i]);

        Console.WriteLine("removed_a=" + sd.Remove("a"));
        Console.WriteLine("count2=" + sd.Count);
        Console.WriteLine("done");
    }
}
