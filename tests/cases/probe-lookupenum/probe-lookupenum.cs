// probe-lookupenum.cs -- b5-gaps section 81: Enumerable.GetEnumerator over
// the ILookup result (foreach over the lookup yields the IGrouping objects in
// FIRST-APPEARANCE key order; each grouping exposes Key + enumerates its
// elements in SOURCE order) -- default and user-comparer lookups, int32 and
// string keys/elements, GC-inside-comparer.
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
        public bool Equals(string x, string y) { GC.Collect(); return string.Equals(x, y); }
        public int GetHashCode(string x) { return x == null ? 0 : x.GetHashCode(); }
    }

    public static class ProbeLookupEnum
    {
        public static string DumpInt(ILookup<int, int> lk)
        {
            string s = "";
            foreach (var g in lk)
            {
                long sum = 0;
                int cnt = 0;
                string members = "";
                foreach (var e in g)
                {
                    ++cnt;
                    sum += e;
                    members += e + ",";
                }
                if (members.Length > 0) members = members.Substring(0, members.Length - 1);
                s += g.Key + ":" + sum + "/" + cnt + "[" + members + "];";
            }
            return s;
        }

        public static string DumpStrKey(ILookup<string, int> lk)
        {
            string s = "";
            foreach (var g in lk)
            {
                long sum = 0;
                int cnt = 0;
                string members = "";
                foreach (var e in g)
                {
                    ++cnt;
                    sum += e;
                    members += e + ",";
                }
                if (members.Length > 0) members = members.Substring(0, members.Length - 1);
                s += g.Key + ":" + sum + "/" + cnt + "[" + members + "];";
            }
            return s;
        }

        public static string DumpStr(ILookup<string, string> lk)
        {
            string s = "";
            foreach (var g in lk)
            {
                int cnt = 0;
                string members = "";
                foreach (var e in g)
                {
                    ++cnt;
                    members += e + ",";
                }
                if (members.Length > 0) members = members.Substring(0, members.Length - 1);
                s += g.Key + ":" + cnt + "[" + members + "];";
            }
            return s;
        }

        public static void Main()
        {
            // [e1] int keys + int elements, default comparer. xs keys by
            // x%3 -> first-appearance 2,0,1; members in source order.
            int[] xs = { 5, 3, 5, 8, 8, 3, 1 };
            var lk = xs.ToLookup(x => x % 3);
            Console.WriteLine("e1c:" + lk.Count);
            Console.WriteLine("e1:" + DumpInt(lk));
            // second enumeration is a fresh enumerator, same order (soundness).
            string again = "";
            foreach (var g in lk) again += g.Key + ";";
            Console.WriteLine("e1a:" + again);

            // [e2] PARITY comparer: keys merge by (x&1); each group's Key is
            // the FIRST-APPEARANCE key (odd -> 7, even -> 4).
            int[] xs2 = { 7, 4, 6, 1, 2, 8 };
            var lk2 = xs2.ToLookup(x => x, new ParityIntComparer());
            Console.WriteLine("e2c:" + lk2.Count);
            Console.WriteLine("e2:" + DumpInt(lk2));

            // [e3] OrdinalIgnoreCase string keys + string elements.
            string[] ws = { "a", "B", "a", "b", "c", "A" };
            var lk3 = ws.ToLookup(w => w, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("e3c:" + lk3.Count);
            Console.WriteLine("e3:" + DumpStr(lk3));

            // [e4] string keys (Substring), the group Key is the FIRST
            // appearance of each distinct key; int elements.
            int[] gxs = { 10, 20, 30, 20, 40, 30 };
            var lk4 = gxs.ToLookup(x => (x / 10).ToString());
            Console.WriteLine("e4c:" + lk4.Count);
            Console.WriteLine("e4:" + DumpStrKey(lk4));

            // [e5] GC.Collect inside the comparer: the materialised groupings
            // (keys + element arrays) stay readable for the whole foreach.
            int[] g5 = { 1, 2, 3, 4, 5 };
            var lk5 = g5.ToLookup(x => "k" + x, new CollectingComparer());
            Console.WriteLine("e5c:" + lk5.Count);
            Console.WriteLine("e5:" + DumpStrKey(lk5));

            Console.WriteLine("done");
        }
    }
}