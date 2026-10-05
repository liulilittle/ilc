using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    const int N = 30000;
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void Main() {
        Console.WriteLine("-- shard mbprob01 start --");
        string[] keys = new string[N];
        for (int i = 0; i < N; i++) keys[i] = "k" + i.ToString();
        Console.WriteLine("[ph] keys-built");
        Dictionary<string, int> d = new Dictionary<string, int>();
        for (int i = 0; i < N; i++) d.Add(keys[i], i);
        Console.WriteLine("[ph] add-done");
        for (int i = 0; i < N; i++) d[keys[i]] = i + 1;
        Console.WriteLine("[ph] set-done");
        long sum = 0;
        for (int i = 0; i < N; i++) sum += d[keys[i]];
        I("ph.getsum", (int)(sum % 1000000007));
        Console.WriteLine("[ph] get-done");
        int n = 0;
        foreach (KeyValuePair<string, int> kv in d) { n++; }
        I("ph.enumn", n);
        Console.WriteLine("[ph] enum-done");
        int hits = 0;
        for (int i = 0; i < N; i++) if (d.ContainsKey(keys[i])) hits++;
        I("ph.hits", hits);
        Console.WriteLine("[ph] contains-done");
        for (int i = 0; i < N; i += 2) d.Remove(keys[i]);
        I("ph.c_afterdel", d.Count);
        Console.WriteLine("[ph] remove-done");
        for (int i = 0; i < N; i += 2) d[keys[i]] = i + 2;
        I("ph.c_readd", d.Count);
        Console.WriteLine("[ph] readd-done");
        d.Clear();
        I("ph.c_clear", d.Count);
        Console.WriteLine("[ph] clear-done");
        Console.WriteLine("-- shard mbprob01 end --");
    }
}}
