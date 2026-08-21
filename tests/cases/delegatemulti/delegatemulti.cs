//------------------------------------------------------------------------------
// delegatemulti.cs -- MULTICAST DELEGATE DEEP probe 4 (e2e batch C74): the
// 4+ leaf COMBINATION MATRIX and the REMOVE SURFACE over longer chains --
// byte-exact vs the net48 oracle. Extends delegateops/delegatecase/
// delegatechain/delegate2 over the transpiler-safe custom-delegate surface
// (int Fn(int) -- the icall_multicast_invoke_i4 fast path).
//
// net48-shape discipline (delegateext.cpp, verified against the reference):
//   * Combine is pure concatenation -- NO dedup (a+a length 2), invocation
//     order = insertion order, last result wins.
//   * Remove drops the LAST occurrence of the value's CONTIGUOUS sub-list
//     and matches by VALUE (method + target), not object identity; absent
//     removal returns the ORIGINAL object (identity); removing the whole
//     chain yields null.
//   * Delegate.Equals compares flattened invocation LISTS structurally
//     (order matters, build shape does not: (a+b)+c == a+(b+c)).
//   * A multicast's Target is the LAST leaf's target.
//
// THIS case drives two axes deeper than delegateops:
//   A. COMBINATION MATRIX on 4-6 leaves: every parenthesization of a+b+c+d,
//      chains built by += vs +, re-combining already-combined values
//      (ab+c vs a+bc), equality/identity across shapes, GetInvocationList
//      leaf identity after each build.
//   B. REMOVE SURFACE: removing EVERY position of a 4-chain, removing a
//      MULTI-LEAF value from every window of a 5-chain, LAST-occurrence
//      removal when the value repeats (a+b+a+a), removal of a nested-tree
//      value, non-contiguous value -> identity (no removal), whole-chain
//      removal, and the surviving chain's order/last-wins after each edit.
//
// Out of scope (recorded, NOT probed): void/ref/non-int32 multicast
// invocation, Delegate.RemoveAll (loud rejection), instance-bound leaves
// (covered by delegateinst).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class DelegateMulti
    {
        public delegate int Fn(int x);

        private static int order = 0;
        private static int A(int x) { order++; return x + 1; }
        private static int B(int x) { order++; return x * 10; }
        private static int C(int x) { order++; return x - 5; }
        private static int D(int x) { order++; return x + 100; }

        private static string Len(Fn f) { return f.GetInvocationList().Length.ToString(); }
        private static bool Ref0(Fn f, Fn leaf) { return ReferenceEquals(f.GetInvocationList()[0], leaf); }
        private static bool Ref1(Fn f, Fn leaf) { return ReferenceEquals(f.GetInvocationList()[1], leaf); }
        private static bool Ref2(Fn f, Fn leaf) { return ReferenceEquals(f.GetInvocationList()[2], leaf); }
        private static bool Ref3(Fn f, Fn leaf) { return ReferenceEquals(f.GetInvocationList()[3], leaf); }

        public static void Main()
        {
            Fn a = A, b = B, c = C, d = D;

            // ============ A1: 4-leaf chain, build by += ============
            Fn abcd = a + b + c + d;
            Console.WriteLine("abcd_len=" + (abcd.GetInvocationList().Length == 4));
            Console.WriteLine("abcd_order0=" + Ref0(abcd, a));
            Console.WriteLine("abcd_order1=" + Ref1(abcd, b));
            Console.WriteLine("abcd_order2=" + Ref2(abcd, c));
            Console.WriteLine("abcd_order3=" + Ref3(abcd, d));
            order = 0;
            Console.WriteLine("abcd_last=" + (abcd(10) == 110));   // D(10)=110
            Console.WriteLine("abcd_runs=" + (order == 4));

            // ============ A2: parenthesizations of a+b+c+d ============
            Fn l1 = (a + b) + (c + d);
            Fn l2 = a + (b + c) + d;
            Fn l3 = (a + (b + c)) + d;
            Fn l4 = a + ((b + c) + d);
            Console.WriteLine("par_len=" + (l1.GetInvocationList().Length == 4));
            Console.WriteLine("par_eq_12=" + l1.Equals(l2));
            Console.WriteLine("par_eq_13=" + l1.Equals(l3));
            Console.WriteLine("par_eq_14=" + l1.Equals(l4));
            Console.WriteLine("par_neq_bad=" + !l1.Equals(b + a + c + d)); // order matters
            order = 0;
            Console.WriteLine("par_last=" + (l1(1) == 101));
            Console.WriteLine("par_runs=" + (order == 4));

            // ============ A3: leaf identity across shapes ============
            Console.WriteLine("shape_head=" + Ref0((a + b) + (c + d), a));
            Console.WriteLine("shape_head2=" + Ref0(a + (b + c) + d, a));
            Console.WriteLine("shape_mid=" + Ref1(a + (b + c) + d, b));
            Console.WriteLine("shape_tail=" + Ref3(a + (b + c) + d, d));

            // ============ A4: combining already-combined values ============
            Fn ab = a + b;
            Fn bc = b + c;
            Fn abcVia = ab + c;
            Fn abcVia2 = a + bc;
            Console.WriteLine("comb_ab_len=" + (ab.GetInvocationList().Length == 2));
            Console.WriteLine("comb_via_eq=" + abcVia.Equals(abcVia2));
            Console.WriteLine("comb_via_len=" + (abcVia.GetInvocationList().Length == 3));
            Fn abcd2 = (ab) + (c + d);
            Console.WriteLine("comb_abcd_len=" + (abcd2.GetInvocationList().Length == 4));
            Console.WriteLine("comb_abcd_eq=" + abcd2.Equals(abcd));
            Fn abab = ab + ab;
            Console.WriteLine("comb_abab_len=" + (abab.GetInvocationList().Length == 4));
            Console.WriteLine("comb_abab_order0=" + Ref0(abab, a));
            Console.WriteLine("comb_abab_order2=" + Ref2(abab, a));

            // ============ A5: combine of a leaf with its own chain ============
            Fn aaa = a + a + a;
            Console.WriteLine("aaa_len=" + (aaa.GetInvocationList().Length == 3));
            order = 0;
            Console.WriteLine("aaa_runs=" + (aaa(1) == 2 && order == 3)); // A runs 3x, each gets x=1, last wins
            Fn aaaa = aaa + a;
            Console.WriteLine("aaaa_len=" + (aaaa.GetInvocationList().Length == 4));
            order = 0;
            Console.WriteLine("aaaa_runs=" + (aaaa(1) == 2 && order == 4));

            // ============ B1: remove EVERY position of a 4-chain ============
            Fn rm0 = abcd - a;                                  // [a,b,c,d] - a -> [b,c,d]
            Console.WriteLine("rm0_len=" + (rm0.GetInvocationList().Length == 3));
            Console.WriteLine("rm0_head=" + Ref0(rm0, b));
            Console.WriteLine("rm0_tail=" + Ref2(rm0, d));
            order = 0;
            Console.WriteLine("rm0_last=" + (rm0(1) == 101));   // D(1)=101
            Console.WriteLine("rm0_runs=" + (order == 3));

            Fn rm1 = abcd - b;                                  // -> [a,c,d]
            Console.WriteLine("rm1_len=" + (rm1.GetInvocationList().Length == 3));
            Console.WriteLine("rm1_order0=" + Ref0(rm1, a));
            Console.WriteLine("rm1_order1=" + Ref1(rm1, c));
            Console.WriteLine("rm1_order2=" + Ref2(rm1, d));

            Fn rm2 = abcd - c;                                  // -> [a,b,d]
            Console.WriteLine("rm2_len=" + (rm2.GetInvocationList().Length == 3));
            Console.WriteLine("rm2_order1=" + Ref1(rm2, b));
            Console.WriteLine("rm2_tail=" + Ref2(rm2, d));

            Fn rm3 = abcd - d;                                  // -> [a,b,c]
            Console.WriteLine("rm3_len=" + (rm3.GetInvocationList().Length == 3));
            Console.WriteLine("rm3_tail=" + Ref2(rm3, c));

            // ============ B2: remove a 2-leaf value from a 4-chain ============
            Fn rm_ab = abcd - (a + b);                          // -> [c,d]
            Console.WriteLine("rm_ab_len=" + (rm_ab.GetInvocationList().Length == 2));
            Console.WriteLine("rm_ab_head=" + Ref0(rm_ab, c));
            Console.WriteLine("rm_ab_tail=" + Ref1(rm_ab, d));

            Fn rm_bc = abcd - (b + c);                          // -> [a,d]
            Console.WriteLine("rm_bc_len=" + (rm_bc.GetInvocationList().Length == 2));
            Console.WriteLine("rm_bc_head=" + Ref0(rm_bc, a));
            Console.WriteLine("rm_bc_tail=" + Ref1(rm_bc, d));

            Fn rm_cd = abcd - (c + d);                          // -> [a,b]
            Console.WriteLine("rm_cd_len=" + (rm_cd.GetInvocationList().Length == 2));
            Console.WriteLine("rm_cd_tail=" + Ref1(rm_cd, b));

            // ============ B3: remove a 3-leaf value ============
            Fn rm_abc = abcd - (a + b + c);                     // -> [d]
            Console.WriteLine("rm_abc_len=" + (rm_abc.GetInvocationList().Length == 1));
            Console.WriteLine("rm_abc_leaf=" + Ref0(rm_abc, d));
            Fn rm_bcd = abcd - (b + c + d);                     // -> [a]
            Console.WriteLine("rm_bcd_len=" + (rm_bcd.GetInvocationList().Length == 1));
            Console.WriteLine("rm_bcd_leaf=" + Ref0(rm_bcd, a));

            // ============ B4: whole-chain removal -> null ============
            Console.WriteLine("rm_whole=" + (abcd - abcd == null));
            Console.WriteLine("rm_self_ab=" + (ab - ab == null));
            Console.WriteLine("rm_single=" + (a - a == null));

            // ============ B5: LAST occurrence when the value repeats ============
            Fn abaa = a + b + a + a;
            Console.WriteLine("abaa_len=" + (abaa.GetInvocationList().Length == 4));
            Fn abaa_ra = abaa - a;                              // last 'a' removed -> [a,b,a]
            Console.WriteLine("abaa_ra_len=" + (abaa_ra.GetInvocationList().Length == 3));
            Console.WriteLine("abaa_ra_order0=" + Ref0(abaa_ra, a));
            Console.WriteLine("abaa_ra_order2=" + Ref2(abaa_ra, a));
            Fn abaa_rab = abaa - (a + b);                       // [a,b] at index 0 -> [a,a]
            Console.WriteLine("abaa_rab_len=" + (abaa_rab.GetInvocationList().Length == 2));
            Console.WriteLine("abaa_rab_order0=" + Ref0(abaa_rab, a));
            Console.WriteLine("abaa_rab_order1=" + Ref1(abaa_rab, a));

            // ============ B6: value-based removal (fresh delegate, same method) ====
            Fn aFresh = A;
            Console.WriteLine("val_fresh=" + ReferenceEquals(ab - aFresh, b)); // [a,b] - A -> [b] identity
            Fn abFresh = A; abFresh += B;
            Console.WriteLine("val_fresh2=" + ((abcd - abFresh).GetInvocationList().Length == 2));

            // ============ B7: absent / non-contiguous value -> identity ============
            Fn ac = a + c;
            Console.WriteLine("absent_identity=" + ReferenceEquals(abcd - ac, abcd));
            Console.WriteLine("absent_len=" + (abcd.GetInvocationList().Length == 4));
            Fn noncontig = abcd - (a + c);                      // [a,c] not contiguous -> identity
            Console.WriteLine("noncontig_identity=" + ReferenceEquals(noncontig, abcd));
            Fn absentLeaf = abcd - d;                            // d IS present -> NOT identity
            Console.WriteLine("present_neq_identity=" + !ReferenceEquals(absentLeaf, abcd));

            // ============ B8: nested-tree removal ============
            Fn nested = (a + b) + (c + d);
            Console.WriteLine("nested_len=" + (nested.GetInvocationList().Length == 4));
            Fn n_remove_cd = nested - (c + d);
            Console.WriteLine("nested_rm_len=" + (n_remove_cd.GetInvocationList().Length == 2));
            Console.WriteLine("nested_rm_head=" + Ref0(n_remove_cd, a));
            Fn n_remove_leaf = nested - c;                      // remove leaf across tree levels
            Console.WriteLine("nested_leaf_len=" + (n_remove_leaf.GetInvocationList().Length == 3));
            Console.WriteLine("nested_leaf_order1=" + Ref1(n_remove_leaf, b));
            Console.WriteLine("nested_leaf_order2=" + Ref2(n_remove_leaf, d));

            // ============ B9: removal from a 5-chain, every window ============
            Fn abcba = a + b + c + b + a;
            Console.WriteLine("abcba_len=" + (abcba.GetInvocationList().Length == 5));
            Fn w1 = abcba - (a + b);                            // [a,b] at index 0 -> [c,b,a]
            Console.WriteLine("w1_len=" + (w1.GetInvocationList().Length == 3));
            Console.WriteLine("w1_head=" + Ref0(w1, c));
            Console.WriteLine("w1_tail=" + Ref2(w1, a));
            Fn w2 = abcba - (b + c);                            // [b,c] at index 1 -> [a,b,a]
            Console.WriteLine("w2_len=" + (w2.GetInvocationList().Length == 3));
            Console.WriteLine("w2_order1=" + Ref1(w2, b));
            Fn w3 = abcba - (c + b);                            // [c,b] at index 2 -> [a,b,a]
            Console.WriteLine("w3_len=" + (w3.GetInvocationList().Length == 3));
            Console.WriteLine("w3_tail=" + Ref2(w3, a));
            Fn w4 = abcba - (b + a);                            // [b,a] at index 3 -> [a,b,c]
            Console.WriteLine("w4_len=" + (w4.GetInvocationList().Length == 3));
            Console.WriteLine("w4_tail=" + Ref2(w4, c));

            // ============ B10: surviving chain order + last-wins ============
            order = 0;
            Fn surv = rm2;                                      // [a,b,d]
            Console.WriteLine("surv_last=" + (surv(1) == 101));
            Console.WriteLine("surv_runs=" + (order == 3));
            Fn surv2 = rm_bc;                                   // [a,d]
            order = 0;
            Console.WriteLine("surv2_last=" + (surv2(1) == 101));
            Console.WriteLine("surv2_runs=" + (order == 2));
            // remove the last leaf from [a,d] -> [a]; last-wins now A
            Fn surv3 = surv2 - d;
            Console.WriteLine("surv3_len=" + (surv3.GetInvocationList().Length == 1));
            Console.WriteLine("surv3_last=" + (surv3(1) == 2));

            // ============ B11: equality after removal ============
            Console.WriteLine("eq_after=" + (rm3.Equals(a + b + c)));      // [a,b,c] == a+b+c
            Console.WriteLine("eq_after2=" + (rm2.Equals(a + b + d)));
            Console.WriteLine("eq_after3=" + !rm2.Equals(a + c + d));
            Console.WriteLine("eq_rebuild=" + (abcd - b - c == a + d));    // [a,b,c,d] - b - c == [a,d]
        }
    }
}
