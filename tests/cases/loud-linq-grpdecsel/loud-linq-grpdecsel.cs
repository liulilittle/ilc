//------------------------------------------------------------------------------
// loud-linq-grpdecsel.cs -- e2e reference case for Enumerable.GroupBy over a
// System.Decimal source with an ELEMENT selector and a RESULT selector (net48
// GroupBy<TSource,TKey,TElement>(source, keySelector, elementSelector) and
// GroupBy<TSource,TKey,TElement,TResult>(..., resultSelector)).
//
// The decimal element crosses as the 16-byte value struct BY VALUE (the
// InvokeSelDec* ABIs); the element selector projects it (int32 / reference
// here) and the bucket stores the projected family.
// Before this change the shape LOUD-rejected ("an element selector / comparer /
// result selector over a decimal source has no runtime callback ABI").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpDecSel
    {
        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m, 4m, 5m };

            // element selector: project the decimal to its integer part
            var g = a.GroupBy(x => (int)(x % 2m), x => (int)x);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key);
                foreach (var e in grp)
                    Console.WriteLine("  " + e);
            }

            // result selector: count the projected elements per bucket
            var g2 = a.GroupBy(x => (int)(x % 2m), x => (int)x, (k, items) => k + ":" + items.Count());
            foreach (var s in g2)
                Console.WriteLine("res=" + s);

            // element selector to a reference type
            var g3 = a.GroupBy(x => (int)(x % 2m), x => "v" + (int)x);
            foreach (var grp in g3)
            {
                Console.WriteLine("rkey=" + grp.Key);
                foreach (var e in grp)
                    Console.WriteLine("  " + e);
            }

            Console.WriteLine("done");
        }
    }
}
