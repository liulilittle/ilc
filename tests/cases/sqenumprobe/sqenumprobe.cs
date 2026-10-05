using System;
using System.Collections;

namespace Cases
{
    public static class SqEnumProbe
    {
        public static void Main()
        {
            // Minimal foreach repro: Stack enumerator (TOP-of-stack first).
            Stack s = new Stack();
            for (int i = 0; i < 4; i++) s.Push(i);
            int n = 0;
            foreach (object o in s) { n++; Console.WriteLine("stack" + n + "=" + (int)o); }
            Console.WriteLine("stackcount=" + n);

            // Minimal foreach repro: Queue enumerator (FRONT first).
            Queue q = new Queue();
            for (int i = 0; i < 4; i++) q.Enqueue(10 + i);
            int m = 0;
            foreach (object o in q) { m++; Console.WriteLine("queue" + m + "=" + (int)o); }
            Console.WriteLine("queuecount=" + m);
        }
    }
}
