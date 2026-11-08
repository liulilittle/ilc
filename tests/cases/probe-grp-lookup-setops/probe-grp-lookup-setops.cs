// probe-grp-lookup-setops.cs -- b5-gaps section 89: the two-sequence set
// operators (Concat/Union/Intersect/Except/SequenceEqual), their user-comparer
// forms, and DefaultIfEmpty over a grouping-enumerable (an ILookup result AND
// a GroupBy result).
//
// net48 semantics that make this probe discriminating:
//   * the DEFAULT equality for IGrouping is REFERENCE equality (IGrouping does
//     not override Equals);
//   * a Lookup CACHES its Grouping objects, so lk.Union(lk.ToArray()) keeps
//     ONE set (the same references) -- 3 elements;
//   * a GroupBy result is LAZY: every enumeration re-runs the grouping and
//     yields FRESH Grouping objects, so gr.Except(gr.ToArray()) keeps BOTH --
//     2 elements;
//   * a comparer that compares by KEY collapses what reference equality keeps
//     apart (u14-u18), which is what makes the comparer arms observable.
// The oracle (net48) is the authority for every count/order below.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public sealed class KeyCmp : IEqualityComparer<IGrouping<int, int>>
    {
        public bool Equals(IGrouping<int, int> a, IGrouping<int, int> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Key == b.Key;
        }

        public int GetHashCode(IGrouping<int, int> o)
        {
            if (o == null) return 0;
            return o.Key.GetHashCode();
        }
    }

    public static class ProbeGrpLookupSetOps
    {
        static string Keys(IGrouping<int, int>[] gs)
        {
            string s = "";
            foreach (var g in gs)
                s += (g == null ? "null" : g.Key.ToString()) + ";";
            return s;
        }

        static string Show(string tag, IGrouping<int, int>[] gs)
        {
            return tag + ":" + gs.Length + ":" + Keys(gs);
        }

        public static void Main()
        {
            var lk = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x);   // keys 3,1,2
            var lk2 = new int[] { 2, 4 }.ToLookup(x => x);               // keys 2,4
            var lkSame = new int[] { 3, 1, 3, 2, 1, 1 }.ToLookup(x => x); // keys 3,1,2 (fresh objects)
            var gr = new int[] { 3, 1 }.GroupBy(x => x);
            var empty = new int[] { }.ToLookup(x => x);
            var cmp = new KeyCmp();

            // u1-u8: the plain forms over the ILookup result.
            Console.WriteLine(Show("u1", lk.Concat(lk.ToArray()).ToArray()));
            Console.WriteLine(Show("u2", lk.Concat(lk2).ToArray()));
            Console.WriteLine(Show("u3", lk.Union(lk2).ToArray()));
            Console.WriteLine(Show("u4", lk.Union(lk.ToArray()).ToArray()));
            Console.WriteLine(Show("u5", lk.Intersect(lk.ToArray()).ToArray()));
            Console.WriteLine(Show("u6", lk.Except(lk.ToArray()).ToArray()));
            Console.WriteLine("u7:" + lk.SequenceEqual(lk.ToArray()));
            Console.WriteLine("u8:" + lk.SequenceEqual(lk2));

            // u9-u11: DefaultIfEmpty (plain + explicit value).
            Console.WriteLine(Show("u9", lk.DefaultIfEmpty().ToArray()));
            Console.WriteLine(Show("u10", empty.DefaultIfEmpty().ToArray()));
            Console.WriteLine(Show("u11", empty.DefaultIfEmpty(lk.First()).ToArray()));

            // u12: Distinct with a comparer. The source is a single sequence of
            // reference-distinct groupings, so the comparer cannot change the
            // result -- this record pins the ARM's presence (a missing arm is a
            // transpile LOUD), not the comparer's effect.
            Console.WriteLine(Show("u12", lk.Distinct(cmp).ToArray()));

            // u13-u18: the comparer forms. u13 exercises the stack-depth-2
            // source recovery (the second sequence is an IGrouping[] snapshot);
            // u14-u18 use two lookups with overlapping keys so the key comparer
            // is observable against the default reference equality.
            Console.WriteLine(Show("u13", lk.Union(lk.ToArray(), cmp).ToArray()));
            Console.WriteLine(Show("u14", lk.Union(lk2, cmp).ToArray()));
            Console.WriteLine(Show("u15", lk.Intersect(lk2, cmp).ToArray()));
            Console.WriteLine(Show("u16", lk.Except(lk2, cmp).ToArray()));
            Console.WriteLine("u17:" + lk.SequenceEqual(lkSame, cmp));
            Console.WriteLine("u18:" + lk.SequenceEqual(lk2, cmp));

            // u19-u22: the same over a GroupBy result (the lazy/fresh-objects
            // side: Except over its own ToArray keeps both).
            Console.WriteLine(Show("u19", gr.Concat(gr.ToArray()).ToArray()));
            Console.WriteLine(Show("u20", gr.Union(lk2).ToArray()));
            Console.WriteLine(Show("u21", gr.Except(gr.ToArray()).ToArray()));
            Console.WriteLine("u22:" + gr.SequenceEqual(gr.ToArray()));

            // u23: the comparer form over a GroupBy result (key comparer
            // collapses the fresh objects; the default keeps both).
            Console.WriteLine(Show("u23", gr.Except(gr.ToArray(), cmp).ToArray()));

            // u24: a null second sequence.
            try
            {
                lk.Union(null);
                Console.WriteLine("u24:none");
            }
            catch (Exception e)
            {
                Console.WriteLine("u24:" + e.GetType().Name);
            }

            Console.WriteLine("done");
        }
    }
}