using System;
using System.Collections.Generic;
using System.Linq;

// Section 110: Enumerable.ToLookup with a double TKey.
// net48 oracle:
//   new[]{1.0,2.0,2.0,3.5,3.5,3.5,double.NaN}.ToLookup(k => k, v => (int)v)
//   Distinct keys (first-appearance order): 1.0, 2.0, 3.5, NaN => Count=4
//   lookup[1.0] -> [1]; lookup[2.0] -> [2,2]; lookup[3.5] -> [3,3,3]
//   NaN groups with NaN (net48 groups NaN with NaN per Double.Equals)
//   lookup.Contains(2.0)=true, lookup.Contains(9.0)=false
//   lookup[9.0] -> empty sequence; foreach preserves source order within group
namespace Cases {
    public static class TolookupDoubleKey {
        public static void Main() {
            var src = new[] { 1.0, 2.0, 2.0, 3.5, 3.5, 3.5, double.NaN };
            var lk = src.ToLookup(k => k, v => (int)v);
            Console.Write("count=" + lk.Count);
            Console.Write(" c2=" + lk.Contains(2.0));
            Console.Write(" c9=" + lk.Contains(9.0));
            Console.Write(" g1=" + string.Join(",", lk[1.0]));
            Console.Write(" g2=" + string.Join(",", lk[2.0]));
            Console.Write(" g35=" + string.Join(",", lk[3.5]));
            // NaN group: net48 groups NaN with NaN
            var nan = lk[double.NaN];
            Console.Write(" gnan=" + string.Join(",", nan) + " nancnt=" + nan.Count());
            Console.Write(" empty9=" + string.Join(",", lk[9.0]));
            Console.WriteLine();
        }
    }
}
