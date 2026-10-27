//------------------------------------------------------------------------------
// loud-linq-grpdtel.cs -- e2e reference case for iterating a GroupBy bucket
// whose TElement is a VALUE TYPE (System.DateTime). net48 semantics
// (Enumerable.cs:776-780): GroupBy<DateTime,int> buckets hold the source
// DateTime elements; `foreach (var e in grp)` yields each DateTime.
//
// Before this change the bucket's element enumerator had no DateTime carrier
// (the 8-byte cell width is neither the int32 nor the 16-byte decimal one).
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpDtEl
    {
        public static void Main()
        {
            DateTime[] a =
            {
                new DateTime(2020, 1, 1),
                new DateTime(2020, 6, 15),
                new DateTime(2021, 3, 3),
            };

            var g = a.GroupBy(x => x.Year);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key + " n=" + grp.Count());
                try
                {
                    foreach (var e in grp)
                        Console.WriteLine("  " + e.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXC: " + ex.GetType().FullName + ": " + ex.Message);
                    Console.WriteLine("STK: " + ex.StackTrace);
                }
            }

            Console.WriteLine("done");
        }
    }
}
