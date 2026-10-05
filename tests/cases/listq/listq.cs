//------------------------------------------------------------------------------
// listq.cs -- System.Collections.Generic.List<int> FULL-OPERATION combination
// deep probe (e2e batch C71). Builds on genlist (the minimal List<int> first
// case: ctor/Add/Count/get_Item) and the LINQ materializations (linqdeep/
// linqops ToList) with a dedicated combination case over the ENTIRE wired
// List`1<int> icall surface (MethodMap.List.cs -> il2cpp_list_i4_*):
//
//   * create / Add / get_Item / Count / Clear / RemoveAt / IndexOf / Contains
//     + set_Item / Insert / Remove / ToArray / capacity ctors (D-LISTQ-1..5)
//
// The case exercises the list as a SEQUENCE (the "queue" axis of the name):
// Add appends at the tail, RemoveAt(0) consumes the head -- a FIFO discipline
// over the indexable surface -- interleaved with random-access reads,
// value-lookup (IndexOf/Contains), tail/mid removals and Clear reuse.
//
//   1. TAIL-APPEND + HEAD-CONSUME FIFO: Add 1..N, then RemoveAt(0) N times --
//      the exact net48 element order must come back; Count tracks the live
//      size through the drain.
//   2. INTERLEAVED ADD/READ: append a run, read every slot via get_Item (both
//      constant and computed indexes), sum the values; IndexOf/Contains on
//      present/absent values; first-match IndexOf on duplicates.
//   3. REMOVE SHAPES: RemoveAt(0) head / RemoveAt(mid) / RemoveAt(Count-1)
//      tail, element order preserved after each shift (element-by-element
//      assert); remove all the way down to empty.
//   4. GROWTH: Add past the initial capacity (25+ elements, multiple
//      doublings), full order preservation + IndexOf/Contains across growth.
//   5. CLEAR + REUSE: Clear() -> Count 0, IndexOf -1, Contains false; re-Add
//      and verify the fresh contents.
//   6. QUEUE MIX: a scripted Add/RemoveAt(0)/IndexOf sequence whose final
//      element sequence is asserted element-by-element (FIFO discipline over
//      mutations).
//
// Every line is a labeled bool; net48 is the byte reference (the runtime
// implements the exact List<int> doubling/remove-shift semantics of
// il2cpp_list_i4, listi4.h).
//
// ADDED (D-LISTQ-1..5, batch b23): the surface below now also probes the
// previously-gapped members, now wired in MethodMap.List.cs + listi4.cpp:
//   * set_Item (list[i] = v)          -- overwrite + out-of-range throw
//   * Insert(index, value)            -- front/middle/end + bounds + shift
//   * Remove(value)                   -- first-equal / absent / order
//   * ToArray()                       -- exact length, content, independence
//   * capacity ctors                  -- positive pre-size, zero, negative
//                                        (ArgumentOutOfRangeException)
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class Listq {
    public static void Main() {
        // ===== 1. tail-append + head-consume FIFO =====
        var q = new List<int>();
        for (int i = 0; i < 6; i++) q.Add(i + 1);          // 1..6
        Console.WriteLine("q_init=" + (q.Count == 6));
        Console.WriteLine("q_head=" + (q[0] == 1));
        Console.WriteLine("q_tail=" + (q[5] == 6));
        bool drainOk = true;
        for (int i = 0; i < 6; i++)
        {
            if (q[0] != i + 1) drainOk = false;
            q.RemoveAt(0);
        }
        Console.WriteLine("q_drain=" + drainOk);
        Console.WriteLine("q_empty=" + (q.Count == 0));

        // ===== 2. interleaved add/read + value lookup =====
        var l = new List<int>();
        l.Add(10); l.Add(20); l.Add(30); l.Add(20);
        Console.WriteLine("l_cnt=" + (l.Count == 4));
        Console.WriteLine("l0=" + (l[0] == 10));
        Console.WriteLine("l3=" + (l[3] == 20));
        Console.WriteLine("l_first20=" + (l.IndexOf(20) == 1));
        Console.WriteLine("l_contains=" + (l.Contains(20)));
        Console.WriteLine("l_missing=" + (l.IndexOf(99) == -1));
        Console.WriteLine("l_contains_neg=" + (l.Contains(99) == false));
        int lsum = 0;
        for (int i = 0; i < l.Count; i++) lsum += l[i];
        Console.WriteLine("l_sum=" + (lsum == 80));

        // ===== 3. remove shapes: head / mid / tail + shift order =====
        var r = new List<int>();
        for (int i = 0; i < 6; i++) r.Add(i * 10);         // 0,10,20,30,40,50
        r.RemoveAt(0);
        Console.WriteLine("r_head_cnt=" + (r.Count == 5));
        Console.WriteLine("r_head0=" + (r[0] == 10));
        r.RemoveAt(2);
        Console.WriteLine("r_mid_cnt=" + (r.Count == 4));
        Console.WriteLine("r_mid=" + (r[2] == 40));
        r.RemoveAt(r.Count - 1);
        Console.WriteLine("r_tail_cnt=" + (r.Count == 3));
        Console.WriteLine("r_tail=" + (r[r.Count - 1] == 40));
        Console.WriteLine("r_ord=" + (r[0] == 10 && r[1] == 20 && r[2] == 40));
        while (r.Count > 0) r.RemoveAt(0);
        Console.WriteLine("r_empty=" + (r.Count == 0));

        // ===== 4. growth past initial capacity (order + lookup preserved) =====
        var g = new List<int>();
        bool growOrd = true;
        for (int i = 0; i < 25; i++)
        {
            g.Add(i);
            if (g[g.Count - 1] != i) growOrd = false;
        }
        Console.WriteLine("g_cnt=" + (g.Count == 25));
        Console.WriteLine("g_ord=" + growOrd);
        Console.WriteLine("g_head=" + (g[0] == 0));
        Console.WriteLine("g_mid=" + (g[12] == 12));
        Console.WriteLine("g_tail=" + (g[24] == 24));
        Console.WriteLine("g_idx_head=" + (g.IndexOf(0) == 0));
        Console.WriteLine("g_idx_tail=" + (g.IndexOf(24) == 24));
        Console.WriteLine("g_contains=" + (g.Contains(12)));
        Console.WriteLine("g_contains_neg=" + (g.Contains(99) == false));

        // ===== 5. Clear + reuse =====
        var c = new List<int>();
        c.Add(1); c.Add(2); c.Add(3);
        c.Clear();
        Console.WriteLine("c_empty=" + (c.Count == 0));
        Console.WriteLine("c_idx=" + (c.IndexOf(1) == -1));
        Console.WriteLine("c_contains=" + (c.Contains(2) == false));
        c.Add(7); c.Add(8);
        Console.WriteLine("c_reuse_cnt=" + (c.Count == 2));
        Console.WriteLine("c_reuse0=" + (c[0] == 7));
        Console.WriteLine("c_reuse1=" + (c[1] == 8));
        Console.WriteLine("c_reuse_idx=" + (c.IndexOf(8) == 1));

        // ===== 6. queue mix: scripted Add/RemoveAt(0)/IndexOf sequence =====
        var m = new List<int>();
        for (int i = 0; i < 10; i++) m.Add(i);
        m.RemoveAt(0);                                     // [1..9]
        m.RemoveAt(0);                                     // [2..9]
        m.RemoveAt(0);                                     // [3..9]
        Console.WriteLine("m_cnt=" + (m.Count == 7));
        Console.WriteLine("m_head=" + (m[0] == 3));
        m.Add(100);                                        // [3..9,100]
        m.RemoveAt(0);                                     // [4..9,100]
        Console.WriteLine("m_mid=" + (m.IndexOf(6) == 2));
        Console.WriteLine("m_tail=" + (m[m.Count - 1] == 100));
        Console.WriteLine("m_idx_new=" + (m.IndexOf(100) == m.Count - 1));
        Console.WriteLine("m_gone=" + (m.Contains(2) == false));
        Console.WriteLine("m_seq=" + (m[0] == 4 && m[1] == 5 && m[2] == 6 && m[3] == 7 &&
                                      m[4] == 8 && m[5] == 9 && m[6] == 100));

        // ===== 7. set_Item (list[i] = v): overwrite + out-of-range =====
        var s = new List<int>();
        s.Add(10); s.Add(20); s.Add(30);
        s[1] = 25;
        Console.WriteLine("s_set=" + (s[1] == 25));
        Console.WriteLine("s_cnt=" + (s.Count == 3));
        Console.WriteLine("s_untouched=" + (s[0] == 10 && s[2] == 30));
        bool sOor = false;
        try { s[3] = 1; } catch (ArgumentOutOfRangeException) { sOor = true; }
        Console.WriteLine("s_oor=" + sOor);
        bool sOorNeg = false;
        try { s[-1] = 1; } catch (ArgumentOutOfRangeException) { sOorNeg = true; }
        Console.WriteLine("s_oor_neg=" + sOorNeg);
        Console.WriteLine("s_cnt_after=" + (s.Count == 3));

        // ===== 8. Insert(index, value): front / middle / end / bounds =====
        var ins = new List<int>();
        ins.Add(1); ins.Add(3);
        ins.Insert(1, 2);
        Console.WriteLine("i_cnt=" + (ins.Count == 3));
        Console.WriteLine("i_mid=" + (ins[0] == 1 && ins[1] == 2 && ins[2] == 3));
        ins.Insert(0, 0);
        Console.WriteLine("i_head=" + (ins[0] == 0 && ins[1] == 1));
        ins.Insert(ins.Count, 9);
        Console.WriteLine("i_append=" + (ins[ins.Count - 1] == 9));
        Console.WriteLine("i_len=" + (ins.Count == 5));
        ins.Insert(2, 99);
        Console.WriteLine("i_shift=" + (ins[2] == 99 && ins[3] == 2));
        bool iOorNeg = false;
        try { ins.Insert(-1, 5); } catch (ArgumentOutOfRangeException) { iOorNeg = true; }
        Console.WriteLine("i_oor_neg=" + iOorNeg);
        bool iOorOver = false;
        try { ins.Insert(7, 5); } catch (ArgumentOutOfRangeException) { iOorOver = true; }
        Console.WriteLine("i_oor_over=" + iOorOver);
        Console.WriteLine("i_len_after=" + (ins.Count == 6));

        // ===== 9. Remove(value): first-equal / absent / order =====
        var rm = new List<int>();
        rm.Add(7); rm.Add(3); rm.Add(7);
        Console.WriteLine("r_rem7=" + rm.Remove(7));
        Console.WriteLine("r_cnt=" + (rm.Count == 2));
        Console.WriteLine("r_keep_later=" + (rm[0] == 3 && rm[1] == 7));
        Console.WriteLine("r_rem3=" + rm.Remove(3));
        Console.WriteLine("r_cnt2=" + (rm.Count == 1));
        Console.WriteLine("r_absent=" + (rm.Remove(99) == false));
        Console.WriteLine("r_cnt3=" + (rm.Count == 1));
        Console.WriteLine("r_last=" + (rm[0] == 7));

        // ===== 10. ToArray(): exact length, content, independence =====
        var ta = new List<int>();
        ta.Add(5); ta.Add(6); ta.Add(7);
        int[] tarr = ta.ToArray();
        Console.WriteLine("t_len=" + (tarr.Length == 3));
        Console.WriteLine("t_elems=" + (tarr[0] == 5 && tarr[1] == 6 && tarr[2] == 7));
        tarr[1] = 999;
        Console.WriteLine("t_indep=" + (ta[1] == 6));
        var te = new List<int>();
        int[] tempty = te.ToArray();
        Console.WriteLine("t_empty=" + (tempty.Length == 0));

        // ===== 11. capacity ctors: pre-size / zero / negative =====
        var cap = new List<int>(10);
        Console.WriteLine("c10_cnt=" + (cap.Count == 0));
        bool capOrd = true;
        for (int i = 0; i < 11; i++)
        {
            cap.Add(i * 7);
            if (cap[cap.Count - 1] != i * 7) capOrd = false;
        }
        Console.WriteLine("c10_after=" + (cap.Count == 11));
        Console.WriteLine("c10_ord=" + capOrd);
        Console.WriteLine("c10_tail=" + (cap[10] == 70));
        var c0 = new List<int>(0);
        Console.WriteLine("c0_cnt=" + (c0.Count == 0));
        c0.Add(42);
        Console.WriteLine("c0_add=" + (c0.Count == 1 && c0[0] == 42));
        bool cNeg = false;
        try { var _ = new List<int>(-1); } catch (ArgumentOutOfRangeException) { cNeg = true; }
        Console.WriteLine("c_neg=" + cNeg);
        bool cNeg2 = false;
        try { var _ = new List<int>(-100); } catch (ArgumentOutOfRangeException) { cNeg2 = true; }
        Console.WriteLine("c_neg2=" + cNeg2);
    }
}}
