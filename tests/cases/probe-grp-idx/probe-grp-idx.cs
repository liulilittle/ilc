// probe-grp-idx.cs -- b5-gaps section 91: the (element, index) Select/Where
// forms over a GROUPBY RESULT. Section 87 wired these over the ILookup result
// (reference cells) via the materialize-then-ref-index-kernel helper; the
// GroupBy result -- the same IEnumerable<IGrouping> surface -- was LOUD.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpIdx
    {
        static string Keys(IGrouping<int, int>[] gs)
        {
            string s = "";
            foreach (var g in gs) s += g.Key + ":" + g.Count() + ";";
            return s;
        }

        static string Ints(int[] xs)
        {
            string s = "";
            foreach (var x in xs) s += x + ",";
            return s;
        }

        static string Strs(string[] xs)
        {
            string s = "";
            foreach (var x in xs) s += x + ",";
            return s;
        }

        public static void Main()
        {
            var gr = new int[] { 3, 1, 3, 2, 1, 1 }.GroupBy(x => x);

            // g1: the (element, index) Select with an int32 result.
            Console.WriteLine("g1:" + Ints(gr.Select((g, i) => i).ToArray()));

            // g2: the (element, index) Select with a reference result.
            Console.WriteLine("g2:" + Strs(gr.Select((g, i) => "k" + g.Key + "_" + i).ToArray()));

            // g3: the (element, index) Select with an int64 result.
            Console.WriteLine("g3:" + gr.Select((g, i) => (long)i).ToArray().Length);

            // g4: the (element, index) Where.
            Console.WriteLine("g4:" + Keys(gr.Where((g, i) => i % 2 == 0).ToArray()));

            // g5/g6: the element-only forms still route.
            Console.WriteLine("g5:" + Ints(gr.Select(g => g.Key).ToArray()));
            Console.WriteLine("g6:" + Keys(gr.Where(g => g.Key > 1).ToArray()));

            Console.WriteLine("done");
        }
    }
}