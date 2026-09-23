using System;
using System.Collections.Generic;

// g4queueint -- G4 Queue<int> e2e case (GREEN since the int expansion:
// box/unbox wrappers over the non-generic queue object + a real int32[]
// ToArray). Deterministic: no time/random/paths.
class Prog
{
    static void Main()
    {
        Queue<int> q = new Queue<int>();
        q.Enqueue(1);
        q.Enqueue(2);
        q.Enqueue(3);
        Console.WriteLine("count=" + q.Count);
        Console.WriteLine("peek=" + q.Peek());
        Console.WriteLine("contains2=" + q.Contains(2));
        Console.WriteLine("contains9=" + q.Contains(9));
        Console.WriteLine("deq=" + q.Dequeue());
        Console.WriteLine("count2=" + q.Count);

        int[] arr = q.ToArray();
        Console.WriteLine("arrlen=" + arr.Length);
        for (int i = 0; i < arr.Length; i++)
            Console.WriteLine("item=" + arr[i]);

        Console.WriteLine("done");
    }
}
