// audit-fix3-d2i -- List<T>.Aggregate(seed, func, resultSelector) where the
// RESULT SELECTOR returns a BY-VALUE VALUE STRUCT (System.DateTime /
// System.TimeSpan / System.Guid). The 4-parameter projection matrix only
// carried int32/int64/float/double/decimal/reference results, so this shape was
// a transpile-time LOUD even though the runtime already returns these structs
// by value from every other LINQ entry.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2i
    {
        private static string D(DateTime v)
        {
            return v.Year + "-" + v.Month + "-" + v.Day + " " + v.Hour + ":" + v.Minute + ":" + v.Second;
        }

        public static void Main()
        {
            List<int> li = new List<int> { 1, 2, 3, 4 };
            List<long> ll = new List<long> { 5L, 6L, 7L };
            List<double> ld = new List<double> { 0.5, 1.5, 2.5 };
            List<string> ls = new List<string> { "a", "bb", "ccc" };

            DateTime dt = li.Aggregate(0, (a, x) => a + x,
                a => new DateTime(2020, 1, 1).AddDays(a));
            Console.WriteLine("[i4_i4_dt]=" + D(dt));

            DateTime dt2 = ll.Aggregate(0L, (a, x) => a + x,
                a => new DateTime(2020, 1, 1).AddHours((int)a));
            Console.WriteLine("[i8_i8_dt]=" + D(dt2));

            DateTime dt3 = ld.Aggregate(0.0, (a, x) => a + x,
                a => new DateTime(2020, 1, 1).AddMinutes((int)(a * 10)));
            Console.WriteLine("[d_d_dt]=" + D(dt3));

            DateTime dt4 = ls.Aggregate("", (a, x) => a + x,
                a => new DateTime(2020, 1, 1).AddDays(a.Length));
            Console.WriteLine("[ref_ref_dt]=" + D(dt4));

            DateTime dt5 = li.Aggregate(0m, (a, x) => a + (decimal)x,
                a => new DateTime(2020, 1, 1).AddDays((int)a));
            Console.WriteLine("[dec_dec_dt]=" + D(dt5));

            TimeSpan ts = li.Aggregate(0, (a, x) => a + x,
                a => TimeSpan.FromMinutes(a * 90));
            Console.WriteLine("[i4_i4_ts]=" + ts.Ticks.ToString(CultureInfo.InvariantCulture));

            Guid g = ls.Aggregate(0, (a, x) => a + x.Length,
                a => new Guid("00000000-0000-0000-0000-00000000000" + a.ToString(CultureInfo.InvariantCulture)));
            Console.WriteLine("[i4_i4_guid]=" + g.ToString());

            Guid g2 = ll.Aggregate(0L, (a, x) => a + x,
                a => new Guid("00000000-0000-0000-0000-0000000000" + (a % 100).ToString("00", CultureInfo.InvariantCulture)));
            Console.WriteLine("[i8_i8_guid]=" + g2.ToString());

            // The selector runs ONCE, on the final accumulator.
            int calls = 0;
            DateTime dt6 = li.Aggregate(0, (a, x) => a + x, a =>
            {
                calls = calls + 1;
                return new DateTime(2020, 1, 1).AddDays(a);
            });
            Console.WriteLine("[dt_calls]=" + calls.ToString(CultureInfo.InvariantCulture) + "/" + D(dt6));
        }
    }
}
