// probe-tolookup-list.cs -- b5-gaps section 82: Enumerable.ToLookup over a
// List<T> SOURCE (int32 and string elements), mirroring the array case
// (first-appearance key order; members in source order; Count / get_Item /
// Contains / foreach over the result) with default and user IEqualityComparer
// overloads and a GC-collection-in-comparer case built from a growing List.
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

    public static class ProbeToLookupList
    {
        static string DumpNum(ILookup<int, int> lk)
        {
        string s = "";
        foreach (var g in lk)
        {
            long sum = 0;
            int cnt = 0;
            string members = "";
            foreach (int e in g)
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

    static string DumpStr(ILookup<string, int> lk)
        {
            string s = "";
        foreach (var g in lk)
        {
            long sum = 0;
            int cnt = 0;
            string members = "";
            foreach (int e in g)
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

    static string DumpStrStr(ILookup<string, string> lk)
        {
            string s = "";
            foreach (var g in lk)
            {
                int cnt = 0;
                string members = "";
                foreach (string e in g)
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
            // [l1] List<int> built by Add() (the list reallocates several
            // times -- the snapshot must be an exact copy of the FINAL list).
            List<int> xs = new List<int>();
            xs.Add(5); xs.Add(3); xs.Add(5); xs.Add(8); xs.Add(8); xs.Add(3); xs.Add(1);
            var lk = xs.ToLookup(x => x % 3);
            Console.WriteLine("l1c:" + lk.Count);
            Console.WriteLine("l1:" + DumpNum(lk));
            Console.WriteLine("l1i0:" + String.Join(",", lk[0]));
            Console.WriteLine("l1has1:" + lk.Contains(1));
            Console.WriteLine("l1has4:" + lk.Contains(4));
            string again = "";
            foreach (var g in lk) again += g.Key + ";";
            Console.WriteLine("l1a:" + again);

            // [l2] user comparer over List<int> (parity merge).
            List<int> xs2 = new List<int> { 7, 4, 6, 1, 2, 8 };
            var lk2 = xs2.ToLookup(x => x, new ParityIntComparer());
            Console.WriteLine("l2c:" + lk2.Count);
            Console.WriteLine("l2:" + DumpNum(lk2));

            // [l3] OrdinalIgnoreCase comparer over List<string>.
            List<string> ws = new List<string> { "a", "B", "a", "b", "c", "A" };
            var lk3 = ws.ToLookup(w => w, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("l3c:" + lk3.Count);
            Console.WriteLine("l3:" + DumpStrStr(lk3));

            // [l4] GC.Collect inside the comparer while iterating result of a
            // List<string> source.
            List<string> g4 = new List<string>();
            for (int i = 1; i <= 5; ++i) g4.Add("k" + i);
            var lk4 = g4.ToLookup(x => x, new CollectingComparer());
            Console.WriteLine("l4c:" + lk4.Count);
            Console.WriteLine("l4:" + DumpStrStr(lk4));

            // [l5] null key from a List<string> source + explicit comparer is
            // a LEGAL single-element group (net48 semantics).
            List<string> n5 = new List<string> { "x", "y" };
            var lk5 = n5.ToLookup(x => x == "x" ? null : x, StringComparer.Ordinal);
            Console.WriteLine("l5c:" + lk5.Count);
            Console.WriteLine("l5:" + DumpStrStr(lk5));

            Console.WriteLine("done");
        }
    }
}