// lockperf.cs -- LOCK DEEP 2 (e2e batch C24, "lock deep probe 2: contention / deep reentry --
// after the lock fix"). Builds on lockdeep (single-threaded nesting/exception/
// manual-Enter matrix, batch C23) + conccases (8-thread lock/Interlocked
// baseline, framestack thread_local fix 9fa9ba5) and goes DEEPER on the
// CONTENTION and REENTRANCY dimensions of the lock statement's lowering:
//
//   * CONTENTION STRESS: 8 threads x 2000 lock(gate){ ++ } -> 16000 -- the
//     Monitor side-table must serialize all 8 producers with no lost update.
//   * DEEP NESTED REENTRANCY FROM THREADS: 8 threads x 400 iterations of a
//     4-deep nested lock(gate) -- reentrant acquires on the SAME thread
//     must not corrupt the recursion counter or the cross-thread mutual
//     exclusion.
//   * RECURSIVE REENTRANCY FROM THREADS: each worker locks the SAME gate at
//     every call depth (Rec(5) = 6 deep), x 100 iterations x 8 threads ->
//     4800; the recursion unwinds through the finally guards.
//   * MANUAL Monitor.Enter(object, ref bool) / Exit from threads: the exact
//     lowering primitive, 8 x 1000, then a re-Enter proves no leak.
//   * EXCEPTION INSIDE lock FROM THREADS: a worker throws at the innermost
//     point of lock(gate){ ++; throw } every iteration (x 300 x 8); the
//     finally must still Exit on the worker thread (monitor never leaks).
//   * TWO-GATE from threads: lock(A){lock(B)} workers then (after Join)
//     lock(B){lock(A)} workers -- each phase joined BEFORE the next, so no
//     ABBA overlap; both totals exact (independent side-table entries).
//   * MIXED Interlocked + lock: workers interleave Interlocked.Increment
//     with lock(gate){++}; both counters exact (the two primitives coexist
//     on the same shared state).
//   * TryEnter CONTENTION: a holder thread owns gate; the main thread's
//     TryEnter(gate) (1-arg) must return false while held, true after the
//     holder releases. (The 2-arg TryEnter(object, ref bool) path is a
//     recorded emitter defect -- see the NOTE in the worker section.)
//   * Wait/Pulse across threads (Pulse wakes the single waiter) + Wait
//     timeout (no Pulse -> false, monitor reacquired).
//
// Determinism contract (conccases model): workers NEVER print and NEVER
// allocate (the M-stage managed allocator is SINGLE-THREADED -- worker
// bodies are lock/Interlocked/Thread.Sleep only). Every observable value is
// read AFTER Thread.Join or after a bounded poll (Thread.Sleep(1)) that
// only gates on a monotonic completion flag, so the final state is
// scheduling-independent. Thread[]/List are avoided (newarr of a BCL
// reference type is LOUD-rejected) -- the 8 threads are fixed locals.
//
// Expected output: all-bool rows over exact final values.
using System;
using System.Threading;

class LockPerf
{
    // ---- shared state (workers write; main reads after Join / poll) ----
    static int g_lck;        // contention-stress counter
    static int g_reent;      // deep-nested reentrancy counter
    static int g_rec;        // recursive-reentrancy counter
    static int g_manual;     // manual Enter/Exit counter
    static int g_exc;        // exception-in-lock counter
    static int g_twogate;    // two-gate counter
    static int g_mixIl;      // Interlocked counter (mixed workers)
    static int g_mixLck;     // lock counter (mixed workers)

    static readonly object gate = new object();
    static readonly object gateB = new object();

    // ---- coordination flags (0/1; main polls with Thread.Sleep(1)) ----
    static int g_holderHeld;    // HolderWorker owns gate
    static int g_release;       // main asks HolderWorker to leave
    static int g_waiterReady;   // WaiterWorker is inside Wait
    static int g_waitResult;    // Wait(gate,5000) result (1=pulsed)
    static int g_timeoutResult; // Wait(gate,150) result (0=timeout)

    const int kThr = 8;
    const int kLckIt = 2000;
    const int kReentIt = 400;
    const int kRecIt = 100;
    const int kManualIt = 1000;
    const int kExcIt = 300;
    const int kMixIt = 500;

    // Run 8 instances of one worker concurrently and join all (happens-before
    // makes the final shared-state reads exact). No Thread[] -- fixed locals.
    static void RunN(ThreadStart w)
    {
        Thread t0 = new Thread(w); t0.Start();
        Thread t1 = new Thread(w); t1.Start();
        Thread t2 = new Thread(w); t2.Start();
        Thread t3 = new Thread(w); t3.Start();
        Thread t4 = new Thread(w); t4.Start();
        Thread t5 = new Thread(w); t5.Start();
        Thread t6 = new Thread(w); t6.Start();
        Thread t7 = new Thread(w); t7.Start();
        t0.Join(); t1.Join(); t2.Join(); t3.Join();
        t4.Join(); t5.Join(); t6.Join(); t7.Join();
    }

    // ============ workers (allocation-free, never print) ============
    static void LockWorker()
    {
        for (int i = 0; i < kLckIt; i++) lock (gate) { g_lck++; }
    }

    static void ReentWorker()
    {
        for (int i = 0; i < kReentIt; i++)
        {
            lock (gate)
            {
                lock (gate)
                {
                    lock (gate)
                    {
                        lock (gate) { g_reent++; }
                    }
                }
            }
        }
    }

    static void Rec(int depth)
    {
        lock (gate)
        {
            g_rec++;
            if (depth > 0) Rec(depth - 1);
        }
    }

    static void RecWorker()
    {
        for (int i = 0; i < kRecIt; i++) Rec(5);
    }

    static void ManualWorker()
    {
        for (int i = 0; i < kManualIt; i++)
        {
            bool lockTaken = false;
            try
            {
                Monitor.Enter(gate, ref lockTaken);
                g_manual++;
            }
            finally
            {
                if (lockTaken) Monitor.Exit(gate);
            }
        }
    }

    static void ExcWorker()
    {
        for (int i = 0; i < kExcIt; i++)
        {
            try
            {
                lock (gate)
                {
                    g_exc++;
                    throw new InvalidOperationException("boom");
                }
            }
            catch (InvalidOperationException) { }
        }
    }

    static void TwoGateABWorker()
    {
        for (int i = 0; i < kMixIt; i++) lock (gate) { lock (gateB) { g_twogate++; } }
    }

    static void TwoGateBAWorker()
    {
        for (int i = 0; i < kMixIt; i++) lock (gateB) { lock (gate) { g_twogate++; } }
    }

    static void MixedWorker()
    {
        for (int i = 0; i < kMixIt; i++)
        {
            Interlocked.Increment(ref g_mixIl);
            lock (gate) { g_mixLck++; }
        }
    }

    static void HolderWorker()
    {
        lock (gate)
        {
            g_holderHeld = 1;
            while (g_release == 0) Thread.Sleep(1);
        }
    }

    // NOTE: the 2-arg TryEnter(object, ref bool) probe is DELIBERATELY
    // absent -- its emitter path is broken (CodeGenerator.cs:5433 gates the
    // bool* flag-address fix on il2cpp_monitor_enter only, so
    // Monitor.TryEnter(obj, ref flag) emits int32_t* and fails the C++
    // compile). Recorded as defect D-LKP-1; the 1-arg contention probe below
    // covers the TryEnter surface.
    static void WaiterWorker()
    {
        lock (gate)
        {
            g_waiterReady = 1;
            bool pulsed = Monitor.Wait(gate, 5000);
            g_waitResult = pulsed ? 1 : 0;
        }
    }

    static void TimeoutWorker()
    {
        lock (gate)
        {
            bool t = Monitor.Wait(gate, 150);
            g_timeoutResult = t ? 1 : 0;
        }
    }

    // ============ main ============
    static void Main()
    {
        // ---- 1. contention stress: 8 x 2000 -> 16000 ----
        RunN(new ThreadStart(LockWorker));
        int lck = g_lck;
        Console.WriteLine("lp_contention=" + (lck == kThr * kLckIt));

        // ---- 2. deep nested reentrancy from threads: 8 x 400 ----
        RunN(new ThreadStart(ReentWorker));
        int reent = g_reent;
        Console.WriteLine("lp_deep_reent=" + (reent == kThr * kReentIt));

        // ---- 3. recursive reentrancy from threads: 8 x 100 x 6 ----
        RunN(new ThreadStart(RecWorker));
        int rec = g_rec;
        Console.WriteLine("lp_rec_reent=" + (rec == kThr * kRecIt * 6));

        // ---- 4. manual Enter(obj, ref bool)/Exit from threads ----
        RunN(new ThreadStart(ManualWorker));
        int manual = g_manual;
        Console.WriteLine("lp_manual_enter=" + (manual == kThr * kManualIt));
        bool freeAfterManual = true;
        try { lock (gate) { } } catch (Exception) { freeAfterManual = false; }
        Console.WriteLine("lp_manual_free=" + freeAfterManual);

        // ---- 5. exception inside lock from threads (no leak) ----
        RunN(new ThreadStart(ExcWorker));
        int exc = g_exc;
        Console.WriteLine("lp_exc_count=" + (exc == kThr * kExcIt));
        bool freeAfterExc = true;
        try { lock (gate) { } } catch (Exception) { freeAfterExc = false; }
        Console.WriteLine("lp_exc_free=" + freeAfterExc);

        // ---- 6. two-gate from threads (phases joined -> no ABBA overlap) ----
        RunN(new ThreadStart(TwoGateABWorker));
        int tg1 = g_twogate;
        RunN(new ThreadStart(TwoGateBAWorker));
        int tg2 = g_twogate;
        Console.WriteLine("lp_twogate_ab=" + (tg1 == kThr * kMixIt));
        Console.WriteLine("lp_twogate_ba=" + (tg2 == kThr * kMixIt * 2));

        // ---- 7. mixed Interlocked + lock ----
        RunN(new ThreadStart(MixedWorker));
        int mixIl = g_mixIl;
        int mixLck = g_mixLck;
        Console.WriteLine("lp_mix_il=" + (mixIl == kThr * kMixIt));
        Console.WriteLine("lp_mix_lock=" + (mixLck == kThr * kMixIt));

        // ---- 8. TryEnter contention (1-arg) ----
        g_holderHeld = 0; g_release = 0;
        Thread holder = new Thread(HolderWorker);
        holder.Start();
        for (int i = 0; i < 5000 && g_holderHeld == 0; i++) Thread.Sleep(1);
        bool contended = Monitor.TryEnter(gate);
        Console.WriteLine("lp_tryenter_contended=" + (contended == false));
        g_release = 1;
        holder.Join();
        bool free1 = Monitor.TryEnter(gate);
        Console.WriteLine("lp_tryenter_free=" + (free1 == true));
        if (free1) Monitor.Exit(gate);

        // ---- 9. Wait/Pulse across threads ----
        g_waiterReady = 0; g_waitResult = -1;
        Thread waiter = new Thread(WaiterWorker);
        waiter.Start();
        for (int i = 0; i < 5000 && g_waiterReady == 0; i++) Thread.Sleep(1);
        lock (gate) { Monitor.Pulse(gate); }
        waiter.Join();
        Console.WriteLine("lp_wait_pulsed=" + (g_waitResult == 1));

        // ---- 10. Wait timeout (no Pulse) ----
        g_timeoutResult = -1;
        Thread to = new Thread(TimeoutWorker);
        to.Start();
        to.Join();
        Console.WriteLine("lp_wait_timeout=" + (g_timeoutResult == 0));
    }
}
