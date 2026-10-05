//------------------------------------------------------------------------------
// arraylistdeep.cs -- System.Collections.ArrayList DEEP e2e reference case #2
// (net48 oracle, byte-for-byte). Created in e2e batch C67 (2026-08-12), AFTER
// the arraylist domain wiring (MethodMap.ArrayList.cs + arraylist.h/.cpp).
//
// DEEP DIMENSIONS (all surfaces below are WIRED):
//   1. ORDERING STABILITY ("sort-order" observable): scripted interleaves of
//      Insert / Remove / RemoveAt whose final element SEQUENCE is asserted
//      element-by-element (net48 produces exactly this order). Insert at
//      head / middle / tail / index==count (append), Remove(value) first-match
//      with duplicates, RemoveAt with shift, set_Item overwrite -- every
//      mutation keeps the surviving elements in their relative order.
//   2. SEARCH SEMANTICS ("binary-search"-adjacent): IndexOf / Contains value
//      semantics -- boxed int vs boxed double are NOT equal, char, bool,
//      string ordinal (case-sensitive), NULL elements, duplicates return the
//      FIRST match, absent returns -1. IndexOf across a grown list.
//   3. CAPACITY GROWTH with ordering preservation: Add 40 elements (multiple
//      4->8->16->32->64 doublings), full-order verification, Count/IndexOf
//      after growth, set_Item into the grown tail, Clear + reuse.
//   4. VALUE-EQUALITY boundary: two distinct boxed ints with the same payload
//      are EQUAL for IndexOf/Contains; boxed int vs boxed double are NOT.
//   5. EXCEPTION PATHS: get_Item(-1) / get_Item(Count) / set_Item(-1) /
//      RemoveAt(-1) / RemoveAt(Count) / Insert(-1) / Insert(Count+1) ->
//      ArgumentOutOfRangeException; RemoveAt on empty; get_Item on empty.
//
// NOT WIRED (recorded in GAPS.md, NOT asserted): ArrayList.Sort / BinarySearch
// / SyncRoot / Synchronized / ToArray / GetEnumerator -- no MethodMap.ArrayList
// case and no runtime icall (D-AL-1).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections;

class Program
{
    static void Main()
    {
        // ============ 1. ordering stability: interleaved mutations ============
        ArrayList a = new ArrayList();
        for (int t = 0; t < 6; t++) a.Add(t);              // [0,1,2,3,4,5]
        a.RemoveAt(2);                                     // [0,1,3,4,5]
        a.Insert(2, 30);                                   // [0,1,30,3,4,5]
        a.Remove(4);                                       // [0,1,30,3,5]
        a.Insert(0, -9);                                   // [-9,0,1,30,3,5]
        a.Insert(a.Count, 99);                             // [-9,0,1,30,3,5,99]
        a.RemoveAt(a.Count - 1);                           // [-9,0,1,30,3,5]
        a.Remove(-9);                                      // [0,1,30,3,5]
        Console.WriteLine("stab_cnt=" + (a.Count == 5));
        Console.WriteLine("stab_seq=" + ((int)a[0] == 0 && (int)a[1] == 1 && (int)a[2] == 30 &&
                                          (int)a[3] == 3 && (int)a[4] == 5));
        Console.WriteLine("stab_idx30=" + (a.IndexOf(30) == 2));
        Console.WriteLine("stab_idx_absent=" + (a.IndexOf(4) == -1));
        Console.WriteLine("stab_contains=" + (a.Contains(30)));
        Console.WriteLine("stab_contains_neg=" + (a.Contains(4) == false));

        // ============ 2. Insert at head / middle / tail / index==count ============
        ArrayList b = new ArrayList();
        b.Add(1); b.Add(4);
        b.Insert(1, 2); b.Insert(2, 3);                   // [1,2,3,4]
        Console.WriteLine("ins_seq=" + ((int)b[0] == 1 && (int)b[1] == 2 && (int)b[2] == 3 && (int)b[3] == 4));
        b.Insert(0, 0);                                   // head
        Console.WriteLine("ins_head=" + ((int)b[0] == 0 && (int)b[1] == 1));
        b.Insert(b.Count, 9);                             // index==count -> append
        Console.WriteLine("ins_append=" + ((int)b[b.Count - 1] == 9));
        Console.WriteLine("ins_cnt=" + (b.Count == 6));
        b.Insert(2, 55);                                  // middle after shifts
        Console.WriteLine("ins_mid=" + ((int)b[2] == 55 && (int)b[3] == 2));

        // ============ 3. Remove(value): first match / duplicate / absent ============
        ArrayList c = new ArrayList();
        c.Add("x"); c.Add("y"); c.Add("x"); c.Add("z");
        c.Remove("x");                                    // removes the FIRST x
        Console.WriteLine("rmv_first=" + (c.Count == 3 && (string)c[0] == "y" && (string)c[1] == "x"));
        c.Remove("q");                                    // absent -> no-op
        Console.WriteLine("rmv_absent=" + (c.Count == 3));
        c.Remove("x");                                    // second x
        Console.WriteLine("rmv_second=" + (c.Count == 2 && (string)c[1] == "z"));

        // ============ 4. search semantics: value equality + type sensitivity ============
        ArrayList d = new ArrayList();
        d.Add(10); d.Add(20); d.Add(10);                  // boxed ints
        Console.WriteLine("srch_int_first=" + (d.IndexOf(10) == 0));
        Console.WriteLine("srch_int_boxed_eq=" + (d.Contains(10)));      // boxed payload equal
        Console.WriteLine("srch_int_vs_dbl=" + (d.IndexOf(10.0) == -1)); // int vs double NOT equal
        Console.WriteLine("srch_dbl_vs_int=" + (d.Contains(20.0) == false));
        ArrayList e = new ArrayList();
        e.Add("alpha"); e.Add("beta");
        Console.WriteLine("srch_str_ord=" + (e.IndexOf("ALPHA") == -1)); // ordinal, case-sensitive
        Console.WriteLine("srch_str=" + (e.IndexOf("beta") == 1));
        ArrayList f = new ArrayList();
        f.Add(true); f.Add('k'); f.Add(-7);
        Console.WriteLine("srch_bool=" + (f.IndexOf(true) == 0));
        Console.WriteLine("srch_char=" + (f.IndexOf('k') == 1));
        Console.WriteLine("srch_neg=" + (f.IndexOf(-7) == 2));
        Console.WriteLine("srch_bool_vs_char=" + (f.Contains('t') == false));
        ArrayList g = new ArrayList();
        g.Add(null); g.Add("a"); g.Add(null);
        Console.WriteLine("srch_null=" + (g.IndexOf(null) == 0));
        Console.WriteLine("srch_null2=" + (g.Contains(null)));
        Console.WriteLine("srch_null_absent=" + (g.IndexOf("zzz") == -1));

        // ============ 5. capacity growth + ordering preservation ============
        ArrayList h = new ArrayList();
        for (int t = 0; t < 40; t++) h.Add(t * 3);
        bool growOk = true;
        for (int t = 0; t < 40; t++) if ((int)h[t] != t * 3) growOk = false;
        Console.WriteLine("grow_order=" + growOk);
        Console.WriteLine("grow_cnt=" + (h.Count == 40));
        Console.WriteLine("grow_idx0=" + (h.IndexOf(0) == 0));
        Console.WriteLine("grow_idxmid=" + (h.IndexOf(57) == 19));
        Console.WriteLine("grow_idxtail=" + (h.IndexOf(117) == 39));
        Console.WriteLine("grow_contains=" + (h.Contains(99)));
        Console.WriteLine("grow_contains_neg=" + (h.Contains(98) == false));
        h[39] = -1;                                       // set_Item into grown tail
        Console.WriteLine("grow_set_tail=" + ((int)h[39] == -1 && h.Count == 40));
        Console.WriteLine("grow_set_idx=" + (h.IndexOf(-1) == 39));
        h.Clear();
        h.Add(7); h.Add(8);
        Console.WriteLine("reuse=" + (h.Count == 2 && (int)h[0] == 7 && (int)h[1] == 8));

        // ============ 6. value-equality boundary: distinct boxed payloads ============
        ArrayList i = new ArrayList();
        i.Add(new Random(1).Next(1000000));               // runtime-known boxed int
        i.Add(new Random(2).Next(1000000));
        Console.WriteLine("boxed_distinct=" + (i.Count == 2));
        Console.WriteLine("boxed_idx_self=" + (i.IndexOf(i[0]) == 0));
        int v0 = (int)i[0];
        Console.WriteLine("boxed_payload=" + (i.IndexOf(v0) == 0));   // same payload found
        Console.WriteLine("boxed_contains_self=" + (i.Contains(i[0])));
        Console.WriteLine("boxed_contains_payload=" + (i.Contains(v0)));

        // ============ 7. exception paths (net48 ArgumentOutOfRangeException) ============
        ArrayList j = new ArrayList();
        j.Add("only");
        bool threw = false;
        try { object t = j[-1]; } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_get_neg=" + threw);
        threw = false;
        try { object t = j[1]; } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_get_cnt=" + threw);
        threw = false;
        try { j[-1] = "x"; } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_set_neg=" + threw);
        threw = false;
        try { j.RemoveAt(-1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_rmat_neg=" + threw);
        threw = false;
        try { j.RemoveAt(1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_rmat_cnt=" + threw);
        threw = false;
        try { j.Insert(-1, "x"); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_ins_neg=" + threw);
        threw = false;
        try { j.Insert(2, "x"); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_ins_cnt1=" + threw);
        ArrayList k = new ArrayList();
        threw = false;
        try { object t = k[0]; } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_empty_get=" + threw);
        threw = false;
        try { k.RemoveAt(0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_empty_rmat=" + threw);
        threw = false;
        try { k.Insert(1, "x"); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("exc_empty_ins=" + threw);

        // ============ 8. D-AL-2 regression: Insert into a FRESH list ============
        // The e2e newobj path allocates via il2cpp_object_new (items == null);
        // Insert used to call GrowList which dereferenced the null items array
        // (0xC0000005 crash). GrowList now runs EnsureItems first (D-AL-2 fixed).
        ArrayList m = new ArrayList();                    // never-Add-ed (items == null)
        m.Insert(0, "fresh0");                            // was: crash
        Console.WriteLine("fresh_ins_cnt=" + (m.Count == 1));
        Console.WriteLine("fresh_ins_val=" + ((string)m[0] == "fresh0"));
        m.Insert(0, "fresh1");                            // head shift on the fresh list
        Console.WriteLine("fresh_ins_head=" + ((string)m[0] == "fresh1" && (string)m[1] == "fresh0"));
        m.Insert(m.Count, "fresh2");                      // index==count append
        Console.WriteLine("fresh_ins_append=" + ((string)m[2] == "fresh2" && m.Count == 3));
        m.Insert(1, "fresh3");                            // middle
        Console.WriteLine("fresh_ins_mid=" + ((string)m[1] == "fresh3" && m.Count == 4));
        Console.WriteLine("fresh_ins_idx0=" + (m.IndexOf("fresh1") == 0));
        Console.WriteLine("fresh_ins_idx1=" + (m.IndexOf("fresh3") == 1));
        Console.WriteLine("fresh_ins_contains=" + (m.Contains("fresh2")));
        for (int t = 0; t < 6; t++) m.Insert(m.Count, t); // grow the fresh list
        Console.WriteLine("fresh_ins_grow=" + (m.Count == 10 && (int)m[9] == 5 && (int)m[4] == 0));
        ArrayList n = new ArrayList();
        n.Insert(0, null);                                // null element into a fresh list
        Console.WriteLine("fresh_ins_null=" + (n.Count == 1 && n[0] == null && n.IndexOf(null) == 0));
    }
}