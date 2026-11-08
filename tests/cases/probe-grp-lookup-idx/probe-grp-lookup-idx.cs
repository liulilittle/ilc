// probe-grp-lookup-idx.cs -- b5-gaps section 87: the (element, index) forms
// over an ILookup result, plus Where over a GroupBy result (the shared
// grouping-enumerable surface). All materialize the grouping objects and apply
// the standard ref index/predicate kernels.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpLookupIdx
    {
        public static void Main()
        {
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);

            // [x1] Where((g, i) => i % 2 == 0) -> the even-index groups.
            var w = lk.Where((g, i) => i % 2 == 0).ToArray();
            Console.WriteLine("x1:" + w.Length);
            string wk = "";
            foreach (var g in w) wk += g.Key + ";";
            Console.WriteLine("x1k:" + wk);

            // [x2] Select((g, i) => g.Key * 10 + i) -> int[].
            var s = lk.Select((g, i) => g.Key * 10 + i).ToArray();
            Console.WriteLine("x2:" + String.Join(",", s));

            // [x3] Select((g, i) => g.Key + ":" + i) -> string[].
            var ss = lk.Select((g, i) => g.Key + ":" + i).ToArray();
            Console.WriteLine("x3:" + String.Join(",", ss));

            // [x4] GroupBy result Where(g => g.Count() > 1) -> the passing keys.
            var gr = new int[] { 3, 1, 3, 2, 1, 1 }.GroupBy(x => x);
            var gw = gr.Where(g => g.Count() > 1).ToArray();
            Console.WriteLine("x4:" + gw.Length);
            string gk = "";
            foreach (var g in gw) gk += g.Key + ";";
            Console.WriteLine("x4k:" + gk);

            // [x5] GroupBy result Where + downstream Select.
            var gws = gr.Where(g => g.Count() > 1).Select(g => g.Key * 100).ToArray();
            Console.WriteLine("x5:" + String.Join(",", gws));

            // [x6] string-key lookup Where idx + Select idx chain.
            var lkS = new[] { "a", "b", "a", "c" }.ToLookup(w2 => w2);
            var x6 = lkS.Where((g, i) => i >= 1).Select((g, i) => g.Key + i).ToArray();
            Console.WriteLine("x6:" + String.Join(",", x6));

            Console.WriteLine("done");
        }
    }
}