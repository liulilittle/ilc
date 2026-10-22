using System;

namespace Cases { public static class BclEnum1 {
    public static void Main() {
        var d = new DateTime(2020, 3, 4);
        Console.WriteLine(d.DayOfWeek.ToString());
        Console.WriteLine(DayOfWeek.Monday.ToString());
        Console.WriteLine(DayOfWeek.Sunday.ToString());
        Console.WriteLine(((int)DayOfWeek.Wednesday).ToString());
        Console.WriteLine(Enum.GetName(typeof(DayOfWeek), 3));

        DayOfWeek parsed = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), "Friday");
        Console.WriteLine(parsed.ToString());
        Console.WriteLine(parsed == DayOfWeek.Friday);

        Console.WriteLine("x" + DayOfWeek.Thursday);
        Console.WriteLine(DayOfWeek.Saturday.GetHashCode());
        Console.WriteLine(DayOfWeek.Saturday.CompareTo(DayOfWeek.Monday));
        Console.WriteLine(Enum.IsDefined(typeof(DayOfWeek), 3));
    }
}}
