using System;
using System.Collections.Generic;
using System.Globalization;

// g4dictstrstr -- G4 Dictionary<string,string> e2e case (GREEN since the
// string->string expansion landed: dictss forwarders over the non-generic
// hashtable runtime + instName-authoritative receiver dispatch + the
// keycollection-style values enumerator). Deterministic: the Keys/Values
// snapshots are copied into arrays and default-sorted (all-lowercase ASCII).
class Prog
{
    static void Main()
    {
        Dictionary<string, string> d = new Dictionary<string, string>();
        d.Add("b", "beta");
        d.Add("a", "alpha");
        d.Add("c", "gamma");
        Console.WriteLine("count=" + d.Count);

        Console.WriteLine("geta=" + d["a"]);
        d["b"] = "beta2";
        Console.WriteLine("setb=" + d["b"]);

        Console.WriteLine("hasc=" + d.ContainsKey("c"));
        Console.WriteLine("hasz=" + d.ContainsKey("z"));

        string tv;
        Console.WriteLine("try_a=" + d.TryGetValue("a", out tv));
        Console.WriteLine("try_a_val=" + tv);
        Console.WriteLine("try_z=" + d.TryGetValue("z", out tv));

        Console.WriteLine("removed_c=" + d.Remove("c"));
        Console.WriteLine("count2=" + d.Count);

        string[] ks = new string[d.Count];
        int nk = 0;
        foreach (string k in d.Keys)
            ks[nk++] = k;
        // Default sort (all-lowercase ASCII: ordinal == culture order, so the
        // oracle is unambiguous). The StringComparer.Ordinal / generic
        // Array.Sort<T>(T[], IComparer<T>) shapes live in the dedicated
        // strsort_ordinal probe (undefined-symbol gap).
        Array.Sort(ks);
        for (int i = 0; i < ks.Length; i++)
            Console.WriteLine("key=" + ks[i]);

        string[] vs = new string[d.Count];
        int nv = 0;
        foreach (string v in d.Values)
            vs[nv++] = v;
        Array.Sort(vs);
        for (int i = 0; i < vs.Length; i++)
            Console.WriteLine("val=" + vs[i]);

        Console.WriteLine("done");
    }
}
