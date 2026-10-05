using System;
namespace Cases { public static class Prog {
    static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static DateTime D2(long ticks) { return new DateTime(ticks, DateTimeKind.Utc); }
    static DateTime D6() { return new DateTime(2020, 2, 29, 12, 34, 56, DateTimeKind.Utc); }
    static void Components() {
        DateTime d = D6();
        L("dt.ticks", d.Ticks);
        I("dt.year", d.Year); I("dt.month", d.Month); I("dt.day", d.Day);
        I("dt.hour", d.Hour); I("dt.minute", d.Minute); I("dt.second", d.Second);
        I("dt.dayofweek", (int)d.DayOfWeek);
        try { I("dt.millisecond", d.Millisecond); } catch (Exception) { G("dt.millisecond"); }
        try { I("dt.dayofyear", d.DayOfYear); } catch (Exception) { G("dt.dayofyear"); }
        try { I("dt.kind", (int)d.Kind); } catch (Exception) { G("dt.kind"); }
        try { L("dt.date.ticks", d.Date.Ticks); } catch (Exception) { G("dt.date.ticks"); }
        try { L("dt.timeofday.ticks", d.TimeOfDay.Ticks); } catch (Exception) { G("dt.timeofday.ticks"); }
    }
    static void HashProbe() { L("dt.hash", D6().GetHashCode()); }
    static void AddCustom() {
        DateTime d = D6();
        try { L("dt.addmonths.p1", d.AddMonths(1).Ticks); } catch (Exception) { G("dt.addmonths.p1"); }
        try { L("dt.addmonths.p12", d.AddMonths(12).Ticks); } catch (Exception) { G("dt.addmonths.p12"); }
        try { L("dt.addmonths.m13", d.AddMonths(-13).Ticks); } catch (Exception) { G("dt.addmonths.m13"); }
        try { L("dt.addyears.p1", d.AddYears(1).Ticks); } catch (Exception) { G("dt.addyears.p1"); }
        try { L("dt.addyears.m1", d.AddYears(-1).Ticks); } catch (Exception) { G("dt.addyears.m1"); }
        try { L("dt.addyears.p4", d.AddYears(4).Ticks); } catch (Exception) { G("dt.addyears.p4"); }
        try { L("dt.addseconds.p1", d.AddSeconds(1).Ticks); } catch (Exception) { G("dt.addseconds.p1"); }
        try { L("dt.addseconds.m1", d.AddSeconds(-1).Ticks); } catch (Exception) { G("dt.addseconds.m1"); }
        try { L("dt.addmilliseconds.p1500", d.AddMilliseconds(1500).Ticks); } catch (Exception) { G("dt.addmilliseconds.p1500"); }
        try { L("dt.addmilliseconds.m1", d.AddMilliseconds(-1).Ticks); } catch (Exception) { G("dt.addmilliseconds.m1"); }
        try { L("dt.adddays.p1", d.AddDays(1).Ticks); } catch (Exception) { G("dt.adddays.p1"); }
        try { L("dt.addhours.p1", d.AddHours(1).Ticks); } catch (Exception) { G("dt.addhours.p1"); }
        try { L("dt.addminutes.p1", d.AddMinutes(1).Ticks); } catch (Exception) { G("dt.addminutes.p1"); }
        try {
            try { L("dt.addmonths.overflow", new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).Ticks); }
            catch (ArgumentOutOfRangeException) { X("dt.addmonths.overflow", "ArgumentOutOfRangeException"); }
        } catch (Exception) { G("dt.addmonths.overflow"); }
    }
    static void Cmp() {
        DateTime a = D6();
        DateTime b = D2(a.Ticks);
        DateTime c;
        try { c = a.AddDays(1); } catch (Exception) { c = a; G("dt.cmp.adddays"); }
        I("dt.equals.same", a.Equals(b) ? 1 : 0);
        I("dt.equals.diff", a.Equals(c) ? 1 : 0);
        I("dt.op.eq", (a == b) ? 1 : 0);
        I("dt.op.ne", (a != c) ? 1 : 0);
        I("dt.op.lt", (a < c) ? 1 : 0);
        I("dt.op.gt", (c > a) ? 1 : 0);
        I("dt.op.le", (a <= b) ? 1 : 0);
        I("dt.op.ge", (c >= a) ? 1 : 0);
        I("dt.compareto", a.CompareTo(c));
        try {
            TimeSpan sub = c.Subtract(a);
            L("dt.subtract.ticks", sub.Ticks);
        } catch (Exception) { G("dt.subtract.ticks"); }
    }
    static void FileTimeProbe() { DateTime u0 = D2(637185764967890000L); L("dt.filetime.utc.filetime", u0.ToFileTime()); }
    static void FileTime() {
        DateTime u = D2(637185764967890000L);
        try { L("dt.filetime.utc.filetimeutc", u.ToFileTimeUtc()); } catch (Exception) { G("dt.filetime.utc.filetimeutc"); }
        try { L("dt.filetime.fromutc", DateTime.FromFileTimeUtc(132223104000000000L).Ticks); } catch (Exception) { G("dt.filetime.fromutc"); }
        try { L("dt.filetime.fromlocal", DateTime.FromFileTime(132223104000000000L).Ticks); } catch (Exception) { G("dt.filetime.fromlocal"); }
        try { L("dt.filetime.roundtrip", DateTime.FromFileTimeUtc(u.ToFileTimeUtc()).Ticks); } catch (Exception) { G("dt.filetime.roundtrip"); }
        try {
            try { L("dt.filetime.neg", DateTime.FromFileTimeUtc(-1).Ticks); }
            catch (ArgumentOutOfRangeException) { X("dt.filetime.neg", "ArgumentOutOfRangeException"); }
        } catch (Exception) { G("dt.filetime.neg"); }
    }
    static void Spans() {
        TimeSpan t = TimeSpan.FromTicks(1234567890123L);
        L("ts.ticks", t.Ticks);
        I("ts.days", t.Days); I("ts.hours", t.Hours); I("ts.minutes", t.Minutes);
        I("ts.seconds", t.Seconds); I("ts.milliseconds", t.Milliseconds);
        L("ts.totalms", (long)t.TotalMilliseconds);
        L("ts.totalsec", (long)t.TotalSeconds);
        L("ts.totalmin", (long)t.TotalMinutes);
        L("ts.totalhours", (long)t.TotalHours);
        L("ts.totaldays", (long)t.TotalDays);
        try { L("ts.fromms1500_5", TimeSpan.FromMilliseconds(1500.5).Ticks); } catch (Exception) { G("ts.fromms1500_5"); }
        try { L("ts.fromsec1_5", TimeSpan.FromSeconds(1.5).Ticks); } catch (Exception) { G("ts.fromsec1_5"); }
        try { L("ts.fromdays.m1", TimeSpan.FromDays(-1).Ticks); } catch (Exception) { G("ts.fromdays.m1"); }
        try { L("ts.negate", t.Negate().Ticks); } catch (Exception) { G("ts.negate"); }
        try { L("ts.duration", TimeSpan.FromDays(-1).Duration().Ticks); } catch (Exception) { G("ts.duration"); }
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
        try { L("ts.hash", t.GetHashCode()); } catch (Exception) { G("ts.hash"); }
        try { L("ts.add", (t + s3).Ticks); } catch (Exception) { G("ts.add"); }
        try { L("ts.sub", (t - s3).Ticks); } catch (Exception) { G("ts.sub"); }
        try { I("ts.compare", TimeSpan.Compare(t, s3)); } catch (Exception) { G("ts.compare"); }
        try { L("ts.parse", TimeSpan.Parse("1.02:03:04").Ticks); } catch (Exception) { G("ts.parse"); }
        try {
            try { L("ts.parse.bad", TimeSpan.Parse("not a span").Ticks); }
            catch (FormatException) { X("ts.parse.bad", "FormatException"); }
        } catch (Exception) { G("ts.parse.bad"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard dt001 start --");
        Components(); AddCustom(); Cmp();
        try { HashProbe(); } catch (Exception) { G("dt.hash"); }
        Console.WriteLine("-- shard dt001 end --");
    }
}}
