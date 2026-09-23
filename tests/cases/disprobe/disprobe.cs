using System;
using System.Collections;

namespace Cases { public static class DisProbe {
    public static void Main() {
        // Stack: partial iteration -> Reset rewinds.
        var s = new Stack();
        s.Push(1); s.Push(2); s.Push(3);
        IEnumerator e = s.GetEnumerator();
        e.MoveNext();
        Console.WriteLine((int)e.Current == 3);
        e.Reset();
        e.MoveNext();
        Console.WriteLine((int)e.Current == 3);
        // Explicit ((IDisposable)e).Dispose(): the Stack struct enumerator
        // does NOT implement IDisposable -- net48 throws InvalidCastException.
        bool stackDisposeThrown = false;
        try { ((IDisposable)e).Dispose(); } catch (InvalidCastException) { stackDisposeThrown = true; }
        Console.WriteLine(stackDisposeThrown);
        int n = 0;
        foreach (object o in s) { n += (int)o; }
        Console.WriteLine(n == 6);
        // Queue: same path, FRONT first.
        var q = new Queue();
        q.Enqueue("a"); q.Enqueue("b");
        IEnumerator qe = q.GetEnumerator();
        qe.MoveNext();
        Console.WriteLine((string)qe.Current == "a");
        qe.Reset();
        qe.MoveNext();
        Console.WriteLine((string)qe.Current == "a");
        bool queueDisposeThrown = false;
        try { ((IDisposable)qe).Dispose(); } catch (InvalidCastException) { queueDisposeThrown = true; }
        Console.WriteLine(queueDisposeThrown);
        int m = 0;
        foreach (object o in q) { m++; }
        Console.WriteLine(m == 2);
    }
}}
