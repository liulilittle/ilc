// audit-fix3-d2c -- List<decimal> ELEMENT with a NON-decimal accumulator, with
// the list arriving as a METHOD PARAMETER (the shape probe-aggrs uses for its
// decimal diagonal), so the receiver hint is the declared List<decimal> type.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2c
    {
        private static int I4_3(List<decimal> l) { return l.Aggregate(0, (a, x) => a + (int)(x * 4m)); }
        private static long I8_3(List<decimal> l) { return l.Aggregate(0L, (a, x) => a + (long)(x * 4m)); }
        private static double D_3(List<decimal> l) { return l.Aggregate(0.0, (a, x) => a + (double)x); }
        private static string Ref_3(List<decimal> l)
        {
            return l.Aggregate("", (a, x) => a + "|" + x.ToString(CultureInfo.InvariantCulture));
        }
        private static int I4_4(List<decimal> l) { return l.Aggregate(0, (a, x) => a + (int)(x * 4m), a => a * 2); }
        private static string Ref_4(List<decimal> l)
        {
            return l.Aggregate("", (a, x) => a + "|" + x.ToString(CultureInfo.InvariantCulture),
                a => "[" + a + "]");
        }
        private static decimal Dec_3(List<decimal> l) { return l.Aggregate(0m, (a, x) => a + x); }

        public static void Main()
        {
            List<decimal> l = new decimal[] { 1.5m, 2.25m, 3.125m }.ToList();
            Console.WriteLine("dec_i4_3=" + I4_3(l).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dec_i8_3=" + I8_3(l).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dec_d_3=" + D_3(l).ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("dec_ref_3=" + Ref_3(l));
            Console.WriteLine("dec_i4_4=" + I4_4(l).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dec_ref_4=" + Ref_4(l));
            Console.WriteLine("dec_dec_3=" + Dec_3(l).ToString(CultureInfo.InvariantCulture));
        }
    }
}
