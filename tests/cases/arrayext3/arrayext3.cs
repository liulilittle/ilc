//------------------------------------------------------------------------------
// arrayext3.cs -- System.Array predicate-domain DEEP 3 e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C58, AFTER the arrfuncs
// (C11) / arrayext2 (rtfind) surface is fully wired and PASSing.
//
// THIS case drives the PREDICATE COMBINATIONS across the int32 delegate
// family (il2cpp_array_ext2.h icalls, static-method single-cast delegate ABI
// -- the arrfuncs pattern):
//
//   1) TrueForAll / Exists duality and differential: two predicates on the
//      SAME array give different answers; TrueForAll(p) == !Exists(!p) on
//      non-empty inputs; the empty-sequence truth table (TrueForAll vacuously
//      true, Exists false).
//   2) Find / FindIndex / Exists agreement: Find returns the FIRST matching
//      VALUE, FindIndex the first matching INDEX, Exists whether any matched
//      -- probed on the same array so the three must agree.
//   3) FindIndex WINDOW forms (4-arg array,start,count,match): a match in /
//      out of the window, a window at the array end, no match -> -1.
//   4) ConvertAll combinations: identity, arithmetic transforms, then a
//      downstream Find/Exists/TrueForAll on the NEW array; ConvertAll o
//      ConvertAll; the SOURCE is never mutated.
//   5) Resize interplay: grow/shrink then run predicates on the resized
//      array; a null array resized to a length then queried.
//   6) Full chains: ConvertAll -> Resize -> Find; Resize -> TrueForAll;
//      FindIndex result feeding Find on the window.
//   7) Exception contracts: null array / null predicate -> ArgumentNull-
//      Exception, Resize negative -> ArgumentOutOfRangeException.
//
// FIXED GAP (D-ARF-1, 2026-08-12): Array.FindAll<T> is now wired at the
// MethodMap layer (icall_array_find_all_i4, il2cpp_array_ext2.h) -- section
// 8 drives the int32 FindAll surface (a NEW array of every matching element
// in source order, empty -> empty, source untouched).
//
// Every line prints "label=True/False"; net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ArrayExt3
    {
        // Static helper predicates (the single-cast STATIC methodPtr ABI the
        // arrfuncs case proves: Array.Find/Exists/TrueForAll/ConvertAll/
        // FindIndex consume the delegate directly).
        private static bool IsEven(int x) { return (x & 1) == 0; }
        private static bool IsOdd(int x) { return (x & 1) == 1; }
        private static bool IsBig(int x) { return x > 4; }
        private static bool IsSmall(int x) { return x < 4; }
        private static bool IsNeg(int x) { return x < 0; }
        private static bool AllPos(int x) { return x > 0; }
        private static int Dbl(int x) { return x * 2; }
        private static int PlusTen(int x) { return x + 10; }
        private static int Negate(int x) { return -x; }

        public static void Main()
        {
            int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            int[] small = { 1, 2, 3 };
            int[] negs = { -1, -5, -3, -2, -4 };
            int[] mixed = { -3, 0, 2, 5, -1 };
            int[] empty = new int[0];
            int[] single = { 7 };

            // ===== 1. TrueForAll / Exists duality + differential =====
            Console.WriteLine("t_odd=" + (Array.TrueForAll(negs, IsOdd) == false));
            Console.WriteLine("t_big=" + (Array.TrueForAll(nums, IsBig) == false));
            Console.WriteLine("t_small=" + Array.TrueForAll(small, IsSmall));
            Console.WriteLine("t_empty=" + Array.TrueForAll(empty, IsBig));
            Console.WriteLine("t_single=" + Array.TrueForAll(single, IsBig));
            Console.WriteLine("t_mixed_notall=" + (Array.TrueForAll(mixed, AllPos) == false));
            // Duality: TrueForAll(p) == !Exists(!p) on non-empty arrays.
            Console.WriteLine("duality_big=" + (Array.TrueForAll(nums, IsBig) == !Array.Exists(nums, x => !IsBig(x))));
            Console.WriteLine("duality_even=" + (Array.TrueForAll(negs, IsOdd) == !Array.Exists(negs, x => !IsOdd(x))));
            // Differential: the same array gives different answers per predicate.
            Console.WriteLine("diff_truefor=" + (Array.TrueForAll(small, IsSmall) != Array.TrueForAll(small, IsBig)));
            Console.WriteLine("e_big=" + Array.Exists(nums, IsBig));
            Console.WriteLine("e_small=" + Array.Exists(nums, IsSmall));
            Console.WriteLine("e_empty=" + (Array.Exists(empty, IsBig) == false));
            Console.WriteLine("e_neg=" + Array.Exists(negs, IsNeg));
            Console.WriteLine("e_mixed=" + Array.Exists(mixed, IsBig));
            Console.WriteLine("e_single=" + Array.Exists(single, IsBig));

            // ===== 2. Find / FindIndex / Exists agreement on one array =====
            // NOTE: only the 4-arg FindIndex overload transpiles (the
            // resolver maps FindIndex to the 4-param icall; 2/3-arg forms
            // abort -- D-ARF-2 in GAPS.md), so all windows are explicit.
            Console.WriteLine("agree_find=" + (Array.Find(nums, IsEven) == 2));
            Console.WriteLine("agree_idx=" + (Array.FindIndex(nums, 0, 9, IsEven) == 5));
            Console.WriteLine("agree_exists=" + Array.Exists(nums, IsEven));
            Console.WriteLine("find_big=" + (Array.Find(nums, IsBig) == 5));
            Console.WriteLine("find_idx_big=" + (Array.FindIndex(nums, 0, 9, IsBig) == 0));
            Console.WriteLine("find_small=" + (Array.Find(nums, IsSmall) == 3));
            Console.WriteLine("find_miss=" + (Array.Find(nums, x => x > 100) == 0));
            Console.WriteLine("find_empty=" + (Array.Find(empty, IsBig) == 0));
            Console.WriteLine("find_neg=" + (Array.Find(negs, IsNeg) == -1));

            // ===== 3. FindIndex window forms (4-arg: array,start,count,match) =====
            Console.WriteLine("win_hit=" + (Array.FindIndex(nums, 5, 3, IsEven) == 5));
            Console.WriteLine("win_start=" + (Array.FindIndex(nums, 6, 3, IsEven) == 6));
            Console.WriteLine("win_miss=" + (Array.FindIndex(nums, 1, 4, IsEven) == -1));
            Console.WriteLine("win_end=" + (Array.FindIndex(nums, 8, 1, x => x == 6) == 8));
            Console.WriteLine("win_outside=" + (Array.FindIndex(nums, 0, 4, IsEven) == -1));
            Console.WriteLine("win_single=" + (Array.FindIndex(single, 0, 1, IsBig) == 0));

            // ===== 4. ConvertAll combinations =====
            int[] d = Array.ConvertAll(nums, Dbl);
            Console.WriteLine("ca_len=" + (d.Length == nums.Length));
            Console.WriteLine("ca_val=" + (d[0] == 10 && d[8] == 12));
            Console.WriteLine("ca_src_untouched=" + (nums[0] == 5));
            int[] p = Array.ConvertAll(mixed, PlusTen);
            Console.WriteLine("ca_pten=" + (p[0] == 7 && p[4] == 9));
            Console.WriteLine("ca_neg=" + (Array.ConvertAll(negs, Negate)[0] == 1));
            int[] dd = Array.ConvertAll(Array.ConvertAll(small, Dbl), Dbl);
            Console.WriteLine("ca_chain=" + (dd[0] == 4 && dd[2] == 12));
            // ConvertAll result feeds the predicate family.
            Console.WriteLine("ca_find=" + (Array.Find(d, IsBig) == 10));
            Console.WriteLine("ca_exists_even=" + Array.Exists(d, IsEven));
            Console.WriteLine("ca_tfa_all=" + Array.TrueForAll(d, IsEven));
            Console.WriteLine("ca_empty=" + (Array.ConvertAll(empty, Dbl).Length == 0));

            // ===== 5. Resize interplay =====
            int[] r = { 1, 2, 3 };
            Array.Resize(ref r, 5);
            Console.WriteLine("rs_grow_len=" + (r.Length == 5));
            Console.WriteLine("rs_grow_pad=" + (r[3] == 0 && r[4] == 0));
            Console.WriteLine("rs_grow_tfa=" + (Array.TrueForAll(r, IsEven) == false));
            Console.WriteLine("rs_grow_find=" + (Array.Find(r, IsOdd) == 1));
            Array.Resize(ref r, 2);
            Console.WriteLine("rs_shrink_len=" + (r.Length == 2));
            Console.WriteLine("rs_shrink_exists=" + Array.Exists(r, IsEven));
            Console.WriteLine("rs_shrink_tfa=" + Array.TrueForAll(r, x => x > 0));
            int[] nul = null;
            Array.Resize(ref nul, 3);
            Console.WriteLine("rs_null_len=" + (nul != null && nul.Length == 3));
            Console.WriteLine("rs_null_find=" + (Array.Find(nul, IsOdd) == 0));
            Console.WriteLine("rs_null_tfa=" + (Array.TrueForAll(nul, IsOdd) == false));
            Console.WriteLine("rs_null_conv=" + (Array.ConvertAll(nul, Dbl).Length == 3));

            // ===== 6. full chains =====
            int[] c = Array.ConvertAll(nums, Dbl);
            Array.Resize(ref c, 6);
            Console.WriteLine("ch_conv_resize=" + (c.Length == 6 && c[0] == 10));
            Console.WriteLine("ch_find_resized=" + (Array.Find(c, IsBig) == 10));
            Console.WriteLine("ch_conv_findidx=" + (Array.FindIndex(c, 0, 6, IsEven) == 0));
            Console.WriteLine("ch_where=" + (Array.ConvertAll(nums, Dbl).Length == 9));
            Console.WriteLine("ch_agg=" + (Array.Exists(nums, IsBig) && Array.TrueForAll(nums, IsOdd) == false && Array.Find(nums, IsSmall) == 3));

            // ===== 7. exception contracts =====
            bool ane1 = false;
            try { Array.Find((int[])null, IsBig); } catch (ArgumentNullException) { ane1 = true; }
            Console.WriteLine("ane_arr=" + ane1);
            bool ane2 = false;
            try { Array.Exists(nums, (Predicate<int>)null); } catch (ArgumentNullException) { ane2 = true; }
            Console.WriteLine("ane_pred=" + ane2);
            bool ane3 = false;
            try { Array.TrueForAll((int[])null, IsBig); } catch (ArgumentNullException) { ane3 = true; }
            Console.WriteLine("ane_tfa=" + ane3);
            bool aoo = false;
            try { int[] t = { 1 }; Array.Resize(ref t, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
            Console.WriteLine("aoo_neg=" + aoo);

            // ===== 8. FindAll (D-ARF-1 FIXED 2026-08-12) =====
            // Array.FindAll<int>(int[], Predicate<int>): a NEW int32[] of every
            // matching element in source order (empty result -> empty array;
            // the source is never modified). net48 oracle == transpiled runtime.
            int[] fa_even = Array.FindAll(nums, IsEven);
            Console.WriteLine("fa_even_len=" + (fa_even.Length == 4));
            Console.WriteLine("fa_even_order=" + (fa_even[0] == 2 && fa_even[1] == 8 && fa_even[2] == 4 && fa_even[3] == 6));
            Console.WriteLine("fa_src_untouched=" + (nums[0] == 5 && nums.Length == 9));
            Console.WriteLine("fa_big_len=" + (Array.FindAll(nums, IsBig).Length == 5));
            Console.WriteLine("fa_small_len=" + (Array.FindAll(nums, IsSmall).Length == 3));
            Console.WriteLine("fa_neg_len=" + (Array.FindAll(negs, IsNeg).Length == 5));
            Console.WriteLine("fa_mixed_neg=" + (Array.FindAll(mixed, IsNeg)[0] == -3 && Array.FindAll(mixed, IsNeg)[1] == -1));
            Console.WriteLine("fa_empty_len=" + (Array.FindAll(empty, IsBig).Length == 0));
            Console.WriteLine("fa_single=" + (Array.FindAll(single, IsBig).Length == 1 && Array.FindAll(single, IsBig)[0] == 7));
            Console.WriteLine("fa_all=" + (Array.FindAll(small, x => x > 0).Length == 3));
            Console.WriteLine("fa_none=" + (Array.FindAll(negs, x => x > 0).Length == 0));
            // FindAll feeds the predicate family and ConvertAll.
            int[] fa_d = Array.FindAll(nums, IsEven);
            Console.WriteLine("fa_chain_tfa=" + Array.TrueForAll(fa_d, IsEven));
            Console.WriteLine("fa_chain_find=" + (Array.Find(fa_d, IsBig) == 8));
            Console.WriteLine("fa_chain_conv=" + (Array.ConvertAll(fa_d, Dbl)[0] == 4));
            Console.WriteLine("fa_conv_feed=" + (Array.FindAll(Array.ConvertAll(nums, Dbl), IsBig).Length == 7));
            // Exception contracts.
            bool fa_ane1 = false;
            try { Array.FindAll((int[])null, IsBig); } catch (ArgumentNullException) { fa_ane1 = true; }
            Console.WriteLine("fa_ane_arr=" + fa_ane1);
            bool fa_ane2 = false;
            try { Array.FindAll(nums, (Predicate<int>)null); } catch (ArgumentNullException) { fa_ane2 = true; }
            Console.WriteLine("fa_ane_pred=" + fa_ane2);
        }
    }
}
