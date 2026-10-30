// audit-fix3-d2a -- RED-LINE probe: List<decimal> ELEMENT with a NON-decimal
// accumulator (the D2 residual the fix documented as "still gated by the
// pre-existing ARRAY decimal-source rule"). Both the 3-parameter seed fold and
// the 4-parameter (seed, func, resultSelector) form are exercised.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2a
    {
        public static void Main()
        {
            List<decimal> l = new decimal[] { 1.5m, 2.25m, 3.125m }.ToList();

            int a3 = l.Aggregate(0, (a, x) => a + (int)(x * 4m));
            Console.WriteLine("dec_i4_3=" + a3.ToString(CultureInfo.InvariantCulture));

            long b3 = l.Aggregate(0L, (a, x) => a + (long)(x * 4m));
            Console.WriteLine("dec_i8_3=" + b3.ToString(CultureInfo.InvariantCulture));

            double c3 = l.Aggregate(0.0, (a, x) => a + (double)x);
            Console.WriteLine("dec_d_3=" + c3.ToString("R", CultureInfo.InvariantCulture));

            string d3 = l.Aggregate("", (a, x) => a + "|" + x.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dec_ref_3=" + d3);

            int a4 = l.Aggregate(0, (a, x) => a + (int)(x * 4m), a => a * 2);
            Console.WriteLine("dec_i4_4=" + a4.ToString(CultureInfo.InvariantCulture));

            long b4 = l.Aggregate(0L, (a, x) => a + (long)(x * 4m), a => a + 1L);
            Console.WriteLine("dec_i8_4=" + b4.ToString(CultureInfo.InvariantCulture));

            string d4 = l.Aggregate("", (a, x) => a + "|" + x.ToString(CultureInfo.InvariantCulture),
                a => "[" + a + "]");
            Console.WriteLine("dec_ref_4=" + d4);
        }
    }
}
