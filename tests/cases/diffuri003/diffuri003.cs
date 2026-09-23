using System;
namespace Cases { public static class Prog {
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void IPv6A() { S("uri3.ipv6.loop", new Uri("http://[::1]:8080/a").ToString()); }
    static void IPv6B() { S("uri3.ipv6.glob", new Uri("http://[2001:db8::1]/p").ToString()); }
    static void FileDrive() { S("uri3.file.drive", new Uri("file:///C:/Temp/File.txt").ToString()); }
    static void FileUnc() { S("uri3.file.unc", new Uri("file://server/share/x").ToString()); }
    static void EmptyQ() { S("uri3.empty.q", new Uri("http://h/a?").ToString()); }
    static void EmptyF() { S("uri3.empty.f", new Uri("http://h/a#").ToString()); }
    static void EmptyQF() { S("uri3.empty.qf", new Uri("http://h/a?#").ToString()); }
    static void Pct2f() { S("uri3.pct.2f", new Uri("http://h/%2f").ToString()); }
    static void ZeroPort() { S("uri3.port.zero", new Uri("http://h:0080/a").ToString()); }
    static void MixedCase() { S("uri3.case.mixed", new Uri("HtTp://ExAmPlE.CoM/PaTh").ToString()); }
    static void LongPath() {
        string p = "http://h/";
        for (int i = 0; i < 1000; i++) { p = p + "a"; }
        S("uri3.long.path", new Uri(p).ToString());
    }
    public static void Main() {
        Console.WriteLine("-- shard diffuri003 start --");
        try { IPv6A(); } catch (Exception) { G("uri3.ipv6.loop"); }
        try { IPv6B(); } catch (Exception) { G("uri3.ipv6.glob"); }
        try { FileDrive(); } catch (Exception) { G("uri3.file.drive"); }
        try { FileUnc(); } catch (Exception) { G("uri3.file.unc"); }
        try { EmptyQ(); } catch (Exception) { G("uri3.empty.q"); }
        try { EmptyF(); } catch (Exception) { G("uri3.empty.f"); }
        try { EmptyQF(); } catch (Exception) { G("uri3.empty.qf"); }
        try { Pct2f(); } catch (Exception) { G("uri3.pct.2f"); }
        try { ZeroPort(); } catch (Exception) { G("uri3.port.zero"); }
        try { MixedCase(); } catch (Exception) { G("uri3.case.mixed"); }
        try { LongPath(); } catch (Exception) { G("uri3.long.path"); }
        Console.WriteLine("-- shard diffuri003 end --");
    }
}}
