// probe-tolookup-down.cs -- b5-gaps section 85: downstream LINQ operators
// over an ILookup RESULT. Enumerable.Count() / Any() over the wrapper ride the
// wrapper's own group count (a generic ref/array Count reads the wrapper as an
// array header -- a garbage count). Also: per-bucket Count() over the IGrouping
// objects the foreach yields, empty-result Count/Any, a comparer-built result
// and an elementSelector-built result.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    sealed class OrdIgnoreStr : IEqualityComparer<string>
    {
        public bool Equals(string x, string y) { return string.Equals(x, y, StringComparison.OrdinalIgnoreCase); }
        public int GetHashCode(string x) { return x == null ? 0 : x.ToUpper().GetHashCode(); }
    }

    public static class ProbeToLookupDown
    {
        public static void Main()
        {
            // [d1] Count() over a List-built lookup (3 groups).
            List<int> xs = new List<int>();
            xs.Add(3); xs.Add(1); xs.Add(3); xs.Add(2); xs.Add(1); xs.Add(1);
            var lk = xs.ToLookup(x => x);
            Console.WriteLine("d1c:" + lk.Count());
            Console.WriteLine("d1a:" + lk.Any());
            Console.WriteLine("d1s:" + lk.Count());

            // [d2] empty source: Count()==0, Any()==false.
            var empty = new int[0].ToLookup(x => x % 3);
            Console.WriteLine("d2c:" + empty.Count());
            Console.WriteLine("d2a:" + empty.Any());

            // [d3] comparator-built (OrdinalIgnoreCase) result Count()/Any().
            string[] ws = { "a", "B", "a", "C" };
            var lk3 = ws.ToLookup(w => w, new OrdIgnoreStr());
            Console.WriteLine("d3c:" + lk3.Count());
            Console.WriteLine("d3a:" + lk3.Any());
            Console.WriteLine("d3g:" + lk3.Count());

            // [d4] elementSelector-built (int selection) result Count()/Any().
            int[] g4 = { 7, 8, 9, 10 };
            var lk4 = g4.ToLookup(x => x % 2, x => x * 10);
            Console.WriteLine("d4c:" + lk4.Count());
            Console.WriteLine("d4a:" + lk4.Any());

            // [d5] per-bucket Count()/Key over the groups the foreach yields.
            string s = "";
            foreach (var g in lk)
                s += g.Key + "=" + g.Count() + ";";
            Console.WriteLine("d5:" + s);

            // [d6] ToLookup(string keys) Count()/Any() over a string result.
            var lk6 = new[] { "p", "q", "r" }.ToLookup(w => w, w => w + "!");
            Console.WriteLine("d6c:" + lk6.Count());
            Console.WriteLine("d6a:" + lk6.Any());

            Console.WriteLine("done");
        }
    }
}