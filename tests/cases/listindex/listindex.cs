//------------------------------------------------------------------------------
// listindex.cs -- System.Collections.Generic.List<int> INDEX domain DEEP e2e
// reference case (net48 oracle, byte-for-byte). Created in e2e batch C82,
// AFTER the list-domain full chain (MethodMap.List.cs -> il2cpp_list_i4_*:
// create / create_with_capacity / Add / get_Item / set_Item / Insert / Remove /
// RemoveAt / IndexOf / Contains / Clear / get_Count / ToArray -- D-LISTQ-1..5,
// batch 133). listq / listdeep3 / listops3 pinned the operational surface; THIS
// case drives the INDEX dimension one stage DEEPER, on every boundary of the
// two index-searching contracts:
//
//   A. List<int>.IndexOf(T) FULL BOUNDARY (the 1-arg form -- the ONLY wired
//      IndexOf arm, MethodMap.List.cs:117; the range overloads IndexOf(T,int) /
//      IndexOf(T,int,int) are NOT wired, recorded D-LI-1): present at head /
//      middle / tail; absent -> -1 (also below-zero and above-max probes);
//      duplicates -> FIRST-match; empty list -> -1; single-element list; value
//      edges (int.MinValue / -1 / 0 / 1 / int.MaxValue); index drift after
//      set_Item / Remove / Insert / RemoveAt; IndexOf across multiple capacity
//      doublings (growth); IndexOf after Clear; Contains-IndexOf agreement.
//   B. BinarySearch FULL BOUNDARY -- the net48-equivalent COMPOSITION
//      List<int>.ToArray() -> Array.BinarySearch<int>(int[], int) (the wired
//      arraysort domain: MethodMap.Array.cs:67 icall_array_binary_search_int32,
//      MethodMap.ArraySort.cs:88 il2cpp_array_binary_search_int32 -- List's own
//      BinarySearch is NOT wired, recorded D-LI-2, the listops3 D-LO3-1
//      ToArray-bridge precedent): found at head / middle / tail; missing
//      returns the bitwise complement ~insertionPoint (below-first -> ~0,
//      between-elements -> ~k, above-last -> ~Length); empty array -> -1;
//      single element hit / below / above; DUPLICATES asserted only as
//      "the element at the returned index equals the value" (net48 makes no
//      which-duplicate guarantee, the runtime lower-bound picks one); value
//      edges int.MinValue / 0 / int.MaxValue; and a ToArray-snapshot
//      independence probe (mutating the list does not move the searched copy).
//
// Every line prints "label=True/False" (or a raw int); net48 is the byte
// reference. All probes stay on the wired surface (List<int>.IndexOf 1-arg +
// Array.BinarySearch<int>). Recorded gaps (NOT probed): D-LI-1 List.IndexOf
// range overloads, D-LI-2 List.BinarySearch -- see GAPS.md.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class ListIndex {
    public static void Main() {
        // ============ A. List<int>.IndexOf full boundary ============
        var a = new List<int>();
        for (int i = 0; i < 10; i++) a.Add(i * 10);        // 0 10 20 30 40 50 60 70 80 90
        Console.WriteLine("io_head=" + (a.IndexOf(0) == 0));
        Console.WriteLine("io_mid=" + (a.IndexOf(40) == 4));
        Console.WriteLine("io_tail=" + (a.IndexOf(90) == 9));
        Console.WriteLine("io_absent=" + (a.IndexOf(99) == -1));
        Console.WriteLine("io_below=" + (a.IndexOf(-5) == -1));

        var em = new List<int>();
        Console.WriteLine("io_empty=" + (em.IndexOf(1) == -1));
        em.Add(42);
        Console.WriteLine("io_single=" + (em.IndexOf(42) == 0));
        Console.WriteLine("io_single_miss=" + (em.IndexOf(43) == -1));

        // Duplicates -> first match.
        var dup = new List<int>();
        dup.Add(7); dup.Add(3); dup.Add(7); dup.Add(9); dup.Add(7);
        Console.WriteLine("io_dup_first=" + (dup.IndexOf(7) == 0));
        Console.WriteLine("io_dup_second=" + (dup.IndexOf(9) == 3));

        // Index drift after mutations.
        dup[0] = 1;                                       // set_Item: 1 3 7 9 7
        Console.WriteLine("io_after_set=" + (dup.IndexOf(7) == 2));
        dup.Remove(7);                                    // removes first 7: 1 3 9 7
        Console.WriteLine("io_after_remove=" + (dup.IndexOf(7) == 3));
        dup.Insert(1, 7);                                 // 1 7 3 9 7
        Console.WriteLine("io_after_insert=" + (dup.IndexOf(7) == 1));
        dup.RemoveAt(1);                                  // 1 3 9 7
        Console.WriteLine("io_after_rmat=" + (dup.IndexOf(7) == 3));

        // IndexOf across multiple capacity doublings (growth).
        var g = new List<int>();
        for (int i = 0; i < 60; i++) g.Add(i * 3);        // 0 3 6 ... 177
        Console.WriteLine("io_grow_head=" + (g.IndexOf(0) == 0));
        Console.WriteLine("io_grow_mid=" + (g.IndexOf(87) == 29));
        Console.WriteLine("io_grow_tail=" + (g.IndexOf(177) == 59));
        Console.WriteLine("io_grow_miss=" + (g.IndexOf(100) == -1));

        // Value edges.
        var ve = new List<int>();
        ve.Add(int.MinValue); ve.Add(-1); ve.Add(0); ve.Add(1); ve.Add(int.MaxValue);
        Console.WriteLine("io_min=" + (ve.IndexOf(int.MinValue) == 0));
        Console.WriteLine("io_neg=" + (ve.IndexOf(-1) == 1));
        Console.WriteLine("io_zero=" + (ve.IndexOf(0) == 2));
        Console.WriteLine("io_max=" + (ve.IndexOf(int.MaxValue) == 4));
        Console.WriteLine("io_miss_minp1=" + (ve.IndexOf(int.MinValue + 1) == -1));

        // Clear -> IndexOf -1.
        var c = new List<int>();
        for (int i = 0; i < 8; i++) c.Add(i);
        c.Clear();
        Console.WriteLine("io_clear=" + (c.IndexOf(0) == -1));

        // Contains-IndexOf agreement.
        Console.WriteLine("io_contains_agree=" + (a.Contains(40) == (a.IndexOf(40) != -1)));
        Console.WriteLine("io_contains_absent=" + (a.Contains(41) == (a.IndexOf(41) != -1)));

        // ============ B. BinarySearch full boundary (ToArray composition) ============
        var src = new List<int>();
        for (int i = 1; i <= 9; i += 2) src.Add(i);       // 1 3 5 7 9 (ascending)
        int[] sa = src.ToArray();
        Console.WriteLine("bs_head=" + (Array.BinarySearch<int>(sa, 1) == 0));
        Console.WriteLine("bs_mid=" + (Array.BinarySearch<int>(sa, 5) == 2));
        Console.WriteLine("bs_tail=" + (Array.BinarySearch<int>(sa, 9) == 4));
        Console.WriteLine("bs_below=" + (Array.BinarySearch<int>(sa, 0) == ~0));
        Console.WriteLine("bs_between=" + (Array.BinarySearch<int>(sa, 4) == ~2));
        Console.WriteLine("bs_above=" + (Array.BinarySearch<int>(sa, 10) == ~5));

        // Snapshot independence: mutating the list does not move the searched copy.
        src.Add(11);
        Console.WriteLine("bs_snap_indep=" + (sa.Length == 5 && sa[4] == 9));
        src.RemoveAt(0);
        Console.WriteLine("bs_snap_indep2=" + (sa[0] == 1));

        // Empty array -> -1; single element hit/below/above.
        int[] earr = new int[0];
        Console.WriteLine("bs_empty=" + (Array.BinarySearch<int>(earr, 5) == -1));
        int[] sarr = { 42 };
        Console.WriteLine("bs_single_hit=" + (Array.BinarySearch<int>(sarr, 42) == 0));
        Console.WriteLine("bs_single_below=" + (Array.BinarySearch<int>(sarr, 41) == ~0));
        Console.WriteLine("bs_single_above=" + (Array.BinarySearch<int>(sarr, 43) == ~1));

        // Duplicates: assert only "element at result == value" (net48 makes no
        // which-duplicate guarantee; the runtime lower-bound picks one).
        int[] darr = { 1, 2, 2, 2, 3 };
        int di = Array.BinarySearch<int>(darr, 2);
        Console.WriteLine("bs_dup=" + (di >= 0 && darr[di] == 2));

        // Value edges in the searched array.
        int[] varr = { int.MinValue, 0, int.MaxValue };
        Console.WriteLine("bs_min=" + (Array.BinarySearch<int>(varr, int.MinValue) == 0));
        Console.WriteLine("bs_zero=" + (Array.BinarySearch<int>(varr, 0) == 1));
        Console.WriteLine("bs_max=" + (Array.BinarySearch<int>(varr, int.MaxValue) == 2));
        Console.WriteLine("bs_min_miss=" + (Array.BinarySearch<int>(varr, int.MinValue + 1) == ~1));

        Console.WriteLine("done");
    }
}}
