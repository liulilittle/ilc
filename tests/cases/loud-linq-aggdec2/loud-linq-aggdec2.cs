//------------------------------------------------------------------------------
// loud-linq-aggdec2.cs -- e2e reference case for the DECIMAL-SOURCE Aggregate
// family (net48 Enumerable.Aggregate over a System.Decimal source):
//   * the no-seed fold  Aggregate<decimal>(source, Func<dec,dec,dec>)
//     [Reference Source System.Core/System/Linq/Enumerable.cs:1379]
//   * the seed fold     Aggregate<decimal,decimal>(source, seed, func) [:1391]
//   * the seed + result-selector form Aggregate<TSource,TAccumulate,TResult>(
//     source, seed, func, resultSelector) [:1399] -- the selector runs ONCE on
//     the final decimal accumulator (int32 source AND decimal source).
//
// Before this change the no-seed form was a phantom "no BCL mapping", the
// seed fold read the 16-byte decimal cells through the int32 reader (a silent
// wrong value) and the result-selector form dropped the selector (a stack
// desync). Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqAggDec2
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 2m, 3m, 4m };

            // no-seed fold over the decimal source: 2 * 3 * 4 = 24
            Console.WriteLine("noseed=" + D(a.Aggregate((acc, x) => acc * x)));

            // seed fold over the decimal source: 1 + 2 + 3 + 4 = 10
            Console.WriteLine("seed=" + D(a.Aggregate(1m, (acc, x) => acc + x)));

            // an empty decimal source returns the seed unchanged (net48)
            Console.WriteLine("empty=" + D(new decimal[0].Aggregate(7m, (acc, x) => acc + x)));

            // seed + resultSelector over the decimal source: (10 + 2 + 3 + 4) / 2 = 9.5
            Console.WriteLine("rs=" + D(a.Aggregate(10m, (acc, x) => acc + x, r => r / 2m)));

            // resultSelector returning a REFERENCE: the projection ABI is the
            // reference slot
            Console.WriteLine("rsref=" + a.Aggregate(1m, (acc, x) => acc * x, r => "v=" + D(r)));

            // the int32 source with a decimal accumulator + resultSelector:
            // (10 + 1 + 2 + 3) * 2 = 32
            int[] ia = { 1, 2, 3 };
            Console.WriteLine("i4rs=" + D(ia.Aggregate(10m, (acc, x) => acc + x, r => r * 2m)));

            Console.WriteLine("done");
        }
    }
}
