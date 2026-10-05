using System;
using System.Threading;

// monwait -- T8 parity probe: Monitor timeout forms (deterministic, no
// sleeps except a 1ms bounded wait with no pulser).
class Prog
{
    static object _lock = new object();
    static void Main()
    {
        Console.WriteLine("te0=" + Monitor.TryEnter(_lock, 0));
        Monitor.Exit(_lock);
        bool flag = false;
        Monitor.TryEnter(_lock, 5, ref flag);
        Console.WriteLine("te5=" + flag);
        Monitor.Exit(_lock);
        lock (_lock)
        {
            Console.WriteLine("w0=" + Monitor.Wait(_lock, 0));
            Console.WriteLine("wts=" + Monitor.Wait(_lock, TimeSpan.Zero));
            Console.WriteLine("wx=" + Monitor.Wait(_lock, 1, true));
        }
        Console.WriteLine("done");
    }
}
