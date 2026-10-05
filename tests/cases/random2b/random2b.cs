//------------------------------------------------------------------------------
// random2b.cs -- System.Random DEEP reference program (net48 oracle),
// second-generation random batch (random2b). Extends randomcase (fixed-seed
// first sequence) with the SPECIFIC probe families requested by the wiring
// audit:
//
//   * Next ALL FORMS       -- Next() / Next(max) / Next(min, max) /
//     NextDouble(), with the parameterless ctor (time-seeded) sanity-checked
//     for range only (non-deterministic seed).
//   * NextBytes            -- byte array fill (1-byte stride), zero-length
//     array no-op, null -> ArgumentNullException.
//   * FIXED-SEED SEQUENCE CONSISTENCY -- two Random(42) instances produce
//     IDENTICAL first-10 sequences (determinism contract); a fixed seed's
//     sequence is REPRODUCIBLE across instances; pinned net48 seed-42 values.
//   * DISTRIBUTION RANGE BOUNDARIES   -- Next(max) in [0, max-1], Next(min,
//     max) in [min, max-1] (incl. the full int32 span), NextDouble() in
//     [0, 1), Next(0) == 0, Next(max<0) / Next(min>max) ->
//     ArgumentOutOfRangeException.
//
// Every line prints "label=True/False" or a raw value. net48 is the
// reference baseline (the runtime implements the exact net48 Knuth
// subtractive generator -- il2cpp_random.h -- so seed-42 raw values match
// byte-for-byte).
//
// DELIBERATELY NOT PROBED (documented runtime gap -- do NOT call here):
//   * Random.Sample() (protected) -- the runtime has no icall_random_sample
//     (MethodMap.RandomExtra.cs LOUD); only reachable from a derived
//     subclass override, which is unsupported.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Random2b
    {
        public static void Main()
        {
            // ===== Next all forms (fixed seed 42) =====
            var r = new Random(42);
            Console.WriteLine("n1=" + r.Next());
            Console.WriteLine("n2=" + r.Next());
            Console.WriteLine("n3=" + r.Next());
            Console.WriteLine("max_small=" + r.Next(10));
            Console.WriteLine("max_one=" + r.Next(1));          // always 0
            Console.WriteLine("max_zero=" + r.Next(0));         // net48: Next(0) == 0
            Console.WriteLine("minmax=" + r.Next(5, 10));
            Console.WriteLine("dbl_fmt=" + r.NextDouble().ToString("F6"));
            Console.WriteLine("dbl_range=" + (r.NextDouble() >= 0.0 && r.NextDouble() < 1.0));

            // ===== parameterless ctor: range-only sanity (non-deterministic) =====
            var rdef = new Random();
            Console.WriteLine("def_inrange=" + (rdef.Next() >= 0 && rdef.Next(100) >= 0));
            Console.WriteLine("def_dbl_range=" + (rdef.NextDouble() >= 0.0 && rdef.NextDouble() < 1.0));

            // ===== fixed-seed sequence consistency =====
            var ra = new Random(42);
            var rb = new Random(42);
            bool same = true;
            for (int i = 0; i < 10; i++)
            {
                if (ra.Next() != rb.Next()) same = false;
            }
            Console.WriteLine("seed42_consistent=" + same);

            var rc = new Random(7);
            var rd = new Random(7);
            bool same7 = true;
            for (int i = 0; i < 10; i++)
            {
                if (rc.Next(1000) != rd.Next(1000)) same7 = false;
            }
            Console.WriteLine("seed7_consistent=" + same7);

            // ===== NextBytes: fill / zero-length (no-op, no throw) / null =====
            var rn = new Random(99);
            byte[] buf = new byte[8];
            rn.NextBytes(buf);
            Console.WriteLine("nb_fill=" + (buf[0] != 0 || buf[7] != 0));
            byte[] zbuf = new byte[0];
            bool zeroThrew = false;
            try { rn.NextBytes(zbuf); } catch (Exception) { zeroThrew = true; }
            Console.WriteLine("nb_zero_nothrow=" + (!zeroThrew));
            bool threw = false;
            try { rn.NextBytes(null); } catch (ArgumentNullException) { threw = true; }
            Console.WriteLine("nb_null=" + threw);

            // ===== distribution range boundaries =====
            var rrange = new Random(3);
            bool inMax = true;
            for (int i = 0; i < 500; i++)
            {
                int v = rrange.Next(5);
                if (v < 0 || v >= 5) inMax = false;
            }
            Console.WriteLine("dist_max=" + inMax);

            bool inMinMax = true;
            for (int i = 0; i < 500; i++)
            {
                int v = rrange.Next(2, 7);
                if (v < 2 || v >= 7) inMinMax = false;
            }
            Console.WriteLine("dist_minmax=" + inMinMax);

            bool inFull = true;
            for (int i = 0; i < 200; i++)
            {
                int v = rrange.Next(int.MinValue, int.MaxValue);   // full int32 span
                if (v < int.MinValue || v >= int.MaxValue) inFull = false;
            }
            Console.WriteLine("dist_full=" + inFull);

            bool inDouble = true;
            for (int i = 0; i < 500; i++)
            {
                double d = rrange.NextDouble();
                if (!(d >= 0.0 && d < 1.0)) inDouble = false;
            }
            Console.WriteLine("dist_double=" + inDouble);

            // ===== argument validation =====
            bool aoore1 = false, aoore2 = false, aoore3 = false;
            try { rrange.Next(-1); } catch (ArgumentOutOfRangeException) { aoore1 = true; }
            Console.WriteLine("arg_negmax=" + aoore1);
            try { rrange.Next(10, 2); } catch (ArgumentOutOfRangeException) { aoore2 = true; }
            Console.WriteLine("arg_minmax=" + aoore2);
            try { rrange.Next(int.MaxValue, int.MinValue); } catch (ArgumentOutOfRangeException) { aoore3 = true; }
            Console.WriteLine("arg_wide=" + aoore3);
        }
    }
}
