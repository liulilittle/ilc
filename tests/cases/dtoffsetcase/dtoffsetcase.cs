//------------------------------------------------------------------------------
// dtoffsetcase.cs -- System.DateTimeOffset e2e oracle.
// The transpiler's dtoffset domain (il2cpp_dtoffset.h) wires the ctors
// (DateTime, TimeSpan) and (int, int, int, int, int, int, TimeSpan), the
// accessors (DateTime/UtcDateTime/Offset/Year/Month/Day/Hour/Minute/Second),
// CompareTo/Equals and ToString. Every output is a deterministic bool;
// expected.txt is generated from a net48 reference run.
//------------------------------------------------------------------------------

using System;

namespace Cases { public static class DtoffsetCase {
    public static void Main() {
        // ---- 1. ctor(DateTime, TimeSpan) + accessors ----
        var dto = new DateTimeOffset(new DateTime(2000, 2, 29, 12, 0, 0), new TimeSpan(8, 0, 0));
        Console.WriteLine(dto.Year == 2000);
        Console.WriteLine(dto.Month == 2);
        Console.WriteLine(dto.Day == 29);
        Console.WriteLine(dto.Hour == 12);
        Console.WriteLine(dto.Minute == 0);
        Console.WriteLine(dto.Second == 0);
        var utc = dto.UtcDateTime;
        Console.WriteLine(utc.Hour == 4);
        var off = dto.Offset;
        Console.WriteLine(off.Hours == 8);

        // ---- 2. ctor_ymd matches the (DateTime, TimeSpan) ctor ----
        var dto2 = new DateTimeOffset(2000, 2, 29, 12, 0, 0, new TimeSpan(8, 0, 0));
        Console.WriteLine(dto2.Equals(dto));

        // ---- 3. negative offset ----
        var dto3 = new DateTimeOffset(new DateTime(2000, 2, 29, 12, 0, 0), new TimeSpan(-5, -30, 0));
        var utc3 = dto3.UtcDateTime;
        Console.WriteLine(utc3.Hour == 17);
        Console.WriteLine(utc3.Minute == 30);

        // ---- 4. same UTC instant, different offsets ----
        var dto4 = new DateTimeOffset(new DateTime(2000, 2, 29, 4, 0, 0), new TimeSpan(0, 0, 0));
        Console.WriteLine(dto4.Equals(dto));
        Console.WriteLine(dto4.CompareTo(dto) == 0);
    }
}}
