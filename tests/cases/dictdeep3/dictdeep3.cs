//------------------------------------------------------------------------------
// dictdeep3.cs -- Dictionary<string,int> DEEP probe #3 (e2e batch C52, after
// the dictsi domain: MethodMap.Dictionary.cs + dictsi_obj.h wrapper). Beyond
// dictcase (happy path) and dictedge (boundary/edge), this case drives the
// three deep dimensions of a hash table: COLLISION, CAPACITY and OPERATION-
// ORDER independence (net48 makes no enumeration-order guarantee, so every
// probe is order-INDEPENDENT by construction).
//
// WIRED SURFACE (the ONLY members used -- anything else aborts transpile):
//   .ctor() / .ctor(int capacity)  Add  set_Item  get_Item  TryGetValue
//   ContainsKey  Remove  get_Count  Clear
//   (Dictionary<string,int>.Keys/Values enumeration is NOT wired --
//   MethodMap.Dictionary.cs maps no get_Keys/get_Values producer; see
//   D-DICT3-1 below. Traversal ORDER itself is therefore out of reach; the
//   case asserts the order-INDEPENDENT observable contract instead.)
//
//   1. COLLISION UNDER LOAD: 300 fixed-seed keys with a SHARED PREFIX
//      ("k000".."k299") force long hash-chain / probe paths and multiple
//      0.72-load-factor resizes. Every key must remain queryable and its
//      value must round-trip through TryGetValue (chain integrity across
//      grow+rehash). A distinct-content key must miss.
//   2. CONTENT-DEDUP COLLISION: two DISTINCT string instances with EQUAL
//      content (one via string.Concat) are the SAME key (content equality,
//      not reference). Contrariwise, keys that differ only by CASE or by a
//      PREFIX ("alpha" vs "alphabet") are distinct keys that must not alias.
//   3. CAPACITY MATRIX: the capacity ctor accepts 0 / 1 / 2 / 3 / 7 / 16 /
//      31 / 100 / 1000 / 10000 (bucket rounding is internal -- observable is
//      "accepted, empty, usable"); a NEGATIVE capacity raises
//      ArgumentOutOfRangeException. A small-capacity dict grows to hold many
//      entries; after growth everything stays queryable.
//   4. OPERATION-ORDER INDEPENDENCE: the same key/value set inserted in
//      THREE different orders yields the SAME Count + ContainsKey + indexer
//      results; interleaved Add/Remove/overwrite converges to the same
//      final state as a straight-line build; remove-then-re-add of the same
//      key behaves identically (content re-insert).
//
// DELIBERATELY NOT PROBED (recorded defects, see GAPS.md):
//   * D-DICT3-1: Dictionary<string,int>.Keys / .Values (and hence foreach
//     enumeration / traversal order) -- no transpiler mapping exists in
//     MethodMap.Dictionary.cs (the runtime dictsi header DOES expose
//     il2cpp_dict_si_get_keys/get_values, but the resolver never emits them).
//     Any `d.Keys` / `foreach` aborts transpile with NotSupportedException.
//   * D-DICT3-2: raw string GetHashCode values are process-randomized on
//     net48 (UseRandomizedStringHashAlgorithm) -- never printed; only the
//     collision RELATIONSHIPS above are probed.
//
// Every line prints "label=True/False" (or a small count/round-trip sum) --
// deterministic on both .NET Framework 4.8 and the transpiled runtime.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class DictDeep3
{
    // Build equal content from two runtime halves (distinct instance).
    static string Join2(string a, string b) { return string.Concat(a, b); }

    public static void Main()
    {
        // ============ 1. collision under load (shared-prefix chains) ============
        var d1 = new Dictionary<string, int>();
        var rng = new Random(12345);
        for (int i = 0; i < 300; i++)
            d1.Add("k" + (i < 100 ? "0" : "") + (i < 10 ? "0" : "") + i, rng.Next(1000000));
        Console.WriteLine("load_count=" + (d1.Count == 300));
        int found = 0;
        var rng2 = new Random(12345);
        for (int i = 0; i < 300; i++)
        {
            int v;
            string key = "k" + (i < 100 ? "0" : "") + (i < 10 ? "0" : "") + i;
            if (d1.TryGetValue(key, out v) && v == rng2.Next(1000000)) found++;
        }
        Console.WriteLine("load_roundtrip=" + (found == 300));
        Console.WriteLine("load_miss=" + !d1.ContainsKey("k300"));
        Console.WriteLine("load_prefix_miss=" + !d1.ContainsKey("k"));
        Console.WriteLine("load_case_miss=" + !d1.ContainsKey("K0"));

        // ============ 2. content-dedup collision ============
        var d2 = new Dictionary<string, int>();
        d2.Add("lit", 1);
        // A distinct instance with equal content is the SAME key.
        Console.WriteLine("dedup_contains=" + d2.ContainsKey(Join2("li", "t")));
        int dv;
        Console.WriteLine("dedup_tryget=" + (d2.TryGetValue(Join2("li", "t"), out dv) && dv == 1));
        Console.WriteLine("dedup_indexer=" + (d2[Join2("li", "t")] == 1));
        // Duplicate content via a fresh instance still throws.
        bool dupThrown = false;
        try { d2.Add(Join2("li", "t"), 2); } catch (ArgumentException) { dupThrown = true; }
        Console.WriteLine("dedup_dup_thrown=" + dupThrown);
        Console.WriteLine("dedup_count_after=" + (d2.Count == 1));

        // ============ 3. ordinal + prefix non-aliasing ============
        var d3 = new Dictionary<string, int>();
        d3.Add("alpha", 1);
        d3.Add("alphabet", 2);   // strict prefix of a DIFFERENT length -> distinct
        d3.Add("Alpha", 3);      // case differs -> distinct
        Console.WriteLine("prefix_both=" + (d3.Count == 3));
        Console.WriteLine("prefix_values=" + (d3["alpha"] == 1 && d3["alphabet"] == 2 && d3["Alpha"] == 3));
        Console.WriteLine("prefix_no_alias=" + (d3["alphabet"] != 1));
        Console.WriteLine("prefix_ordinal=" + (!d3.ContainsKey("ALPHA") && !d3.ContainsKey("alphabetx")));

        // ============ 4. capacity matrix ============
        int[] caps = { 0, 1, 2, 3, 7, 16, 31, 100, 1000, 10000 };
        bool capsOk = true;
        foreach (int c in caps)
        {
            var dc = new Dictionary<string, int>(c);
            dc.Add("x", 42);
            if (dc.Count != 1 || dc["x"] != 42) capsOk = false;
        }
        Console.WriteLine("capacity_matrix=" + capsOk);
        bool negThrown = false;
        try { var _ = new Dictionary<string, int>(-5); } catch (ArgumentOutOfRangeException) { negThrown = true; }
        Console.WriteLine("capacity_neg_thrown=" + negThrown);

        // ============ 5. small-capacity growth (bucket rounding) ============
        var dg = new Dictionary<string, int>(1);
        for (int i = 0; i < 200; i++) dg.Add("g" + i, i * 3);
        Console.WriteLine("growth_count=" + (dg.Count == 200));
        int gok = 0;
        for (int i = 0; i < 200; i++) if (dg.ContainsKey("g" + i) && dg["g" + i] == i * 3) gok++;
        Console.WriteLine("growth_verify=" + (gok == 200));

        // ============ 6. operation-order independence ============
        // Same 6 keys added in three orders -> identical observable state.
        string[] ks = { "p", "q", "r", "s", "t", "u" };
        var o1 = new Dictionary<string, int>();
        var o2 = new Dictionary<string, int>();
        var o3 = new Dictionary<string, int>();
        for (int i = 0; i < 6; i++) o1.Add(ks[i], i);
        for (int i = 5; i >= 0; i--) o2.Add(ks[i], i);
        int[] seq3 = { 2, 0, 5, 1, 4, 3 };
        for (int i = 0; i < 6; i++) o3.Add(ks[seq3[i]], seq3[i]);
        bool orderSame = true;
        foreach (string k in ks)
            if (o1[k] != o2[k] || o1[k] != o3[k]) orderSame = false;
        Console.WriteLine("order_same_values=" + orderSame);
        Console.WriteLine("order_same_count=" + (o1.Count == 6 && o2.Count == 6 && o3.Count == 6));

        // Interleaved Add/Remove/overwrite converges to the straight-line state.
        var inter = new Dictionary<string, int>();
        for (int i = 0; i < 5; i++) inter.Add("i" + i, i);
        inter.Remove("i1");
        inter.Add("i5", 5);
        inter["i2"] = 22;
        inter.Remove("i0");
        inter.Add("i6", 6);
        inter["i3"] = 33;
        inter.Remove("i4");
        inter.Add("i4", 44);
        Console.WriteLine("interleaved_count=" + (inter.Count == 5));
        Console.WriteLine("interleaved_values=" + (inter["i2"] == 22 && inter["i3"] == 33 && inter["i4"] == 44 && inter["i5"] == 5 && inter["i6"] == 6));
        Console.WriteLine("interleaved_gone=" + (!inter.ContainsKey("i0") && !inter.ContainsKey("i1")));

        // Remove-then-re-add of the same content: a fresh insert.
        var rr = new Dictionary<string, int>();
        rr.Add("z", 1);
        rr.Remove("z");
        rr.Add("z", 2);
        Console.WriteLine("readd_value=" + (rr["z"] == 2));
        Console.WriteLine("readd_count=" + (rr.Count == 1));

        Console.WriteLine("done");
    }
}}
