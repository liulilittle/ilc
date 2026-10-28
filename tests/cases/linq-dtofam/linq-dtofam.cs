//------------------------------------------------------------------------------
// linq-dtofam.cs -- System.DateTimeOffset in the LINQ value families (delta 2/1).
//
// Covered surface (net48 Enumerable, all statically resolvable):
//   * Enumerable.ToList<DateTimeOffset>  -> List<DateTimeOffset> (boxed listobj)
//   * Enumerable.Select<int,DateTimeOffset>        -> DateTimeOffset[]
//   * Enumerable.Select<DateTime,DateTimeOffset>   -> DateTimeOffset[]
//   * Enumerable.Min<DateTime> / Max<Guid>         -> Comparer<T>.Default
//   * Enumerable.Min<TimeSpan> / Max<TimeSpan>
// Every printed value is deterministic (no clock, no culture-dependent text).
// The DateTimeOffset SOURCE arrays are produced by Select (the array-literal
// dtoffset element access is a separate wiring item), so this case exercises
// the LINQ carriers only.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class LinqDtoFam
    {
        public static void Main()
        {
            // ===== 1. Select<int,DateTimeOffset> then ToList<DateTimeOffset> =====
            int[] a = { 1, 2, 3 };
            DateTimeOffset[] fromInt = a
                .Select(x => new DateTimeOffset(2020, 1, x, 0, 0, 0, TimeSpan.Zero))
                .ToArray();
            Console.WriteLine("seli4_len=" + (fromInt.Length == 3));

            List<DateTimeOffset> list = fromInt.ToList();
            Console.WriteLine("dto_count=" + (list.Count == 3));
            Console.WriteLine("dto_0=" + (list[0].Year == 2020));
            Console.WriteLine("dto_1=" + (list[1].Day == 2));
            Console.WriteLine("dto_2=" + (list[2].Day == 3));

            // ===== 2. Select<DateTime,DateTimeOffset> =====
            DateTime[] dts =
            {
                new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Unspecified),
                new DateTime(2019, 12, 31, 23, 59, 59, DateTimeKind.Unspecified),
            };
            DateTimeOffset[] mapped = dts
                .Select(x => new DateTimeOffset(x, new TimeSpan(2, 0, 0)))
                .ToArray();
            Console.WriteLine("seldt_len=" + (mapped.Length == 2));

            List<DateTimeOffset> mappedList = mapped.ToList();
            Console.WriteLine("seldt_list=" + (mappedList.Count == 2));
            Console.WriteLine("seldt_year=" + (mappedList[0].Year == 2021));
            Console.WriteLine("seldt_hour=" + (mappedList[1].Hour == 23));
            Console.WriteLine("seldt_off=" + (mappedList[0].Offset.Hours == 2));
            Console.WriteLine("seldt_list1=" + (mappedList[1].Year == 2019));

            // ===== 3. Min / Max over DateTime / TimeSpan / Guid =====
            DateTime[] dtSource =
            {
                new DateTime(2021, 1, 1),
                new DateTime(2019, 5, 5),
                new DateTime(2020, 3, 3),
            };
            Console.WriteLine("min_dt=" + (dtSource.Min().Year == 2019));
            Console.WriteLine("max_dt=" + (dtSource.Max().Year == 2021));

            TimeSpan[] tsSource =
            {
                new TimeSpan(3, 0, 0),
                new TimeSpan(1, 30, 0),
                new TimeSpan(2, 0, 0),
            };
            Console.WriteLine("min_ts=" + (tsSource.Min().TotalMinutes == 90.0));
            Console.WriteLine("max_ts=" + (tsSource.Max().TotalMinutes == 180.0));

            Guid g1 = new Guid("0aa79e57-1c2b-4d3e-9f40-112233445566");
            Guid g2 = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            Guid g3 = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
            Guid[] gSource = { g1, g3, g2 };
            Console.WriteLine("min_guid=" + (gSource.Min() == g3));
            Console.WriteLine("max_guid=" + (gSource.Max() == g2));

            Console.WriteLine("done");
        }
    }
}

