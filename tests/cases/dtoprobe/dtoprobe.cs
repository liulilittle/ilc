using System;
namespace Cases { public static class Prog {
    public static void Main() {
        DateTimeOffset mn = DateTimeOffset.MinValue;
        DateTimeOffset mx = DateTimeOffset.MaxValue;
        DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 1, 1, 0, 0, 0), TimeSpan.Zero);
        Console.WriteLine("[mn] " + mn.Year);
        Console.WriteLine("[mx] " + mx.Year);
        Console.WriteLine("[hc] " + o.GetHashCode());
        Console.WriteLine("[ts] " + o.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}}
