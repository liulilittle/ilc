using System;
namespace Cases { public static class DateTimeChainCase {
    public static void Main() {
        // DateTime chain: ctor -> AddDays/AddHours -> subtract -> TimeSpan -> Ticks -> ToString.
        // Bool probes avoid culture-dependent output; ToString uses custom invariant formats only.
        DateTime dt = new DateTime(2020, 6, 15, 12, 30, 45);
        DateTime plus = dt.AddDays(3).AddHours(2);          // 2020-06-18 14:30:45
        Console.WriteLine(plus.Year == 2020);               // True
        Console.WriteLine(plus.Month == 6);                 // True
        Console.WriteLine(plus.Day == 18);                  // True (15 + 3 days)
        Console.WriteLine(plus.Hour == 14);                 // True (12 + 2 hours)
        Console.WriteLine(plus.Minute == 30);               // True (unchanged)
        Console.WriteLine(plus.Second == 45);               // True (unchanged)
        // wraparound probes inside the chain
        Console.WriteLine(new DateTime(2020, 2, 28).AddDays(1).Day == 29);               // True (leap year)
        Console.WriteLine(new DateTime(2020, 2, 28).AddDays(2).Month == 3);              // True (wrap into March)
        Console.WriteLine(new DateTime(2020, 6, 15, 23, 30, 0).AddHours(2).Day == 16);   // True (crosses midnight)
        Console.WriteLine(new DateTime(2020, 6, 15, 23, 30, 0).AddHours(2).Hour == 1);   // True
        Console.WriteLine(new DateTime(1999, 12, 31, 23, 59, 59).AddHours(1).Year == 2000);   // True (millenium rollover)
        Console.WriteLine(new DateTime(1999, 12, 31, 23, 59, 59).AddHours(1).Minute == 59);   // True
        // subtraction: DateTime - DateTime -> TimeSpan (3 days 2 hours exactly)
        TimeSpan span = plus - dt;
        Console.WriteLine(span.Ticks == 2664000000000L);    // True (3*86400 + 2*3600 seconds)
        Console.WriteLine(span.Ticks);                      // 2664000000000
        Console.WriteLine(span.TotalSeconds == 266400);     // True
        Console.WriteLine(span.Days == 3);                  // True
        Console.WriteLine(span.Hours == 2);                 // True
        Console.WriteLine(span.Minutes == 0);               // True
        Console.WriteLine(span.Seconds == 0);               // True
        // Subtract instance method mirrors the operator
        TimeSpan viaMethod = dt.Subtract(new DateTime(2020, 6, 12, 10, 30, 45));
        Console.WriteLine(viaMethod.CompareTo(span) == 0);  // True (same 3d 2h delta)
        Console.WriteLine(dt.CompareTo(plus) < 0);          // True (dt is earlier)
        // TimeSpan ctor + component accessors
        Console.WriteLine(new TimeSpan(2, 3, 4).Ticks);     // 73840000000 (2h 3m 4s)
        Console.WriteLine(new TimeSpan(2, 3, 4).Hours == 2);    // True (2h 3m 4s -> hours = 2)
        Console.WriteLine(new TimeSpan(3, 2, 0).Ticks == 109200000000L);  // True (3h 2m)
        // ToString: existence probe + custom invariant formats only
        Console.WriteLine(dt.ToString().Length > 0);        // True (plain ToString exists)
        Console.WriteLine(dt.ToString("yyyy-MM-dd"));       // 2020-06-15
        Console.WriteLine(dt.ToString("yyyy-MM-dd HH:mm:ss"));  // 2020-06-15 12:30:45
        Console.WriteLine(plus.ToString("yyyy-MM-dd"));     // 2020-06-18
        Console.WriteLine(new DateTime(1999, 12, 31, 23, 59, 59).AddHours(1).ToString("yyyy-MM-dd HH:mm:ss")); // 2000-01-01 00:59:59
    }
}}
