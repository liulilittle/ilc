using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static int EnumN(Dictionary<string, int> d) { int n = 0; foreach (KeyValuePair<string, int> kv in d) { n++; } return n; }
    static string EnumSeq(Dictionary<string, int> d) { string s = ""; foreach (KeyValuePair<string, int> kv in d) { s = s + kv.Key; } return s; }
    static void DomainEnum() {
        Dictionary<string, int> e = new Dictionary<string, int>();
        try { I("d.enum.empty.n", EnumN(e)); } catch (Exception) { G("d.enum.empty.n"); }
        try {
            Dictionary<string, int>.Enumerator en = e.GetEnumerator();
            try { KeyValuePair<string, int> c = en.Current; I("d.enum.empty.cur", (c.Key == null) ? 1 : 0); }
            catch (InvalidOperationException) { S("d.enum.empty.cur", "InvalidOperationException"); }
        } catch (Exception) { G("d.enum.empty.cur"); }
        Dictionary<string, int> s1 = new Dictionary<string, int>();
        s1.Add("a", 1);
        try { S("d.enum.single.seq", EnumSeq(s1)); } catch (Exception) { G("d.enum.single.seq"); }
        Dictionary<string, int> m = new Dictionary<string, int>();
        m.Add("a", 1); m.Add("b", 2); m.Add("c", 3);
        m["b"] = 20;
        try { S("d.enum.overwrite.seq", EnumSeq(m)); } catch (Exception) { G("d.enum.overwrite.seq"); }
        try { I("d.enum.overwrite.val", m["b"]); } catch (Exception) { G("d.enum.overwrite.val"); }
        m.Remove("b");
        try { S("d.enum.afterdel.seq", EnumSeq(m)); } catch (Exception) { G("d.enum.afterdel.seq"); }
        m.Add("b", 5);
        try { S("d.enum.readd.seq", EnumSeq(m)); } catch (Exception) { G("d.enum.readd.seq"); }
        Dictionary<string, int> L = new Dictionary<string, int>();
        for (int i = 0; i < 40; i++) { L.Add("k" + i.ToString(), i); }
        try { S("d.enum.large.seq", EnumSeq(L)); } catch (Exception) { G("d.enum.large.seq"); }
        try { I("d.enum.large.n", EnumN(L)); } catch (Exception) { G("d.enum.large.n"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard diffdict001 start --");
        try { DomainEnum(); } catch (Exception) { G("d.enum"); }
        Console.WriteLine("-- shard diffdict001 end --");
    }
}}
