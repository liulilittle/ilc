using System;
using System.Threading;

// threadcase.cs -- net48 reference oracle for the System.Threading.Thread
// domain: the statics (Sleep / Yield / critical regions / TickCount) AND
// the managed Thread OBJECT model (new Thread / Start / Join / IsAlive /
// ManagedThreadId / CurrentThread / Priority).
//
// The statics map to the thread.cpp icalls (il2cpp_thread.h); the object
// model maps to the threadobj domain (threadobj.h: System_Threading_Thread_
// class descriptor + icall_threadobj_*). The runtime runs REAL OS threads.
//
// M-stage constraint: the managed allocator is SINGLE-THREADED, so the
// child-thread callback must NOT allocate -- ChildWorker only writes static
// int32 fields and calls Thread.Sleep (both allocation-free).
//
// Expected output (deterministic; see expected.txt):
//   sleep50 delta >= 45: True      (real 50 ms sleep on the main thread)
//   sleep0 returned: True
//   sleep(-2) throws: True         (every negative value, incl. -1, throws)
//   yield returned: True
//   priority normal: True          (priority is pinned to Normal)
//   managed thread id nonzero: True (OS thread id)
//   critical regions ok: True
//   isalive before start: True
//   isalive after start: True      (the child sleeps 50 ms -- still running)
//   join waited: True              (Join blocked until the child finished)
//   child ran: True                (the child ran to completion)
//   child id != main id: True
//   child ManagedThreadId nonzero: True
//   isalive after join: True
//
// Note: Thread.Sleep(-1) (== Timeout.Infinite) BLOCKS FOREVER on net48, so
// the throw probe uses -2, which is also negative and must throw
// ArgumentOutOfRangeException. The runtime icall rejects EVERY negative
// value (documented in il2cpp_thread.h).
class ThreadCase
{
    // Written by the child thread (allocation-free callback); read by the
    // main thread after Join (happens-before through the join).
    static int g_childRan;

    // The ThreadStart callback: runs on a REAL OS thread. No allocations --
    // the M-stage allocator is single-threaded (the child may not touch
    // the managed heap). The 50 ms sleep keeps IsAlive-after-Start
    // deterministic (the child is still running when the main thread
    // probes it).
    static void ChildWorker()
    {
        g_childRan = 1;
        Thread.Sleep(50);
        g_childRan = 2;
    }

    static void Main()
    {
        // Environment.TickCount (32-bit signed ms) paired with Sleep(50):
        // the delta must be >= 45 ms.
        int t0 = Environment.TickCount;
        Thread.Sleep(50);
        int t1 = Environment.TickCount;
        Console.WriteLine("sleep50 delta >= 45: " + (t1 - t0 >= 45));

        // Sleep(0) yields the time slice and returns immediately.
        Thread.Sleep(0);
        Console.WriteLine("sleep0 returned: True");

        // A negative argument other than -1 must throw.
        try
        {
            Thread.Sleep(-2);
            Console.WriteLine("sleep(-2) throws: False");
        }
        catch (ArgumentOutOfRangeException)
        {
            Console.WriteLine("sleep(-2) throws: True");
        }

        // Thread.Yield: returns, no crash.
        Thread.Yield();
        Console.WriteLine("yield returned: True");

        // Priority is pinned to Normal in the runtime model.
        Console.WriteLine("priority normal: " + (Thread.CurrentThread.Priority == ThreadPriority.Normal));

        // ManagedThreadId of the main thread is the (nonzero) OS thread id.
        Console.WriteLine("managed thread id nonzero: " + (Thread.CurrentThread.ManagedThreadId != 0));

        // BeginCriticalRegion/EndCriticalRegion are no-op placeholders.
        Thread.BeginCriticalRegion();
        Thread.EndCriticalRegion();
        Console.WriteLine("critical regions ok: True");

        // --- Thread OBJECT model: new Thread / Start / Join / IsAlive ---
        Thread t = new Thread(ChildWorker);
        Console.WriteLine("isalive before start: " + (t.IsAlive == false));
        t.Start();
        Console.WriteLine("isalive after start: " + t.IsAlive);
        int tt0 = Environment.TickCount;
        t.Join();
        int tt1 = Environment.TickCount;
        Console.WriteLine("join waited: " + (tt1 - tt0 >= 45));
        Console.WriteLine("child ran: " + (g_childRan == 2));
        Console.WriteLine("child id != main id: " + (t.ManagedThreadId != Thread.CurrentThread.ManagedThreadId));
        Console.WriteLine("child ManagedThreadId nonzero: " + (t.ManagedThreadId != 0));
        Console.WriteLine("isalive after join: " + (t.IsAlive == false));
    }
}
