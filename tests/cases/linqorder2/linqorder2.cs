//------------------------------------------------------------------------------
// linqorder2.cs -- System.Linq Enumerable.OrderBy DEEP e2e reference case #2
// (net48 oracle, byte-for-byte). Created in e2e batch C82, AFTER the LINQ sort
// domain full chain (MethodMap.Linq.cs OrderBy ->
// il2cpp_linq_order_by_key_i4, linq_sort.h:82 / linq_sort.cpp SortInt32KeyCopy:
// int32 elements + int32 keys, key cached EXACTLY ONCE per element -- net48
// EnumerableSorter semantics -- then a STABLE merge sort on the cached keys,
// result a fresh array, source never written). linqops / linqgroup / linqagg /
// enumeratordeep / linqchain2 pinned the operator + chain surface; THIS case
// drives the SORT-CORRECTNESS dimensions of the key-selector bridge one stage
// deeper:
//
//   1. KEY-CACHE-ONCE: a side-effecting key selector with a static call
//      counter -- after OrderBy(...).ToArray() the counter must equal the
//      source length (net48 EnumerableSorter caches keys once; the runtime
//      SortInt32KeyCopy does the same). A re-invoking implementation would
//      over-count.
//   2. STABILITY UNDER EQUAL KEYS: equal-key elements keep their SOURCE order
//      (net48 OrderBy is stable; the runtime merge sort is stable) -- probed
//      with index-marked elements (value = key*10 + source-rank) so an
//      unstable sort is distinguishable.
//   3. ThenBy SEMANTICS -- DIRECT (D-LO2-1 FIXED): Enumerable.ThenBy /
//      ThenByDescending are now WIRED (MethodMap.Linq.cs ThenBy arm ->
//      ResolvedMethod.ThenByCombineMarker; StackFold rewrites
//      OrderBy(k1).ThenBy(k2) into ONE il2cpp_linq_then_by_key[_desc]_i4
//      call -- the fused (k1 asc, k2 asc/desc) composite sort, net48
//      EnumerableSorter chain semantics). Section 8 probes the DIRECT chain:
//      composite-key equivalence (the old bridge, removed), equal-primary-key
//      groups ordered by the SECONDARY key, primary groups kept lexicographic
//      (a naive whole-array secondary re-sort would interleave them), stability
//      under equal secondary keys, both selectors cached EXACTLY ONCE, and
//      empty/single edge chains.
//   4. KEY FAMILIES: identity ascending; NEGATED key (descending emulation --
//      OrderByDescending is likewise not wired); modulus ties; negative values;
//      zero; int.MinValue / int.MaxValue as keys.
//   5. COPY SEMANTICS: the source array is NEVER modified by OrderBy, and the
//      result is a FRESH array (mutating the source after the sort does not
//      move the sorted snapshot).
//   6. COMPOSITION: Where -> OrderBy, Select -> OrderBy, OrderBy -> Where /
//      Select, OrderBy -> ToArray -> downstream operators (First / Take / Sum /
//      Count / Min / Max / Any / All).
//   7. EDGE INPUTS: empty array, single element, already-sorted, reverse,
//      all-equal keys, zero-length selector chain.
//
// Every line prints "label=True/False"; net48 is the byte reference. All
// probes stay on the wired surface (OrderBy<int,int> + ThenBy<int,int>).
// Remaining gap (NOT wired): OrderByDescending / OrderByDescending.ThenBy
// chains -- see GAPS.md.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases { public static class LinqOrder2 {
    private static int _calls = 0;
    private static int IdKey(int x) { return x; }
    private static int NegKey(int x) { return -x; }
    private static int Mod3Key(int x) { return x % 3; }
    private static int TensKey(int x) { return x / 10; }
    private static int UnitsKey(int x) { return x % 10; }
    private static int CompositeKey(int x) { return (x / 10) * 100 + (x % 10); }
    private static int CountingKey(int x) { _calls++; return x; }
    private static int _calls2 = 0;
    private static int UnitsCountingKey(int x) { _calls2++; return x % 10; }

    public static void Main() {
        // ============ 1. key-cache-once ============
        int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
        _calls = 0;
        int[] once = nums.OrderBy(CountingKey).ToArray();
        Console.WriteLine("cache_calls=" + (_calls == nums.Length));
        Console.WriteLine("cache_len=" + (once.Length == 9));

        // ============ 2. stability under equal keys ============
        // Elements: value = key*10 + source-rank, sorted by the TENS key
        // (x/10). Keys: 5,3,5,1,5,3,1,3,5 -- equal-key groups {11,12},
        // {32,33,31}, {51,53,54,52}. A stable sort keeps, WITHIN each key
        // group, the SOURCE order (unstable sorts reorder those
        // indistinguishable-by-key but distinguishable-by-unit elements).
        int[] marked = { 51, 32, 53, 11, 54, 33, 12, 31, 52 };
        int[] stable = marked.OrderBy(TensKey).ToArray();
        Console.WriteLine("stab_keys=" + (stable[0] / 10 == 1 && stable[1] / 10 == 1 &&
                                          stable[2] / 10 == 3 && stable[3] / 10 == 3 && stable[4] / 10 == 3 &&
                                          stable[5] / 10 == 5 && stable[6] / 10 == 5 && stable[7] / 10 == 5 && stable[8] / 10 == 5));
        Console.WriteLine("stab_units=" + (stable[0] % 10 == 1 && stable[1] % 10 == 2 &&
                                           stable[2] % 10 == 2 && stable[3] % 10 == 3 && stable[4] % 10 == 1 &&
                                           stable[5] % 10 == 1 && stable[6] % 10 == 3 && stable[7] % 10 == 4 && stable[8] % 10 == 2));
        Console.WriteLine("stab_full=" + (stable[0] == 11 && stable[1] == 12 && stable[2] == 32 &&
                                          stable[3] == 33 && stable[4] == 31 && stable[5] == 51 &&
                                          stable[6] == 53 && stable[7] == 54 && stable[8] == 52));

        // ============ 3. ThenBy semantics via composite key ============
        int[] pairs = { 53, 14, 78, 32, 61, 97, 45, 89, 26 };
        int[] comp = pairs.OrderBy(CompositeKey).ToArray();
        Console.WriteLine("comp_sorted=" + (comp[0] == 14 && comp[1] == 26 && comp[2] == 32 &&
                                            comp[3] == 45 && comp[4] == 53 && comp[5] == 61 &&
                                            comp[6] == 78 && comp[7] == 89 && comp[8] == 97));
        bool tensAsc = true, unitsAscInTens = true;
        for (int i = 1; i < comp.Length; i++)
        {
            if (TensKey(comp[i - 1]) > TensKey(comp[i])) tensAsc = false;
        }
        for (int i = 1; i < comp.Length; i++)
        {
            if (TensKey(comp[i - 1]) == TensKey(comp[i]) && UnitsKey(comp[i - 1]) > UnitsKey(comp[i]))
                unitsAscInTens = false;
        }
        Console.WriteLine("comp_tens_asc=" + tensAsc);
        Console.WriteLine("comp_units_in_tens=" + unitsAscInTens);

        // ============ 4. key families ============
        int[] asc = nums.OrderBy(IdKey).ToArray();
        Console.WriteLine("fam_asc=" + (asc[0] == 1 && asc[1] == 2 && asc[2] == 3 && asc[3] == 4 &&
                                        asc[4] == 5 && asc[5] == 6 && asc[6] == 7 && asc[7] == 8 && asc[8] == 9));
        int[] desc = nums.OrderBy(NegKey).ToArray();
        Console.WriteLine("fam_desc=" + (desc[0] == 9 && desc[1] == 8 && desc[2] == 7 && desc[3] == 6 &&
                                         desc[4] == 5 && desc[5] == 4 && desc[6] == 3 && desc[7] == 2 && desc[8] == 1));
        int[] byMod = nums.OrderBy(Mod3Key).ToArray();
        bool modAsc = true;
        for (int i = 1; i < byMod.Length; i++)
            if (Mod3Key(byMod[i - 1]) > Mod3Key(byMod[i])) modAsc = false;
        Console.WriteLine("fam_mod_asc=" + modAsc);
        int[] negs = { -3, 7, -1, 0, -9, 4 };
        int[] negSorted = negs.OrderBy(IdKey).ToArray();
        Console.WriteLine("fam_negs=" + (negSorted[0] == -9 && negSorted[1] == -3 && negSorted[2] == -1 &&
                                         negSorted[3] == 0 && negSorted[4] == 4 && negSorted[5] == 7));
        int[] edges = { int.MaxValue, int.MinValue, 0 };
        int[] edgeSorted = edges.OrderBy(IdKey).ToArray();
        Console.WriteLine("fam_edge=" + (edgeSorted[0] == int.MinValue && edgeSorted[1] == 0 && edgeSorted[2] == int.MaxValue));

        // ============ 5. copy semantics ============
        int[] src = { 8, 3, 6, 1, 9 };
        int[] copy = src.OrderBy(IdKey).ToArray();
        src[0] = 999;
        src[4] = -999;
        Console.WriteLine("copy_fresh=" + (copy[0] == 1 && copy[4] == 9));
        Console.WriteLine("copy_src_dirty=" + (src[0] == 999 && src[4] == -999));
        int[] src2 = { 8, 3, 6, 1, 9 };
        int[] copy2 = src2.OrderBy(IdKey).ToArray();
        Console.WriteLine("copy_src_untouched=" + (src2[0] == 8 && src2[1] == 3 && src2[2] == 6 && src2[3] == 1 && src2[4] == 9));

        // ============ 6. composition ============
        Console.WriteLine("cmp_where_ob=" + (nums.Where(x => x % 2 == 0).OrderBy(IdKey).First() == 2));
        Console.WriteLine("cmp_sel_ob=" + (nums.Select(x => x * 10).OrderBy(IdKey).First() == 10));
        Console.WriteLine("cmp_ob_where=" + (nums.OrderBy(IdKey).Where(x => x % 2 == 1).Count() == 5));
        Console.WriteLine("cmp_ob_sel=" + (nums.OrderBy(NegKey).Select(x => x * 2).First() == 18));
        Console.WriteLine("cmp_ob_take=" + (nums.OrderBy(IdKey).Take(3).Sum() == 6));
        Console.WriteLine("cmp_ob_count=" + (nums.OrderBy(IdKey).Count() == 9));
        Console.WriteLine("cmp_ob_minmax=" + (nums.OrderBy(IdKey).Min() == 1 && nums.OrderBy(NegKey).Max() == 9));
        Console.WriteLine("cmp_ob_any=" + (nums.OrderBy(IdKey).Any(x => x > 8)));
        Console.WriteLine("cmp_ob_all=" + (nums.OrderBy(IdKey).All(x => x > 0)));

        // ============ 7. edge inputs ============
        int[] empty = new int[0];
        Console.WriteLine("edge_empty=" + (empty.OrderBy(IdKey).Count() == 0));
        int[] single = { 42 };
        Console.WriteLine("edge_single=" + (single.OrderBy(IdKey).First() == 42));
        int[] sortedIn = { 1, 2, 3, 4, 5 };
        Console.WriteLine("edge_already=" + (sortedIn.OrderBy(IdKey).ToArray()[4] == 5));
        int[] revIn = { 9, 8, 7, 6, 5 };
        Console.WriteLine("edge_rev=" + (revIn.OrderBy(IdKey).First() == 5 && revIn.OrderBy(IdKey).ToArray()[4] == 9));
        int[] allEq = { 7, 7, 7, 7 };
        Console.WriteLine("edge_alleq_len=" + (allEq.OrderBy(IdKey).Count() == 4));
        int[] allEqOnce = allEq.OrderBy(IdKey).ToArray();
        Console.WriteLine("edge_alleq_vals=" + (allEqOnce[0] == 7 && allEqOnce[3] == 7));

        // ============ 8. ThenBy / ThenByDescending DIRECT (D-LO2-1) ============
        // The composite-key bridge is now exercised directly: the fold fuses
        // OrderBy(k1).ThenBy(k2) into one il2cpp_linq_then_by_key_i4 call
        // (net48 EnumerableSorter chain -- (k1 asc, k2 asc), each key cached
        // EXACTLY ONCE). Probe 1: direct ThenBy equals the composite-key
        // OrderBy (the D-LO2-1 equivalence, bridge removed).
        int[] tb = pairs.OrderBy(TensKey).ThenBy(UnitsKey).ToArray();
        Console.WriteLine("thenby_eq_comp=" + (tb.Length == comp.Length &&
            tb[0] == comp[0] && tb[1] == comp[1] && tb[2] == comp[2] && tb[3] == comp[3] &&
            tb[4] == comp[4] && tb[5] == comp[5] && tb[6] == comp[6] && tb[7] == comp[7] && tb[8] == comp[8]));
        // Equal-PRIMARY-key groups: within each tens group the elements order
        // by the SECONDARY key (the net48 "equal primary -> secondary order"
        // probe). Interleaved secondary keys across groups must NOT break the
        // primary group order (a naive whole-array secondary re-sort would).
        int[] grp = { 53, 54, 51, 12, 11 };
        int[] tbg = grp.OrderBy(TensKey).ThenBy(UnitsKey).ToArray();
        Console.WriteLine("thenby_grp=" + (tbg[0] == 11 && tbg[1] == 12 && tbg[2] == 51 && tbg[3] == 53 && tbg[4] == 54));
        bool grpLex = true;
        for (int i = 1; i < tbg.Length; i++)
            if (TensKey(tbg[i - 1]) > TensKey(tbg[i])) grpLex = false;
        Console.WriteLine("thenby_grp_lex=" + grpLex);
        // ThenByDescending: the SECONDARY key descends WITHIN each primary
        // group; the primary key stays ascending.
        int[] tbd = grp.OrderBy(TensKey).ThenByDescending(UnitsKey).ToArray();
        Console.WriteLine("thenbydesc_grp=" + (tbd[0] == 12 && tbd[1] == 11 && tbd[2] == 54 && tbd[3] == 53 && tbd[4] == 51));
        bool descLex = true;
        for (int i = 1; i < tbd.Length; i++)
            if (TensKey(tbd[i - 1]) > TensKey(tbd[i])) descLex = false;
        Console.WriteLine("thenbydesc_grp_lex=" + descLex);
        // Stable within a group when the SECONDARY keys tie (source order).
        int[] stb = { 21, 12, 22, 11 };
        int[] tbs = stb.OrderBy(TensKey).ThenBy(UnitsKey).ToArray();
        Console.WriteLine("thenby_stab=" + (tbs[0] == 11 && tbs[1] == 12 && tbs[2] == 21 && tbs[3] == 22));
        // Each selector is called EXACTLY ONCE per element (net48
        // EnumerableSorter caches BOTH keys -- a re-invoking implementation
        // would over-count).
        _calls = 0; _calls2 = 0;
        int[] cnt = nums.OrderBy(CountingKey).ThenBy(UnitsCountingKey).ToArray();
        Console.WriteLine("thenby_cache1=" + (_calls == nums.Length));
        Console.WriteLine("thenby_cache2=" + (_calls2 == nums.Length));
        // Edge inputs through the chain.
        Console.WriteLine("thenby_edge_empty=" + (empty.OrderBy(TensKey).ThenBy(UnitsKey).Count() == 0));
        Console.WriteLine("thenby_edge_single=" + (single.OrderBy(TensKey).ThenByDescending(UnitsKey).First() == 42));

        Console.WriteLine("done");
    }
}}
