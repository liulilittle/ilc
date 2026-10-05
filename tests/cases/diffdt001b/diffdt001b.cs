using System;
namespace Cases { public static class Prog {
    static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    public static void Main() {
        Console.WriteLine("-- shard dt001b filetime start --");
        DateTime u = new DateTime(637185764967890000L, DateTimeKind.Utc);
        Console.WriteLine("[dt.filetime.seed] " + u.Ticks);
        Console.WriteLine("[dt.filetime.utc.filetimeutc] " + u.ToFileTimeUtc());
        Console.WriteLine("[dt.filetime.utc.filetime] " + u.ToFileTime());
        Console.WriteLine("[dt.filetime.fromutc] " + DateTime.FromFileTimeUtc(132223104000000000L).Ticks);
        Console.WriteLine("[dt.filetime.fromlocal] " + DateTime.FromFileTime(132223104000000000L).Ticks);
        Console.WriteLine("[dt.filetime.roundtrip] " + DateTime.FromFileTimeUtc(u.ToFileTimeUtc()).Ticks);
        try { Console.WriteLine("[dt.filetime.neg] " + DateTime.FromFileTimeUtc(-1).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.filetime.neg", "ArgumentOutOfRangeException"); }
        Console.WriteLine("-- shard dt001b filetime end --");
    }
}}
