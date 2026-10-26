using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Cases
{
    public static class LoudLinqListAgg
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            List<int> l = new List<int> { 1, 2, 3 };
            Console.WriteLine("sum=" + l.Sum());
            Console.WriteLine("min=" + l.Min());
            Console.WriteLine("max=" + l.Max());
            Console.WriteLine("avg=" + l.Average().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("sumsel_i8=" + l.Sum(x => (long)x));
            Console.WriteLine("maxsel_d=" + l.Max(x => (double)x).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("sumsel_i4=" + l.Sum(x => x + 1));

            List<long> ll = new List<long> { 1, 2, 3 };
            Console.WriteLine("lsum=" + ll.Sum());
            Console.WriteLine("lsumsel_i8=" + ll.Sum(x => x));
            Console.WriteLine("lsumsel_i4=" + ll.Sum(x => (int)x));

            List<double> dd = new List<double> { 1.5, 2.5 };
            Console.WriteLine("dsum=" + dd.Sum().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dsumsel_i4=" + dd.Sum(x => (int)x));
            Console.WriteLine("davgsel_d=" + dd.Average(x => x).ToString(CultureInfo.InvariantCulture));

            // decimal PROJECTION (net48 Sum/Min/Max/Average(source, selector))
            List<int> dl = new List<int> { 1, 2, 3 };
            Console.WriteLine("dec_sumsel=" + D(dl.Sum(x => (decimal)x)));
            Console.WriteLine("dec_minsel=" + D(dl.Min(x => (decimal)x)));
            Console.WriteLine("dec_maxsel=" + D(dl.Max(x => (decimal)x)));
            Console.WriteLine("dec_avgsel=" + D(dl.Average(x => (decimal)x)));

            Console.WriteLine("done");
        }
    }
}
