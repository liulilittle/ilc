using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void DomainDict2() {
        Dictionary<int, string> d2 = new Dictionary<int, string>();
        try { d2[1] = "x"; d2[2] = "y"; I("col.dict2.count", d2.Count); } catch (Exception) { G("col.dict2.count"); }
        try {
            Dictionary<int, string>.Enumerator e2 = d2.GetEnumerator();
            try { KeyValuePair<int, string> c2 = e2.Current; I("col.dict2.cur.beforemove", (c2.Key == 0) ? 1 : 0); }
            catch (InvalidOperationException) { X("col.dict2.cur.beforemove", "InvalidOperationException"); }
        } catch (Exception) { G("col.dict2.cur.beforemove"); }
        try {
            Dictionary<int, string>.Enumerator e2 = d2.GetEnumerator();
            I("col.dict2.move1", e2.MoveNext() ? 1 : 0);
            I("col.dict2.cur.key", e2.Current.Key);
            S("col.dict2.cur.val", e2.Current.Value);
        } catch (Exception) { G("col.dict2.cur.key"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard diffcoldict2 start --");
        try { DomainDict2(); } catch (Exception) { G("col.dict2"); }
        Console.WriteLine("-- shard diffcoldict2 end --");
    }
}}
