//------------------------------------------------------------------------------
// linqzip.cs -- LINQ COMBINATION-OPERATOR DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C64.
//
// SCOPE: the four sequence-COMBINATION / set-family operators the batch
// worklist names -- Zip / Concat / Distinct / Except -- probed DEEP:
//
//   1) ZIP -- element-wise pairing to the SHORTER length with a static
//      two-arg selector Func<int,int,int> (linq.h delegate contract):
//      equal lengths, first-shorter, second-shorter, single-element, and
//      EMPTY operand (Zip yields the empty sequence).
//   2) CONCAT -- append the second sequence after the first, PRESERVING
//      duplicates and order; empty-first / empty-second / both-empty;
//      multi-sequence chain; length = sum of parts.
//   3) DISTINCT -- remove duplicates keeping FIRST-APPEARANCE order
//      (default ordinal equality on int32); all-distinct / all-same /
//      single / empty / negatives; Distinct after a Wired-operator chain.
//   4) EXCEPT -- elements of the first not in the second, first-appearance
//      order, deduplicated output; empty-first / empty-second / no-overlap /
//      full-overlap / duplicates-in-both; Except after a Where chain.
//
// WIRED-SURFACE REALITY AT HEAD (D-LINQZ-1 resolved in the M6 linqzip batch,
// 2026-08-12): all four operators are wired. TryMapLinq (MethodMap.Linq.cs)
// maps Concat/Distinct/Except/Zip to the linq_map.cpp set-family icalls
// (il2cpp_linq_concat_i4 / distinct_i4 / except_i4 / zip_i4 -- int32 element
// family, eager materialization into a fresh int32[]). expected.txt is the
// net48 oracle frozen below and documents the INTENDED semantics; this case
// is the regression gate that verifies the four operators byte-for-byte
// against net48 (it was a FORWARD GREEN GATE at HEAD 4c2dd91 -- the two
// probe lines below aborted at MethodRefResolver.cs:296 with "generic
// instantiation of System.Linq.Enumerable.{Zip|Distinct} has no BCL mapping
// in M5", recorded in GAPS.md as D-LINQZ-1).
//
// All elements are int32 (the only stage-1 LINQ element family); every
// predicate/selector is a single-cast STATIC-method delegate (the linq.h /
// linq_map.h callback contract). Every line prints "label=True/False".
// net48 is the byte-exact baseline.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases { public static class LinqZip {
    // ---- static two-arg selectors (Zip) + predicates (chains) ----
    public static int Add(int a, int b) { return a + b; }
    public static int Times(int a, int b) { return a * b; }
    public static int Diff(int a, int b) { return a - b; }
    public static bool IsEven(int x) { return x % 2 == 0; }
    public static bool IsOdd(int x) { return (x & 1) == 1; }
    public static bool IsBig4(int x) { return x > 4; }
    public static bool IsNeg(int x) { return x < 0; }
    public static bool AlwaysTrue(int x) { return true; }

    public static void Main()
    {
        int[] a = { 1, 2, 3, 4, 5 };
        int[] b = { 10, 21, 30, 41, 50 };
        int[] short1 = { 1, 2, 3 };
        int[] short2 = { 100, 200 };
        int[] single = { 7 };
        int[] empty = new int[0];
        int[] dup = { 3, 1, 3, 2, 1, 4 };
        int[] negs = { -3, -1, -3, 0, -2, -1 };

        // ==================== 1. ZIP ====================
        // equal lengths: {1..5} x {10..50} with Add -> {11,22,33,44,55}
        int[] z = a.Zip(b, new Func<int, int, int>(Add)).ToArray();
        Console.WriteLine("zip_eq_len=" + (z.Length == 5));
        Console.WriteLine("zip_eq_0=" + (z[0] == 11));
        Console.WriteLine("zip_eq_4=" + (z[4] == 55));
        // first shorter: result length = min(3, 5) = 3
        int[] zs1 = a.Zip(short1, new Func<int, int, int>(Times)).ToArray();
        Console.WriteLine("zip_fs_len=" + (zs1.Length == 3));
        Console.WriteLine("zip_fs_2=" + (zs1[2] == 9));
        // second shorter: result length = min(5, 2) = 2
        int[] zs2 = a.Zip(short2, new Func<int, int, int>(Diff)).ToArray();
        Console.WriteLine("zip_ss_len=" + (zs2.Length == 2));
        Console.WriteLine("zip_ss_1=" + (zs2[1] == -198));
        // single-element pair
        int[] z1 = single.Zip(single, new Func<int, int, int>(Add)).ToArray();
        Console.WriteLine("zip_single_len=" + (z1.Length == 1));
        Console.WriteLine("zip_single_0=" + (z1[0] == 14));
        // EMPTY operand -> empty result (no selector invocation)
        int[] ze1 = a.Zip(empty, new Func<int, int, int>(Add)).ToArray();
        int[] ze2 = empty.Zip(a, new Func<int, int, int>(Add)).ToArray();
        Console.WriteLine("zip_empty_a=" + (ze1.Length == 0));
        Console.WriteLine("zip_empty_b=" + (ze2.Length == 0));
        // Zip on a derived chain (Where is wired; the pair is the gate)
        // a.Where(IsOdd)={1,3,5} x b.Where(IsEven)={10,30,50} -> {11,33,55}
        int[] zw = a.Where(IsOdd).Zip(b.Where(IsEven), new Func<int, int, int>(Add)).ToArray();
        Console.WriteLine("zip_where_0=" + (zw[0] == 11));
        Console.WriteLine("zip_where_2=" + (zw[2] == 55));
        Console.WriteLine("zip_where_len=" + (zw.Length == 3));

        // ==================== 2. CONCAT ====================
        // two non-empty: order preserved, duplicates kept
        int[] c1 = a.Concat(b).ToArray();
        Console.WriteLine("cc_len=" + (c1.Length == 10));
        Console.WriteLine("cc_4=" + (c1[4] == 5));
        Console.WriteLine("cc_5=" + (c1[5] == 10));
        // empty-first / empty-second / both-empty
        int[] ce1 = empty.Concat(a).ToArray();
        int[] ce2 = a.Concat(empty).ToArray();
        int[] ce3 = empty.Concat(empty).ToArray();
        Console.WriteLine("cc_ef=" + (ce1.Length == 5 && ce1[0] == 1));
        Console.WriteLine("cc_es=" + (ce2.Length == 5 && ce2[4] == 5));
        Console.WriteLine("cc_ee=" + (ce3.Length == 0));
        // duplicates preserved across the boundary
        int[] cd = dup.Concat(dup).ToArray();
        Console.WriteLine("cc_dup_len=" + (cd.Length == 12));
        Console.WriteLine("cc_dup_5=" + (cd[5] == 4));
        Console.WriteLine("cc_dup_6=" + (cd[6] == 3));
        // multi-sequence chain: (a.Concat(b)).Concat(short1)
        int[] c3 = a.Concat(b).Concat(short1).ToArray();
        Console.WriteLine("cc_chain_len=" + (c3.Length == 13));
        Console.WriteLine("cc_chain_9=" + (c3[9] == 50));
        Console.WriteLine("cc_chain_10=" + (c3[10] == 1));
        // Concat with a derived operand (Where wired)
        int[] cw = a.Where(IsEven).Concat(a.Where(IsOdd)).ToArray();
        Console.WriteLine("cc_partition_len=" + (cw.Length == 5));
        Console.WriteLine("cc_partition_0=" + (cw[0] == 2));
        Console.WriteLine("cc_partition_4=" + (cw[4] == 5));

        // ==================== 3. DISTINCT ====================
        // {3,1,3,2,1,4} -> {3,1,2,4} (first-appearance order)
        int[] d = dup.Distinct().ToArray();
        Console.WriteLine("di_len=" + (d.Length == 4));
        Console.WriteLine("di_0=" + (d[0] == 3));
        Console.WriteLine("di_1=" + (d[1] == 1));
        Console.WriteLine("di_2=" + (d[2] == 2));
        Console.WriteLine("di_3=" + (d[3] == 4));
        // all-distinct input: unchanged length + order
        int[] da = a.Distinct().ToArray();
        Console.WriteLine("di_alldist=" + (da.Length == 5 && da[0] == 1 && da[4] == 5));
        // all-same input: collapses to one element
        int[] same = { 7, 7, 7, 7 };
        int[] ds = same.Distinct().ToArray();
        Console.WriteLine("di_same_len=" + (ds.Length == 1));
        Console.WriteLine("di_same_0=" + (ds[0] == 7));
        // single / empty
        Console.WriteLine("di_single=" + (single.Distinct().Count() == 1));
        Console.WriteLine("di_empty=" + (empty.Distinct().Count() == 0));
        // negatives + zero: value-based dedup, first occurrence kept
        int[] dn = negs.Distinct().ToArray();
        Console.WriteLine("di_neg_len=" + (dn.Length == 4));
        Console.WriteLine("di_neg_0=" + (dn[0] == -3));
        Console.WriteLine("di_neg_3=" + (dn[3] == -2));
        // Distinct after a Wired-operator chain (Where -> Distinct)
        int[] dw = dup.Where(IsOdd).Distinct().ToArray();
        Console.WriteLine("di_where_len=" + (dw.Length == 2));
        Console.WriteLine("di_where_0=" + (dw[0] == 3));
        Console.WriteLine("di_where_1=" + (dw[1] == 1));
        // Distinct keeps only first occurrence of a value seen mid-chain
        // a.Concat(dup)={1,2,3,4,5,3,1,3,2,1,4} -> {1,2,3,4,5} (all dup values
        // already appear in a) -- first-appearance order preserved.
        int[] dm = a.Concat(dup).Distinct().ToArray();
        Console.WriteLine("di_aftercc_len=" + (dm.Length == 5));
        Console.WriteLine("di_aftercc_4=" + (dm[4] == 5));

        // ==================== 4. EXCEPT ====================
        // {3,1,3,2,1,4} minus {1} -> {3,2,4} (first appearance, dedup)
        int[] e = dup.Except(new int[] { 1 }).ToArray();
        Console.WriteLine("ex_len=" + (e.Length == 3));
        Console.WriteLine("ex_0=" + (e[0] == 3));
        Console.WriteLine("ex_1=" + (e[1] == 2));
        Console.WriteLine("ex_2=" + (e[2] == 4));
        // no overlap: everything returned (deduplicated) in first-appearance order
        int[] en = a.Except(new int[] { 90, 91 }).ToArray();
        Console.WriteLine("ex_noover=" + (en.Length == 5 && en[0] == 1 && en[4] == 5));
        // full overlap: empty result
        int[] ef = a.Except(a).ToArray();
        Console.WriteLine("ex_full=" + (ef.Length == 0));
        // empty-first: empty result; empty-second: deduplicated first
        Console.WriteLine("ex_ef=" + (empty.Except(a).Count() == 0));
        int[] es = dup.Except(empty).ToArray();
        Console.WriteLine("ex_es_len=" + (es.Length == 4));
        Console.WriteLine("ex_es_3=" + (es[3] == 4));
        // duplicates in the second: set membership, not count (1 removes all 1s)
        int[] e2 = dup.Except(new int[] { 1, 1 }).ToArray();
        Console.WriteLine("ex_dupsec_len=" + (e2.Length == 3));
        // multiple removed values from a derived chain
        int[] ew = dup.Where(IsEven).Except(new int[] { 2 }).ToArray();
        Console.WriteLine("ex_where_len=" + (ew.Length == 1));
        Console.WriteLine("ex_where_0=" + (ew[0] == 4));
        // negatives interplay
        int[] en2 = negs.Except(new int[] { -3 }).ToArray();
        Console.WriteLine("ex_neg_len=" + (en2.Length == 3));
        Console.WriteLine("ex_neg_0=" + (en2[0] == -1));
        Console.WriteLine("ex_neg_2=" + (en2[2] == -2));
    }
}}
