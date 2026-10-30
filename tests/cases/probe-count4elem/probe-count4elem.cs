//------------------------------------------------------------------------------
// probe-count4elem -- the ARRAY Aggregate(seed, func, resultSelector) ELEMENT
// FAMILY matrix (RED/GREEN e2e probe).
//
// The count==4 mapping used to pick the fold kernel from the ACCUMULATOR family
// only (seed_i4/seed_i8/seed_f/seed_d, whose element reader is a fixed 4-byte
// int32 read) and ignored the SOURCE element family. Measured (audit-fix3
// section 3.2): Aggregate(0.5f, (a,x)=>a+x, a=>...) over a float source folded
// the int32 bit pattern of every float cell -- a SILENT wrong value.
//
// This probe pins the (element family x accumulator family x result family)
// cells of the count==4 ARRAY surface that the old mapping got wrong:
//   element: int64[] / real float[] (4-byte) / float Select carrier (8-byte) /
//            double[] / byte[] / string[]
//   acc:     int32 / int64 / float (F-stack carrier) / double / decimal
//   result:  int32 / float / double
// plus the unseeded (count==2) narrow diagonal.
//
// NOTE on probe size: every cached lambda is a static reference field and thus
// a runtime GC static root. The runtime's static-root table was ORIGINALLY 128
// slots, which forced this probe to stay below ~64 lambdas; it has since been
// raised to 4096 (gc.cpp), so the matrix below is no longer constrained. The
// limit was always OUR runtime's artificial ceiling -- net48 has no such table
// and never aborted on a large matrix.
//
// Every float/double is printed with its raw bit pattern so a width/ABI
// misread is visible byte-for-byte against the net48 oracle.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeCount4Elem
    {
        private static string FB(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.ToUInt32(BitConverter.GetBytes(v), 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string DB(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.DoubleToInt64Bits(v).ToString(CultureInfo.InvariantCulture);
        }

        private static string S(string v) { return v ?? "<null>"; }

        private static string Dec(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        // element int64[]
        private static void ElemI8(long[] a)
        {
            Console.WriteLine("[i8_f_i4]=" + a.Aggregate(0.5f, (x, y) => x + y, x => (int)(x * 10f)).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_f_f]=" + FB(a.Aggregate(0.5f, (x, y) => x + y, x => x * 2f)));
            Console.WriteLine("[i8_d_d]=" + DB(a.Aggregate(0.5, (x, y) => x + y, x => x / 4.0)));
            Console.WriteLine("[i8_i4_i4]=" + a.Aggregate(0, (x, y) => x + (int)y, x => x * 10).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_i8_i8]=" + a.Aggregate(0L, (x, y) => x + y, x => x * 3L).ToString(CultureInfo.InvariantCulture));
        }

        // element float[] (4-byte real cells OR 8-byte Select carrier)
        private static void ElemF(float[] a)
        {
            Console.WriteLine("[f_f_i4]=" + a.Aggregate(0.5f, (x, y) => x + y, x => (int)(x * 10f)).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_f_f]=" + FB(a.Aggregate(0.5f, (x, y) => x + y, x => x * 2f)));
            Console.WriteLine("[f_d_d]=" + DB(a.Aggregate(0.5, (x, y) => x + y, x => x / 4.0)));
            Console.WriteLine("[f_i4_i4]=" + a.Aggregate(0, (x, y) => x + (int)y, x => x * 10).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_i8_i8]=" + a.Aggregate(0L, (x, y) => x + (long)y, x => x * 3L).ToString(CultureInfo.InvariantCulture));
        }

        // element double[]
        private static void ElemD(double[] a)
        {
            Console.WriteLine("[d_f_f]=" + FB(a.Aggregate(0.5f, (x, y) => x + (float)y, x => x * 2f)));
            Console.WriteLine("[d_d_d]=" + DB(a.Aggregate(0.5, (x, y) => x + y, x => x / 4.0)));
            Console.WriteLine("[d_i4_i4]=" + a.Aggregate(0, (x, y) => x + (int)y, x => x * 10).ToString(CultureInfo.InvariantCulture));
        }

        public static void Main()
        {
            int[] ai = { 1, 2, 3, 4 };
            long[] al = { 1L, 2L, 3L, 4L };
            float[] afl = { 0.5f, 1.25f, 2.5f, 4.0f };
            float[] afc = ai.Select(x => (float)(x * 1.5)).ToArray();
            double[] ad = { 0.5, 1.25, 2.5, 4.0 };
            byte[] aby = { 200, 100, 55 };
            string[] asr = { "a", "bb", "ccc" };
            char[] ach = { 'a', 'b', 'c' };

            ElemI8(al);
            Console.WriteLine("--- float 4-byte real ---");
            ElemF(afl);
            Console.WriteLine("--- float 8-byte carrier ---");
            ElemF(afc);
            Console.WriteLine("--- double ---");
            ElemD(ad);

            // element int32[] control cells (correct before this fix)
            Console.WriteLine("[i4_f_f]=" + FB(ai.Aggregate(0.5f, (x, y) => x + y, x => x * 2f)));
            Console.WriteLine("[i4_d_d]=" + DB(ai.Aggregate(0.5, (x, y) => x + y, x => x / 4.0)));
            Console.WriteLine("[i4_i4_i4]=" + ai.Aggregate(0, (x, y) => x + y, x => x * 10).ToString(CultureInfo.InvariantCulture));

            // narrow element
            Console.WriteLine("[by_i4_i4]=" + aby.Aggregate(0, (x, y) => x + y, x => x * 10).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[by_f_f]=" + FB(aby.Aggregate(0f, (x, y) => x + y, x => x * 2f)));
            Console.WriteLine("[ch_i4_i4]=" + ach.Aggregate(0, (x, y) => x + y, x => x * 2).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[ch_l_l]=" + ach.Aggregate(0L, (x, y) => x + y, x => x * 3L).ToString(CultureInfo.InvariantCulture));

            // reference element with a numeric accumulator
            Console.WriteLine("[sr_i4_i4]=" + asr.Aggregate(0, (x, y) => x + y.Length, x => x * 10).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[sr_d_d]=" + DB(asr.Aggregate(0.0, (x, y) => x + y.Length, x => x / 4.0)));
            Console.WriteLine("[sr_l_l]=" + asr.Aggregate(0L, (x, y) => x + y.Length, x => x * 3L).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[sr_f_f]=" + FB(asr.Aggregate(0f, (x, y) => x + y.Length, x => x * 2f)));

            // reference accumulator over a float element source
            Console.WriteLine("[fr_ref_ref]=" + S(afl.Aggregate("s", (x, y) => x + ((int)y).ToString(CultureInfo.InvariantCulture), x => "[" + x + "]")));

            // decimal accumulator over the non-int32 element families (the base
            // seed_dec_rs read a 4-byte int32 out of every cell)
            Console.WriteLine("[dc_i8]=" + Dec(al.Aggregate(0m, (a, x) => a + (decimal)(int)x, r => r * 2m)));
            Console.WriteLine("[dc_f]=" + Dec(afl.Aggregate(0m, (a, x) => a + (decimal)(int)(x * 10f), r => r * 2m)));
            Console.WriteLine("[dc_d]=" + Dec(ad.Aggregate(0m, (a, x) => a + (decimal)(int)(x * 10.0), r => r * 2m)));
            Console.WriteLine("[dc_by]=" + Dec(aby.Aggregate(0m, (a, x) => a + (decimal)(int)x, r => r * 2m)));
            Console.WriteLine("[dc_ref]=" + Dec(asr.Aggregate(0m, (a, x) => a + (decimal)x.Length, r => r * 2m)));

            // unseeded (count==2): wide and narrow diagonals
            Console.WriteLine("[un_i8]=" + al.Aggregate((x, y) => x + y).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[un_flit]=" + FB(afl.Aggregate((x, y) => x + y)));
            Console.WriteLine("[un_fcar]=" + FB(afc.Aggregate((x, y) => x + y)));
            Console.WriteLine("[un_by]=" + aby.Aggregate((x, y) => (byte)(x + y)).ToString(CultureInfo.InvariantCulture));

            // empty source: the fold returns the seed, then the selector runs on it
            float[] afEmpty = new float[0];
            long[] alEmpty = new long[0];
            Console.WriteLine("[e_f_f]=" + FB(afEmpty.Aggregate(0.5f, (x, y) => x + y, x => x * 2f)));
            Console.WriteLine("[e_i8_i4]=" + alEmpty.Aggregate(7, (x, y) => x + (int)y, x => x * 10).ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("done");
        }
    }
}
