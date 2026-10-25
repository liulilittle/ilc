using System;
using System.Globalization;
using System.Linq;

//------------------------------------------------------------------------------
// p0grp companion -- the per-bucket operators over a DECIMAL-source bucket.
//
// `decimal[].GroupBy(x => (int)x)` buckets 16-byte System.Decimal VALUE cells.
// Before this batch the per-bucket operator family was int32-kernel only, so
// reading a 16-byte decimal cell through it would silently reinterpret every
// decimal as an int32 (a wrong sum/min/max/contains). This batch routes the
// decimal aggregate family (Sum/Min/Max/Average) and Contains to the decimal
// kernels; the result rides the decimal T3 result-slot ABI.
//
// Count() / Any() / get_Key / ToArray() / foreach stay family-agnostic and are
// covered by tests/cases/p0grp.
//
// Every value is deterministic: the sum/min/max go through
// CultureInfo.InvariantCulture text.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP0GrpDecOp
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] ds = { 3.7m, 1.2m, 3.1m, 1.9m, 1.0m };

            foreach (var g in ds.GroupBy(x => (int)x))
            {
                if (g.Key == 3)
                {
                    // decimal aggregate family over the bucket.
                    decimal sum = g.Sum();
                    Console.WriteLine("[decop] sum=" + D(sum));
                    decimal min = g.Min();
                    Console.WriteLine("[decop] min=" + D(min));
                    decimal max = g.Max();
                    Console.WriteLine("[decop] max=" + D(max));
                    decimal avg = g.Average();
                    Console.WriteLine("[decop] avg=" + D(avg));
                    bool has = g.Contains(3.1m);
                    Console.WriteLine("[decop] contains3.1=" + has);
                    bool hasNot = g.Contains(9.9m);
                    Console.WriteLine("[decop] contains9.9=" + hasNot);
                }
            }

            // A single-bucket source: the whole bucket is one group.
            decimal[] one = { 1.5m, 2.5m, 3.5m };
            foreach (var g in one.GroupBy(x => 7))
            {
                decimal sum = g.Sum();
                Console.WriteLine("[one] sum=" + D(sum));
            }
        }
    }
}
