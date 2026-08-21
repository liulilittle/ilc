//------------------------------------------------------------------------------
// arrayresize2.cs -- Array.Resize<int> DEEP probe 2 (e2e batch C54): resize
// round-trips / boundary lattice -- byte-exact vs the net48 oracle,
// dual-compiler (mingw + msvc).
//
// Goes DEEPER than arrresize (C12: grow/shrink/equal/null/zero/cycles/large/
// exception) and arrfuncs section E: this case hammers the ROUND-TRIP and
// BOUNDARY behavior of the wired icall -- icall_array_resize_i4
// (il2cpp_array_ext2.h:194-202, reached through MethodMap.Array.cs "Resize",
// int32 generic arg only 0x08).
//
//   * A GROW→SHRINK→GROW round trip: the leading prefix survives, the new
//     tail is always zeroed (never garbage).
//   * B/C shrink to a single element and mutate between resizes: the first
//     N elements are copied verbatim, the tail is zero.
//   * D boundary sizes 0 and 1 (resize a 1-array to 0, a 0-array to 1).
//   * E REFERENCE semantics: only the ref'd variable is replaced; an alias
//     keeps the OLD array untouched.
//   * F large grow (100 -> 200): head preserved, new tail zeroed.
//   * G repeated resizes in a loop (grow 2->7 one element at a time, then
//     shrink back): cumulative length and data integrity.
//   * H equal-length resize keeps the SAME reference (no-op).
//   * I null chain: null -> array -> resize.
//   * J exception contract: negative newSize throws ArgumentOutOfRangeException
//     and the caller's array is left untouched.
//   * K bulk integrity: a sum across grow/shrink cycles matches the expected
//     arithmetic value.
//
// Semantics pinned by il2cpp_array_ext2.h and the net48 reference: a NEW
// int32[] of exactly newSize; first min(old,new) elements copied; extra
// elements zero; newSize < 0 -> ArgumentOutOfRangeException; null array is
// legal (becomes new int32[newSize]); equal length is a no-op (same ref).
//
// All int32 (the only generic argument the resize icall exposes). Every line
// is a labeled bool. Deterministic, all-ASCII.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class ArrayResize2 {
    public static void Main() {
        // ============ A. grow -> shrink -> grow round trip ============
        int[] a = { 1, 2, 3 };
        Array.Resize(ref a, 5);     // [1,2,3,0,0]
        Array.Resize(ref a, 4);     // [1,2,3,0]
        Array.Resize(ref a, 6);     // [1,2,3,0,0,0]
        Console.WriteLine("A_len=" + (a.Length == 6));
        Console.WriteLine("A_pre=" + (a[0] == 1 && a[1] == 2 && a[2] == 3));
        Console.WriteLine("A_zero=" + (a[3] == 0 && a[4] == 0 && a[5] == 0));

        // ============ B. shrink to 1, grow back ============
        int[] b = { 9, 8, 7, 6 };
        Array.Resize(ref b, 1);     // [9]
        Array.Resize(ref b, 3);     // [9,0,0]
        Console.WriteLine("B_len=" + (b.Length == 3));
        Console.WriteLine("B_head=" + (b[0] == 9));
        Console.WriteLine("B_zero=" + (b[1] == 0 && b[2] == 0));

        // ============ C. mutate between resizes ============
        int[] c = { 1, 2, 3 };
        c[1] = 20;
        Array.Resize(ref c, 5);     // [1,20,3,0,0]
        c[3] = 40;
        c[4] = 50;                  // [1,20,3,40,50]
        Array.Resize(ref c, 2);     // [1,20]
        Console.WriteLine("C_len=" + (c.Length == 2));
        Console.WriteLine("C_keep=" + (c[0] == 1 && c[1] == 20));

        // ============ D. boundary sizes 0 and 1 ============
        int[] d = { 42 };
        Array.Resize(ref d, 0);     // []
        Console.WriteLine("D_len0=" + (d.Length == 0));
        Array.Resize(ref d, 1);     // [0]
        Console.WriteLine("D_len1=" + (d.Length == 1));
        Console.WriteLine("D_zero=" + (d[0] == 0));

        // ============ E. alias sees the OLD array ============
        int[] e = { 5, 6, 7 };
        int[] ealias = e;
        Array.Resize(ref e, 2);     // e -> new [5,6]; ealias still [5,6,7]
        Console.WriteLine("E_len=" + (e.Length == 2));
        Console.WriteLine("E_alias=" + (ealias.Length == 3 && ealias[2] == 7));
        Console.WriteLine("E_keep=" + (e[0] == 5 && e[1] == 6));

        // ============ F. large grow ============
        int[] f = new int[100];
        f[0] = 7;
        f[99] = 8;
        Array.Resize(ref f, 200);
        Console.WriteLine("F_len=" + (f.Length == 200));
        Console.WriteLine("F_head=" + (f[0] == 7 && f[99] == 8));
        Console.WriteLine("F_tail=" + (f[100] == 0 && f[199] == 0));

        // ============ G. repeated resizes in a loop ============
        int[] g = { 1, 2 };
        for (int i = 0; i < 5; i++) { Array.Resize(ref g, g.Length + 1); }
        Console.WriteLine("G_len=" + (g.Length == 7));
        Console.WriteLine("G_head=" + (g[0] == 1 && g[1] == 2));
        Array.Resize(ref g, 3);     // [1,2,0]
        Console.WriteLine("G_shrink=" + (g.Length == 3 && g[0] == 1 && g[1] == 2 && g[2] == 0));

        // ============ H. equal length keeps the same reference ============
        int[] h = { 4, 4 };
        int[] href = h;
        Array.Resize(ref h, 2);
        Console.WriteLine("H_same=" + (h == href));
        Console.WriteLine("H_val=" + (h[0] == 4 && h[1] == 4));

        // ============ I. null chain ============
        int[] n = null;
        Array.Resize(ref n, 3);     // [0,0,0]
        Console.WriteLine("I_notnull=" + (n != null));
        n[0] = 1;
        n[2] = 3;                   // [1,0,3]
        Array.Resize(ref n, 1);     // [1]
        Console.WriteLine("I_keep=" + (n.Length == 1 && n[0] == 1));

        // ============ J. negative size throws, original untouched ============
        bool aoo = false;
        int[] j = { 1 };
        try { Array.Resize(ref j, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("J_aoo=" + aoo);
        Console.WriteLine("J_untouched=" + (j.Length == 1 && j[0] == 1));

        // ============ K. bulk integrity across round trips ============
        int[] k = new int[0];
        for (int i = 0; i < 10; i++) { Array.Resize(ref k, k.Length + 1); k[k.Length - 1] = i; }
        int sum = 0;
        for (int i = 0; i < k.Length; i++) { sum += k[i]; }
        Console.WriteLine("K_grow=" + (k.Length == 10 && sum == 45));
        Array.Resize(ref k, 5);
        int sum2 = 0;
        for (int i = 0; i < k.Length; i++) { sum2 += k[i]; }
        Console.WriteLine("K_shrink=" + (k.Length == 5 && sum2 == 10));
    }
}}
