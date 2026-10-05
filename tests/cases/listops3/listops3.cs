//------------------------------------------------------------------------------
// listops3.cs -- System.Collections.Generic.List<int> FULL-OPERATION DEEP e2e
// reference case #3 (net48 oracle, byte-for-byte). Created in e2e batch C78,
// AFTER the list-domain full-chain: the List<int32> AOT expansion surface
// (MethodMap.List.cs -> il2cpp_list_i4_*: create/create_with_capacity/Add/
// get_Item/set_Item/Insert/Remove/RemoveAt/IndexOf/Contains/Clear/Count/
// ToArray -- D-LISTQ-1..5, batch 133) PLUS the array-function domain
// (Array.Sort/Reverse int32 icalls + the arrayext2 predicate icalls
// Find/FindAll/Exists/TrueForAll with the single-cast static-delegate ABI,
// arrfuncs precedent). listq pins the list-only surface; this case drives
// the COMBINATION dimension -- list operations feeding Array.Sort/Reverse/
// FindAll through ToArray()'s independent int[] snapshot, the same
// compose-then-verify shape arrayfuncs2 used for the array domain.
//
// NOTE on List<T>.Sort/Reverse/FindAll (instance methods): NOT wired -- the
// List<int32> AOT expansion has no sort/reverse/findall icall arms
// (MethodMap.List.cs ends at ToArray), so a call resolves to the
// generated-name path -> undefined C++ symbol -> build fail (probe verified
// 2026-08-13). The net48-equivalent composition here goes List<int> ->
// ToArray() -> Array.Sort / Array.Reverse / Array.FindAll, which IS the
// wired surface (D-LO3-1, GAPS.md).
//
// Coverage:
//   1. ToArray snapshot independence: the int[] is a fresh copy -- mutating
//      the list after ToArray leaves the snapshot untouched and vice versa.
//   2. Sort+Reverse composition on the snapshot: sort ascending, reverse ->
//      descending, sort again -> ascending (the pair is involutive).
//   3. FindAll on the snapshot with static predicates: filter evens, filter
//      > threshold, all-match, no-match (empty result), source untouched.
//   4. FindAll -> Sort composition: the filtered subset sorts independently
//      of the source order.
//   5. Reverse -> FindAll composition: filtering a reversed snapshot keeps
//      the reversed source order.
//   6. Find / Exists / TrueForAll on list-fed snapshots (first-match value,
//      any-match, all-match incl. empty -> vacuous true).
//   7. A scripted List<int> build via Insert/Remove/RemoveAt/set_Item whose
//      final element sequence is asserted element-by-element, then fed to a
//      sort and a reverse -- order stability across the bridge.
//   8. Capacity-ctor + growth list feeding a sort.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class ListOps3 {
    private static bool IsEven(int x) { return (x & 1) == 0; }
    private static bool IsBig(int x) { return x > 20; }
    private static bool IsSmall(int x) { return x < 6; }
    private static bool AlwaysTrue(int x) { return true; }

    private static bool IsSorted(int[] a)
    {
        for (int i = 1; i < a.Length; ++i)
            if (a[i - 1] > a[i]) return false;
        return true;
    }

    public static void Main() {
        // ============ 1. ToArray snapshot independence ============
        List<int> l = new List<int>();
        for (int i = 1; i <= 5; ++i) l.Add(i * 10);          // 10 20 30 40 50
        int[] snap = l.ToArray();
        Console.WriteLine("snap_len=" + (snap.Length == 5));
        Console.WriteLine("snap_ok=" + (snap[0] == 10 && snap[4] == 50 && IsSorted(snap)));
        l.Add(60);                                           // list mutates
        Console.WriteLine("snap_indep=" + (snap.Length == 5 && snap[4] == 50));
        Console.WriteLine("list_cnt=" + (l.Count == 6));
        snap[0] = 999;                                       // snapshot mutates
        Console.WriteLine("list_indep=" + (l[0] == 10));

        // ============ 2. Sort + Reverse composition (involutive pair) ============
        List<int> c = new List<int> { 9, 1, 5, 3, 7, 2, 8, 4, 6 };
        int[] ca = c.ToArray();
        Array.Sort(ca);
        Console.WriteLine("s_asc=" + IsSorted(ca));
        Array.Reverse(ca);
        Console.WriteLine("s_desc=" + (ca[0] == 9 && ca[1] == 8 && ca[2] == 7 && ca[3] == 6 && ca[4] == 5 && ca[5] == 4 && ca[6] == 3 && ca[7] == 2 && ca[8] == 1));
        Array.Sort(ca);
        Console.WriteLine("s_asc2=" + IsSorted(ca));
        Console.WriteLine("s_full=" + (ca[0] == 1 && ca[8] == 9));

        // ============ 3. FindAll on the snapshot (static predicates) ============
        int[] evens = Array.FindAll(ca, IsEven);             // ca = 1..9
        Console.WriteLine("f_even_len=" + (evens.Length == 4));
        Console.WriteLine("f_even_val=" + (evens[0] == 2 && evens[1] == 4 && evens[2] == 6 && evens[3] == 8));
        int[] big = Array.FindAll(ca, IsBig);                // none > 20
        Console.WriteLine("f_big_len=" + (big.Length == 0));
        int[] all = Array.FindAll(ca, AlwaysTrue);
        Console.WriteLine("f_all_len=" + (all.Length == ca.Length));
        Console.WriteLine("f_src_untouched=" + (ca[0] == 1 && ca[8] == 9)); // source never modified

        // ============ 4. FindAll -> Sort composition ============
        int[] unsorted = { 8, 3, 10, 5, 6, 1 };
        int[] small = Array.FindAll(unsorted, IsSmall);      // 3 5 1 (x<6)
        Console.WriteLine("fs_small_len=" + (small.Length == 3));
        Array.Sort(small);
        Console.WriteLine("fs_sorted=" + (small[0] == 1 && small[1] == 3 && small[2] == 5));
        Console.WriteLine("fs_src=" + (unsorted[0] == 8 && unsorted[5] == 1)); // filter does not reorder source

        // ============ 5. Reverse -> FindAll composition ============
        int[] rv = new int[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 };  // reverse of sorted ca (1..9)
        int[] rvEven = Array.FindAll(rv, IsEven);
        Console.WriteLine("rf_len=" + (rvEven.Length == 4));
        Console.WriteLine("rf_rev_order=" + (rvEven[0] == 8 && rvEven[1] == 6 && rvEven[2] == 4 && rvEven[3] == 2));
        int[] rvSmall = Array.FindAll(rv, IsSmall);
        Console.WriteLine("rf_small_len=" + (rvSmall.Length == 5));
        Console.WriteLine("rf_small_order=" + (rvSmall[0] == 5 && rvSmall[4] == 1));

        // ============ 6. Find / Exists / TrueForAll on list-fed snapshots ============
        List<int> g = new List<int>();
        for (int i = 1; i <= 8; ++i) g.Add(i * 3);          // 3 6 9 12 15 18 21 24
        int[] ga = g.ToArray();
        Console.WriteLine("t_find_first_even=" + (Array.Find(ga, IsEven) == 6));
        Console.WriteLine("t_find_miss=" + (Array.Find(ga, IsSmall) == 3)); // first x<6 is 3
        Console.WriteLine("t_exists=" + Array.Exists(ga, IsBig));
        Console.WriteLine("t_exists_neg=" + (Array.Exists(new[] { 1, 2 }, IsBig) == false));
        Console.WriteLine("t_trueforall=" + Array.TrueForAll(ga, AlwaysTrue));
        Console.WriteLine("t_notall=" + (Array.TrueForAll(ga, IsEven) == false));

        // ============ 7. Scripted build (Insert/Remove/RemoveAt/set_Item) + sort ============
        List<int> s = new List<int>();
        s.Add(4);
        s.Insert(0, 1);                                      // 1 4
        s.Insert(2, 9);                                      // 1 4 9
        s.Insert(1, 7);                                      // 1 7 4 9
        s.Remove(7);                                         // 1 4 9
        s.RemoveAt(0);                                       // 4 9
        s.Add(2);                                            // 4 9 2
        s[1] = 3;                                            // 4 3 2
        Console.WriteLine("s_seq=" + (s.Count == 3 && s[0] == 4 && s[1] == 3 && s[2] == 2));
        int[] sa = s.ToArray();
        Array.Sort(sa);
        Console.WriteLine("s_sorted=" + (sa[0] == 2 && sa[1] == 3 && sa[2] == 4));
        Array.Reverse(sa);
        Console.WriteLine("s_desc=" + (sa[0] == 4 && sa[1] == 3 && sa[2] == 2));
        Console.WriteLine("s_list_unchanged=" + (s[0] == 4 && s[1] == 3 && s[2] == 2));

        // ============ 8. Capacity-ctor + growth feeding a sort ============
        List<int> cap = new List<int>(2);
        for (int i = 0; i < 6; ++i) cap.Add(5 - i);         // 5 4 3 2 1 0 (two doublings)
        Console.WriteLine("c_grow=" + (cap.Count == 6));
        int[] capa = cap.ToArray();
        Array.Sort(capa);
        Console.WriteLine("c_sorted=" + (capa[0] == 0 && capa[5] == 5));
        Console.WriteLine("c_order_ok=" + IsSorted(capa));

        Console.WriteLine("done");
    }
}}
