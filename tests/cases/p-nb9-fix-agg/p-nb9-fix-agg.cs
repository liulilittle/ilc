using System;
using System.Linq;

//------------------------------------------------------------------------------
// p-nb9-fix-agg -- nb9 fix batch: Aggregate's TAccumulate family + the
// 3-delegate (seed, func, resultSelector) overload.
//
// Before this fix every 3-argument Aggregate was routed to the int32 seed
// entry: a long/double seed was truncated and the callback ran through the
// int32 ABI (silent wrong values); the 4-argument overload was LOUD.
//
// net48 oracle (host .NET Framework 4.x, source {1,2,3,4,5}):
//   [agg_long]=3000000015
//   [agg_double]=15.5
//   [agg_seed_result]=230        (fold 100+1+2+3+4+5 = 115, resultSelector
//                                 runs ONCE on the final accumulator -> 230)
//   [agg_long_empty]=7
//   [agg_double_empty]=1.5
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixAgg
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3, 4, 5 };

            Console.WriteLine("[agg_long]=" + data.Aggregate(3000000000L, (a, b) => a + b));
            Console.WriteLine("[agg_double]=" + data.Aggregate(0.5, (a, b) => a + b));
            Console.WriteLine("[agg_seed_result]=" + data.Aggregate(100, (a, b) => a + b, a => a * 2));

            int[] empty = new int[0];
            Console.WriteLine("[agg_long_empty]=" + empty.Aggregate(7L, (a, b) => a + b));
            Console.WriteLine("[agg_double_empty]=" + empty.Aggregate(1.5, (a, b) => a + b));
        }
    }
}
