//------------------------------------------------------------------------------
// listdeep3.cs -- System.Collections.ArrayList DEEP reference program #3
// (net48 oracle). Third deep pass over the arraylist domain after listops and
// listops2 -- this one targets the capacity-growth / insert-delete /
// ordering-stability dimensions (MethodMap.ArrayList.cs + arraylist.h/.cpp):
//
//   * CAPACITY GROWTH: Add 25 elements (multiple 4->8->16->32 doublings),
//     full-order preservation, IndexOf/Contains across growth.
//   * INSERT: at head / middle / tail / index==count (append); insert that
//     TRIGGERS growth at capacity; insert after shifts; net48
//     insert-before semantics verified exactly.
//   * DELETE: Remove(value) FIRST-match / duplicate / absent (silent no-op) /
//     null value; RemoveAt at head/middle/tail and empty-out.
//   * ORDERING STABILITY: a scripted interleave of Insert/Remove/RemoveAt
//     whose final element SEQUENCE is asserted element-by-element (the exact
//     order the net48 oracle produces) -- the "sort stability" dimension
//     reduced to its observable core: relative order is preserved through
//     every mutation.
//   * IndexOf drift after insert/remove; set_Item after growth.
//
// Every line prints "label=True/False"; net48 is the byte reference. All
// surfaces below are WIRED (no defect probes).
//------------------------------------------------------------------------------
using System;
using System.Collections;

class Program
{
    static void Main()
    {
        // ============ capacity growth: Add past initial capacity ============
        ArrayList a = new ArrayList();
        for (int i = 0; i < 25; i++) a.Add(i);
        Console.WriteLine("grow_cnt=" + (a.Count == 25));
        Console.WriteLine("grow_head=" + ((int)a[0] == 0));
        Console.WriteLine("grow_mid=" + ((int)a[12] == 12));
        Console.WriteLine("grow_tail=" + ((int)a[24] == 24));
        bool ok = true;
        for (int i = 0; i < 25; i++) if ((int)a[i] != i) ok = false;
        Console.WriteLine("grow_order=" + ok);
        Console.WriteLine("grow_idx_head=" + (a.IndexOf(0) == 0));
        Console.WriteLine("grow_idx_tail=" + (a.IndexOf(24) == 24));
        Console.WriteLine("grow_contains=" + (a.Contains(12)));
        Console.WriteLine("grow_contains_neg=" + (a.Contains(99) == false));

        // ============ Insert: head / middle / tail / at-count ============
        ArrayList b = new ArrayList();
        b.Add(1); b.Add(3);
        b.Insert(1, 2);                                   // middle, insert-before
        Console.WriteLine("ins_mid_cnt=" + (b.Count == 3));
        Console.WriteLine("ins_mid=" + ((int)b[1] == 2));
        Console.WriteLine("ins_mid_order=" + ((int)b[0] == 1 && (int)b[2] == 3));
        b.Insert(0, 0);                                   // head
        Console.WriteLine("ins_head=" + ((int)b[0] == 0 && (int)b[1] == 1));
        b.Insert(b.Count, 9);                             // index==count -> append
        Console.WriteLine("ins_append=" + ((int)b[b.Count - 1] == 9));
        Console.WriteLine("ins_len=" + (b.Count == 5));
        b.Insert(2, 99);                                  // middle after shifts
        Console.WriteLine("ins_shift=" + ((int)b[2] == 99 && (int)b[3] == 2));

        // ============ Insert triggering growth at capacity ============
        ArrayList c = new ArrayList();
        for (int i = 0; i < 4; i++) c.Add(i);             // fills initial capacity 4
        c.Insert(c.Count, 4);                             // 5th -> must grow
        Console.WriteLine("ins_grow_cnt=" + (c.Count == 5));
        Console.WriteLine("ins_grow_tail=" + ((int)c[4] == 4));
        Console.WriteLine("ins_grow_head_kept=" + ((int)c[0] == 0));
        c.Insert(0, -1);                                  // head insert into grown array
        Console.WriteLine("ins_grow_head=" + ((int)c[0] == -1 && (int)c[1] == 0));
        Console.WriteLine("ins_grow_order=" + ((int)c[5] == 4));

        // ============ Remove(value): first match / duplicate / absent / null ============
        ArrayList d = new ArrayList();
        d.Add(1); d.Add(2); d.Add(2); d.Add(3);
        d.Remove(2);                                      // removes the FIRST 2
        Console.WriteLine("rmv_first_cnt=" + (d.Count == 3));
        Console.WriteLine("rmv_first=" + ((int)d[0] == 1 && (int)d[1] == 2 && (int)d[2] == 3));
        d.Remove(99);                                     // absent -> silent no-op
        Console.WriteLine("rmv_absent=" + (d.Count == 3));
        Console.WriteLine("rmv_absent_order=" + ((int)d[1] == 2));
        ArrayList dn = new ArrayList();
        dn.Add("x"); dn.Add(null); dn.Add("y");
        dn.Remove(null);                                  // null value removal
        Console.WriteLine("rmv_null_cnt=" + (dn.Count == 2));
        Console.WriteLine("rmv_null=" + ((string)dn[0] == "x" && (string)dn[1] == "y"));
        dn.Remove("x");
        Console.WriteLine("rmv_str=" + (dn.Count == 1 && (string)dn[0] == "y"));
        dn.Remove("z");
        Console.WriteLine("rmv_str_absent=" + (dn.Count == 1));
        dn.Remove("y");
        Console.WriteLine("rmv_to_empty=" + (dn.Count == 0));

        // ============ RemoveAt: head / middle / tail / empty-out ============
        ArrayList e = new ArrayList();
        for (int i = 0; i < 8; i++) e.Add(i);
        e.RemoveAt(0);
        Console.WriteLine("rmat_head_cnt=" + (e.Count == 7));
        Console.WriteLine("rmat_head=" + ((int)e[0] == 1));
        e.RemoveAt(3);
        Console.WriteLine("rmat_mid=" + (e.Count == 6 && (int)e[3] == 5));
        e.RemoveAt(e.Count - 1);
        Console.WriteLine("rmat_tail=" + (e.Count == 5 && (int)e[4] == 6));
        while (e.Count > 0) e.RemoveAt(0);
        Console.WriteLine("rmat_empty=" + (e.Count == 0));

        // ============ interleaved insert/delete: ordering stability ============
        ArrayList f = new ArrayList();
        for (int i = 0; i < 10; i++) f.Add(i);
        f.RemoveAt(3);                                    // [0,1,2,4,5,6,7,8,9]
        f.Insert(3, 30);                                  // [0,1,2,30,4,5,6,7,8,9]
        f.Remove(6);                                      // [0,1,2,30,4,5,7,8,9]
        f.Insert(0, -5);                                  // [-5,0,1,2,30,4,5,7,8,9]
        f.Insert(f.Count, 99);                            // [-5,0,1,2,30,4,5,7,8,9,99]
        f.RemoveAt(2);                                    // [-5,0,2,30,4,5,7,8,9,99]
        Console.WriteLine("stab_cnt=" + (f.Count == 10));
        Console.WriteLine("stab_seq=" +
            ((int)f[0] == -5 && (int)f[1] == 0 && (int)f[2] == 2 && (int)f[3] == 30 &&
             (int)f[4] == 4 && (int)f[5] == 5 && (int)f[6] == 7 && (int)f[7] == 8 &&
             (int)f[8] == 9 && (int)f[9] == 99));
        Console.WriteLine("stab_idx30=" + (f.IndexOf(30) == 3));
        Console.WriteLine("stab_idx99=" + (f.IndexOf(99) == 9));
        Console.WriteLine("stab_idx_neg=" + (f.IndexOf(-5) == 0));
        Console.WriteLine("stab_contains=" + (f.Contains(7)));
        Console.WriteLine("stab_contains_gone=" + (f.Contains(6) == false));
        Console.WriteLine("stab_contains_neg=" + (f.Contains(1) == false));

        // ============ IndexOf drift across inserts/removes ============
        ArrayList g = new ArrayList();
        g.Add("a"); g.Add("b"); g.Add("c"); g.Add("b");
        Console.WriteLine("drift_b0=" + (g.IndexOf("b") == 1));
        g.RemoveAt(1);
        Console.WriteLine("drift_b1=" + (g.IndexOf("b") == 2));
        g.Insert(1, "b");
        Console.WriteLine("drift_b2=" + (g.IndexOf("b") == 1));
        g.Remove("b");
        Console.WriteLine("drift_b3=" + (g.IndexOf("b") == 2));
        g.Clear();
        Console.WriteLine("drift_empty=" + (g.Count == 0 && g.IndexOf("a") == -1));

        // ============ set_Item after growth + contains ============
        ArrayList h = new ArrayList();
        for (int i = 0; i < 20; i++) h.Add(i * 10);
        h[19] = 999;
        Console.WriteLine("set_grown_tail=" + ((int)h[19] == 999 && h.Count == 20));
        h[0] = -10;
        Console.WriteLine("set_grown_head=" + ((int)h[0] == -10));
        Console.WriteLine("set_grown_idx=" + (h.IndexOf(999) == 19));
        Console.WriteLine("set_grown_idx_head=" + (h.IndexOf(-10) == 0));
        Console.WriteLine("set_grown_mid=" + ((int)h[10] == 100));

        // ============ reverse-growth: clear then reuse ============
        ArrayList r = new ArrayList();
        for (int i = 0; i < 30; i++) r.Add(i);
        r.Clear();
        Console.WriteLine("reuse_clear=" + (r.Count == 0));
        r.Add(5); r.Add(6);
        Console.WriteLine("reuse_cnt=" + (r.Count == 2));
        Console.WriteLine("reuse_order=" + ((int)r[0] == 5 && (int)r[1] == 6));
        Console.WriteLine("reuse_idx=" + (r.IndexOf(6) == 1));
    }
}
