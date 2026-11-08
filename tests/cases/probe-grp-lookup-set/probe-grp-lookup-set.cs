// probe-grp-lookup-set.cs -- b5-gaps section 88: the set/sequence family
// operators over a grouping-enumerable (an ILookup result AND a GroupBy
// result): Distinct / Reverse / Skip / Take. Each materializes the grouping
// objects (object[]) and applies the family-generic reference kernels.
// net48 note: Distinct over IGrouping uses reference equality (IGrouping does
// not override Equals), so every grouping is distinct -- the probe pins the
// order/length semantics, not a dedup.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpLookupSet
    {
        static string Keys(IGrouping<int, int>[] gs)
        {
            string s = "";
            foreach (var g in gs) s += g.Key + ";";
            return s;
        }

        public static void Main()
        {
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);

            // [s1] Distinct over the lookup (reference equality -> all groups).
            Console.WriteLine("s1:" + lk.Distinct().ToArray().Length);
            Console.WriteLine("s1k:" + Keys(lk.Distinct().ToArray()));

            // [s2] Reverse over the lookup.
            var r = lk.Reverse().ToArray();
            Console.WriteLine("s2:" + r.Length);
            Console.WriteLine("s2k:" + Keys(r));

            // [s3] Skip(1) / Take(2) over the lookup.
            var sk = lk.Skip(1).ToArray();
            var tk = lk.Take(2).ToArray();
            Console.WriteLine("s3sk:" + Keys(sk));
            Console.WriteLine("s3tk:" + Keys(tk));

            // [s4] the same four over a GroupBy result.
            var gr = new int[] { 3, 1, 3, 2, 1, 1 }.GroupBy(x => x);
            Console.WriteLine("s4d:" + gr.Distinct().ToArray().Length);
            Console.WriteLine("s4r:" + Keys(gr.Reverse().ToArray()));
            Console.WriteLine("s4sk:" + Keys(gr.Skip(1).ToArray()));
            Console.WriteLine("s4tk:" + Keys(gr.Take(2).ToArray()));

            // [s5] Skip past the end / Take(0) edge cases over the lookup.
            Console.WriteLine("s5sk:" + lk.Skip(99).ToArray().Length);
            Console.WriteLine("s5tk:" + lk.Take(0).ToArray().Length);

            // [s6] a string-key lookup Reverse + downstream Select.
            var lkS = new[] { "a", "b", "a", "c" }.ToLookup(w => w);
            var s6 = lkS.Reverse().Select(g => g.Key).ToArray();
            Console.WriteLine("s6:" + String.Join(",", s6));

            Console.WriteLine("done");
        }
    }
}