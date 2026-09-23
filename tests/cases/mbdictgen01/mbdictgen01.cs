using System;
using System.Collections.Generic;
namespace Cases { public static class Prog {
    const int N = 3000;
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void Main() {
        Console.WriteLine("-- shard mbdictgen01 start --");
        string[] pool = new string[128];
        for (int i = 0; i < 128; i++) pool[i] = "v" + i.ToString();
        Dictionary<int, string> d = new Dictionary<int, string>();
        for (int i = 0; i < N; i++) d.Add(i, pool[i % 128]);
        for (int i = 0; i < N; i++) d[i] = pool[(i + 1) % 128];
        int sum = 0;
        for (int i = 0; i < N; i++) sum += d[i].Length;
        I("gen.getsum", sum);
        int hits = 0;
        for (int i = 0; i < N; i++) if (d.ContainsKey(i)) hits++;
        I("gen.hits", hits);
        string seq = "";
        int n = 0;
        foreach (KeyValuePair<int, string> kv in d) { n++; if (n <= 4) seq = seq + kv.Key.ToString() + ":" + kv.Value + ";"; }
        I("gen.enumn", n);
        Console.WriteLine("[gen.enumhead] " + seq);
        for (int i = 0; i < N; i += 2) d.Remove(i);
        I("gen.c_afterdel", d.Count);
        for (int i = 0; i < N; i += 2) d[i] = pool[i % 128];
        I("gen.c_readd", d.Count);
        d.Clear();
        I("gen.c_clear", d.Count);
        Console.WriteLine("-- shard mbdictgen01 end --");
    }
}}
