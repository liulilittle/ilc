using System;
namespace Cases { public static class TimeCase {
    public static void Main() {
        int year = DateTime.Now.Year;
        Console.WriteLine(year);                                 // e.g. 2026 (current year)
        Console.WriteLine(new DateTime(2020, 2, 28).AddDays(1).ToString("MM/dd/yyyy"));  // 02/29/2020 (leap)
        Console.WriteLine(new DateTime(2020, 2, 28).AddDays(2).ToString("MM/dd/yyyy"));  // 03/01/2020
        TimeSpan ts = new TimeSpan(2, 3, 4);
        Console.WriteLine(ts.TotalSeconds);                      // 7384
        Console.WriteLine(new TimeSpan(1, 0, 0).Add(new TimeSpan(0, 30, 0)).TotalMinutes); // 90
        TimeSpan diff = new DateTime(2020, 6, 15, 12, 0, 0) - new DateTime(2020, 6, 15, 11, 30, 0);
        Console.WriteLine(diff.TotalMinutes);                    // 30
        Console.WriteLine(new TimeSpan(1, 0, 0).Subtract(new TimeSpan(0, 40, 0)).TotalSeconds); // 1200
        Console.WriteLine(new DateTime(2020, 6, 15).DayOfWeek);  // Monday
        Console.WriteLine(DateTime.UtcNow.Year);                 // same year as Now.Year
    }
}}
