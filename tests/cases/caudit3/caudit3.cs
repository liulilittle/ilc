//------------------------------------------------------------------------------
// caudit3.cs -- Comparer.Default SINGLE-DIRECTION IComparable fallback probe
// (G6-N follow-up, 2026-09-12).
//
// net48 System.Collections.Comparer.Compare:
//     a as IComparable -> a.CompareTo(b)
//     else b as IComparable -> -b.CompareTo(a)
//     else throw ArgumentException
// so BOTH argument orders are comparable when exactly one side implements
// IComparable. The runtime comparer (arraysort.cpp ComparerCompareCore)
// dispatches only the FIRST argument's IComparable, so the NoCmp-first probe
// is the discriminator. Both calls are wrapped; the exception TYPE NAME is
// printed so the run continues and the diff is byte-stable.
//
// net48: fwd=0, rev=0 (CompareTo returns 0 for every input). A runtime throw
// shows up as fwd_ex=<TypeName>.
//------------------------------------------------------------------------------
using System;
using System.Collections;

class NoCmp { }
class HasCmp : IComparable {
    public int CompareTo(object o) { return 0; }
}

namespace Cases { public static class Caudit3 {
    public static void Main() {
        Comparer cd = Comparer.Default;
        try { Console.WriteLine("fwd=" + cd.Compare(new NoCmp(), new HasCmp())); }
        catch (Exception e) { Console.WriteLine("fwd_ex=" + e.GetType().Name); }
        try { Console.WriteLine("rev=" + cd.Compare(new HasCmp(), new NoCmp())); }
        catch (Exception e) { Console.WriteLine("rev_ex=" + e.GetType().Name); }
    }
}}
