using System;
using System.Linq;

//------------------------------------------------------------------------------
// zz-b4-round5 -- round-5 P0 regressions:
//   D1  FindIndex 3-arg (array, startIndex, Predicate)
//   D9  non-generic IndexOf over reference arrays
//   D10 non-generic IndexOf/LastIndexOf/BinarySearch over long[]
//   D11 mismatched boxed comparand -> InvalidOperationException
//   D12 LastIndexOf(empty, v, 0) / (empty, v, 0, 0) -> -1
//   D16 generic LastIndexOf(float/object) 2-arg scans from the END
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ZZB4Round5
    {
        static string P(Func<int> f)
        {
            try { return f().ToString(); }
            catch (Exception e) { return e.GetType().Name; }
        }

        public static void Main()
        {
            int[] a = { 1, 2, 3, 1 };
            Console.WriteLine("[find_3]=" + P(() => Array.FindIndex(a, 1, p => p == 2)));
            Console.WriteLine("[find_2]=" + P(() => Array.FindIndex(a, p => p == 3)));
            Console.WriteLine("[find_4]=" + P(() => Array.FindIndex(a, 0, 4, p => p == 1)));

            float[] fa = { 1f, 2f, 1f };
            Console.WriteLine("[lio_f2]=" + P(() => Array.LastIndexOf(fa, 1f)));

            object[] oa = { "a", "b", "a" };
            Console.WriteLine("[lio_o2]=" + P(() => Array.LastIndexOf(oa, "a")));
            Console.WriteLine("[io_o2]=" + P(() => Array.IndexOf(oa, "b")));

            Array empty = new int[0];
            Console.WriteLine("[lio_empty_start]=" + P(() => Array.LastIndexOf(empty, 5, 0)));
            Console.WriteLine("[lio_empty_range]=" + P(() => Array.LastIndexOf(empty, 5, 0, 0)));

            long[] la = { 1L << 32, 2L << 32, 3L << 32 };
            Array larr = la;
            Console.WriteLine("[ng_io_long]=" + P(() => Array.IndexOf(larr, 3L << 32)));
            Console.WriteLine("[ng_lio_long]=" + P(() => Array.LastIndexOf(larr, 1L << 32)));
            Console.WriteLine("[ng_bs_long]=" + P(() => Array.BinarySearch(larr, 3L << 32)));

            double[] da = { 1.5, 2.5, 3.5 };
            Array darr = da;
            Console.WriteLine("[ng_bs_double]=" + P(() => Array.BinarySearch(darr, 2.5)));

            Array ia = new int[] { 0, 1, 2 };
            Console.WriteLine("[io_mismatch]=" + P(() => Array.IndexOf(ia, 1.0f)));
            Console.WriteLine("[bs_mismatch]=" + P(() => Array.BinarySearch(ia, "x")));

            // nb9 D10 element-kind matrix (primitive arrays must be told apart
            // by the recorded kind, not by the boxed value).
            Array farr = new float[] { 2f, 1f };
            Console.WriteLine("[ng_io_float]=" + P(() => Array.IndexOf(farr, 1f)));
            Console.WriteLine("[ng_lio_float]=" + P(() => Array.LastIndexOf(farr, 1f)));
            Array fbs = new float[] { 1f, 2f, 3f };
            Console.WriteLine("[ng_bs_float]=" + P(() => Array.BinarySearch(fbs, 2f)));
            Console.WriteLine("[ng_bs_float_int]=" + P(() => Array.BinarySearch(fbs, 1)));
            Console.WriteLine("[ng_bs_int_float]=" + P(() => Array.BinarySearch(ia, 1.0f)));
            Array larr2 = new long[] { 1L << 32 };
            Console.WriteLine("[ng_io_long_double]=" + P(() => Array.IndexOf(larr2, 1.0)));
            Console.WriteLine("[ng_bs_long_double]=" + P(() => Array.BinarySearch(larr2, 1.0)));
            Array darr2 = new double[] { 1.5 };
            Console.WriteLine("[ng_bs_double_long]=" + P(() => Array.BinarySearch(darr2, 1L)));
        }
    }
}
