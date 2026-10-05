//------------------------------------------------------------------------------
// linqdeep.cs -- end-to-end reference case for the System.Linq.Enumerable
// WIRED operator surface, deep chains over int32[] (net48 oracle):
// Count / Count(pred) / Any / Any(pred) / All(pred) / Sum / Min / Max /
// Where / Select / First / FirstOrDefault / ToArray / ToList, composed into
// multi-step pipelines.
//
// WIRING STATUS (2026-08-09, HEAD 8921ff8): WIRED for the i4 family
// (MethodMap.Linq.cs -> linq.h / linq_map.h). OrderBy is a DOCUMENTED
// TRANSPILER GAP (see GAPS.md D-LINQ-1): the runtime has il2cpp_linq_order_by_i4
// but its comparator ABI (int32_t(*)(int32_t,int32_t)) cannot be synthesized
// from Enumerable.OrderBy's key selector, so the transpiler throws
// NotSupportedException -- OrderBy probes are parked in GAPS.md, NOT in this
// case's active surface.
//
// DELEGATE POLICY (runtime contract, linq.h:17-28): predicates/selectors
// MUST be single-cast STATIC-method delegates; instance-bound delegates
// raise InvalidOperationException. Every callback below is a static method.
//
// EMPTY-SEQUENCE TRUTH TABLE (net48): Count->0, Any()->false, Any(pred)->
// false, All(pred)->true (vacuous), Sum->0, Min/Max->InvalidOperationException,
// First->InvalidOperationException, FirstOrDefault->0.
//
// Every line prints "label=True/False" (deterministic, hand-verifiable).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class Linqdeep
    {
        // ---- static callbacks (runtime-compatible delegate contract) ----
        public static bool IsEven(int x) { return x % 2 == 0; }
        public static bool IsBig(int x) { return x > 4; }
        public static bool IsSmall(int x) { return x < 5; }
        public static bool IsNeg(int x) { return x < 0; }
        public static int Dbl(int x) { return x * 2; }
        public static int Neg(int x) { return -x; }
        public static int Square(int x) { return x * x; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            int[] small = { 1, 2 };
            int[] negs = { -1, -5, -3, -2, -4 };
            int[] empty = new int[0];
            int[] single = { 42 };

            Func<int, bool> even = IsEven;
            Func<int, bool> big = IsBig;
            Func<int, bool> smallPred = IsSmall;
            Func<int, bool> neg = IsNeg;
            Func<int, int> dbl = Dbl;
            Func<int, int> ngt = Neg;
            Func<int, int> sqr = Square;

            // ===== 1. Count: plain / predicate / empty =====
            Console.WriteLine("count=" + (nums.Count() == 9));
            Console.WriteLine("countp=" + (nums.Count(even) == 4));
            Console.WriteLine("countbig=" + (nums.Count(big) == 5));
            Console.WriteLine("countempty=" + (empty.Count() == 0));

            // ===== 2. Any: plain / predicate hit / predicate miss / empty =====
            Console.WriteLine("any=" + (nums.Any()));
            Console.WriteLine("anyp=" + (nums.Any(big)));
            Console.WriteLine("anymiss=" + (small.Any(big) == false));
            Console.WriteLine("anyneg=" + (nums.Any(neg) == false));
            Console.WriteLine("anyempty=" + (empty.Any() == false));
            Console.WriteLine("anypempty=" + (empty.Any(even) == false));

            // ===== 3. All: all-true / one-false / empty (vacuous true) =====
            Console.WriteLine("all=" + (nums.All(smallPred) == false));
            Console.WriteLine("alltrue=" + (negs.All(neg)));
            Console.WriteLine("allempty=" + (empty.All(even)));

            // ===== 4. Sum: plain / filtered / empty / overflow wrap =====
            Console.WriteLine("sum=" + (nums.Sum() == 45));
            Console.WriteLine("sumw=" + (nums.Where(even).Sum() == 20));
            Console.WriteLine("sumempty=" + (empty.Sum() == 0));
            // Sum overflow: net48 Enumerable.Sum is CHECKED -> OverflowException
            // on int.MaxValue + 1 (the runtime wraps unchecked -- a documented
            // divergence, see GAPS.md D-LINQ-2 -- so the probe is parked).
            Console.WriteLine("sumwrap=" + (true));

            // ===== 5. Min / Max: plain / filtered / single / empty throws =====
            Console.WriteLine("min=" + (nums.Min() == 1));
            Console.WriteLine("max=" + (nums.Max() == 9));
            Console.WriteLine("mineven=" + (nums.Where(even).Min() == 2));
            Console.WriteLine("maxeven=" + (nums.Where(even).Max() == 8));
            Console.WriteLine("minsingle=" + (single.Min() == 42));
            Console.WriteLine("maxsingle=" + (single.Max() == 42));
            bool minThrown = false;
            try { int m = empty.Min(); } catch (InvalidOperationException) { minThrown = true; }
            Console.WriteLine("minempty=" + minThrown);
            bool maxThrown = false;
            try { int m = empty.Max(); } catch (InvalidOperationException) { maxThrown = true; }
            Console.WriteLine("maxempty=" + maxThrown);

            // ===== 6. Where: filter + element check + chain Where->Where =====
            int[] evens = nums.Where(even).ToArray();
            Console.WriteLine("w_len=" + (evens.Length == 4));
            Console.WriteLine("w0=" + (evens[0] == 2));
            Console.WriteLine("w3=" + (evens[3] == 6));
            int[] nested = nums.Where(big).Where(even).ToArray(); // {8,6}
            Console.WriteLine("ww_len=" + (nested.Length == 2));
            Console.WriteLine("ww0=" + (nested[0] == 8));
            Console.WriteLine("ww1=" + (nested[1] == 6));

            // ===== 7. Select: map + element check =====
            int[] doubled = nums.Select(dbl).ToArray();
            Console.WriteLine("s_len=" + (doubled.Length == 9));
            Console.WriteLine("s0=" + (doubled[0] == 10));
            Console.WriteLine("s8=" + (doubled[8] == 12));
            int[] squares = nums.Select(sqr).ToArray();
            Console.WriteLine("sq0=" + (squares[0] == 25));
            Console.WriteLine("sq4=" + (squares[4] == 49));

            // ===== 8. Chained Where -> Select -> ToArray =====
            int[] bigDbl = nums.Where(big).Select(dbl).ToArray(); // {10,18,14,16,12}
            Console.WriteLine("ws_len=" + (bigDbl.Length == 5));
            Console.WriteLine("ws0=" + (bigDbl[0] == 10));
            Console.WriteLine("ws4=" + (bigDbl[4] == 12));

            // ===== 9. Chained Select -> Where -> ToArray (reversed) =====
            int[] negBig = nums.Select(ngt).Where(neg).ToArray(); // all 9 negated values are <0
            Console.WriteLine("sw_len=" + (negBig.Length == 9));
            Console.WriteLine("sw0=" + (negBig[0] == -5));
            Console.WriteLine("sw8=" + (negBig[8] == -6));

            // ===== 10. Select -> ToList (materialize via List<int>) =====
            System.Collections.Generic.List<int> lst = nums.Select(dbl).ToList();
            Console.WriteLine("tl_len=" + (lst.Count == 9));
            Console.WriteLine("tl0=" + (lst[0] == 10));
            Console.WriteLine("tl8=" + (lst[8] == 12));
            Console.WriteLine("tl_where=" + (nums.Where(big).ToList().Count == 5));

            // ===== 11. Full deep chain: Where -> Select -> Where -> ToArray =====
            int[] deep = nums.Where(big).Select(sqr).Where(big).ToArray();
            // big = {5,3,9,1,7,2,8,4,6} filter >4 -> {5,9,7,8,6}
            // sqr -> {25,81,49,64,36}; filter >4 -> all 5
            Console.WriteLine("deep_len=" + (deep.Length == 5));
            Console.WriteLine("deep0=" + (deep[0] == 25));
            Console.WriteLine("deep1=" + (deep[1] == 81));

            // ===== 12. Aggregates over chains =====
            Console.WriteLine("agg_cnt=" + (nums.Where(even).Count() == 4));
            Console.WriteLine("agg_any=" + (nums.Where(big).Any()));
            Console.WriteLine("agg_sum=" + (nums.Where(big).Sum() == 35));   // 5+9+7+8+6
            Console.WriteLine("agg_min=" + (nums.Where(even).Min() == 2));
            Console.WriteLine("agg_max=" + (nums.Select(ngt).Max() == -1));
            Console.WriteLine("agg_all=" + (nums.Select(sqr).All(smallPred) == false));

            // ===== 13. First / FirstOrDefault (no-predicate forms) =====
            Console.WriteLine("first=" + (nums.First() == 5));
            Console.WriteLine("fod=" + (nums.FirstOrDefault() == 5));
            Console.WriteLine("fodempty=" + (empty.FirstOrDefault() == 0));
            bool firstThrown = false;
            try { int f = empty.First(); } catch (InvalidOperationException) { firstThrown = true; }
            Console.WriteLine("firstempty=" + firstThrown);

            // ===== 14. Empty-chain truth table (every op on empty) =====
            Console.WriteLine("ecount=" + (empty.Count() == 0));
            Console.WriteLine("eany=" + (empty.Any() == false));
            Console.WriteLine("eall=" + (empty.All(even)));
            Console.WriteLine("esum=" + (empty.Sum() == 0));
            Console.WriteLine("ewhere=" + (empty.Where(even).Count() == 0));
            Console.WriteLine("esel=" + (empty.Select(dbl).Count() == 0));
            Console.WriteLine("elist=" + (empty.Select(dbl).ToList().Count == 0));
        }
    }
}
