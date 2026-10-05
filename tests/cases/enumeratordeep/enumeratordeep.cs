//------------------------------------------------------------------------------
// enumeratordeep.cs -- ENUMERATOR FULL-INTERFACE e2e case (net48 oracle,
// byte-for-byte). Created in e2e batch C38, AFTER the D-SL-3 enumerator
// wiring (batch 94, commit 9652107): ICollection.get_Count /
// IEnumerable.GetEnumerator / IEnumerator.MoveNext/get_Current/Reset over
// the SortedList Keys / Values SNAPSHOT arrays (MethodMap.Collections.cs ->
// sortedlist.cpp snapshot enumerator).
//
// The enumerator interface wiring is reachable ONLY for the SortedList
// Keys/Values snapshot receivers, so this case drives that surface hard and
// adds the "LINQ over collections" half (the int32[] collection LINQ
// surface, MethodMap.Linq.cs -> linq.h/linq_map.h/linq_sort.h):
//
//   1. ICollection.Count through the interface (Keys AND Values).
//   2. foreach over an ICollection-typed Keys / Values (the compiled
//      IEnumerable.GetEnumerator + IEnumerator.MoveNext/Current shape).
//   3. EXPLICIT IEnumerator protocol: GetEnumerator / MoveNext / Current /
//      Reset / exhaustion (MoveNext returns false past the end, then again).
//   4. TWO INDEPENDENT enumerators over the SAME snapshot -- each
//      GetEnumerator wraps the snapshot array with its own cursor (D-SL-3
//      creates a FRESH enumerator per call); interleaved MoveNext probes
//      prove independent cursors.
//   5. LINQ over int32[] collections (the wired LINQ surface): Count /
//      Where / Select / Sum / Min / Max / First / FirstOrDefault / Any /
//      OrderBy -- static-method predicates (the linqdeep/linqops contract).
//
// RECORDED, NOT PROBED (no transpiler producer): LINQ over a NON-GENERIC
// collection (SortedList.Keys is IEnumerable, not IEnumerable<T> -- LINQ
// extension methods need IEnumerable<T>; the ref-family LINQ icalls exist in
// the runtime but no transpiler path constructs a reference-element LINQ
// chain). Also: enumerator.Current BEFORE the first MoveNext / AFTER
// exhaustion is undefined on net48 and would be an out-of-bounds read on the
// snapshot enumerator -- never probed here.
//
// Every line prints "label=True/False" (deterministic, hand-verifiable).
// net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Linq;

namespace Cases
{
    public static class EnumeratorDeep
    {
        public static bool IsEven(int x) { return x % 2 == 0; }
        public static bool IsBig(int x) { return x > 4; }
        public static int Dbl(int x) { return x * 2; }
        public static int Key(int x) { return x; }

        public static void Main()
        {
            SortedList sl = new SortedList();
            sl.Add("delta", "d");
            sl.Add("alpha", "a");
            sl.Add("charlie", "c");
            sl.Add("bravo", "b");

            // ===== 1. ICollection.Count through the interface =====
            ICollection ikeys = sl.Keys;
            ICollection ivals = sl.Values;
            Console.WriteLine("cnt_keys=" + (ikeys.Count == 4));
            Console.WriteLine("cnt_vals=" + (ivals.Count == 4));

            // ===== 2. foreach over the snapshot (IEnumerable wiring) =====
            string korder = "";
            foreach (object k in ikeys) korder += (string)k;
            Console.WriteLine("fe_keys=" + (korder == "alphabravocharliedelta"));
            string vorder = "";
            foreach (object v in ivals) vorder += (string)v;
            Console.WriteLine("fe_vals=" + (vorder == "abcd"));

            // ===== 3. explicit IEnumerator protocol =====
            IEnumerator en = ikeys.GetEnumerator();
            Console.WriteLine("en_mn1=" + en.MoveNext());
            Console.WriteLine("en_cur1=" + ((string)en.Current == "alpha"));
            Console.WriteLine("en_mn2=" + en.MoveNext());
            Console.WriteLine("en_cur2=" + ((string)en.Current == "bravo"));
            en.Reset();
            Console.WriteLine("en_rs=" + en.MoveNext());
            Console.WriteLine("en_rscur=" + ((string)en.Current == "alpha"));
            // exhaustion: walk to the end, then MoveNext stays false
            int n = 0;
            IEnumerator en2 = ikeys.GetEnumerator();
            while (en2.MoveNext()) n++;
            Console.WriteLine("en_walk=" + (n == 4));
            Console.WriteLine("en_exh=" + (en2.MoveNext() == false));
            Console.WriteLine("en_exh2=" + (en2.MoveNext() == false));

            // ===== 4. two independent enumerators (interleaved) =====
            IEnumerator a = ikeys.GetEnumerator();
            IEnumerator b = ikeys.GetEnumerator();
            Console.WriteLine("ind_mn=" + (a.MoveNext() && b.MoveNext()));
            Console.WriteLine("ind_a=" + ((string)a.Current == "alpha"));
            Console.WriteLine("ind_b=" + ((string)b.Current == "alpha"));
            Console.WriteLine("ind_a2=" + (a.MoveNext() && (string)a.Current == "bravo"));
            Console.WriteLine("ind_b1=" + ((string)b.Current == "alpha"));
            Console.WriteLine("ind_b2=" + (b.MoveNext() && (string)b.Current == "bravo"));

            // ===== 5. LINQ over int32[] collections =====
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            Func<int, bool> even = IsEven;
            Func<int, bool> big = IsBig;
            Func<int, int> dbl = Dbl;
            Func<int, int> key = Key;
            Console.WriteLine("l_count=" + (nums.Count(even) == 4));
            Console.WriteLine("l_where=" + (nums.Where(even).ToArray()[0] == 2));
            Console.WriteLine("l_sel=" + (nums.Select(dbl).ToArray()[0] == 10));
            Console.WriteLine("l_sum=" + (nums.Where(even).Sum() == 20));
            Console.WriteLine("l_min=" + (nums.Min() == 1));
            Console.WriteLine("l_max=" + (nums.Max() == 9));
            Console.WriteLine("l_first=" + (nums.First() == 5));
            Console.WriteLine("l_fod=" + (nums.FirstOrDefault() == 5));
            Console.WriteLine("l_any=" + (nums.Any(big)));
            Console.WriteLine("l_ob=" + (nums.OrderBy(key).ToArray()[0] == 1));
            Console.WriteLine("l_oblast=" + (nums.OrderBy(key).ToArray()[8] == 9));
        }
    }
}
