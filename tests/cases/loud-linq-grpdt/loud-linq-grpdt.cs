//------------------------------------------------------------------------------
// loud-linq-grpdt.cs -- e2e reference case for Enumerable.GroupBy over a
// DateTime SOURCE (net48 GroupBy<TSource,TKey> / GroupBy<TSource,TKey,
// TElement> with TSource = System.DateTime).
//
// The DateTime element crosses as the 8-byte runtime value struct BY VALUE (the
// InvokeSelDt* ABIs); the bucket stores the DateTime cells
// (GroupElemKind::DateTime). Before this change the shape LOUD-rejected
// ("over a value-type source is not wired: the decimal-source grouping surface
// covers System.Decimal[] only").
//
// NOTE: iterating a bucket whose TElement is a VALUE TYPE (DateTime) is a
// pre-existing transpiler limitation (the grouping bucket's element enumerator
// does not propagate the TElement carrier) -- the e2e case therefore covers the
// key-only form and an int32 TElement.
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpDt
    {
        public static void Main()
        {
            DateTime[] a = { new DateTime(2020, 1, 1), new DateTime(2020, 1, 2), new DateTime(2021, 1, 1), new DateTime(2021, 6, 1) };

            // key-only: the bucket element IS the source DateTime
            var g = a.GroupBy(x => x.Year);
            foreach (var grp in g)
                Console.WriteLine("key=" + grp.Key + " n=" + grp.Count());

            // element selector to int32
            var g2 = a.GroupBy(x => x.Year, x => x.Month);
            foreach (var grp in g2)
            {
                Console.WriteLine("mkey=" + grp.Key);
                foreach (var e in grp)
                    Console.WriteLine("  m=" + e);
            }

            Console.WriteLine("done");
        }
    }
}
