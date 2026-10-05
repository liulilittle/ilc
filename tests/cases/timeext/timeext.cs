using System;
using System.Globalization;
namespace Cases { public static class TimeExtCase {
    public static void Main() {
        // net48 oracle for the DateTime.ToString(format) icall subset.
        // All probe lines are the reference for the C++ runtime output.
        // NOTE: CultureInfo.CurrentCulture.Name is a transpiler literal fold
        // ("en-US") and the IFormatProvider argument of ToString(string,
        // provider) is dropped at emission, so current-culture patterns are
        // NOT representable here -- every probe passes InvariantCulture
        // explicitly (the net48 oracle then agrees with the runtime's
        // invariant output). See docs/audit/e2e-full-matrix7.md (timeext).
        DateTime dt = new DateTime(2020, 6, 15, 13, 45, 30);
        Console.WriteLine(dt.ToString("d", CultureInfo.InvariantCulture));     // 06/15/2020 (invariant short date)
        Console.WriteLine(dt.ToString("D", CultureInfo.InvariantCulture));     // Monday, 15 June 2020 (invariant long date)
        Console.WriteLine(dt.ToString("t", CultureInfo.InvariantCulture));     // 13:45
        Console.WriteLine(dt.ToString("T", CultureInfo.InvariantCulture));     // 13:45:30
        Console.WriteLine(dt.ToString("f", CultureInfo.InvariantCulture));     // Monday, 15 June 2020 13:45
        Console.WriteLine(dt.ToString("F", CultureInfo.InvariantCulture));     // Monday, 15 June 2020 13:45:30
        Console.WriteLine("[" + dt.ToString("", CultureInfo.InvariantCulture) + "]");                        // empty format == General "G" (net48 rule, invariant)
        Console.WriteLine(dt.ToString((string)null, CultureInfo.InvariantCulture));                          // null format == General "G": 06/15/2020 13:45:30
        Console.WriteLine(dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); // 2020-06-15 13:45:30
        Console.WriteLine(dt.ToString("yy/MM/dd hh:mm:ss", CultureInfo.InvariantCulture));   // 20/06/15 01:45:30
        Console.WriteLine(dt.ToString("MMM ddd", CultureInfo.InvariantCulture));   // Jun Mon
        Console.WriteLine(dt.ToString("MMMM dddd", CultureInfo.InvariantCulture)); // June Monday
        Console.WriteLine(dt.ToString("H h m s", CultureInfo.InvariantCulture));   // 13 1 45 30 (no-pad tokens)
        Console.WriteLine(dt.ToString("yyyy", CultureInfo.InvariantCulture));  // 2020
        Console.WriteLine(dt.ToString("yy", CultureInfo.InvariantCulture));    // 20 (last two digits)
        Console.WriteLine(dt.ToString("yyy", CultureInfo.InvariantCulture));   // 2020 (min 3)
        Console.WriteLine(dt.ToString("yyyyy", CultureInfo.InvariantCulture)); // 02020 (min 5)
        DateTime year5 = new DateTime(5, 6, 15);
        Console.WriteLine(year5.ToString("y", CultureInfo.InvariantCulture));  // 5 (no pad)
        Console.WriteLine(year5.ToString("yy", CultureInfo.InvariantCulture)); // 05
        Console.WriteLine(year5.ToString("yyy", CultureInfo.InvariantCulture));// 005
        Console.WriteLine(year5.ToString("yyyy", CultureInfo.InvariantCulture));// 0005
        Console.WriteLine(year5.ToString("yyyyy", CultureInfo.InvariantCulture));// 00005
        Console.WriteLine(dt.ToString("HH", CultureInfo.InvariantCulture));    // 13
        Console.WriteLine(dt.ToString("HHH", CultureInfo.InvariantCulture));   // token > 2: padding behavior probe
        Console.WriteLine(dt.ToString("hh", CultureInfo.InvariantCulture));    // 01 (12-hour pad)
        Console.WriteLine(dt.ToString("hhh", CultureInfo.InvariantCulture));   // probe
        Console.WriteLine(dt.ToString("mm", CultureInfo.InvariantCulture));    // 45
        Console.WriteLine(dt.ToString("mmm", CultureInfo.InvariantCulture));   // probe
        Console.WriteLine(dt.ToString("ss", CultureInfo.InvariantCulture));    // 30
        Console.WriteLine(dt.ToString("sss", CultureInfo.InvariantCulture));   // probe
        Console.WriteLine(dt.ToString("d/M", CultureInfo.InvariantCulture));   // 15/6 (custom single d/M = day/month no pad)
        Console.WriteLine(dt.ToString("dd", CultureInfo.InvariantCulture));    // 15
        Console.WriteLine(dt.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)); // Monday, June 15, 2020 (custom en-US style)
        try { Console.WriteLine(new DateTime(2020, 6, 15, 0, 5, 6).ToString("h", CultureInfo.InvariantCulture)); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); } // single "h": FormatException (not a standard specifier)
        Console.WriteLine(new DateTime(2020, 6, 15, 12, 0, 0).ToString("hh", CultureInfo.InvariantCulture)); // 12 (noon 12-hour)
        Console.WriteLine(new DateTime(2020, 6, 15, 0, 5, 6).ToString("h:mm", CultureInfo.InvariantCulture)); // 12:05 (mm = 2-digit minute)
        Console.WriteLine(new DateTime(2020, 6, 15, 0, 5, 6).ToString("h:m", CultureInfo.InvariantCulture));  // 12:5 (single m unpadded)
        for (int m = 1; m <= 12; m++) {
            Console.WriteLine(new DateTime(2020, m, 1).ToString("MMM", CultureInfo.InvariantCulture));
        }
        for (int m = 1; m <= 12; m++) {
            Console.WriteLine(new DateTime(2020, m, 1).ToString("MMMM", CultureInfo.InvariantCulture));
        }
        for (int d = 0; d <= 6; d++) {
            Console.WriteLine(new DateTime(2020, 6, 14 + d).ToString("ddd", CultureInfo.InvariantCulture));
        }
        for (int d = 0; d <= 6; d++) {
            Console.WriteLine(new DateTime(2020, 6, 14 + d).ToString("dddd", CultureInfo.InvariantCulture));
        }
        // Out-of-scope divergence probes (documented in the report):
        Console.WriteLine(dt.ToString("m", CultureInfo.InvariantCulture));     // standard month-day: June 15
        Console.WriteLine(dt.ToString("y", CultureInfo.InvariantCulture));     // standard year-month: June 2020
        Console.WriteLine(dt.ToString("s", CultureInfo.InvariantCulture));     // standard sortable: 2020-06-15T13:45:30
        Console.WriteLine(dt.ToString("tt", CultureInfo.InvariantCulture));    // custom AM/PM designator: PM
        Console.WriteLine(dt.ToString("g", CultureInfo.InvariantCulture));     // standard short general: 06/15/2020 13:45
        try { Console.WriteLine(dt.ToString("H")); } catch (Exception e) { Console.WriteLine(e.GetType().Name); } // single non-standard letter: FormatException
        try { Console.WriteLine(dt.ToString("q")); } catch (Exception e) { Console.WriteLine(e.GetType().Name); } // unknown single letter: FormatException
    }
}}
