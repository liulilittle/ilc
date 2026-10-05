//------------------------------------------------------------------------------
// stackops2.cs -- System.Collections.Stack / Queue operations DEEP probe 2
// (e2e batch C68): enumerator / capacity-growth / mixed -- byte-exact vs the
// net48 oracle. Goes beyond stackqdeep (mixed round trips + GC pressure +
// ToArray enumeration + empty-exception + clear/reuse) into the surface the
// C42 sqenum-b23 enumerator wiring opened:
//
//   S1 ENUMERATOR deep: foreach order (Stack top-first / Queue front-first),
//      manual IEnumerator MoveNext/get_Current/Reset on the snapshot
//      enumerator, end-of-enumeration, empty-container enumeration, and a
//      WRAPPED-ring Queue enumeration (head != 0 after dequeue+enqueue).
//      D-STK-1 (resolved): net48 Stack/Queue enumerators are VERSION-checked
//      -- mutating the container after GetEnumerator throws
//      InvalidOperationException on the next MoveNext/Reset; the runtime now
//      matches (a container version counter + a per-MoveNext/Reset check), so
//      the probes below assert the net48 throw for every mutator.
//   S2 CAPACITY / GROWTH deep: Stack doubling growth (4->8->16->32) with the
//      LIFO order intact after 100 pushes; Queue ring growth + wrap cycles
//      (head walks the ring without growing while count < capacity, then a
//      growth re-layout resets head); growth-then-GC enumeration; capacity
//      pressure via repeated mixed enqueue/dequeue.
//   S3 MIXED deep: mixed-type round trips THROUGH the enumerator and ToArray
//      (boxed bool/int/string/char/long), Stack vs Queue cross-check of the
//      same pushed/enqueued sequence, and interleaved push/pop/foreach
//      state machine.
//
// All probes print bools/ints -- deterministic across console encodings.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class StackOps2 {
    public static void Main() {
        // ================= S1: enumerator deep =================
        // Stack foreach: TOP-of-stack first (3,2,1,0)
        Stack s = new Stack();
        for (int i = 0; i < 4; i++) s.Push(i);
        int cnt = 0; bool sok = true;
        foreach (object o in s) { sok = sok && ((int)o == 3 - cnt); cnt++; }
        Console.WriteLine("se_count=" + (cnt == 4));
        Console.WriteLine("se_order=" + sok);
        Console.WriteLine("se_untouched=" + (s.Count == 4));

        // Queue foreach: FRONT-first (0,1,2,3)
        Queue q = new Queue();
        for (int i = 0; i < 4; i++) q.Enqueue(10 + i);
        cnt = 0; bool qok = true;
        foreach (object o in q) { qok = qok && ((int)o == 10 + cnt); cnt++; }
        Console.WriteLine("qe_count=" + (cnt == 4));
        Console.WriteLine("qe_order=" + qok);

        // empty containers: foreach yields nothing
        Stack se = new Stack();
        Queue qe = new Queue();
        int en = 0; foreach (object o in se) en++;
        int eq = 0; foreach (object o in qe) eq++;
        Console.WriteLine("e_stack=" + (en == 0));
        Console.WriteLine("e_queue=" + (eq == 0));

        // manual IEnumerator over a STABLE stack (0,1,2 -> top first 2,1,0)
        Stack sn = new Stack();
        for (int i = 0; i < 3; i++) sn.Push(i);
        IEnumerator esn = sn.GetEnumerator();
        Console.WriteLine("sn_mn1=" + esn.MoveNext());
        Console.WriteLine("sn_cur1=" + ((int)esn.Current == 2));
        esn.MoveNext();
        Console.WriteLine("sn_cur2=" + ((int)esn.Current == 1));
        esn.MoveNext();
        Console.WriteLine("sn_cur3=" + ((int)esn.Current == 0));
        Console.WriteLine("sn_end=" + (esn.MoveNext() == false));

        // ---- D-STK-1 VERSION CHECK probes (net48 oracle) ----
        // Every structural mutation after GetEnumerator throws
        // InvalidOperationException on the next MoveNext / Reset; read-only
        // operations never do. Each probe prints whether the expected throw
        // (or the expected clean walk) happened.
        bool threw;

        // Stack: Push / Pop / Clear after GetEnumerator -> IOE on MoveNext.
        Stack svp = new Stack(); svp.Push(1); svp.Push(2); svp.Push(3);
        IEnumerator evp = svp.GetEnumerator();
        evp.MoveNext();
        svp.Push(99);
        threw = false;
        try { evp.MoveNext(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("sv_push_ioe=" + threw);

        Stack svpo = new Stack(); svpo.Push(1); svpo.Push(2);
        IEnumerator evpo = svpo.GetEnumerator();
        svpo.Pop();
        threw = false;
        try { evpo.MoveNext(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("sv_pop_ioe=" + threw);

        Stack svc = new Stack(); svc.Push(1); svc.Push(2);
        IEnumerator evc = svc.GetEnumerator();
        svc.Clear();
        threw = false;
        try { evc.MoveNext(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("sv_clear_ioe=" + threw);

        // Stack: Reset is version-checked too (net48 Reset throws).
        Stack svr = new Stack(); svr.Push(1); svr.Push(2);
        IEnumerator evr = svr.GetEnumerator();
        svr.Push(3);
        threw = false;
        try { evr.Reset(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("sv_reset_ioe=" + threw);

        // Stack: a STABLE stack never throws (no false positive).
        Stack svs = new Stack(); svs.Push(1); svs.Push(2);
        IEnumerator evs = svs.GetEnumerator();
        evs.MoveNext();
        evs.Reset();
        bool svs_ok = evs.MoveNext() && ((int)evs.Current == 2);
        Console.WriteLine("sv_stable=" + svs_ok);

        // Queue: Enqueue / Dequeue / Clear after GetEnumerator -> IOE on
        // MoveNext; Reset is version-checked too; a stable queue never throws.
        Queue qvp = new Queue(); qvp.Enqueue(1); qvp.Enqueue(2); qvp.Enqueue(3);
        IEnumerator eqp = qvp.GetEnumerator();
        eqp.MoveNext();
        qvp.Enqueue(99);
        threw = false;
        try { eqp.MoveNext(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("qv_enq_ioe=" + threw);

        Queue qvd = new Queue(); qvd.Enqueue(1); qvd.Enqueue(2);
        IEnumerator eqd = qvd.GetEnumerator();
        qvd.Dequeue();
        threw = false;
        try { eqd.MoveNext(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("qv_deq_ioe=" + threw);

        Queue qvc = new Queue(); qvc.Enqueue(1); qvc.Enqueue(2);
        IEnumerator eqc = qvc.GetEnumerator();
        qvc.Clear();
        threw = false;
        try { eqc.MoveNext(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("qv_clear_ioe=" + threw);

        Queue qvr = new Queue(); qvr.Enqueue(1); qvr.Enqueue(2);
        IEnumerator eqr = qvr.GetEnumerator();
        qvr.Enqueue(3);
        threw = false;
        try { eqr.Reset(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("qv_reset_ioe=" + threw);

        Queue qvs = new Queue(); qvs.Enqueue(1); qvs.Enqueue(2);
        IEnumerator eqs = qvs.GetEnumerator();
        eqs.MoveNext();
        eqs.Reset();
        bool qvs_ok = eqs.MoveNext() && ((int)eqs.Current == 1);
        Console.WriteLine("qv_stable=" + qvs_ok);

        // Reset + re-enumerate (manual) on a fresh stack [0,1,2,99], top=99
        Stack sr2 = new Stack();
        sr2.Push(0); sr2.Push(1); sr2.Push(2); sr2.Push(99);
        IEnumerator rs = sr2.GetEnumerator();
        rs.MoveNext(); rs.MoveNext();
        Console.WriteLine("rs_before=" + ((int)rs.Current == 2));
        rs.Reset();
        rs.MoveNext();
        Console.WriteLine("rs_after=" + ((int)rs.Current == 99));

        // Queue enumerator over a WRAPPED ring (head != 0)
        Queue qr = new Queue();
        for (int i = 0; i < 5; i++) qr.Enqueue(i);     // 0..4
        qr.Dequeue(); qr.Dequeue();                    // drain 0,1 -> head=2
        qr.Enqueue(5); qr.Enqueue(6);                  // wrap: 2,3,4,5,6
        cnt = 0; bool rok = true;
        foreach (object o in qr) { rok = rok && ((int)o == 2 + cnt); cnt++; }
        Console.WriteLine("qr_count=" + (cnt == 5));
        Console.WriteLine("qr_order=" + rok);
        // the same wrapped ring through ToArray (front-first)
        object[] qra = qr.ToArray();
        Console.WriteLine("qr_ta0=" + ((int)qra[0] == 2));
        Console.WriteLine("qr_ta4=" + ((int)qra[4] == 6));

        // ================= S2: capacity / growth deep =================
        // Stack doubling: push 100 -> order intact on the way out (LIFO)
        Stack gs = new Stack();
        for (int i = 0; i < 100; i++) gs.Push(i);
        Console.WriteLine("gs_count=" + (gs.Count == 100));
        bool gok = true;
        for (int i = 99; i >= 0; i--) gok = gok && ((int)gs.Pop() == i);
        Console.WriteLine("gs_order=" + gok);
        Console.WriteLine("gs_empty=" + (gs.Count == 0));

        // Queue ring wrap cycles WITHOUT growth (count stays < capacity 4)
        Queue ring = new Queue();
        for (int k = 0; k < 20; k++)
        {
            ring.Enqueue(k); ring.Enqueue(k + 100); ring.Enqueue(k + 200);
            ring.Dequeue(); ring.Dequeue(); ring.Dequeue();
        }
        Console.WriteLine("ring_empty=" + (ring.Count == 0));
        ring.Enqueue(1); ring.Enqueue(2); ring.Enqueue(3);
        bool ringok = (ring.Count == 3)
            && ((int)ring.Dequeue() == 1)
            && ((int)ring.Dequeue() == 2)
            && ((int)ring.Dequeue() == 3);
        Console.WriteLine("ring_order=" + ringok);
        Console.WriteLine("ring_drained=" + (ring.Count == 0));

        // Queue growth re-layout: fill 4, grow to 8, drain 5, keep order
        Queue gq = new Queue();
        for (int i = 0; i < 4; i++) gq.Enqueue(i);
        gq.Enqueue(4); gq.Enqueue(5); gq.Enqueue(6); gq.Enqueue(7);   // grows to 8
        bool gqok = true;
        for (int i = 0; i < 8; i++) gqok = gqok && ((int)gq.Dequeue() == i);
        Console.WriteLine("gq_order=" + gqok);

        // growth + GC: enumerator still yields the surviving elements
        Stack gg = new Stack();
        for (int i = 0; i < 50; i++) gg.Push(i);
        GC.Collect();
        int gsum = 0;
        foreach (object o in gg) gsum += (int)o;
        Console.WriteLine("gg_sum=" + (gsum == 49 * 50 / 2));
        Console.WriteLine("gg_count=" + (gg.Count == 50));

        // ================= S3: mixed deep =================
        // mixed-type round trip through the ENUMERATOR (LIFO: Q,7L,false,two,1)
        Stack sm = new Stack();
        sm.Push(1); sm.Push("two"); sm.Push(false); sm.Push(7L); sm.Push('Q');
        int mn = 0; bool mok = true; object[] expect = new object[] { 'Q', 7L, false, "two", 1 };
        foreach (object o in sm) { mok = mok && o.Equals(expect[mn]); mn++; }
        Console.WriteLine("sm_count=" + (mn == 5));
        Console.WriteLine("sm_order=" + mok);
        // mixed-type round trip through the enumerator (FIFO: 1,two,false,7L,Q)
        Queue qm = new Queue();
        qm.Enqueue(1); qm.Enqueue("two"); qm.Enqueue(false); qm.Enqueue(7L); qm.Enqueue('Q');
        object[] qexpect = new object[] { 1, "two", false, 7L, 'Q' };
        mn = 0; mok = true;
        foreach (object o in qm) { mok = mok && o.Equals(qexpect[mn]); mn++; }
        Console.WriteLine("qm_count=" + (mn == 5));
        Console.WriteLine("qm_order=" + mok);

        // Stack vs Queue cross-check of the SAME sequence (ToArray orders)
        Stack sx = new Stack();
        Queue qx = new Queue();
        for (int i = 0; i < 10; i++) { sx.Push(i); qx.Enqueue(i); }
        object[] sa = sx.ToArray();
        object[] qa = qx.ToArray();
        Console.WriteLine("x_stack_top=" + ((int)sa[0] == 9));
        Console.WriteLine("x_queue_front=" + ((int)qa[0] == 0));
        bool xok = true;
        for (int i = 0; i < 10; i++) { xok = xok && ((int)sa[i] == 9 - i); xok = xok && ((int)qa[i] == i); }
        Console.WriteLine("x_orders=" + xok);

        // interleaved push/pop/foreach state machine
        Stack st = new Stack();
        st.Push(1); st.Push(2); st.Push(3);
        int c1 = 0; foreach (object o in st) c1++;
        st.Pop();                                    // 3 out -> top 2
        st.Push(4); st.Push(5);
        int c2 = 0; foreach (object o in st) c2++;
        Console.WriteLine("il_c1=" + (c1 == 3));
        Console.WriteLine("il_c2=" + (c2 == 4));
        bool ilok = ((int)st.Pop() == 5) && ((int)st.Pop() == 4)
                 && ((int)st.Pop() == 2) && ((int)st.Pop() == 1);
        Console.WriteLine("il_drain=" + ilok);
        Console.WriteLine("il_empty=" + (st.Count == 0));
    }
}}
