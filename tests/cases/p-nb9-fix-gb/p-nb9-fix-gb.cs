using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-gb -- nb9 fix batch: GroupBy overload disambiguation.
//
// Before this fix ALL 3-argument GroupBy calls were routed to the
// element-selector entry, so:
//   * GroupBy(key, comparer)          -> "Object is not a delegate."
//   * GroupBy(key, resultSelector)    -> wrong 1-arg selector ABI
//   * GroupBy(key, element) with a REFERENCE element -> access violation
//   * GroupBy arity 4/5               -> NotSupportedException (LOUD)
//
// net48 oracle (measured on host .NET Framework 4.x):
//   [gb_cmp]=1=3,0=2,
//   [gb_rs]=1:3,0:2,
//   [gb_elem_ref]=1=e1|e3|e5,0=e2|e4,
//   [gb_elem_rs]=1#3,0#2,
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixGb
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3, 4, 5 };

            // GroupBy(key, IEqualityComparer<TKey>)
            var sb = new StringBuilder();
            foreach (var g in data.GroupBy(x => x % 2, EqualityComparer<int>.Default))
                sb.Append(g.Key + "=" + g.Count() + ",");
            Console.WriteLine("[gb_cmp]=" + sb.ToString());

            // GroupBy(key, resultSelector) -- Func<TKey,IEnumerable<T>,TResult>
            sb = new StringBuilder();
            foreach (var s in data.GroupBy(x => x % 2, (k, vals) => k + ":" + vals.Count()))
                sb.Append(s + ",");
            Console.WriteLine("[gb_rs]=" + sb.ToString());

            // GroupBy(key, elementSelector) with a REFERENCE element (string).
            sb = new StringBuilder();
            foreach (var g in data.GroupBy(x => x % 2, x => "e" + x))
            {
                sb.Append(g.Key + "=");
                foreach (string s in g) sb.Append(s + "|");
                sb.Append(",");
            }
            Console.WriteLine("[gb_elem_ref]=" + sb.ToString());

            // GroupBy(key, element, resultSelector) -- 4 arguments.
            sb = new StringBuilder();
            foreach (var s in data.GroupBy(x => x % 2, x => x * 10, (k, vals) => k + "#" + vals.Count()))
                sb.Append(s + ",");
            Console.WriteLine("[gb_elem_rs]=" + sb.ToString());
        }
    }
}
