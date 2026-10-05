//------------------------------------------------------------------------------
// linqgroup.cs -- LINQ GROUPING / AGGREGATION e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C58, AFTER the LINQ operator
// chain is fully wired (linqdeep3 C39 instance-bound, C56-era Take/Skip/
// OrderBy) and PASSing.
//
// THIS case drives the AGGREGATE family (Sum/Min/Max) and the grouping
// PATTERN on top of the wired surface: Count/Any/All/Sum/Min/Max/Where/
// Select/OrderBy/Take/Skip/First/FirstOrDefault/ToArray/ToList. The title
// operator Enumerable.GroupBy is NOT wired (D-LINQ-4 in GAPS.md -- it would
// abort the transpile), so the "grouping" semantics are exercised through
// FIXED-KEY BUCKET AGGREGATION -- the closest reachable shape:
//
//   1) Aggregate truth tables: Sum/Min/Max on nums/negs/mixed/single/empty;
//      Sum checked-overflow (net48 + runtime both throw OverflowException);
//      Min/Max on int.MinValue/int.MaxValue present; empty Min/Max ->
//      InvalidOperationException.
//   2) Filter-then-aggregate: Where(pred).Sum()/Min()/Max()/Count() across
//      parity, magnitude and modulus predicates.
//   3) Select-then-aggregate: Select(sel).Sum()/Min()/Max() and the
//      Where->Select, Select->Where combinations.
//   4) OrderBy combinations: OrderBy(int key).First()/Take(n).Sum()/ToArray()
//      with ascending and negated (descending) keys; OrderBy then filter.
//   5) Take/Skip slice semantics: negative/zero/oversized counts, slices then
//      aggregates, slice-then-filter.
//   6) Materialization: Where/OrderBy/Select -> ToArray() / ToList() with
//      index + Count reads.
//   7) GROUPING PATTERN (fixed-key buckets): even/odd and mod-3 buckets --
//      per-bucket Count + Sum over the same source (the GroupBy aggregation
//      shape with literal keys).
//   8) Empty-sequence truth table: Count 0, Any false, Sum 0, All vacuous
//      true, Count(pred) 0, FirstOrDefault 0, First/Min/Max throw,
//      Take/Skip empty.
//   9) Exception contracts + instance-bound (linqib) aggregate differential.
//
// Every line prints "label=True/False"; net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    // instance-bound predicate source (linqib mode -- linqdeep3 precedent).
    public class LinqPred
    {
        public int Hi;
        public LinqPred(int hi) { Hi = hi; }
        public bool IsBig(int x) { return x > Hi; }
    }

    public static class LinqGroup
    {
        // static helpers (the single-cast STATIC methodPtr ABI).
        private static bool IsEven(int x) { return x % 2 == 0; }
        private static bool IsOdd(int x) { return (x & 1) == 1; }
        private static bool IsBig(int x) { return x > 4; }
        private static bool IsSmall(int x) { return x < 4; }
        private static bool IsNeg(int x) { return x < 0; }
        private static bool Mod3_0(int x) { return x % 3 == 0; }
        private static bool Mod3_1(int x) { return x % 3 == 1; }
        private static bool Mod3_2(int x) { return x % 3 == 2; }
        private static int Identity(int x) { return x; }
        private static int NegKey(int x) { return -x; }
        private static int Dbl(int x) { return x * 2; }
        private static int Mod2(int x) { return x & 1; }
        private static int Mod3(int x) { return x % 3; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            int[] negs = { -1, -5, -3, -2, -4 };
            int[] single = { 42 };
            int[] empty = new int[0];
            LinqPred p4 = new LinqPred(4);
            LinqPred p6 = new LinqPred(6);

            // ===== 1. aggregate truth tables =====
            Console.WriteLine("s_nums=" + (nums.Sum() == 45));
            Console.WriteLine("s_negs=" + (negs.Sum() == -15));
            Console.WriteLine("s_single=" + (single.Sum() == 42));
            Console.WriteLine("s_empty=" + (empty.Sum() == 0));
            Console.WriteLine("mn_nums=" + (nums.Min() == 1 && nums.Max() == 9));
            Console.WriteLine("mn_negs=" + (negs.Min() == -5 && negs.Max() == -1));
            Console.WriteLine("mn_single=" + (single.Min() == 42 && single.Max() == 42));
            Console.WriteLine("mn_bounds=" + (new int[] { int.MinValue, 0, int.MaxValue }.Min() == int.MinValue
                && new int[] { int.MinValue, 0, int.MaxValue }.Max() == int.MaxValue));
            bool sumOverflow = false;
            try { int x = new int[] { int.MaxValue, 1 }.Sum(); } catch (OverflowException) { sumOverflow = true; }
            Console.WriteLine("s_overflow=" + sumOverflow);
            bool emptyMinThrow = false, emptyMaxThrow = false;
            try { int a = empty.Min(); } catch (InvalidOperationException) { emptyMinThrow = true; }
            try { int b = empty.Max(); } catch (InvalidOperationException) { emptyMaxThrow = true; }
            Console.WriteLine("mn_empty_throw=" + (emptyMinThrow && emptyMaxThrow));

            // ===== 2. filter-then-aggregate =====
            Console.WriteLine("fa_even_sum=" + (nums.Where(IsEven).Sum() == 20));
            Console.WriteLine("fa_odd_sum=" + (nums.Where(IsOdd).Sum() == 25));
            Console.WriteLine("fa_even_cnt=" + (nums.Where(IsEven).Count() == 4));
            Console.WriteLine("fa_big_cnt=" + (nums.Where(IsBig).Count() == 5));
            Console.WriteLine("fa_big_mn=" + (nums.Where(IsBig).Min() == 5 && nums.Where(IsBig).Max() == 9));
            Console.WriteLine("fa_small_sum=" + (nums.Where(IsSmall).Sum() == 6));
            Console.WriteLine("fa_neg_sum=" + (negs.Where(IsOdd).Sum() == -9));
            Console.WriteLine("fa_m3_sum=" + (nums.Where(Mod3_0).Sum() == 18));
            Console.WriteLine("fa_empty_where=" + (nums.Where(IsBig).Where(x => x > 100).Sum() == 0));

            // ===== 3. select-then-aggregate =====
            Console.WriteLine("sa_sum=" + (nums.Select(Dbl).Sum() == 90));
            Console.WriteLine("sa_mn=" + (nums.Select(Dbl).Min() == 2 && nums.Select(Dbl).Max() == 18));
            Console.WriteLine("sa_we=" + (nums.Where(IsEven).Select(Dbl).Sum() == 40));
            Console.WriteLine("sa_sw=" + (nums.Select(Dbl).Where(IsBig).Sum() == 84));
            Console.WriteLine("sa_sw_cnt=" + (nums.Select(Dbl).Where(IsBig).Count() == 7));

            // ===== 4. OrderBy combinations =====
            Console.WriteLine("ob_first=" + (nums.OrderBy(Identity).First() == 1));
            Console.WriteLine("ob_fod=" + (nums.OrderBy(Identity).FirstOrDefault() == 1));
            Console.WriteLine("ob_arr0=" + (nums.OrderBy(Identity).ToArray()[0] == 1));
            Console.WriteLine("ob_sum=" + (nums.OrderBy(Identity).Sum() == 45));
            Console.WriteLine("ob_take3=" + (nums.OrderBy(Identity).Take(3).Sum() == 6));
            Console.WriteLine("ob_desc_first=" + (nums.OrderBy(NegKey).First() == 9));
            Console.WriteLine("ob_desc_take2=" + (nums.OrderBy(NegKey).Take(2).Sum() == 17));
            Console.WriteLine("ob_negs=" + (negs.OrderBy(Identity).First() == -5));
            Console.WriteLine("ob_where_sum=" + (nums.OrderBy(Identity).Where(IsEven).Sum() == 20));

            // ===== 5. Take / Skip slice semantics =====
            Console.WriteLine("tk_3=" + (nums.Take(3).Count() == 3 && nums.Take(3).Sum() == 17));
            Console.WriteLine("tk_4=" + (nums.Take(4).Sum() == 18));
            Console.WriteLine("tk_0=" + (nums.Take(0).Count() == 0));
            Console.WriteLine("tk_neg=" + (nums.Take(-1).Count() == 0));
            Console.WriteLine("tk_big=" + (nums.Take(100).Count() == 9));
            Console.WriteLine("sk_2=" + (nums.Skip(2).Count() == 7));
            Console.WriteLine("sk_neg=" + (nums.Skip(-1).Count() == 9));
            Console.WriteLine("sk_big=" + (nums.Skip(100).Count() == 0));
            Console.WriteLine("sk_tk=" + (nums.Skip(2).Take(3).Sum() == 17));
            Console.WriteLine("sk_where=" + (nums.Skip(2).Where(IsEven).Sum() == 20));
            Console.WriteLine("tk_where_empty=" + (nums.Take(5).Where(IsEven).Sum() == 0));

            // ===== 6. materialization =====
            Console.WriteLine("mat_wa=" + (nums.Where(IsEven).ToArray().Length == 4 && nums.Where(IsEven).ToArray()[0] == 2));
            Console.WriteLine("mat_oa=" + (nums.OrderBy(Identity).ToArray()[1] == 2));
            Console.WriteLine("mat_sel=" + (nums.Select(Dbl).ToArray()[4] == 14));
            Console.WriteLine("mat_wl=" + (nums.Where(IsEven).ToList().Count == 4 && nums.Where(IsEven).ToList()[1] == 8));
            Console.WriteLine("mat_list=" + (nums.ToList().Count == 9 && nums.ToList()[0] == 5));

            // ===== 7. GROUPING PATTERN: fixed-key bucket aggregation =====
            // (GroupBy<int,int> is unwired -- D-LINQ-4; the bucket-count /
            // bucket-sum aggregation shape with literal keys is the reachable
            // equivalent.)
            Console.WriteLine("grp_eo=" + (nums.Where(IsEven).Count() == 4 && nums.Where(IsOdd).Count() == 5));
            Console.WriteLine("grp_eo_sum=" + (nums.Where(IsEven).Sum() == 20 && nums.Where(IsOdd).Sum() == 25));
            Console.WriteLine("grp_m0=" + (nums.Where(Mod3_0).Count() == 3 && nums.Where(Mod3_0).Sum() == 18));
            Console.WriteLine("grp_m1=" + (nums.Where(Mod3_1).Count() == 3 && nums.Where(Mod3_1).Sum() == 12));
            Console.WriteLine("grp_m2=" + (nums.Where(Mod3_2).Count() == 3 && nums.Where(Mod3_2).Sum() == 15));
            Console.WriteLine("grp_total=" + (nums.Where(Mod3_0).Sum() + nums.Where(Mod3_1).Sum() + nums.Where(Mod3_2).Sum() == nums.Sum()));
            Console.WriteLine("grp_neg=" + (negs.Where(IsNeg).Count() == 5 && negs.Where(x => x > 0).Count() == 0));

            // ===== 7b. GroupBy direct usage (D-LINQ-4) =====
            // The transpiler maps GroupBy(source, keySelector) to the runtime
            // il2cpp_linq_group_by_i4 (eager materialization of the parity /
            // mod-3 buckets); net48 DEFERS the work until enumeration. With a
            // side-effect-free key selector both agree: the call executes and
            // returns a grouping result object (non-null) for a non-empty and
            // an empty source alike. The bucket-level Count/Sum/order are
            // driven in the runtime unit test (test_linq_map.cpp section 14)
            // because the stage-1 transpiler has no grouping-enumerator wiring
            // (TryMapLinqGrouping is not emitted -- MethodMap.Linq.cs).
            var grpParity = nums.GroupBy(Mod2);
            Console.WriteLine("gb_parity_null=" + (grpParity != null));
            var grpMod3 = nums.GroupBy(Mod3);
            Console.WriteLine("gb_mod3_null=" + (grpMod3 != null));
            var grpEmpty = empty.GroupBy(Mod2);
            Console.WriteLine("gb_empty_null=" + (grpEmpty != null));

            // ===== 8. empty-sequence truth table =====
            Console.WriteLine("em_cnt=" + (empty.Count() == 0));
            Console.WriteLine("em_sum=" + (empty.Sum() == 0));
            Console.WriteLine("em_any=" + (empty.Any() == false));
            Console.WriteLine("em_all=" + (empty.All(IsOdd) == true));
            Console.WriteLine("em_cntp=" + (empty.Count(IsEven) == 0));
            Console.WriteLine("em_anyp=" + (empty.Any(IsEven) == false));
            Console.WriteLine("em_fod=" + (empty.FirstOrDefault() == 0));
            bool emFirstThrow = false;
            try { int x = empty.First(); } catch (InvalidOperationException) { emFirstThrow = true; }
            Console.WriteLine("em_first_throw=" + emFirstThrow);
            Console.WriteLine("em_tk=" + (empty.Take(3).Count() == 0 && empty.Skip(2).Count() == 0));
            Console.WriteLine("em_arr=" + (empty.ToArray().Length == 0));
            Console.WriteLine("em_list=" + (empty.ToList().Count == 0));

            // ===== 9. exceptions + instance-bound differential =====
            bool aneSrc = false;
            try { int x = ((int[])null).Count(); } catch (ArgumentNullException) { aneSrc = true; }
            Console.WriteLine("ex_null_src=" + aneSrc);
            bool anePred = false;
            try { int y = nums.Count((Func<int, bool>)null); } catch (ArgumentNullException) { anePred = true; }
            Console.WriteLine("ex_null_pred=" + anePred);
            Console.WriteLine("ib_cnt4=" + (nums.Count(p4.IsBig) == 5));
            Console.WriteLine("ib_cnt6=" + (nums.Count(p6.IsBig) == 3));
            Console.WriteLine("ib_where_sum=" + (nums.Where(p4.IsBig).Sum() == 35));
            Console.WriteLine("ib_where_sum6=" + (nums.Where(p6.IsBig).Sum() == 24));
            Console.WriteLine("ib_diff=" + (nums.Count(p4.IsBig) != nums.Count(p6.IsBig)));
        }
    }
}
