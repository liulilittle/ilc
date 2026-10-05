//------------------------------------------------------------------------------
// delegateops.cs -- DELEGATE DOMAIN DEEP probe 3 (e2e batch C49): the
// Combine/Remove all-combination matrix and event-subscription removal --
// byte-exact vs the net48 oracle. Extends delegops/delegatecase/delegate2/
// delegatechain over the transpiler-safe custom-delegate surface.
//
// Signature discipline: every delegate is `int Fn(int)` -- the
// icall_multicast_invoke_i4 fast path (int32 return, <=4 int32 args) so
// SINGLE and MULTICAST chains both invoke correctly (multicast of other
// signatures is a documented gap; net48 probes here stay on the wired
// surface).
//
// Wired surface (MethodMap.Delegate.cs + delegateext.cpp), all probes stay
// within it:
//   Delegate.Combine / op_Addition        -> icall_delegate_combine
//   Delegate.Remove / op_Subtraction      -> icall_delegate_remove
//   op_Equality / op_Inequality           -> icall_delegate_equals (null-safe)
//   GetInvocationList()                   -> icall_delegate_get_invocation_list
//   get_Target                            -> icall_delegate_get_target
//   instance Delegate.Equals(object)      -> icall_delegate_equals
//
// net48-shape discipline (delegateext.cpp verified against the reference):
//   * Combine is pure concatenation -- NO dedup (a+a has length 2, no
//     identity shortcut), invocations run in insertion order, last result
//     wins.
//   * Remove drops the LAST occurrence of the value's CONTIGUOUS sub-list
//     and matches by VALUE (method + target), not object identity -- a
//     fresh delegate bound to the same method removes the same leaf.
//   * Absent removal returns the ORIGINAL object (identity -- no copy);
//     removing the whole chain yields null.
//   * Delegate.Equals compares invocation LISTS structurally (order
//     matters); a multicast's Target is the LAST leaf's target.
//
// Out of scope (recorded, NOT probed): Delegate.RemoveAll (loud rejection),
// get_Method (the CG6 name registry is emitted, but the string-name probe
// surface is covered by delegops), void/ref/non-int32 multicast invocation.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class Accum
    {
        public int Total;
        public Accum(int t) { Total = t; }
        public int Add(int x) { Total = Total + x; return Total; }
    }

    public static class DelegateOps
    {
        public delegate int Fn(int x);

        private static int order = 0;
        private static int A(int x) { order++; return x + 1; }
        private static int B(int x) { order++; return x * 10; }
        private static int C(int x) { order++; return x - 5; }
        private static int D(int x) { order++; return x + 100; }
        private static int Thrower(int x) { if (x >= 0) throw new Exception("boom"); return 0; }

        // ---- event pattern (field-backed, custom add/remove) ----
        private static Fn _changed;
        public static event Fn Changed
        {
            add { _changed = _changed + value; }
            remove { _changed = _changed - value; }
        }
        private static int Raise(int x)
        {
            if (ReferenceEquals(_changed, null)) { return -1; }
            return _changed(x);
        }

        public static void Main()
        {
            Fn a = A, b = B, c = C, d = D;

            // ============ S1: combine -- no dedup, pure concatenation ============
            Fn aa = a + a;
            Console.WriteLine("aa_len=" + (aa.GetInvocationList().Length == 2));   // True
            order = 0; aa(10);
            Console.WriteLine("aa_runs=" + (order == 2));                          // True (A called twice)
            Console.WriteLine("aa_last=" + (aa(10) == 11));                        // True (last result wins, both are A)
            Fn aaa = aa - a;                                                       // remove LAST a
            Console.WriteLine("aaa_len=" + (aaa.GetInvocationList().Length == 1)); // True
            order = 0; aaa(10);
            Console.WriteLine("aaa_runs=" + (order == 1));                         // True

            // associativity: (a+b)+c == a+(b+c) (same flattened list)
            Fn l = (a + b) + c;
            Fn r = a + (b + c);
            Console.WriteLine("assoc_eq=" + (l == r));                             // True
            Console.WriteLine("assoc_len=" + (l.GetInvocationList().Length == 3)); // True

            // order matters: (a+b) != (b+a)
            Console.WriteLine("order_diff=" + !(l == (b + a)));                    // True
            Fn ab = a + b;
            Fn ba = b + a;
            Console.WriteLine("head=" + ReferenceEquals(ab.GetInvocationList()[0], a)); // True
            Console.WriteLine("revhead=" + ReferenceEquals(ba.GetInvocationList()[0], b)); // True

            // ============ S2: remove -- last occurrence, value match, sequence ============
            Fn abc = a + b + c;
            // value-based removal: a FRESH delegate with method A removes the A leaf
            Fn a2 = A;
            Console.WriteLine("val_rem=" + ReferenceEquals(ab - a2, b));           // True ([a,b] - A -> [b], identity b)
            // last occurrence: [a,b,a] - a -> [a,b]
            Fn aba = a + b + a;
            Fn ab2 = aba - a;
            Console.WriteLine("last_occ_len=" + (ab2.GetInvocationList().Length == 2)); // True
            Console.WriteLine("last_occ0=" + ReferenceEquals(ab2.GetInvocationList()[0], a)); // True
            Console.WriteLine("last_occ1=" + ReferenceEquals(ab2.GetInvocationList()[1], b)); // True
            // contiguous sequence removal: [a,b,c] - (a+b) -> [c]
            Fn ac = abc - (a + b);
            Console.WriteLine("seq_len=" + (ac.GetInvocationList().Length == 1));  // True
            Console.WriteLine("seq0=" + ReferenceEquals(ac.GetInvocationList()[0], c)); // True
            // removing the whole chain -> null
            Console.WriteLine("whole_null=" + ((a + b) - ab == null));             // True
            // absent removal -> identity (no copy)
            Fn absent = abc - d;
            Console.WriteLine("absent_id=" + ReferenceEquals(absent, abc));        // True
            Console.WriteLine("absent_len=" + (absent.GetInvocationList().Length == 3)); // True
            // equality after successive removals: [a,b,c] - c - b == [a]
            Console.WriteLine("after_rem=" + ((abc - c - b) == a));                // True
            // remove-all via chained ops -> null
            Console.WriteLine("chain_rem_null=" + ((a + b) - a - b == null));      // True
            // pair removal: [a,b] - b -> [a]
            Console.WriteLine("pair_b_left=" + (ReferenceEquals((a + b) - b, a))); // True
            // nested tree, sequence removal across tree levels
            Fn nested = (a + b) + (c + d);
            Console.WriteLine("nested_len=" + (nested.GetInvocationList().Length == 4)); // True
            Fn nab = nested - (c + d);
            Console.WriteLine("nested_ab_len=" + (nab.GetInvocationList().Length == 2)); // True
            Console.WriteLine("nested_ab0=" + ReferenceEquals(nab.GetInvocationList()[0], a)); // True
            Console.WriteLine("nested_ab1=" + ReferenceEquals(nab.GetInvocationList()[1], b)); // True

            // ============ S3: null rules (Combine / Remove / == / !=) ============
            Console.WriteLine("combine_nl=" + ReferenceEquals(Delegate.Combine(null, a), a)); // True
            Console.WriteLine("combine_nr=" + ReferenceEquals(Delegate.Combine(a, null), a)); // True
            Console.WriteLine("remove_null=" + ReferenceEquals(Delegate.Remove(abc, null), abc)); // True
            Console.WriteLine("remove_null_src=" + (Delegate.Remove(null, a) == null));   // True
            Fn nul = null;
            Console.WriteLine("eq_null_null=" + (nul == null));                    // True
            Console.WriteLine("eq_a_null=" + (a == null));                         // False (a is bound)
            Console.WriteLine("ne_null_a=" + (null != a));                         // True
            Console.WriteLine("ne_a_b=" + (a != b));                               // True
            Console.WriteLine("eq_a_a=" + (a == a));                               // True
            Fn a3 = A;
            Console.WriteLine("eq_same_m=" + (a == a3));                           // True (same method+target)
            Console.WriteLine("eq_struct=" + (l == r));                            // True (same flattened list)

            // ============ S4: invocation order + last-wins over mixed chains ============
            order = 0; l(1);
            Console.WriteLine("mix_order=" + (order == 3));                        // True (A,B,C)
            Console.WriteLine("mix_last=" + (l(1) == -4));                         // True (C(1) = 1-5)
            Fn cb = c + b;
            Console.WriteLine("cb_last=" + (cb(2) == 20));                         // True (B(2) = 20)
            // instance-bound leaf joins a chain (delegops precedent)
            Accum acc = new Accum(100);
            Fn inst = acc.Add;
            Console.WriteLine("inst_single=" + (inst(5) == 105));                  // True
            Console.WriteLine("inst_state=" + (acc.Total == 105));                 // True
            Fn mix = inst + A;
            order = 0;
            Console.WriteLine("inst_chain_last=" + (mix(1) == 2));                 // True (acc.Add=101, A=2)
            Console.WriteLine("inst_chain_order=" + (order == 1));                 // True
            // Target surface
            Console.WriteLine("tgt_static=" + ReferenceEquals(a.Target, null));    // True (static leaf)
            Console.WriteLine("tgt_inst=" + ReferenceEquals(inst.Target, acc));    // True (instance leaf)
            Fn mi = inst + a;
            Console.WriteLine("tgt_last=" + ReferenceEquals(mi.Target, null));     // True (multicast -> LAST leaf target, a is static)

            // ============ S5: event subscription / removal ============
            order = 0;
            Changed += A; Changed += B; Changed += C;
            order = 0;
            Console.WriteLine("evt3_last=" + (Raise(1) == -4));                    // True (A,B,C; C(1)=-4)
            Console.WriteLine("evt3_order=" + (order == 3));                       // True
            Changed -= B;                                                          // remove MIDDLE handler
            order = 0;
            Console.WriteLine("evt2_last=" + (Raise(1) == -4));                    // True (A,C)
            Console.WriteLine("evt2_order=" + (order == 2));                       // True
            // remove an ABSENT handler -> the backing chain is untouched (identity)
            Fn before = _changed;
            Changed -= D;
            Console.WriteLine("evt_absent_id=" + ReferenceEquals(_changed, before)); // True
            Console.WriteLine("evt_absent_len=" + (Raise(1) == -4));               // True (chain unchanged)
            // subscribe the SAME handler twice -> runs twice; remove once -> runs once
            Changed += A;
            order = 0; Raise(1);
            Console.WriteLine("evt_dup_runs=" + (order == 3));                     // True (A,C,A -> 3 calls)
            Console.WriteLine("evt_dup_last=" + (Raise(1) == 2));                  // True (last is A -> 2)
            Changed -= A;
            order = 0; Raise(1);
            Console.WriteLine("evt_dup_removed=" + (order == 2));                  // True (A,C -> 2 calls)
            // unsubscribe all -> raise=none
            Changed -= A; Changed -= C;
            Console.WriteLine("evt_none=" + (Raise(1) == -1));                     // True
            Console.WriteLine("evt_none_isnull=" + ReferenceEquals(_changed, null)); // True
            // re-subscribe rebuilds the chain
            Changed += B;
            Console.WriteLine("evt_resub=" + (Raise(5) == 50));                    // True (B(5)=50)
            Changed -= B;
            // instance-bound handler in the event; instance state mutates per raise
            Accum acc2 = new Accum(1000);
            Changed += acc2.Add;
            Console.WriteLine("evt_inst=" + (Raise(1) == 1001));                   // True (acc2.Add returns new Total)
            Console.WriteLine("evt_inst_state=" + (acc2.Total == 1001));           // True
            Changed -= acc2.Add;
            Console.WriteLine("evt_inst_removed=" + (Raise(1) == -1));             // True (empty again)
            // a throwing handler aborts the remaining handlers (caught at the raise site)
            Changed += A; Changed += Thrower; Changed += C;
            order = 0;
            try { Raise(1); Console.WriteLine("evt_threw=false"); }
            catch (Exception) { Console.WriteLine("evt_threw=true"); }
            Console.WriteLine("evt_threw_order=" + (order == 1));                  // True (C skipped)
            Changed -= A; Changed -= Thrower; Changed -= C;
            Console.WriteLine("evt_final_none=" + (Raise(1) == -1));               // True
        }
    }
}
