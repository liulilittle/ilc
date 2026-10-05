using System;
using System.Collections.Generic;

// g4eqcmp -- W6 probe: EqualityComparer<string/long/double/object/class>.
// Ordinal string equality (case-sensitive), long/double value equality
// (NaN == NaN), object/class reference identity. Deterministic.
class Prog
{
    sealed class Box
    {
        public int v;
        public Box(int v) { this.v = v; }
    }

    static void Main()
    {
        var se = EqualityComparer<string>.Default;
        Console.WriteLine("seq=" + se.Equals("abc", "abc"));
        Console.WriteLine("sne=" + se.Equals("abc", "ABC"));
        Console.WriteLine("snullnull=" + se.Equals(null, null));
        Console.WriteLine("snullx=" + se.Equals(null, "x"));
        Console.WriteLine("sxnull=" + se.Equals("x", null));

        var le = EqualityComparer<long>.Default;
        Console.WriteLine("leq=" + le.Equals(123456789012345L, 123456789012345L));
        Console.WriteLine("lne=" + le.Equals(1L, 2L));
        Console.WriteLine("lneg=" + le.Equals(-5L, -5L));

        var de = EqualityComparer<double>.Default;
        Console.WriteLine("deq=" + de.Equals(1.5, 1.5));
        Console.WriteLine("dne=" + de.Equals(1.5, 2.5));
        Console.WriteLine("dnan=" + de.Equals(double.NaN, double.NaN));
        Console.WriteLine("dzero=" + de.Equals(0.0, -0.0));

        var oe = EqualityComparer<object>.Default;
        object o1 = new object();
        object o2 = new object();
        Console.WriteLine("osame=" + oe.Equals(o1, o1));
        Console.WriteLine("odiff=" + oe.Equals(o1, o2));
        Console.WriteLine("onullnull=" + oe.Equals(null, null));

        var ce = EqualityComparer<Box>.Default;
        Box b1 = new Box(7);
        Box b2 = new Box(7);
        Console.WriteLine("csame=" + ce.Equals(b1, b1));
        Console.WriteLine("cdiff=" + ce.Equals(b1, b2));

        Console.WriteLine("done");
    }
}
