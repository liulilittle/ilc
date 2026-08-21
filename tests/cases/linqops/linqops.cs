//------------------------------------------------------------------------------
// linqops.cs -- end-to-end reference case for the System.Linq.Enumerable
// operator CHAINS (net48 oracle): Count / Any / Where / Select / OrderBy /
// ToList composed into multi-step pipelines over int32[].
//
// PLACEMENT: created in e2e batch C3 as a FORWARD GREEN GATE for the batch-23
// LINQ operator wiring (the "linqops" work order). The batch-23 wiring
// connects MethodMap.Linq.cs to the runtime linq.h / linq_map.h / linq_sort.h
// icalls (count/any/all/sum/min/max + where/select/first/to_array/to_list +
// order_by). Until that wiring lands the case FAILS at transpile (unmapped
// operators -> generated-name fallback); expected.txt is the net48 reference
// and documents the INTENDED semantics. (linqcase is the earlier single-op
// gate; linqops adds the CHAIN composition + OrderBy + ToList materializer.)
//
// DELEGATE POLICY (matches the batch-23 runtime contract, linq_map.h:19-27):
// the runtime accepts ONLY single-cast STATIC-method delegates for the
// predicate/selector callbacks; instance-bound delegates raise
// InvalidOperationException. Every callback below is therefore a STATIC
// method bound to a Func<...> delegate (the delegatecase/delegateinst-proven
// static-binding shape).
//
// EVERY line prints "label=True/False" (deterministic, hand-verifiable).
// net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LinqOps
    {
        // ---- static callback methods (runtime-compatible delegate contract) ----
        public static bool IsEven(int x) { return x % 2 == 0; }
        public static bool IsBig(int x) { return x > 4; }
        public static int Dbl(int x) { return x * 2; }
        public static int Neg(int x) { return -x; }
        public static bool IsDeepNeg(int x) { return x < -4; }
        public static int Key(int x) { return x; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            int[] small = { 1, 2 };
            int[] empty = new int[0];

            Func<int, bool> even = IsEven;
            Func<int, bool> big = IsBig;
            Func<int, int> dbl = Dbl;
            Func<int, int> neg = Neg;
            Func<int, int> key = Key;

            // ===== 1. Count: plain + predicate + empty =====
            Console.WriteLine("count=" + (nums.Count() == 9));
            Console.WriteLine("countp=" + (nums.Count(even) == 4));
            Console.WriteLine("counte=" + (empty.Count() == 0));

            // ===== 2. Any: plain + predicate + empty =====
            Console.WriteLine("any=" + (nums.Any()));
            Console.WriteLine("anyp=" + (nums.Any(big)));
            Console.WriteLine("anyne=" + (nums.Any(even) == false ? "bad" : "ok"));
            Console.WriteLine("anyfalse=" + (small.Any(big) == false));
            Console.WriteLine("anyempty=" + (empty.Any() == false));

            // ===== 3. Where -> Count / Any (filter then aggregate) =====
            Console.WriteLine("wcnt=" + (nums.Where(even).Count() == 4));
            Console.WriteLine("wany=" + (nums.Where(big).Any()));
            Console.WriteLine("wanynone=" + (small.Where(big).Any() == false));
            Console.WriteLine("wempty=" + (empty.Where(even).Count() == 0));

            // ===== 4. Where -> Select -> ToArray (two-op chain) =====
            int[] evens = nums.Where(even).ToArray();
            Console.WriteLine("we_len=" + (evens.Length == 4));
            Console.WriteLine("we0=" + (evens[0] == 2));
            Console.WriteLine("we1=" + (evens[1] == 8));
            Console.WriteLine("we2=" + (evens[2] == 4));
            Console.WriteLine("we3=" + (evens[3] == 6));

            // ===== 5. Select -> Where -> ToArray (reversed order) =====
            Func<int, bool> deepNeg = IsDeepNeg;
            int[] negBig = nums.Select(neg).Where(deepNeg).ToArray();
            Console.WriteLine("sw_len=" + (negBig.Length == 5));
            Console.WriteLine("sw0=" + (negBig[0] == -5));
            Console.WriteLine("sw1=" + (negBig[1] == -9));
            Console.WriteLine("sw4=" + (negBig[4] == -6));

            // ===== 6. Where -> Select (map after filter) =====
            int[] bigDbl = nums.Where(big).Select(dbl).ToArray();
            Console.WriteLine("wd_len=" + (bigDbl.Length == 5));
            Console.WriteLine("wd0=" + (bigDbl[0] == 10));
            Console.WriteLine("wd4=" + (bigDbl[4] == 12));

            // ===== 7. Select -> ToList (materialize via List<int>) =====
            System.Collections.Generic.List<int> lst = nums.Select(dbl).ToList();
            Console.WriteLine("tl_len=" + (lst.Count == 9));
            Console.WriteLine("tl0=" + (lst[0] == 10));
            Console.WriteLine("tl8=" + (lst[8] == 12));
            Console.WriteLine("tl_even=" + (lst[1] == 6));

            // ===== 8. OrderBy -> ToArray (stable ascending by key) =====
            int[] sorted = nums.OrderBy(key).ToArray();
            Console.WriteLine("ob_len=" + (sorted.Length == 9));
            Console.WriteLine("ob0=" + (sorted[0] == 1));
            Console.WriteLine("ob1=" + (sorted[1] == 2));
            Console.WriteLine("ob8=" + (sorted[8] == 9));
            Console.WriteLine("ob_src=" + (nums[0] == 5));   // source untouched

            // ===== 9. OrderBy -> Where chain =====
            int[] sortedEven = nums.OrderBy(key).Where(even).ToArray();
            Console.WriteLine("obw_len=" + (sortedEven.Length == 4));
            Console.WriteLine("obw0=" + (sortedEven[0] == 2));
            Console.WriteLine("obw3=" + (sortedEven[3] == 8));

            // ===== 10. Full 4-op chain: Where -> Select -> OrderBy -> ToList =====
            System.Collections.Generic.List<int> chain =
                nums.Where(big).Select(dbl).OrderBy(key).ToList();
            Console.WriteLine("chain_len=" + (chain.Count == 5));
            Console.WriteLine("chain0=" + (chain[0] == 10));
            Console.WriteLine("chain1=" + (chain[1] == 12));
            Console.WriteLine("chain4=" + (chain[4] == 18));

            // ===== 11. Empty-sequence truth table =====
            Console.WriteLine("e_count=" + (empty.Count() == 0));
            Console.WriteLine("e_any=" + (empty.Any() == false));
            Console.WriteLine("e_where=" + (empty.Where(even).Count() == 0));
            Console.WriteLine("e_sel=" + (empty.Select(dbl).Count() == 0));
            Console.WriteLine("e_ob=" + (empty.OrderBy(key).Count() == 0));
        }
    }
}
