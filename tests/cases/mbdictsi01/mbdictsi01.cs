using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    const int N = 3000;
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void Main() {
        Console.WriteLine("-- shard mbdictsi01 start --");
        string[] keys = new string[N];
        for (int i = 0; i < N; i++) keys[i] = "k" + i.ToString();
        Dictionary<string, int> d = new Dictionary<string, int>();
        for (int i = 0; i < N; i++) d.Add(keys[i], i);
        for (int i = 0; i < N; i++) d[keys[i]] = i + 1;
        long sum = 0;
        for (int i = 0; i < N; i++) sum += d[keys[i]];
        I("si.getsum", (int)(sum % 1000000007));
        int hits = 0;
        for (int i = 0; i < N; i++) if (d.ContainsKey(keys[i])) hits++;
        I("si.hits", hits);
        string seq = "";
        int n = 0;
        foreach (KeyValuePair<string, int> kv in d) { n++; if (n <= 4) seq = seq + kv.Key + ":" + kv.Value.ToString() + ";"; }
        I("si.enumn", n);
        Console.WriteLine("[si.enumhead] " + seq);
        for (int i = 0; i < N; i += 2) d.Remove(keys[i]);
        I("si.c_afterdel", d.Count);
        for (int i = 0; i < N; i += 2) d[keys[i]] = i + 2;
        I("si.c_readd", d.Count);
        d.Clear();
        I("si.c_clear", d.Count);
        Console.WriteLine("-- shard mbdictsi01 end --");
    }
}}
