// dictedge.cs -- Dictionary<string,int> boundary / edge-case case. The
// System.Collections.Generic.Dictionary`2 string->int32 expansion is WIRED
// (M6 batch 81: the dictsi GC-object wrapper -- il2cpp_dict_si_* via the
// System_Collections_Generic_Dictionary_2_string_int_class descriptor). This
// case exercises the FULL wired boundary surface around dictcase's happy path:
//
//   * EMPTY: a fresh dictionary Count == 0, TryGetValue on a missing key
//     returns false, ContainsKey false, Remove false, get_Item throws
//     KeyNotFoundException, indexer-set inserts.
//   * CAPACITY CTOR: `new Dictionary<string,int>(n)` -- the wired capacity
//     channel (icall_dict_si_ctor(self, capacity)); the bucket count is
//     internal (no observable), so the boundary is the ACCEPTED value (no
//     throw) + subsequent Count/Add behavior. NEGATIVE capacity raises
//     ArgumentOutOfRangeException (il2cpp_dict_si_create capacity < 0).
//   * DUPLICATE Add -> ArgumentException (content-equality contract), dict
//     unchanged.
//   * NULL key -> ArgumentNullException on Add / TryGetValue / ContainsKey /
//     get_Item / set_Item / Remove (ThrowIfNullKey).
//   * INDEXER: get_Item hit returns the value; MISS throws
//     KeyNotFoundException; set_Item INSERT-or-REPLACE (never throws on a
//     missing key; a present key is overwritten in place -- count unchanged).
//   * TryGetValue hit/miss (out-param written on hit).
//   * Remove present -> true + count decrement; Remove absent -> false.
//   * Clear -> count 0, dictionary reusable.
//   * VALUE EDGES: int.MinValue / int.MaxValue / 0 / negative values stored
//     and read back byte-exact.
//   * KEY CONTENT: ordinal string keys (the dictsi content-equality contract
//     is byte equality -- no case folding, no culture).
//
// DELIBERATELY EXCLUDED (unwired / out-of-subset, transpiler would reject):
//   * SortedList / non-generic Dictionary / other generic instantiations
//     (only the string->int32 expansion is wired -- dictcase records the
//     SortedList gap).
//   * Keys / Values enumeration (the wrapper exposes no Keys/Values surface
//     in MethodMap.Dictionary.cs; only the methods below are wired).
//   * int capacity with a huge value (grow/rehash is internal; only small
//     capacities are probed -- the observable behavior is identical).
//
// EVERY line of output is a single bool (or a small Count int): no raw hash
// iteration order, no formatting drift. Runs on .NET Framework 4.8 (the
// reference oracle) + the transpiled runtime; deterministic by construction.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class DictEdge
{
    public static void Main()
    {
        // ---- 1. fresh dictionary: the empty state ----
        var d0 = new Dictionary<string, int>();
        Console.WriteLine("empty_count=" + (d0.Count == 0));
        int v0;
        Console.WriteLine("empty_tryget_miss=" + !d0.TryGetValue("x", out v0));
        Console.WriteLine("empty_contains=" + !d0.ContainsKey("x"));
        Console.WriteLine("empty_remove=" + !d0.Remove("x"));
        bool emptyGetThrown = false;
        try { int _ = d0["x"]; } catch (Exception) { emptyGetThrown = true; }
        Console.WriteLine("empty_get_thrown=" + emptyGetThrown);
        d0["zero"] = 0;
        Console.WriteLine("empty_set_insert_count=" + (d0.Count == 1));
        Console.WriteLine("empty_set_insert_value=" + (d0["zero"] == 0));

        // ---- 2. capacity ctor: 0 / 1 / 16 accepted; negative rejected ----
        var dc0 = new Dictionary<string, int>(0);
        Console.WriteLine("cap0_ok=" + (dc0.Count == 0));
        var dc1 = new Dictionary<string, int>(1);
        dc1.Add("a", 1);
        Console.WriteLine("cap1_add=" + (dc1["a"] == 1));
        var dc16 = new Dictionary<string, int>(16);
        dc16.Add("k", 42);
        Console.WriteLine("cap16_add=" + (dc16["k"] == 42));
        bool negCapThrown = false;
        try { var _ = new Dictionary<string, int>(-1); } catch (ArgumentOutOfRangeException) { negCapThrown = true; }
        Console.WriteLine("neg_capacity_thrown=" + negCapThrown);

        // ---- 3. value edges: MinValue / MaxValue / 0 / negative ----
        var de = new Dictionary<string, int>();
        de.Add("min", int.MinValue);
        de.Add("max", int.MaxValue);
        de.Add("zero", 0);
        de.Add("neg", -12345);
        Console.WriteLine("edge_min=" + (de["min"] == int.MinValue));
        Console.WriteLine("edge_max=" + (de["max"] == int.MaxValue));
        Console.WriteLine("edge_zero=" + (de["zero"] == 0));
        Console.WriteLine("edge_neg=" + (de["neg"] == -12345));
        Console.WriteLine("edge_count=" + (de.Count == 4));

        // ---- 4. duplicate Add -> ArgumentException, dict unchanged ----
        bool dupThrown = false;
        try { de.Add("min", 1); } catch (ArgumentException) { dupThrown = true; }
        Console.WriteLine("dup_add_thrown=" + dupThrown);
        Console.WriteLine("dup_count=" + (de.Count == 4));
        Console.WriteLine("dup_value_unchanged=" + (de["min"] == int.MinValue));

        // ---- 5. null key -> ArgumentNullException everywhere ----
        bool nkAdd = false, nkTry = false, nkContains = false, nkGet = false, nkSet = false, nkRemove = false;
        try { de.Add(null, 1); } catch (ArgumentNullException) { nkAdd = true; }
        try { int _; de.TryGetValue(null, out _); } catch (ArgumentNullException) { nkTry = true; }
        try { de.ContainsKey(null); } catch (ArgumentNullException) { nkContains = true; }
        try { int _ = de[null]; } catch (ArgumentNullException) { nkGet = true; }
        try { de[null] = 5; } catch (ArgumentNullException) { nkSet = true; }
        try { de.Remove(null); } catch (ArgumentNullException) { nkRemove = true; }
        Console.WriteLine("null_add_thrown=" + nkAdd);
        Console.WriteLine("null_tryget_thrown=" + nkTry);
        Console.WriteLine("null_contains_thrown=" + nkContains);
        Console.WriteLine("null_get_thrown=" + nkGet);
        Console.WriteLine("null_set_thrown=" + nkSet);
        Console.WriteLine("null_remove_thrown=" + nkRemove);

        // ---- 6. indexer set: insert-or-replace ----
        var di = new Dictionary<string, int>();
        di["one"] = 1;                       // insert
        di["two"] = 2;                       // insert
        Console.WriteLine("set_insert_count=" + (di.Count == 2));
        di["one"] = 11;                      // replace (present key)
        Console.WriteLine("set_replace_value=" + (di["one"] == 11));
        Console.WriteLine("set_replace_count=" + (di.Count == 2));
        di["three"] = 3;
        Console.WriteLine("set_second_insert_count=" + (di.Count == 3));

        // ---- 7. get_Item miss -> KeyNotFoundException ----
        bool missThrown = false;
        try { int _ = di["missing"]; } catch (Exception) { missThrown = true; }
        Console.WriteLine("get_miss_thrown=" + missThrown);
        Console.WriteLine("get_miss_count_unchanged=" + (di.Count == 3));

        // ---- 8. TryGetValue hit/miss ----
        int tv;
        Console.WriteLine("tryget_hit=" + (di.TryGetValue("two", out tv) && tv == 2));
        Console.WriteLine("tryget_miss=" + !di.TryGetValue("nope", out tv));

        // ---- 9. ordinal key content (no folding) ----
        var dk = new Dictionary<string, int>();
        dk.Add("Key", 1);
        Console.WriteLine("ordinal_contains_upper=" + dk.ContainsKey("Key"));
        Console.WriteLine("ordinal_misses_lower=" + !dk.ContainsKey("key"));
        Console.WriteLine("ordinal_misses_mixed=" + !dk.ContainsKey("KEY"));
        dk.Add("key", 2);
        Console.WriteLine("ordinal_both_count=" + (dk.Count == 2));
        Console.WriteLine("ordinal_lower_value=" + (dk["key"] == 2));
        Console.WriteLine("ordinal_upper_value=" + (dk["Key"] == 1));

        // ---- 10. Remove present / absent ----
        var dr = new Dictionary<string, int>();
        dr.Add("a", 1);
        dr.Add("b", 2);
        Console.WriteLine("remove_present=" + dr.Remove("a"));
        Console.WriteLine("remove_count_after=" + (dr.Count == 1));
        Console.WriteLine("remove_absent=" + !dr.Remove("a"));
        Console.WriteLine("remove_missing_key=" + !dr.Remove("zzz"));
        Console.WriteLine("remove_remaining_value=" + (dr["b"] == 2));

        // ---- 11. Clear + reuse ----
        var dl = new Dictionary<string, int>();
        for (int i = 0; i < 10; i++) dl.Add("key" + i, i);
        Console.WriteLine("fill_count=" + (dl.Count == 10));
        Console.WriteLine("fill_spot=" + (dl["key7"] == 7));
        dl.Clear();
        Console.WriteLine("clear_count=" + (dl.Count == 0));
        Console.WriteLine("clear_tryget_miss=" + !dl.TryGetValue("key7", out v0));
        dl.Add("again", 99);
        Console.WriteLine("reuse_count=" + (dl.Count == 1));
        Console.WriteLine("reuse_value=" + (dl["again"] == 99));

        // ---- 12. long-lived: many adds + interleaved removes, final counts ----
        var dg = new Dictionary<string, int>();
        for (int i = 0; i < 50; i++) dg.Add("g" + i, i * 2);
        Console.WriteLine("grow_count=" + (dg.Count == 50));
        for (int i = 0; i < 25; i++) dg.Remove("g" + i);
        Console.WriteLine("shrink_count=" + (dg.Count == 25));
        Console.WriteLine("shrink_removed=" + !dg.ContainsKey("g7"));
        Console.WriteLine("shrink_kept=" + dg.ContainsKey("g49"));
        Console.WriteLine("shrink_value=" + (dg["g49"] == 98));
        Console.WriteLine("done");
    }
}}
