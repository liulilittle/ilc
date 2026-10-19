using System;
using System.Linq;

//------------------------------------------------------------------------------
// p-nb9-fix-aggsel -- nb9 fix batch M21: the SELECTOR forms of the int32
// array aggregates (Sum/Min/Max/Average).
//
// Before this fix the transpiler ignored `count` and returned the
// selector-less aggregate with ParamCount 1, so the selector was dropped and
// the result was the wrong value (plus a stack desync).
//
// net48 oracle (host .NET Framework 4.x):
//   a = {1,2,3}
//   [sum_sel]=60     a.Sum(x => x * 10)
//   [min_sel]=10     a.Min(x => x * 10)
//   [max_sel]=30     a.Max(x => x * 10)
//   [avg_sel]=4      a.Average(x => x * 2)
//   [sum_neg]=-60    {-1,-2,-3}.Sum(x => x * 10)
//   [sum_nosel]=6    a.Sum()
//   [min_nosel]=1    a.Min()
//   [max_nosel]=3    a.Max()
//   [avg_nosel]=2    a.Average()
//   [sum_empty]=0    new int[0].Sum(x => x * 10)
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixAggSel
    {
        public static void Main()
        {
            int[] a = { 1, 2, 3 };

            Console.WriteLine("[sum_sel]=" + a.Sum(x => x * 10));
            Console.WriteLine("[min_sel]=" + a.Min(x => x * 10));
            Console.WriteLine("[max_sel]=" + a.Max(x => x * 10));
            Console.WriteLine("[avg_sel]=" + a.Average(x => x * 2));

            int[] neg = { -1, -2, -3 };
            Console.WriteLine("[sum_neg]=" + neg.Sum(x => x * 10));

            Console.WriteLine("[sum_nosel]=" + a.Sum());
            Console.WriteLine("[min_nosel]=" + a.Min());
            Console.WriteLine("[max_nosel]=" + a.Max());
            Console.WriteLine("[avg_nosel]=" + a.Average());

            int[] empty = new int[0];
            Console.WriteLine("[sum_empty]=" + empty.Sum(x => x * 10));
        }
    }
}
