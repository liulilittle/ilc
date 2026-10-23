using System;
using System.Collections.Generic;

// hseta1 -- HashSet<int> basic semantics + ENUMERATION ORDER vs net48.
// net48 HashSet<T> enumerates the internal _entries array (insertion order,
// with LIFO reuse of freed slots), NOT bucket order. This probe pins the
// exact sequence after add/remove/add so a bucket-ordered implementation
// fails byte-exact.
class Prog
{
    static void Main()
    {
        HashSet<int> s = new HashSet<int>();
        Console.WriteLine("c0=" + s.Count);
        Console.WriteLine("add3=" + s.Add(3));
        Console.WriteLine("add1=" + s.Add(1));
        Console.WriteLine("add2=" + s.Add(2));
        Console.WriteLine("add2b=" + s.Add(2));
        Console.WriteLine("c3=" + s.Count);
        Console.WriteLine("has2=" + s.Contains(2));
        Console.WriteLine("has9=" + s.Contains(9));
        Console.WriteLine("rm2=" + s.Remove(2));
        Console.WriteLine("rm9=" + s.Remove(9));
        Console.WriteLine("c2=" + s.Count);
        s.Add(7);
        s.Add(2);
        s.Add(5);
        Console.WriteLine("order:");
        foreach (int v in s) Console.WriteLine("  v=" + v);
        Console.WriteLine("clear:");
        s.Clear();
        Console.WriteLine("c0b=" + s.Count);
        s.Add(9);
        s.Add(8);
        foreach (int v in s) Console.WriteLine("  w=" + v);

        HashSet<string> t = new HashSet<string>();
        t.Add("delta");
        t.Add("alpha");
        t.Add("charlie");
        t.Add("bravo");
        t.Remove("charlie");
        t.Add("echo");
        Console.WriteLine("sorder:");
        foreach (string v in t) Console.WriteLine("  s=" + v);
        Console.WriteLine("scount=" + t.Count);
    }
}
