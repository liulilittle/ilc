using System;
using System.Collections.Generic;

// g4stackstr -- G4 Stack<string> e2e case (GREEN since the string expansion:
// reference-element members over the non-generic stack object + contains
// scan and a System.String[]-tracked ToArray). Stack<T>.ToArray is TOP-first
// (LIFO), so the order is deterministic.
class Prog
{
    static void Main()
    {
        Stack<string> s = new Stack<string>();
        s.Push("a");
        s.Push("b");
        s.Push("c");
        Console.WriteLine("count=" + s.Count);
        Console.WriteLine("peek=" + s.Peek());
        Console.WriteLine("contains_a=" + s.Contains("a"));
        Console.WriteLine("contains_z=" + s.Contains("z"));
        Console.WriteLine("pop=" + s.Pop());
        Console.WriteLine("count2=" + s.Count);

        string[] arr = s.ToArray();
        Console.WriteLine("arrlen=" + arr.Length);
        for (int i = 0; i < arr.Length; i++)
            Console.WriteLine("item=" + arr[i]);

        Console.WriteLine("done");
    }
}
