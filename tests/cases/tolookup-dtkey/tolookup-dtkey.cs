using System;
using System.Collections.Generic;
using System.Linq;

// Section 112: Enumerable.ToLookup with a DateTime TKey.
// DateTime (0x11 / cell code "12") rides slot 1 (the int64 slot) as raw ticks
// with dictgen kind "19", boxed via icall_box_date_time.
// net48 oracle:
//   new[]{new DateTime(2020,1,1), new DateTime(2020,2,2), new DateTime(2020,1,1)}
//     .ToLookup(k => k)
//   Distinct keys (first-appearance order): 2020-01-01, 2020-02-02 => Count=2
//   lookup[2020-01-01] -> [2020-01-01, 2020-01-01]; lookup[2020-02-02] -> [2020-02-02]
//   lookup.Contains(2020-01-01)=true, lookup.Contains(2020-03-03)=false
//   lookup[2020-03-03] -> empty sequence
//   foreach preserves source order within each group
namespace Cases {
    public static class TolookupDateTimeKey {
        public static void Main() {
            var src = new[] { new DateTime(2020, 1, 1), new DateTime(2020, 2, 2), new DateTime(2020, 1, 1) };
            var lk = src.ToLookup(k => k);
            Console.Write("count=" + lk.Count);
            Console.Write(" c11=" + lk.Contains(new DateTime(2020, 1, 1)));
            Console.Write(" c03=" + lk.Contains(new DateTime(2020, 3, 3)));
            Console.Write(" g11=" + string.Join("|", lk[new DateTime(2020, 1, 1)]));
            Console.Write(" g22=" + string.Join("|", lk[new DateTime(2020, 2, 2)]));
            Console.Write(" g03=" + string.Join("|", lk[new DateTime(2020, 3, 3)]));
            Console.WriteLine();
        }
    }
}
