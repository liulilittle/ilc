//------------------------------------------------------------------------------
// arrgen1b.cs -- Array predicate/scan family (FLOAT + uint64) probe.
//
// arrgen1 covered the int64 / string / double families (the arrgen1 batch).
// This case drives the two families that batch left LOUD:
//
//   float    (0x0C)   float[]  -- the double-slot predicate ABI (the transpiler
//                               stores float in a double slot, so a float leaf
//                               widens to double; the runtime invokes it through
//                               InvokeDPred)
//   uint64   (0x0B)   ulong[]  -- the 8-byte value family with no dedicated
//                               elemKind (the plain int64-style kernels)
//
// plus the Buffer.SetByte and ArrayList(int capacity) residuals. Every line
// prints a label + bool/int; net48 is the byte oracle.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases
{
    public static class ArrGen1B
    {
        private static bool IsBig(float x) { return x > 1.5f; }
        private static bool IsNeg(float x) { return x < 0f; }
        private static bool IsEven(ulong x) { return (x & 1UL) == 0UL; }
        private static bool IsBigL(ulong x) { return x > 10UL; }

        private static int g_positives;

        private static void Accum(float x) { if (x > 0f) g_positives++; }

        public static void Main()
        {
            // ---- A. Array.Empty<ulong> ----
            ulong[] eu = Array.Empty<ulong>();
            Console.WriteLine("empty_u8_len=" + (eu != null && eu.Length == 0));

            // ---- B. Array.Reverse<ulong> ----
            ulong[] ru = { 1UL, 2UL, 3UL, 4UL };
            Array.Reverse(ru);
            Console.WriteLine("rev_u8=" + (ru[0] == 4UL && ru[1] == 3UL && ru[2] == 2UL && ru[3] == 1UL));
            ulong[] ru1 = { 9UL };
            Array.Reverse(ru1);
            Console.WriteLine("rev_u8_single=" + (ru1[0] == 9UL));

            // ---- C. Array.Find<float> ----
            float[] fa = { 1.0f, 2.5f, -3.0f, 4.0f };
            Console.WriteLine("f_f=" + (Array.Find(fa, IsBig) == 2.5f));
            Console.WriteLine("f_f_neg=" + (Array.Find(fa, IsNeg) == -3.0f));
            Console.WriteLine("f_f_miss=" + (Array.Find(fa, x => x > 100f) == 0f));
            float[] fe = new float[0];
            Console.WriteLine("f_f_empty=" + (Array.Find(fe, IsBig) == 0f));

            // ---- D. Array.FindLast<float> ----
            float[] fla = { 2.0f, 3.0f, 5.0f };
            Console.WriteLine("fl_f=" + (Array.FindLast(fla, IsBig) == 5.0f));
            Console.WriteLine("fl_f_miss=" + (Array.FindLast(fla, x => x > 100f) == 0f));

            // ---- E. Array.FindIndex<float> / <double> ----
            Console.WriteLine("fi_f=" + Array.FindIndex(fa, IsBig));
            Console.WriteLine("fi_f_3=" + Array.FindIndex(fa, 1, IsBig));
            Console.WriteLine("fi_f_4=" + Array.FindIndex(fa, 0, 3, IsBig));
            Console.WriteLine("fi_f_miss=" + Array.FindIndex(fa, x => x > 100f));
            double[] da = { 1.0, 2.5, -3.0, 4.0 };
            Console.WriteLine("fi_d=" + Array.FindIndex(da, x => x < 0));
            Console.WriteLine("fi_d_miss=" + Array.FindIndex(da, x => x > 100));

            // ---- F. Array.FindLastIndex<float> / <long> / <string> / <double> ----
            Console.WriteLine("fli_f=" + Array.FindLastIndex(fla, IsBig));
            Console.WriteLine("fli_f_miss=" + Array.FindLastIndex(fla, x => x > 100f));
            long[] la = { 1L, 2L, 3L, 2L };
            Console.WriteLine("fli_i8=" + Array.FindLastIndex(la, x => x == 2L));
            Console.WriteLine("fli_i8_miss=" + Array.FindLastIndex(la, x => x == 9L));
            string[] sa = { "a", "bb", "a" };
            Console.WriteLine("fli_str=" + Array.FindLastIndex(sa, x => x == "a"));
            Console.WriteLine("fli_d=" + Array.FindLastIndex(da, x => x < 0));
            // net48 FindLastIndex(array, startIndex, count) scans BACKWARD over
            // [startIndex - count + 1, startIndex]; this window is [0, 2].
            Console.WriteLine("fli_d_4=" + Array.FindLastIndex(da, 2, 3, x => x < 0));
            Console.WriteLine("fli_d_3=" + Array.FindLastIndex(da, 2, x => x < 0));
            bool fliAoo = false;
            try { Array.FindLastIndex(da, 0, 3, x => x < 0); } catch (ArgumentOutOfRangeException) { fliAoo = true; }
            Console.WriteLine("fli_aoo=" + fliAoo);

            // ---- G. Array.Exists<float> ----
            Console.WriteLine("e_f=" + Array.Exists(fa, IsBig));
            Console.WriteLine("e_f_neg=" + Array.Exists(fa, IsNeg));
            Console.WriteLine("e_f_false=" + Array.Exists(fa, x => x > 100f));
            Console.WriteLine("e_f_empty=" + Array.Exists(fe, IsBig));

            // ---- H. Array.TrueForAll<float> ----
            Console.WriteLine("t_f=" + Array.TrueForAll(fa, x => x < 100f));
            Console.WriteLine("t_f_false=" + Array.TrueForAll(fa, IsBig));
            Console.WriteLine("t_f_empty=" + Array.TrueForAll(fe, x => false));

            // ---- I. Array.FindAll<float> ----
            float[] fall = Array.FindAll(fa, IsBig);
            Console.WriteLine("fall_f_len=" + fall.Length);
            Console.WriteLine("fall_f_v=" + (fall[0] == 2.5f && fall[1] == 4.0f));
            Console.WriteLine("fall_f_empty_len=" + Array.FindAll(fe, IsBig).Length);

            // ---- J. Array.ForEach<float> ----
            g_positives = 0;
            Array.ForEach(fa, Accum);
            Console.WriteLine("fe_pos=" + (g_positives == 3));

            // ---- K. Array.Resize<float> ----
            float[] rz = { 1.5f, 2.5f, 3.5f };
            Array.Resize(ref rz, 5);
            Console.WriteLine("rz_f_len=" + (rz.Length == 5));
            Console.WriteLine("rz_f_keep=" + (rz[0] == 1.5f && rz[2] == 3.5f));
            Console.WriteLine("rz_f_pad=" + (rz[3] == 0f && rz[4] == 0f));
            Array.Resize(ref rz, 2);
            Console.WriteLine("rz_f_shrink=" + (rz.Length == 2 && rz[1] == 2.5f));

            // ---- L. Array.Exists<ulong> / Find<ulong> / TrueForAll<ulong> ----
            ulong[] ua = { 1UL, 2UL, 3UL, 4UL, 5UL };
            Console.WriteLine("e_u8=" + Array.Exists(ua, IsBigL));
            Console.WriteLine("e_u8_false=" + Array.Exists(ua, x => x > 100UL));
            Console.WriteLine("f_u8_miss=" + (Array.Find(ua, IsBigL) == 0UL));
            Console.WriteLine("f_u8_hit=" + (Array.Find(ua, IsEven) == 2UL));
            Console.WriteLine("t_u8=" + Array.TrueForAll(ua, x => x < 100UL));
            Console.WriteLine("fli_u8=" + Array.FindLastIndex(ua, IsBigL));
            Console.WriteLine("fli_u8_2=" + Array.FindLastIndex(ua, 1, IsBigL));
            ulong[] fallU = Array.FindAll(ua, IsEven);
            Console.WriteLine("fall_u8=" + (fallU.Length == 2 && fallU[0] == 2UL && fallU[1] == 4UL));

            // ---- L2. narrow-integral families (bool/byte/char/short/ushort/uint) ----
            bool[] ba = { false, true, false, true };
            Console.WriteLine("e_b=" + Array.Exists(ba, x => x));
            Console.WriteLine("f_b=" + (Array.Find(ba, x => x) == true));
            Console.WriteLine("t_b=" + Array.TrueForAll(ba, x => !x == false));
            byte[] bya = { 1, 2, 3, 4 };
            Console.WriteLine("fi_u1=" + Array.FindIndex(bya, x => x == 3));
            Console.WriteLine("f_u1=" + (Array.Find(bya, x => x > 2) == 3));
            char[] ca = { 'a', 'b', 'c' };
            Console.WriteLine("fi_c=" + Array.FindIndex(ca, x => x == 'b'));
            short[] sha = { 1, 2, 3 };
            Console.WriteLine("f_i2=" + (Array.Find(sha, x => x > 1) == 2));
            ushort[] usa = { 1, 2, 3 };
            Console.WriteLine("e_u2=" + Array.Exists(usa, x => x == 3));
            uint[] uia = { 1u, 2u, 3u };
            Console.WriteLine("fli_u4=" + Array.FindLastIndex(uia, x => x < 3u));
            sbyte[] sba = { -1, 0, 1 };
            Console.WriteLine("e_i1=" + Array.Exists(sba, x => x < 0));
            byte[] rz2 = { 1, 2, 3 };
            Array.Resize(ref rz2, 5);
            Console.WriteLine("rz_u1=" + (rz2.Length == 5 && rz2[3] == 0));

            // ---- M. Buffer.SetByte ----
            byte[] bb = new byte[4];
            Buffer.SetByte(bb, 0, 0xAB);
            Buffer.SetByte(bb, 1, 0xCD);
            Console.WriteLine("sb=" + (bb[0] == 0xAB && bb[1] == 0xCD && bb[2] == 0 && bb[3] == 0));
            Console.WriteLine("sb_roundtrip=" + (Buffer.GetByte(bb, 0) == 0xAB));
            bool sbAoo = false;
            try { Buffer.SetByte(bb, -1, 0); } catch (ArgumentOutOfRangeException) { sbAoo = true; }
            Console.WriteLine("sb_aoo=" + sbAoo);
            // net48 raises ArgumentOutOfRangeException for BOTH a negative
            // and an out-of-range index (Buffer.GetByte/SetByte).
            bool sbIo = false;
            try { Buffer.SetByte(bb, 4, 0); } catch (ArgumentOutOfRangeException) { sbIo = true; }
            Console.WriteLine("sb_io=" + sbIo);

            // ---- O. Array.ConvertAll<float,...> / <...,float> ----
            float[] cfa = { 1.5f, 2.5f, 3.5f };
            int[] ci = Array.ConvertAll(cfa, x => (int)(x * 2));
            Console.WriteLine("ca_f_i4=" + (ci.Length == 3 && ci[0] == 3 && ci[1] == 5 && ci[2] == 7));
            float[] cf = Array.ConvertAll(ci, x => x + 0.5f);
            Console.WriteLine("ca_i4_f=" + (cf.Length == 3 && cf[0] == 3.5f && cf[2] == 7.5f));
            double[] cd = Array.ConvertAll(cfa, x => x * 2.0);
            Console.WriteLine("ca_f_d=" + (cd.Length == 3 && cd[1] == 5.0));
            float[] cff = Array.ConvertAll(cfa, x => x * 2f);
            Console.WriteLine("ca_f_f=" + (cff.Length == 3 && cff[1] == 5.0f));

            // ---- N. ArrayList(int capacity) ----
            ArrayList cap = new ArrayList(8);
            Console.WriteLine("al_cap0=" + cap.Capacity);
            Console.WriteLine("al_cnt0=" + cap.Count);
            cap.Add(10);
            cap.Add(20);
            Console.WriteLine("al_cnt2=" + cap.Count);
            Console.WriteLine("al_item1=" + ((int)cap[1] == 20));
            ArrayList empty = new ArrayList(0);
            Console.WriteLine("al_capz=" + empty.Capacity);
            empty.Add(7);
            Console.WriteLine("al_capz_grow=" + (empty.Capacity >= 1 && (int)empty[0] == 7));
            bool alAoo = false;
            try { new ArrayList(-1); } catch (ArgumentOutOfRangeException) { alAoo = true; }
            Console.WriteLine("al_aoo=" + alAoo);

            Console.WriteLine("done");
        }
    }
}
