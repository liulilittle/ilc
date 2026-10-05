using System;
using System.Collections.Generic;

// g4queueval -- T2 W4 probe: Queue<string/long/double/object>.
// net48 oracle byte-exact.
class Prog
{
    static void Main()
    {
        Queue<string> qs = new Queue<string>();
        qs.Enqueue("a");
        qs.Enqueue("b");
        qs.Enqueue("c");
        Console.WriteLine("scount=" + qs.Count);
        Console.WriteLine("sdeq=" + qs.Dequeue());
        Console.WriteLine("speek=" + qs.Peek());
        Console.WriteLine("shas_b=" + qs.Contains("b"));
        Console.WriteLine("shas_z=" + qs.Contains("z"));
        string[] sa = qs.ToArray();
        Console.WriteLine("sarrlen=" + sa.Length);
        for (int i = 0; i < sa.Length; i++)
            Console.WriteLine("sitem=" + sa[i]);

        Queue<long> ql = new Queue<long>();
        ql.Enqueue(10L);
        ql.Enqueue(20L);
        ql.Enqueue(30L);
        Console.WriteLine("lcount=" + ql.Count);
        Console.WriteLine("ldeq=" + ql.Dequeue());
        Console.WriteLine("lpeek=" + ql.Peek());
        Console.WriteLine("lhas20=" + ql.Contains(20L));
        Console.WriteLine("lhas99=" + ql.Contains(99L));
        long[] la = ql.ToArray();
        Console.WriteLine("larrlen=" + la.Length);
        for (int i = 0; i < la.Length; i++)
            Console.WriteLine("litem=" + la[i]);
        foreach (long v in ql)
            Console.WriteLine("lenum=" + v);

        Queue<double> qd = new Queue<double>();
        qd.Enqueue(1.5);
        qd.Enqueue(2.5);
        Console.WriteLine("dcount=" + qd.Count);
        Console.WriteLine("ddeq=" + qd.Dequeue());
        Console.WriteLine("dpeek=" + qd.Peek());
        Console.WriteLine("dhas25=" + qd.Contains(2.5));

        Queue<object> qo = new Queue<object>();
        qo.Enqueue(null);
        object o = new object();
        qo.Enqueue(o);
        Console.WriteLine("ocount=" + qo.Count);
        Console.WriteLine("odeqnull=" + (qo.Dequeue() == null));
        Console.WriteLine("opeekisobj=" + Object.ReferenceEquals(qo.Peek(), o));

        Console.WriteLine("done");
    }
}
