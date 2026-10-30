// audit-fix3-d2j -- List<by-value value struct> ELEMENT with
// DefaultIfEmpty(source, defaultValue). Before the list storage kinds 13..16
// existed these lists fell into the reference tag, so the caller's struct value
// was passed into an Il2CppObject* slot.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2j
    {
        private static string D(DateTime v)
        {
            return v.Year + "-" + v.Month + "-" + v.Day + " " + v.Hour + ":" + v.Minute + ":" + v.Second;
        }

        public static void Main()
        {
            DateTime dt = new DateTime(2021, 6, 7, 8, 9, 10);
            List<DateTime> le = new List<DateTime>();
            DateTime[] r1 = le.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_len=" + r1.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_0=" + D(r1[0]));

            List<DateTime> l1 = new List<DateTime> { new DateTime(2000, 1, 2, 3, 4, 5) };
            DateTime[] r2 = l1.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_one_len=" + r2.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_one_0=" + D(r2[0]));

            List<DateTime> l2 = new List<DateTime>
            {
                new DateTime(1999, 12, 31), new DateTime(2001, 2, 3, 4, 5, 6)
            };
            DateTime[] r3 = l2.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_two_len=" + r3.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dt_two_0=" + D(r3[0]));
            Console.WriteLine("dt_two_1=" + D(r3[1]));

            TimeSpan ts = new TimeSpan(1, 2, 3, 4, 5);
            List<TimeSpan> lte = new List<TimeSpan>();
            TimeSpan[] r4 = lte.DefaultIfEmpty(ts).ToArray();
            Console.WriteLine("ts_len=" + r4.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("ts_0=" + r4[0].Ticks.ToString(CultureInfo.InvariantCulture));

            List<TimeSpan> lt1 = new List<TimeSpan> { TimeSpan.FromMinutes(7) };
            Console.WriteLine("ts_one_0=" +
                lt1.DefaultIfEmpty(ts).ToArray()[0].Ticks.ToString(CultureInfo.InvariantCulture));

            Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            List<Guid> lge = new List<Guid>();
            Guid[] r5 = lge.DefaultIfEmpty(g).ToArray();
            Console.WriteLine("guid_len=" + r5.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("guid_0=" + r5[0].ToString());

            List<Guid> lg1 = new List<Guid> { new Guid("ffeeddcc-bbaa-9988-7766-554433221100") };
            Console.WriteLine("guid_one_0=" + lg1.DefaultIfEmpty(g).ToArray()[0].ToString());

            // The non-empty list passes through unchanged (identity, not a rebuilt
            // array), so the element VALUES must survive the boxed round trip.
            List<DateTime> l3 = new List<DateTime> { new DateTime(2010, 5, 6), new DateTime(2011, 7, 8) };
            DateTime[] r6 = l3.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("pass_len=" + r6.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("pass_0=" + D(r6[0]));
            Console.WriteLine("pass_1=" + D(r6[1]));
        }
    }
}
