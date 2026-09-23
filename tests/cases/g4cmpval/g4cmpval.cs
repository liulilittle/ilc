using System;
using System.Collections.Generic;

// g4cmpval -- W6 probe: Comparer<int/string/long/object>. Numeric compare,
// ordinal string compare (null-first), object IComparable dispatch.
// Verdicts normalized with Math.Sign (-1/0/1). Deterministic.
// NOTE: mixed-case ordering (Compare("abc","ABC")) is excluded -- net48
// uses culture order (-1) while this domain documents ordinal (+1);
// owned by strsort_culture / P1-extra #1.
class Prog
{
    sealed class Box : IComparable
    {
        public int v;
        public Box(int v) { this.v = v; }
        public int CompareTo(object other)
        {
            Box o = (Box)other;
            if (this.v < o.v) return -1;
            if (this.v > o.v) return 1;
            return 0;
        }
    }

    static void Main()
    {
        var ic = Comparer<int>.Default;
        Console.WriteLine("ilt=" + Math.Sign(ic.Compare(1, 2)));
        Console.WriteLine("igt=" + Math.Sign(ic.Compare(2, 1)));
        Console.WriteLine("ieq=" + Math.Sign(ic.Compare(5, 5)));
        Console.WriteLine("ineg=" + Math.Sign(ic.Compare(-3, -3)));

        var sc = Comparer<string>.Default;
        Console.WriteLine("slt=" + Math.Sign(sc.Compare("a", "b")));
        Console.WriteLine("sgt=" + Math.Sign(sc.Compare("b", "a")));
        Console.WriteLine("seq=" + Math.Sign(sc.Compare("abc", "abc")));
        Console.WriteLine("snull=" + Math.Sign(sc.Compare(null, "a")));
        Console.WriteLine("sxnull=" + Math.Sign(sc.Compare("a", null)));
        Console.WriteLine("snullnull=" + Math.Sign(sc.Compare(null, null)));

        var lc = Comparer<long>.Default;
        Console.WriteLine("llt=" + Math.Sign(lc.Compare(1L, 2L)));
        Console.WriteLine("lgt=" + Math.Sign(lc.Compare(2L, 1L)));
        Console.WriteLine("leq=" + Math.Sign(lc.Compare(123456789012345L, 123456789012345L)));

        var oc = Comparer<object>.Default;
        Console.WriteLine("olt=" + Math.Sign(oc.Compare(new Box(1), new Box(2))));
        Console.WriteLine("ogt=" + Math.Sign(oc.Compare(new Box(2), new Box(1))));
        Console.WriteLine("oeq=" + Math.Sign(oc.Compare(new Box(3), new Box(3))));
        Console.WriteLine("onull=" + Math.Sign(oc.Compare(null, new Box(1))));

        Console.WriteLine("done");
    }
}
