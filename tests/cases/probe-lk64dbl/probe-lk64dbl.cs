using System;
using System.Collections.Generic;
using System.Linq;
static class P
{
    static void Main()
    {
        var a = new[] { 3.0, 1.0, 2.0 };
        var lk = a.ToLookup(x => x);
        Console.WriteLine("[cnt] " + lk.Count);
        Console.WriteLine("[k1] " + string.Join(",", lk[1.0]));
        Console.WriteLine("[k2] " + string.Join(",", lk[2.0]));
        Console.WriteLine("[has3] " + lk.Contains(3.0));
        Console.WriteLine("[has9] " + lk.Contains(9.0));

        var b = new[] { "a", "b", "a" };
        var lk2 = b.ToLookup(s => s.Length);
        Console.WriteLine("[s.cnt] " + lk2.Count);
        Console.WriteLine("[s.k1] " + string.Join(",", lk2[1]));
        Console.WriteLine("[s.has2] " + lk2.Contains(2));

        var c = new[] { 5.0, 5.0, 7.0 };
        var lk3 = c.ToLookup(x => x);
        Console.WriteLine("[d.dup] " + string.Join(",", lk3[5.0]));
        Console.WriteLine("[d.cnt] " + lk3.Count);

        // b5-gaps section 107: the enumeration surface -- foreach over the lookup
        // yields one IGrouping per key; get_Key reads the double key.
        var lk4 = new[] { 2.0, 1.0, 2.0 }.ToLookup(x => x);
        foreach (var g in lk4)
        {
            Console.WriteLine("[enum] " + g.Key + ":" + string.Join(",", g));
        }

        // A double key group's ToList / Count / First ride the bucket's own family.
        var g2 = lk4[2.0];
        Console.WriteLine("[g.tolist] " + string.Join(",", g2.ToList()));
        Console.WriteLine("[g.count] " + g2.Count());
        Console.WriteLine("[g.first] " + g2.First());

        // NaN keys: net48 EqualityComparer<double>.Default is Double.Equals
        // (NaN == NaN), so a NaN key groups with itself.
        var lk5 = new[] { double.NaN, double.NaN, 1.0 }.ToLookup(x => x);
        Console.WriteLine("[nan.cnt] " + lk5.Count);
        Console.WriteLine("[nan.has] " + lk5.Contains(double.NaN));
        Console.WriteLine("[nan.k] " + string.Join(",", lk5[double.NaN]));

        // +0.0 / -0.0 are equal under Double.Equals (different bit patterns).
        var lk6 = new[] { 0.0, -0.0 }.ToLookup(x => x);
        Console.WriteLine("[zero.cnt] " + lk6.Count);
        Console.WriteLine("[zero.k] " + string.Join(",", lk6[0.0]));

        // A List<double> source (the List snapshot delegates to the array kernel).
        var src = new List<double> { 4.0, 4.0, 9.0 };
        var lk7 = src.ToLookup(x => x);
        Console.WriteLine("[l.cnt] " + lk7.Count);
        Console.WriteLine("[l.k4] " + string.Join(",", lk7[4.0]));

        // A user IEqualityComparer<double>.
        var lk8 = new[] { 1.0, 2.0, 3.0 }.ToLookup(x => x, new AbsComparer());
        Console.WriteLine("[cmp.cnt] " + lk8.Count);
        Console.WriteLine("[cmp.k1] " + string.Join(",", lk8[1.0]));
    }

    sealed class AbsComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) { return Math.Abs(x) == Math.Abs(y); }
        public int GetHashCode(double obj) { return Math.Abs(obj).GetHashCode(); }
    }
}
