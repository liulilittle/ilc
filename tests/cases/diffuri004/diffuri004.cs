using System;
namespace Cases { public static class Prog {
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void AbsPath() {
        S("uri4.abspath.plain", new Uri("http://example.com/a").AbsolutePath);
        S("uri4.abspath.dots", new Uri("http://example.com/a/../b/./c").AbsolutePath);
        S("uri4.abspath.pct", new Uri("http://example.com/a%20b").AbsolutePath);
        S("uri4.abspath.empty", new Uri("http://example.com").AbsolutePath);
        S("uri4.abspath.opaque", new Uri("mailto:foo/bar").AbsolutePath);
        S("uri4.abspath.opaque-dots", new Uri("custom:a/../b").AbsolutePath);
        S("uri4.abspath.aboveroot", new Uri("http://example.com/../x").AbsolutePath);
    }
    public static void Main() {
        Console.WriteLine("-- shard diffuri004 start --");
        try { AbsPath(); } catch (Exception) { G("uri4.abspath"); }
        Console.WriteLine("-- shard diffuri004 end --");
    }
}}
