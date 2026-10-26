using System;
using System.Collections;
namespace Cases { public static class LoudRest1QsCap {
    public static void Main() {
        // Queue(int capacity) / Stack(int initialCapacity): the net48 capacity
        // ctors (previously a LOUD rejection -- "the runtime has only the
        // parameterless create icall"). The observable surface is Count /
        // Enqueue / Dequeue / Peek / Push / Pop.
        var q = new Queue(8);
        q.Enqueue("a");
        q.Enqueue("b");
        q.Enqueue("c");
        Console.WriteLine(q.Count);
        Console.WriteLine(q.Dequeue());
        Console.WriteLine(q.Peek());

        var s = new Stack(8);
        s.Push(1);
        s.Push(2);
        s.Push(3);
        Console.WriteLine(s.Count);
        Console.WriteLine(s.Pop());
        Console.WriteLine(s.Peek());
    }
}}
