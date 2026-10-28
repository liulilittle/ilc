//------------------------------------------------------------------------------
// thenbyfamprobe -- OrderBy(k1).ThenBy(k2) / ThenByDescending(k2) with MIXED
// key families (int32 / int64 / double primary x int32 / int64 / double
// secondary).
//
// impl-thenbyfam: the fused composite entry used to encode ONLY the element
// family and the PRIMARY key family (the ThenBy marker dropped the secondary
// family), so OrderBy(int).ThenBy(double) picked the int32-key kernel and read
// the double secondary key with the int32 reader (eax residue) and
// OrderBy(double).ThenBy(int) read the int32 secondary key with the
// double/xmm0 reader -- SILENT mis-order (oracle != runtime, no loud
// rejection). Each (primary, secondary) family pair below must produce the
// exact net48 order.
//
// Elements are four-digit markers value = k1*1000 + k2*10 + rank so that
//   * duplicate PRIMARY keys force the secondary key to decide,
//   * duplicate (k1, k2) tuples expose stability (rank must stay ascending),
//   * asc/desc on either key is observable.
//
// Covered: int32[] and int64[] sources x 9 (primary, secondary) family pairs
// x 4 direction combinations (asc/desc primary, asc/desc secondary).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ThenByFamProbe
    {
        // int32[] source selectors (int32 / int64 / double families).
        // The int64 keys shift the digit into the HIGH word (a genuinely
        // 64-bit value: a 32-bit read of the low word sees 0) and the double
        // keys are FRACTIONAL (a real double computation, not an integer
        // widened by cvtsi2sd, whose eax residue happens to survive).
        private static int K1I4(int x) { return x / 1000; }
        private static int K2I4(int x) { return (x / 10) % 10; }
        private static long K1I8(int x) { return ((long)(x / 1000)) << 32; }
        private static long K2I8(int x) { return ((long)((x / 10) % 10)) << 32; }
        private static double K1D(int x) { return (x / 1000) * 1.5 + 0.25; }
        private static double K2D(int x) { return (((x / 10) % 10) * 1.5) + 0.5; }

        // int64[] source selectors (int32 / int64 / double families).
        private static int K1L4(long x) { return (int)(x / 1000L); }
        private static int K2L4(long x) { return (int)((x / 10L) % 10L); }
        private static long K1L8(long x) { return (x / 1000L) << 32; }
        private static long K2L8(long x) { return ((x / 10L) % 10L) << 32; }
        private static double K1LD(long x) { return (x / 1000L) * 1.5 + 0.25; }
        private static double K2LD(long x) { return (((x / 10L) % 10L) * 1.5) + 0.5; }

        // Exactly-once contract probes: each selector must be invoked ONE time
        // per element in the fused composite (mixed families included).
        private static int s_calls;
        private static int CountK1(int x) { ++s_calls; return x / 1000; }
        private static double CountK2(int x) { ++s_calls; return (((x / 10) % 10) * 1.5) + 0.5; }

        private static string JoinInt(int[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        private static string JoinLong(long[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        private static void All4Int(string tag, int[] src,
            Func<int, int> p4, Func<int, long> p8, Func<int, double> pd,
            Func<int, int> s4, Func<int, long> s8, Func<int, double> sd,
            string pf, string sf)
        {
            switch (pf)
            {
                case "i4":
                    switch (sf)
                    {
                        case "i4":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(p4).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(p4).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(p4).ThenByDescending(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(p4).ThenByDescending(s4).ToArray()));
                            break;
                        case "i8":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(p4).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(p4).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(p4).ThenByDescending(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(p4).ThenByDescending(s8).ToArray()));
                            break;
                        case "d":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(p4).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(p4).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(p4).ThenByDescending(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(p4).ThenByDescending(sd).ToArray()));
                            break;
                    }
                    break;
                case "i8":
                    switch (sf)
                    {
                        case "i4":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(p8).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(p8).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(p8).ThenByDescending(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(p8).ThenByDescending(s4).ToArray()));
                            break;
                        case "i8":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(p8).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(p8).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(p8).ThenByDescending(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(p8).ThenByDescending(s8).ToArray()));
                            break;
                        case "d":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(p8).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(p8).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(p8).ThenByDescending(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(p8).ThenByDescending(sd).ToArray()));
                            break;
                    }
                    break;
                case "d":
                    switch (sf)
                    {
                        case "i4":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(pd).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(pd).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(pd).ThenByDescending(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(pd).ThenByDescending(s4).ToArray()));
                            break;
                        case "i8":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(pd).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(pd).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(pd).ThenByDescending(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(pd).ThenByDescending(s8).ToArray()));
                            break;
                        case "d":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinInt(src.OrderBy(pd).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinInt(src.OrderByDescending(pd).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinInt(src.OrderBy(pd).ThenByDescending(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinInt(src.OrderByDescending(pd).ThenByDescending(sd).ToArray()));
                            break;
                    }
                    break;
            }
        }

        private static void All4Long(string tag, long[] src,
            Func<long, int> p4, Func<long, long> p8, Func<long, double> pd,
            Func<long, int> s4, Func<long, long> s8, Func<long, double> sd,
            string pf, string sf)
        {
            switch (pf)
            {
                case "i4":
                    switch (sf)
                    {
                        case "i4":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(p4).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(p4).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(p4).ThenByDescending(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(p4).ThenByDescending(s4).ToArray()));
                            break;
                        case "i8":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(p4).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(p4).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(p4).ThenByDescending(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(p4).ThenByDescending(s8).ToArray()));
                            break;
                        case "d":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(p4).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(p4).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(p4).ThenByDescending(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(p4).ThenByDescending(sd).ToArray()));
                            break;
                    }
                    break;
                case "i8":
                    switch (sf)
                    {
                        case "i4":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(p8).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(p8).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(p8).ThenByDescending(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(p8).ThenByDescending(s4).ToArray()));
                            break;
                        case "i8":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(p8).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(p8).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(p8).ThenByDescending(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(p8).ThenByDescending(s8).ToArray()));
                            break;
                        case "d":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(p8).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(p8).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(p8).ThenByDescending(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(p8).ThenByDescending(sd).ToArray()));
                            break;
                    }
                    break;
                case "d":
                    switch (sf)
                    {
                        case "i4":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(pd).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(pd).ThenBy(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(pd).ThenByDescending(s4).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(pd).ThenByDescending(s4).ToArray()));
                            break;
                        case "i8":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(pd).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(pd).ThenBy(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(pd).ThenByDescending(s8).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(pd).ThenByDescending(s8).ToArray()));
                            break;
                        case "d":
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_aa]=" + JoinLong(src.OrderBy(pd).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_da]=" + JoinLong(src.OrderByDescending(pd).ThenBy(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_ad]=" + JoinLong(src.OrderBy(pd).ThenByDescending(sd).ToArray()));
                            Console.WriteLine("[" + tag + "_" + pf + "_" + sf + "_dd]=" + JoinLong(src.OrderByDescending(pd).ThenByDescending(sd).ToArray()));
                            break;
                    }
                    break;
            }
        }

        public static void Main()
        {
            // k1 groups: 1 (4), 3 (3), 5 (4); duplicate tuples (5,3) and (1,1)
            // keep their source order (stability).
            int[] src = { 5032, 5012, 3021, 5031, 1011, 1022, 1021, 3011, 5022, 1012, 3031 };
            string[] fams = { "i4", "i8", "d" };
            foreach (string pf in fams)
            {
                foreach (string sf in fams)
                {
                    All4Int("i4src", src, K1I4, K1I8, K1D, K2I4, K2I8, K2D, pf, sf);
                }
            }

            long[] src64 = { 5032L, 5012L, 3021L, 5031L, 1011L, 1022L, 1021L, 3011L, 5022L, 1012L, 3031L };
            foreach (string pf in fams)
            {
                foreach (string sf in fams)
                {
                    All4Long("i8src", src64, K1L4, K1L8, K1LD, K2L4, K2L8, K2LD, pf, sf);
                }
            }

            // Mixed-family fused composite: each selector is invoked exactly
            // once per element (n = 11 -> 22 calls), the primary direction is
            // fused and the secondary double key orders within each group.
            s_calls = 0;
            int[] counted = src.OrderBy(CountK1).ThenBy(CountK2).ToArray();
            Console.WriteLine("[i4_mixed_once]=" + s_calls + "|" + JoinInt(counted));
        }
    }
}