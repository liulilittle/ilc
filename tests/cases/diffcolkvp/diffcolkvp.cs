using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void DomainKvp() {
        Dictionary<string, int> dk = new Dictionary<string, int>();
        dk.Add("k", 7);
        Dictionary<string, int>.Enumerator ek = dk.GetEnumerator();
        ek.MoveNext();
        try {
            KeyValuePair<string, int> kvp = ek.Current;
            S("col.kvp.key", kvp.Key); I("col.kvp.val", kvp.Value);
        } catch (Exception) { G("col.kvp.key"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard diffcolkvp start --");
        try { DomainKvp(); } catch (Exception) { G("col.kvp"); }
        Console.WriteLine("-- shard diffcolkvp end --");
    }
}}
