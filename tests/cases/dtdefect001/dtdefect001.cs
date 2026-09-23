using System;
namespace Cases { public static class Prog {
    static DateTimeOffset RetCtor() { return new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero); }
    static void MixKind(DateTimeOffset o) {
        int k = (int)o.UtcDateTime.Kind;
        Console.WriteLine("[mix.kind] " + k);
    }
    public static void Main() {
        Console.WriteLine("-- defect probe --");
        DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 1, 1, 0, 0, 0), TimeSpan.Zero);
        DateTimeOffset r = RetCtor();
        Console.WriteLine("[ret.offset.ticks] " + r.Offset.Ticks);
        MixKind(o);
        Console.WriteLine("-- defect end --");
    }
}}
