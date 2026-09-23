using System;
namespace Cases { public static class Prog {
    static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    public static void Main() {
        Console.WriteLine("-- shard dt001c timespan start --");
        TimeSpan t = TimeSpan.FromTicks(1234567890123L);
        Console.WriteLine("[ts.ticks] " + t.Ticks);
        Console.WriteLine("[ts.fromticks.seed] 1234567890123");
        I("ts.days", t.Days); I("ts.hours", t.Hours); I("ts.minutes", t.Minutes);
        I("ts.seconds", t.Seconds); I("ts.milliseconds", t.Milliseconds);
        L("ts.totalms", (long)t.TotalMilliseconds);
        L("ts.totalsec", (long)t.TotalSeconds);
        L("ts.totalmin", (long)t.TotalMinutes);
        L("ts.totalhours", (long)t.TotalHours);
        L("ts.totaldays", (long)t.TotalDays);
        L("ts.fromms1500_5", TimeSpan.FromMilliseconds(1500.5).Ticks);
        L("ts.fromsec1_5", TimeSpan.FromSeconds(1.5).Ticks);
        L("ts.fromdays.m1", TimeSpan.FromDays(-1).Ticks);
        L("ts.negate", t.Negate().Ticks);
        L("ts.duration", TimeSpan.FromDays(-1).Duration().Ticks);
        TimeSpan s2 = TimeSpan.FromTicks(t.Ticks);
        TimeSpan s3 = TimeSpan.FromTicks(t.Ticks + 1);
        I("ts.equals.same", t.Equals(s2) ? 1 : 0);
        I("ts.equals.diff", t.Equals(s3) ? 1 : 0);
        I("ts.op.eq", (t == s2) ? 1 : 0);
        I("ts.op.ne", (t != s3) ? 1 : 0);
        I("ts.op.lt", (t < s3) ? 1 : 0);
        I("ts.op.gt", (s3 > t) ? 1 : 0);
        I("ts.op.le", (t <= s2) ? 1 : 0);
        I("ts.op.ge", (s3 >= t) ? 1 : 0);
        L("ts.hash", t.GetHashCode());
        L("ts.add", (t + s3).Ticks);
        L("ts.sub", (t - s3).Ticks);
        I("ts.compare", TimeSpan.Compare(t, s3));
        L("ts.parse", TimeSpan.Parse("1.02:03:04").Ticks);
        try { L("ts.parse.bad", TimeSpan.Parse("not a span").Ticks); }
        catch (FormatException) { X("ts.parse.bad", "FormatException"); }
        Console.WriteLine("-- shard dt001c timespan end --");
    }
}}
