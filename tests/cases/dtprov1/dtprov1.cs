using System;
using System.Globalization;

namespace Cases { public static class DtProv1 {
    public static void Main() {
        var inv = CultureInfo.InvariantCulture;
        // 8-arg ctor (y,mo,d,h,mi,s,ms,kind).
        var dt = new DateTime(2020, 6, 15, 12, 30, 45, 123, DateTimeKind.Utc);
        Console.WriteLine(dt.Year == 2020);
        Console.WriteLine(dt.Month == 6);
        Console.WriteLine(dt.Day == 15);
        Console.WriteLine(dt.Hour == 12);
        Console.WriteLine(dt.Minute == 30);
        Console.WriteLine(dt.Second == 45);
        Console.WriteLine(dt.Millisecond == 123);
        Console.WriteLine(dt.Kind == DateTimeKind.Utc);
        Console.WriteLine(dt.Ticks == new DateTime(2020, 6, 15, 12, 30, 45, 123).Ticks);
        // Provider-only ToString == ("G", provider).
        Console.WriteLine(dt.ToString(inv) == dt.ToString("G", inv));
        Console.WriteLine(dt.ToString(inv) == "06/15/2020 12:30:45");
        Console.WriteLine(dt.ToString((IFormatProvider)null) == dt.ToString());
        var local = new DateTime(2021, 1, 2, 3, 4, 5, 6, DateTimeKind.Local);
        Console.WriteLine(local.Millisecond == 6);
        Console.WriteLine(local.Kind == DateTimeKind.Local);
        Console.WriteLine(local.ToString(inv) == local.ToString("G", inv));
        var unspec = new DateTime(2019, 12, 31, 23, 59, 58, 999, DateTimeKind.Unspecified);
        Console.WriteLine(unspec.Millisecond == 999 && unspec.Kind == DateTimeKind.Unspecified);
        Console.WriteLine(unspec.ToString(inv) == "12/31/2019 23:59:58");
    }
}}
