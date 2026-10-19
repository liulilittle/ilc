using System;
using System.Collections.Generic;
using System.Linq;

//------------------------------------------------------------------------------
// p-nb9-fix-gbops -- nb9 fix batch: the IGrouping / IGroupingResult operator
// surface (M5 / M15 / M20).
//
// Before this fix:
//   * g.Where(pred)   -> "Object is not an array."   (array icall on a grouping)
//   * g.Count(pred)   -> silent wrong value (read the grouping as an array)
//   * g.Sum(sel)      -> access violation
//   * g.Min()/g.Max() -> NotSupportedException (LOUD)
//   * grouped.Count() -> NotSupportedException (the gate rejected 0x15)
//   * grouped.ToArray()/Any()/First()/ToList() -> LOUD
//
// net48 oracle (host .NET Framework 4.x, source {1,2,3,4,5}, key x%2):
//   [grp_count]=2  [grp_any]=True  [grp_tolist]=2  [grp_toarray]=2
//   [grp_first_key]=1
//   [g_where]=3,5  [g_cntp]=2  [g_sumsel]=90  [g_min]=1  [g_max]=5
//   [g_any]=True   [g_all]=True
//
// NOTE: the per-bucket assertions run on the bucket with Key == 1 ({1,3,5}).
// A `break` inside a foreach body is deliberately avoided (a separate
// pre-existing IR gap drops it; unrelated to this batch).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixGbOps
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3, 4, 5 };
            var grouped = data.GroupBy(x => x % 2);

            Console.WriteLine("[grp_count]=" + grouped.Count());
            Console.WriteLine("[grp_any]=" + grouped.Any());
            Console.WriteLine("[grp_tolist]=" + grouped.ToList().Count);
            Console.WriteLine("[grp_toarray]=" + grouped.ToArray().Length);
            Console.WriteLine("[grp_first_key]=" + grouped.First().Key);

            foreach (var g in grouped)
            {
                if (g.Key == 1)
                {
                    Console.WriteLine("[g_where]=" + string.Join(",", g.Where(v => v > 2).ToArray()));
                    Console.WriteLine("[g_cntp]=" + g.Count(v => v > 2));
                    Console.WriteLine("[g_sumsel]=" + g.Sum(v => v * 10));
                    Console.WriteLine("[g_min]=" + g.Min());
                    Console.WriteLine("[g_max]=" + g.Max());
                    Console.WriteLine("[g_any]=" + g.Any());
                    Console.WriteLine("[g_all]=" + g.All(v => v > 0));
                }
            }
        }
    }
}
