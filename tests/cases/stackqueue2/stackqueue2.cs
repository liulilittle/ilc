//------------------------------------------------------------------------------
// stackqueue2.cs -- System.Collections.Stack / Queue DEEP e2e case #3 (net48
// oracle, byte-for-byte). Created in e2e batch C85, AFTER the stack/queue
// domain closure (M7 stkq: Push/Pop/Peek/Count/Clear/ToArray + the C42
// sqenum-b23 snapshot enumerator + D-STK-1 version checks; stackqdeep probes
// mixed round trips / GC pressure / ToArray order / empty exceptions; stackops2
// probes enumerator order + version-check mutations + basic doubling growth).
//
// WHERE THIS GOES DEEPER THAN THE PREDECESSORS -- CAPACITY/ENUMERATION
// COMBINATIONS (the growth boundary + enumeration surface):
//   S1 STACK CAPACITY-BOUNDARY GROWTH: the initial items array holds 4 slots
//      (stackqueue.h); pushing the 5th / 9th / 17th element DOUBLES the array
//      (4->8->16->32). Each boundary (exactly 4, then 5, then 9, then 17) is
//      verified: Count is exact AND the full LIFO drain order is intact right
//      AFTER the growth-triggering push -- a growth must never reorder.
//   S2 QUEUE CAPACITY-BOUNDARY GROWTH: enqueue exactly 4, then the 5th (ring
//      grows 4->8, head resets to 0), then the 9th (8->16). FIFO drain order
//      intact at every boundary.
//   S3 QUEUE RING WRAP + GROWTH: dequeue past the front (head walks the ring
//      WITHOUT growth while count < capacity) then enqueue across a growth --
//      the ring re-layout preserves front-to-back order.
//   S4 ENUMERATION COMBINATIONS: foreach and ToArray over the SAME live
//      container agree (Stack top-first, Queue front-first); MULTIPLE
//      CONCURRENT enumerators (two foreach / two manual IEnumerator over one
//      container) walk independently (each GetEnumerator snapshots its own
//      cursor); a manual enumerator advanced interleaved with ToArray still
//      yields the correct sequence; a fresh enumerator right after a growth
//      boundary yields the full correct order.
//   S5 READ-ONLY OPS NEVER BUST THE VERSION: Peek / ToArray / get_Count
//      between GetEnumerator and MoveNext do NOT throw (the D-STK-1 version
//      counter is bumped ONLY by structural mutation).
//   S6 LARGE CAPACITY PRESSURE + GC: 20000 boxed elements (beyond stackqdeep's
//      5000), GC.Collect() mid-way, full order intact on both drains.
//
// All probes print labeled bools/ints -- deterministic across encodings. The
// net48 run is the byte reference; the transpiled build must match.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class StackQueue2 {
    public static void Main() {
        // ================= S1: Stack capacity-boundary growth =================
        // initial capacity 4: exactly 4 pushes fit without growth
        Stack s4 = new Stack();
        for (int i = 0; i < 4; i++) s4.Push(i);
        Console.WriteLine("s4_count=" + (s4.Count == 4));
        bool s4ok = true;
        for (int i = 3; i >= 0; i--) s4ok = s4ok && ((int)s4.Pop() == i);
        Console.WriteLine("s4_lifo=" + s4ok);
        // 5th push GROWS 4->8; order intact right after the boundary
        Stack s8 = new Stack();
        for (int i = 0; i < 4; i++) s8.Push(i);
        s8.Push(4);                                        // growth trigger
        Console.WriteLine("s8_count=" + (s8.Count == 5));
        bool s8ok = true;
        for (int i = 4; i >= 0; i--) s8ok = s8ok && ((int)s8.Pop() == i);
        Console.WriteLine("s8_lifo=" + s8ok);
        // fill to 8, then the 9th grows 8->16
        Stack s16 = new Stack();
        for (int i = 0; i < 8; i++) s16.Push(i);
        s16.Push(8);                                       // growth trigger 8->16
        Console.WriteLine("s16_count=" + (s16.Count == 9));
        bool s16ok = true;
        for (int i = 8; i >= 0; i--) s16ok = s16ok && ((int)s16.Pop() == i);
        Console.WriteLine("s16_lifo=" + s16ok);
        // fill to 16, then the 17th grows 16->32
        Stack s32 = new Stack();
        for (int i = 0; i < 16; i++) s32.Push(i);
        s32.Push(16);                                      // growth trigger 16->32
        Console.WriteLine("s32_count=" + (s32.Count == 17));
        bool s32ok = true;
        for (int i = 16; i >= 0; i--) s32ok = s32ok && ((int)s32.Pop() == i);
        Console.WriteLine("s32_lifo=" + s32ok);
        // 1000 pushes: many doublings, order intact end-to-end
        Stack s1k = new Stack();
        for (int i = 0; i < 1000; i++) s1k.Push(i * 3);
        Console.WriteLine("s1k_count=" + (s1k.Count == 1000));
        bool s1kok = true;
        for (int i = 999; i >= 0; i--) s1kok = s1kok && ((int)s1k.Pop() == i * 3);
        Console.WriteLine("s1k_lifo=" + s1kok);

        // ================= S2: Queue capacity-boundary growth =================
        Queue q4 = new Queue();
        for (int i = 0; i < 4; i++) q4.Enqueue(i);
        Console.WriteLine("q4_count=" + (q4.Count == 4));
        bool q4ok = true;
        for (int i = 0; i < 4; i++) q4ok = q4ok && ((int)q4.Dequeue() == i);
        Console.WriteLine("q4_fifo=" + q4ok);
        // 5th enqueue GROWS 4->8; FIFO intact
        Queue q8 = new Queue();
        for (int i = 0; i < 4; i++) q8.Enqueue(i);
        q8.Enqueue(4);                                     // growth trigger
        Console.WriteLine("q8_count=" + (q8.Count == 5));
        bool q8ok = true;
        for (int i = 0; i < 5; i++) q8ok = q8ok && ((int)q8.Dequeue() == i);
        Console.WriteLine("q8_fifo=" + q8ok);
        // fill to 8, then the 9th grows 8->16
        Queue q16 = new Queue();
        for (int i = 0; i < 8; i++) q16.Enqueue(i);
        q16.Enqueue(8);                                    // growth trigger 8->16
        Console.WriteLine("q16_count=" + (q16.Count == 9));
        bool q16ok = true;
        for (int i = 0; i < 9; i++) q16ok = q16ok && ((int)q16.Dequeue() == i);
        Console.WriteLine("q16_fifo=" + q16ok);
        // 1000 enqueues across many doublings
        Queue q1k = new Queue();
        for (int i = 0; i < 1000; i++) q1k.Enqueue(i * 5);
        Console.WriteLine("q1k_count=" + (q1k.Count == 1000));
        bool q1kok = true;
        for (int i = 0; i < 1000; i++) q1kok = q1kok && ((int)q1k.Dequeue() == i * 5);
        Console.WriteLine("q1k_fifo=" + q1kok);

        // ========== S3: Queue ring wrap WITHOUT growth, then growth ==========
        // capacity 4: enqueue 4, dequeue 2 -> head=2 (ring wrapped, count 2)
        Queue rw = new Queue();
        for (int i = 0; i < 4; i++) rw.Enqueue(10 + i);    // 10,11,12,13
        rw.Dequeue(); rw.Dequeue();                        // drain 10,11 -> head=2
        Console.WriteLine("rw_count=" + (rw.Count == 2));
        // dequeue+enqueue cycles keep the ring alive without growing
        rw.Enqueue(20); rw.Enqueue(21); rw.Enqueue(22); rw.Enqueue(23); // grows to 8
        Console.WriteLine("rw_count2=" + (rw.Count == 6));
        bool rwok = true;
        int[] rwExp = new int[] { 12, 13, 20, 21, 22, 23 };
        for (int i = 0; i < 6; i++) rwok = rwok && ((int)rw.Dequeue() == rwExp[i]);
        Console.WriteLine("rw_fifo=" + rwok);
        // a fresh wrapped ring: drain some, enumerate via foreach (front-first)
        Queue rw2 = new Queue();
        for (int i = 0; i < 4; i++) rw2.Enqueue(30 + i);   // 30,31,32,33
        rw2.Dequeue();                                     // head=1
        rw2.Enqueue(40); rw2.Enqueue(41); rw2.Enqueue(42); // wrap + grow to 8
        int rw2c = 0; bool rw2ok = true;
        int[] rw2Exp = new int[] { 31, 32, 33, 40, 41, 42 };
        foreach (object o in rw2) { rw2ok = rw2ok && ((int)o == rw2Exp[rw2c]); rw2c++; }
        Console.WriteLine("rw2_enum_count=" + (rw2c == 6));
        Console.WriteLine("rw2_enum_order=" + rw2ok);

        // ================= S4: enumeration combinations =================
        // foreach and ToArray over the SAME live stack agree (top-first)
        Stack se = new Stack();
        for (int i = 0; i < 5; i++) se.Push(i);            // 0..4 pushed
        object[] sea = se.ToArray();
        int sec = 0; bool seok = true;
        foreach (object o in se) { seok = seok && (int)o == (int)sea[sec]; sec++; }
        Console.WriteLine("se_agree=" + seok);
        Console.WriteLine("se_len=" + (sec == 5));
        // foreach and ToArray agree on a queue (front-first)
        Queue qe = new Queue();
        for (int i = 0; i < 5; i++) qe.Enqueue(100 + i);
        object[] qea = qe.ToArray();
        int qec = 0; bool qeok = true;
        foreach (object o in qe) { qeok = qeok && (int)o == (int)qea[qec]; qec++; }
        Console.WriteLine("qe_agree=" + qeok);
        Console.WriteLine("qe_len=" + (qec == 5));
        // MULTIPLE CONCURRENT enumerators over one stack walk independently
        Stack sc = new Stack();
        for (int i = 0; i < 3; i++) sc.Push(i);            // 0,1,2 -> top first
        IEnumerator e1 = sc.GetEnumerator();
        IEnumerator e2 = sc.GetEnumerator();
        e1.MoveNext();
        e2.MoveNext();
        Console.WriteLine("conc_cur1=" + ((int)e1.Current == 2));
        Console.WriteLine("conc_cur2=" + ((int)e2.Current == 2));
        e1.MoveNext();                                     // advance only e1
        Console.WriteLine("conc_adv1=" + ((int)e1.Current == 1));
        Console.WriteLine("conc_stable2=" + ((int)e2.Current == 2));
        e2.MoveNext();
        Console.WriteLine("conc_adv2=" + ((int)e2.Current == 1));
        // two concurrent FOREACH loops over one queue give identical sequences
        Queue qc2 = new Queue();
        for (int i = 0; i < 4; i++) qc2.Enqueue(50 + i);
        int f1 = 0; bool f1ok = true;
        foreach (object o in qc2) { f1ok = f1ok && (int)o == 50 + f1; f1++; }
        int f2 = 0; bool f2ok = true;
        foreach (object o in qc2) { f2ok = f2ok && (int)o == 50 + f2; f2++; }
        Console.WriteLine("twoforeach1=" + f1ok);
        Console.WriteLine("twoforeach2=" + f2ok);
        // manual enumerator interleaved with ToArray still walks correctly
        Stack sm = new Stack();
        for (int i = 0; i < 4; i++) sm.Push(i);            // 0..3 -> top first
        IEnumerator me = sm.GetEnumerator();
        me.MoveNext();
        object[] sma = sm.ToArray();                       // read-only, no version bump
        Console.WriteLine("inter_cur=" + ((int)me.Current == 3));
        me.MoveNext();
        Console.WriteLine("inter_next=" + ((int)me.Current == 2));
        Console.WriteLine("inter_ta=" + ((int)sma[0] == 3 && (int)sma[3] == 0));
        // fresh enumerator right AFTER a growth boundary yields full order
        Stack sg = new Stack();
        for (int i = 0; i < 8; i++) sg.Push(i);
        sg.Push(8);                                        // grow 8->16
        int sgc = 0; bool sgok = true;
        foreach (object o in sg) { sgok = sgok && (int)o == 8 - sgc; sgc++; }
        Console.WriteLine("sg_after_grow=" + sgok);
        Console.WriteLine("sg_count=" + (sgc == 9));

        // ========== S5: read-only ops never bust the enumerator version ==========
        Stack sv = new Stack();
        sv.Push(1); sv.Push(2); sv.Push(3);
        IEnumerator ev = sv.GetEnumerator();
        ev.MoveNext();
        sv.Peek();                                         // read-only
        sv.ToArray();                                      // read-only
        int v = sv.Count;                                  // read-only
        bool svok = ev.MoveNext() && (int)ev.Current == 2; // must NOT throw
        Console.WriteLine("sv_ro_ok=" + svok);
        Console.WriteLine("sv_count=" + (v == 3));
        Queue qv = new Queue();
        qv.Enqueue(1); qv.Enqueue(2); qv.Enqueue(3);
        IEnumerator evq = qv.GetEnumerator();
        evq.MoveNext();
        qv.Peek(); qv.ToArray();
        bool qvok = evq.MoveNext() && (int)evq.Current == 2;
        Console.WriteLine("qv_ro_ok=" + qvok);

        // ========== S6: large capacity pressure + GC ==========
        Stack gs = new Stack();
        for (int i = 0; i < 20000; i++) gs.Push(i);
        GC.Collect();
        Console.WriteLine("gs_count=" + (gs.Count == 20000));
        bool gsok = true;
        for (int i = 19999; i >= 0; i--) gsok = gsok && (int)gs.Pop() == i;
        Console.WriteLine("gs_lifo=" + gsok);
        Console.WriteLine("gs_empty=" + (gs.Count == 0));
        Queue gq = new Queue();
        for (int i = 0; i < 20000; i++) gq.Enqueue(i * 7);
        GC.Collect();
        bool gqok = true;
        for (int i = 0; i < 20000; i++) gqok = gqok && (int)gq.Dequeue() == i * 7;
        Console.WriteLine("gq_fifo=" + gqok);
        Console.WriteLine("gq_empty=" + (gq.Count == 0));
    }
}}
