//------------------------------------------------------------------------------
// calendarcase.cs -- System.Globalization.GregorianCalendar e2e oracle.
// The transpiler's calendar domain (il2cpp_calendar.h) wires the int-based
// queries (IsLeapYear / GetDaysInMonth / GetDaysInYear / GetMonth /
// GetDayOfYear / GetDayOfMonth) and the DateTime-based queries (GetYear /
// GetMonth / GetDayOfMonth / GetDayOfYear / GetDayOfWeek). Every output is a
// deterministic bool; expected.txt is generated from a net48 reference run.
//------------------------------------------------------------------------------

using System;
using System.Globalization;

namespace Cases { public static class CalendarCase {
    public static void Main() {
        var cal = new GregorianCalendar();

        // ---- 1. int-based queries ----
        Console.WriteLine(cal.IsLeapYear(2000));
        Console.WriteLine(cal.IsLeapYear(1900));
        Console.WriteLine(cal.IsLeapYear(2004));
        Console.WriteLine(cal.GetDaysInMonth(2000, 2) == 29);
        Console.WriteLine(cal.GetDaysInMonth(1900, 2) == 28);
        Console.WriteLine(cal.GetDaysInYear(2000) == 366);
        Console.WriteLine(cal.GetDaysInYear(1900) == 365);

        // ---- 2. DateTime-based queries ----
        var d = new DateTime(2000, 2, 29);
        Console.WriteLine(cal.GetYear(d) == 2000);
        Console.WriteLine(cal.GetMonth(d) == 2);
        Console.WriteLine(cal.GetDayOfMonth(d) == 29);
        Console.WriteLine(cal.GetDayOfYear(d) == 60);
        Console.WriteLine((int)cal.GetDayOfWeek(d) == 2);
        Console.WriteLine((int)cal.GetDayOfWeek(new DateTime(2000, 1, 1)) == 6);
        Console.WriteLine((int)cal.GetDayOfWeek(new DateTime(1970, 1, 1)) == 4);
        Console.WriteLine((int)cal.GetDayOfWeek(new DateTime(2024, 1, 1)) == 1);
        Console.WriteLine((int)cal.GetDayOfWeek(new DateTime(1, 1, 1)) == 1);
    }
}}
