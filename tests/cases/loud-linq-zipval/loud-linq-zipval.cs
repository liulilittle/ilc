//------------------------------------------------------------------------------
// loud-linq-zipval.cs -- e2e reference case for Enumerable.Zip with a 0x11
// VALUE-TYPE result (net48 Zip<TSource,TSource2,TResult> with TResult =
// System.Decimal / System.DateTime / System.Guid).
//
// The byte alone cannot tell the four value types apart, so the transpiler
// resolves the generic-argument NAME to a result cell ABI:
//   System.Decimal           -> resKind 13 (16-byte cell)
//   System.DateTime/.TimeSpan-> resKind 14 (8-byte cell)
//   System.Guid              -> resKind 15 (16-byte cell)
// The selector returns the struct BY VALUE; the kernels copy it into the cell.
// Before this change the shape LOUD-rejected ("no result cell ABI for this
// TResult").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqZipVal
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            int[] a = { 1, 2, 3 };
            int[] b = { 10, 20, 30 };

            var zd = a.Zip(b, (x, y) => (decimal)(x + y)).ToArray();
            Console.WriteLine("dec=" + D(zd[0]) + "," + D(zd[1]) + "," + D(zd[2]));

            var zt = a.Zip(b, (x, y) => new DateTime(2020, 1, 1).AddDays(x + y)).ToArray();
            Console.WriteLine("dt=" + zt[0].ToString("yyyy-MM-dd") + "," + zt[1].ToString("yyyy-MM-dd"));

            var zg = a.Zip(b, (x, y) => Guid.Parse((x + y).ToString("00000000") + "-0000-0000-0000-000000000000")).ToArray();
            Console.WriteLine("guid=" + zg[0].ToString("N").Substring(0, 8) + "," + zg[1].ToString("N").Substring(0, 8));

            Console.WriteLine("done");
        }
    }
}
