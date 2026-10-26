//------------------------------------------------------------------------------
// loud-linq-grpdecel.cs -- e2e reference case for Enumerable.GroupBy over a
// System.Decimal source with a DECIMAL TElement (net48
// GroupBy<TSource,TKey,TElement>(source, keySelector, elementSelector) with
// TElement = System.Decimal).
//
// The bucket stores 16-byte System.Decimal VALUE cells (the decimal T3
// contract), so the per-bucket decimal aggregates (Sum / Sum(selector)) read a
// real decimal[]. Before this change the shape LOUD-rejected ("TElement
// 'valuetype' is not a supported element family").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpDecEl
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m, 4m, 5m };
            var g = a.GroupBy(x => (int)(x % 2m), x => x);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key + " sum=" + D(grp.Sum()) + " sel=" + D(grp.Sum(v => v)));
                foreach (var e in grp)
                    Console.WriteLine("  " + D(e));
            }
            Console.WriteLine("done");
        }
    }
}
