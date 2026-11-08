// probe-tolookup-es.cs -- b5-gaps section 84: Enumerable.ToLookup with an
// elementSelector (3-generic / 4-generic overloads) over ARRAY + List sources.
// element families: int32 and string; key families: int32 and string; plus
// GC.Collect inside the ELEMENT selector (the selected values must stay alive
// through the two-pass materialization). Surfaces exercised: Count /
// this[key] (get_Item returns the TElement[] snapshot) / Contains / foreach
// over the lookup + group members + group Key.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    sealed class OrdIgnoreInt : IEqualityComparer<string>
    {
        public bool Equals(string x, string y) { return string.Equals(x, y, StringComparison.OrdinalIgnoreCase); }
        public int GetHashCode(string x) { return x == null ? 0 : x.ToUpper().GetHashCode(); }
    }

    public static class ProbeToLookupEs
    {
        static string DumpII(ILookup<int, int> lk)
        {
            string s = "";
            foreach (var g in lk)
            {
                string m = "";
                foreach (int e in g) m += e + ",";
                if (m.Length > 0) m = m.Substring(0, m.Length - 1);
                s += g.Key + "[" + m + "];";
            }
            return s;
        }

        static string DumpIS(ILookup<int, string> lk)
        {
            string s = "";
            foreach (var g in lk)
            {
                string m = "";
                foreach (string e in g) m += e + ",";
                if (m.Length > 0) m = m.Substring(0, m.Length - 1);
                s += g.Key + "[" + m + "];";
            }
            return s;
        }

        static string DumpSS(ILookup<string, string> lk)
        {
            string s = "";
            foreach (var g in lk)
            {
                string m = "";
                foreach (string e in g) m += e + ",";
                if (m.Length > 0) m = m.Substring(0, m.Length - 1);
                s += g.Key + "[" + m + "];";
            }
            return s;
        }

        public static void Main()
        {
            // [e1] List<int> source -> selected int values (x*10): first-appearance
            // keys, source-ordered members, get_Item + Contains.
            List<int> xs = new List<int>();
            xs.Add(5); xs.Add(3); xs.Add(5); xs.Add(8); xs.Add(3); xs.Add(1);
            var lk = xs.ToLookup(x => x % 3, x => x * 10);
            Console.WriteLine("e1c:" + lk.Count);
            Console.WriteLine("e1:" + DumpII(lk));
            Console.WriteLine("e1i0:" + String.Join(",", lk[0]));
            Console.WriteLine("e1i9:" + String.Join(",", lk[9]));
            Console.WriteLine("e1h1:" + lk.Contains(1));

            // [e2] int[] source -> string elements via elementSelector.
            int[] a = { 1, 2, 3 };
            var lk2 = a.ToLookup(x => x % 2, x => "v" + x);
            Console.WriteLine("e2c:" + lk2.Count);
            Console.WriteLine("e2:" + DumpIS(lk2));
            Console.WriteLine("e2i0:" + String.Join(",", lk2[0]));

            // [e3] string source -> string keys + string selected elements (List).
            List<string> ws = new List<string> { "a", "B", "a", "c", "A" };
            var lk3 = ws.ToLookup(w => w, w => w.ToLower());
            Console.WriteLine("e3c:" + lk3.Count);
            Console.WriteLine("e3:" + DumpSS(lk3));

            // [e4] 4-generic: elementSelector + user comparer (OrdinalIgnoreCase).
            List<string> ns = new List<string> { "key", "kEY", "other" };
            var lk4 = ns.ToLookup(w => w, w => "l:" + w.Length, new OrdIgnoreInt());
            Console.WriteLine("e4c:" + lk4.Count);
            Console.WriteLine("e4:" + DumpSS(lk4));
            Console.WriteLine("e4k:" + lk4.Contains("KEY"));

            // [e5] GC.Collect inside the ELEMENT selector over List<int> -> string.
            List<int> g5 = new List<int>();
            for (int i = 1; i <= 5; ++i) g5.Add(i);
            var lk5 = g5.ToLookup(x => x % 2, x => { GC.Collect(); return "e" + x; });
            Console.WriteLine("e5c:" + lk5.Count);
            Console.WriteLine("e5:" + DumpIS(lk5));

            // [e6] string[] source + 2-arg-like shape via elementSelector identity
            // over int elements with GC (int selection needs no rooting).
            int[] g6 = { 7, 8, 9 };
            var lk6 = g6.ToLookup(x => x / 10, x => { GC.Collect(); return x * x; });
            Console.WriteLine("e6c:" + lk6.Count);
            Console.WriteLine("e6:" + DumpII(lk6));

            // [e7] every element maps to a distinct key (all groups size 1).
            var lk7 = new List<string> { "p", "q", "r" }.ToLookup(w => w, w => w + "!");
            Console.WriteLine("e7c:" + lk7.Count);
            Console.WriteLine("e7:" + DumpSS(lk7));

            Console.WriteLine("done");
        }
    }
}