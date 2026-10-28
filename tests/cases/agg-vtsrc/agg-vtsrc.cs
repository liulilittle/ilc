//------------------------------------------------------------------------------
// agg-vtsrc.cs -- e2e reference case for Aggregate over a NON-decimal
// VALUE-TYPE source (System.DateTime / System.TimeSpan / System.Guid) with a
// DECIMAL accumulator and, for the 4-arity form, a result selector:
//   Aggregate<TSource,decimal>(source, seed, func)                [Enumerable.cs:1391]
//   Aggregate<TSource,decimal,TResult>(source, seed, func, rs)    [Enumerable.cs:1399]
//
// Before this fix the transpiler dispatched these shapes to the INT32 element
// reader (DecAccFn), so an 8-byte TimeSpan cell was read as 4 bytes (silent
// wrong value) and the packed 8-byte DateTime cell through a stale pointer (a
// wild read / access violation). Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AggVtSrc
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            // ---- TimeSpan source (8-byte tick cells), negative value + Days ----
            TimeSpan[] ts = { TimeSpan.FromDays(1), TimeSpan.FromDays(-2), new TimeSpan(3, 0, 0) };

            // seed fold: (1*24+0) + (-2*24+0) + (0*24+3) = 24 - 48 + 3 = -21
            Console.WriteLine("ts_seed=" + D(ts.Aggregate(0m, (acc, t) => acc + t.Days * 24 + t.Hours)));

            // an empty value-type source returns the seed unchanged (net48)
            Console.WriteLine("ts_empty=" + D(new TimeSpan[0].Aggregate(7m, (acc, t) => acc + t.Hours)));

            // seed + resultSelector, TResult = int32: days sum -1
            Console.WriteLine("ts_rs_i4=" + ts.Aggregate(0m, (acc, t) => acc + t.Days, r => (int)r));

            // seed + resultSelector, TResult = decimal: days sum -1 -> *2 = -2
            Console.WriteLine("ts_rs_dec=" + D(ts.Aggregate(0m, (acc, t) => acc + t.Days, r => r * 2m)));

            // ---- DateTime source (8-byte packed cell), cross-month + Day ----
            DateTime[] dt =
            {
                new DateTime(2020, 1, 31),
                new DateTime(2020, 2, 29),
                new DateTime(2021, 3, 1),
            };

            // day-of-month sum: 31 + 29 + 1 = 61
            Console.WriteLine("dt_day=" + D(dt.Aggregate(0m, (acc, d) => acc + d.Day)));

            // month*100 + day: 131 + 229 + 301 = 661 (cross-month)
            Console.WriteLine("dt_monthday=" + D(dt.Aggregate(0m, (acc, d) => acc + d.Month * 100 + d.Day)));

            // seed + resultSelector, TResult = decimal: 61 / 2 = 30.5
            Console.WriteLine("dt_rs_dec=" + D(dt.Aggregate(0m, (acc, d) => acc + d.Day, r => r / 2m)));

            // seed + resultSelector, TResult = int32: 61 * 10 = 610
            Console.WriteLine("dt_rs_i4=" + dt.Aggregate(0m, (acc, d) => acc + d.Day, r => (int)(r * 10m)));

            // ---- Guid source (16-byte cell) ----
            Guid[] gs =
            {
                Guid.Empty,
                new Guid("11111111-2222-3333-4444-555555555555"),
                Guid.Empty,
            };

            // 36 + 36 + 36 = 108 (branch-free fold: a DECIMAL stack-phi inside
            // a branchy lambda body hits the pre-existing stack-fold temp-
            // typing gap, see docs/audit/b5-gaps.md)
            Console.WriteLine("guid_seed=" + D(gs.Aggregate(0m, (acc, g) => acc + g.ToString().Length)));

            // seed + resultSelector, TResult = int32: 108
            Console.WriteLine("guid_rs_i4=" + gs.Aggregate(0m, (acc, g) => acc + g.ToString().Length, r => (int)r));

            Console.WriteLine("done");
        }
    }
}