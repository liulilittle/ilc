//------------------------------------------------------------------------------
// arrayslice.cs -- System.Array COPY-SLICE deep probe (e2e batch C72): the
// full 5-arg Array.Copy window/edge matrix over multiple element kinds, plus
// the CopyTo instance surface. Byte-exact vs the net48 oracle (expected.txt).
//
// Builds on arraydeep2/arrayops/arrcomp (which probe the basic Copy shapes)
// with a SLICE-CENTRIC deep matrix: extraction of arbitrary windows, window
// placement inside a larger destination, one-element and one-past-end
// boundaries, zero-length validated no-ops, self/overlap copies in both
// directions, and per-element-kind value/ref slices. net48 is the byte
// reference (icall_array_copy, arrayext.cpp:185).
//
// DELIBERATELY NOT PROBED (documented runtime/net48 divergences -- see
// GAPS.md; a probe would fail the byte diff):
//   * CROSS-KIND WIDENING Copy: net48 Array.Copy performs element conversion
//     for compatible-but-different element sizes (int[] -> long[],
//     byte[] -> int[] succeed by widening; probe-verified net48), while the
//     runtime icall_array_copy requires equal elemSize AND isRefElem and
//     throws ArrayTypeMismatchException. Only SAME-TYPE copies and the
//     int->string kind mismatch (rejected by both) are probed here.
//   * 3-arg Array.Copy(src, dst, length) / 2-arg long-index overloads -- not
//     wired (MethodMap.Array.cs: Copy only accepts the 5-arg form).
//
// EVERY line prints a labeled bool; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

class Program
{
    static void Main()
    {
        // ============ 1. int32 window slices + destination placement ============
        int[] src = new int[6];
        for (int i = 0; i < 6; i++) src[i] = i * 10;            // 0,10,20,30,40,50

        int[] w1 = new int[3];
        Array.Copy(src, 1, w1, 0, 3);                           // 10,20,30
        Console.WriteLine("w_head=" + (w1[0] == 10 && w1[1] == 20 && w1[2] == 30));

        int[] w2 = new int[3];
        Array.Copy(src, 3, w2, 0, 3);                           // 30,40,50 (tail window)
        Console.WriteLine("w_tail=" + (w2[0] == 30 && w2[1] == 40 && w2[2] == 50));

        int[] w3 = new int[2];
        Array.Copy(src, 2, w3, 0, 2);                           // 20,30 (mid window)
        Console.WriteLine("w_mid=" + (w3[0] == 20 && w3[1] == 30));

        int[] w4 = new int[5];
        Array.Copy(src, 1, w4, 2, 3);                           // dest placement [_,_,10,20,30,_,_]
        Console.WriteLine("w_place=" + (w4[0] == 0 && w4[1] == 0 && w4[2] == 10 && w4[4] == 30));

        int[] w5 = new int[6];
        Array.Copy(src, 0, w5, 0, 6);                           // full copy
        Console.WriteLine("w_full=" + (w5[5] == 50 && w5[0] == 0));

        // ============ 2. boundary shapes ============
        int[] b1 = new int[1];
        Array.Copy(src, 5, b1, 0, 1);                           // last element
        Console.WriteLine("b_last=" + (b1[0] == 50));

        int[] b2 = new int[1];
        Array.Copy(src, 0, b2, 0, 1);                           // first element
        Console.WriteLine("b_first=" + (b2[0] == 0));

        int[] zsrc = new int[2];
        int[] zdst = new int[2];
        Array.Copy(zsrc, 2, zdst, 2, 0);                        // zero-length at one-past-end
        Console.WriteLine("b_zero1=" + (zdst[0] == 0 && zdst[1] == 0));
        Array.Copy(zsrc, 0, zdst, 0, 0);                        // zero-length at head
        Console.WriteLine("b_zero2=" + true);

        int[] ed = new int[4];
        Array.Copy(src, 2, ed, 3, 1);                           // dstIndex+len == dst.Length
        Console.WriteLine("b_dstfill=" + (ed[0] == 0 && ed[1] == 0 && ed[2] == 0 && ed[3] == 20));

        // ============ 3. self / overlap copies (memmove both directions) ============
        int[] self = new int[] { 1, 2, 3, 4, 5 };
        Array.Copy(self, 0, self, 1, 4);                        // shift right
        Console.WriteLine("o_right=" + (self[0] == 1 && self[1] == 1 && self[2] == 2 && self[4] == 4));

        int[] back = new int[] { 1, 2, 3, 4, 5 };
        Array.Copy(back, 1, back, 0, 4);                        // shift left
        Console.WriteLine("o_left=" + (back[0] == 2 && back[1] == 3 && back[3] == 5 && back[4] == 5));

        int[] selfsame = new int[] { 7, 8, 9 };
        Array.Copy(selfsame, 0, selfsame, 0, 3);                // no-op overlap
        Console.WriteLine("o_same=" + (selfsame[0] == 7 && selfsame[2] == 9));

        // ============ 4. per-element-kind value slices ============
        byte[] bsrc = new byte[] { 1, 2, 3, 4, 5 };
        byte[] bdst = new byte[3];
        Array.Copy(bsrc, 1, bdst, 0, 3);
        Console.WriteLine("k_byte=" + (bdst[0] == 2 && bdst[1] == 3 && bdst[2] == 4));

        long[] lsrc = new long[] { 10L, 20L, 30L, 40L };
        long[] ldst = new long[4];
        Array.Copy(lsrc, 1, ldst, 2, 2);
        Console.WriteLine("k_long=" + (ldst[0] == 0L && ldst[1] == 0L && ldst[2] == 20L && ldst[3] == 30L));

        string[] ssrc = new string[] { "a", "b", "c", "d" };
        string[] sdst = new string[4];
        Array.Copy(ssrc, 1, sdst, 1, 2);
        Console.WriteLine("k_ref=" + (sdst[0] == null && sdst[1] == "b" && sdst[2] == "c" && sdst[3] == null));

        // ============ 5. error matrix (same classification as net48) ============
        bool aoo = false;
        try { Array.Copy(src, -1, w1, 0, 1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("e_negsrc=" + aoo);
        aoo = false;
        try { Array.Copy(src, 0, w1, -1, 1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("e_negdst=" + aoo);
        aoo = false;
        try { Array.Copy(src, 0, w1, 0, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("e_neglen=" + aoo);

        bool ae = false;
        try { Array.Copy(src, 4, w1, 0, 3); } catch (ArgumentException) { ae = true; }  // src too short
        Console.WriteLine("e_srcshort=" + ae);
        ae = false;
        try { Array.Copy(src, 0, w1, 1, 3); } catch (ArgumentException) { ae = true; }  // dst too short
        Console.WriteLine("e_dstshort=" + ae);
        ae = false;
        try { Array.Copy(src, 7, w1, 0, 0); } catch (ArgumentException) { ae = true; } // srcIndex > length
        Console.WriteLine("e_srcpast=" + ae);

        bool atm = false;
        int[] isrc = new int[] { 1, 2 };
        try { Array.Copy(ssrc, 0, isrc, 0, 1); } catch (ArrayTypeMismatchException) { atm = true; }
        Console.WriteLine("e_kind=" + atm);

        // ============ 6. CopyTo instance surface (full / indexed / errors) ============
        int[] csrc = new int[] { 5, 6, 7, 8 };
        int[] cdst = new int[6];
        csrc.CopyTo(cdst, 0);
        Console.WriteLine("ct_full=" + (cdst[0] == 5 && cdst[3] == 8 && cdst[4] == 0 && cdst[5] == 0));
        int[] cdst2 = new int[6];
        csrc.CopyTo(cdst2, 2);
        Console.WriteLine("ct_idx=" + (cdst2[0] == 0 && cdst2[2] == 5 && cdst2[5] == 8));
        int[] csmall = new int[3];
        ae = false;
        try { csrc.CopyTo(csmall, 0); } catch (ArgumentException) { ae = true; }
        Console.WriteLine("ct_small=" + ae);
        aoo = false;
        try { csrc.CopyTo(cdst, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
        Console.WriteLine("ct_neg=" + aoo);
        ae = false;
        try { csrc.CopyTo(cdst, 5); } catch (ArgumentException) { ae = true; }
        Console.WriteLine("ct_past=" + ae);
        byte[] bb = new byte[] { 9, 8, 7 };
        byte[] bb2 = new byte[4];
        bb.CopyTo(bb2, 1);
        Console.WriteLine("ct_byte=" + (bb2[0] == 0 && bb2[1] == 9 && bb2[3] == 7));
    }
}
