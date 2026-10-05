using System;
using System.Collections.Generic;

// g4stackval -- T2 W4 probe: Stack<int/long/double/object>.
// net48 oracle byte-exact.
class Prog
{
    static void Main()
    {
        Stack<int> si = new Stack<int>();
        si.Push(1);
        si.Push(2);
        si.Push(3);
        Console.WriteLine("icount=" + si.Count);
        Console.WriteLine("ipop=" + si.Pop());
        Console.WriteLine("ipeek=" + si.Peek());
        Console.WriteLine("ihas2=" + si.Contains(2));
        Console.WriteLine("ihas9=" + si.Contains(9));
        int[] ia = si.ToArray();
        Console.WriteLine("iarrlen=" + ia.Length);
        for (int i = 0; i < ia.Length; i++)
            Console.WriteLine("iitem=" + ia[i]);
        foreach (int v in si)
            Console.WriteLine("ienum=" + v);

        Stack<long> sl = new Stack<long>();
        sl.Push(100L);
        sl.Push(200L);
        Console.WriteLine("lcount=" + sl.Count);
        Console.WriteLine("lpop=" + sl.Pop());
        Console.WriteLine("lpeek=" + sl.Peek());
        Console.WriteLine("lhas100=" + sl.Contains(100L));
        long[] la = sl.ToArray();
        Console.WriteLine("larrlen=" + la.Length);
        for (int i = 0; i < la.Length; i++)
            Console.WriteLine("litem=" + la[i]);

        Stack<double> sd = new Stack<double>();
        sd.Push(1.5);
        sd.Push(2.5);
        Console.WriteLine("dcount=" + sd.Count);
        Console.WriteLine("dpop=" + sd.Pop());
        Console.WriteLine("dpeek=" + sd.Peek());
        Console.WriteLine("dhas15=" + sd.Contains(1.5));

        Stack<object> so = new Stack<object>();
        so.Push(null);
        object o = new object();
        so.Push(o);
        Console.WriteLine("ocount=" + so.Count);
        Console.WriteLine("opopisobj=" + Object.ReferenceEquals(so.Pop(), o));
        Console.WriteLine("opeeknull=" + (so.Peek() == null));

        Console.WriteLine("done");
    }
}
