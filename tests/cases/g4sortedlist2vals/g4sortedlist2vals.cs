using System;
using System.Collections.Generic;

// g4sortedlist2vals -- P3-extra probe: SortedList<string,int>.Values view +
// ContainsValue on the wired net48 surface:
//   sl.Values[i]                   (IList<int> indexer, LIVE view)
//   sl.Values.Count                (ICollection<int>.Count, LIVE view)
//   foreach (int v in sl.Values)   (IEnumerable<int> + IEnumerator<int>)
//   sl.ContainsValue(v)            (net48 value equality)
// The captured `live` view check pins the net48 LIVE semantics: mutating the
// list after `IList<int> live = sl.Values` must be visible through the view
// (the P3-extra wrapper returns the list itself, so this holds for the
// transpiled build too).
class Prog
{
    static void Main()
    {
        SortedList<string, int> sl = new SortedList<string, int>();
        sl.Add("b", 2);
        sl.Add("a", 1);
        sl.Add("c", 3);
        Console.WriteLine("v0=" + sl.Values[0]);
        Console.WriteLine("v1=" + sl.Values[1]);
        Console.WriteLine("v2=" + sl.Values[2]);
        Console.WriteLine("vcount=" + sl.Values.Count);
        int sum = 0;
        foreach (int v in sl.Values)
        {
            sum += v;
        }
        Console.WriteLine("vsum=" + sum);
        Console.WriteLine("has2=" + sl.ContainsValue(2));
        Console.WriteLine("has9=" + sl.ContainsValue(9));
        IList<int> live = sl.Values;
        sl["b"] = 20;
        Console.WriteLine("live1=" + live[1]);
        Console.WriteLine("has20=" + sl.ContainsValue(20));
        sl.RemoveAt(0);
        Console.WriteLine("livecount=" + live.Count);
        Console.WriteLine("vcount2=" + sl.Values.Count);
        int sum2 = 0;
        foreach (int v in sl.Values)
        {
            sum2 += v;
        }
        Console.WriteLine("vsum2=" + sum2);
        Console.WriteLine("done");
    }
}
