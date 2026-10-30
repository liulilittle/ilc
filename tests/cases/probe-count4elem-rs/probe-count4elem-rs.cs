//------------------------------------------------------------------------------
// probe-count4elem-rs -- the count==4 ARRAY Aggregate SILENT WRONG VALUE probe.
//
// probe-count4elem (the wide matrix) aborts at the baseline because the old
// element read walks off the cell, so its RED evidence is "no records". The
// dangerous half of the same defect is the SILENT varying value: the fold read
// a 4-byte int32 out of every cell and answered a plausible number. This probe
// keeps exactly the shapes the audit measured (sel_seed_rs, same naming) so a
// RED run prints WRONG values instead of crashing:
//   * float[] literal (4-byte cells)        -> fold returned the SEED
//   * float Select carrier (8-byte cells)   -> fold returned the SEED
//   * byte[] (1-byte cells)                 -> garbage int32
//   * long[] with an int32 accumulator      -> low 32 bits only
//   * double[] (already correct before)     -> control row
//
// net48 is the oracle; float/double print their raw bit pattern.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeCount4ElemRs
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

        public static void Main()
        {
            float[] afl = { 0.5f, 1.25f, 2.5f, 4.0f };
            float[] afc = new int[] { 1, 2, 3, 4 }.Select(x => (float)(x * 1.5)).ToArray();
            double[] ad = { 0.5, 1.25, 2.5, 4.0 };
            long[] al = { 1L, 2L, 3L, 4L };
            byte[] aby = { 200, 100, 55 };

            Console.WriteLine("[sel_seed_rs_flit]=" + FB(afl.Aggregate(0.5f, (a, x) => a + x, r => r * 2f)));
            Console.WriteLine("[sel_seed_rs_fcar]=" + FB(afc.Aggregate(0.5f, (a, x) => a + x, r => r * 2f)));
            Console.WriteLine("[sel_seed_rs_dlit]=" + DB(ad.Aggregate(0.5, (a, x) => a + x, r => r / 4.0)));
            Console.WriteLine("[sel_seed_rs_long]=" + al.Aggregate(0, (a, x) => a + (int)x, r => r * 10).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[sel_seed_rs_longf]=" + FB(al.Aggregate(0f, (a, x) => a + x, r => (int)r)));
            Console.WriteLine("[sel_seed_rs_byte]=" + aby.Aggregate(0, (a, x) => a + x, r => r * 10).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("done");
        }
    }
}
