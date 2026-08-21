//------------------------------------------------------------------------------
// waithandlecase.cs -- System.Threading wait-handle e2e oracle (Semaphore /
// Mutex / EventWaitHandle). The transpiler's waithandle domain
// (il2cpp_waithandle.h) wires the ctors, WaitOne, Release/ReleaseMutex and
// Set/Reset. Every output is a deterministic bool; expected.txt is generated
// from a net48 reference run.
//
// Determinism contract (the threadsync2/conccases model): the worker thread
// NEVER prints and NEVER allocates (the M-stage managed allocator is
// SINGLE-THREADED) -- it only WaitOne's and writes static fields. The
// observable values are read after a bounded poll (Thread.Sleep(1) x 2000)
// gated on a completion counter, then after Thread.Join, so the final state
// is scheduling-independent.
//------------------------------------------------------------------------------

using System;
using System.Threading;

class WaitHandleCase
{
    static Semaphore g_sem;
    static int g_entered;
    static int g_done;

    static void Worker()
    {
        if (g_sem.WaitOne()) { g_entered = 1; }
        g_done = 1;
    }

    static void Main()
    {
        // ---- 1. Semaphore: WaitOne decrements, Release increments ----
        var sem = new Semaphore(1, 2);
        Console.WriteLine(sem.WaitOne());
        Console.WriteLine(sem.Release() == 0);
        Console.WriteLine(sem.Release() == 1);

        // ---- 2. Mutex: WaitOne/ReleaseMutex round-trip ----
        var mtx = new Mutex();
        Console.WriteLine(mtx.WaitOne());
        mtx.ReleaseMutex();
        Console.WriteLine(mtx.WaitOne());
        mtx.ReleaseMutex();

        // ---- 3. EventWaitHandle: auto-reset clears, manual-reset keeps ----
        var ev = new EventWaitHandle(true, EventResetMode.AutoReset);
        Console.WriteLine(ev.WaitOne());
        ev.Set();
        Console.WriteLine(ev.WaitOne());
        var evm = new EventWaitHandle(true, EventResetMode.ManualReset);
        Console.WriteLine(evm.WaitOne());
        Console.WriteLine(evm.WaitOne());
        evm.Reset();

        // ---- 4. Two-thread semaphore: worker blocks, main releases ----
        g_sem = new Semaphore(0, 1);
        var t = new Thread(Worker);
        t.Start();
        for (int i = 0; i < 2000 && g_done == 0; i++) Thread.Sleep(1);
        Console.WriteLine(g_entered == 0);
        g_sem.Release();
        t.Join();
        Console.WriteLine(g_entered == 1);
        Console.WriteLine(g_done == 1);
    }
}
