// audit-fix3-d2h -- DefaultIfEmpty(source, defaultValue) over a VALUE-STRUCT
// ARRAY source (System.DateTime / System.TimeSpan / System.Guid). The 2-argument
// form used to be a transpile-time LOUD ("the stage-1 runtime LINQ surface is
// int32 (0x08)"), even though the runtime already reads/writes every one of
// these cells at its real width (LinqCellReadDt/Ts/Guid, LinqCellDefault).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2h
    {
        private static string D(DateTime v)
        {
            return v.Year + "-" + v.Month + "-" + v.Day + " " + v.Hour + ":" + v.Minute + ":" + v.Second;
        }

        public static void Main()
        {
            DateTime dt = new DateTime(2021, 6, 7, 8, 9, 10);

            DateTime[] empty = new DateTime[0];
            DateTime[] r1 = empty.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_empty_len=" + r1.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_empty_0=" + D(r1[0]));

            DateTime[] one = new DateTime[] { new DateTime(2000, 1, 2, 3, 4, 5) };
            DateTime[] r2 = one.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_one_len=" + r2.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_one_0=" + D(r2[0]));

            DateTime[] two = new DateTime[] { new DateTime(1999, 12, 31), new DateTime(2001, 2, 3) };
            DateTime[] r3 = two.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_two_len=" + r3.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_two_1=" + D(r3[1]));

            TimeSpan ts = new TimeSpan(1, 2, 3, 4, 5);
            TimeSpan[] te = new TimeSpan[0];
            TimeSpan[] r4 = te.DefaultIfEmpty(ts).ToArray();
            Console.WriteLine("ts_empty_len=" + r4.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("ts_empty_0=" + r4[0].Ticks.ToString(CultureInfo.InvariantCulture));

            Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            Guid[] ge = new Guid[0];
            Guid[] r5 = ge.DefaultIfEmpty(g).ToArray();
            Console.WriteLine("guid_empty_len=" + r5.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("guid_empty_0=" + r5[0].ToString());

            Guid g2 = new Guid("ffeeddcc-bbaa-9988-7766-554433221100");
            Guid[] g1 = new Guid[] { g2 };
            Guid[] r6 = g1.DefaultIfEmpty(g).ToArray();
            Console.WriteLine("guid_one_len=" + r6.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("guid_one_0=" + r6[0].ToString());
        }
    }
}
