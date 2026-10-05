using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static int SetAndCount(Dictionary<string, int> d, string k, int v) { d[k] = v; return d.Count; }
    static string KVKey(KeyValuePair<string, int> kv) { return kv.Key; }
    static void DomainDict() {
        Dictionary<string, int> d = new Dictionary<string, int>();
        try { d.Add("a", 1); d.Add("b", 2); d.Add("c", 3); I("col.dict.add.count", d.Count); } catch (Exception) { G("col.dict.add.count"); }
        try { I("col.dict.getitem.a", d["a"]); } catch (Exception) { G("col.dict.getitem.a"); }
        try { I("col.dict.setitem.new", SetAndCount(d, "d", 4)); } catch (Exception) { G("col.dict.setitem.new"); }
        try { I("col.dict.setitem.over", SetAndCount(d, "a", 9)); } catch (Exception) { G("col.dict.setitem.over"); }
        try { I("col.dict.getitem.over", d["a"]); } catch (Exception) { G("col.dict.getitem.over"); }
        try {
            try { int v = d["zzz"]; I("col.dict.getitem.missing", v); }
            catch (KeyNotFoundException) { X("col.dict.getitem.missing", "KeyNotFoundException"); }
        } catch (Exception) { G("col.dict.getitem.missing"); }
        try {
            try { int v = d[null]; I("col.dict.getitem.nullkey", v); }
            catch (ArgumentNullException) { X("col.dict.getitem.nullkey", "ArgumentNullException"); }
        } catch (Exception) { G("col.dict.getitem.nullkey"); }
        try {
            try { d[null] = 1; I("col.dict.setitem.nullkey", 0); }
            catch (ArgumentNullException) { X("col.dict.setitem.nullkey", "ArgumentNullException"); }
        } catch (Exception) { G("col.dict.setitem.nullkey"); }
        try {
            Dictionary<string, int>.Enumerator e = d.GetEnumerator();
            try { KeyValuePair<string, int> c = e.Current; I("col.dict.cur.beforemove", (c.Key == null) ? 1 : 0); }
            catch (InvalidOperationException) { X("col.dict.cur.beforemove", "InvalidOperationException"); }
        } catch (Exception) { G("col.dict.cur.beforemove"); }
        try {
            Dictionary<string, int>.Enumerator e = d.GetEnumerator();
            I("col.dict.move1", e.MoveNext() ? 1 : 0);
            S("col.dict.cur.key", KVKey(e.Current));
            I("col.dict.cur.val", e.Current.Value);
        } catch (Exception) { G("col.dict.cur.key"); }
        try {
            int n = 0; string keys = "";
            Dictionary<string, int>.Enumerator e = d.GetEnumerator();
            while (e.MoveNext()) { n++; keys = keys + e.Current.Key; }
            I("col.dict.enum.n", n);
            S("col.dict.enum.keys", keys);
        } catch (Exception) { G("col.dict.enum.n"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard diffcoldict start --");
        try { DomainDict(); } catch (Exception) { G("col.dict"); }
        Console.WriteLine("-- shard diffcoldict end --");
    }
}}
