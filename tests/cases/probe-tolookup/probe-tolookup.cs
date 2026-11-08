// probe-tolookup.cs -- b5-gaps section 79: Enumerable.ToLookup over ARRAY
// sources (default key comparer; string/int keys; int/string elements).
//
// Expected (net48): ToLookup groups the source elements by the key selector,
// preserving the FIRST-APPEARANCE order of distinct keys and the SOURCE order
// within each group. The indexer lookup[key] yields the group's elements; a
// missing key yields an EMPTY sequence. Duplicate keys merge. A null key or a
// null keySelector throws ArgumentNullException. The lookup result's Count is
// the number of distinct keys; Contains(key) reports key membership.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeToLookup
    {
        public static void Main()
        {
            // [t1] int keys + int elements; key = x % 3 (keys 2,0,1 by
            // first-appearance); groups preserve source order.
            int[] xs = { 5, 3, 5, 8, 8, 3, 1 };
            var lk1 = xs.ToLookup(x => x % 3);
            Console.WriteLine("t1c:" + lk1.Count);
            Console.WriteLine("t1k2:" + lk1.Contains(2));
            Console.WriteLine("t1k9:" + lk1.Contains(9));
            var g1a = lk1[2];
            Console.WriteLine("t1g2:" + g1a.Sum() + "/" + g1a.Count());
            var g1b = lk1[0];
            Console.WriteLine("t1g0:" + g1b.Sum() + "/" + g1b.Count());
            var g1c = lk1[1];
            Console.WriteLine("t1g1:" + g1c.Sum() + "/" + g1c.Count());
            Console.WriteLine("t1miss:" + lk1[7].Count() + "/" + lk1[7].Sum());

            // [t2] string keys + int elements (ordinal), duplicate keys merge.
            string[] ws = { "a", "b", "a", "c", "b", "a" };
            var lk2 = ws.ToLookup(w => w);
            Console.WriteLine("t2c:" + lk2.Count);
            Console.WriteLine("t2a:" + lk2["a"].Count() + "/"
                + ((lk2["a"].Contains("a") ? 1 : 0) + (lk2["a"].Contains("b") ? 1 : 0)));
            Console.WriteLine("t2b:" + lk2["b"].Count() + "/" + lk2["b"].Contains("b"));
            Console.WriteLine("t2c9:" + lk2["c"].Count() + "/" + lk2["c"].Contains("c"));
            Console.WriteLine("t2x:" + lk2["x"].Count());
            Console.WriteLine("t2cc:" + ((lk2.Contains("b") ? 1 : 0) + (lk2.Contains("zz") ? 1 : 0)));

            // [t3] string keys + string elements (substring key merges).
            string[] ss = { "apple", "anvil", "berry", "avocado" };
            var lk3 = ss.ToLookup(s => s.Substring(0, 1));
            Console.WriteLine("t3c:" + lk3.Count);
            Console.WriteLine("t3a:" + lk3["a"].Count() + "/" + lk3["a"].Contains("anvil"));
            Console.WriteLine("t3b:" + lk3["b"].Count() + "/" + lk3["b"].Contains("berry"));

            // [t4] duplicate keys merge BEFORE any rational ordering of the
            // merged group (source order preserved: 20,20 / 30,30).
            int[] ds = { 10, 20, 30, 20, 40, 30 };
            var lk4 = ds.ToLookup(d => d / 10);
            Console.WriteLine("t4c:" + lk4.Count);
            Console.WriteLine("t4g1:" + lk4[1].Sum() + "/" + lk4[1].Count());
            Console.WriteLine("t4g2:" + lk4[2].Sum() + "/" + lk4[2].Count());
            Console.WriteLine("t4g3:" + lk4[3].Sum() + "/" + lk4[3].Count());
            Console.WriteLine("t4g4:" + lk4[4].Sum() + "/" + lk4[4].Count());

            // [t5] a NULL KEY is a LEGAL distinct key (net48 Lookup.Create stores it as
            // its own group -- unlike Dictionary). Count = 3 (a, null, b); the
            // "a" group has ONLY the "a" element; Contains(null) = true.
            string[] withNull = { "a", null, "b" };
            var l5 = withNull.ToLookup(x => x);
            Console.WriteLine("t5c:" + l5.Count);
            Console.WriteLine("t5a:" + l5["a"].Count() + "/" + l5["a"].Contains("a")
                + (l5["a"].Contains(null) ? 1 : 0));
            Console.WriteLine("t5n:" + ((l5.Contains(null) ? 0 : 1)));

            // [t6] null keySelector -> ArgumentNullException.
            string[] sn = { "a", "b" };
            try { var l6 = sn.ToLookup((Func<string, string>)null); Console.WriteLine("t6:no-throw"); }
            catch (ArgumentNullException) { Console.WriteLine("t6:argnull"); }

            // [t7] GC.Collect inside the key selector (rooting: the selector
            // produces NEW strings that must stay alive for the lookup's life).
            int[] gxs = { 1, 2, 3, 4 };
            var lk7 = gxs.ToLookup(x => { GC.Collect(); return "k" + x; });
            Console.WriteLine("t7c:" + lk7.Count);
            Console.WriteLine("t7g2:" + lk7["k2"].Sum() + "/" + lk7["k2"].Count());
            Console.WriteLine("t7g4:" + lk7["k4"].Sum() + "/" + lk7["k4"].Count());

            // [t8] empty source -> zero distinct keys; indexer yields empty.
            int[] empty = new int[0];
            var lk8 = empty.ToLookup(x => x);
            Console.WriteLine("t8c:" + lk8.Count);
            Console.WriteLine("t8m:" + lk8[0].Count() + "/" + lk8[0].Sum());

            Console.WriteLine("done");
        }
    }
}