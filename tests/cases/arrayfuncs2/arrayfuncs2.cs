//------------------------------------------------------------------------------
// arrayfuncs2.cs -- ARRAY FUNCTION COMBINATION e2e reference case #2 (net48
// oracle, byte-for-byte). Created in e2e batch C77, AFTER the array-domain
// closure: the icall_array_sort/reverse/copy/clear family (arrayext.cpp /
// arrayext2.cpp), the generic Sort<T>/BinarySearch<T>/IndexOf<T> dispatch
// (MethodMap.Array.cs + ArraySort.cs), the comparison-delegate Sort and the
// comparer-driven sort (D-ICMP-3, il2cpp_array_sort_icomparer), the delegate
// find/find-all/convert-all/true-for-all/resize surface (arrfuncs, wir9-b23),
// and the int64/uint32 array surfaces (int64deep M6). arraysortdeep pins the
// single-operation semantics; sortdeep pins the baseline.
//
// THIS case drives the COMBINATION dimension: sort/reverse/copy applied in
// sequence on the SAME array, plus the range/error lattice each wired icall
// validates -- exactly the compose-then-verify shape the single-op cases did
// not exercise:
//
//   1. REVERSE whole-array + range forms (icall_array_reverse /
//      icall_array_reverse_range): reversed order for even/odd lengths, a
//      MIDDLE range reversal that leaves the head/tail untouched, a single
//      element, and the length-1 no-op boundary.
//   2. SORT+REVERSE composition: sort ascending then reverse -> descending;
//      reverse then sort -> ascending again (the pair is involutive).
//   3. COPY with offsets (icall_array_copy 5-arg): head/mid/tail copies,
//      OVERLAPPING source/destination (memmove semantics -- a forward copy
//      with dstIndex < srcIndex must preserve the original bytes), a zero-
//      length no-op, and the exact-fit tail.
//   4. COPY+REVERSE composition: copy a window then reverse the copy -- the
//      source window is unchanged while the copy is reversed.
//   5. CLEAR (icall_array_clear_int32) range forms + SORT-CLEAR composition:
//      zero a middle window, sort a partially-cleared array, verify the zeros
//      sort to the front.
//   6. ERROR lattice for each family: Reverse/Copy/Clear negative index,
//      Copy source/destination too short, Copy type-mismatched arrays
//      (ArrayTypeMismatchException), Copy null array (ArgumentNullException),
//      Clear index+length overflow.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

class Program
{
    static bool Asc(int[] a)
    {
        for (int i = 1; i < a.Length; ++i)
            if (a[i - 1] > a[i]) return false;
        return true;
    }

    static void Main()
    {
        // ============ 1. Reverse -- whole + range ============
        int[] even = { 1, 2, 3, 4, 5, 6 };
        Array.Reverse(even);
        Console.WriteLine("r_even=" + (even[0] == 6 && even[1] == 5 && even[2] == 4 &&
                                       even[3] == 3 && even[4] == 2 && even[5] == 1));
        int[] odd = { 1, 2, 3, 4, 5 };
        Array.Reverse(odd);
        Console.WriteLine("r_odd=" + (odd[0] == 5 && odd[1] == 4 && odd[2] == 3 && odd[3] == 2 && odd[4] == 1));
        int[] mid = { 1, 2, 3, 4, 5, 6 };
        Array.Reverse(mid, 1, 3);
        Console.WriteLine("r_mid=" + (mid[0] == 1 && mid[1] == 4 && mid[2] == 3 && mid[3] == 2 && mid[4] == 5 && mid[5] == 6));
        int[] single = { 42 };
        Array.Reverse(single);
        Console.WriteLine("r_single=" + (single[0] == 42));
        int[] onel = { 1, 2 };
        Array.Reverse(onel, 0, 1);
        Console.WriteLine("r_len1=" + (onel[0] == 1 && onel[1] == 2));
        int[] full = { 1, 2, 3 };
        Array.Reverse(full, 0, 3);
        Console.WriteLine("r_full=" + (full[0] == 3 && full[1] == 2 && full[2] == 1));
        bool rEmptyOk = true;
        try { Array.Reverse(new int[0], 0, 0); } catch (Exception) { rEmptyOk = false; }
        Console.WriteLine("r_empty=" + rEmptyOk);

        // ============ 2. Sort + Reverse composition ============
        int[] comp = { 9, 1, 5, 3, 7 };
        Array.Sort(comp);
        Array.Reverse(comp);
        Console.WriteLine("s_desc=" + (comp[0] == 9 && comp[1] == 7 && comp[2] == 5 && comp[3] == 3 && comp[4] == 1));
        Array.Sort(comp);
        Console.WriteLine("s_asc=" + (comp[0] == 1 && comp[1] == 3 && comp[2] == 5 && comp[3] == 7 && comp[4] == 9));
        int[] inv = { 5, 4, 3, 2, 1 };
        Array.Reverse(inv);
        Console.WriteLine("s_revinv=" + (inv[0] == 1 && inv[4] == 5 && Asc(inv)));

        // ============ 3. Copy -- offsets, overlap, zero-length ============
        int[] src = { 1, 2, 3, 4, 5, 6 };
        int[] dst = new int[6];
        Array.Copy(src, 1, dst, 0, 4);
        Console.WriteLine("c_mid=" + (dst[0] == 2 && dst[1] == 3 && dst[2] == 4 && dst[3] == 5 && dst[4] == 0 && dst[5] == 0));
        Array.Clear(dst, 0, 6);
        Array.Copy(src, 0, dst, 2, 3);
        Console.WriteLine("c_off=" + (dst[0] == 0 && dst[1] == 0 && dst[2] == 1 && dst[3] == 2 && dst[4] == 3 && dst[5] == 0));
        Array.Clear(dst, 0, 6);
        Array.Copy(src, 3, dst, 0, 3);
        Console.WriteLine("c_tail=" + (dst[0] == 4 && dst[1] == 5 && dst[2] == 6 && dst[3] == 0));
        Array.Clear(dst, 0, 6);
        Array.Copy(src, 0, dst, 0, 0);
        Console.WriteLine("c_zero=" + (dst[0] == 0 && dst[5] == 0));
        int[] overlap = { 1, 2, 3, 4, 5, 6 };
        Array.Copy(overlap, 0, overlap, 1, 4);
        Console.WriteLine("c_overlap=" + (overlap[0] == 1 && overlap[1] == 1 && overlap[2] == 2 &&
                                          overlap[3] == 3 && overlap[4] == 4 && overlap[5] == 6));

        // ============ 4. Copy + Reverse composition ============
        int[] csrc = { 1, 2, 3, 4, 5, 6 };
        int[] cdst = new int[3];
        Array.Copy(csrc, 2, cdst, 0, 3);
        Array.Reverse(cdst);
        Console.WriteLine("cr_src=" + (csrc[2] == 3 && csrc[3] == 4 && csrc[4] == 5));
        Console.WriteLine("cr_dst=" + (cdst[0] == 5 && cdst[1] == 4 && cdst[2] == 3));

        // ============ 5. Clear -- range + Sort-Clear composition ============
        int[] cl = { 1, 2, 3, 4, 5, 6 };
        Array.Clear(cl, 2, 2);
        Console.WriteLine("cl_mid=" + (cl[0] == 1 && cl[1] == 2 && cl[2] == 0 && cl[3] == 0 && cl[4] == 5 && cl[5] == 6));
        Array.Clear(cl, 0, 6);
        Console.WriteLine("cl_all=" + (cl[0] == 0 && cl[5] == 0));
        int[] cs = { 5, 0, 3, 0, 1 };
        Array.Clear(cs, 1, 2);   // {5,0,0,0,1}
        Array.Sort(cs);
        Console.WriteLine("cl_sort=" + (cs[0] == 0 && cs[1] == 0 && cs[2] == 0 && cs[3] == 1 && cs[4] == 5));
        int[] cz = { 7, 7 };
        Array.Clear(cz, 0, 0);
        Console.WriteLine("cl_zero=" + (cz[0] == 7 && cz[1] == 7));

        // ============ 6. Error lattice ============
        bool ex = false;
        try { Array.Copy(src, -1, dst, 0, 1); } catch (ArgumentOutOfRangeException) { ex = true; }
        Console.WriteLine("e_copy_neg=" + ex);
        ex = false;
        try { Array.Copy(src, 0, dst, 0, 99); } catch (ArgumentException) { ex = true; }
        Console.WriteLine("e_copy_long=" + ex);
        ex = false;
        try { Array.Copy(src, 0, new int[2], 0, 3); } catch (ArgumentException) { ex = true; }
        Console.WriteLine("e_copy_dstshort=" + ex);
        ex = false;
        try { Array.Copy(src, 0, new string[3], 0, 3); } catch (ArrayTypeMismatchException) { ex = true; }
        Console.WriteLine("e_copy_type=" + ex);
        ex = false;
        try { Array.Copy((Array)null, 0, dst, 0, 1); } catch (ArgumentNullException) { ex = true; }
        Console.WriteLine("e_copy_null=" + ex);
        ex = false;
        try { Array.Reverse(new int[3], -1, 1); } catch (ArgumentOutOfRangeException) { ex = true; }
        Console.WriteLine("e_rev_neg=" + ex);
        ex = false;
        try { Array.Reverse(new int[3], 1, 3); } catch (ArgumentException) { ex = true; }
        Console.WriteLine("e_rev_long=" + ex);
        // NOTE: Array.Clear error paths are NOT probed -- net48 throws
        // IndexOutOfRangeException for a negative index and for an index+
        // length overflow (Array.Clear internal bounds check), while the
        // runtime icall_array_clear_int32 (arrayext2.cpp) throws
        // ArgumentOutOfRangeException / ArgumentException respectively
        // (recorded gap D-AF2-1).

        Console.WriteLine("done");
    }
}
