using System;

namespace E2eDtProbe
{
    public static class Program
    {
        public static int Main()
        {
            int failures = 0;

            // 1. IsLeapYear: 2024 is a leap year, 2023 is not
            bool leap = DateTime.IsLeapYear(2024) && !DateTime.IsLeapYear(2023);
            if (leap) Console.WriteLine("PASS IsLeapYear(2024)=true IsLeapYear(2023)=false");
            else { Console.WriteLine("FAIL IsLeapYear(2024)=" + DateTime.IsLeapYear(2024) + " IsLeapYear(2023)=" + DateTime.IsLeapYear(2023)); failures++; }

            // 2. DaysInMonth: Feb 2024 has 29 days
            int dim = DateTime.DaysInMonth(2024, 2);
            if (dim == 29) Console.WriteLine("PASS DaysInMonth(2024,2)=29");
            else { Console.WriteLine("FAIL DaysInMonth(2024,2)=" + dim); failures++; }

            // 3. AddDays across month boundary: Jan 31 + 1 day = Feb 1
            DateTime feb = new DateTime(2024, 1, 31).AddDays(1);
            if (feb.Year == 2024 && feb.Month == 2 && feb.Day == 1) Console.WriteLine("PASS Jan31+1d=2024-02-01");
            else { Console.WriteLine("FAIL Jan31+1d=" + feb.Year + "-" + feb.Month + "-" + feb.Day); failures++; }

            // 4. TimeSpan.FromMinutes(90).TotalHours == 1.5
            double hours = TimeSpan.FromMinutes(90).TotalHours;
            if (hours == 1.5) Console.WriteLine("PASS FromMinutes(90).TotalHours=1.5");
            else { Console.WriteLine("FAIL FromMinutes(90).TotalHours=" + hours); failures++; }

            // 5. Ticks arithmetic: consecutive dates differ by exactly
            //    TicksPerDay, and a ticks-built TimeSpan totals 24 hours
            DateTime d0 = new DateTime(2024, 1, 1);
            long delta = d0.AddDays(1).Ticks - d0.Ticks;
            double dayHours = new TimeSpan(delta).TotalHours;
            if (delta == TimeSpan.TicksPerDay && dayHours == 24.0)
                Console.WriteLine("PASS Ticks dayDelta=" + delta + " dayHours=" + dayHours);
            else { Console.WriteLine("FAIL Ticks dayDelta=" + delta + " expected " + TimeSpan.TicksPerDay + " dayHours=" + dayHours); failures++; }

            if (failures == 0)
            {
                Console.WriteLine("ALL-PASS");
                return 0;
            }
            Console.WriteLine("ALL-FAIL failures=" + failures);
            return 1;
        }
    }
}
