using System;
namespace Cases { public static class Prog {

    public static void Main() { int e = (1 == 1) ? 1 : 0; DateTime d = new DateTime(2020, 1, 1); DateTimeOffset a = new DateTimeOffset(d, TimeSpan.Zero); Console.WriteLine("[v] " + (e + a.Offset.Ticks)); }
}}