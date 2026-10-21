using System;
using System.Collections.Generic;
using System.Linq;

// B5-R5: Zip over the SEVEN narrow element families (byte/sbyte/short/ushort/
// char/bool/uint) and over MIXED families.
//
// Two defects this case is designed to catch:
//   * il2cpp_linq_list_zip used to pass the IL2CPP_LIST_KIND_* code straight
//     through as the zip `kind`, which the runtime interprets as the
//     IL2CPP_LINQ_NARROW_* space. The two enumerations share the small integers
//     with DIFFERENT meanings (LIST_KIND SBYTE=5 vs NARROW CHAR=5, LIST_KIND
//     BYTE=6 vs NARROW UINT=6), so a 1-byte cell was read as char16_t/uint32 --
//     an out-of-bounds read that printed garbage (measured before the fix:
//     1441803,2162710,33 instead of 11,22,33).
//   * the second source was read with the RECEIVER's family, so a 4-byte int32
//     cell was reinterpreted as a float in List<double>.Zip(int[]) (measured:
//     1005009872 instead of 11.5).
//
// Narrow RESULT families (sbyte/byte/short/ushort/char/bool) are rejected at
// transpile time -- see loud-p0-17-zipres-loud; a uint result IS wired
// (4-byte cell, elemKind 5) and is asserted here through explicit casts.
namespace Cases
{
    public static class LoudP015ZipNarrow
    {
        static string JoinI(int[] a) { return string.Join(",", a); }
        static string JoinL(long[] a) { return string.Join(",", a); }
        static string JoinD(double[] a) { return string.Join(",", a); }
        static string JoinF(float[] a) { return string.Join(",", a); }

        public static void Main()
        {
            List<byte> bl = new List<byte> { 1, 200 };
            List<sbyte> sbl = new List<sbyte> { -1, 127 };
            List<short> shl = new List<short> { -2, 300 };
            List<ushort> usl = new List<ushort> { 1, 65535 };
            List<char> chl = new List<char> { 'a', 'z' };
            List<bool> bll = new List<bool> { true, false };
            List<uint> uil = new List<uint> { 1, 4000000000 };
            List<double> dl = new List<double> { 1.5, 2.5 };
            List<long> ll = new List<long> { 3000000000L, 2L };
            List<float> fl = new List<float> { 1.5f, 2.5f };
            List<int> il = new List<int> { 10, 20 };
            int[] ia = { 10, 20 };

            // ===== the seven narrow families, each with itself (int32 result) =====
            Console.WriteLine("[z-byte-int]=" + JoinI(bl.Zip(bl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-sbyte-int]=" + JoinI(sbl.Zip(sbl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-short-int]=" + JoinI(shl.Zip(shl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-ushort-int]=" + JoinI(usl.Zip(usl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-char-int]=" + JoinI(chl.Zip(chl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-bool-int]=" + JoinI(bll.Zip(bll, (a, b) => (a ? 1 : 0) + (b ? 1 : 0)).ToArray()));

            // ===== 1-byte pairs of DIFFERENT signedness =====
            Console.WriteLine("[z-sbyte-bool]=" + JoinI(sbl.Zip(bll, (a, b) => a * 10 + (b ? 1 : 0)).ToArray()));
            Console.WriteLine("[z-bool-byte]=" + JoinI(bll.Zip(bl, (a, b) => (a ? 1000 : 0) + b).ToArray()));

            // ===== 2-byte pairs of DIFFERENT signedness =====
            Console.WriteLine("[z-ushort-char]=" + JoinI(usl.Zip(chl, (a, b) => a * 100 + b).ToArray()));
            Console.WriteLine("[z-short-ushort]=" + JoinL(shl.Zip(usl, (a, b) => (long)a * 100000 + b).ToArray()));

            // ===== 1-byte source with a 2-byte source (different cell widths) =====
            Console.WriteLine("[z-byte-short]=" + JoinL(bl.Zip(shl, (a, b) => (long)a * 1000 + b).ToArray()));

            // ===== narrow with an int32 source / an int32 source array =====
            Console.WriteLine("[z-byte-iarr]=" + JoinI(bl.Zip(ia, (a, b) => a * 100 + b).ToArray()));
            Console.WriteLine("[z-ilist-short]=" + JoinI(il.Zip(shl, (a, b) => a * 100 + b).ToArray()));

            // ===== Mixed wide families (each side read at its OWN width) =====
            Console.WriteLine("[z-ilist-dlist-int]=" + JoinI(il.Zip(dl, (a, b) => (int)(a + b)).ToArray()));
            Console.WriteLine("[z-dlist-ilist-long]=" + JoinL(dl.Zip(il, (a, b) => (long)(a * 1000000000.0 + b)).ToArray()));
            Console.WriteLine("[z-dlist-iarr-long]=" + JoinL(dl.Zip(ia, (a, b) => (long)(a * 1000000000.0 + b)).ToArray()));
            Console.WriteLine("[z-dlist-iarr-int]=" + JoinI(dl.Zip(ia, (a, b) => (int)(a + b)).ToArray()));
            Console.WriteLine("[z-dlist-byte-dbl]=" + JoinD(dl.Zip(bl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-longlist-dlist-dbl]=" + JoinD(ll.Zip(dl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-dlist-longlist-long]=" + JoinL(dl.Zip(ll, (a, b) => (long)(a + b)).ToArray()));
            Console.WriteLine("[z-longlist-iarr-dbl]=" + JoinL(ll.Zip(ia, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-uint-longarr-long]=" + JoinL(uil.Zip(new long[] { 5L, 6L }, (a, b) => a + b).ToArray()));

            // ===== Result families: uint32 (4-byte cell, elemKind 5) and float =====
            uint[] uintRes = uil.Zip(uil, (a, b) => a + b).ToArray();
            Console.WriteLine("[z-uint-res]=" + uintRes.Length + ":" + (long)uintRes[0] + "," + (long)uintRes[1]);
            Console.WriteLine("[z-float-res]=" + JoinF(dl.Zip(dl, (a, b) => (float)(a + b)).ToArray()));
            Console.WriteLine("[z-float-src-res]=" + JoinF(fl.Zip(fl, (a, b) => a + b).ToArray()));
            Console.WriteLine("[z-float-src-dbl]=" + JoinD(fl.Zip(dl, (a, b) => a + b).ToArray()));

            // ===== Pairing stops at the SHORTER source (net48) =====
            Console.WriteLine("[z-shorter]=" + JoinL(bl.Zip(new short[] { 7, 8, 9 }, (a, b) => (long)a * 100 + b).ToArray()));
        }
    }
}
