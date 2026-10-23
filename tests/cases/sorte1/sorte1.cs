using System;
using System.Collections.Generic;

// sorte1 -- ordered-collection family: SortedDictionary / SortedList<TKey,TValue> /
// SortedSet ordering, Keys/Values order, Min/Max, GetViewBetween.
class Prog
{
    static void Main()
    {
        SortedDictionary<string, int> sd = new SortedDictionary<string, int>();
        sd["delta"] = 4;
        sd["alpha"] = 1;
        sd["charlie"] = 3;
        sd["bravo"] = 2;
        Console.WriteLine("sd_count=" + sd.Count);
        string keys = "";
        foreach (string k in sd.Keys) keys += k + ",";
        Console.WriteLine("sd_keys=" + keys);
        string vals = "";
        foreach (int v in sd.Values) vals += v + ",";
        Console.WriteLine("sd_vals=" + vals);
        string pairs = "";
        foreach (KeyValuePair<string, int> kv in sd) pairs += kv.Key + "=" + kv.Value + ";";
        Console.WriteLine("sd_pairs=" + pairs);
        int got;
        Console.WriteLine("sd_tryget=" + sd.TryGetValue("bravo", out got) + ":" + got);
        Console.WriteLine("sd_missing=" + sd.TryGetValue("zulu", out got));

        SortedList<int, string> sl = new SortedList<int, string>();
        sl.Add(30, "c");
        sl.Add(10, "a");
        sl.Add(20, "b");
        Console.WriteLine("sl_count=" + sl.Count);
        string slk = "";
        foreach (int k in sl.Keys) slk += k + ",";
        Console.WriteLine("sl_keys=" + slk);
        Console.WriteLine("sl_idx0=" + sl[10]);
        Console.WriteLine("sl_indexof=" + sl.IndexOfKey(20));

        SortedSet<int> ss = new SortedSet<int>();
        ss.Add(5); ss.Add(1); ss.Add(9); ss.Add(3);
        Console.WriteLine("ss_count=" + ss.Count);
        string ssv = "";
        foreach (int v in ss) ssv += v + ",";
        Console.WriteLine("ss_order=" + ssv);
        Console.WriteLine("ss_min=" + ss.Min + " ss_max=" + ss.Max);
        SortedSet<int> view = ss.GetViewBetween(2, 6);
        string vv = "";
        foreach (int v in view) vv += v + ",";
        Console.WriteLine("view=" + vv + " count=" + view.Count);
        Console.WriteLine("ss_contains=" + ss.Contains(3) + " " + ss.Contains(4));
        Console.WriteLine("ss_remove=" + ss.Remove(5) + " count=" + ss.Count);
    }
}
