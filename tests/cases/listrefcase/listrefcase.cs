using System;
using System.Collections.Generic;

// listrefcase -- MINIMAL isolation.
// Only: new List<string>() + Add + Count + indexer.
// If this builds, the reference instantiation is reachable and the fault lies
// in one of the D-LISTOBJ-1..10 members; if it does NOT, the fault is upstream
// (declaration emission for a method that touches a reference instantiation).
class Prog
{
    static void Main()
    {
        var l = new List<string>();
        l.Add("b");
        l.Add("d");
        Console.WriteLine("count=" + l.Count);
        Console.WriteLine("idx1=" + l[1]);
        Console.WriteLine("done");
    }
}
