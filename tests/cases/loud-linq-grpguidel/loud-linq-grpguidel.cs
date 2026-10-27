//------------------------------------------------------------------------------
// loud-linq-grpguidel.cs -- e2e reference case for iterating a GroupBy bucket
// whose TElement is a VALUE TYPE (System.Guid). net48 semantics
// (Enumerable.cs:776-780): GroupBy<Guid,int> buckets hold the source Guid
// elements; `foreach (var e in grp)` yields each Guid.
//
// Before this change the bucket enumerator dispatched on the CELL WIDTH only,
// so every 16-byte Guid cell was misboxed as System.Decimal (the widths are
// identical) and the loop variable read a decimal payload. Every printed
// value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpGuidEl
    {
        public static void Main()
        {
            Guid[] a =
            {
                new Guid("00000000-0000-0000-0000-000000000001"),
                new Guid("00000000-0000-0000-0000-000000000002"),
                new Guid("20000000-0000-0000-0000-000000000003"),
            };

            var g = a.GroupBy(x => x.GetHashCode() % 2);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key + " n=" + grp.Count());
                foreach (var e in grp)
                    Console.WriteLine("  " + e.ToString());
            }

            // the bucket snapshot keeps the Guid identity for downstream reads
            var first = a.GroupBy(x => x.GetHashCode() % 2).First().ToArray();
            Console.WriteLine("arr=" + first[0].ToString());

            Console.WriteLine("done");
        }
    }
}
