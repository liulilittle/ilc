using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void DomainSortedSet() {
        SortedSet<int> ss = new SortedSet<int>();
        try { ss.Add(3); ss.Add(1); ss.Add(2); I("col.sortedset.count", ss.Count); } catch (Exception) { G("col.sortedset.count"); }
        try {
            SortedSet<int>.Enumerator e3 = ss.GetEnumerator();
            try { int c3 = e3.Current; I("col.sortedset.cur.beforemove", c3); }
            catch (InvalidOperationException) { X("col.sortedset.cur.beforemove", "InvalidOperationException"); }
        } catch (Exception) { G("col.sortedset.cur.beforemove"); }
        try {
            SortedSet<int>.Enumerator e3 = ss.GetEnumerator();
            e3.MoveNext();
            I("col.sortedset.cur.first", e3.Current);
        } catch (Exception) { G("col.sortedset.cur.first"); }
        try {
            int n3 = 0; string seq = "";
            SortedSet<int>.Enumerator e3 = ss.GetEnumerator();
            while (e3.MoveNext()) { n3++; seq = seq + e3.Current.ToString(); }
            I("col.sortedset.enum.n", n3);
            S("col.sortedset.enum.seq", seq);
        } catch (Exception) { G("col.sortedset.enum.n"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard diffcolset001 start --");
        try { DomainSortedSet(); } catch (Exception) { G("col.sortedset"); }
        Console.WriteLine("-- shard diffcolset001 end --");
    }
}}
