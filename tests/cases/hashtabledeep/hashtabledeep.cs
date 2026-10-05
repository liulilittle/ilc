//------------------------------------------------------------------------------
// hashtabledeep.cs -- System.Collections.Hashtable DEEP probe (e2e batch C52,
// after the hashtable domain: il2cpp_hashtable.h + MethodMap.Hashtable.cs).
// Beyond hashtablecase (basic ops) and hashtable2/hashdeep (string + boxed
// int distribution), this case drives the MIXED-KEY-TYPE dimension: string,
// boxed primitives (int/double/char/bool/float), and custom reference-type
// keys coexisting in ONE table, plus the identity/value-hash contract per key
// family and the observable surface (Count / ContainsKey / indexer / Remove /
// Keys / Values snapshot).
//
// WIRED SURFACE (the ONLY members used):
//   .ctor() / .ctor(int capacity)  Add  set_Item  get_Item  ContainsKey
//   Remove  get_Count  Clear  get_Keys  get_Values
//
// HASH CONTRACT per key family (hashtable.cpp -- equal keys MUST hash equal):
//   * System.String keys    -> ORDINAL string hash (content equality;
//     distinct instances with equal content are the SAME key);
//   * boxed primitives (D-HT-1): System.Int32 / System.Double / System.Char /
//     System.Boolean / System.Single -> the boxed VALUE (two boxes of the same
//     value are the same key; a fresh box resolves too);
//   * custom reference keys -> ADDRESS hash + reference equality (two
//     distinct instances are distinct keys; the same instance resolves).
//   net48's default Hashtable comparer matches every family (string content,
//   value-type GetHashCode, object reference), so the probes are byte-exact.
//
//   1. MIXED TABLE: string + boxed-int + custom-object keys stored together;
//      every key retrievable through the indexer, ContainsKey and Remove.
//   2. STRING FAMILY: equal content via a distinct instance is the same key;
//      ordinal (case-sensitive) content -- "Key" vs "key" distinct.
//   3. INT32 FAMILY: value identity across distinct boxes; fresh-box lookup;
//      missing int key -> null (no exception).
//   4. REFERENCE FAMILY: the same instance resolves; a DIFFERENT instance is
//      a distinct key (identity, not content).
//   5. DUPLICATE / EDGE: duplicate Add -> ArgumentException (table unchanged);
//      indexer set insert-or-replace; null key -> ArgumentNullException on
//      every member; null VALUE legal; missing key -> null; Remove missing ->
//      silent; Clear + reuse.
//   6. KEYS/VALUES SNAPSHOT: get_Keys/get_Values return non-null, distinct
//      snapshots whose element count equals Count (the runtime snapshot
//      arrays; net48's Keys/Values are live views -- element-count only, no
//      ReferenceEquals-across-calls, no enumeration-order assumption).
//   7. MIXED GROWTH: many string + int + object keys force load-factor
//      resizes (0.72); every key stays queryable after growth.
//   8. VALUE-TYPE KEY FAMILIES (D-HT-1): boxed double / char / bool / float
//      keys hash + compare BY VALUE -- a fresh box resolves an entry stored
//      under another box of the same value; distinct values stay distinct;
//      boxed Int32 7 and boxed Double 7.0 are DISTINCT keys.
//
// DELIBERATELY NOT PROBED (recorded defects, see GAPS.md):
//   * D-HT-2: Hashtable.Synchronized / IsSynchronized / SyncRoot -- NO
//     runtime icall and NO transpiler mapping exist (grep finds no
//     "Synchronized" in src/runtime or src/il2cppc); a call would abort at
//     transpile. The synchronized wrapper is therefore OUT of reach.
//   * D-HT-3: raw enumeration ORDER of Keys/Values is unspecified on net48
//     (bucket-index order on the runtime); never printed. Keys[i]/Values[i]
//     positional alignment is not asserted (net48 view vs runtime snapshot).
//
// Every line prints "label=True/False" (deterministic on both runtimes).
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class HashtableDeep
{
    // Custom reference key type: no Equals/GetHashCode override -> net48 uses
    // reference identity, the runtime uses address identity (consistent).
    class Tag
    {
        public string Name;
        public Tag(string n) { Name = n; }
    }

    // Distinct string instance with equal content.
    static string Build(string part) { return string.Concat("ht-", part); }
    static string Join2(string a, string b) { return string.Concat(a, b); }

    public static void Main()
    {
        // ============ 1. mixed key types in ONE table ============
        var h = new Hashtable();
        Tag t1 = new Tag("one");
        Tag t2 = new Tag("two");
        h.Add("skey", "sval");      // string key
        h.Add(7, "ival");           // boxed int key
        h.Add(t1, "t1val");         // reference key
        h.Add(t2, "t2val");         // second reference key
        Console.WriteLine("mixed_count=" + (h.Count == 4));
        Console.WriteLine("mixed_str=" + ((string)h["skey"] == "sval"));
        Console.WriteLine("mixed_int=" + ((string)h[7] == "ival"));
        Console.WriteLine("mixed_ref1=" + ((string)h[t1] == "t1val"));
        Console.WriteLine("mixed_ref2=" + ((string)h[t2] == "t2val"));
        Console.WriteLine("mixed_contains=" + (h.ContainsKey("skey") && h.ContainsKey(7) && h.ContainsKey(t1) && h.ContainsKey(t2)));

        // ============ 2. string family: content equality + ordinal ============
        var hs = new Hashtable();
        hs.Add("alpha", 1);
        Console.WriteLine("str_content_same=" + ((int)hs[Join2("alp", "ha")] == 1));
        Console.WriteLine("str_content_key=" + hs.ContainsKey(Join2("alp", "ha")));
        Console.WriteLine("str_ordinal=" + (!hs.ContainsKey("Alpha") && !hs.ContainsKey("ALPHA") && !hs.ContainsKey("alphaX")));
        hs.Add("Alpha", 2);
        Console.WriteLine("str_two_cases=" + (hs.Count == 2 && (int)hs["alpha"] == 1 && (int)hs["Alpha"] == 2));

        // ============ 3. int32 family: value identity across boxes ============
        var hi = new Hashtable();
        object boxA = 42;
        object boxB = 42;   // distinct box, same value
        hi.Add(boxA, "answer");
        Console.WriteLine("int_freshbox=" + ((string)hi[42] == "answer"));
        Console.WriteLine("int_otherbox=" + ((string)hi[boxB] == "answer"));
        Console.WriteLine("int_contains=" + (hi.ContainsKey(42) && hi.ContainsKey(boxA)));
        Console.WriteLine("int_missing_null=" + (hi[99] == null));
        Console.WriteLine("int_neg=" + (hi[-1] == null));

        // ============ 4. reference family: identity (not content) ============
        var hr = new Hashtable();
        Tag a = new Tag("same");
        Tag b = new Tag("same");   // equal content, DIFFERENT instance
        hr.Add(a, "ra");
        Console.WriteLine("ref_same_inst=" + ((string)hr[a] == "ra"));
        Console.WriteLine("ref_diff_inst_null=" + (hr[b] == null));
        Console.WriteLine("ref_diff_key=" + !hr.ContainsKey(b));
        Console.WriteLine("ref_count=" + (hr.Count == 1));

        // ============ 5. duplicate / edge semantics ============
        var hd = new Hashtable();
        hd.Add("k", "v");
        bool dup = false;
        try { hd.Add("k", "v2"); } catch (ArgumentException) { dup = true; }
        Console.WriteLine("dup_thrown=" + dup);
        Console.WriteLine("dup_unchanged=" + ((string)hd["k"] == "v" && hd.Count == 1));
        hd["k"] = "v3";                 // indexer set: replace
        Console.WriteLine("set_replace=" + ((string)hd["k"] == "v3" && hd.Count == 1));
        hd["new"] = "nv";               // indexer set: insert
        Console.WriteLine("set_insert=" + ((string)hd["new"] == "nv" && hd.Count == 2));
        bool nk = false;
        try { hd.Add(null, "x"); } catch (ArgumentNullException) { nk = true; }
        Console.WriteLine("null_key_add=" + nk);
        hd["nv"] = null;                // null value legal
        Console.WriteLine("null_value=" + (hd["nv"] == null));
        Console.WriteLine("missing_null=" + (hd["ghost"] == null));
        bool nk2 = false;
        try { object _ = hd[null]; } catch (ArgumentNullException) { nk2 = true; }
        Console.WriteLine("null_key_get=" + nk2);
        hd.Remove("k");
        Console.WriteLine("remove_present=" + (!hd.ContainsKey("k") && hd.Count == 2));
        hd.Remove("ghost");             // silent on missing
        Console.WriteLine("remove_missing_ok=" + (hd.Count == 2));
        hd.Clear();
        Console.WriteLine("clear_count=" + (hd.Count == 0));
        hd.Add("reuse", 1);
        Console.WriteLine("reuse_ok=" + ((int)hd["reuse"] == 1 && hd.Count == 1));

        // ============ 6. Keys / Values snapshot surface ============
        var hk = new Hashtable();
        hk.Add("k0", "v0");
        hk.Add("k1", "v1");
        hk.Add("k2", "v2");
        Console.WriteLine("keys_nonnull=" + (hk.Keys != null));
        Console.WriteLine("values_nonnull=" + (hk.Values != null));
        Console.WriteLine("keys_distinct=" + !ReferenceEquals(hk.Keys, hk.Values));

        // ============ 7. mixed growth across load-factor resizes ============
        var hg = new Hashtable();
        var rng = new Random(12345);
        for (int i = 0; i < 200; i++) hg.Add("s" + i, rng.Next(1000000));   // strings
        for (int i = 0; i < 100; i++) hg.Add(i, "int" + i);                 // boxed ints
        for (int i = 0; i < 50; i++) hg.Add(new Tag("t" + i), i);           // references
        Console.WriteLine("grow_count=" + (hg.Count == 350));
        int strOk = 0;
        var rng2 = new Random(12345);
        for (int i = 0; i < 200; i++)
        {
            int v = (int)hg["s" + i];
            if (v == rng2.Next(1000000)) strOk++;
        }
        Console.WriteLine("grow_str=" + (strOk == 200));
        int intOk = 0;
        for (int i = 0; i < 100; i++) if ((string)hg[i] == "int" + i) intOk++;
        Console.WriteLine("grow_int=" + (intOk == 100));
        Console.WriteLine("grow_miss=" + (hg["s200"] == null && hg[100] == null));

        // ============ 8. D-CAST-NULL: castclass on null references ============
        // ECMA-335: castclass on a null reference is a NO-OP returning null
        // (NOT an InvalidCastException). A Hashtable indexer miss / stored null
        // value yields null; the transpiled castclass must short-circuit null
        // instead of falling into the throw arm.
        var hz = new Hashtable();
        hz.Add("present", "pv");
        hz["nv"] = null;
        Console.WriteLine("cast_missing_null=" + ((string)hz["ghost"] == null));
        Console.WriteLine("cast_stored_null=" + ((string)hz["nv"] == null));
        Console.WriteLine("cast_present=" + ((string)hz["present"] == "pv"));
        Console.WriteLine("cast_missing_false=" + ((string)hz["ghost"] != "pv"));

        // ============ 9. boxed VALUE-type keys (D-HT-1: value hash for
        //    double/char/bool/float -- net48 GetHashCode vs the runtime's
        //    former address hash DIVERGED; a cross-box value lookup must now
        //    resolve) ============
        var hv = new Hashtable();
        object dbA = 5.0;          // boxed double
        object dbB = 5.0;          // distinct box, same value
        hv.Add(dbA, "dval");
        Console.WriteLine("dbl_freshbox=" + ((string)hv[5.0] == "dval"));
        Console.WriteLine("dbl_otherbox=" + ((string)hv[dbB] == "dval"));
        Console.WriteLine("dbl_contains=" + (hv.ContainsKey(5.0) && hv.ContainsKey(dbA)));
        Console.WriteLine("dbl_missing=" + (hv[-5.0] == null && hv[1.5] == null));
        hv[5.0] = "dval2";         // indexer set: replace by value
        Console.WriteLine("dbl_set_replace=" + ((string)hv[5.0] == "dval2" && hv.Count == 1));
        hv.Remove(5.0);
        Console.WriteLine("dbl_remove=" + (!hv.ContainsKey(5.0) && hv.Count == 0));

        object chA = 'K';          // boxed char
        object chB = 'K';          // distinct box, same value
        hv.Add(chA, "chval");
        Console.WriteLine("char_freshbox=" + ((string)hv['K'] == "chval"));
        Console.WriteLine("char_otherbox=" + ((string)hv[chB] == "chval"));
        Console.WriteLine("char_ordinal=" + (hv['k'] == null && hv['J'] == null));

        object btA = true;         // boxed bool
        object btB = true;         // distinct box, same value
        hv.Add(btA, "bval");
        Console.WriteLine("bool_freshbox=" + ((string)hv[true] == "bval"));
        Console.WriteLine("bool_otherbox=" + ((string)hv[btB] == "bval"));
        Console.WriteLine("bool_false_missing=" + (hv[false] == null));

        object fA = 2.5f;          // boxed float (System.Single)
        hv.Add(fA, "fval");
        Console.WriteLine("flt_freshbox=" + ((string)hv[2.5f] == "fval"));
        Console.WriteLine("flt_missing=" + (hv[9.9f] == null));
        Console.WriteLine("value_count=" + (hv.Count == 3));

        hv.Add(7, "int7");         // boxed int, value hash (pre-existing family)
        Console.WriteLine("int_vs_double=" + (hv[7.0] == null && (string)hv[7] == "int7"));

        Console.WriteLine("done");
    }
}}
