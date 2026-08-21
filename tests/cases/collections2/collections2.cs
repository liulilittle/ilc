//------------------------------------------------------------------------------
// collections2.cs -- end-to-end reference case for System.Collections.Stack /
// Queue DEEP coverage (net48 oracle). Extends stackq (batch B, basic surface)
// with: ToArray, growth beyond the initial items capacity, interleaved
// Push/Pop/Peek and Enqueue/Dequeue/Peek, mixed-value element families
// (int / string / bool / double / char), empty-stack/queue Pop/Dequeue
// (InvalidOperationException), Clear-then-reuse.
//
// WIRED surface (MethodMap.Stack.cs / MethodMap.Queue.cs -> stackqueue.h):
//   Stack: .ctor (create icall) / Push / Pop / Peek / get_Count / Clear
//   Queue: .ctor (create icall) / Enqueue / Dequeue / Peek / get_Count / Clear
// Every element is a boxed Il2CppObject* slot: box wiring boxes the value at
// the call site, Pop/Dequeue/Peek results are unboxed by the caller's cast.
//
// NOT WIRED (documented gap, do NOT uncomment until wired):
//   * Stack.ToArray / Queue.ToArray -- NO MethodMap entry and NO runtime
//     to_array icall in stackqueue.h (the surface stops at the create/push/
//     pop/peek/get_count/clear + queue create/enqueue/dequeue/peek/get_count/
//     clear family). net48 semantics documented below (top-of-stack first for
//     Stack.ToArray, head-first for Queue.ToArray). Stack/Queue capacity
//     ctors are also unmapped (create icall is parameterless).
//
// Every line prints "label=True/False" so the reference output is fully
// deterministic and hand-verifiable. net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class Collections2 {
    public static void Main() {
        // ---- Stack: LIFO order + interleaved Push/Pop/Peek ----
        Stack s = new Stack();
        Console.WriteLine("s_init=" + (s.Count == 0));
        s.Push(1);
        s.Push(2);
        s.Push(3);
        Console.WriteLine("s_cnt=" + (s.Count == 3));
        Console.WriteLine("s_peek=" + ((int)s.Peek() == 3));
        Console.WriteLine("s_peek_keep=" + (s.Count == 3));   // Peek does not remove
        Console.WriteLine("s_pop=" + ((int)s.Pop() == 3));
        s.Push(4);
        Console.WriteLine("s_push_again=" + (s.Count == 3));
        Console.WriteLine("s_peek2=" + ((int)s.Peek() == 4));
        Console.WriteLine("s_pop2=" + ((int)s.Pop() == 4));
        Console.WriteLine("s_pop3=" + ((int)s.Pop() == 2));
        Console.WriteLine("s_pop4=" + ((int)s.Pop() == 1));
        Console.WriteLine("s_empty=" + (s.Count == 0));

        // ---- Stack: growth beyond initial capacity (kMinItems = 4) ----
        Stack g = new Stack();
        bool gok = true;
        for (int i = 0; i < 40; i++) { g.Push(i); }
        Console.WriteLine("s_grow_cnt=" + (g.Count == 40));
        for (int i = 39; i >= 0; i--) { gok = gok && ((int)g.Pop() == i); }
        Console.WriteLine("s_grow_lifo=" + gok);
        Console.WriteLine("s_grow_empty=" + (g.Count == 0));

        // ---- Stack: mixed element types (boxed -> unboxed on exit) ----
        Stack m = new Stack();
        m.Push(42);        // int
        m.Push("str");     // string (reference, no box)
        m.Push(true);      // bool
        m.Push(2.5);       // double
        m.Push('Z');       // char
        Console.WriteLine("s_mix_cnt=" + (m.Count == 5));
        Console.WriteLine("s_mix_c=" + ((char)m.Pop() == 'Z'));
        Console.WriteLine("s_mix_d=" + ((double)m.Pop() == 2.5));
        Console.WriteLine("s_mix_b=" + ((bool)m.Pop() == true));
        Console.WriteLine("s_mix_s=" + ((string)m.Pop() == "str"));
        Console.WriteLine("s_mix_i=" + ((int)m.Pop() == 42));
        Console.WriteLine("s_mix_empty=" + (m.Count == 0));

        // ---- Stack: empty Pop / Peek -> InvalidOperationException ----
        Stack e = new Stack();
        bool ePop = false, ePeek = false;
        try { e.Pop(); } catch (InvalidOperationException) { ePop = true; }
        try { e.Peek(); } catch (InvalidOperationException) { ePeek = true; }
        Console.WriteLine("s_empty_pop_ex=" + ePop);
        Console.WriteLine("s_empty_peek_ex=" + ePeek);

        // ---- Stack: Clear then reuse ----
        Stack c = new Stack();
        c.Push("a");
        c.Push("b");
        c.Clear();
        Console.WriteLine("s_clear=" + (c.Count == 0));
        c.Push("x");
        Console.WriteLine("s_reuse=" + ((string)c.Pop() == "x"));

        // ---- Queue: FIFO order + interleaved Enqueue/Dequeue/Peek ----
        Queue q = new Queue();
        Console.WriteLine("q_init=" + (q.Count == 0));
        q.Enqueue("a");
        q.Enqueue("b");
        q.Enqueue("c");
        Console.WriteLine("q_cnt=" + (q.Count == 3));
        Console.WriteLine("q_peek=" + ((string)q.Peek() == "a"));
        Console.WriteLine("q_peek_keep=" + (q.Count == 3));   // Peek does not remove
        Console.WriteLine("q_dq=" + ((string)q.Dequeue() == "a"));
        q.Enqueue("d");
        Console.WriteLine("q_enq_again=" + (q.Count == 3));
        Console.WriteLine("q_peek2=" + ((string)q.Peek() == "b"));
        Console.WriteLine("q_dq2=" + ((string)q.Dequeue() == "b"));
        Console.WriteLine("q_dq3=" + ((string)q.Dequeue() == "c"));
        Console.WriteLine("q_dq4=" + ((string)q.Dequeue() == "d"));
        Console.WriteLine("q_empty=" + (q.Count == 0));

        // ---- Queue: growth beyond initial capacity ----
        Queue qg = new Queue();
        bool qok = true;
        for (int i = 0; i < 40; i++) { qg.Enqueue(i); }
        Console.WriteLine("q_grow_cnt=" + (qg.Count == 40));
        for (int i = 0; i < 40; i++) { qok = qok && ((int)qg.Dequeue() == i); }
        Console.WriteLine("q_grow_fifo=" + qok);
        Console.WriteLine("q_grow_empty=" + (qg.Count == 0));

        // ---- Queue: mixed element types ----
        Queue qm = new Queue();
        qm.Enqueue(7);       // int
        qm.Enqueue("mid");   // string
        qm.Enqueue(false);   // bool
        qm.Enqueue(1.25);    // double
        qm.Enqueue('Q');     // char
        Console.WriteLine("q_mix_cnt=" + (qm.Count == 5));
        Console.WriteLine("q_mix_i=" + ((int)qm.Dequeue() == 7));
        Console.WriteLine("q_mix_s=" + ((string)qm.Dequeue() == "mid"));
        Console.WriteLine("q_mix_b=" + ((bool)qm.Dequeue() == false));
        Console.WriteLine("q_mix_d=" + ((double)qm.Dequeue() == 1.25));
        Console.WriteLine("q_mix_c=" + ((char)qm.Dequeue() == 'Q'));
        Console.WriteLine("q_mix_empty=" + (qm.Count == 0));

        // ---- Queue: empty Dequeue / Peek -> InvalidOperationException ----
        Queue qe = new Queue();
        bool qDq = false, qPeek = false;
        try { qe.Dequeue(); } catch (InvalidOperationException) { qDq = true; }
        try { qe.Peek(); } catch (InvalidOperationException) { qPeek = true; }
        Console.WriteLine("q_empty_dq_ex=" + qDq);
        Console.WriteLine("q_empty_peek_ex=" + qPeek);

        // ---- Queue: Clear then reuse ----
        Queue qc = new Queue();
        qc.Enqueue("a");
        qc.Enqueue("b");
        qc.Clear();
        Console.WriteLine("q_clear=" + (qc.Count == 0));
        qc.Enqueue("x");
        Console.WriteLine("q_reuse=" + ((string)qc.Dequeue() == "x"));
    }
}}
