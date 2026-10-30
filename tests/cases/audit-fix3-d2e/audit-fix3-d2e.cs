// audit-fix3-d2e -- which List<decimal> RECEIVER SHAPES reach the List arm for
// a NON-decimal accumulator? (a) local new List<decimal>() + Add, (b) inline
// ToList in the call, (c) ToList into a local, (d) method parameter.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2e
    {
        private static int Param(List<decimal> l) { return l.Aggregate(0, (a, x) => a + (int)(x * 4m)); }

        public static void Main()
        {
            List<decimal> local = new List<decimal>();
            local.Add(1.5m);
            local.Add(2.25m);
            Console.WriteLine("a_local=" + local.Aggregate(0, (a, x) => a + (int)(x * 4m))
                .ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("b_inline=" + new decimal[] { 1.5m, 2.25m }.ToList()
                .Aggregate(0, (a, x) => a + (int)(x * 4m)).ToString(CultureInfo.InvariantCulture));

            List<decimal> conv = new decimal[] { 1.5m, 2.25m }.ToList();
            Console.WriteLine("c_conv=" + conv.Aggregate(0, (a, x) => a + (int)(x * 4m))
                .ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("d_param=" + Param(conv).ToString(CultureInfo.InvariantCulture));
        }
    }
}
