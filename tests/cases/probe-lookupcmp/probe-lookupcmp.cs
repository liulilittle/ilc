// probe-lookupcmp.cs -- b5-gaps section 80: Enumerable.ToLookup with a USER
// IEqualityComparer<TKey> over ARRAY sources (int32 keys + string keys).
//
// Expected (net48): the comparer's equality drives BOTH the group MERGE
// (first-appearance order of EQUAL-UNDER-COMPARER keys) and the instance
// Contains(key) / get_Item(key) membership scans. A null comparer falls back
// to the default (ordinal == / string ordinal) -- identical to section 79.
// A NULL KEY under a non-null comparer is measured separately (net48 calls
// comparer.GetHashCode(null)); the probe pins the exception TYPE only.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    sealed class ParityIntComparer : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) { return (x & 1) == (y & 1); }
        public int GetHashCode(int x) { return x & 1; }
    }

    sealed class CollectingComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y)
        {
            GC.Collect();
            return string.Equals(x, y);
        }
        public int GetHashCode(string x) { return x == null ? 0 : x.GetHashCode(); }
    }

    public static class ProbeLookupCmp
    {
        public static void Main()
        {
            // [c1] int keys + int elements with the PARITY comparer: keys
            // merge by (x&1) only. xs = { 7, 4, 6, 1, 2, 8 } -> odd {7,1},
            // even {4,6,2,8} in first-appearance order. Contains true for
            // EVERY int (each parity bucket exists).
            int[] xs = { 7, 4, 6, 1, 2, 8 };
            var lk = xs.ToLookup(x => x, new ParityIntComparer());
            Console.WriteLine("c1c:" + lk.Count);
            Console.WriteLine("c1e:" + lk[2].Sum() + "/" + lk[2].Count());
            Console.WriteLine("c1o:" + lk[9].Sum() + "/" + lk[9].Count());
            Console.WriteLine("c1c33:" + lk[3].Sum() + "/" + lk[3].Count());
            Console.WriteLine("c1ce:" + lk.Contains(4) + "/" + lk.Contains(-6));
            Console.WriteLine("c1co:" + lk.Contains(3) + "/" + lk.Contains(1004));

            // [c2] string keys + string elements with OrdinalIgnoreCase.
            string[] ws = { "a", "B", "a", "b", "c", "A" };
            var lk2 = ws.ToLookup(w => w, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("c2c:" + lk2.Count);
            Console.WriteLine("c2A:" + lk2["A"].Count() + "/" + (lk2["A"].Contains("a") ? 1 : 0));
            Console.WriteLine("c2b:" + lk2["B"].Count() + "/" + (lk2["B"].Contains("b") ? 1 : 0));
            Console.WriteLine("c2c9:" + lk2["C"].Count() + "/" + (lk2["C"].Contains("c") ? 1 : 0));
            Console.WriteLine("c2cc:" + ((lk2.Contains("C") ? 1 : 0) + (lk2.Contains("x") ? 1 : 0)));
            Console.WriteLine("c2x:" + lk2["z"].Count());

            // [c3] null comparer == default: duplicate int keys merge (sanity).
            int[] xs3 = { 5, 3, 5, 8, 5 };
            var lk3 = xs3.ToLookup(x => x, null);
            Console.WriteLine("c3c:" + lk3.Count + "/" + lk3[5].Sum() + "/" + lk3[5].Count());

            // [c4] GC.Collect INSIDE the comparer's Equals: the comparer
            // object + the selector-produced key strings must stay readable
            // for the whole lookup life.
            int[] gxs = { 1, 2, 3, 4, 5 };
            var lk4 = gxs.ToLookup(x => "k" + x, new CollectingComparer());
            Console.WriteLine("c4c:" + lk4.Count);
            Console.WriteLine("c4k2:" + lk4["k2"].Sum() + "/" + lk4["k2"].Count());
            Console.WriteLine("c4k5:" + lk4["k5"].Sum() + "/" + lk4["k5"].Count());

            // [c5] NULL KEY under a non-null comparer: net48 calls
            // comparer.GetHashCode(null) -> ArgumentNullException. Pin the
            // TYPE only (the case catches and prints the type name).
            try
            {
                string[] nn = { "a", null, "b" };
                var lk5 = nn.ToLookup(x => x, StringComparer.Ordinal);
                Console.WriteLine("c5:no-throw:" + lk5.Count);
            }
            catch (Exception e) { Console.WriteLine("c5:" + e.GetType().Name); }

            Console.WriteLine("done");
        }
    }
}