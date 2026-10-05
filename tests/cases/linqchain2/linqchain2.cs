//------------------------------------------------------------------------------
// linqchain2.cs -- LINQ CHAIN DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C40, AFTER the FULL LINQ chain is
// wired at HEAD: the operator surface (linqops C3), the static-callback
// chains (linqdeep C15), the enumerator surface (enumeratordeep C38) and
// the instance-bound predicate/selector surface (linqdeep3 C39) are all
// byte-exact. THIS case drives the OPERATOR-COMPOSITION matrix none of the
// predecessors covered: OrderBy composed with Where and Select in every
// order, with INSTANCE-BOUND predicates AND INSTANCE-BOUND KEY SELECTORS
// INTERLEAVED with static callbacks mid-chain (the linq_map.cpp linqib
// mode -- target in the `this` slot -- and the linq_sort.h instance-bound
// key-selector acceptance, linq_sort.h:82, the D-LINQ-1 ABI bridge):
//
//   1. Where+OrderBy+Select FULL pipelines (instance predicate + static
//      key + instance selector, and every other interleave).
//   2. OrderBy with INSTANCE-BOUND KEY SELECTORS (p4.KeyParity /
//      p4.KeyModHi) -- the key-selector target-passing contract.
//   3. INSTANCE-STATE DIFFERENTIAL ORDERING: p4.KeyModHi (x % 4) vs
//      p6.KeyModHi (x % 6) on the SAME array must produce DIFFERENT orders
//      -- the same probe idea as linqdeep3's Count/Any/First, extended to
//      the ORDER BY key.
//   4. STABILITY: equal keys keep source order (net48 OrderBy is stable) --
//      probed with the index-marked {21,10,20,11} marker array (key = x/10).
//   5. empty / single / no-mutation-of-source edges.
//   6. chains TERMINATING in Count/Any/All/First/FirstOrDefault/Sum/Min/Max
//      after a sort.
//   7. double OrderBy (the second sort dominates) + ToList materializer at
//      the chain end.
//
// Every line prints "label=True/False". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    // instance-method predicate/key-selector source: bound to Func delegates,
    // with per-instance STATE (Hi) so the target-passing contract is observable.
    public class LinqKeyPred
    {
        public int Hi;
        public LinqKeyPred(int hi) { Hi = hi; }
        public bool IsEven(int x) { return x % 2 == 0; }
        public bool IsBig(int x) { return x > Hi; }
        public bool IsSmall(int x) { return x < Hi; }
        public bool IsNeg(int x) { return x < 0; }
        public int Dbl(int x) { return x * 2; }
        public int KeyVal(int x) { return x; }
        public int KeyParity(int x) { return x % 2; }
        public int KeyModHi(int x) { return x % Hi; }
    }

    public static class LinqChain2
    {
        // ---- static callback methods (the linqops/linqdeep static contract) ----
        public static bool StBig(int x) { return x > 4; }
        public static bool StEven(int x) { return x % 2 == 0; }
        public static int StDbl(int x) { return x * 2; }
        public static int StKey(int x) { return x; }
        public static int StNegKey(int x) { return -x; }
        public static int StModKey(int x) { return x % 3; }
        public static int StDiv10(int x) { return x / 10; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            int[] negs = { -1, -5, -3, -2, -4 };
            int[] empty = new int[0];
            int[] single = { 42 };
            int[] marker = { 21, 10, 20, 11 };

            LinqKeyPred p4 = new LinqKeyPred(4);
            LinqKeyPred p6 = new LinqKeyPred(6);
            Func<int, bool> even = p4.IsEven;
            Func<int, bool> big = p4.IsBig;
            Func<int, bool> big6 = p6.IsBig;
            Func<int, bool> smallPred = p4.IsSmall;
            Func<int, bool> neg = p4.IsNeg;
            Func<int, int> dbl = p4.Dbl;
            Func<int, int> keyVal = p4.KeyVal;
            Func<int, int> keyParity = p4.KeyParity;
            Func<int, int> modHi4 = p4.KeyModHi;
            Func<int, int> modHi6 = p6.KeyModHi;

            // ===== 1. Where+OrderBy+Select full pipelines =====
            Console.WriteLine("wos_len=" + (nums.Where(big).OrderBy(StKey).Select(dbl).Count() == 5));
            Console.WriteLine("wos_first=" + (nums.Where(big).OrderBy(StKey).Select(dbl).First() == 10));
            Console.WriteLine("wos_last=" + (nums.Where(big).OrderBy(StKey).Select(dbl).ToArray()[4] == 18));
            Console.WriteLine("wos_sum=" + (nums.Where(big).OrderBy(StKey).Select(dbl).Sum() == 70));
            Console.WriteLine("wos_even_first=" + (nums.Where(even).OrderBy(StKey).Select(dbl).First() == 4));
            Console.WriteLine("wos_agg=" + (nums.Where(big).OrderBy(StKey).Select(dbl).Count() == 5 &&
                                             nums.Where(even).OrderBy(StKey).Select(dbl).First() == 4 &&
                                             nums.Where(smallPred).OrderBy(StKey).Select(dbl).First() == 2));

            // ===== 2. OrderBy -> Select -> Where (order swapped) =====
            Console.WriteLine("osw_count=" + (nums.OrderBy(StKey).Select(dbl).Where(big6).Count() == 6));
            Console.WriteLine("osw_first=" + (nums.OrderBy(StKey).Select(dbl).Where(big6).First() == 8));
            Console.WriteLine("osw_sum=" + (nums.OrderBy(StKey).Select(dbl).Where(big6).Sum() == 78));
            Console.WriteLine("osw_empty=" + (empty.OrderBy(StKey).Select(dbl).Where(big).Count() == 0));

            // ===== 3. Select -> OrderBy (selector before sort) =====
            Console.WriteLine("so_first=" + (nums.Select(dbl).OrderBy(StNegKey).First() == 18));
            Console.WriteLine("so_len=" + (nums.Select(dbl).OrderBy(StNegKey).Count() == 9));
            Console.WriteLine("so_last=" + (nums.Select(dbl).OrderBy(StNegKey).ToArray()[8] == 2));
            Console.WriteLine("so_first_mod=" + (nums.Select(dbl).OrderBy(StModKey).First() == 6));

            // ===== 4. OrderBy with instance-bound key selectors =====
            int[] byParity = nums.OrderBy(keyParity).ToArray();
            Console.WriteLine("ob_parity_len=" + (byParity.Length == 9));
            Console.WriteLine("ob_parity_0=" + (byParity[0] == 2));
            Console.WriteLine("ob_parity_3=" + (byParity[3] == 6));
            Console.WriteLine("ob_parity_4=" + (byParity[4] == 5));
            Console.WriteLine("ob_parity_8=" + (byParity[8] == 7));
            Console.WriteLine("ob_instkey_staticpred=" + (nums.OrderBy(keyVal).Where(StBig).First() == 5));
            Console.WriteLine("ob_statickey_instpred=" + (nums.OrderBy(StKey).Where(big).First() == 5));

            // ===== 5. stability: equal keys keep source order =====
            int[] byMod = nums.OrderBy(StModKey).ToArray();
            Console.WriteLine("st_len=" + (byMod.Length == 9));
            Console.WriteLine("st_0=" + (byMod[0] == 3));
            Console.WriteLine("st_2=" + (byMod[2] == 6));
            Console.WriteLine("st_3=" + (byMod[3] == 1));
            Console.WriteLine("st_8=" + (byMod[8] == 8));
            int[] byDiv = marker.OrderBy(StDiv10).ToArray();
            Console.WriteLine("st_marker=" + (byDiv[0] == 10 && byDiv[1] == 11 && byDiv[2] == 21 && byDiv[3] == 20));

            // ===== 6. instance-state differential ORDERING =====
            int[] diff4 = nums.OrderBy(modHi4).ToArray();
            int[] diff6 = nums.OrderBy(modHi6).ToArray();
            Console.WriteLine("diff4_0=" + (diff4[0] == 8));
            Console.WriteLine("diff6_0=" + (diff6[0] == 6));
            Console.WriteLine("diff6_8=" + (diff6[8] == 5));
            Console.WriteLine("diff_order_differs=" + (diff4[0] != diff6[0]));

            // ===== 7. empty / single / source immutability =====
            Console.WriteLine("edge_empty=" + (empty.OrderBy(StKey).Count() == 0));
            Console.WriteLine("edge_empty_fod=" + (empty.OrderBy(StKey).FirstOrDefault() == 0));
            Console.WriteLine("edge_single=" + (single.OrderBy(StKey).ToArray()[0] == 42));
            Console.WriteLine("edge_src_untouched=" + (nums.Length == 9 && nums[0] == 5 && nums[8] == 6));

            // ===== 8. chains terminating in downstream operators =====
            Console.WriteLine("term_first=" + (nums.Where(even).OrderBy(StNegKey).First() == 8));
            Console.WriteLine("term_fod=" + (nums.OrderBy(StKey).Where(big6).FirstOrDefault() == 7));
            Console.WriteLine("term_count=" + (nums.Where(big).OrderBy(StKey).Where(even).Count() == 2));
            Console.WriteLine("term_any=" + (nums.Where(even).OrderBy(StKey).Any()));
            Console.WriteLine("term_any_pred=" + (nums.Where(even).OrderBy(StKey).Any(big6)));
            Console.WriteLine("term_all=" + (negs.OrderBy(StKey).All(neg)));
            Console.WriteLine("term_all_false=" + (nums.OrderBy(StKey).All(even) == false));
            Console.WriteLine("term_sum=" + (nums.OrderBy(StKey).Where(even).Sum() == 20));
            Console.WriteLine("term_minmax=" + (nums.OrderBy(StNegKey).Min() == 1 && nums.OrderBy(StNegKey).Max() == 9));
            Console.WriteLine("term_agg=" + (nums.Where(even).OrderBy(StKey).Where(big).Sum() == 14));
            Console.WriteLine("term_double_order=" + (nums.OrderBy(StModKey).OrderBy(StNegKey).First() == 9));

            // ===== 9. ToList materializer at the chain end =====
            var lst = nums.Where(big).OrderBy(StKey).ToList();
            Console.WriteLine("tolist_count=" + (lst.Count == 5));
            Console.WriteLine("tolist_0=" + (lst[0] == 5));
            Console.WriteLine("tolist_2=" + (lst[2] == 7));
            Console.WriteLine("tolist_4=" + (lst[4] == 9));

            // ===== 10. static/instance interleave across operators =====
            int[] mix1 = nums.Where(even).OrderBy(StNegKey).Select(dbl).ToArray();
            Console.WriteLine("mix1_0=" + (mix1[0] == 16));
            Console.WriteLine("mix1_3=" + (mix1[3] == 4));
            int[] mix2 = nums.OrderBy(StModKey).Where(big).Select(StDbl).ToArray();
            Console.WriteLine("mix2_len=" + (mix2.Length == 5));
            Console.WriteLine("mix2_0=" + (mix2[0] == 18));
            Console.WriteLine("mix2_4=" + (mix2[4] == 16));
            int[] mix3 = nums.Where(smallPred).OrderBy(keyVal).Select(dbl).ToArray();
            Console.WriteLine("mix3_0=" + (mix3[0] == 2));
            Console.WriteLine("mix3_2=" + (mix3[2] == 6));
            int[] mix4 = nums.Where(StBig).OrderBy(keyParity).Where(big).Select(StDbl).ToArray();
            Console.WriteLine("mix4_len=" + (mix4.Length == 5));
            Console.WriteLine("mix4_0=" + (mix4[0] == 16));
            Console.WriteLine("mix4_1=" + (mix4[1] == 12));
            Console.WriteLine("mix4_4=" + (mix4[4] == 14));
        }
    }
}
