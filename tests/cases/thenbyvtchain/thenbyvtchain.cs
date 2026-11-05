using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ThenByVtChain
    {
        static string J(DateTime[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString("s");
            }
            return s;
        }
        static string JL(List<DateTime> a)
        {
            string s = "";
            for (int i = 0; i < a.Count; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString("s");
            }
            return s;
        }
        static string JT(TimeSpan[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].Ticks.ToString();
            }
            return s;
        }

        static DateTime[] Ds()
        {
            return new DateTime[]
            {
                new DateTime(2020, 3, 4, 5, 6, 7),
                new DateTime(2019, 1, 2, 3, 4, 5),
                new DateTime(2021, 5, 6, 7, 8, 9),
                new DateTime(2019, 6, 7, 8, 9, 10),
                new DateTime(2020, 1, 2, 3, 4, 5),
                new DateTime(2021, 2, 3, 4, 5, 6),
            };
        }

        // primary Year, secondary Month, tertiary int (day % 2)
        static int KY(DateTime x) { return x.Year; }
        static int KM(DateTime x) { return x.Month; }
        static int KD(DateTime x) { return x.Day % 2; }
        // a TimeSpan SECONDARY key (family 6): even months get ticks=1, odd=2,
        // so within each Year group the Month parity breaks the tie.
        static TimeSpan KTS(DateTime x) { return TimeSpan.FromTicks(x.Month % 2 == 0 ? 1 : 2); }

        static int KTSHours(TimeSpan x) { return x.Hours; }
        static int KTSMins(TimeSpan x) { return x.Minutes; }
        static int KTSSecs(TimeSpan x) { return x.Seconds; }

        public static void Main()
        {
            var ds = Ds();
            Console.WriteLine("[vt3.ymd]=" + J(ds.OrderBy(KY).ThenBy(KM).ThenBy(KD).ToArray()));
            Console.WriteLine("[vt3.desc]=" + J(ds.OrderByDescending(KY).ThenBy(KM).ThenByDescending(KD).ToArray()));
            Console.WriteLine("[vt3.dt2]=" + J(ds.OrderBy(KY).ThenByDescending(KM).ThenBy(KY).ToArray()));
            Console.WriteLine("[vt3.tskey]=" + J(ds.OrderBy(KY).ThenBy(KTS).ToArray()));
            var lst = new List<DateTime>(ds);
            Console.WriteLine("[vt3.list]=" + JL(lst.OrderBy(KY).ThenBy(KM).ThenBy(KD).ToList()));
            Console.WriteLine("[vt2.list]=" + JL(lst.OrderBy(KY).ThenByDescending(KM).ToList()));
            // a TimeSpan[] source (srcKind 14) with int primaries.
            TimeSpan[] tsa = {
                new TimeSpan(3, 2, 1),
                new TimeSpan(1, 4, 2),
                new TimeSpan(2, 1, 5),
                new TimeSpan(1, 2, 3),
                new TimeSpan(3, 3, 3),
            };
            Console.WriteLine("[vt3.ts]=" + JT(tsa.OrderBy(KTSHours).ThenBy(KTSMins).ThenBy(KTSSecs).ToArray()));
            Console.WriteLine("[vt3.tsdesc]=" + JT(tsa.OrderByDescending(KTSHours).ThenBy(KTSMins).ToArray()));
        }
    }
}