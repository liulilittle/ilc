//------------------------------------------------------------------------------
// linqtake.cs -- LINQ Take/Skip DEEP e2e reference case (net48 oracle,
// byte-for-byte, dual-compiler). Created in e2e batch C80 AFTER the LINQ
// domain landed (linq.h / linq_map.h wired -- Take/Skip route to
// il2cpp_linq_take_i4 / il2cpp_linq_skip_i4, MethodMap.Linq.cs:212-228).
//
// Goes deeper than linqcase (C13: the basic slice lattice) and linqagg/
// linqgroup (aggregate + orderby combos): THIS case drives the Take/Skip
// operator surface from every angle that distinguishes a correct slice from
// an off-by-one:
//
//   1) BOUNDARY LATTICE -- count in {-5,-2,-1,0,1,2,3,4,5,6,7,8,9,10,11,100,
//      int.MaxValue} against a 9-element source, asserting the exact RESULT
//      VALUES (not just lengths) for the near-clamp counts (1, len-1, len,
//      len+1), the zero/negative clamps (empty result for Take, full copy
//      for Skip), and the over-run clamps.
//   2) CHAIN COMPOSITION -- Take->Skip, Skip->Take, Take->Take, Skip->Skip,
//      Take->Where, Skip->Where, Where->Take, Select->Take, Take->Select,
//      Skip->Select, Select->Skip, Where->Skip, and the full
//      Where->Skip->Take->Select pipeline, asserting element VALUES at each
//      stage.
//   3) TAKE/SKIP ON SHAPES -- empty, single, negative-element, duplicate-
//      heavy and unsorted sources, plus the length-1 / length-2 clamps.
//   4) AGGREGATES OVER SLICES -- Count/Sum/Min/Max/First/FirstOrDefault/
//      ToArray/ToList on sliced results (the common downstream operators).
//   5) SLICE PARTITION IDENTITY -- Take(k).Concat(Skip(k)) re-assembles the
//      source in ORDER for every k in {0,1,4,8,9,10} (net48 concat).
//
// Delegate contract (linq.h / linq_map.h): every predicate/selector below is
// a single-cast STATIC-method delegate. Only the WIRED operator surface is
// probed; OrderBy is NOT used here (it is covered by linqgroup/linqagg).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases { public static class LinqTake {
    // ---- static callbacks (single-cast static-method ABI) ----
    public static bool IsEven(int x) { return x % 2 == 0; }
    public static bool IsOdd(int x) { return (x & 1) == 1; }
    public static bool IsNeg(int x) { return x < 0; }
    public static bool IsBig4(int x) { return x > 4; }
    public static int Dbl(int x) { return x * 2; }
    public static int Neg(int x) { return -x; }
    public static int Identity(int x) { return x; }

    // ---- D-LT-2 TakeWhile/SkipWhile predicates (1-arg and 2-arg) ----
    public static bool Less5(int x) { return x < 5; }
    public static bool Less7(int x) { return x < 7; }
    public static bool Less8(int x) { return x < 8; }
    public static bool Less9(int x) { return x < 9; }
    public static bool Less50(int x) { return x < 50; }
    public static bool Big50(int x) { return x > 50; }
    public static bool AlwaysTrueW(int x) { return true; }
    // 2-arg (element, source-index) predicates -- the net48 index overloads.
    public static bool IdxLess2(int x, int i) { return i < 2; }
    public static bool ValLess9(int x, int i) { return x < 9; }

    public static void Main() {
        int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };   // 9 elements
        int[] empty = new int[0];
        int[] single = { 42 };
        int[] negs = { -1, -5, -3, -2, -4 };           // all negative
        int[] dups = { 7, 7, 3, 3, 3, 1, 9, 9 };      // duplicates
        int[] unsorted = { 8, 2, 9, 1, 5 };            // unsorted 5

        Func<int, bool> even = IsEven;
        Func<int, bool> odd = IsOdd;
        Func<int, bool> neg = IsNeg;
        Func<int, bool> big4 = IsBig4;
        Func<int, int> dbl = Dbl;
        Func<int, int> ngt = Neg;
        Func<int, int> id = Identity;
        Func<int, bool> less5 = Less5;
        Func<int, bool> less7 = Less7;
        Func<int, bool> less8 = Less8;
        Func<int, bool> less9 = Less9;
        Func<int, bool> less50 = Less50;
        Func<int, bool> big50 = Big50;
        Func<int, bool> alwaysTrueW = AlwaysTrueW;

        // ============ 1. BOUNDARY LATTICE on the 9-element source ============
        // Take: count 0 / 1 / len-1 / len / len+1 / oversize / negatives.
        Console.WriteLine("b_t0=" + (nums.Take(0).ToArray().Length == 0));
        Console.WriteLine("b_t1=" + (nums.Take(1).ToArray().Length == 1 && nums.Take(1).ToArray()[0] == 5));
        Console.WriteLine("b_t8=" + (nums.Take(8).ToArray().Length == 8 && nums.Take(8).ToArray()[7] == 4));
        Console.WriteLine("b_t9=" + (nums.Take(9).ToArray().Length == 9 && nums.Take(9).ToArray()[8] == 6));
        Console.WriteLine("b_t10=" + (nums.Take(10).ToArray().Length == 9 && nums.Take(10).ToArray()[8] == 6));
        Console.WriteLine("b_t100=" + (nums.Take(100).ToArray().Length == 9));
        Console.WriteLine("b_tMax=" + (nums.Take(int.MaxValue).ToArray().Length == 9));
        Console.WriteLine("b_tneg1=" + (nums.Take(-1).ToArray().Length == 0));
        Console.WriteLine("b_tneg5=" + (nums.Take(-5).ToArray().Length == 0));
        Console.WriteLine("b_tnegMax=" + (nums.Take(int.MinValue).ToArray().Length == 0));
        // Skip: count 0 / 1 / len-1 / len / len+1 / oversize / negatives.
        Console.WriteLine("b_s0=" + (nums.Skip(0).ToArray().Length == 9 && nums.Skip(0).ToArray()[0] == 5));
        Console.WriteLine("b_s1=" + (nums.Skip(1).ToArray().Length == 8 && nums.Skip(1).ToArray()[0] == 3));
        Console.WriteLine("b_s8=" + (nums.Skip(8).ToArray().Length == 1 && nums.Skip(8).ToArray()[0] == 6));
        Console.WriteLine("b_s9=" + (nums.Skip(9).ToArray().Length == 0));
        Console.WriteLine("b_s10=" + (nums.Skip(10).ToArray().Length == 0));
        Console.WriteLine("b_s100=" + (nums.Skip(100).ToArray().Length == 0));
        Console.WriteLine("b_sMax=" + (nums.Skip(int.MaxValue).ToArray().Length == 0));
        Console.WriteLine("b_sneg1=" + (nums.Skip(-1).ToArray().Length == 9 && nums.Skip(-1).ToArray()[0] == 5));
        Console.WriteLine("b_sneg5=" + (nums.Skip(-5).ToArray().Length == 9));
        Console.WriteLine("b_snegMax=" + (nums.Skip(int.MinValue).ToArray().Length == 9));
        // Exact slice VALUES near the clamp boundary (not just lengths).
        int[] tv = nums.Take(4).ToArray();   // {5,3,9,1}
        Console.WriteLine("v_t4=" + (tv.Length == 4 && tv[0] == 5 && tv[1] == 3 && tv[2] == 9 && tv[3] == 1));
        int[] sv = nums.Skip(4).ToArray();   // {7,2,8,4,6}
        Console.WriteLine("v_s4=" + (sv.Length == 5 && sv[0] == 7 && sv[1] == 2 && sv[2] == 8 && sv[3] == 4 && sv[4] == 6));

        // ============ 2. CHAIN COMPOSITION (values, not just counts) ============
        // Take->Skip: first 6 then drop 2 -> {9,1,7,2}
        int[] tksk = nums.Take(6).Skip(2).ToArray();
        Console.WriteLine("c_tksk=" + (tksk.Length == 4 && tksk[0] == 9 && tksk[1] == 1 && tksk[2] == 7 && tksk[3] == 2));
        // Skip->Take: drop 2 then first 3 -> {9,1,7}
        int[] sktk = nums.Skip(2).Take(3).ToArray();
        Console.WriteLine("c_sktk=" + (sktk.Length == 3 && sktk[0] == 9 && sktk[1] == 1 && sktk[2] == 7));
        // Take->Take: idempotent when the second Take is not larger -> {5,3}
        int[] tktk = nums.Take(5).Take(2).ToArray();
        Console.WriteLine("c_tktk=" + (tktk.Length == 2 && tktk[0] == 5 && tktk[1] == 3));
        // Take->Take expanding clamp -> full first-5
        int[] tktk2 = nums.Take(2).Take(9).ToArray();
        Console.WriteLine("c_tktk2=" + (tktk2.Length == 2 && tktk2[0] == 5 && tktk2[1] == 3));
        // Skip->Skip: cumulative -> Skip(2)={9,1,7,2,8,4,6} (7), Skip(5) -> {4,6}
        int[] sksk = nums.Skip(2).Skip(5).ToArray();
        Console.WriteLine("c_sksk=" + (sksk.Length == 2 && sksk[0] == 4 && sksk[1] == 6));
        // Take->Where: first 5 then even -> {2,8}? no: first 5 = {5,3,9,1,7}, even filter = none
        int[] tkw = nums.Take(5).Where(even).ToArray();
        Console.WriteLine("c_tkw=" + (tkw.Length == 0));
        // Take->Where on first 7 -> {5,3,9,1,7,2,8} evens {2,8}
        int[] tkw7 = nums.Take(7).Where(even).ToArray();
        Console.WriteLine("c_tkw7=" + (tkw7.Length == 2 && tkw7[0] == 2 && tkw7[1] == 8));
        // Skip->Where: skip 3 then evens -> {2,8,4,6}
        int[] skw = nums.Skip(3).Where(even).ToArray();
        Console.WriteLine("c_skw=" + (skw.Length == 4 && skw[0] == 2 && skw[1] == 8 && skw[2] == 4 && skw[3] == 6));
        // Where->Take: evens then first 2 -> {2,8}
        int[] wtk = nums.Where(even).Take(2).ToArray();
        Console.WriteLine("c_wtk=" + (wtk.Length == 2 && wtk[0] == 2 && wtk[1] == 8));
        // Select->Take: doubled then first 3 -> {10,6,18}
        int[] seltk = nums.Select(dbl).Take(3).ToArray();
        Console.WriteLine("c_seltk=" + (seltk.Length == 3 && seltk[0] == 10 && seltk[1] == 6 && seltk[2] == 18));
        // Take->Select: first 4 then doubled -> {10,6,18,2}
        int[] tksel = nums.Take(4).Select(dbl).ToArray();
        Console.WriteLine("c_tksel=" + (tksel.Length == 4 && tksel[0] == 10 && tksel[1] == 6 && tksel[2] == 18 && tksel[3] == 2));
        // Skip->Select: skip 4 then doubled -> {14,4,16,8,12}
        int[] sksel = nums.Skip(4).Select(dbl).ToArray();
        Console.WriteLine("c_sksel=" + (sksel.Length == 5 && sksel[0] == 14 && sksel[1] == 4 && sksel[2] == 16 && sksel[3] == 8 && sksel[4] == 12));
        // Select->Skip: doubled then skip 7 -> {8,12}
        int[] selsk = nums.Select(dbl).Skip(7).ToArray();
        Console.WriteLine("c_selsk=" + (selsk.Length == 2 && selsk[0] == 8 && selsk[1] == 12));
        // Where->Skip: evens then skip 2 -> {4,6}
        int[] wsk = nums.Where(even).Skip(2).ToArray();
        Console.WriteLine("c_wsk=" + (wsk.Length == 2 && wsk[0] == 4 && wsk[1] == 6));
        // FULL PIPELINE: Where(big4) -> Skip(1) -> Take(2) -> Select(dbl)
        // big4 = {5,9,7,8,6}; skip 1 = {9,7,8,6}; take 2 = {9,7}; dbl = {18,14}
        int[] pipe = nums.Where(big4).Skip(1).Take(2).Select(dbl).ToArray();
        Console.WriteLine("c_pipe=" + (pipe.Length == 2 && pipe[0] == 18 && pipe[1] == 14));
        // NEGATED pipeline: Take(4) -> Select(neg) -> Skip(1) -> Take(2)
        // take 4 = {5,3,9,1}; neg = {-5,-3,-9,-1}; skip 1 = {-3,-9,-1}; take 2 = {-3,-9}
        int[] npipe = nums.Take(4).Select(ngt).Skip(1).Take(2).ToArray();
        Console.WriteLine("c_npipe=" + (npipe.Length == 2 && npipe[0] == -3 && npipe[1] == -9));

        // ============ 3. TAKE/SKIP ON SHAPES ============
        Console.WriteLine("sh_empty_t=" + (empty.Take(3).ToArray().Length == 0));
        Console.WriteLine("sh_empty_s=" + (empty.Skip(3).ToArray().Length == 0));
        Console.WriteLine("sh_single_t0=" + (single.Take(0).ToArray().Length == 0));
        Console.WriteLine("sh_single_t1=" + (single.Take(1).ToArray().Length == 1 && single.Take(1).ToArray()[0] == 42));
        Console.WriteLine("sh_single_t2=" + (single.Take(2).ToArray().Length == 1 && single.Take(2).ToArray()[0] == 42));
        Console.WriteLine("sh_single_s0=" + (single.Skip(0).ToArray().Length == 1 && single.Skip(0).ToArray()[0] == 42));
        Console.WriteLine("sh_single_s1=" + (single.Skip(1).ToArray().Length == 0));
        Console.WriteLine("sh_neg_t2=" + (negs.Take(2).ToArray().Length == 2 && negs.Take(2).ToArray()[0] == -1 && negs.Take(2).ToArray()[1] == -5));
        Console.WriteLine("sh_neg_s3=" + (negs.Skip(3).ToArray().Length == 2 && negs.Skip(3).ToArray()[0] == -2 && negs.Skip(3).ToArray()[1] == -4));
        Console.WriteLine("sh_neg_all=" + (negs.Skip(2).All(neg)));
        Console.WriteLine("sh_dup_t5=" + (dups.Take(5).ToArray().Length == 5 && dups.Take(5).ToArray()[4] == 3));
        Console.WriteLine("sh_dup_s4=" + (dups.Skip(4).ToArray().Length == 4 && dups.Skip(4).ToArray()[0] == 3));
        Console.WriteLine("sh_uns_t2=" + (unsorted.Take(2).ToArray().Length == 2 && unsorted.Take(2).ToArray()[0] == 8 && unsorted.Take(2).ToArray()[1] == 2));
        Console.WriteLine("sh_uns_s2=" + (unsorted.Skip(2).ToArray().Length == 3 && unsorted.Skip(2).ToArray()[0] == 9 && unsorted.Skip(2).ToArray()[2] == 5));
        Console.WriteLine("sh_1_t=" + (nums.Take(1).ToArray()[0] == 5));
        Console.WriteLine("sh_1_s=" + (nums.Skip(8).ToArray()[0] == 6));

        // ============ 4. AGGREGATES OVER SLICES ============
        Console.WriteLine("a_count_t3=" + (nums.Take(3).Count() == 3));
        Console.WriteLine("a_count_s4=" + (nums.Skip(4).Count() == 5));
        Console.WriteLine("a_sum_t3=" + (nums.Take(3).Sum() == 17));        // 5+3+9
        Console.WriteLine("a_sum_s3=" + (nums.Skip(3).Sum() == 28));        // 1+7+2+8+4+6
        Console.WriteLine("a_sum_t0=" + (nums.Take(0).Sum() == 0));
        Console.WriteLine("a_sum_s9=" + (nums.Skip(9).Sum() == 0));
        Console.WriteLine("a_sum_t100=" + (nums.Take(100).Sum() == 45));
        Console.WriteLine("a_min_t5=" + (nums.Take(5).Min() == 1));
        Console.WriteLine("a_max_t5=" + (nums.Take(5).Max() == 9));
        Console.WriteLine("a_min_s4=" + (nums.Skip(4).Min() == 2));
        Console.WriteLine("a_max_s4=" + (nums.Skip(4).Max() == 8));
        Console.WriteLine("a_first_t3=" + (nums.Take(3).First() == 5));
        Console.WriteLine("a_first_s4=" + (nums.Skip(4).First() == 7));
        Console.WriteLine("a_fod_t0=" + (nums.Take(0).FirstOrDefault() == 0));
        Console.WriteLine("a_fod_s9=" + (nums.Skip(9).FirstOrDefault() == 0));
        bool tEmptyThrow = false;
        try { int x = nums.Take(0).First(); } catch (InvalidOperationException) { tEmptyThrow = true; }
        Console.WriteLine("a_first_t0_throws=" + tEmptyThrow);
        bool sEmptyThrow = false;
        try { int y = nums.Skip(9).Min(); } catch (InvalidOperationException) { sEmptyThrow = true; }
        Console.WriteLine("a_min_s9_throws=" + sEmptyThrow);
        Console.WriteLine("a_any_t0=" + (nums.Take(0).Any() == false));
        Console.WriteLine("a_any_s0=" + (nums.Skip(0).Any()));
        Console.WriteLine("a_all_t3=" + (nums.Take(3).All(big4) == false));  // 3 not > 4
        Console.WriteLine("a_toarr_t=" + (nums.Take(4).ToArray().Length == 4));
        Console.WriteLine("a_tolist_t=" + (nums.Skip(2).Take(3).ToList().Count == 3));
        Console.WriteLine("a_tolist_val=" + (nums.Skip(2).Take(3).ToList()[2] == 7));

        // ============ 5. SLICE PARTITION IDENTITY ============
        int[] all9 = nums.Take(9).Concat(nums.Skip(9)).ToArray();
        Console.WriteLine("p_all9=" + (all9.Length == 9 && all9[0] == 5 && all9[8] == 6));
        int[] p0 = nums.Take(0).Concat(nums.Skip(0)).ToArray();
        Console.WriteLine("p_k0=" + (p0.Length == 9 && p0[0] == 5 && p0[8] == 6));
        int[] p1 = nums.Take(1).Concat(nums.Skip(1)).ToArray();
        Console.WriteLine("p_k1=" + (p1.Length == 9 && p1[0] == 5 && p1[1] == 3 && p1[8] == 6));
        int[] p4 = nums.Take(4).Concat(nums.Skip(4)).ToArray();
        Console.WriteLine("p_k4=" + (p4.Length == 9 && p4[0] == 5 && p4[3] == 1 && p4[4] == 7 && p4[8] == 6));
        int[] p8 = nums.Take(8).Concat(nums.Skip(8)).ToArray();
        Console.WriteLine("p_k8=" + (p8.Length == 9 && p8[7] == 4 && p8[8] == 6));
        int[] p10 = nums.Take(10).Concat(nums.Skip(10)).ToArray();
        Console.WriteLine("p_k10=" + (p10.Length == 9 && p10[8] == 6));

        // ============ 6. TakeWhile / SkipWhile (D-LT-2, net48 prefix semantics) ============
        // nums = {5, 3, 9, 1, 7, 2, 8, 4, 6}
        // TakeWhile keeps the longest prefix while the predicate holds and
        // STOPS at the first failure (the failing element is EXCLUDED);
        // SkipWhile skips the prefix and yields the first failing element AND
        // the rest (the predicate is never called again after the prefix).
        // ---- 6a. TakeWhile (1-arg predicate) ----
        Console.WriteLine("w_t_empty=" + (nums.TakeWhile(less5).ToArray().Length == 0));
        Console.WriteLine("w_t_even=" + (nums.TakeWhile(even).ToArray().Length == 0));
        Console.WriteLine("w_t_big4=" + (nums.TakeWhile(big4).ToArray().Length == 1 && nums.TakeWhile(big4).ToArray()[0] == 5));
        Console.WriteLine("w_t_l8=" + (nums.TakeWhile(less8).ToArray().Length == 2 && nums.TakeWhile(less8).ToArray()[1] == 3));
        Console.WriteLine("w_t_odd=" + (nums.TakeWhile(odd).ToArray().Length == 5 && nums.TakeWhile(odd).ToArray()[4] == 7));
        Console.WriteLine("w_t_all=" + (nums.TakeWhile(alwaysTrueW).ToArray().Length == 9 && nums.TakeWhile(alwaysTrueW).ToArray()[8] == 6));
        // ---- 6b. SkipWhile (1-arg predicate) ----
        Console.WriteLine("w_s_none=" + (nums.SkipWhile(less5).ToArray().Length == 9 && nums.SkipWhile(less5).ToArray()[0] == 5));
        Console.WriteLine("w_s_big4=" + (nums.SkipWhile(big4).ToArray().Length == 8 && nums.SkipWhile(big4).ToArray()[0] == 3));
        Console.WriteLine("w_s_l8=" + (nums.SkipWhile(less8).ToArray().Length == 7 && nums.SkipWhile(less8).ToArray()[0] == 9));
        Console.WriteLine("w_s_odd=" + (nums.SkipWhile(odd).ToArray().Length == 4 && nums.SkipWhile(odd).ToArray()[0] == 2 && nums.SkipWhile(odd).ToArray()[3] == 6));
        Console.WriteLine("w_s_even=" + (nums.SkipWhile(even).ToArray().Length == 9));
        Console.WriteLine("w_s_all=" + (nums.SkipWhile(alwaysTrueW).ToArray().Length == 0));
        // ---- 6c. INDEX overloads (Func<TSource,int,bool>) ----
        Console.WriteLine("w_t_idx=" + (nums.TakeWhile(new Func<int, int, bool>(IdxLess2)).ToArray().Length == 2 && nums.TakeWhile(new Func<int, int, bool>(IdxLess2)).ToArray()[1] == 3));
        Console.WriteLine("w_t_val9=" + (nums.TakeWhile(new Func<int, int, bool>(ValLess9)).ToArray().Length == 2 && nums.TakeWhile(new Func<int, int, bool>(ValLess9)).ToArray()[0] == 5));
        Console.WriteLine("w_s_idx=" + (nums.SkipWhile(new Func<int, int, bool>(IdxLess2)).ToArray().Length == 7 && nums.SkipWhile(new Func<int, int, bool>(IdxLess2)).ToArray()[0] == 9));
        Console.WriteLine("w_s_val9=" + (nums.SkipWhile(new Func<int, int, bool>(ValLess9)).ToArray().Length == 7 && nums.SkipWhile(new Func<int, int, bool>(ValLess9)).ToArray()[6] == 6));
        // ---- 6d. CHAIN COMPOSITION ----
        // evens {2,8,4,6} -> TakeWhile(<7) = {2}.
        Console.WriteLine("w_chain1=" + (nums.Where(even).TakeWhile(less7).ToArray().Length == 1 && nums.Where(even).TakeWhile(less7).ToArray()[0] == 2));
        // TakeWhile(odd) = {5,3,9,1,7}; Skip(2) = {9,1,7}.
        Console.WriteLine("w_chain2=" + (nums.TakeWhile(odd).Skip(2).ToArray().Length == 3 && nums.TakeWhile(odd).Skip(2).ToArray()[0] == 9));
        // Take(5) = {5,3,9,1,7}; SkipWhile(<8) = {9,1,7}.
        Console.WriteLine("w_chain3=" + (nums.Take(5).SkipWhile(less8).ToArray().Length == 3 && nums.Take(5).SkipWhile(less8).ToArray()[0] == 9));
        // TakeWhile(<8) = {5,3}; Select(dbl) = {10,6}.
        Console.WriteLine("w_chain4=" + (nums.TakeWhile(less8).Select(dbl).ToArray().Length == 2 && nums.TakeWhile(less8).Select(dbl).ToArray()[0] == 10));
        // SkipWhile(<8) = {9,1,7,2,8,4,6}; Take(2) = {9,1}.
        Console.WriteLine("w_chain5=" + (nums.SkipWhile(less8).Take(2).ToArray().Length == 2 && nums.SkipWhile(less8).Take(2).ToArray()[1] == 1));
        // ---- 6e. AGGREGATES OVER WHILE SLICES ----
        Console.WriteLine("w_agg1=" + (nums.TakeWhile(odd).Count() == 5));
        Console.WriteLine("w_agg2=" + (nums.SkipWhile(odd).Sum() == 20));          // 2+8+4+6
        Console.WriteLine("w_agg3=" + (nums.TakeWhile(less8).Sum() == 8));         // 5+3
        Console.WriteLine("w_agg4=" + (nums.TakeWhile(odd).First() == 5));
        Console.WriteLine("w_agg5=" + (nums.SkipWhile(odd).Min() == 2));
        Console.WriteLine("w_agg6=" + (nums.SkipWhile(odd).Max() == 8));
        Console.WriteLine("w_agg7=" + (nums.TakeWhile(less8).ToArray().Length == 2));
        // ---- 6f. SHAPES + WHILE PARTITION IDENTITY ----
        Console.WriteLine("w_empty_t=" + (empty.TakeWhile(odd).ToArray().Length == 0));
        Console.WriteLine("w_empty_s=" + (empty.SkipWhile(odd).ToArray().Length == 0));
        Console.WriteLine("w_single_t1=" + (single.TakeWhile(less50).ToArray().Length == 1 && single.TakeWhile(less50).ToArray()[0] == 42));
        Console.WriteLine("w_single_s1=" + (single.SkipWhile(less50).ToArray().Length == 0));
        Console.WriteLine("w_single_t2=" + (single.TakeWhile(big50).ToArray().Length == 0));
        Console.WriteLine("w_single_s2=" + (single.SkipWhile(big50).ToArray().Length == 1 && single.SkipWhile(big50).ToArray()[0] == 42));
        // TakeWhile(<8) = {5,3}; SkipWhile(<8) = {9,1,7,2,8,4,6}; the concat
        // re-assembles the source IN ORDER (the while partition identity).
        int[] wpart = nums.TakeWhile(less8).Concat(nums.SkipWhile(less8)).ToArray();
        Console.WriteLine("w_part=" + (wpart.Length == 9 && wpart[0] == 5 && wpart[8] == 6));
    }
}}
