//------------------------------------------------------------------------------
// hglobaldeep.cs -- HGlobal DEEP probe 2 (e2e batch C66 + D-HG-1 2026-08-12):
// the AllocHGlobal / Marshal.Copy(int[]) read-write combination surface --
// allocation sizing, partial (offset) copies in BOTH directions, the
// zero-init contract, alloc-family interop (Uni buffer + int[] Copy +
// PtrToStringUni), the ReAllocHGlobal grow/shrink/zero-pv realloc family
// (S13-S16, D-HG-1 wiring), and the free/round-trip stress loop -- byte-exact
// vs the net48 oracle.
//
// Wired surface (MethodMap.Marshal.cs -> runtime.cpp, all Windows-path):
//   Marshal.AllocHGlobal(int)      -> icall_marshal_alloc_hglobal
//     (GlobalAlloc(GMEM_FIXED|GMEM_ZEROINIT): the buffer is zeroed AND
//     directly usable as a byte pointer -- a net48-faithful family).
//   Marshal.FreeHGlobal(IntPtr)    -> icall_marshal_free_hglobal
//     (GlobalFree on Windows; null/zero is a no-op).
//   Marshal.Copy(int[],int,IntPtr,int)      -> icall_marshal_copy_to_unmanaged
//     (memcpy with the INT32 stride: arr->data + start*4, length*4 bytes).
//   Marshal.Copy(IntPtr,int[],int,int)      -> icall_marshal_copy_from_unmanaged
//     (the reverse direction, same int32 stride).
//   Marshal.PtrToStringUni/Ansi    -> il2cpp_pinvoke_string_from_utf16/utf8.
//   Marshal.StringToHGlobalUni(string) -> il2cpp_pinvoke_string_to_utf16_hglobal.
// The int[] element type is the ONLY stride-correct Copy payload: the icall
// multiplies EVERY element slot by 4 regardless of the managed element type,
// so byte[]/short[]/long[] payloads would copy the wrong byte counts
// (RECORDED gap D-HG-2 below). Every probe below is an int[] copy.
//
// RECORDED WIRING GAPS (defects, NOT probed -- the case stays byte-exact):
//   * D-HG-1: Marshal.ReAllocHGlobal(IntPtr, IntPtr) has NO MethodMap entry
//     (MethodMap.Marshal.cs has no "ReAllocHGlobal" case) and NO runtime
//     icall -- a use falls to the generic generated-name path (undefined
//     extern at cmake build, NOT a LOUD reject). net48's growth/expand path
//     is therefore unreachable; this case covers Alloc/Copy combos only.
//   * D-HG-2: Marshal.Copy with a non-int[] payload (byte[]/short[]/long[]/
//     double[]/...) copies using the int32 stride on the runtime (arr->data
//     + start*4, memcpy len*4) while net48 copies the element-sized bytes --
//     a recorded element-width divergence, so only int[] is probed.
//   * D-HG-3 (oracle-verified 2026-08-12): AllocHGlobal's ZERO-INIT contract
//     DIVERGES -- the runtime zeroes every allocation (GlobalAlloc GMEM_ZEROINIT)
//     while net48 AllocHGlobal is LocalAlloc LMEM_FIXED (NOT zeroed; untouched
//     slots hold garbage). The probe "write 2 of 8 slots then read all 8"
//     printed False on the net48 oracle (garbage slots), so it was dropped --
//     every probe reads back ONLY the slots it wrote.
//   * StringToHGlobalAnsi stays LOUD-rejected (il2cpp_marshal_ext2.h missing
//     from the generated include chain -- recorded in hglobalops).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class Hglobaldeep {

    public static void Main()
    {
        // ============ S1: alloc -> int[] write -> read back -> free ============
        IntPtr p1 = Marshal.AllocHGlobal(4 * 4);
        int[] src1 = { 10, 20, 30, 40 };
        Marshal.Copy(src1, 0, p1, 4);
        int[] dst1 = new int[4];
        Marshal.Copy(p1, dst1, 0, 4);
        Marshal.FreeHGlobal(p1);
        Console.WriteLine("c_full=" + (dst1[0] == 10 && dst1[1] == 20 && dst1[2] == 30 && dst1[3] == 40));

        // ============ S2: partial copies with offsets (both directions) ============
        IntPtr p2 = Marshal.AllocHGlobal(6 * 4);
        int[] src2 = { 1, 2, 3, 4, 5, 6 };
        Marshal.Copy(src2, 2, p2, 3);            // elements {3,4,5} -> native[0..3)
        int[] mid2 = new int[6];
        Marshal.Copy(p2, mid2, 1, 3);            // native -> dst[1..4) = {3,4,5}
        Marshal.FreeHGlobal(p2);
        Console.WriteLine("c_off=" + (mid2[0] == 0 && mid2[1] == 3 && mid2[2] == 4 && mid2[3] == 5 && mid2[4] == 0 && mid2[5] == 0));

        // ============ S4: multiple buffers interleaved (independent regions) ============
        IntPtr q1 = Marshal.AllocHGlobal(2 * 4);
        IntPtr q2 = Marshal.AllocHGlobal(2 * 4);
        int[] sa = { 111, 222 };
        int[] sb = { 333, 444 };
        Marshal.Copy(sa, 0, q1, 2);
        Marshal.Copy(sb, 0, q2, 2);
        int[] da = new int[2];
        int[] db = new int[2];
        Marshal.Copy(q1, da, 0, 2);
        Marshal.Copy(q2, db, 0, 2);
        Marshal.FreeHGlobal(q1);
        Marshal.FreeHGlobal(q2);
        Console.WriteLine("c_indep=" + (da[0] == 111 && da[1] == 222 && db[0] == 333 && db[1] == 444));

        // ============ S5: overwrite a region (read-modify-write on native) ============
        IntPtr p5 = Marshal.AllocHGlobal(4 * 4);
        int[] w1 = { 1, 2, 3, 4 };
        int[] w2 = { 99, 100 };
        Marshal.Copy(w1, 0, p5, 4);
        Marshal.Copy(w2, 0, p5, 2);              // overwrite elements 0..1
        int[] dst5 = new int[4];
        Marshal.Copy(p5, dst5, 0, 4);
        Marshal.FreeHGlobal(p5);
        Console.WriteLine("c_rw=" + (dst5[0] == 99 && dst5[1] == 100 && dst5[2] == 3 && dst5[3] == 4));

        // ============ S6: AllocHGlobal(0) + FreeHGlobal(Zero) no-op ============
        IntPtr p6 = Marshal.AllocHGlobal(0);
        Console.WriteLine("a_zero=" + (p6 != IntPtr.Zero));
        Marshal.FreeHGlobal(p6);
        Marshal.FreeHGlobal(IntPtr.Zero);        // no-op

        // ============ S7: UTF-16 write via int[] Copy -> PtrToStringUni ============
        // 0x00420041 little-endian bytes = 'A','B',0,0 -> PtrToStringUni "AB";
        // the second int32 zeroes the following 4 bytes (terminator).
        IntPtr p7 = Marshal.AllocHGlobal(8);
        int[] unichars = { 0x00420041, 0 };
        Marshal.Copy(unichars, 0, p7, 2);
        string u = Marshal.PtrToStringUni(p7);
        Marshal.FreeHGlobal(p7);
        Console.WriteLine("u16=" + (u == "AB"));

        // ============ S8: StringToHGlobalUni buffer + int[] Copy interop ============
        // Allocate a Uni string, then overwrite its head with UTF-16 via int[]
        // Copy and read back with PtrToStringUni (same global-heap family).
        IntPtr p8 = Marshal.StringToHGlobalUni("xxxx");
        int[] utf16 = { 0x00430044, 0 };         // 'D','C' + 4 NUL bytes -> "DC"
        Marshal.Copy(utf16, 0, p8, 2);
        string u8 = Marshal.PtrToStringUni(p8);
        Marshal.FreeHGlobal(p8);
        Console.WriteLine("u16_interop=" + (u8 == "DC"));

        // ============ S9: ASCII via int[] Copy -> PtrToStringAnsi ============
        IntPtr p9 = Marshal.AllocHGlobal(12);
        int[] ascii = { 0x41414141, 0x42424242, 0 };   // "AAAA" "BBBB" + NUL
        Marshal.Copy(ascii, 0, p9, 3);
        string a = Marshal.PtrToStringAnsi(p9);
        Marshal.FreeHGlobal(p9);
        Console.WriteLine("ansi2=" + (a == "AAAABBBB"));

        // ============ S10: size boundary (exact-fit alloc) ============
        IntPtr p10 = Marshal.AllocHGlobal(3 * 4);
        int[] src10 = { 5, 6, 7 };
        Marshal.Copy(src10, 0, p10, 3);
        int[] dst10 = new int[3];
        Marshal.Copy(p10, dst10, 0, 3);
        Marshal.FreeHGlobal(p10);
        Console.WriteLine("c_exact=" + (dst10[0] == 5 && dst10[1] == 6 && dst10[2] == 7));

        // ============ S11: stress -- 30 alloc/write/read/free cycles ============
        bool ok = true;
        for (int i = 0; i < 30; i++)
        {
            IntPtr hp = Marshal.AllocHGlobal(2 * 4);
            int[] wr = { i, i * 2 };
            Marshal.Copy(wr, 0, hp, 2);
            int[] rd = new int[2];
            Marshal.Copy(hp, rd, 0, 2);
            if (rd[0] != i || rd[1] != i * 2) ok = false;
            Marshal.FreeHGlobal(hp);
        }
        Console.WriteLine("stress=" + ok);

        // ============ S12: re-read after free boundary control (fresh alloc) ============
        IntPtr p12 = Marshal.AllocHGlobal(4);
        int[] one = { 42 };
        Marshal.Copy(one, 0, p12, 1);
        Marshal.FreeHGlobal(p12);
        IntPtr p12b = Marshal.AllocHGlobal(4);
        int[] two = { 77 };
        Marshal.Copy(two, 0, p12b, 1);
        int[] dst12 = new int[1];
        Marshal.Copy(p12b, dst12, 0, 1);
        Marshal.FreeHGlobal(p12b);
        Console.WriteLine("c_seqalloc=" + (dst12[0] == 77));

        // ============ S13: ReAllocHGlobal GROW preserves min(old,new) ============
        // D-HG-1 (wired 2026-08-12): realloc to a LARGER size -- the old
        // content (all of it, since old < new) survives, the returned pointer
        // is the new block (the old pointer is INVALID -- net48 realloc
        // semantics), and the grown tail is writable. The tail is written
        // explicitly (D-HG-3: net48 does NOT zero the grown region), so only
        // written slots are asserted.
        IntPtr p13 = Marshal.AllocHGlobal(2 * 4);
        int[] src13 = { 111, 222 };
        Marshal.Copy(src13, 0, p13, 2);
        IntPtr p13r = Marshal.ReAllocHGlobal(p13, new IntPtr(4 * 4));
        int[] grow13 = { 333, 444 };
        Marshal.Copy(grow13, 0, IntPtr.Add(p13r, 2 * 4), 2);   // write the tail
        int[] dst13 = new int[4];
        Marshal.Copy(p13r, dst13, 0, 4);
        Marshal.FreeHGlobal(p13r);
        Console.WriteLine("r_grow=" + (dst13[0] == 111 && dst13[1] == 222 && dst13[2] == 333 && dst13[3] == 444));

        // ============ S14: ReAllocHGlobal SHRINK preserves min(old,new) ============
        // realloc to a SMALLER size -- only the first min(old,new) bytes
        // survive; the tail beyond the new size is gone.
        IntPtr p14 = Marshal.AllocHGlobal(4 * 4);
        int[] src14 = { 1, 2, 3, 4 };
        Marshal.Copy(src14, 0, p14, 4);
        IntPtr p14r = Marshal.ReAllocHGlobal(p14, new IntPtr(2 * 4));
        int[] dst14 = new int[4];
        Marshal.Copy(p14r, dst14, 0, 2);       // read only the surviving prefix
        Marshal.FreeHGlobal(p14r);
        Console.WriteLine("r_shrink=" + (dst14[0] == 1 && dst14[1] == 2));

        // ============ S15: shrink-then-grow round trip ============
        // ReAllocHGlobal twice: shrink to 2 ints (prefix {1,2} survives),
        // then grow back to 4 ints and WRITE the recovered tail slots --
        // the round trip keeps the buffer usable and the content byte-exact.
        IntPtr p15 = Marshal.AllocHGlobal(4 * 4);
        int[] src15 = { 10, 20, 30, 40 };
        Marshal.Copy(src15, 0, p15, 4);
        IntPtr p15s = Marshal.ReAllocHGlobal(p15, new IntPtr(2 * 4));
        IntPtr p15g = Marshal.ReAllocHGlobal(p15s, new IntPtr(4 * 4));
        int[] tail15 = { 30, 40 };
        Marshal.Copy(tail15, 0, IntPtr.Add(p15g, 2 * 4), 2);   // rewrite the tail
        int[] dst15 = new int[4];
        Marshal.Copy(p15g, dst15, 0, 4);
        Marshal.FreeHGlobal(p15g);
        Console.WriteLine("r_roundtrip=" + (dst15[0] == 10 && dst15[1] == 20 && dst15[2] == 30 && dst15[3] == 40));

        // ============ S16: realloc stress -- grow/shrink/read cycle ============
        // Repeated realloc (3 growth steps) preserves the written prefix each
        // time and keeps the buffer usable with Copy read-back after every
        // step. Only WRITTEN slots are read back (D-HG-3: net48 does not zero
        // the realloc tail -- unwritten slots hold garbage and must not be
        // asserted).
        IntPtr p16 = Marshal.AllocHGlobal(1 * 4);
        int[] w16 = { 7 };
        Marshal.Copy(w16, 0, p16, 1);
        for (int i = 1; i <= 3; i++)
        {
            p16 = Marshal.ReAllocHGlobal(p16, new IntPtr((i + 1) * 4));
            int[] w16b = { 70 + i };
            Marshal.Copy(w16b, 0, p16, 1);     // keep overwriting slot 0
        }
        int[] dst16 = new int[1];
        Marshal.Copy(p16, dst16, 0, 1);        // read only the written slot
        Marshal.FreeHGlobal(p16);
        Console.WriteLine("r_stress=" + (dst16[0] == 73));
    }
}}
