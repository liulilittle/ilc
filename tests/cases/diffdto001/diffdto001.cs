using System;
namespace Cases { public static class Prog {
    static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    public static void Main() {
        Console.WriteLine("-- shard dto001 start --");
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); L("dto.offset.ticks", o.Offset.Ticks); } catch (Exception) { G("dto.offset.ticks"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); L("dto.datetime.ticks", o.DateTime.Ticks); } catch (Exception) { G("dto.datetime.ticks"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); L("dto.utcdatetime.ticks", o.UtcDateTime.Ticks); } catch (Exception) { G("dto.utcdatetime.ticks"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); I("dto.utcdatetime.kind", (int)o.UtcDateTime.Kind); } catch (Exception) { G("dto.utcdatetime.kind"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); I("dto.year", o.Year); I("dto.month", o.Month); I("dto.day", o.Day); } catch (Exception) { G("dto.ymd"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); I("dto.hour", o.Hour); I("dto.minute", o.Minute); I("dto.second", o.Second); } catch (Exception) { G("dto.hms"); }
        try { DateTimeOffset z = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.FromHours(2)); L("dto.z.offset.ticks", z.Offset.Ticks); } catch (Exception) { G("dto.z.offset.ticks"); }
        try { DateTimeOffset z = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.FromHours(2)); I("dto.z.hour", z.Hour); I("dto.z.minute", z.Minute); } catch (Exception) { G("dto.z.hms"); }
        try { DateTimeOffset z = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.FromHours(2)); L("dto.z.utcdatetime.ticks", z.UtcDateTime.Ticks); } catch (Exception) { G("dto.z.utcdatetime.ticks"); }
        try { DateTimeOffset z = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.FromHours(2)); L("dto.z.datetime.ticks", z.DateTime.Ticks); } catch (Exception) { G("dto.z.datetime.ticks"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); DateTimeOffset same = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); I("dto.equals.same", o.Equals(same) ? 1 : 0); } catch (Exception) { G("dto.equals.same"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); DateTimeOffset same = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); I("dto.compareto.same", o.CompareTo(same)); } catch (Exception) { G("dto.compareto.same"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); DateTimeOffset si = new DateTimeOffset(new DateTime(2020, 2, 29, 14, 0, 0), TimeSpan.FromHours(2)); I("dto.equals.sameinstant", o.Equals(si) ? 1 : 0); } catch (Exception) { G("dto.equals.sameinstant"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); DateTimeOffset si = new DateTimeOffset(new DateTime(2020, 2, 29, 14, 0, 0), TimeSpan.FromHours(2)); I("dto.compareto.sameinstant", o.CompareTo(si)); } catch (Exception) { G("dto.compareto.sameinstant"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); DateTimeOffset later = new DateTimeOffset(new DateTime(2020, 3, 1, 12, 0, 0), TimeSpan.Zero); I("dto.equals.diff", o.Equals(later) ? 1 : 0); I("dto.compareto.lt", o.CompareTo(later)); I("dto.compareto.gt", later.CompareTo(o)); } catch (Exception) { G("dto.compareto.order"); }
        try { DateTimeOffset z = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.FromHours(2)); S("dto.tostring", z.ToString()); } catch (Exception) { G("dto.tostring"); }
        try { DateTimeOffset o = new DateTimeOffset(new DateTime(2020, 2, 29, 12, 0, 0), TimeSpan.Zero); S("dto.tostring.zero", o.ToString()); } catch (Exception) { G("dto.tostring.zero"); }
        try { DateTimeOffset mx = new DateTimeOffset(new DateTime(2020, 1, 1, 0, 0, 0), TimeSpan.FromHours(14)); L("dto.max14.offset.ticks", mx.Offset.Ticks); } catch (Exception) { G("dto.max14.offset.ticks"); }
        try { DateTimeOffset mn = new DateTimeOffset(new DateTime(2020, 1, 1, 0, 0, 0), TimeSpan.FromHours(-14)); L("dto.min14.offset.ticks", mn.Offset.Ticks); } catch (Exception) { G("dto.min14.offset.ticks"); }
        try { DateTimeOffset mx = new DateTimeOffset(new DateTime(2020, 1, 1, 0, 0, 0), TimeSpan.FromHours(14)); L("dto.max14.utcdatetime.ticks", mx.UtcDateTime.Ticks); } catch (Exception) { G("dto.max14.utcdatetime.ticks"); }
        try { DateTimeOffset mn = new DateTimeOffset(new DateTime(2020, 1, 1, 0, 0, 0), TimeSpan.FromHours(-14)); L("dto.min14.utcdatetime.ticks", mn.UtcDateTime.Ticks); } catch (Exception) { G("dto.min14.utcdatetime.ticks"); }
        try { I("dto.utcnow.offset0", (DateTimeOffset.UtcNow.Offset.Ticks == 0) ? 1 : 0); } catch (Exception) { G("dto.utcnow.offset0"); }
        try { int y = DateTimeOffset.UtcNow.Year; I("dto.utcnow.year.sane", (y >= 2020 && y <= 2100) ? 1 : 0); } catch (Exception) { G("dto.utcnow.year.sane"); }
        try { I("dto.utcnow.utc.kind", (int)DateTimeOffset.UtcNow.UtcDateTime.Kind); } catch (Exception) { G("dto.utcnow.utc.kind"); }
        Console.WriteLine("-- shard dto001 end --");
    }
}}
