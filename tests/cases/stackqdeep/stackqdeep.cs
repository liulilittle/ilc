//------------------------------------------------------------------------------
// stackqdeep.cs -- Stack / Queue DEEP e2e case #2 (batch C42). The
// stackq/stackqueue runtime chain is fully wired (stackq passed in M7):
// Stack Push/Pop/Peek/Count/Clear/ToArray + Queue Enqueue/Dequeue/Peek/
// Count/Clear/ToArray (stackqueue.h, il2cpp:: namespace). This case goes
// DEEPER than the stackq baseline:
//
//   1) MIXED-TYPE round trips: boxed int/string/bool/long/double/char
//      pushed/enqueued and unboxed back out on Pop/Dequeue (the box wiring
//      + caller-side unbox cast prove the slot stays a boxed Il2CppObject*).
//   2) GC PRESSURE: thousands of boxed elements, GC.Collect() between
//      pushes and pops -- the refFieldBitmap contract (stackqueue.h: the
//      items array is a REFERENCE array, so a rooted stack/queue keeps its
//      whole contents alive across collections). Every element must survive.
//   3) ENUMERATION via ToArray(): Stack.ToArray is TOP-OF-STACK first,
//      Queue.ToArray is FRONT-first (ring-aware). The result is an
//      exact-length object[] that is iterated by index. (GetEnumerator /
//      foreach is NOT wired for Stack/Queue -- MethodMap.Collections.cs
//      handles only SortedList snapshots -- so enumeration goes through the
//      wired ToArray surface.)
//   4) EMPTY-container Pop/Peek/Dequeue -> InvalidOperationException
//      (runtime "Stack empty."/"Queue empty.", net48-identical type name).
//   5) Clear/reuse + null-element round trip.
//
// All probes print bools or ints -- deterministic across console encodings.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class StackQDeep {
    private static void PushSeq(Stack s, int n) { for (int i = 0; i < n; i++) s.Push(i); }

    public static void Main() {
        //===== 1. mixed-type Stack round trip (LIFO) =====
        Stack sm = new Stack();
        sm.Push(42);
        sm.Push("deep");
        sm.Push(true);
        sm.Push(9L);
        sm.Push(3.5);
        sm.Push('Z');
        Console.WriteLine(sm.Count == 6);             // True
        Console.WriteLine((char)sm.Pop() == 'Z');     // True
        Console.WriteLine((double)sm.Pop() == 3.5);   // True
        Console.WriteLine((long)sm.Pop() == 9L);      // True
        Console.WriteLine((bool)sm.Pop() == true);    // True
        Console.WriteLine((string)sm.Pop() == "deep");// True
        Console.WriteLine((int)sm.Pop() == 42);       // True
        Console.WriteLine(sm.Count == 0);             // True

        //===== 2. mixed-type Queue round trip (FIFO) =====
        Queue qm = new Queue();
        qm.Enqueue(1);
        qm.Enqueue("two");
        qm.Enqueue(false);
        qm.Enqueue(8L);
        qm.Enqueue(2.25);
        qm.Enqueue('Q');
        Console.WriteLine(qm.Count == 6);             // True
        Console.WriteLine((int)qm.Dequeue() == 1);    // True
        Console.WriteLine((string)qm.Dequeue() == "two"); // True
        Console.WriteLine((bool)qm.Dequeue() == false);   // True
        Console.WriteLine((long)qm.Dequeue() == 8L);      // True
        Console.WriteLine((double)qm.Dequeue() == 2.25);  // True
        Console.WriteLine((char)qm.Dequeue() == 'Q');     // True
        Console.WriteLine(qm.Count == 0);             // True

        //===== 3. GC pressure: 5000 boxed ints survive a collection =====
        Stack gs = new Stack();
        for (int i = 0; i < 5000; i++) gs.Push(i * 2);
        GC.Collect();
        Console.WriteLine(gs.Count == 5000);          // True
        int sum = 0;
        for (int i = 0; i < 5000; i++) sum += (int)gs.Pop();
        Console.WriteLine(gs.Count == 0);             // True
        Console.WriteLine(sum == 5000 * 4999);        // True (2*sum(0..4999))
        // Queue pressure: strings stay FIFO-ordered across a collection.
        Queue gq = new Queue();
        for (int i = 0; i < 3000; i++) gq.Enqueue("s" + i);
        GC.Collect();
        bool ok = true;
        for (int i = 0; i < 3000; i++) ok = ok && ((string)gq.Dequeue() == "s" + i);
        Console.WriteLine(ok);                        // True
        Console.WriteLine(gq.Count == 0);             // True

        //===== 4. interleaved growth + partial drain, then GC =====
        Stack gr = new Stack();
        for (int i = 0; i < 1000; i++) gr.Push(i);
        for (int i = 0; i < 400; i++) gr.Pop();       // drain 400
        GC.Collect();
        Console.WriteLine(gr.Count == 600);           // True
        ok = true;
        for (int i = 599; i >= 0; i--) ok = ok && ((int)gr.Pop() == i);
        Console.WriteLine(ok);                        // True
        Console.WriteLine(gr.Count == 0);             // True

        //===== 5. enumeration via ToArray: Stack top-first =====
        Stack se = new Stack();
        PushSeq(se, 4);                               // 0,1,2,3 pushed
        object[] sea = se.ToArray();
        Console.WriteLine(sea.Length == 4);           // True
        Console.WriteLine((int)sea[0] == 3);          // True (top first)
        Console.WriteLine((int)sea[1] == 2);          // True
        Console.WriteLine((int)sea[3] == 0);          // True (bottom last)
        Console.WriteLine(se.Count == 4);             // True (ToArray does not drain)
        // ToArray on an empty stack -> length-0 array.
        Stack seEmpty = new Stack();
        object[] sea2 = seEmpty.ToArray();
        Console.WriteLine(sea2.Length == 0);          // True

        //===== 6. enumeration via ToArray: Queue front-first (ring-aware) =====
        Queue qe = new Queue();
        for (int i = 0; i < 5; i++) qe.Enqueue(10 + i);
        object[] qea = qe.ToArray();
        Console.WriteLine(qea.Length == 5);           // True
        Console.WriteLine((int)qea[0] == 10);         // True (front first)
        Console.WriteLine((int)qea[4] == 14);         // True (back last)
        Console.WriteLine(qe.Count == 5);             // True
        // Wrap the ring (dequeue some, enqueue more), then ToArray must be
        // FRONT-first across the wrap.
        qe.Dequeue();                                 // front now 11
        qe.Enqueue(20);                               // ring head moves
        object[] qea2 = qe.ToArray();
        Console.WriteLine(qea2.Length == 5);          // True
        Console.WriteLine((int)qea2[0] == 11);        // True (front first)
        Console.WriteLine((int)qea2[4] == 20);        // True (newest last)
        // Empty queue ToArray -> length-0.
        Queue qeEmpty = new Queue();
        Console.WriteLine(qeEmpty.ToArray().Length == 0); // True

        //===== 7. empty-container exceptions (type name only) =====
        Stack es = new Stack();
        bool threw = false;
        try { es.Pop(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine(threw);                     // True
        threw = false;
        try { es.Peek(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine(threw);                     // True
        Queue eq = new Queue();
        threw = false;
        try { eq.Dequeue(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine(threw);                     // True
        threw = false;
        try { eq.Peek(); } catch (InvalidOperationException) { threw = true; }
        Console.WriteLine(threw);                     // True

        //===== 8. Clear + reuse; null elements =====
        Stack sc = new Stack();
        PushSeq(sc, 3);
        sc.Clear();
        Console.WriteLine(sc.Count == 0);             // True
        sc.Push(null);
        sc.Push("reuse");
        Console.WriteLine(sc.Count == 2);             // True
        Console.WriteLine((string)sc.Pop() == "reuse"); // True
        Console.WriteLine(sc.Pop() == null);          // True
        Queue qc = new Queue();
        qc.Enqueue("x");
        qc.Clear();
        Console.WriteLine(qc.Count == 0);             // True
        qc.Enqueue(null);
        qc.Enqueue(7);
        Console.WriteLine(qc.Dequeue() == null);      // True
        Console.WriteLine((int)qc.Dequeue() == 7);    // True
    }
}}
