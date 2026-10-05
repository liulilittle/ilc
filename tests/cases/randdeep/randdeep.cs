//------------------------------------------------------------------------------
// randdeep.cs -- System.Random DEEP reference program (net48 oracle),
// third-generation random batch (randdeep). Extends randomcase (first fixed-
// seed sequence) and random2b (all Next forms / NextBytes fill / distribution
// bounds / argument validation) with the SPECIFIC deep probes requested by
// the wiring audit -- raw-value pinning and determinism/divergence contracts:
//
//   * MULTI-SEED RAW SEQUENCES  -- pinned first-5 raw Next() values for
//     seeds 42 / 1 / 0 / -1 / int.MaxValue / int.MinValue / 161803398
//     (the net48 Knuth subtractive generator -- il2cpp_random.h -- is a
//     faithful port, so every raw value must match byte-for-byte; also
//     covers negative / extreme seed handling in the ctor).
//   * NextBytes EXACT BYTES     -- all 16 bytes for seed 42 printed as ints
//     (not just a non-zero check), plus a second seed's 8 bytes.
//   * Next(min,max) PINNED      -- exact values across mixed ranges:
//     positive span, negative min, full int32 span (large-range path),
//     min == max edge, zero range.
//   * Next(max) BOUNDARY        -- Next(0) == 0, Next(1) == 0 (forced),
//     Next(int.MaxValue) in [0, int.MaxValue-1] pinned first value, and
//     the Sample-path boundary Next(0, int.MaxValue) (range == int.MaxValue).
//   * NextDouble PINNED         -- exact F6 text for a fixed seed (and a
//     range sanity over 200 draws).
//   * DETERMINISM (long)        -- 5 instances of seed 42, 50 Next() draws
//     each, all five sequences identical.
//   * DIVERGENCE                -- seed 42 vs seed 43 first draw differs;
//     seed 42 vs seed 42 identical (repeatability).
//   * ARGUMENT VALIDATION       -- Next(max<0) and Next(min>max) throw
//     ArgumentOutOfRangeException (net48 contract).
//
// Every line prints "label=<value>" (raw value or True/False). net48 is the
// reference baseline (the runtime implements the exact net48 Knuth
// subtractive generator -- il2cpp_random.h -- so pinned seed values match
// byte-for-byte).
//
// DELIBERATELY NOT PROBED (documented runtime gap -- do NOT call here):
//   * Random.Sample() (protected) -- no icall_random_sample (MethodMap.
//     RandomExtra.cs LOUD); only reachable from a derived Random subclass,
//     which is unsupported.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class RandDeep
    {
        public static void Main()
        {
            // ===== multi-seed raw sequences (first 5 Next() values) =====
            int[] seeds = { 42, 1, 0, -1, int.MaxValue, int.MinValue, 161803398 };
            foreach (int seed in seeds)
            {
                var r = new Random(seed);
                string line = "seed_" + seed + "=" + r.Next();
                for (int i = 0; i < 4; i++)
                    line += "," + r.Next();
                Console.WriteLine(line);
            }

            // ===== NextBytes exact bytes (seed 42, 16 bytes) =====
            var rb42 = new Random(42);
            byte[] buf = new byte[16];
            rb42.NextBytes(buf);
            string byteline = "bytes42=" + (int)buf[0];
            for (int i = 1; i < 16; i++)
                byteline += "," + (int)buf[i];
            Console.WriteLine(byteline);

            // ===== NextBytes exact bytes (seed 7, 8 bytes) =====
            var rb7 = new Random(7);
            byte[] buf7 = new byte[8];
            rb7.NextBytes(buf7);
            string byteline7 = "bytes7=" + (int)buf7[0];
            for (int i = 1; i < 8; i++)
                byteline7 += "," + (int)buf7[i];
            Console.WriteLine(byteline7);

            // ===== Next(min,max) pinned values across mixed ranges =====
            var rp = new Random(42);
            Console.WriteLine("mm_pos=" + rp.Next(10, 20));
            Console.WriteLine("mm_neg=" + rp.Next(-5, 5));
            Console.WriteLine("mm_wide=" + rp.Next(-1000000000, 1000000000));
            Console.WriteLine("mm_full=" + rp.Next(int.MinValue, int.MaxValue));
            Console.WriteLine("mm_eq=" + rp.Next(7, 7));          // min == max -> min
            Console.WriteLine("mm_zero=" + rp.Next(0, 0));        // zero range -> 0
            Console.WriteLine("mm_one=" + rp.Next(3, 4));         // range 1 -> 3

            // ===== Next(max) boundary pins =====
            var rmax = new Random(42);
            Console.WriteLine("max_zero=" + rmax.Next(0));                 // always 0
            Console.WriteLine("max_one=" + rmax.Next(1));                  // always 0
            Console.WriteLine("max_pin=" + rmax.Next(int.MaxValue));       // pinned in [0, Max-1]
            Console.WriteLine("max_ten=" + rmax.Next(10));
            int[] small = { 0, 0, 0 };
            small[rmax.Next(1)] = 1;                                       // forced 0 index
            int s0 = small[0], s1 = small[1], s2 = small[2];
            Console.WriteLine("max_small=" + s0 + "," + s1 + "," + s2);

            // ===== Next(0, int.MaxValue): Sample-path boundary (range == Max) =====
            var rbound = new Random(42);
            int bv = rbound.Next(0, int.MaxValue);
            Console.WriteLine("bound_sample_path=" + bv + ":" + (bv >= 0 && bv < int.MaxValue));

            // ===== NextDouble pinned + range sanity =====
            var rd42 = new Random(42);
            Console.WriteLine("dbl_pin1=" + rd42.NextDouble().ToString("F6"));
            Console.WriteLine("dbl_pin2=" + rd42.NextDouble().ToString("F6"));
            bool dblOk = true;
            for (int i = 0; i < 200; i++)
            {
                double d = rd42.NextDouble();
                if (!(d >= 0.0 && d < 1.0)) dblOk = false;
            }
            Console.WriteLine("dbl_range=" + dblOk);

            // ===== determinism: 5 instances x 50 draws, all identical =====
            int[] firstSeq = new int[50];
            var r0 = new Random(42);
            for (int i = 0; i < 50; i++) firstSeq[i] = r0.Next();
            bool allSame = true;
            for (int inst = 0; inst < 5; inst++)
            {
                var rn = new Random(42);
                for (int i = 0; i < 50; i++)
                    if (rn.Next() != firstSeq[i]) allSame = false;
            }
            Console.WriteLine("det_5x50=" + allSame);

            // ===== divergence / repeatability =====
            var d42 = new Random(42);
            var d43 = new Random(43);
            Console.WriteLine("div_42_43=" + (d42.Next() != d43.Next()));
            var e1 = new Random(42);
            var e2 = new Random(42);
            Console.WriteLine("rep_42=" + (e1.Next() == e2.Next()));

            // ===== argument validation =====
            bool aoore1 = false, aoore2 = false;
            var rarg = new Random(1);
            try { rarg.Next(-1); } catch (ArgumentOutOfRangeException) { aoore1 = true; }
            Console.WriteLine("arg_negmax=" + aoore1);
            try { rarg.Next(10, 2); } catch (ArgumentOutOfRangeException) { aoore2 = true; }
            Console.WriteLine("arg_minmax=" + aoore2);
        }
    }
}
