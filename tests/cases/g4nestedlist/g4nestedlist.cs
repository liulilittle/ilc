using System;
using System.Collections.Generic;

// g4nestedlist -- G4 nested-generic probe: List<List<int>> (a nested
// GenericInst element). Expected red until the nested reference domain is
// wired (the element is a managed reference -- the listobj storage with an
// inner List<int> element type tracked for downstream member calls).
// Deterministic: no time/random/paths.
class Prog
{
    static void Main()
    {
        List<List<int>> outer = new List<List<int>>();
        List<int> a = new List<int>();
        a.Add(1);
        a.Add(2);
        List<int> b = new List<int>();
        b.Add(3);
        outer.Add(a);
        outer.Add(b);
        Console.WriteLine("outercount=" + outer.Count);

        List<int> first = outer[0];
        Console.WriteLine("innercount=" + first.Count);
        Console.WriteLine("inner0=" + first[0]);
        Console.WriteLine("inner1=" + first[1]);

        List<int> second = outer[1];
        Console.WriteLine("second0=" + second[0]);

        int total = 0;
        for (int i = 0; i < outer.Count; i++)
        {
            List<int> inner = outer[i];
            for (int j = 0; j < inner.Count; j++)
                total += inner[j];
        }
        Console.WriteLine("total=" + total);

        Console.WriteLine("done");
    }
}
