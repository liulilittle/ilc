// stackq.cs -- System.Collections.Stack / Queue e2e reference case (net48 baseline).
//
// NEW-DOMAIN case (batch B bonus): the non-generic object collections.
// MethodMap.Stack.cs / MethodMap.Queue.cs map to the stackqueue.h runtime
// surface (il2cpp_stack_* / il2cpp_queue_*, PLAIN il2cpp:: namespace):
//
//   Stack: Push / Pop (LIFO) / Peek (no removal) / get_Count / Clear
//   Queue: Enqueue / Dequeue (FIFO) / Peek (no removal) / get_Count / Clear
//
// Elements are boxed Il2CppObject* slots -- Push/Enqueue box the values
// (box wiring), Pop/Dequeue/Peek return objects that the CALLER unboxes
// ((int)/(string)/(bool) casts). Mixed-type elements (int + string + bool)
// probe the unbox dispatch on the way out.
//
// All probes are deterministic bool/int assertions.

using System;
using System.Collections;
namespace Cases { public static class StackQ {
    public static void Main() {
        // ---- Stack (LIFO) ----
        Stack st = new Stack();
        Console.WriteLine(st.Count == 0);          // True
        st.Push(1);
        st.Push(2);
        st.Push(3);
        Console.WriteLine(st.Count);               // 3
        Console.WriteLine((int)st.Peek());         // 3
        Console.WriteLine(st.Count);               // 3 (Peek does not remove)
        Console.WriteLine((int)st.Pop());          // 3
        Console.WriteLine((int)st.Pop());          // 2
        Console.WriteLine((int)st.Pop());          // 1
        Console.WriteLine(st.Count == 0);          // True
        // mixed-type stack elements
        Stack sm = new Stack();
        sm.Push("top");
        sm.Push(42);
        sm.Push(true);
        Console.WriteLine((bool)sm.Pop());         // True
        Console.WriteLine((int)sm.Pop());          // 42
        Console.WriteLine((string)sm.Pop());       // top
        // Clear
        Stack sc = new Stack();
        sc.Push("a");
        sc.Push("b");
        sc.Clear();
        Console.WriteLine(sc.Count);               // 0

        // ---- Queue (FIFO) ----
        Queue q = new Queue();
        Console.WriteLine(q.Count == 0);           // True
        q.Enqueue("first");
        q.Enqueue("second");
        q.Enqueue("third");
        Console.WriteLine(q.Count);                // 3
        Console.WriteLine((string)q.Peek());       // first
        Console.WriteLine(q.Count);                // 3 (Peek does not remove)
        Console.WriteLine((string)q.Dequeue());    // first
        Console.WriteLine((string)q.Dequeue());    // second
        Console.WriteLine((string)q.Dequeue());    // third
        Console.WriteLine(q.Count == 0);           // True
        // mixed-type queue elements
        Queue qm = new Queue();
        qm.Enqueue(7);
        qm.Enqueue("mid");
        qm.Enqueue(false);
        Console.WriteLine((int)qm.Dequeue());      // 7
        Console.WriteLine((string)qm.Dequeue());   // mid
        Console.WriteLine((bool)qm.Dequeue());     // False
        // Clear
        Queue qc = new Queue();
        qc.Enqueue("x");
        qc.Clear();
        Console.WriteLine(qc.Count);               // 0
    }
}}
