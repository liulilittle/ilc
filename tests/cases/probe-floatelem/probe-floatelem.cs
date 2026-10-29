//------------------------------------------------------------------------------
// probe-floatelem -- float ELEMENT CARRIER read-width matrix (RED/GREEN probe).
//
// The runtime materializes a float Select result as an 8-byte cell (a double
// carrier) while a genuine float[] literal is a 4-byte cell. Both are the same
// "float element family" to the transpiler, so every element reader on that
// path must follow the ARRAY's real cell width, never a hard-coded 4 bytes.
//
// Measured regression (p-nb9-float, commit 9ddcacfb routing): a seeded float
// Aggregate over a Select carrier routed to seed_f_f, whose FoldFF read 4 bytes
// out of each 8-byte double cell -- 1.0's low four bytes are 0, so the fold
// returned the seed. This probe pins every shape:
//   * source: Select carrier (8-byte, elemKind 4) / ref-source Select carrier
//     (8-byte, elemKind 3) / real float[] literal (4-byte) / List<float>
//   * Aggregate: unseeded (float acc) / seeded with a float acc / seeded with a
//     double acc / seeded with a reference acc
//   * Sum / Min / Max / Average over each source
//   * the (element family, float/double accumulator) cell matrix (int32, int64,
//     double, byte, short, reference) stays correct
//
// The net48 oracle is the byte reference: a wrong cell width shows up both in
// the value and in the float/double raw bit pattern.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeFloatElem
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

        public static void Main()
        {
            int[] data = { 1, 2, 3, 4 };
            string[] strs = { "a", "bb", "ccc", "dddd" };

            // 8-byte carrier from an int32 Select (elemKind 4, exact doubles).
            float[] car = data.Select(x => (float)(x * 1.5)).ToArray();
            // 8-byte carrier from a reference Select (elemKind 3, exact doubles).
            float[] carRef = strs.Select(s => (float)s.Length).ToArray();
            // A genuine 4-byte float[] literal.
            float[] lit = { 0.5f, 1.25f, 2.5f, 4.0f };
            List<float> lst = new List<float> { 0.5f, 1.25f, 2.5f, 4.0f };

            // ---- source: 8-byte int32-Select carrier ----
            Console.WriteLine("[car_unseed_f]=" + FB(car.Aggregate((a, b) => a + b)));
            Console.WriteLine("[car_seed_f]=" + FB(car.Aggregate(0.5f, (a, b) => a + b)));
            Console.WriteLine("[car_seed_d]=" + DB(car.Aggregate(0.5, (a, b) => a + (double)b)));
            Console.WriteLine("[car_seed_ref]=" + S(car.Aggregate("s", (a, b) => a + ((int)b).ToString(CultureInfo.InvariantCulture))));
            Console.WriteLine("[car_sum]=" + FB(car.Sum()));
            Console.WriteLine("[car_min]=" + FB(car.Min()));
            Console.WriteLine("[car_max]=" + FB(car.Max()));
            Console.WriteLine("[car_avg]=" + FB(car.Average()));

            // ---- source: 8-byte reference-Select carrier (elemKind 3) ----
            Console.WriteLine("[carref_unseed_f]=" + FB(carRef.Aggregate((a, b) => a + b)));
            Console.WriteLine("[carref_seed_f]=" + FB(carRef.Aggregate(0.5f, (a, b) => a + b)));
            Console.WriteLine("[carref_seed_d]=" + DB(carRef.Aggregate(0.5, (a, b) => a + (double)b)));
            Console.WriteLine("[carref_seed_ref]=" + S(carRef.Aggregate("s", (a, b) => a + ((int)b).ToString(CultureInfo.InvariantCulture))));
            Console.WriteLine("[carref_sum]=" + FB(carRef.Sum()));
            Console.WriteLine("[carref_min]=" + FB(carRef.Min()));
            Console.WriteLine("[carref_max]=" + FB(carRef.Max()));
            Console.WriteLine("[carref_avg]=" + FB(carRef.Average()));

            // ---- source: real 4-byte float[] literal ----
            Console.WriteLine("[lit_unseed_f]=" + FB(lit.Aggregate((a, b) => a + b)));
            Console.WriteLine("[lit_seed_f]=" + FB(lit.Aggregate(0.5f, (a, b) => a + b)));
            Console.WriteLine("[lit_seed_d]=" + DB(lit.Aggregate(0.5, (a, b) => a + (double)b)));
            Console.WriteLine("[lit_seed_ref]=" + S(lit.Aggregate("s", (a, b) => a + ((int)b).ToString(CultureInfo.InvariantCulture))));
            Console.WriteLine("[lit_sum]=" + FB(lit.Sum()));
            Console.WriteLine("[lit_min]=" + FB(lit.Min()));
            Console.WriteLine("[lit_max]=" + FB(lit.Max()));
            Console.WriteLine("[lit_avg]=" + FB(lit.Average()));

            // ---- source: List<float> (4-byte snapshot) ----
            Console.WriteLine("[list_unseed_f]=" + FB(lst.Aggregate((a, b) => a + b)));
            Console.WriteLine("[list_seed_f]=" + FB(lst.Aggregate(0.5f, (a, b) => a + b)));
            Console.WriteLine("[list_seed_d]=" + DB(lst.Aggregate(0.5, (a, b) => a + (double)b)));

            // ---- cell matrix: float/double accumulator over other element families ----
            long[] al = { 4294967298L, 3000000000L, -7L };
            double[] ad = { 0.5, 1.25, 2.5 };
            byte[] aby = { 200, 100, 55 };
            short[] ash = { -30000, 5, 1234 };
            Console.WriteLine("[i4_f]=" + FB(data.Aggregate(0f, (x, y) => x + y)));
            Console.WriteLine("[i4_d]=" + DB(data.Aggregate(0.0, (x, y) => x + y)));
            Console.WriteLine("[i8_f]=" + FB(al.Aggregate(0f, (x, y) => x + y)));
            Console.WriteLine("[i8_d]=" + DB(al.Aggregate(0.0, (x, y) => x + y)));
            Console.WriteLine("[d_f]=" + FB(ad.Aggregate(0f, (x, y) => x + (float)y)));
            Console.WriteLine("[by_f]=" + FB(aby.Aggregate(0f, (x, y) => x + y)));
            Console.WriteLine("[sh_f]=" + FB(ash.Aggregate(0f, (x, y) => x + y)));
            Console.WriteLine("[str_f]=" + FB(strs.Aggregate(0f, (x, y) => x + y.Length)));

            Console.WriteLine("done");
        }
    }
}
