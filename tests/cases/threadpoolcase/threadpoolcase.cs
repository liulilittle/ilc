using System;
using System.Threading;

// threadpoolcase.cs -- System.Threading.ThreadPool probe: a QueueUserWorkItem
// fan-out chain. The net48 oracle and the M-stage runtime must agree:
//
//   QueueUserWorkItem(WaitCallback, object) -> bool   (2-arg overload)
//   QueueUserWorkItem(WaitCallback)         -> bool   (1-arg overload)
//   a loop queues kTarget - 2 more items (state = boxed int)
//   the main thread polls g_done (bounded, Thread.Sleep(10) steps)
//
// M-stage contract: worker callbacks are allocation-free (static int field
// writes only -- the managed allocator is single-threaded); ALL delegate
// construction and int boxing happens on the MAIN thread, before/while the
// pool workers run.
class ThreadPoolCase
{
    const int kTarget = 8;
    static int g_done;

    static void Worker(object state)
    {
        g_done++;
    }

    static void Main()
    {
        bool ok2 = ThreadPool.QueueUserWorkItem(new WaitCallback(Worker), "s");
        Console.WriteLine("queued 2-arg: " + ok2);
        bool ok1 = ThreadPool.QueueUserWorkItem(new WaitCallback(Worker));
        Console.WriteLine("queued 1-arg: " + ok1);
        for (int i = 0; i < kTarget - 2; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(Worker), i);
        bool allRan = false;
        for (int i = 0; i < 2000; i++)
        {
            if (g_done >= kTarget) { allRan = true; break; }
            Thread.Sleep(10);
        }
        Console.WriteLine("all ran: " + allRan);
        // read into a local first: `"..." + g_done` compiles to
        // ldsflda + Int32::ToString(), and the fold has no Ldsflda case
        // (recorded gap -- StackFold.cs default LOUD). The local pattern
        // (ldsfld; stloc; ldloc) folds fine.
        int done = g_done;
        Console.WriteLine("done count: " + done);
    }
}
