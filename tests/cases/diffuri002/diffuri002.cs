using System;
namespace Cases { public static class Prog {
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void RelVerbatim() {
        Uri u = new Uri("/a/../b/./c", UriKind.Relative);
        S("uri2.rel.verbatim", u.ToString());
    }
    static void FtpDefPort() {
        S("uri2.ftp.defport", new Uri("ftp://h:21/x").ToString());
    }
    static void UserInfoPort() {
        S("uri2.userinfo.port", new Uri("http://u:p@h:8080/a?q=1#f").ToString());
    }
    static void PctUnescape() {
        S("uri2.pct.unescape", new Uri("http://h/%e4%b8%ad").ToString());
    }
    static void PlusPreserved() {
        S("uri2.plus.preserved", new Uri("http://h/a+b").ToString());
    }
    static void TrailSlash() {
        S("uri2.trail.slash", new Uri("http://h").ToString());
    }
    public static void Main() {
        Console.WriteLine("-- shard diffuri002 start --");
        try { RelVerbatim(); } catch (Exception) { G("uri2.rel"); }
        try { FtpDefPort(); } catch (Exception) { G("uri2.ftp"); }
        try { UserInfoPort(); } catch (Exception) { G("uri2.userinfo"); }
        try { PctUnescape(); } catch (Exception) { G("uri2.pct"); }
        try { PlusPreserved(); } catch (Exception) { G("uri2.plus"); }
        try { TrailSlash(); } catch (Exception) { G("uri2.trail"); }
        Console.WriteLine("-- shard diffuri002 end --");
    }
}}
