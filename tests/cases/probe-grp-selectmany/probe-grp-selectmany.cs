// probe-grp-selectmany.cs -- b5-gaps section 92: Enumerable.SelectMany over a
// grouping-enumerable (an ILookup result AND a GroupBy result). One method per
// shape so one LOUD does not hide the others.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpSelectMany
    {
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

        static string Dbls(double[] xs)
        {
            string s = "";
            foreach (var x in xs) s += System.Math.Round(x, 1) + ",";
            return s;
        }

        static void M1()
        {
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);
            Console.WriteLine("m1:" + Ints(lk.SelectMany(g => g).ToArray()));
        }

        static void M2()
        {
            // An ARRAY inner (the section-59 kernel's native shape) instead of
            // `g.Where(...)`: element-only operators over a SINGLE GROUPING
            // object are a separate registered gap (b5-gaps section 92).
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);
            Console.WriteLine("m2:" + Ints(lk.SelectMany(g => new int[] { g.Key, g.Count() }).ToArray()));
        }

        static void M3()
        {
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);
            Console.WriteLine("m3:" + Dbls(lk.SelectMany(g => g, (g, x) => (double)(g.Key * 10 + x)).ToArray()));
        }

        static void M4()
        {
            // A reference-element ARRAY inner.
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);
            Console.WriteLine("m4:" + Strs(lk.SelectMany(g => new string[] { "v" + g.Key }).ToArray()));
        }

        static void M5()
        {
            var gr = new int[] { 3, 1, 3, 2, 1, 1 }.GroupBy(x => x);
            Console.WriteLine("m5:" + Ints(gr.SelectMany(g => g).ToArray()));
        }

        static void M6()
        {
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);
            Console.WriteLine("m6:" + Ints(lk.Select(g => g.Key).ToArray()));
        }

        public static void Main()
        {
            M1(); M2(); M3(); M4(); M5(); M6();
            Console.WriteLine("done");
        }
    }
}