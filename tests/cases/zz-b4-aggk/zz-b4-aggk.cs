using System;
using System.Linq;

//------------------------------------------------------------------------------
// zz-b4-aggk -- nb9 H-F3: Aggregate(seed, func, resultSelector) must carry the
// TResult family (long/int64, float, double, reference), not truncate to int32.
// Also covers the accumulator families (int32/long/double) and the
// string-element Aggregate(seed, func) source read (H-B6).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ZZB4Aggk
    {
        public static void Main()
        {
            int[] i = { 1, 2, 3 };
            Console.WriteLine("[i4acc_i8res]=" + i.Aggregate(0, (a, b) => a + b, a => (long)a * 2000000000L));
            long[] l = { 1L, 2L, 3L };
            Console.WriteLine("[i8acc_i8res]=" + l.Aggregate(0L, (a, b) => a + b, a => a * 2000000000L));
            double[] d = { 1.5, 2.5 };
            Console.WriteLine("[dacc_i8res]=" + d.Aggregate(0.0, (a, b) => a + b, a => (long)(a * 1000000000.0)));
            int[] f = { 1, 2, 3 };
            Console.WriteLine("[i4acc_f_res]=" + f.Aggregate(0, (a, b) => a + b, a => (float)a * 1.5f));
            Console.WriteLine("[i4acc_d_res]=" + f.Aggregate(0, (a, b) => a + b, a => a * 1.5));

            string[] s = { "a", "b" };
            Console.WriteLine("[refacc_ref]=" + s.Aggregate("z", (acc, x) => acc + x));
            Console.WriteLine("[refacc_ref_res]=" + s.Aggregate("z", (acc, x) => acc + x, acc => acc + "!"));
        }
    }
}
