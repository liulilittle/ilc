//------------------------------------------------------------------------------
// threadsync2.cs -- System.Threading DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C44. EXTENDS the thread surface proven
// by threadops/threadops2/threadcase2/conccases/threadext2/threadpoolcase
// (MethodMap.Thread.cs + MethodMap.ThreadPool.cs + MethodMap.Monitor.cs --
// threadobj.cpp / threadpool.cpp / monitor.cpp) with three DEEPER
// synchronization families:
//
//   1) LOCK NESTING (depth 3 + two-object nesting) -- the SAME gate entered
//      three levels deep (reentrant Monitor, recursion depth 3): the monitor
//      recursion counter grows to 3 and unwinds exactly (net48 reentrant lock
//      semantics). PLUS nesting TWO distinct objects (lock a { lock b { } })
//      -- separate monitor entries, no shared state corruption. Deterministic
//      because the main thread does it all (no scheduling dependence).
//   2) WAIT / PULSE family -- ONE waiter blocked in Monitor.Wait(gate, 5000)
//      woken by a single Monitor.Pulse(gate) from the main thread (returns
//      true -- .NET pulse-wait round trip). A TIMEOUT probe: a waiter in
//      Monitor.Wait(gate, 60) with NO pulse times out and returns false. The
//      runtime's SIMPLIFIED pulse wakes ALL waiters (monitor.h), so single-
//      waiter probes are byte-exact with net48 (which wakes the FIFO waiter);
//      multi-waiter Pulse probes would DIVERGE (net48 wakes 1, runtime wakes
//      N) and are therefore recorded, not probed.
//   3) THREAD POOL LOOPBACK -- a pool worker that RE-QUEUES the SAME prebuilt
//      WaitCallback until a bounded target: the first queued item is picked
//      up by a pool worker, increments the seq counter (Interlocked) and, if
//      below kTarget, enqueues the static callback again. Every processed
//      item carries a UNIQUE sequence number (Interlocked.Increment result),
//      so exactly kTarget items are processed no matter how many pool workers
//      race (the re-enqueue decision keys on the returned seq < kTarget).
//      Final g_loop == kTarget exactly -- a deterministic pool loopback.
//
// Determinism contract (the threadext2/conccases model): workers NEVER print
// and NEVER allocate (the M-stage managed allocator is SINGLE-THREADED) --
// worker callbacks only Interlocked / lock / Thread.Sleep / re-queue a
// PREBUILT static WaitCallback field (no delegate construction on the worker).
// Every observable value is read AFTER Thread.Join or after a bounded poll
// (Thread.Sleep(10) x 2000) gated on a monotonic completion counter, so the
// final state is scheduling-independent.
//
// Every line prints "label=True/False". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Threading;

class ThreadSync2
{
    // ---- lock-nesting state (main-thread only; no race) ----
    static readonly object gate = new object();
    static readonly object gateB = new object();
    static int g_nest;        // recursion-depth-3 counter
    static int g_twoObj;      // two-object nested-lock counter

    // ---- wait/pulse state ----
    static int g_w1;          // single Pulse waiter: 1 = woken
    static int g_wReady;      // waiter is inside Monitor.Wait
    static int g_wTimeout;    // timeout waiter: 1 = timed out (Wait returned false)

    // ---- threadpool loopback state ----
    static readonly WaitCallback loopCb = new WaitCallback(LoopWorker); // prebuilt (main)
    static int g_loop;        // processed item sequence (Interlocked)
    const int kTarget = 7;    // bounded loopback depth

    // ============ workers (allocation-free, never print) ============
    static void LoopWorker(object state)
    {
        int seq = Interlocked.Increment(ref g_loop);   // unique 1..kTarget
        if (seq < kTarget)
        {
            ThreadPool.QueueUserWorkItem(loopCb);       // re-queue the SAME static cb
        }
    }

    static void Waiter1()
    {
        lock (gate)
        {
            Interlocked.Increment(ref g_wReady);
            bool w = Monitor.Wait(gate, 5000);
            if (w) g_w1 = 1;
        }
    }

    static void TimeoutWaiter()
    {
        lock (gate)
        {
            bool w = Monitor.Wait(gate, 60);  // no pulse: must time out -> false
            if (!w) g_wTimeout = 1;
        }
    }

    // ============ main ============
    static void Main()
    {
        // ---- 1a. lock nesting depth 3 (reentrant, same gate) ----
        g_nest = 0;
        lock (gate)
        {
            lock (gate)
            {
                lock (gate) { g_nest++; }      // recursion depth 3
            }
        }
        Console.WriteLine("nest_depth3=" + (g_nest == 1));
        // The monitor is fully released after the triple unwind: a TryEnter
        // from the main thread must now acquire it (recursion hit zero).
        bool reacq = Monitor.TryEnter(gate);
        Console.WriteLine("nest_unwound=" + reacq);
        if (reacq) Monitor.Exit(gate);

        // ---- 1b. nesting TWO distinct objects ----
        g_twoObj = 0;
        lock (gate)
        {
            lock (gateB) { g_twoObj++; }       // independent monitors
        }
        Console.WriteLine("nest_twoobj=" + (g_twoObj == 1));

        // ---- 2a. single Pulse wakes the single waiter ----
        g_w1 = 0; g_wReady = 0;
        Thread w = new Thread(Waiter1);
        w.Start();
        for (int i = 0; i < 5000 && g_wReady == 0; i++) Thread.Sleep(1);
        lock (gate) { Monitor.Pulse(gate); }   // single Pulse, ONE waiter
        w.Join();
        Console.WriteLine("pulse_one_woken=" + (g_w1 == 1));

        // ---- 2b. Wait timeout (no pulse -> false) ----
        g_wTimeout = 0;
        Thread tw = new Thread(TimeoutWaiter);
        tw.Start();
        tw.Join();
        Console.WriteLine("wait_timeout=" + (g_wTimeout == 1));

        // ---- 3. threadpool loopback (worker re-queues prebuilt cb) ----
        g_loop = 0;
        ThreadPool.QueueUserWorkItem(loopCb);  // seed the loop
        bool loopDone = false;
        for (int i = 0; i < 2000; i++)
        {
            if (g_loop >= kTarget) { loopDone = true; break; }
            Thread.Sleep(10);
        }
        Console.WriteLine("loop_ran=" + loopDone);
        int loop = g_loop;
        Console.WriteLine("loop_count=" + (loop == kTarget));
    }
}
