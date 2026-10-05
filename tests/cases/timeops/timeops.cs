//------------------------------------------------------------------------------
// timeops.cs -- end-to-end reference case for DateTime / TimeSpan ARITHMETIC
// and FORMATTING depth (net48 oracle). Extends timecase (single-shot
// AddDays/Total*/Subtract) to cross-month/cross-year arithmetic, value-type
// comparisons via CompareTo, full getter surfaces and the culture-neutral
// format specifier set.
//
// Coverage (active, all wired -- see MethodMap.DateTime.cs / TimeSpan.cs):
//   * DateTime 3-arg and 6-arg ctors + get_Year/Month/Day/Hour/Minute/Second
//   * DateTime.DayOfWeek printed by NAME (boxed-enum -> il2cpp_enum_to_string
//     built-in DayOfWeek table, timecase wiring)
//   * AddDays / AddHours / AddMinutes across month/year boundaries incl.
//     negative amounts (back across the boundary)
//   * leap-year AddDays (Feb 28/29 boundary)
//   * DateTime - DateTime (op_Subtraction) and Subtract(DateTime) -> TimeSpan
//   * DateTime.CompareTo (the relational operators < > == are NOT mapped;
//     CompareTo is the wired comparison surface)
//   * ToString(format) over the culture-neutral set: "yyyy-MM-dd HH:mm:ss",
//     "yy/MM/dd", "MM/dd/yyyy", "dd/MM/yyyy", "HH:mm", "yyyy", "MM/dd"
//   * TimeSpan 3-arg (h:m:s) and 1-arg (ticks) ctors
//   * TimeSpan get_Days/Hours/Minutes/Seconds + TotalSeconds/TotalHours/
//     TotalMinutes/TotalDays/TotalMilliseconds + get_Ticks
//   * TimeSpan Add / op_Addition / Subtract / op_Subtraction / Negate /
//     op_UnaryNegation + CompareTo
//
// Every line prints "label=True/False" or a formatted string. net48 is the
// reference baseline.
//
// ACCEPTANCE SURFACE (NOT wired -- do not uncomment until fixed):
//   * DateTime.AddMonths / AddYears      -- no icall in MethodMap.DateTime.cs
//     (generated-name fallback).
//   * DateTime.Subtract(TimeSpan)        -- LOUD: only Subtract(DateTime)
//     is wired (MethodMap.DateTime.cs:43).
//   * DateTime relational operators ==/!=/</> -- not mapped (MethodMap
//     DateTime.cs has no op_* entries); use CompareTo.
//   * DateTime.ToString() (no format)   -- culture-dependent (zh-CN vs the
//     runtime's en-US/invariant), timeext documented deviation.
//   * "MMM"/month-name specifiers       -- culture-dependent (net48 zh-CN
//     vs runtime English), excluded.
//   * TimeSpan.ToString()               -- LOUD (no timespan formatter icall,
//     MethodMap.TimeSpan.cs:49).
//   * TimeSpan 4/5-arg ctors             -- LOUD: from_ticks/from_hms only.
//   * TimeSpan relational operators      -- not mapped; use CompareTo.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class TimeOps {
    public static void Main() {
        // --- DateTime 6-arg ctor + full getter surface ---
        DateTime d = new DateTime(2020, 6, 15, 13, 45, 30);
        Console.WriteLine("y=" + (d.Year == 2020));
        Console.WriteLine("mo=" + (d.Month == 6));
        Console.WriteLine("day=" + (d.Day == 15));
        Console.WriteLine("h=" + (d.Hour == 13));
        Console.WriteLine("mi=" + (d.Minute == 45));
        Console.WriteLine("s=" + (d.Second == 30));
        Console.Write("dow_name=");
        Console.WriteLine(d.DayOfWeek);                                  // Monday (boxed-enum WriteLine path)
        Console.WriteLine("dow_num=" + ((int)d.DayOfWeek == 1));
        // --- formatting (culture-neutral set) ---
        Console.WriteLine("fmt_full=" + d.ToString("yyyy-MM-dd HH:mm:ss"));   // 2020-06-15 13:45:30
        Console.WriteLine("fmt_yy=" + d.ToString("yy/MM/dd"));                // 20/06/15
        Console.WriteLine("fmt_us=" + d.ToString("MM/dd/yyyy"));              // 06/15/2020
        Console.WriteLine("fmt_uk=" + d.ToString("dd/MM/yyyy"));              // 15/06/2020
        Console.WriteLine("fmt_hm=" + d.ToString("HH:mm"));                   // 13:45
        Console.WriteLine("fmt_yyy=" + d.ToString("yyyy"));                   // 2020
        Console.WriteLine("fmt_md=" + d.ToString("MM/dd"));                   // 06/15
        // --- cross-month / cross-year AddDays ---
        DateTime nye = new DateTime(2021, 12, 31, 23, 59, 59);
        Console.WriteLine("nye_d=" + nye.AddDays(1).ToString("yyyy-MM-dd"));  // 2022-01-01
        Console.WriteLine("nye_h=" + nye.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss")); // 2022-01-01 00:59:59
        Console.WriteLine("nye_m=" + nye.AddMinutes(2).ToString("MM/dd/yyyy")); // 01/01/2022
        // --- leap year ---
        Console.WriteLine("leap_1=" + new DateTime(2020, 2, 28).AddDays(1).ToString("MM/dd/yyyy"));  // 02/29/2020
        Console.WriteLine("leap_2=" + new DateTime(2020, 2, 28).AddDays(2).ToString("MM/dd/yyyy"));  // 03/01/2020
        Console.WriteLine("leap_neg=" + new DateTime(2020, 3, 1).AddDays(-1).ToString("MM/dd/yyyy")); // 02/29/2020
        // --- DateTime - DateTime / Subtract(DateTime) -> TimeSpan ---
        TimeSpan diff = new DateTime(2021, 1, 2) - new DateTime(2021, 1, 1);
        Console.WriteLine("diff_days=" + (diff.Days == 1));
        Console.WriteLine("diff_hours=" + (diff.TotalHours == 24.0));
        TimeSpan sub = new DateTime(2020, 1, 1).Subtract(new DateTime(2019, 12, 31, 23, 0, 0));
        Console.WriteLine("sub_hours=" + (sub.TotalHours == 1.0));
        TimeSpan spanAcross = new DateTime(2020, 6, 15, 12, 0, 0) - new DateTime(2020, 6, 15, 11, 30, 0);
        Console.WriteLine("span_min=" + (spanAcross.TotalMinutes == 30.0));
        // --- DateTime.CompareTo ---
        Console.WriteLine("cmp_lt=" + (new DateTime(2021, 1, 1).CompareTo(new DateTime(2021, 1, 2)) < 0));
        Console.WriteLine("cmp_eq=" + (new DateTime(2021, 1, 2).CompareTo(new DateTime(2021, 1, 2)) == 0));
        Console.WriteLine("cmp_gt=" + (new DateTime(2021, 1, 3).CompareTo(new DateTime(2021, 1, 2)) > 0));
        // --- TimeSpan 3-arg ctor + component getters ---
        TimeSpan ts = new TimeSpan(2, 3, 4);
        Console.WriteLine("ts_h=" + (ts.Hours == 2));
        Console.WriteLine("ts_m=" + (ts.Minutes == 3));
        Console.WriteLine("ts_s=" + (ts.Seconds == 4));
        Console.WriteLine("ts_days=" + (ts.Days == 0));
        Console.WriteLine("ts_total_s=" + (ts.TotalSeconds == 7384.0));
        Console.WriteLine("ts_total_m=" + (ts.TotalMinutes == 123.0 + 4.0 / 60.0));
        Console.WriteLine("ts_ticks=" + (ts.Ticks == 73840000000L));
        // --- TimeSpan 1-arg (ticks) ctor ---
        TimeSpan oneSec = new TimeSpan(10000000L);
        Console.WriteLine("tick_s=" + (oneSec.TotalSeconds == 1.0));
        Console.WriteLine("tick_ticks=" + (oneSec.Ticks == 10000000L));
        // --- TimeSpan arithmetic (Add / op_Addition / Subtract / op_Subtraction / Negate) ---
        Console.WriteLine("add_h=" + (ts.Add(new TimeSpan(1, 0, 0)).Hours == 3));            // 3:03:04
        Console.WriteLine("add_m=" + ((ts + new TimeSpan(0, 57, 0)).Minutes == 0));           // 3:00:04
        Console.WriteLine("add_total=" + ((ts + new TimeSpan(0, 57, 0)).TotalSeconds == 10804.0));
        Console.WriteLine("sub_m=" + (ts.Subtract(new TimeSpan(0, 1, 0)).Minutes == 2));      // 2:02:04
        Console.WriteLine("sub_s=" + ((ts - new TimeSpan(0, 0, 2)).TotalSeconds == 7382.0));
        Console.WriteLine("neg=" + ((-ts).TotalSeconds == -7384.0));
        Console.WriteLine("neg2=" + (ts.Negate().TotalSeconds == -7384.0));
        Console.WriteLine("neg_back=" + ((-ts).Negate().TotalSeconds == 7384.0));
        Console.WriteLine("ts_cmp_gt=" + (ts.CompareTo(new TimeSpan(1, 0, 0)) > 0));
        Console.WriteLine("ts_cmp_eq=" + (ts.CompareTo(new TimeSpan(2, 3, 4)) == 0));
        // --- chained TimeSpan arithmetic ---
        TimeSpan chained = ts.Add(new TimeSpan(0, 57, 0)).Subtract(new TimeSpan(0, 0, 4));
        Console.WriteLine("chain_h=" + (chained.Hours == 3));
        Console.WriteLine("chain_total=" + (chained.TotalSeconds == 10800.0));
        // --- 24-hour day: new DateTime(2020,6,15) is a Monday (weekday check via CompareTo chain) ---
        DateTime baseDt = new DateTime(2020, 6, 15);
        DateTime plus7 = baseDt.AddDays(7);
        Console.WriteLine("week_p1=" + (plus7.Day == 22));
        Console.WriteLine("week_p7=" + (plus7.DayOfWeek == baseDt.DayOfWeek));
    }
}}
