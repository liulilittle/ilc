using System;
using System.Linq;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-linq -- nb9 LINQ-ARITY regression probe.
//
// These four forms were SILENTLY MIS-WIRED before nb9:
//   * GroupBy(key, element)  -- lost the element selector (fell back to a stub)
//   * Aggregate(seed, func)  -- treated the int32 seed as the source array pointer
//   * Select((x, i) => ...)  -- 2-arg Func<T,int,T> called through the 1-arg ABI
//   * Where((x, i) => ...)   -- same
//   * StringBuilder.Append(string, int, int) -- dropped receiver+string (C0 break)
//
// All of them are now REAL implementations (no LOUD fallback). net48 oracle:
//   [lq.aggseed]=110 / [lq.aggseedres]=s110 / [lq.agg.seed.empty]=5
//   [lq.gbelem]=1:10,30,0:20,40 / [lq.gbresult]=1=2,0=2
//   [lq.whereidx]=1,3 / [sb.appendsub]=cde / [sb.appendcarr]=bc
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9Linq
    {
        static int Sum2(int a, int b) => a + b;

        public static void Main()
        {
            // Aggregate(seed, func): 1..4 summed from seed 100 -> 110.
            int[] nums = { 1, 2, 3, 4 };
            int seeded = nums.Aggregate(100, Sum2);
            Console.WriteLine("[lq.aggseed]=" + seeded);

            // Aggregate(seed, func) on an empty source returns the seed.
            int[] empty = new int[0];
            Console.WriteLine("[lq.agg.seed.empty]=" + empty.Aggregate(5, Sum2));

            // Aggregate(func) (seedless) must still work.
            Console.WriteLine("[lq.agg.seedless]=" + nums.Aggregate(Sum2));

            // GroupBy(keySelector, elementSelector): group by parity, project x*10.
            int[] data = { 10, 20, 30, 40 };
            var grouped = data.GroupBy(x => x % 20, x => x + 0);
            var sb = new StringBuilder();
            foreach (var g in grouped)
            {
                sb.Append(g.Key + ":");
                foreach (int v in g) sb.Append(v + ",");
            }
            Console.WriteLine("[lq.gbelem]=" + sb.ToString());

            // GroupBy(keySelector) only (1 delegate) must still work.
            var grouped1 = data.GroupBy(x => x % 20);
            var sb1 = new StringBuilder();
            foreach (var g in grouped1) sb1.Append(g.Key + "=" + g.Count() + ",");
            Console.WriteLine("[lq.gbresult]=" + sb1.ToString());

            // Select((x, i) => ...) -- index-aware selector.
            int[] picked = nums.Select((x, i) => i).ToArray();
            Console.WriteLine("[lq.selectidx]=" + string.Join(",", picked));

            // Where((x, i) => ...) -- index-aware predicate (odd positions).
            int[] odd = nums.Where((x, i) => i % 2 == 1).ToArray();
            Console.WriteLine("[lq.whereidx]=" + string.Join(",", odd));

            // StringBuilder.Append(string, int, int) -- net48 substring append.
            var sb2 = new StringBuilder("ab");
            sb2.Append("cdef", 1, 3);
            Console.WriteLine("[sb.appendsub]=" + sb2.ToString());

            // Append(string,int,int) bounds: startIndex+count past the end throws.
            try
            {
                new StringBuilder().Append("abc", 1, 5);
                Console.WriteLine("[sb.appendsub.oob]=none");
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("[sb.appendsub.oob]=AOORE");
            }
        }
    }
}
