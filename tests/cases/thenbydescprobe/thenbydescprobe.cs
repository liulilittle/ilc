//------------------------------------------------------------------------------
// thenbydescprobe -- OrderByDescending(k1).ThenBy/ThenByDescending(k2) probes.
//
// impl-thenbydesc: the fused ThenBy composite must carry BOTH directions.
// Before this case the int32/int64-source fold dropped the primary direction:
// OrderByDescending(k1).ThenBy(k2) was rewritten to the (k1 asc, k2 asc)
// kernel, i.e. a SILENT mis-order (oracle != runtime, no loud rejection).
//
// Elements are three-digit markers value = k1*100 + k2*10 + rank so that
//   * duplicate PRIMARY keys exercise the secondary key,
//   * duplicate (k1, k2) tuples exercise stability (rank must stay ascending),
//   * asc vs desc primary and asc vs desc secondary are all distinguishable.
//
// The fused runtime entries assume ONE key family for both selectors (the
// entry suffix fixes it), so each probe keeps k1 and k2 in the SAME family
// and the six supported entries are all exercised with a descending primary:
//   int32[]  source: _i4, _i4_i8, _i4_d
//   int64[]  source: _i8_i4, _i8_i8, _i8_d
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ThenByDescProbe
    {
        // int32[] source keys (int32 / int64 / double families).
        private static int K1I4(int x) { return x / 100; }
        private static int K2I4(int x) { return (x / 10) % 10; }
        private static long K1I8(int x) { return x / 100; }
        private static long K2I8(int x) { return (x / 10) % 10; }
        private static double K1D(int x) { return (double)(x / 100); }
        private static double K2D(int x) { return (double)((x / 10) % 10); }

        // long[] source keys (int32 / int64 / double families).
        private static int K1L32(long x) { return (int)(x / 100L); }
        private static int K2L32(long x) { return (int)((x / 10L) % 10L); }
        private static long K1L64(long x) { return x / 100L; }
        private static long K2L64(long x) { return (x / 10L) % 10L; }
        private static double K1LD(long x) { return (double)(x / 100L); }
        private static double K2LD(long x) { return (double)((x / 10L) % 10L); }

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

        public static void Main()
        {
            // k1 groups: 1 (3 elems), 3 (3), 5 (5); two (1,2) and two (5,3)
            // tuples tie on BOTH keys, so stability is observable.
            int[] src = { 532, 512, 321, 531, 111, 122, 121, 311, 522, 112, 331 };

            // ===== int32[] source, int32 keys (_i4) =====
            Console.WriteLine("[i4_asc_asc]=" + JoinInt(src.OrderBy(K1I4).ThenBy(K2I4).ToArray()));
            Console.WriteLine("[i4_desc_asc]=" + JoinInt(src.OrderByDescending(K1I4).ThenBy(K2I4).ToArray()));
            Console.WriteLine("[i4_asc_desc]=" + JoinInt(src.OrderBy(K1I4).ThenByDescending(K2I4).ToArray()));
            Console.WriteLine("[i4_desc_desc]=" + JoinInt(src.OrderByDescending(K1I4).ThenByDescending(K2I4).ToArray()));
            Console.WriteLine("[i4_desc_only]=" + JoinInt(src.OrderByDescending(K1I4).ToArray()));
            Console.WriteLine("[i4_src]=" + JoinInt(src));

            // ===== int32[] source, int64 keys (_i4_i8) =====
            Console.WriteLine("[i4i8_asc_asc]=" + JoinInt(src.OrderBy(K1I8).ThenBy(K2I8).ToArray()));
            Console.WriteLine("[i4i8_desc_asc]=" + JoinInt(src.OrderByDescending(K1I8).ThenBy(K2I8).ToArray()));
            Console.WriteLine("[i4i8_asc_desc]=" + JoinInt(src.OrderBy(K1I8).ThenByDescending(K2I8).ToArray()));
            Console.WriteLine("[i4i8_desc_desc]=" + JoinInt(src.OrderByDescending(K1I8).ThenByDescending(K2I8).ToArray()));

            // ===== int32[] source, double keys (_i4_d) =====
            Console.WriteLine("[i4d_asc_asc]=" + JoinInt(src.OrderBy(K1D).ThenBy(K2D).ToArray()));
            Console.WriteLine("[i4d_desc_asc]=" + JoinInt(src.OrderByDescending(K1D).ThenBy(K2D).ToArray()));
            Console.WriteLine("[i4d_asc_desc]=" + JoinInt(src.OrderBy(K1D).ThenByDescending(K2D).ToArray()));
            Console.WriteLine("[i4d_desc_desc]=" + JoinInt(src.OrderByDescending(K1D).ThenByDescending(K2D).ToArray()));

            long[] src64 = { 532L, 512L, 321L, 531L, 111L, 122L, 121L, 311L, 522L, 112L, 331L };

            // ===== long[] source, int32 keys (_i8_i4) =====
            Console.WriteLine("[i8_asc_asc]=" + JoinLong(src64.OrderBy(K1L32).ThenBy(K2L32).ToArray()));
            Console.WriteLine("[i8_desc_asc]=" + JoinLong(src64.OrderByDescending(K1L32).ThenBy(K2L32).ToArray()));
            Console.WriteLine("[i8_asc_desc]=" + JoinLong(src64.OrderBy(K1L32).ThenByDescending(K2L32).ToArray()));
            Console.WriteLine("[i8_desc_desc]=" + JoinLong(src64.OrderByDescending(K1L32).ThenByDescending(K2L32).ToArray()));

            // ===== long[] source, int64 keys (_i8_i8) =====
            Console.WriteLine("[i8l_asc_asc]=" + JoinLong(src64.OrderBy(K1L64).ThenBy(K2L64).ToArray()));
            Console.WriteLine("[i8l_desc_asc]=" + JoinLong(src64.OrderByDescending(K1L64).ThenBy(K2L64).ToArray()));
            Console.WriteLine("[i8l_asc_desc]=" + JoinLong(src64.OrderBy(K1L64).ThenByDescending(K2L64).ToArray()));
            Console.WriteLine("[i8l_desc_desc]=" + JoinLong(src64.OrderByDescending(K1L64).ThenByDescending(K2L64).ToArray()));

            // ===== long[] source, double keys (_i8_d) =====
            Console.WriteLine("[i8d_asc_asc]=" + JoinLong(src64.OrderBy(K1LD).ThenBy(K2LD).ToArray()));
            Console.WriteLine("[i8d_desc_asc]=" + JoinLong(src64.OrderByDescending(K1LD).ThenBy(K2LD).ToArray()));
            Console.WriteLine("[i8d_asc_desc]=" + JoinLong(src64.OrderBy(K1LD).ThenByDescending(K2LD).ToArray()));
            Console.WriteLine("[i8d_desc_desc]=" + JoinLong(src64.OrderByDescending(K1LD).ThenByDescending(K2LD).ToArray()));
        }
    }
}