//------------------------------------------------------------------------------
// dateops.cs -- System.DateTime DEEP reference program (net48 oracle),
// second-generation date batch (dateops). Extends timeops (arithmetic /
// format) and datetime2 (ParseExact / yy expansion) with the SPECIFIC probe
// families requested by the wiring audit:
//
//   * ctor MULTI-FORM    -- 3-arg (y,m,d) and 6-arg (y,m,d,h,min,s) (the two
//     wired shapes; the 7-arg (ms) and 1-arg (ticks) ctors are LOUD gaps).
//   * Ticks               -- routed through the TimeSpan surface
//     (DateTime - DateTime -> TimeSpan.get_Ticks; TimeSpan ctor(ticks) +
//     get_Ticks round trip) because DateTime.get_Ticks itself is LOUD
//     (MethodMap.DateTimeExtra.cs).
//   * AddDays CROSS-MONTH -- forward/backward across month and year
//     boundaries, leap-February boundaries.
//   * Parse ROUND-TRIP    -- DateTime.Parse(invariant) -> ToString("yyyy-MM-
//     dd HH:mm:ss") == the input (the W2 wired subset: ISO + US orders).
//   * FORMAT yyyy-MM-dd HH:mm:ss + the culture-neutral custom set.
//   * COMPARISON          -- DateTime.CompareTo and the static
//     DateTime.Compare (wired; the relational operators < > are NOT mapped).
//   * SUBTRACTION         -- DateTime - DateTime / Subtract(DateTime) ->
//     TimeSpan, with the TimeSpan getter surface.
//
// Every line prints "label=True/False" or a formatted string. net48 is the
// reference baseline.
//
// DELIBERATELY NOT PROBED (documented runtime gaps -- do NOT call here):
//   * DateTime.ToUniversalTime / ToLocalTime / get_Kind / get_Today /
//     get_Date / get_TimeOfDay / get_Millisecond / get_DayOfYear /
//     AddMonths / AddYears / AddSeconds / AddMilliseconds / op_Addition /
//     Subtract(TimeSpan) / relational < > == -- ALL LOUD in
//     MethodMap.DateTimeExtra.cs (no runtime icall). The Ticks requirement
//     is met via the TimeSpan surface (the wired representation is a raw
//     64-bit tick count, so TimeSpan.Ticks IS the DateTime tick delta).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class DateOps
    {
        public static void Main()
        {
            // ===== ctor multi-form: 3-arg + 6-arg =====
            DateTime d3 = new DateTime(2020, 6, 15);
            Console.WriteLine("c3_y=" + (d3.Year == 2020));
            Console.WriteLine("c3_m=" + (d3.Month == 6));
            Console.WriteLine("c3_d=" + (d3.Day == 15));
            Console.WriteLine("c3_h=" + (d3.Hour == 0));
            Console.WriteLine("c3_s=" + (d3.Second == 0));

            DateTime d6 = new DateTime(2020, 6, 15, 13, 45, 30);
            Console.WriteLine("c6_y=" + (d6.Year == 2020));
            Console.WriteLine("c6_m=" + (d6.Month == 6));
            Console.WriteLine("c6_d=" + (d6.Day == 15));
            Console.WriteLine("c6_h=" + (d6.Hour == 13));
            Console.WriteLine("c6_min=" + (d6.Minute == 45));
            Console.WriteLine("c6_s=" + (d6.Second == 30));
            Console.WriteLine("c6_dow=" + ((int)d6.DayOfWeek == 1));          // 2020-06-15 = Monday

            // ===== Ticks via the TimeSpan surface =====
            TimeSpan dayDiff = new DateTime(2021, 1, 2) - new DateTime(2021, 1, 1);
            Console.WriteLine("tick_1day=" + (dayDiff.Ticks == 864000000000L));
            TimeSpan msDiff = new DateTime(2021, 1, 1, 0, 0, 5) - new DateTime(2021, 1, 1);
            Console.WriteLine("tick_5sec=" + (msDiff.Ticks == 50000000L));
            TimeSpan built = new TimeSpan(dayDiff.Ticks);                      // TimeSpan ctor(ticks) round trip
            Console.WriteLine("tick_roundtrip=" + (built.Ticks == dayDiff.Ticks));
            Console.WriteLine("tick_neg=" + ((new DateTime(2021, 1, 1) - new DateTime(2021, 1, 2)).Ticks == -864000000000L));

            // ===== AddDays cross-month / cross-year / leap =====
            DateTime eom = new DateTime(2020, 1, 31);
            Console.WriteLine("add_month_end=" + eom.AddDays(1).ToString("yyyy-MM-dd"));    // 2020-02-01
            Console.WriteLine("add_feb_end=" + new DateTime(2020, 2, 28).AddDays(1).ToString("yyyy-MM-dd")); // 2020-02-29 (leap)
            Console.WriteLine("add_leap_next=" + new DateTime(2020, 2, 28).AddDays(2).ToString("yyyy-MM-dd")); // 2020-03-01
            Console.WriteLine("add_yearend=" + new DateTime(2020, 12, 31).AddDays(1).ToString("yyyy-MM-dd")); // 2021-01-01
            Console.WriteLine("add_back=" + new DateTime(2020, 3, 1).AddDays(-1).ToString("yyyy-MM-dd"));    // 2020-02-29
            Console.WriteLine("add_back_nonleap=" + new DateTime(2019, 3, 1).AddDays(-1).ToString("yyyy-MM-dd")); // 2019-02-28
            Console.WriteLine("add_multiple=" + new DateTime(2020, 6, 15).AddDays(30).ToString("yyyy-MM-dd")); // 2020-07-15
            Console.WriteLine("add_many=" + new DateTime(2020, 6, 15).AddDays(400).ToString("yyyy-MM-dd"));  // 2021-07-20
            Console.WriteLine("add_frac=" + new DateTime(2020, 6, 15, 12, 0, 0).AddDays(0.5).ToString("yyyy-MM-dd HH:mm:ss")); // +12h

            // ===== Parse round-trip (invariant subset) =====
            Console.WriteLine("parse_iso=" + DateTime.Parse("2020-06-15 13:45:30", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("parse_iso_mid=" + DateTime.Parse("2020-06-15 03:05:09", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("parse_us=" + DateTime.Parse("6/15/2020", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd"));
            Console.WriteLine("parse_yy=" + DateTime.Parse("6/15/50", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd")); // TwoDigitYearMax 2049 -> 1950
            Console.WriteLine("parse_yy2=" + DateTime.Parse("6/15/49", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd")); // -> 2049
            Console.WriteLine("parse_roundtrip=" + (DateTime.Parse("2020-02-29 23:59:59", CultureInfo.InvariantCulture)
                                                       .ToString("yyyy-MM-dd HH:mm:ss") == "2020-02-29 23:59:59"));
            Console.WriteLine("parse_leap_ok=" + (DateTime.Parse("2020-02-29", CultureInfo.InvariantCulture).Day == 29));

            // ===== format yyyy-MM-dd HH:mm:ss + custom set =====
            DateTime fm = new DateTime(2020, 6, 15, 13, 45, 30);
            Console.WriteLine("fmt_full=" + fm.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("fmt_slash=" + fm.ToString("yyyy/MM/dd"));
            Console.WriteLine("fmt_ymd=" + fm.ToString("yyyyMMdd"));
            Console.WriteLine("fmt_yy=" + fm.ToString("yy-MM-dd"));
            Console.WriteLine("fmt_hms=" + fm.ToString("HH:mm:ss"));
            Console.WriteLine("fmt_dow=" + fm.ToString("dddd", CultureInfo.InvariantCulture));  // Monday
            Console.WriteLine("fmt_year=" + fm.ToString("yyyy"));

            // ===== comparison: CompareTo + static Compare =====
            DateTime a = new DateTime(2020, 6, 15);
            DateTime b = new DateTime(2020, 6, 16);
            Console.WriteLine("cmp_lt=" + (a.CompareTo(b) < 0));
            Console.WriteLine("cmp_eq=" + (a.CompareTo(new DateTime(2020, 6, 15)) == 0));
            Console.WriteLine("cmp_gt=" + (b.CompareTo(a) > 0));
            Console.WriteLine("cmp_static_lt=" + (DateTime.Compare(a, b) < 0));
            Console.WriteLine("cmp_static_eq=" + (DateTime.Compare(a, a) == 0));
            Console.WriteLine("cmp_static_gt=" + (DateTime.Compare(b, a) > 0));

            // ===== subtraction -> TimeSpan + TimeSpan getter surface =====
            DateTime start = new DateTime(2020, 6, 15, 8, 0, 0);
            DateTime end = new DateTime(2020, 6, 17, 10, 30, 45);
            TimeSpan span = end - start;                                        // op_Subtraction
            Console.WriteLine("sub_days=" + (span.Days == 2));
            Console.WriteLine("sub_hours=" + (span.Hours == 2));
            Console.WriteLine("sub_minutes=" + (span.Minutes == 30));
            Console.WriteLine("sub_seconds=" + (span.Seconds == 45));
            Console.WriteLine("sub_total_h=" + (span.TotalHours == 50.5));
            Console.WriteLine("sub_total_m=" + (span.TotalMinutes == 3030.0));
            Console.WriteLine("sub_total_s=" + (span.TotalSeconds == 181845.0));
            Console.WriteLine("sub_total_d=" + (span.TotalDays == 181845.0 / 86400.0));
            TimeSpan span2 = end.Subtract(start);                              // Subtract(DateTime)
            Console.WriteLine("sub_method_eq=" + (span2.Ticks == span.Ticks));
            Console.WriteLine("sub_neg=" + ((start - end).TotalSeconds == -181845.0));
            // DateTime(long ticks) ctor (batch 151)
            DateTime t0 = new DateTime(0L);
            Console.WriteLine("ticks0=" + (t0.Ticks == 0L && t0.Year == 1));
            DateTime t1 = new DateTime(599266080000000000L);
            Console.WriteLine("ticks1=" + (t1.Year == 1900 && t1.Ticks == 599266080000000000L));
            try { new DateTime(-1L); Console.WriteLine("ticksneg=no-throw"); }
            catch (ArgumentOutOfRangeException) { Console.WriteLine("ticksneg=throw"); }
            // TimeSpan(days, hours, minutes, seconds) ctor (batch 152)
            TimeSpan ts4 = new TimeSpan(1, 2, 3, 4);
            Console.WriteLine("ts4_days=" + (ts4.Days == 1 && ts4.Hours == 2 && ts4.Minutes == 3 && ts4.Seconds == 4));
            Console.WriteLine("ts4_ticks=" + (ts4.Ticks == ((1L * 24 + 2) * 3600 + 3 * 60 + 4) * 10000000L));
            TimeSpan tsNeg = new TimeSpan(0, 0, -5);
            Console.WriteLine("tsneg=" + (tsNeg.Ticks == -50000000L));
        }
    }
}
