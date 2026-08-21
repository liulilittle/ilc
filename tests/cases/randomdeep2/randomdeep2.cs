//------------------------------------------------------------------------------
// randomdeep2.cs -- System.Random DEEP probe #2 (e2e batch C70).
// Extends randomcase/random2b/randdeep (first seeds, all Next forms,
// NextBytes, distribution bounds, argument validation, multi-seed pins)
// with the SPECIFIC deep probes requested by the wiring audit -- SEED
// DISTRIBUTION and NextDouble BOUNDARY contracts:
//
//   * SEED DISTRIBUTION        -- a run of consecutive seeds 0..9 with the
//     first TWO raw Next() values pinned (adjacent-seed divergence), plus a
//     uniform spread audit over seeds 0..99: min/max of the first Next(),
//     a fixed 22-bucket histogram (each seed's first value / 1e8), the
//     number of occupied buckets, and adjacent-seed first-draw divergence
//     (0..9).
//   * EXTRA SEED PINNING        -- negative / large / mid-range seeds
//     (-5, -100, 1000000, 123456789, int.MinValue+1) first-two raw values.
//   * NextDouble BOUNDARY       -- over seeds 0..999 first-draw NextDouble():
//     every value in [0,1), none == 1.0, the count that are EXACTLY 0.0
//     (the generator can emit 0), and the min/max first-draw pinned as F6.
//   * SAMPLE-PATH CONSISTENCY   -- (int)(NextDouble() * max) == Next(max)
//     on fresh same-seed instances (both consume exactly one InternalSample
//     step), for max 10 / 100 / 1 -- the boundary between the two Sample()
//     paths.
//   * LONG-RUN SEQUENCE         -- seed 42 after 1000 warm-up draws
//     (draws 1000..1002 pinned) -- the subtractive generator's long-run
//     behavior.
//   * NextDouble PRECISION PIN  -- seed 123456 first three NextDouble() F6.
//
// Every line prints "label=<value>" (raw value, True/False, or a pinned
// double). net48 is the reference baseline (the runtime implements the exact
// net48 Knuth subtractive generator -- il2cpp_random.h -- so every pinned
// seed value and the whole histogram match byte-for-byte).
//
// DELIBERATELY NOT PROBED (documented runtime gap -- do NOT call here):
//   * Random.Sample() (protected) -- no icall_random_sample
//     (MethodMap.RandomExtra.cs LOUD); only reachable from a derived Random
//     subclass, which is unsupported.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class RandomDeep2
    {
        public static void Main()
        {
            // ===== S1: seed distribution -- consecutive seeds 0..9, first 2 draws =====
            for (int seed = 0; seed < 10; seed++)
            {
                var r = new Random(seed);
                Console.WriteLine("sd_" + seed + "=" + r.Next() + "," + r.Next());
            }

            // ===== S2: spread audit over seeds 0..99 (first Next() each) =====
            int[] buckets = new int[22];
            int mn = int.MaxValue, mx = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                int v = new Random(seed).Next();
                if (v < mn) mn = v;
                if (v > mx) mx = v;
                buckets[v / 100000000]++;
            }
            int nb = 0;
            string hist = "";
            for (int i = 0; i < 22; i++)
            {
                if (buckets[i] > 0) nb++;
                if (i > 0) hist += ",";
                hist += buckets[i];
            }
            Console.WriteLine("sprd_min=" + (mn >= 0));
            Console.WriteLine("sprd_max=" + (mx < int.MaxValue));
            Console.WriteLine("sprd_buckets=" + nb);
            Console.WriteLine("sprd_hist=" + hist);

            // ===== S3: adjacent-seed divergence (0..9) =====
            for (int seed = 0; seed < 9; seed++)
            {
                int a = new Random(seed).Next();
                int b = new Random(seed + 1).Next();
                Console.WriteLine("div_" + seed + "_" + (seed + 1) + "=" + (a != b));
            }

            // ===== S4: extra seed pins (negative / large / mid-range) =====
            int[] extra = { -5, -100, 1000000, 123456789, int.MinValue + 1 };
            foreach (int seed in extra)
            {
                var r = new Random(seed);
                Console.WriteLine("ex_" + seed + "=" + r.Next() + "," + r.Next());
            }

            // ===== S5: NextDouble boundary -- seeds 0..999 first draw =====
            int zeroCount = 0;
            double ndMin = 1.0, ndMax = 0.0;
            bool allGe0 = true, allLt1 = true;
            for (int seed = 0; seed < 1000; seed++)
            {
                double d = new Random(seed).NextDouble();
                if (d < 0.0) allGe0 = false;
                if (d >= 1.0) allLt1 = false;
                if (d == 0.0) zeroCount++;
                if (d < ndMin) ndMin = d;
                if (d > ndMax) ndMax = d;
            }
            Console.WriteLine("nd_ge0=" + allGe0);
            Console.WriteLine("nd_lt1=" + allLt1);
            Console.WriteLine("nd_none1=" + (ndMax < 1.0));
            Console.WriteLine("nd_zero=" + zeroCount);
            Console.WriteLine("nd_min=" + ndMin.ToString("F6"));
            Console.WriteLine("nd_max=" + ndMax.ToString("F6"));

            // ===== S6: Sample-path consistency (int)(NextDouble()*max)==Next(max) =====
            var rd10 = new Random(42); var rn10 = new Random(42);
            Console.WriteLine("cons10=" + ((int)(rd10.NextDouble() * 10) == rn10.Next(10)));
            var rd100 = new Random(42); var rn100 = new Random(42);
            Console.WriteLine("cons100=" + ((int)(rd100.NextDouble() * 100) == rn100.Next(100)));
            var rd1 = new Random(42); var rn1 = new Random(42);
            Console.WriteLine("cons1=" + ((int)(rd1.NextDouble() * 1) == rn1.Next(1)));

            // ===== S7: long-run sequence -- seed 42 after 1000 warm-up draws =====
            var rl = new Random(42);
            for (int i = 0; i < 1000; i++) rl.Next();
            Console.WriteLine("long_1000=" + rl.Next());
            Console.WriteLine("long_1001=" + rl.Next());
            Console.WriteLine("long_1002=" + rl.Next());

            // ===== S8: NextDouble precision pin -- seed 123456 first 3 =====
            var rp = new Random(123456);
            Console.WriteLine("pin_d1=" + rp.NextDouble().ToString("F6"));
            Console.WriteLine("pin_d2=" + rp.NextDouble().ToString("F6"));
            Console.WriteLine("pin_d3=" + rp.NextDouble().ToString("F6"));
        }
    }
}
