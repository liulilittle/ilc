//------------------------------------------------------------------------------
// dhashoracle.cs -- RESEARCH ONLY (net48 oracle). Emits the exact expected
// records for the four e2e probe cases (decimalhash1/2/3, decimalfloat1) in
// the same "id=value" shape the probes print, so expected.txt can be
// cross-checked line by line against the runtime.
//
// Record shapes:
//   H <id> <hash>
//   D <id> <doubleBitsHex>
//   F <id> <floatBitsHex>
//   V <id> <invariant string>
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Cases
{
    public static class DHashOracle
    {
        static readonly decimal[] Values =
        {
            // --- scale 0, exactly representable ---
            0m, 1m, -1m, 2m, 7m, 10m, 100m, 1234m, 9007199254740992m, 9007199254740993m,
            4294967296m, 18446744073709551616m,
            // --- scale 1..5, exactly representable in binary ---
            1.5m, 2.5m, -1.5m, 0.5m, 0.25m, 0.125m, 1.0m, 1.00m, 100.00m,
            // --- scale 1..4, NOT exactly representable ---
            0.1m, 1.1m, 0.3m, 0.7m, 0.2m, 0.4m, 0.6m, 0.8m, 0.9m, -0.1m,
            1234.5678m, -1234.5678m, 123.456m,
            // --- trailing-zero variants of the SAME value (net48 does NOT canonicalize) ---
            1.10m, 1.100m, 1.1000m, 2.50m, 2.500m, 0.10m, 0.100m,
            // --- scale 10 / 28 ---
            0.0000000001m,
            0.0000000000000000000000000001m,
            0.0000000000000000000000000002m,
            0.0000000000000000000000000003m,
            -0.0000000000000000000000000001m,
            1.0000000000000000000000000001m,
            // --- 96-bit mantissas ---
            decimal.MaxValue, decimal.MinValue,
            123456789012345678901234567m,
            1234567890123456789012345678m,
            12345678901234567890123456789m,
            10000000000000000000000000000m,
            // --- irrational-ish literals ---
            3.1415926535897932384626433832m,
            2.7182818284590452353602874713m,
            0.3333333333333333333333333333m,
            1.2345678901234567890123456789m,
            // --- scale 23..27 (where the OLD to_double went wrong) ---
            1.23456789012345678901234m,
            1.234567890123456789012345m,
            1.2345678901234567890123456m,
            9.8765432109876543210987654m,
            // --- negative zero variants ---
            -0.0m, 0.00m, decimal.Negate(0m),
            // --- powers / boundary magnitudes ---
            0.0000000000000000000000000005m,
            7922816251426433759354395033m,
            // --- float conversion edge values ---
            16777216m, 16777217m, 16777215m,
            0.0000000000000000000000000004m,
        };

        static void Emit(decimal d, int index)
        {
            string id = index.ToString("D3", CultureInfo.InvariantCulture);
            double dd = (double)d;
            float ff = (float)d;
            long db = BitConverter.DoubleToInt64Bits(dd);
            int fb = BitConverter.ToInt32(BitConverter.GetBytes(ff), 0);
            int[] gb = decimal.GetBits(d);
            Console.WriteLine("G " + id + " " + gb[0].ToString(CultureInfo.InvariantCulture) + ","
                + gb[1].ToString(CultureInfo.InvariantCulture) + ","
                + gb[2].ToString(CultureInfo.InvariantCulture) + ","
                + gb[3].ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("H " + id + " " + d.GetHashCode().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("D " + id + " " + db.ToString("X16", CultureInfo.InvariantCulture));
            Console.WriteLine("F " + id + " " + fb.ToString("X8", CultureInfo.InvariantCulture));
            Console.WriteLine("V " + id + " " + d.ToString(CultureInfo.InvariantCulture));
        }

        public static void Main()
        {
            for (int i = 0; i < Values.Length; i++) Emit(Values[i], i);
            Console.WriteLine("count=" + Values.Length.ToString(CultureInfo.InvariantCulture));

            // ---- dictionary / hashset behaviour (decimalhash2) ----
            // (a) Dictionary<decimal,int> exact-value lookup after re-adding the
            //     same literal written with a different scale.
            var dict = new Dictionary<decimal, int>();
            dict[1.5m] = 10;
            dict[0.1m] = 20;
            dict[1234.5678m] = 30;
            dict[-2.25m] = 40;
            Console.WriteLine("K d_count=" + dict.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K d_get_1_5=" + dict[1.5m].ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K d_get_0_1=" + dict[0.1m].ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K d_get_1234_5678=" + dict[1234.5678m].ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K d_get_neg2_25=" + dict[-2.25m].ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K d_has_1_50=" + dict.ContainsKey(1.50m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K d_has_0_10=" + dict.ContainsKey(0.10m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K d_has_2_5=" + dict.ContainsKey(2.5m).ToString(CultureInfo.InvariantCulture));

            // (b) HashSet<decimal>
            var set = new HashSet<decimal>();
            Console.WriteLine("K s_add_1_5=" + set.Add(1.5m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K s_add_1_5_again=" + set.Add(1.5m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K s_add_1_50=" + set.Add(1.50m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K s_has_0_1=" + set.Contains(0.1m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K s_add_0_1=" + set.Add(0.1m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K s_add_0_10=" + set.Add(0.10m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K s_add_1234_5678=" + set.Add(1234.5678m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K s_count=" + set.Count.ToString(CultureInfo.InvariantCulture));

            // (c) Hashtable with boxed decimal keys (the runtime object model)
            var ht = new Hashtable();
            ht[1.5m] = "a";
            ht[0.1m] = "b";
            ht[1234.5678m] = "c";
            Console.WriteLine("K ht_count=" + ht.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K ht_get_1_5=" + ht[1.5m]);
            Console.WriteLine("K ht_get_0_1=" + ht[0.1m]);
            Console.WriteLine("K ht_get_1234_5678=" + ht[1234.5678m]);
            Console.WriteLine("K ht_has_1_50=" + ht.ContainsKey(1.50m).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("K ht_has_9_9=" + ht.ContainsKey(9.9m).ToString(CultureInfo.InvariantCulture));
        }
    }
}
