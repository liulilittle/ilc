//------------------------------------------------------------------------------
// listops2.cs -- System.Collections.ArrayList DEEP e2e reference case
// (net48 oracle). Deepens listops (which PASSED at HEAD after the batch-24
// runtime fix "ArrayList IndexOf/Contains value semantics"):
//
//   1) IndexOf/Contains VALUE SEMANTICS across boxed primitives -- int vs
//      double (boxed 2 vs 2.0 are NOT equal in ArrayList: Object.Equals on
//      the box payload), bool, char, duplicate values (FIRST match), string
//      ordinal (case-sensitive) / reference elements, mixed int/string/double
//      in one list.
//   2) RemoveAt at HEAD / MIDDLE / TAIL + remove-all-down-to-empty; index
//      shift after removal; get_Item/set_Item at range boundaries.
//   3) Growth beyond the initial capacity (Add 12) + indexed traversal +
//      accumulation; Clear-then-reuse.
//   4) BOXED MIX: one list holding int + string + double + bool + char,
//      typed unbox-back reads, set_Item overwriting a slot with a DIFFERENT
//      type, cross-type IndexOf/Contains.
//
// DEFECT PROBE (recorded, NOT fixed -- batch rule): ArrayList.Insert(index,
// value) has NO MethodMap.ArrayList.cs mapping (the runtime arraylist.h
// exposes no insert icall) -> the resolver falls to the generated-name path
// -> undefined C++ identifier -> cmake build fails. The net48 oracle below
// pins the expected Insert semantics (insert-before semantics, head/mid/tail)
// so the defect is precisely characterized.
//
// Every active line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class ListOps2 {
    public static void Main() {
        // --- 1a. IndexOf/Contains value semantics: boxed int + duplicates ---
        ArrayList a = new ArrayList();
        a.Add(10); a.Add(20); a.Add(30); a.Add(20);
        Console.WriteLine("dup_idx=" + (a.IndexOf(20) == 1));
        Console.WriteLine("cnt_i=" + (a.Contains(20)));
        Console.WriteLine("cnt_i_neg=" + (a.Contains(99) == false));
        Console.WriteLine("idx_missing=" + (a.IndexOf(99) == -1));
        Console.WriteLine("cnt_after_growth=" + (a.Count == 4));

        // --- 1b. boxed int vs boxed double are NOT equal (Object.Equals) ---
        Console.WriteLine("dbl_vs_int_idx=" + (a.IndexOf(20.0) == -1));
        Console.WriteLine("dbl_vs_int_contains=" + (a.Contains(20.0) == false));

        // --- 1c. bool / char value semantics ---
        ArrayList bc = new ArrayList();
        bc.Add(true); bc.Add(false); bc.Add('x');
        Console.WriteLine("bc_cnt=" + (bc.Count == 3));
        Console.WriteLine("bc_idx_true=" + (bc.IndexOf(true) == 0));
        Console.WriteLine("bc_idx_false=" + (bc.IndexOf(false) == 1));
        Console.WriteLine("bc_char=" + (bc.IndexOf('x') == 2));
        Console.WriteLine("bc_char_neg=" + (bc.IndexOf('y') == -1));
        Console.WriteLine("bc_char_val=" + ((char)bc[2] == 'x'));
        Console.WriteLine("bc_bool_val=" + ((bool)bc[0] == true));

        // --- 1d. double value semantics (payload equality) ---
        ArrayList d = new ArrayList();
        d.Add(1.5); d.Add(2.0); d.Add(3.25);
        Console.WriteLine("d_idx=" + (d.IndexOf(2.0) == 1));
        Console.WriteLine("d_miss=" + (d.IndexOf(2) == -1));
        Console.WriteLine("d_contains=" + (d.Contains(3.25)));
        Console.WriteLine("d_neg=" + (d.Contains(2.5) == false));

        // --- 1e. string reference elements: ordinal + first match ---
        ArrayList strs = new ArrayList();
        strs.Add("alpha"); strs.Add("beta"); strs.Add("alpha");
        Console.WriteLine("s_idx_first=" + (strs.IndexOf("alpha") == 0));
        Console.WriteLine("s_contains=" + (strs.Contains("beta")));
        Console.WriteLine("s_miss=" + (strs.Contains("gamma") == false));
        Console.WriteLine("s_case=" + (strs.IndexOf("ALPHA") == -1));
        Console.WriteLine("s_val=" + ((string)strs[1] == "beta"));

        // --- 2. BOXED MIX: int + string + double + bool + char in one list ---
        ArrayList mx = new ArrayList();
        mx.Add(7);
        mx.Add("seven");
        mx.Add(7.0);
        mx.Add(true);
        mx.Add('7');
        Console.WriteLine("mx_cnt=" + (mx.Count == 5));
        Console.WriteLine("mx_int=" + ((int)mx[0] == 7));
        Console.WriteLine("mx_str=" + ((string)mx[1] == "seven"));
        Console.WriteLine("mx_dbl=" + ((double)mx[2] == 7.0));
        Console.WriteLine("mx_bool=" + ((bool)mx[3] == true));
        Console.WriteLine("mx_char=" + ((char)mx[4] == '7'));
        Console.WriteLine("mx_idx_int=" + (mx.IndexOf(7) == 0));
        Console.WriteLine("mx_idx_str=" + (mx.IndexOf("seven") == 1));
        Console.WriteLine("mx_idx_dbl=" + (mx.IndexOf(7.0) == 2));
        Console.WriteLine("mx_int_not_dbl=" + (mx.IndexOf(7) == 0 && mx.Contains(7.0) == true));
        Console.WriteLine("mx_char_idx=" + (mx.IndexOf('7') == 4));
        // set_Item overwriting a slot with a DIFFERENT type
        mx[0] = "seven";
        Console.WriteLine("mx_set_type=" + ((string)mx[0] == "seven"));
        Console.WriteLine("mx_set_cnt=" + (mx.Count == 5));
        Console.WriteLine("mx_set_idx=" + (mx.IndexOf("seven") == 0));
        Console.WriteLine("mx_set_old=" + (mx.IndexOf(7) == -1));

        // --- 3. RemoveAt at head / middle / tail + index shift ---
        ArrayList r = new ArrayList();
        for (int i = 0; i < 6; i++) r.Add(i * 10);
        r.RemoveAt(0);
        Console.WriteLine("rm_head=" + (r.Count == 5));
        Console.WriteLine("rm_head0=" + ((int)r[0] == 10));
        r.RemoveAt(2);
        Console.WriteLine("rm_mid=" + (r.Count == 4));
        Console.WriteLine("rm_mid2=" + ((int)r[2] == 40));
        r.RemoveAt(r.Count - 1);
        Console.WriteLine("rm_tail=" + (r.Count == 3));
        Console.WriteLine("rm_tail2=" + ((int)r[r.Count - 1] == 40));
        Console.WriteLine("rm_idx=" + (r.IndexOf(40) == 2));
        Console.WriteLine("rm_idx_shifted=" + (r.IndexOf(20) == 1));
        while (r.Count > 0) r.RemoveAt(0);
        Console.WriteLine("rm_empty=" + (r.Count == 0));

        // --- 4. growth beyond the initial capacity + traversal + Clear/reuse ---
        ArrayList g = new ArrayList();
        for (int i = 0; i < 12; i++) g.Add(i);
        Console.WriteLine("g_cnt=" + (g.Count == 12));
        Console.WriteLine("g_head=" + ((int)g[0] == 0));
        Console.WriteLine("g_mid=" + ((int)g[6] == 6));
        Console.WriteLine("g_tail=" + ((int)g[11] == 11));
        Console.WriteLine("g_idx=" + (g.IndexOf(11) == 11));
        int sum = 0;
        for (int i = 0; i < g.Count; i++) sum += (int)g[i];
        Console.WriteLine("g_sum=" + (sum == 66));
        Console.WriteLine("g_contains=" + (g.Contains(11)));
        g.Clear();
        g.Add(5);
        Console.WriteLine("reuse=" + (g.Count == 1 && (int)g[0] == 5));
        Console.WriteLine("reuse_idx=" + (g.IndexOf(5) == 0));

        // --- 5. IndexOf over range positions after removals ---
        ArrayList p = new ArrayList();
        p.Add("a"); p.Add("b"); p.Add("c"); p.Add("d");
        p.RemoveAt(1);
        Console.WriteLine("p_cnt=" + (p.Count == 3));
        Console.WriteLine("p0=" + ((string)p[0] == "a"));
        Console.WriteLine("p1=" + ((string)p[1] == "c"));
        Console.WriteLine("p2=" + ((string)p[2] == "d"));
        Console.WriteLine("p_idx=" + (p.IndexOf("d") == 2));

        // ===== DEFECT PROBE: Insert(index, value) is NOT wired (net48 oracle) =====
        ArrayList ins = new ArrayList();
        ins.Add(1); ins.Add(3);
        ins.Insert(1, 2);
        Console.WriteLine("ins_cnt=" + (ins.Count == 3));
        Console.WriteLine("ins_mid=" + ((int)ins[1] == 2));
        Console.WriteLine("ins_ord=" + ((int)ins[0] == 1 && (int)ins[2] == 3));
        ins.Insert(0, 0);
        Console.WriteLine("ins_head=" + ((int)ins[0] == 0));
        ins.Insert(ins.Count, 9);
        Console.WriteLine("ins_tail=" + ((int)ins[ins.Count - 1] == 9));
        Console.WriteLine("ins_len=" + (ins.Count == 5));
    }
}}
