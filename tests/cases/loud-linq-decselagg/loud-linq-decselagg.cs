//------------------------------------------------------------------------------
// loud-linq-decselagg.cs -- e2e reference case for the ARRAY-source selector
// aggregates with a DECIMAL projection (net48
// Sum/Min/Max/Average<TSource>(source, Func<TSource,decimal>)).
//
// The 16-byte decimal result rides an explicit result slot (linq_map.h):
//   Sum<int,decimal>    -> il2cpp_linq_sum_sel_dec
//   Min<int,decimal>    -> il2cpp_linq_min_sel_dec
//   Max<int,decimal>    -> il2cpp_linq_max_sel_dec
//   Average<int,decimal>-> il2cpp_linq_average_sel_dec
// Before this change the decimal projection LOUD-rejected ("a decimal projection
// has no runtime aggregate kernel").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecSelAgg
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            int[] a = { 1, 2, 3 };
            Console.WriteLine("sum=" + D(a.Sum(x => (decimal)x)));
            Console.WriteLine("min=" + D(a.Min(x => (decimal)x)));
            Console.WriteLine("max=" + D(a.Max(x => (decimal)x)));
            Console.WriteLine("avg=" + D(a.Average(x => (decimal)x)));

            // an empty source: Sum is 0, Min/Max/Average throw
            int[] e = new int[0];
            Console.WriteLine("empty_sum=" + D(e.Sum(x => (decimal)x)));

            Console.WriteLine("done");
        }
    }
}
