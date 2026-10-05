//------------------------------------------------------------------------------
// arraychain.cs -- System.Array chain-combination DEEP e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C60, AFTER the array
// predicate domain (arrayext3) and the sort domain (arraysortdeep) are fully
// wired and PASSing.
//
// THIS case drives the FULL-DOMAIN CHAINS: the output of one wired array
// icall feeds the input of the next across stages --
//
//   1) ConvertAll -> Find: the converter result (NEW array, source never
//      mutated) feeds the predicate Find; also Exists / TrueForAll on the
//      converted array.
//   2) ConvertAll -> Sort -> BinarySearch: convert, sort in place, then a
//      BinarySearch hit / MISS (complement of insertion point) / IndexOf.
//   3) ConvertAll o ConvertAll (nested transforms) -> Find / Exists /
//      TrueForAll on the double-transformed array.
//   4) FindIndex 4-arg window over a converted array -> element read-back.
//   5) ConvertAll -> Resize -> Sort: the resized window re-sorted.
//   6) Reverse after Sort; Sort<T>(T[], Comparison<T>) in a chain.
//   7) EMPTY arrays flowing through ConvertAll / Find / Sort / BinarySearch
//      (default(int) Find result, vacuous TrueForAll, complement miss).
//   8) Source-immutability pins: ConvertAll / Find never touch the input.
//
// The delegate ABI is the single-cast STATIC methodPtr form (the arrfuncs
// pattern) -- every predicate/converter/comparison is a static method.
// Every printed line is "label=True/False"; net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ArrayChainCase {
    private static bool IsEven(int x) { return (x & 1) == 0; }
    private static bool IsBig(int x) { return x > 5; }
    private static bool IsBig100(int x) { return x > 100; }
    private static bool IsNeg(int x) { return x < 0; }
    private static int Dbl(int x) { return x * 2; }
    private static int Sq(int x) { return x * x; }
    private static int Neg(int x) { return -x; }
    private static int Desc(int a, int b) { return a < b ? 1 : (a > b ? -1 : 0); }

    public static void Main() {
        // ============ 1. ConvertAll -> Find (source never mutated) =========
        int[] src = { 1, 2, 3, 4, 5 };
        int[] c = Array.ConvertAll(src, Dbl);                 // {2,4,6,8,10}
        Console.WriteLine("conv_len=" + (c.Length == 5));
        Console.WriteLine("conv_elems=" + (c[0] == 2 && c[1] == 4 && c[2] == 6 && c[3] == 8 && c[4] == 10));
        Console.WriteLine("conv_src_unchanged=" + (src[0] == 1 && src[1] == 2 && src[2] == 3 && src[3] == 4 && src[4] == 5));
        Console.WriteLine("conv_find_even=" + (Array.Find(c, IsEven) == 2));
        Console.WriteLine("conv_find_big=" + (Array.Find(c, IsBig) == 6));
        Console.WriteLine("conv_exists=" + Array.Exists(c, IsBig));
        Console.WriteLine("conv_trueforall=" + Array.TrueForAll(c, IsEven));
        Console.WriteLine("conv_src_after_find=" + (src[4] == 5));

        // ============ 2. ConvertAll -> Sort -> BinarySearch / IndexOf =====
        int[] c2 = Array.ConvertAll(src, Dbl);                // fresh {2,4,6,8,10}
        Array.Sort(c2);                                       // already sorted
        Console.WriteLine("csort_hit=" + (Array.BinarySearch(c2, 10) == 4));
        Console.WriteLine("csort_miss=" + (Array.BinarySearch(c2, 7) < 0));
        // miss == complement of insertion point 3 (~3 == -4) on {2,4,6,8,10}.
        Console.WriteLine("csort_miss_complement=" + (Array.BinarySearch(c2, 7) == -4));
        Console.WriteLine("csort_indexof=" + (Array.IndexOf(c2, 6) == 2));

        // reversed input: sort makes it ascending
        int[] rev = { 5, 3, 9, 1, 7 };
        int[] c3 = Array.ConvertAll(rev, Dbl);               // {10,6,18,2,14}
        Array.Sort(c3);                                       // {2,6,10,14,18}
        Console.WriteLine("csort_rev_sorted=" + (c3[0] == 2 && c3[1] == 6 && c3[2] == 10 && c3[3] == 14 && c3[4] == 18));
        Console.WriteLine("csort_rev_hit=" + (Array.BinarySearch(c3, 14) == 3));
        Console.WriteLine("csort_rev_miss=" + (Array.BinarySearch(c3, 9) == -3));  // insert between 6(idx1) and 10(idx2) -> ~2 == -3
        Console.WriteLine("csort_rev_indexof=" + (Array.IndexOf(c3, 18) == 4));

        // ============ 3. ConvertAll o ConvertAll -> Find / Exists =========
        int[] d = Array.ConvertAll(src, Sq);                 // {1,4,9,16,25}
        int[] e = Array.ConvertAll(d, Dbl);                  // {2,8,18,32,50}
        Console.WriteLine("nested_elems=" + (e[0] == 2 && e[1] == 8 && e[2] == 18 && e[3] == 32 && e[4] == 50));
        Console.WriteLine("nested_find_big=" + (Array.Find(e, IsBig) == 8));
        Console.WriteLine("nested_exists_big100=" + (Array.Exists(e, IsBig100) == false));
        Console.WriteLine("nested_trueforall_even=" + Array.TrueForAll(e, IsEven));
        Console.WriteLine("nested_mid_unchanged=" + (d[4] == 25));

        // ============ 4. FindIndex 4-arg window over a converted array ====
        Console.WriteLine("fi_window=" + (Array.FindIndex(d, 1, 2, IsBig) == 2));  // window [4,9]: first >5 is 9 at idx2
        Console.WriteLine("fi_window_none=" + (Array.FindIndex(d, 0, 2, IsBig) == -1)); // window [1,4]
        Console.WriteLine("fi_window_readback=" + (d[Array.FindIndex(d, 0, 5, IsBig)] == 9));

        // ============ 5. ConvertAll -> Resize -> Sort =====================
        int[] r = Array.ConvertAll(rev, Dbl);                // {10,6,18,2,14}
        Array.Resize(ref r, 3);                              // {10,6,18}
        Console.WriteLine("resize_len=" + (r.Length == 3));
        Array.Sort(r);                                       // {6,10,18}
        Console.WriteLine("resize_sorted=" + (r[0] == 6 && r[1] == 10 && r[2] == 18));
        Console.WriteLine("resize_binsearch=" + (Array.BinarySearch(r, 10) == 1));

        // ============ 6. Reverse after Sort + Comparison<T> chain =========
        int[] rr = { 3, 1, 4, 1, 5 };
        Array.Sort(rr);                                      // {1,1,3,4,5}
        Array.Reverse(rr);                                   // {5,4,3,1,1}
        Console.WriteLine("sort_reverse=" + (rr[0] == 5 && rr[1] == 4 && rr[2] == 3 && rr[3] == 1 && rr[4] == 1));
        Console.WriteLine("reverse_indexof=" + (Array.IndexOf(rr, 3) == 2));
        int[] cc = { 3, 1, 4, 1, 5 };
        Array.Sort(cc, new Comparison<int>(Desc));           // {5,4,3,1,1}
        Console.WriteLine("cmp_desc=" + (cc[0] == 5 && cc[1] == 4 && cc[2] == 3 && cc[3] == 1 && cc[4] == 1));
        Console.WriteLine("cmp_desc_first=" + (Array.Find(cc, IsEven) == 4));

        // ============ 7. empty arrays through the chain ===================
        int[] empty = new int[0];
        int[] ce = Array.ConvertAll(empty, Dbl);
        Console.WriteLine("empty_conv_len=" + (ce.Length == 0));
        Console.WriteLine("empty_find_default=" + (Array.Find(empty, IsEven) == 0));
        Console.WriteLine("empty_exists=" + (Array.Exists(empty, IsBig) == false));
        Console.WriteLine("empty_trueforall=" + Array.TrueForAll(empty, IsBig));
        Array.Sort(empty);
        Console.WriteLine("empty_sort_binsearch=" + (Array.BinarySearch(empty, 1) == -1));

        // ============ 8. negative + zero elements through the chain =======
        int[] neg = { -3, 0, 5, -2, 7 };
        int[] cn = Array.ConvertAll(neg, Neg);               // {3,0,-5,2,-7}
        Console.WriteLine("neg_elems=" + (cn[0] == 3 && cn[1] == 0 && cn[2] == -5 && cn[3] == 2 && cn[4] == -7));
        Console.WriteLine("neg_find_neg=" + (Array.Find(cn, IsNeg) == -5));
        Array.Sort(cn);                                      // {-7,-5,0,2,3}
        Console.WriteLine("neg_sorted=" + (cn[0] == -7 && cn[1] == -5 && cn[2] == 0 && cn[3] == 2 && cn[4] == 3));
        Console.WriteLine("neg_binsearch_zero=" + (Array.BinarySearch(cn, 0) == 2));
        Console.WriteLine("neg_binsearch_miss=" + (Array.BinarySearch(cn, -4) == -3));  // insert between -5(idx1) and 0(idx2) -> insertion 2 -> ~2 == -3
        Console.WriteLine("neg_indexof=" + (Array.IndexOf(cn, 5) == -1));
        Console.WriteLine("neg_src_unchanged=" + (neg[0] == -3 && neg[4] == 7));
    }
}}
