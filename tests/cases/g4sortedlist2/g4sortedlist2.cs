using System;
using System.Collections.Generic;

// g4sortedlist2 -- G4 second-stage probe: SortedList<string,int> INDEX
// surface. net48-legal rewrite: GetKey/GetValueAt/GetByIndex/SetByIndex are
// NOT public on SortedList<TKey,TValue> (original probe: 6x CS1061), and the
// generic class does NOT implement non-generic IList on net48 ((IList)sl
// compiles but throws InvalidCastException at run time -- probe evidence in
// the case workdir). The public index surface is the IList<TKey> Keys
// snapshot plus the by-key IDictionary indexer, so:
//   GetKey(i)       -> Keys[i]
//   GetValueAt(i)   -> this[Keys[i]]   (value at sorted index i)
//   GetByIndex(i)   -> this[Keys[i]]
//   SetByIndex(i,v) -> this[Keys[i]] = v
// Keys enumerate in SORTED order (net48), so every index is stable. Capacity
// is deterministic only via a >= comparison (growth policy is an oracle-fixed
// implementation detail). RemoveAt/IndexOfKey/Capacity/Count are public as-is.
class Prog
{
    static void Main()
    {
        SortedList<string, int> sl = new SortedList<string, int>();
        sl.Add("b", 2);
        sl.Add("a", 1);
        sl.Add("c", 3);
        Console.WriteLine("capok=" + (sl.Capacity >= sl.Count));
        Console.WriteLine("key0=" + sl.Keys[0]);
        Console.WriteLine("val0=" + sl[sl.Keys[0]]);
        Console.WriteLine("byidx1=" + sl[sl.Keys[1]]);
        Console.WriteLine("idxb=" + sl.IndexOfKey("b"));
        Console.WriteLine("idxz=" + sl.IndexOfKey("z"));
        sl[sl.Keys[1]] = 20;
        Console.WriteLine("byidx1b=" + sl[sl.Keys[1]]);
        sl.RemoveAt(0);
        Console.WriteLine("count=" + sl.Count);
        Console.WriteLine("key0b=" + sl.Keys[0]);
        Console.WriteLine("done");
    }
}
