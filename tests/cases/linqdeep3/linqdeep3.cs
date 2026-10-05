//------------------------------------------------------------------------------
// linqdeep3.cs -- LINQ DEEP 3 e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C39, AFTER the LINQ chain is fully wired (linqops
// PASS byte-exact, commit a0317d9; linqdeep C15 + enumeratordeep C38 already
// cover the STATIC-method callback surface). THIS case drives the INSTANCE-
// BOUND callback surface (the linqcase shape p.IsEven / p.IsBig, runtime
// linq_map.cpp "linqib" mode -- a target-carrying single-cast delegate is
// dispatched with its stored target in the generated instance method's `this`
// slot) across the four aggregate operators, FULL COMBINATION:
//
//   operator x {plain, predicate} x {nums, small, negs, empty, single} x
//   instance-bound predicate, where the instance carries STATE (two LinqPred
//   instances p4/p6 with different Hi thresholds -> same operator on the same
//   array must return DIFFERENT results -- proving the target is really
//   passed, not a static predicate renamed).
//
//   1. Count()  / Count(pred)  -- plain / instance-bound / empty / single.
//   2. Any()    / Any(pred)    -- plain / hit / miss / neg / empty / single.
//   3. All(pred)               -- all-true / one-false / empty (vacuous) /
//      single.
//   4. First() / First(pred) / FirstOrDefault() / FirstOrDefault(pred) --
//      plain / instance-bound hit / no-match throw / no-match default /
//      empty throw / empty default / single.
//   5. CHAINS with instance-bound callbacks THROUGHOUT: Where->Where,
//      Where->Select, Select->Where, Where->Count / Any / All / First /
//      FirstOrDefault -- the runtime's fresh int32[] result tracked for the
//      downstream operator.
//   6. INSTANCE-STATE DIFFERENTIAL: p4.IsBig (x>4) vs p6.IsBig (x>6) on the
//      SAME array give different Count/Any/First -- the distinguishing probe
//      for the linqib target-passing contract.
//
// EMPTY-SEQUENCE TRUTH TABLE (net48, instance-bound predicates): Count->0,
// Any()->false, Any(pred)->false, All(pred)->true (vacuous -- the predicate
// is NEVER called), First(pred)->InvalidOperationException,
// FirstOrDefault(pred)->0.
//
// Every line prints "label=True/False". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    // instance-method predicate/mapper source: bound to Func delegates, with
    // per-instance STATE (Hi) so the target-passing contract is observable.
    public class LinqPred
    {
        public int Hi;
        public LinqPred(int hi) { Hi = hi; }
        public bool IsEven(int x) { return x % 2 == 0; }
        public bool IsBig(int x) { return x > Hi; }
        public bool IsSmall(int x) { return x < Hi; }
        public bool IsNeg(int x) { return x < 0; }
        public int Dbl(int x) { return x * 2; }
    }

    public static class LinqDeep3
    {
        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            int[] small = { 1, 2 };
            int[] negs = { -1, -5, -3, -2, -4 };
            int[] empty = new int[0];
            int[] single = { 42 };

            LinqPred p4 = new LinqPred(4);
            LinqPred p6 = new LinqPred(6);
            Func<int, bool> even = p4.IsEven;
            Func<int, bool> big = p4.IsBig;      // x > 4
            Func<int, bool> big6 = p6.IsBig;     // x > 6
            Func<int, bool> smallPred = p4.IsSmall;
            Func<int, bool> neg = p4.IsNeg;
            Func<int, int> dbl = p4.Dbl;

            // ===== 1. Count: plain / instance-bound / empty / single =====
            Console.WriteLine("c_plain=" + (nums.Count() == 9));
            Console.WriteLine("c_even=" + (nums.Count(even) == 4));
            Console.WriteLine("c_big=" + (nums.Count(big) == 5));
            Console.WriteLine("c_big6=" + (nums.Count(big6) == 3));
            Console.WriteLine("c_empty=" + (empty.Count() == 0));
            Console.WriteLine("c_epred=" + (empty.Count(even) == 0));
            Console.WriteLine("c_small=" + (small.Count(big) == 0));
            Console.WriteLine("c_single=" + (single.Count(even) == 1));

            // ===== 2. Any: plain / hit / miss / neg / empty / single =====
            Console.WriteLine("a_plain=" + (nums.Any()));
            Console.WriteLine("a_big=" + (nums.Any(big)));
            Console.WriteLine("a_big6=" + (nums.Any(big6)));
            Console.WriteLine("a_miss=" + (small.Any(big) == false));
            Console.WriteLine("a_neg=" + (nums.Any(neg) == false));
            Console.WriteLine("a_empty=" + (empty.Any() == false));
            Console.WriteLine("a_epred=" + (empty.Any(even) == false));
            Console.WriteLine("a_single=" + (single.Any(smallPred) == false));

            // ===== 3. All: all-true / one-false / empty (vacuous) / single =====
            Console.WriteLine("al_true=" + (negs.All(neg)));
            Console.WriteLine("al_false=" + (nums.All(smallPred) == false));
            Console.WriteLine("al_empty=" + (empty.All(even)));
            Console.WriteLine("al_single=" + (single.All(big)));
            Console.WriteLine("al_mixed=" + (nums.All(big6) == false));

            // ===== 4. First / FirstOrDefault: plain + instance-bound =====
            Console.WriteLine("f_plain=" + (nums.First() == 5));
            Console.WriteLine("f_even=" + (nums.First(even) == 2));
            Console.WriteLine("f_big=" + (nums.First(big) == 5));
            Console.WriteLine("f_big6=" + (nums.First(big6) == 9));
            bool fNoneThrow = false;
            try { int x = small.First(big); } catch (InvalidOperationException) { fNoneThrow = true; }
            Console.WriteLine("f_none_throws=" + fNoneThrow);
            bool fEmptyThrow = false;
            try { int y = empty.First(); } catch (InvalidOperationException) { fEmptyThrow = true; }
            Console.WriteLine("f_empty_throws=" + fEmptyThrow);
            Console.WriteLine("fod_plain=" + (nums.FirstOrDefault() == 5));
            Console.WriteLine("fod_even=" + (nums.FirstOrDefault(even) == 2));
            Console.WriteLine("fod_big6=" + (nums.FirstOrDefault(big6) == 9));
            Console.WriteLine("fod_none=" + (small.FirstOrDefault(big) == 0));
            Console.WriteLine("fod_empty=" + (empty.FirstOrDefault() == 0));
            Console.WriteLine("fod_empty_pred=" + (empty.FirstOrDefault(even) == 0));
            Console.WriteLine("fod_single=" + (single.FirstOrDefault() == 42));

            // ===== 5. chains with instance-bound callbacks throughout =====
            int[] evens = nums.Where(even).ToArray();
            Console.WriteLine("ch_where=" + (evens.Length == 4 && evens[0] == 2));
            Console.WriteLine("ch_ww=" + (nums.Where(big).Where(even).Count() == 2));
            Console.WriteLine("ch_ws=" + (nums.Where(big).Select(dbl).ToArray()[0] == 10));
            Console.WriteLine("ch_sw=" + (nums.Select(dbl).Where(even).Count() == 9));
            Console.WriteLine("ch_count=" + (nums.Where(even).Count() == 4));
            Console.WriteLine("ch_any=" + (nums.Where(big).Any()));
            Console.WriteLine("ch_any_pred=" + (nums.Where(big).Any(even)));
            Console.WriteLine("ch_all=" + (negs.Select(dbl).All(neg)));
            Console.WriteLine("ch_all_even=" + (nums.Select(dbl).All(even)));
            Console.WriteLine("ch_first=" + (nums.Select(dbl).First() == 10));
            Console.WriteLine("ch_first_pred=" + (nums.Where(even).First(big) == 8));
            Console.WriteLine("ch_fod_pred=" + (nums.Where(smallPred).FirstOrDefault(even) == 2));
            Console.WriteLine("ch_agg=" + (nums.Where(big).Count() == 5 && nums.Where(even).Any() && nums.Select(dbl).All(even)));

            // ===== 6. instance-state differential (target-passing contract) =====
            Console.WriteLine("state_count4=" + (nums.Count(big) == 5));
            Console.WriteLine("state_count6=" + (nums.Count(big6) == 3));
            Console.WriteLine("state_any4=" + (nums.Any(big)));
            Console.WriteLine("state_any6=" + (nums.Any(big6)));
            Console.WriteLine("state_first4=" + (nums.First(big) == 5));
            Console.WriteLine("state_first6=" + (nums.First(big6) == 9));
            Console.WriteLine("state_diff=" + (nums.Count(big) != nums.Count(big6)));
        }
    }
}
