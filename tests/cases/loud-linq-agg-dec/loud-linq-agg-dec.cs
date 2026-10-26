//------------------------------------------------------------------------------
// loud-linq-agg-dec.cs -- e2e reference case for Aggregate with a DECIMAL
// accumulator (net48 Aggregate<TSource,decimal>(source, seed,
// Func<decimal,TSource,decimal>)).
//
// The 16-byte decimal result rides an explicit result slot (linq_map.h):
//   Aggregate<int,decimal> -> il2cpp_linq_aggregate_seed_dec
// Before this change the decimal accumulator LOUD-rejected ("TAccumulate
// 'valuetype' is not a supported accumulator family").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqAggDec
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            int[] a = { 1, 2, 3 };
            Console.WriteLine("sum=" + D(a.Aggregate(0m, (acc, x) => acc + x)));
            Console.WriteLine("mul=" + D(a.Aggregate(1m, (acc, x) => acc * x)));

            // an empty source returns the seed unchanged (net48)
            int[] e = new int[0];
            Console.WriteLine("empty=" + D(e.Aggregate(5m, (acc, x) => acc + x)));

            Console.WriteLine("done");
        }
    }
}
