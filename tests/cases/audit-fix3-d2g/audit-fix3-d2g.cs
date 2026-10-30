// audit-fix3-d2g -- the ARRAY counterpart of audit-fix3-d2a. A decimal[]
// source with a NON-decimal accumulator:
//   * count == 3 goes through the ARRAY CELL MATRIX (seed_cell_<acc>), which
//     reads the 16-byte decimal cell at its REAL width and calls the
//     accumulator's own leaf ABI -- the impl-loudvalstruct counterpart of the
//     List<decimal> fix (the old gate rejected both shapes).
//   * count == 4 (seed, func, resultSelector) has NO array kernel, so it must
//     stay a transpile-time LOUD (never a silent 4-byte read of a 16-byte cell).
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2g
    {
        public static void Main()
        {
            decimal[] a = new decimal[] { 1.5m, 2.25m, 3.125m };

            int i4 = a.Aggregate(0, (acc, x) => acc + (int)(x * 4m));
            Console.WriteLine("arr_dec_i4=" + i4.ToString(CultureInfo.InvariantCulture));

            long i8 = a.Aggregate(0L, (acc, x) => acc + (long)(x * 4m));
            Console.WriteLine("arr_dec_i8=" + i8.ToString(CultureInfo.InvariantCulture));

            double d = a.Aggregate(0.0, (acc, x) => acc + (double)x);
            Console.WriteLine("arr_dec_d=" + d.ToString("R", CultureInfo.InvariantCulture));

            string r = a.Aggregate("", (acc, x) => acc + "|" + x.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("arr_dec_ref=" + r);
        }
    }
}
