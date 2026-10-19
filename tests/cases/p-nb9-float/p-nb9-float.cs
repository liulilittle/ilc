using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-float -- nb9 H-F1..H-F4 acceptance probe.
//
//   H-F1  a float kernel result used DIRECTLY as an argument (never stored in
//         a local) must keep its float/double width, not truncate to int32.
//   H-F2  Select(x => (float)...) materializes a REAL float[] (net48), so
//         element-level consumption reads 4-byte elements.
//   H-F3  Aggregate(seed, func, resultSelector) with numeric TResult
//         (long / float / double) returns the real value.
//   H-F4  GroupBy(..., resultSelector) with numeric TResult (long / float /
//         double) uses the real element ABI (not the reference ABI).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9Float
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3, 4 };

            // --- H-F1: direct float kernel results as arguments ---
            Console.WriteLine("[sum_direct]=" + data.Sum(x => (float)x).ToString("R"));
            Console.WriteLine("[avg_direct]=" + data.Average(x => (float)x).ToString("R"));
            Console.WriteLine("[min_direct]=" + data.Min(x => (float)x).ToString("R"));
            Console.WriteLine("[max_direct]=" + data.Max(x => (float)x).ToString("R"));

            // --- H-F2: real float[] element consumption ---
            float[] fa = data.Select(x => (float)(x * 1.5)).ToArray();
            var sb = new StringBuilder();
            for (int i = 0; i < fa.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(fa[i].ToString("R"));
            }
            Console.WriteLine("[sel_f_arr]=" + sb.ToString() + " len=" + fa.Length);

            float fs = 0;
            foreach (float v in fa) fs += v;
            Console.WriteLine("[sel_f_foreach]=" + fs.ToString("R"));

            float agg = data.Select(x => (float)x).Aggregate((a, b) => a + b);
            Console.WriteLine("[agg_f_f]=" + agg.ToString("R"));
            float aggs = data.Select(x => (float)x).Aggregate(0.5f, (a, b) => a + b);
            Console.WriteLine("[agg_f_f_seed]=" + aggs.ToString("R"));

            // --- H-F3: numeric TResult result selectors ---
            long rl = data.Aggregate(0, (acc, b) => acc + b, acc => (long)acc * 100);
            Console.WriteLine("[rs_long]=" + rl);
            float rf = data.Aggregate(0, (acc, b) => acc + b, acc => (float)acc / 2);
            Console.WriteLine("[rs_float]=" + ((double)rf).ToString("R"));
            double rd = data.Aggregate(0, (acc, b) => acc + b, acc => (double)acc / 4);
            Console.WriteLine("[rs_double]=" + rd.ToString("R"));

            // --- H-F4: GroupBy numeric TResult result selectors ---
            var gl = data.GroupBy(x => x % 2, (k, g) => (long)k * 1000 + g.Sum());
            Console.WriteLine("[gb_rs_long]=" + JoinI8(gl));

            var gf = data.GroupBy(x => x % 2, (k, g) => (float)g.Sum() / 2);
            Console.WriteLine("[gb_rs_float]=" + JoinFl(gf));

            var gd = data.GroupBy(x => x % 2, (k, g) => (double)g.Sum() / 4);
            Console.WriteLine("[gb_rs_double]=" + JoinDb(gd));
        }

        static string JoinI8(IEnumerable<long> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (long v in items)
            {
                if (!first) sb.Append(",");
                sb.Append(v);
                first = false;
            }
            return sb.ToString();
        }

        static string JoinFl(IEnumerable<float> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (float v in items)
            {
                if (!first) sb.Append(",");
                sb.Append(v.ToString("R"));
                first = false;
            }
            return sb.ToString();
        }

        static string JoinDb(IEnumerable<double> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (double v in items)
            {
                if (!first) sb.Append(",");
                sb.Append(v.ToString("R"));
                first = false;
            }
            return sb.ToString();
        }
    }
}
