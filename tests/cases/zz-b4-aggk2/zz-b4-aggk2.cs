using System;
using System.Linq;
namespace Cases
{
    public static class ProbeK2
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
            Console.WriteLine("done");
        }
    }
}
