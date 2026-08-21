//------------------------------------------------------------------------------
// arrresize.cs -- Array.Resize<int> DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C12 (2026-08-09).
//
// Goes DEEPER than arrfuncs' section E (which pinned the basic Resize surface
// after the M6 batch-35 wiring: icall_array_resize_i4 -- il2cpp_array_ext2.h:
// 191, reached through MethodMap.Array.cs "Resize" + the "addr:a" byref-array
// cast). This case focuses EXCLUSIVELY on the resize semantics and the
// net48 REFERENCE-identity contract:
//
//   grow:        a NEW int32[newSize]; leading elements copied, tail padded 0;
//                the OLD reference keeps its old array (galias untouched).
//   shrink:      a NEW int32[newSize] holding the first newSize elements;
//                the old reference keeps the FULL old array (salias.Length==4).
//   equal size:  a NO-OP -- the SAME reference is kept (q == qalias, net48
//                Array.Resize keeps the instance when newSize == Length).
//   null:        a null array becomes new int32[newSize] (never stays null).
//   resize to 0: a non-null zero-length array (from null AND from non-null).
//   grow empty:  0-length -> newSize pads with 0.
//   cycles:      grow -> shrink -> grow: leading data survives, new tail is 0.
//   large:       100-element grow; edges zeroed.
//   exception:   negative newSize -> ArgumentOutOfRangeException.
//
// All int32 (the only generic argument the Resize icall exposes -- genArgs[0]
// == 0x08). All-ASCII output (the harness-wide diff requirement).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class ArrResize {
    public static void Main() {
        // ============ A. grow ============
        int[] g = { 1, 2, 3 };
        int[] galias = g;
        Array.Resize(ref g, 5);
        Console.WriteLine("grow_len=" + (g.Length == 5));
        Console.WriteLine("grow_keep=" + (g[0] == 1 && g[1] == 2 && g[2] == 3));
        Console.WriteLine("grow_pad=" + (g[3] == 0 && g[4] == 0));
        Console.WriteLine("grow_newref=" + (g != galias));
        Console.WriteLine("grow_alias_untouched=" + (galias.Length == 3 && galias[2] == 3));

        // ============ B. shrink ============
        int[] s = { 7, 8, 9, 10 };
        int[] salias = s;
        Array.Resize(ref s, 2);
        Console.WriteLine("shrink_len=" + (s.Length == 2));
        Console.WriteLine("shrink_keep=" + (s[0] == 7 && s[1] == 8));
        Console.WriteLine("shrink_newref=" + (s != salias));
        Console.WriteLine("shrink_alias_len=" + (salias.Length == 4));

        // ============ C. equal size: same reference kept (no-op) ============
        int[] q = { 5, 6 };
        int[] qalias = q;
        Array.Resize(ref q, 2);
        Console.WriteLine("eq_len=" + (q.Length == 2));
        Console.WriteLine("eq_sameref=" + (q == qalias));
        Console.WriteLine("eq_val=" + (q[0] == 5 && q[1] == 6));

        // ============ D. null array -> new array ============
        int[] n = null;
        Array.Resize(ref n, 4);
        Console.WriteLine("null_notnull=" + (n != null));
        Console.WriteLine("null_len=" + (n != null && n.Length == 4));
        Console.WriteLine("null_zero=" + (n != null && n[0] == 0 && n[3] == 0));

        // ============ E. resize to 0 ============
        int[] z = null;
        Array.Resize(ref z, 0);
        Console.WriteLine("zero_fromnull=" + (z != null && z.Length == 0));
        int[] zn = { 1, 2 };
        Array.Resize(ref zn, 0);
        Console.WriteLine("zero_fromarr=" + (zn != null && zn.Length == 0));

        // ============ F. grow from empty ============
        int[] e = new int[0];
        Array.Resize(ref e, 3);
        Console.WriteLine("grow_empty=" + (e.Length == 3 && e[0] == 0 && e[2] == 0));

        // ============ G. repeated cycles ============
        int[] c = { 5, 6, 7 };
        Array.Resize(ref c, 6);
        Array.Resize(ref c, 2);
        Array.Resize(ref c, 4);
        Console.WriteLine("cycle_len=" + (c.Length == 4));
        Console.WriteLine("cycle_keep=" + (c[0] == 5 && c[1] == 6));
        Console.WriteLine("cycle_pad=" + (c[2] == 0 && c[3] == 0));

        // ============ H. large grow ============
        int[] big = new int[3];
        Array.Resize(ref big, 100);
        Console.WriteLine("big_len=" + (big.Length == 100));
        Console.WriteLine("big_edges=" + (big[0] == 0 && big[99] == 0));

        // ============ I. exception contract: negative size ============
        bool aoo = false;
        try { int[] t = { 1 }; Array.Resize(ref t, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("aoo_neg=" + aoo);
    }
}}
