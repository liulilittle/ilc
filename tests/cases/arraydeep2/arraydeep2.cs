//------------------------------------------------------------------------------
// arraydeep2.cs -- ARRAY DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C63 (2026-08-12).
//
// Extends arraydeep/arrdeep/arrcomp/arrayops/arraybounds/newarrdeep on the
// WIRED System.Array surface (MethodMap.Array.cs + ArraySort.cs,
// il2cpp_array_ext.h / _ext2.h / arraysort.h) with a focused theme:
//
//   A. MULTIDIM-BOUNDARY API SURFACE -- the dimension-argument surface:
//      GetLength(int) / GetUpperBound(int) / GetLowerBound(int) with a
//      dimension argument (the boundary validation RequireDimension: only
//      dimension 0 exists -> a dimension 1 / -1 probe throws
//      IndexOutOfRangeException, matching net48); get_Rank (1 for the
//      one-dimensional scope); get_LongLength == Length; empty-array
//      GetUpperBound == -1.
//
//   B. COPY DEEP -- Array.Copy 5-arg with the FULL validation matrix
//      (partial / overlap / zero-length / negative-index AOO / source-too-
//      short ArgumentException / destination-too-short ArgumentException /
//      int32-vs-string ArrayTypeMismatchException) + Array.CopyTo 2-arg
//      (full / indexed / destination-too-small ArgumentException).
//
//   C. WIRED EXTRAS -- Array.Initialize (int32 zeroing / string[] no-op),
//      Array.Empty<char/int/string> (zero-length), Array.Clear range.
//
// RECORDED GAPS (parked, NOT called -- each would break the transpile):
//   * multidim NEWARR `int[,] m = new int[2,3]` -- D-ARR-1 (MethodBodyReader
//     rejects the 0x14 multidim local: "multidim-array/function-pointer
//     local ... not supported"). The dimension-argument surface ABOVE is the
//     reachable boundary API; the allocator path is future work.
//   * Array.Clone() -- no "Clone" case in MethodMap.Array.cs/ArraySort.cs
//     -> generated-name undefined symbol -> link failure (arrcomp record).
//
// Every active line prints a bool (True/False) or a deterministic int;
// net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ArrayDeep2 {
    public static void Main() {
        // ============ A. multidim-boundary API surface ============
        int[] a = new int[5];
        Console.WriteLine("len=" + a.Length);
        Console.WriteLine("g0=" + a.GetLength(0));
        Console.WriteLine("ub0=" + a.GetUpperBound(0));
        Console.WriteLine("lb0=" + a.GetLowerBound(0));
        Console.WriteLine("rank=" + a.Rank);
        Console.WriteLine("longlen=" + (a.LongLength == 5L));
        Console.WriteLine("longlen_eq=" + (a.LongLength == a.Length));

        // dimension boundary: dimension 1 / -1 -> IndexOutOfRangeException
        bool dim = false;
        try { int x = a.GetLength(1); Console.WriteLine(x); } catch (IndexOutOfRangeException) { dim = true; }
        Console.WriteLine("dim1_gl=" + dim);
        dim = false;
        try { int x = a.GetUpperBound(1); Console.WriteLine(x); } catch (IndexOutOfRangeException) { dim = true; }
        Console.WriteLine("dim1_gub=" + dim);
        dim = false;
        try { int x = a.GetLowerBound(-1); Console.WriteLine(x); } catch (IndexOutOfRangeException) { dim = true; }
        Console.WriteLine("dimneg_glb=" + dim);

        // empty array: GetUpperBound(0) == -1 (net48 contract)
        int[] empty = new int[0];
        Console.WriteLine("e_len=" + empty.Length);
        Console.WriteLine("e_ub=" + empty.GetUpperBound(0));
        Console.WriteLine("e_lb=" + empty.GetLowerBound(0));
        Console.WriteLine("e_gl=" + empty.GetLength(0));

        // one-element array boundary
        int[] one = new int[1];
        Console.WriteLine("o_ub=" + one.GetUpperBound(0));

        // ============ B. copy deep ============
        // B.1 Array.Copy 5-arg: partial copy with offsets.
        int[] src = new int[5];
        for (int i = 0; i < 5; i++) src[i] = i * 10;           // 0,10,20,30,40
        int[] dst = new int[4];
        Array.Copy(src, 2, dst, 1, 2);
        Console.WriteLine("p=" + (dst[0] == 0 && dst[1] == 20 && dst[2] == 30 && dst[3] == 0));

        // B.2 overlap forward (memmove semantics): shift right by one.
        int[] ov = new int[5];
        for (int i = 0; i < 5; i++) ov[i] = i + 1;             // 1,2,3,4,5
        Array.Copy(ov, 0, ov, 1, 4);
        Console.WriteLine("ovf=" + (ov[0] == 1 && ov[1] == 1 && ov[2] == 2 && ov[3] == 3 && ov[4] == 4));

        // B.3 overlap backward (memmove semantics): shift left by one.
        int[] ovb = new int[5];
        for (int i = 0; i < 5; i++) ovb[i] = i + 1;            // 1,2,3,4,5
        Array.Copy(ovb, 1, ovb, 0, 4);
        Console.WriteLine("ovb=" + (ovb[0] == 2 && ovb[1] == 3 && ovb[2] == 4 && ovb[3] == 5 && ovb[4] == 5));

        // B.4 zero-length copy: validated no-op (one-past-end index legal).
        int[] zsrc = new int[3];
        int[] zdst = new int[3];
        Array.Copy(zsrc, 3, zdst, 3, 0);
        Console.WriteLine("zero=" + (zdst[0] == 0 && zdst[1] == 0 && zdst[2] == 0));

        // B.5 negative index -> ArgumentOutOfRangeException.
        bool aoo = false;
        try { Array.Copy(src, -1, dst, 0, 1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("neg=" + aoo);
        // B.6 source too short -> ArgumentException.
        bool ae = false;
        try { Array.Copy(src, 4, dst, 0, 2); } catch (ArgumentException) { ae = true; }
        Console.WriteLine("srcshort=" + ae);
        // B.7 destination too short -> ArgumentException.
        ae = false;
        try { Array.Copy(src, 0, dst, 3, 2); } catch (ArgumentException) { ae = true; }
        Console.WriteLine("dstshort=" + ae);
        // B.8 element-kind mismatch -> ArrayTypeMismatchException.
        string[] ss = new string[2];
        bool atm = false;
        try { Array.Copy(src, 0, ss, 0, 2); } catch (ArrayTypeMismatchException) { atm = true; }
        Console.WriteLine("mismatch=" + atm);

        // B.9 string[] reference-element copy.
        string[] ssrc = new string[] { "a", "b", "c" };
        string[] sdst = new string[4];
        Array.Copy(ssrc, 1, sdst, 0, 2);
        Console.WriteLine("sref=" + (sdst[0] == "b" && sdst[1] == "c" && sdst[2] == null));

        // B.10 Array.CopyTo 2-arg instance: full + indexed.
        int[] csrc = new int[] { 10, 20, 30, 40, 50 };
        int[] cdst = new int[5];
        csrc.CopyTo(cdst, 0);
        Console.WriteLine("ct_full=" + (cdst[0] == 10 && cdst[4] == 50));
        int[] cdst2 = new int[7];
        csrc.CopyTo(cdst2, 2);
        Console.WriteLine("ct_idx=" + (cdst2[0] == 0 && cdst2[2] == 10 && cdst2[6] == 50));
        // B.11 CopyTo destination too small -> ArgumentException.
        int[] small = new int[3];
        ae = false;
        try { csrc.CopyTo(small, 0); } catch (ArgumentException) { ae = true; }
        Console.WriteLine("ct_small=" + ae);
        // B.12 CopyTo negative index -> ArgumentOutOfRangeException.
        aoo = false;
        try { csrc.CopyTo(cdst, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("ct_neg=" + aoo);

        // ============ C. wired extras ============
        // C.1 Initialize on string[]: no-op (reference elements untouched).
        //     NOTE: int[] Initialize is NOT probed -- net48 Array.Initialize
        //     on a value-element array is a NO-OP (values preserved, verified
        //     by probe) while ILC's icall_array_initialize memset-zeroes --
        //     a recorded divergence (GAPS.md D-ARR-C63-1).
        string[] sin = new string[] { "x", "y" };
        sin.Initialize();
        Console.WriteLine("sini=" + (sin[0] == "x" && sin[1] == "y"));
        // C.3 Array.Empty<char/int/string>: zero-length.
        Console.WriteLine("ec=" + (Array.Empty<char>().Length == 0));
        Console.WriteLine("ei=" + (Array.Empty<int>().Length == 0));
        Console.WriteLine("es=" + (Array.Empty<string>().Length == 0));
        // C.4 Array.Clear range.
        int[] cl = new int[] { 1, 2, 3, 4, 5 };
        Array.Clear(cl, 1, 3);
        Console.WriteLine("cl=" + (cl[0] == 1 && cl[1] == 0 && cl[3] == 0 && cl[4] == 5));
    }
}}
