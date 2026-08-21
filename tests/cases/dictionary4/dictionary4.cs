//------------------------------------------------------------------------------
// dictionary4.cs -- Dictionary<string,int> DEEP probe 4 (e2e batch C75, after
// the dict domain full chain: MethodMap.Dictionary.cs + the dictsi_obj.h
// GC-object wrapper -- .ctor / .ctor(int) / Add / set_Item / get_Item /
// TryGetValue / ContainsKey / Remove / get_Count / Clear). dictdeep3 (C52)
// proved collision-under-load, content-dedup, the capacity matrix and
// operation-order independence; THIS case drives the SAME wired surface one
// stage DEEPER, on the three dimensions of a hash table:
//
//   1. COLLISION: 1200 shared-prefix keys ("k0000".."k1199") inserted through
//      MULTIPLE 0.72-load-factor resize cycles (bucket doublings 4->8->16->
//      ...->2048) with full round-trip integrity (every key queryable via
//      TryGetValue, value byte-exact); the distinct-key side of the hash
//      contract (case / prefix / length / beyond-range misses).
//   2. CAPACITY: a matrix of ctor capacities 0/1/2/3/4/8/15/16/32/1000/
//      65536/1000000 (accepted, empty, usable); growth from a 1-capacity dict
//      to 1500 entries (repeated rehash); Clear-then-reuse on the SAME
//      instance; partial-Remove pressure (every 3rd of 200 removed -> the
//      remaining 133 intact); add-after-clear.
//   3. TRAVERSAL (logical, order-INDEPENDENT): the wired surface exposes no
//      Keys/Values/enumerator producer (D-DICT3-1 -- get_Keys/get_Values are
//      not mapped in MethodMap.Dictionary.cs), so traversal is probed as the
//      observable CONTRACT: every one of N logical keys is reachable through
//      ContainsKey + indexer + TryGetValue; the SAME 12-key set inserted in
//      FOUR orders converges to identical state; interleaved Add/Remove/
//      overwrite converges; remove-then-re-add; overwrite preserves Count.
//   4. VALUE EDGES: int.MinValue / int.MaxValue / 0 / -1 / 123456789 round-
//      trip through Add + indexer.
//   5. EXCEPTION FACE (wired members only): negative capacity
//      ArgumentOutOfRangeException; null key ArgumentNullException on every
//      key-taking member (Add / TryGetValue / ContainsKey / Remove /
//      get_Item / set_Item); duplicate Add ArgumentException with the table
//      unchanged; get_Item miss KeyNotFoundException.
//
// GC CONTRACT (dictsi.h -- the wrapper does NOT root its keys): every STORED
// key is rooted here by keeping the whole key set in a string[] local (the
// transpiler frame-root tracks the array; the array's elements keep every
// stored key reachable for the dict's lifetime). MISS probes use transient
// keys that are never stored. This is the documented caller-side contract --
// a case that re-created keys at every access would let the collector free a
// stored key (recorded D-DICT4-1, probed around by construction).
//
// Every line prints "label=True/False" (or one raw int count) -- deterministic
// on both .NET Framework 4.8 and the transpiled runtime. net48 makes NO
// enumeration-order guarantee, so every probe is order-INDEPENDENT by
// construction.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class Dictionary4
{
    // "k0000".."k1199": zero-padded 4-digit suffix, pure function of i.
    static string K(int i)
    {
        string s = "k";
        if (i < 1000) s += "0";
        if (i < 100) s += "0";
        if (i < 10) s += "0";
        return s + i;
    }

    public static void Main()
    {
        // Pre-build + root the key set (the dictsi GC contract: the caller
        // keeps every stored key reachable -- the string[] does that).
        var ks = new string[1200];
        for (int i = 0; i < 1200; i++) ks[i] = K(i);

        // ============ 1. COLLISION UNDER MULTIPLE RESIZES ============
        var d = new Dictionary<string, int>();
        for (int i = 0; i < 1200; i++) d.Add(ks[i], i * 7 + 3);
        Console.WriteLine("load_count=" + (d.Count == 1200));
        int found = 0;
        for (int i = 0; i < 1200; i++)
        {
            int v;
            if (d.TryGetValue(ks[i], out v) && v == i * 7 + 3) found++;
        }
        Console.WriteLine("load_roundtrip=" + (found == 1200));
        Console.WriteLine("load_indexer=" + (d["k0599"] == 599 * 7 + 3 && d["k0000"] == 3 && d["k1199"] == 1199 * 7 + 3));
        Console.WriteLine("load_miss_beyond=" + !d.ContainsKey("k1200"));
        Console.WriteLine("load_miss_prefix=" + !d.ContainsKey("k"));
        Console.WriteLine("load_miss_case=" + !d.ContainsKey("K0000"));
        Console.WriteLine("load_miss_short=" + !d.ContainsKey("k000"));
        Console.WriteLine("load_miss_extend=" + !d.ContainsKey("k00000"));

        // ============ 2. CAPACITY MATRIX ============
        int[] caps = { 0, 1, 2, 3, 4, 8, 15, 16, 32, 1000, 65536, 1000000 };
        bool capsOk = true;
        foreach (int c in caps)
        {
            var dc = new Dictionary<string, int>(c);
            if (dc.Count != 0) capsOk = false;
            dc.Add("x", 42);
            if (dc.Count != 1 || dc["x"] != 42) capsOk = false;
        }
        Console.WriteLine("capacity_matrix=" + capsOk);
        bool negThrown = false;
        try { var _ = new Dictionary<string, int>(-5); } catch (ArgumentOutOfRangeException) { negThrown = true; }
        Console.WriteLine("capacity_neg_thrown=" + negThrown);

        // ============ 3. GROWTH FROM A TINY CAPACITY ============
        var kg = new string[1500];
        for (int i = 0; i < 1500; i++) kg[i] = K(i);
        var dg = new Dictionary<string, int>(1);
        for (int i = 0; i < 1500; i++) dg.Add(kg[i], i);
        Console.WriteLine("growth_count=" + (dg.Count == 1500));
        int gok = 0;
        for (int i = 0; i < 1500; i++) if (dg.ContainsKey(kg[i]) && dg[kg[i]] == i) gok++;
        Console.WriteLine("growth_verify=" + (gok == 1500));

        // ============ 4. CLEAR THEN REUSE (same instance) ============
        var dc2 = new Dictionary<string, int>();
        for (int i = 0; i < 500; i++) dc2.Add(ks[i], i);
        dc2.Clear();
        Console.WriteLine("clear_count=" + (dc2.Count == 0));
        Console.WriteLine("clear_contains=" + !dc2.ContainsKey("k0000"));
        int cv;
        Console.WriteLine("clear_tryget=" + !dc2.TryGetValue("k0000", out cv));
        dc2.Add("fresh", 7);
        dc2["other"] = 8;
        Console.WriteLine("clear_reuse=" + (dc2.Count == 2 && dc2["fresh"] == 7 && dc2["other"] == 8));

        // ============ 5. PARTIAL-REMOVE PRESSURE ============
        var dp = new Dictionary<string, int>();
        for (int i = 0; i < 200; i++) dp.Add(ks[i], i);
        for (int i = 1; i < 200; i += 3) dp.Remove(ks[i]);   // 67 removed (i%3==1)
        Console.WriteLine("remove_remaining=" + dp.Count);   // raw count 133
        int rok = 0;
        for (int i = 0; i < 200; i++)
        {
            bool gone = (i % 3 == 1);
            if (gone ? !dp.ContainsKey(ks[i]) : (dp[ks[i]] == i)) rok++;
        }
        Console.WriteLine("remove_verify=" + (rok == 200));
        Console.WriteLine("remove_absent_false=" + !dp.Remove("zzz-not-present"));

        // ============ 6. LOGICAL TRAVERSAL (order-independent contract) ============
        // The same 12-key set inserted in FOUR different orders -> identical
        // observable state (values keyed by key, never by insertion rank).
        string[] ks12 = { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l" };
        var o1 = new Dictionary<string, int>();
        var o2 = new Dictionary<string, int>();
        var o3 = new Dictionary<string, int>();
        var o4 = new Dictionary<string, int>();
        for (int i = 0; i < 12; i++) o1.Add(ks12[i], i * 5);
        for (int i = 11; i >= 0; i--) o2.Add(ks12[i], i * 5);
        int[] seq = { 7, 0, 11, 3, 5, 9, 1, 8, 2, 10, 4, 6 };
        for (int i = 0; i < 12; i++) o3.Add(ks12[seq[i]], seq[i] * 5);
        o4.Add("a", 0); o4.Add("e", 20); o4.Add("i", 40); o4.Add("b", 5);
        o4.Add("f", 25); o4.Add("j", 45); o4.Add("c", 10); o4.Add("g", 30);
        o4.Add("k", 50); o4.Add("d", 15); o4.Add("h", 35); o4.Add("l", 55);
        bool orderSame = true;
        foreach (string k in ks12)
            if (o1[k] != o2[k] || o1[k] != o3[k] || o1[k] != o4[k]) orderSame = false;
        Console.WriteLine("order_same_values=" + orderSame);
        Console.WriteLine("order_same_count=" + (o1.Count == 12 && o2.Count == 12 && o3.Count == 12 && o4.Count == 12));

        // Interleaved Add/Remove/overwrite converges to the straight-line state.
        // "i"+i keys are runtime-created: root them (dictsi GC contract).
        var iks = new string[10];
        for (int i = 0; i < 10; i++) iks[i] = "i" + i;
        var inter = new Dictionary<string, int>();
        for (int i = 0; i < 8; i++) inter.Add(iks[i], i);
        inter.Remove(iks[2]);
        inter.Add(iks[8], 8);
        inter[iks[3]] = 33;
        inter.Remove(iks[0]);
        inter.Add(iks[9], 9);
        inter[iks[5]] = 55;
        inter.Remove(iks[7]);
        inter.Add(iks[7], 77);
        inter.Remove(iks[1]);
        Console.WriteLine("interleaved_count=" + (inter.Count == 7));
        Console.WriteLine("interleaved_values=" + (inter[iks[3]] == 33 && inter[iks[4]] == 4 && inter[iks[5]] == 55 && inter[iks[6]] == 6 && inter[iks[8]] == 8 && inter[iks[9]] == 9 && inter[iks[7]] == 77));
        Console.WriteLine("interleaved_gone=" + (!inter.ContainsKey(iks[0]) && !inter.ContainsKey(iks[1]) && !inter.ContainsKey(iks[2])));

        // Remove-then-re-add of the same content: a fresh insert.
        var rr = new Dictionary<string, int>();
        rr.Add("z", 1);
        rr.Remove("z");
        Console.WriteLine("readd_gone=" + (rr.Count == 0));
        rr.Add("z", 2);
        Console.WriteLine("readd_value=" + (rr["z"] == 2));
        Console.WriteLine("readd_count=" + (rr.Count == 1));

        // Overwrite via the indexer preserves Count.
        var ow = new Dictionary<string, int>();
        ow.Add("k1", 1); ow.Add("k2", 2);
        ow["k1"] = 100; ow["k2"] = 200; ow["k1"] = 300;
        Console.WriteLine("overwrite_count=" + (ow.Count == 2));
        Console.WriteLine("overwrite_values=" + (ow["k1"] == 300 && ow["k2"] == 200));

        // ============ 7. VALUE EDGES ============
        var ve = new Dictionary<string, int>();
        ve.Add("min", int.MinValue);
        ve.Add("max", int.MaxValue);
        ve.Add("zero", 0);
        ve.Add("neg", -1);
        ve.Add("big", 123456789);
        int vm, vx, vz, vn, vb;
        Console.WriteLine("value_edges=" + (ve.TryGetValue("min", out vm) && vm == int.MinValue
            && ve.TryGetValue("max", out vx) && vx == int.MaxValue
            && ve.TryGetValue("zero", out vz) && vz == 0
            && ve.TryGetValue("neg", out vn) && vn == -1
            && ve.TryGetValue("big", out vb) && vb == 123456789));

        // ============ 8. EXCEPTION FACE ============
        bool dupThrown = false;
        try { ve.Add("big", 999); } catch (ArgumentException) { dupThrown = true; }
        Console.WriteLine("dup_add_thrown=" + dupThrown);
        Console.WriteLine("dup_table_unchanged=" + (ve.Count == 5 && ve["big"] == 123456789));
        bool keyMiss = false;
        // D-DICT4-2 (fixed): TYPED catch restored -- the runtime chain now
        // leads with the real full name System.Collections.Generic.
        // KeyNotFoundException (exceptionext.cpp, same fix as D-MON-3), so
        // catch (KeyNotFoundException) matches (was catch (Exception), the
        // workaround for the wrong chain name -- see GAPS.md D-DICT4-2).
        try { int _ = ve["no-such-key"]; } catch (KeyNotFoundException) { keyMiss = true; }
        Console.WriteLine("indexer_miss_throws=" + keyMiss);
        // Null key on every key-taking member.
        bool n1 = false, n2 = false, n3 = false, n4 = false, n5 = false, n6 = false;
        try { ve.Add(null, 1); } catch (ArgumentNullException) { n1 = true; }
        try { int _; ve.TryGetValue(null, out _); } catch (ArgumentNullException) { n2 = true; }
        try { bool _ = ve.ContainsKey(null); } catch (ArgumentNullException) { n3 = true; }
        try { bool _ = ve.Remove(null); } catch (ArgumentNullException) { n4 = true; }
        try { int _ = ve[null]; } catch (ArgumentNullException) { n5 = true; }
        try { ve[null] = 1; } catch (ArgumentNullException) { n6 = true; }
        Console.WriteLine("nullkey_all_thrown=" + (n1 && n2 && n3 && n4 && n5 && n6));

        Console.WriteLine("done");
    }
}}
