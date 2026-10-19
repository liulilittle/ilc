using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-p0 -- nb9 P0 acceptance probe.
//
// Every expected value below is the AUTHORITATIVE net48 measurement recorded in
// .opencode/tmp/b9-fix/ORACLE-P0.md (host .NET Framework 4.x, Add-Type).
//
// Covered:
//   * float Aggregate (seed / no seed) -- float in / float out ABI
//   * float selector Sum / Average
//   * Comparer<int/double/long>.Create(...).Compare
//   * Select((x,i) => long/double/string) through ToArray / Count / foreach
//   * GroupBy TElement = int64 / double / reference / int32 / byte
//   * foreach over Where, and Where(...).ToArray()
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixP0
    {
        public static void Main()
        {
            var sb = new StringBuilder();
            int[] data = { 1, 2, 3, 4 };

            // --- float Aggregate: float in / float out ---
            float acc = data.Aggregate(0.5f, (a, b) => a + b);
            Console.WriteLine("[agg_float_seed0_5]=" + ((double)acc).ToString("R"));
            float acc2 = data.Select(x => (float)x).Aggregate((a, b) => a + b);
            Console.WriteLine("[agg_float_noseed]=" + ((double)acc2).ToString("R"));

            // --- float selector Sum / Average ---
            float sf = data.Sum(x => (float)x);
            Console.WriteLine("[sum_float_sel]=" + ((double)sf).ToString("R"));
            float af = data.Average(x => (float)x);
            Console.WriteLine("[avg_float_sel]=" + ((double)af).ToString("R"));

            // --- Comparer<T>.Create must run the WRAPPED delegate ---
            var ci = Comparer<int>.Create((a, b) => b.CompareTo(a));
            Console.WriteLine("[cmp_create_int_1_2]=" + ci.Compare(1, 2));
            Console.WriteLine("[cmp_create_int_2_1]=" + ci.Compare(2, 1));
            Console.WriteLine("[cmp_create_int_2_2]=" + ci.Compare(2, 2));

            var cd = Comparer<double>.Create((a, b) => b > a ? 1 : (b < a ? -1 : 0));
            Console.WriteLine("[cmp_create_dbl_1_2]=" + cd.Compare(1.0, 2.0));
            Console.WriteLine("[cmp_create_dbl_2_1]=" + cd.Compare(2.0, 1.0));

            var cl = Comparer<long>.Create((a, b) => a > b ? 1 : (a < b ? -1 : 0));
            Console.WriteLine("[cmp_create_long_5_3]=" + cl.Compare(5L, 3L));

            // --- Select with index: long / double / string through ToArray ---
            int[] r5 = { 0, 1, 2, 3, 4 };
            int[] r4 = { 1, 2, 3, 4 };
            int[] r3 = { 0, 1, 2 };
            int[] r7 = { 0, 1, 2, 3, 4, 5, 6 };
            long[] la = r5.Select((x, i) => (long)(x * 100 + i)).ToArray();
            Console.WriteLine("[sel_idx_long]=" + string.Join(",", la) + " sum=" + la.Sum() + " count=" + la.Length);

            double[] da = r4.Select((x, i) => x * 1.5 + i).ToArray();
            var db = new StringBuilder();
            for (int i = 0; i < da.Length; i++)
            {
                if (i > 0) db.Append(",");
                db.Append(da[i].ToString("R"));
            }
            Console.WriteLine("[sel_idx_dbl]=" + db.ToString() + " sum=" + da.Sum().ToString("R"));

            string[] sa = r3.Select((x, i) => "v" + x + "_" + i).ToArray();
            Console.WriteLine("[sel_idx_str]=" + string.Join("|", sa));

            int cnt = r7.Select((x, i) => (long)i).Count();
            Console.WriteLine("[sel_idx_long_count]=" + cnt);

            // --- Select through foreach (element families) ---
            long fsum = 0;
            foreach (long v in r5.Select((x, i) => (long)(x * 100 + i))) fsum += v;
            Console.WriteLine("[foreach_sel_long]=" + fsum);

            double dsum = 0;
            foreach (double v in r4.Select((x, i) => x * 1.5 + i)) dsum += v;
            Console.WriteLine("[foreach_sel_dbl]=" + dsum.ToString("R"));

            var ssb = new StringBuilder();
            foreach (string v in r3.Select((x, i) => "v" + x + "_" + i)) ssb.Append(v + "|");
            Console.WriteLine("[foreach_sel_str]=" + ssb.ToString());

            // --- GroupBy element families (first-occurrence key order) ---
            var g1 = data.GroupBy(x => x % 3, x => (long)x * 10);
            foreach (var g in g1) Console.WriteLine("[gb_i8_" + g.Key + "]=" + JoinI8(g));

            var g2 = data.GroupBy(x => x % 2, x => (double)x / 2.0);
            foreach (var g in g2) Console.WriteLine("[gb_d_" + g.Key + "]=" + JoinD(g));

            var g3 = data.GroupBy(x => x % 2, x => "s" + x);
            foreach (var g in g3) Console.WriteLine("[gb_ref_" + g.Key + "]=" + JoinS(g));

            var g4 = data.GroupBy(x => x % 2, x => x * 2);
            foreach (var g in g4) Console.WriteLine("[gb_i4_" + g.Key + "]=" + JoinI4(g));

            var g5 = data.GroupBy(x => x % 2, x => (byte)x);
            foreach (var g in g5) Console.WriteLine("[gb_u1_" + g.Key + "]=" + JoinU1(g));

            // --- Where through foreach / ToArray ---
            int total = 0;
            foreach (var v in data.Where(x => x % 2 == 0)) total += v;
            Console.WriteLine("[foreach_where]=" + total);

            int[] wa = data.Where(x => x % 2 == 0).ToArray();
            Console.WriteLine("[where_toarray]=" + string.Join(",", wa));


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

        static string JoinD(IEnumerable<double> items)
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

        static string JoinS(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (string v in items)
            {
                if (!first) sb.Append(",");
                sb.Append(v);
                first = false;
            }
            return sb.ToString();
        }

        static string JoinI4(IEnumerable<int> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (int v in items)
            {
                if (!first) sb.Append(",");
                sb.Append(v);
                first = false;
            }
            return sb.ToString();
        }

        static string JoinU1(IEnumerable<byte> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (byte v in items)
            {
                if (!first) sb.Append(",");
                sb.Append((int)v);
                first = false;
            }
            return sb.ToString();
        }
    }
}
