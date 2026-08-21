//------------------------------------------------------------------------------
// enumerableops.cs -- ENUMERABLE MATERIALIZER DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C73, on the WIRED LINQ i4
// surface (MethodMap.Linq.cs -> linq.h / linq_map.h / linq_sort.h).
//
// SCOPE: the three MATERIALIZATION-facing operators -- ToArray (fresh
// int32[]), ToList (List<int32> materializer, listi4 domain), Count / Count
// (predicate) -- combined DEEP. Earlier LINQ cases drove the operator
// surface (linqops/linqdeep/linqfirst/linqfod), chain composition
// (linqchain2), the set/combination family (linqzip) and aggregates
// (linqagg). THIS case drives the materializer LIFETIME/ROUND-TRIP matrix
// none of them covered as its focus:
//
//   1. TOARRAY ROUND-TRIP -- ToArray() is a FRESH COPY: mutating the array
//      materialized from a Where chain must not disturb the source; a second
//      ToArray() of the result re-copies (same content, fresh buffer); the
//      materialized length/index layout asserted byte-exact.
//   2. TOLIST MATERIALIZER -- ToList() yields a List<int32> whose surface
//      (get_Count / get_Item / Add) is the listi4 domain: count, indexer
//      reads, Add AFTER materialization (the list grows), Clear+re-Add, and
//      List->ToArray()/Count() re-entering the enumerable surface.
//   3. TOARRAY<->TOLIST ROUND-TRIP -- ToArray().ToList().ToArray() preserves
//      order/length; ToList().ToArray() materializes the list content.
//   4. COUNT / COUNT(PRED) COMBOS -- plain Count on array / ToArray / ToList,
//      Count(predicate) on each materialized form, Count as the terminator of
//      Where/Select/Take/Skip chains, and the empty/single truth table.
//   5. MATERIALIZED-ALIAS SAFETY -- the same source materialized TWICE (two
//      ToArray calls) yields independent buffers; mutating one leaves the
//      other and the source intact (the D-ARR copy semantics, proven on the
//      LINQ materializer path).
//
// CALLBACK POLICY (runtime contract, linq.h:17-28): predicates/selectors
// MUST be single-cast STATIC-method delegates; every callback below is a
// static method.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class EnumerableOps
    {
        // ---- static callbacks (runtime-compatible delegate contract) ----
        public static bool IsEven(int x) { return x % 2 == 0; }
        public static bool IsBig(int x) { return x > 4; }
        public static int Dbl(int x) { return x * 2; }
        public static int Key(int x) { return x; }

        public static void Main()
        {
            int[] src = { 3, 1, 4, 1, 5, 9, 2, 6 };
            int[] empty = new int[0];

            // ===== 1. ToArray round-trip: fresh copy, source untouched =====
            int[] copy = src.ToArray();
            Console.WriteLine("copy_len=" + (copy.Length == 8));
            Console.WriteLine("copy_same_content=" + (copy[0] == 3 && copy[7] == 6));
            copy[0] = 999;
            Console.WriteLine("copy_is_independent=" + (src[0] == 3));
            Console.WriteLine("copy_mutated=" + (copy[0] == 999));

            int[] copy2 = copy.ToArray();              // re-materialize the mutated buffer
            Console.WriteLine("copy2_sees_mut=" + (copy2[0] == 999));
            Console.WriteLine("copy2_independent=" + (copy2[0] == 999 && copy[0] == 999));
            copy2[0] = 7;
            Console.WriteLine("copy2_mut_isolated=" + (copy[0] == 999 && src[0] == 3));

            int[] twiceSrc = src.ToArray().ToArray();  // chain of two materializers
            Console.WriteLine("twice_len=" + (twiceSrc.Length == 8));
            Console.WriteLine("twice_content=" + (twiceSrc[0] == 3 && twiceSrc[3] == 1));

            // ===== 2. ToList materializer: List<int32> surface =====
            List<int> lst = src.ToList();
            Console.WriteLine("lst_count=" + (lst.Count == 8));
            Console.WriteLine("lst_idx0=" + (lst[0] == 3));
            Console.WriteLine("lst_idx7=" + (lst[7] == 6));
            lst.Add(42);
            Console.WriteLine("lst_grow=" + (lst.Count == 9));
            Console.WriteLine("lst_grown_tail=" + (lst[8] == 42));
            lst.Clear();
            Console.WriteLine("lst_clear=" + (lst.Count == 0));
            lst.Add(11);
            lst.Add(22);
            Console.WriteLine("lst_reuse=" + (lst.Count == 2 && lst[1] == 22));
            Console.WriteLine("lst_src_untouched=" + (src.Length == 8));

            // ===== 3. ToArray <-> ToList round-trip =====
            int[] rt = src.ToList().ToArray();
            Console.WriteLine("rt_len=" + (rt.Length == 8));
            Console.WriteLine("rt_content=" + (rt[0] == 3 && rt[4] == 5));
            int[] rt2 = src.ToArray().ToList().ToArray();
            Console.WriteLine("rt2_len=" + (rt2.Length == 8));
            Console.WriteLine("rt2_content=" + (rt2[2] == 4 && rt2[6] == 2));

            // ===== 4. Count / Count(pred) combos over materialized forms =====
            Console.WriteLine("c_src=" + (src.Count() == 8));
            Console.WriteLine("c_toarray=" + (src.ToArray().Count() == 8));
            // D-LINQC-1 (fixed): the LINQ Enumerable.Count over a ToList()
            // receiver is now wired -- the List`1 receiver hint routes Count()
            // to the listi4 get_Count icall (il2cpp_list_i4_get_count) instead
            // of mis-casting the List object as an Il2CppArray* (the old
            // il2cpp_linq_count(Il2CppArray*) path read a garbage count).
            Console.WriteLine("c_tolist_linq=" + (src.ToList().Count() == 8));
            Console.WriteLine("c_tolist=" + (src.ToList().Count == 8));
            Console.WriteLine("c_pred_src=" + (src.Count(IsEven) == 3));
            Console.WriteLine("c_pred_toarray=" + (src.ToArray().Count(IsEven) == 3));
            Console.WriteLine("c_pred_big=" + (src.Count(IsBig) == 3));
            Console.WriteLine("c_empty=" + (empty.Count() == 0));
            Console.WriteLine("c_empty_pred=" + (empty.Count(IsBig) == 0));
            Console.WriteLine("c_single_pred=" + (new int[] { 8 }.Count(IsBig) == 1));
            Console.WriteLine("c_single_pred_miss=" + (new int[] { 2 }.Count(IsBig) == 0));

            // ===== 5. Count as chain terminator over materializers =====
            Console.WriteLine("chain_wc=" + (src.Where(IsEven).Count() == 3));
            Console.WriteLine("chain_w_toarray_c=" + (src.Where(IsEven).ToArray().Count() == 3));
            Console.WriteLine("chain_s_toarray_c=" + (src.Select(Dbl).ToArray().Count() == 8));
            Console.WriteLine("chain_take_c=" + (src.Take(3).Count() == 3));
            Console.WriteLine("chain_skip_c=" + (src.Skip(2).Count() == 6));
            Console.WriteLine("chain_take_toarray_c=" + (src.Take(4).ToArray().Count() == 4));
            // Where -> ToList -> Count() (the LINQ method form, D-LINQC-1 --
            // the same listi4 get_Count route as c_tolist_linq above).
            Console.WriteLine("chain_w_tolist_linq_c=" + (src.Where(IsBig).ToList().Count() == 3));
            Console.WriteLine("chain_w_tolist_c=" + (src.Where(IsBig).ToList().Count == 3));
            Console.WriteLine("chain_multi=" + (src.Where(IsEven).Select(Dbl).ToArray().Count() == 3));

            // ===== 6. Materialized-alias safety: two independent buffers =====
            int[] a1 = src.ToArray();
            int[] a2 = src.ToArray();
            a1[1] = -1;
            Console.WriteLine("alias_a1=" + (a1[1] == -1));
            Console.WriteLine("alias_a2_intact=" + (a2[1] == 1));
            Console.WriteLine("alias_src_intact=" + (src[1] == 1));

            int[] w1 = src.Where(IsEven).ToArray();
            int[] w2 = src.Where(IsEven).ToArray();
            w1[0] = -2;
            Console.WriteLine("alias_w1=" + (w1[0] == -2));
            Console.WriteLine("alias_w2_intact=" + (w2[0] == 4));
        }
    }
}
