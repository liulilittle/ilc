//------------------------------------------------------------------------------
// dictp25.cs -- P25 Dictionary<string,int> enumeration / collection surface
// probe: closes the "Dictionary<K,V> generic interface impl" emitter gap.
//
// Coverage:
//   1. foreach (KeyValuePair<string,int> kvp in dict) -- Sum/count.
//   2. foreach over dict.Keys (KeyCollection snapshot) and dict.Values.
//   3. foreach over dict.Keys typed as ICollection<string>.
//   4. IDictionary<string,int> interface receiver: Count / Add / foreach.
//   5. GetEnumerator() snapshot independence (mutate after taking enumerator).
//   6. Nested dictionary (two live dicts, interleaved foreach).
//
// Every line prints "label=Value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class DictP25 {
    public static void Main() {
        // ============ 1. foreach over KeyValuePair ============
        var d1 = new Dictionary<string, int>();
        d1.Add("a", 1);
        d1.Add("b", 2);
        d1.Add("c", 3);
        d1.Add("d", 4);
        d1.Add("e", 5);
        int sum = 0;
        int cnt = 0;
        foreach (KeyValuePair<string, int> kvp in d1)
        {
            sum += kvp.Value;
            cnt++;
        }
        Console.WriteLine("fore_kv_sum=" + (sum == 15));
        Console.WriteLine("fore_kv_cnt=" + (cnt == 5));

        // ============ 2. Keys / Values snapshots ============
        int ksum = 0;
        foreach (string k in d1.Keys) { ksum += k.Length; }
        Console.WriteLine("keys_len_sum=" + (ksum == 5));
        int vsum = 0;
        foreach (int v in d1.Values) { vsum += v; }
        Console.WriteLine("vals_sum=" + (vsum == 15));
        Console.WriteLine("keys_cnt=" + (d1.Keys.Count == 5));
        Console.WriteLine("vals_cnt=" + (d1.Values.Count == 5));

        // ============ 3. Keys as ICollection<string> ============
        ICollection<string> ick = d1.Keys;
        int icnt = 0;
        foreach (string k in ick) { icnt++; }
        Console.WriteLine("icol_foreach=" + (icnt == 5));
        Console.WriteLine("icol_contains=" + ick.Contains("c"));

        // ============ 4. IDictionary<string,int> interface receiver ============
        IDictionary<string, int> id = d1;
        Console.WriteLine("idict_cnt=" + (id.Count == 5));
        id["f"] = 6;
        Console.WriteLine("idict_added=" + (id.Count == 6));
        int isum = 0;
        foreach (KeyValuePair<string, int> kvp in id) { isum += kvp.Value; }
        Console.WriteLine("idict_foreach_sum=" + (isum == 21));

        // ============ 5. Snapshot independence ============
        var snap = new Dictionary<string, int>();
        snap.Add("x", 100);
        snap.Add("y", 200);
        int snapSum = 0;
        foreach (KeyValuePair<string, int> kvp in snap) { snapSum += kvp.Value; }
        snap.Add("z", 300); // mutate AFTER enumeration completes
        Console.WriteLine("snap_sum=" + (snapSum == 300));
        Console.WriteLine("snap_grew=" + (snap.Count == 3));

        // ============ 6. Two live dictionaries, interleaved foreach ============
        var da = new Dictionary<string, int>(); da.Add("a", 1); da.Add("b", 2);
        var db = new Dictionary<string, int>(); db.Add("p", 10); db.Add("q", 20); db.Add("r", 30);
        int ta = 0, tb = 0;
        foreach (KeyValuePair<string, int> x in da) { ta += x.Value; }
        foreach (KeyValuePair<string, int> y in db) { tb += y.Value; }
        foreach (KeyValuePair<string, int> x in da) { ta += x.Value; }
        Console.WriteLine("two_sum=" + (ta == 6 && tb == 60));

        Console.WriteLine("done");
    }
}}