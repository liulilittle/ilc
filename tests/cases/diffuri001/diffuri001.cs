using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void Direct() {
        Uri u = new Uri("http://example.com/a");
        S("uri.tostring.direct", u.ToString());
        S("uri.scheme", u.Scheme);
        S("uri.host", u.Host);
        S("uri.path", u.AbsolutePath);
    }
    static void ViaObject() {
        object o = new Uri("http://example.com/a");
        S("uri.tostring.object", o.ToString());
    }
    static void ViaQueue() {
        Queue<Uri> qu = new Queue<Uri>();
        qu.Enqueue(new Uri("http://example.com/a"));
        S("uri.tostring.queue", qu.Peek().ToString());
    }
    static void Canon() {
        S("uri.canon.upper", new Uri("HTTP://Example.COM/A").ToString());
        S("uri.canon.defport", new Uri("http://example.com:80/a").ToString());
        S("uri.canon.dots", new Uri("http://example.com/a/../b/./c").ToString());
        S("uri.canon.pct", new Uri("http://example.com/a%20b").ToString());
        S("uri.canon.notrail", new Uri("http://example.com").ToString());
    }
    static void OriginalStr() {
        Uri u = new Uri("http://example.com/a");
        S("uri.originalstring", u.OriginalString);
    }
    static void AbsoluteUriM() {
        Uri u = new Uri("http://example.com/a");
        S("uri.absoluteuri", u.AbsoluteUri);
    }
    static void EqualsM() {
        Uri a = new Uri("http://example.com/a");
        Uri b = new Uri("http://example.com/a");
        I("uri.equals.same", a.Equals(b) ? 1 : 0);
    }
    static void HashM() {
        Uri a = new Uri("http://example.com/a");
        I("uri.gethashcode", a.GetHashCode());
    }
    public static void Main() {
        Console.WriteLine("-- shard diffuri001 start --");
        try { Direct(); } catch (Exception) { G("uri.direct"); }
        try { ViaObject(); } catch (Exception) { G("uri.object"); }
        try { ViaQueue(); } catch (Exception) { G("uri.queue"); }
        try { Canon(); } catch (Exception) { G("uri.canon"); }
        try { OriginalStr(); } catch (Exception) { G("uri.originalstring"); }
        try { AbsoluteUriM(); } catch (Exception) { G("uri.absoluteuri"); }
        try { EqualsM(); } catch (Exception) { G("uri.equals"); }
        try { HashM(); } catch (Exception) { G("uri.gethashcode"); }
        Console.WriteLine("-- shard diffuri001 end --");
    }
}}
