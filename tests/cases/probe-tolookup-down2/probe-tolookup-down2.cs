// probe-tolookup-down2.cs -- b5-gaps section 86: the grouping operators over
// an ILookup RESULT (IEnumerable<IGrouping>): Where / Select / ToArray /
// ToList / First / FirstOrDefault. Each materializes the wrapper's grouping
// snapshot and applies the reference-element kernels.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeToLookupDown2
    {
        public static void Main()
        {
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);

            // [w1] Where(g => g.Count() > 1) then ToArray -> the passing keys.
            var w = lk.Where(g => g.Count() > 1).ToArray();
            Console.WriteLine("w1:" + w.Length);
            string wk = "";
            foreach (var g in w) wk += g.Key + ";";
            Console.WriteLine("w1k:" + wk);

            // [w2] Select(g => g.Key * 10) -> int[].
            var s = lk.Select(g => g.Key * 10).ToArray();
            Console.WriteLine("w2:" + String.Join(",", s));

            // [w3] Select(g => g.Key) (identity, int result).
            var sid = lk.Select(g => g.Key).ToArray();
            Console.WriteLine("w3:" + String.Join(",", sid));

            // [w4] ToArray() length.
            Console.WriteLine("w4:" + lk.ToArray().Length);

            // [w5] ToList() Count.
            Console.WriteLine("w5:" + lk.ToList().Count);

            // [w6] First().Key + FirstOrDefault() on an empty lookup.
            Console.WriteLine("w6:" + lk.First().Key);
            var empty = new int[0].ToLookup(x => x);
            var fd = empty.FirstOrDefault();
            Console.WriteLine("w6fd:" + (fd == null ? "null" : fd.Key.ToString()));

            // [w7] string keys: Select(g => g.Key) (ref result).
            var lkS = new[] { "a", "b", "a", "c" }.ToLookup(w2 => w2);
            var ks = lkS.Select(g => g.Key).ToArray();
            Console.WriteLine("w7:" + String.Join(",", ks));

            // [w8] Where over string keys + downstream Select over the result.
            var w8 = lkS.Where(g => g.Key != "b").Select(g => g.Key + "!").ToArray();
            Console.WriteLine("w8:" + String.Join(",", w8));

            // [w9] First() over an empty lookup -> InvalidOperationException.
            try { empty.First(); Console.WriteLine("w9:no-throw"); }
            catch (InvalidOperationException) { Console.WriteLine("w9:ioe"); }

            Console.WriteLine("done");
        }
    }
}