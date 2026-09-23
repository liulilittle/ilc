using System;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static void G(string id) { Console.WriteLine("[" + id + "] EX gap"); }
    static void DomainCmp() {
        try { I("col.cmp.lt", System.Collections.Comparer.Default.Compare(1, 2)); } catch (Exception) { G("col.cmp.lt"); }
        try { I("col.cmp.eq", System.Collections.Comparer.Default.Compare(2, 2)); } catch (Exception) { G("col.cmp.eq"); }
        try { I("col.cmp.gt", System.Collections.Comparer.Default.Compare(3, 1)); } catch (Exception) { G("col.cmp.gt"); }
        try { I("col.cmp.null1", System.Collections.Comparer.Default.Compare(null, 5)); } catch (Exception) { G("col.cmp.null1"); }
        try { I("col.cmp.null2", System.Collections.Comparer.Default.Compare(5, null)); } catch (Exception) { G("col.cmp.null2"); }
        try {
            try { I("col.cmp.bad", System.Collections.Comparer.Default.Compare("a", 1)); }
            catch (ArgumentException) { X("col.cmp.bad", "ArgumentException"); }
        } catch (Exception) { G("col.cmp.bad"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard diffcolcmp start --");
        try { DomainCmp(); } catch (Exception) { G("col.cmp"); }
        Console.WriteLine("-- shard diffcolcmp end --");
    }
}}
