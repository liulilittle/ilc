//------------------------------------------------------------------------------
// arrayops.cs -- System.Array DEEP e2e reference case (net48 oracle).
//
// WIRED surface (MethodMap.Array.cs ?? TryMapArraySort.cs, il2cpp_array_ext.h
// / _ext2.h / arraysort.h):
//   Array.CopyTo(Array, int)     -- il2cpp_array_copy_to (2-arg, instance)
//   Array.Copy 5-arg             -- icall_array_copy (partial + overlapping)
//   Array.IndexOf<T> 2-arg       -- int32/int64/string (icall + direct calls)
//   Array.Reverse 1-arg / 3-arg  -- icall_array_reverse / reverse_range (int32)
//   Array.Sort<T> 1-arg          -- int32/uint32/int64/string ascending
//   Array.Clear / BinarySearch / get_Length / GetLength / get_Rank
//
// STABILITY note: .NET Array.Sort is NOT stable (introsort) and the ILC
// runtime uses quicksort, so NO index-marker assertion is made on equal
// elements -- the complete ascending VALUE sequence is the only valid
// assertion (sortcase2 precedent).
//
// RECORDED GAPS (parked here -- each would break the transpile/build):
//   * Array.Clone()          -- no "Clone" case in MethodMap.Array.cs ->
//     generated-name undefined symbol -> link failure.
//   * Array.ConvertAll<T,T>  -- no case (needs a Converter<T,T> delegate
//     ABI + a per-type converter icall) -> link failure.
//   * Array.Resize<T>        -- no case (ref-parameter array resize) ->
//     link failure.
//   * Array.Copy 3-arg / 2-arg / long-index overloads -- only the 5-arg
//     (Array, int, Array, int, int) form is wired (icall_array_copy).
//   * Array.FindIndex / GetLowerBound / multidim -- unwired (batch A D-ARR-*).
//
// Every active line uses ONLY the wired surface; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ArrayOps {
    private static bool Ascending(int[] a)
    {
        for (int i = 1; i < a.Length; i++)
            if (a[i - 1] > a[i]) return false;
        return true;
    }
    private static bool Ascending(long[] a)
    {
        for (int i = 1; i < a.Length; i++)
            if (a[i - 1] > a[i]) return false;
        return true;
    }
    private static bool Ascending(string[] a)
    {
        for (int i = 1; i < a.Length; i++)
        {
            string p = a[i - 1], q = a[i];
            bool pNull = p == null, qNull = q == null;
            if (pNull && qNull) continue;
            if (pNull) continue;
            if (qNull) return false;
            if (string.CompareOrdinal(p, q) > 0) return false;
        }
        return true;
    }

    public static void Main() {
        // ---- CopyTo (instance, 2-arg): full + indexed + string[] ----
        int[] src = new int[] { 10, 20, 30, 40, 50 };
        int[] dst = new int[5];
        src.CopyTo(dst, 0);
        Console.WriteLine(dst[0] == 10 && dst[4] == 50);          // True
        int[] dst2 = new int[7];
        src.CopyTo(dst2, 2);
        Console.WriteLine(dst2[0] == 0 && dst2[2] == 10 && dst2[6] == 50); // True
        string[] ssrc = new string[] { "a", "b", "c" };
        string[] sdst = new string[3];
        ssrc.CopyTo(sdst, 0);
        Console.WriteLine(sdst[0] == "a" && sdst[2] == "c");      // True

        // ---- Copy 5-arg: partial, destination offset, overlap shift, string[] ----
        int[] a = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        int[] b = new int[8];
        Array.Copy(a, 2, b, 1, 3);
        Console.WriteLine(b[0] == 0 && b[1] == 3 && b[3] == 5 && b[4] == 0); // True
        int[] c = new int[] { 1, 2, 3, 4, 5 };
        Array.Copy(c, 0, c, 1, 4);                                // overlapping forward
        Console.WriteLine(c[0] == 1 && c[1] == 1 && c[4] == 4);   // True
        string[] s2 = new string[] { "x", "y", "z", "w" };
        string[] t2 = new string[4];
        Array.Copy(s2, 1, t2, 0, 2);
        Console.WriteLine(t2[0] == "y" && t2[1] == "z" && t2[2] == null); // True

        // ---- IndexOf<T> generic 2-arg: int32 / int64 / string ----
        int[] ia = new int[] { 7, 1, 9, 1, 7 };
        Console.WriteLine(Array.IndexOf(ia, 9));                  // 2
        Console.WriteLine(Array.IndexOf(ia, 1));                  // 1 (first match)
        Console.WriteLine(Array.IndexOf(ia, 99));                 // -1
        long[] la = new long[] { 10L, 20L, 30L };
        Console.WriteLine(Array.IndexOf(la, 20L));                // 1
        Console.WriteLine(Array.IndexOf(la, 5L));                 // -1
        string[] sa = new string[] { "pear", "apple", "pear" };
        Console.WriteLine(Array.IndexOf(sa, "apple"));            // 1
        Console.WriteLine(Array.IndexOf(sa, "cherry"));           // -1

        // ---- Reverse (int32 only wired): full + range ----
        int[] ra = new int[] { 1, 2, 3, 4, 5 };
        Array.Reverse(ra);
        Console.WriteLine(ra[0] == 5 && ra[4] == 1);              // True
        int[] rb = new int[] { 1, 2, 3, 4, 5 };
        Array.Reverse(rb, 1, 3);
        Console.WriteLine(rb[0] == 1 && rb[1] == 4 && rb[3] == 2 && rb[4] == 5); // True

        // ---- Sort (value-sequence assertions; duplicates allowed) ----
        int[] sa1 = new int[] { 3, 1, 4, 1, 5, 9, 2, 6, -1, 0 };
        Array.Sort(sa1);
        Console.WriteLine(Ascending(sa1));                        // True
        Console.WriteLine(sa1[0] == -1 && sa1[9] == 9);           // True (min/max endpoints)
        long[] sl = new long[] { 9L, -2L, 7L, 4L, 4L };
        Array.Sort(sl);
        Console.WriteLine(Ascending(sl));                         // True
        Console.WriteLine(sl[0] == -2L && sl[4] == 9L);           // True
        string[] ss = new string[] { "pear", null, "apple", "cherry", "cherry" };
        Array.Sort(ss);                                           // ordinal, null first
        Console.WriteLine(Ascending(ss));                         // True
        Console.WriteLine(ss[0] == null && ss[1] == "apple" && ss[4] == "pear"); // True

        // ---- wired extras: Clear / get_Length / GetLength / get_Rank ----
        int[] cl = new int[] { 1, 2, 3, 4, 5 };
        Array.Clear(cl, 1, 3);
        Console.WriteLine(cl[0] == 1 && cl[1] == 0 && cl[3] == 0 && cl[4] == 5); // True
        Console.WriteLine(ia.Length);                             // 5
        Console.WriteLine(ia.GetLength(0));                       // 5
        Console.WriteLine(ia.Rank);                               // 1
    }
}}
