// conccases.cs -- CONCURRENCY deep probe (e2e batch C21, "并发深测:
// Interlocked/锁/Monitor 重入/ThreadPool 互作, 线程域后"). Builds on the
// thread domain (threadcase/threadcase2/bgthread) + the M7 ref/out wiring
// (interlockedcase / lockcase TODO: the 8-thread Interlocked accumulation
// interlockedcase.cs:16-20 explicitly deferred until threading).
//
// Determinism contract (the threadcase2 model): workers NEVER print and
// NEVER allocate (the M-stage managed allocator is SINGLE-THREADED -- worker
// callbacks only Interlocked/lock/Thread.Sleep, all allocation-free). Every
// observable value is read AFTER Thread.Join or after a bounded poll
// (Thread.Sleep(1)) that only gates on a monotonic completion counter, so
// the final state is scheduling-independent.
//
// Coverage:
//   * Interlocked threaded accumulation -- 8 threads x 1000 Increment(int) ->
//     8000; 8 x 1000 Add(int, 2) -> 16000. Single-thread value-semantics deep
//     pins (wrap at int.MaxValue, CompareExchange no-match, Exchange returns
//     OLD, long matrix on locals) + SpeculationBarrier.
//   * lock across threads -- 8 threads x 500 lock(gate){ ++ } -> 4000 (the
//     Monitor.Enter/Exit thread-safe side-table, monitor.cpp).
//   * Monitor REENTRANCY from threads -- nested lock(gate){ lock(gate){ } }
//     per worker iteration.
//   * Monitor.TryEnter contention -- a holder thread owns the gate; the main
//     thread's TryEnter(gate) must return false, then true after the holder
//     releases (1-arg TryEnter; the timeout overload is LOUD-unwired).
//   * Monitor.Wait/Pulse -- waiter blocks in Wait(gate, 5000), main Pulse(gate)
//     wakes it (Wait returns true). Wait timeout: no Pulse -> Wait(gate,150)
//     returns false. (2-arg Wait only -- Wait(obj) 1-arg is LOUD-unwired.)
//   * ThreadPool interop -- QueueUserWorkItem fan-out whose workers mix
//     Interlocked.Increment and lock, plus Interlocked.Exchange from pool
//     workers (last-write-wins claim probe).
//
// KNOWN RUNTIME DEFECT (recorded, not fixed): framestack.cpp:31 -- g_frameHead
// is a plain GLOBAL (the file header claims "thread-local" but the threading
// task never converted it). A worker that pushes a managed frame (ANY ref-typed
// local/param: a `lock(gate)` gate local, an object pool state) corrupts the
// shared chain when running concurrently; the worker's il2cpp_frame_pop aborts
// with "frame ... is not the current top". Workers WITHOUT managed frames
// (pure Interlocked on static int -- IncWorker/AddWorker) run concurrently
// cleanly. The net48 oracle is all-True; the ILC side currently FAILS the
// lock/Monitor/ThreadPool sections until the frame stack becomes thread-local.
//
// Expected output: all-bool rows over deterministic final values.
using System;
using System.Threading;

class ConcCases
{
    // ---- shared state (workers write; main reads after Join / poll) ----
    static int g_inc;      // Interlocked.Increment counter
    static int g_add;      // Interlocked.Add counter
    static int g_lck;      // lock-protected counter
    static int g_reent;    // nested-lock counter
    static readonly object gate = new object();

    // ---- coordination flags (0/1; main polls with Thread.Sleep(1)) ----
    static int g_holderHeld;   // HolderWorker owns gate
    static int g_release;      // main asks HolderWorker to leave
    static int g_waiterReady;  // WaiterWorker is inside Wait
    static int g_waitResult;   // Wait(gate,5000) result (1=pulsed)
    static int g_timeoutResult;// Wait(gate,150) result (0=timeout)

    // ---- thread pool workers ----
    static int g_poolInc;      // pool IncWorker increments
    static int g_poolLck;      // pool LockWorker lock count
    static int g_poolDone;     // pool workers that finished
    static int g_last;         // last Interlocked.Exchange winner

    const int kIncThr = 8;
    const int kIncIt = 1000;
    const int kAddThr = 8;
    const int kAddIt = 1000;
    const int kLockThr = 8;
    const int kLockIt = 500;
    const int kReentThr = 8;
    const int kReentIt = 200;

    // Run 8 instances of one worker concurrently and join all (happens-before
    // makes the final shared-state reads exact). No Thread[]/List<Thread> --
    // newarr of a BCL reference type is LOUD-rejected (audit C4), so the 8
    // threads are fixed locals.
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
    static void IncWorker()
    {
        for (int i = 0; i < kIncIt; i++) Interlocked.Increment(ref g_inc);
    }

    static void AddWorker()
    {
        for (int i = 0; i < kAddIt; i++) Interlocked.Add(ref g_add, 2);
    }

    static void LockWorker()
    {
        for (int i = 0; i < kLockIt; i++)
        {
            lock (gate) { g_lck++; }
        }
    }

    static void ReentWorker()
    {
        for (int i = 0; i < kReentIt; i++)
        {
            lock (gate)
            {
                lock (gate) { g_reent++; }
            }
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

    static void PoolIncWorker(object state)
    {
        for (int i = 0; i < 100; i++) Interlocked.Increment(ref g_poolInc);
        Interlocked.Increment(ref g_poolDone);
    }

    static void PoolLockWorker(object state)
    {
        for (int i = 0; i < 50; i++)
        {
            lock (gate) { g_poolLck++; }
        }
        Interlocked.Increment(ref g_poolDone);
    }

    static void PoolExchWorker(object state)
    {
        Interlocked.Exchange(ref g_last, (int)state);
        Interlocked.Increment(ref g_poolDone);
    }

    // ============ main ============
    static void Main()
    {
        // ---- 1. Interlocked threaded accumulation (int) ----
        RunN(new ThreadStart(IncWorker));
        int inc = g_inc;
        Console.WriteLine("il_inc=" + (inc == kIncThr * kIncIt));

        // ---- 2. Interlocked threaded accumulation (Add, negative-safe) ----
        RunN(new ThreadStart(AddWorker));
        int add = g_add;
        Console.WriteLine("il_add=" + (add == kAddThr * kAddIt * 2));

        // ---- 3. Interlocked long value-semantics deep matrix (locals only:
        // static long fields are emitted as int32_t by StaticFieldSemantics --
        // recorded defect C21-conccases-2 -- so threaded long atomics need an
        // int64 static slot the M-stage model does not have) ----
        long la = 0;
        Interlocked.Increment(ref la);                  // 1
        Interlocked.Add(ref la, 1000000L);              // 1000001
        long oldA = Interlocked.Exchange(ref la, 42);   // OLD 1000001
        long oldB = Interlocked.CompareExchange(ref la, 7, 42);    // match -> 7
        long oldC = Interlocked.CompareExchange(ref la, 99, 1);    // no match
        Console.WriteLine("il_long_matrix=" + (la == 7 && oldA == 1000001 && oldB == 42 && oldC == 7));

        // ---- 4. lock across threads (Monitor side-table) ----
        RunN(new ThreadStart(LockWorker));
        int lck = g_lck;
        Console.WriteLine("lock_count=" + (lck == kLockThr * kLockIt));

        // ---- 5. Monitor reentrancy from threads (nested lock) ----
        RunN(new ThreadStart(ReentWorker));
        int reent = g_reent;
        Console.WriteLine("monitor_reent=" + (reent == kReentThr * kReentIt));

        // ---- 6. Monitor.TryEnter contention (1-arg form) ----
        g_holderHeld = 0; g_release = 0;
        Thread holder = new Thread(HolderWorker);
        holder.Start();
        for (int i = 0; i < 5000 && g_holderHeld == 0; i++) Thread.Sleep(1);
        bool contended = Monitor.TryEnter(gate);
        Console.WriteLine("tryenter_contended=" + (contended == false));
        g_release = 1;
        holder.Join();
        bool free = Monitor.TryEnter(gate);
        Console.WriteLine("tryenter_free=" + (free == true));
        if (free) Monitor.Exit(gate);

        // ---- 7. Monitor.Wait / Pulse ----
        g_waiterReady = 0; g_waitResult = -1;
        Thread waiter = new Thread(WaiterWorker);
        waiter.Start();
        for (int i = 0; i < 5000 && g_waiterReady == 0; i++) Thread.Sleep(1);
        lock (gate) { Monitor.Pulse(gate); }
        waiter.Join();
        Console.WriteLine("wait_pulsed=" + (g_waitResult == 1));

        // ---- 8. Monitor.Wait timeout (no Pulse) ----
        g_timeoutResult = -1;
        Thread to = new Thread(TimeoutWorker);
        to.Start();
        to.Join();
        Console.WriteLine("wait_timeout=" + (g_timeoutResult == 0));

        // ---- 9. ThreadPool interop: Interlocked + lock workers ----
        g_poolInc = 0; g_poolLck = 0; g_poolDone = 0;
        for (int i = 0; i < 10; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PoolIncWorker));
        for (int i = 0; i < 8; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PoolLockWorker));
        bool poolRan = false;
        for (int i = 0; i < 2000; i++)
        {
            int done = g_poolDone;
            if (done >= 18) { poolRan = true; break; }
            Thread.Sleep(10);
        }
        Console.WriteLine("pool_all_ran=" + poolRan);
        int poolInc = g_poolInc;
        int poolLck = g_poolLck;
        Console.WriteLine("pool_il_count=" + (poolInc == 10 * 100));
        Console.WriteLine("pool_lock_count=" + (poolLck == 8 * 50));

        // ---- 10. ThreadPool interop: Interlocked.Exchange last-writer ----
        g_poolDone = 0; g_last = -1;
        for (int i = 0; i < 6; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PoolExchWorker), 100 + i);
        bool exchRan = false;
        for (int i = 0; i < 2000; i++)
        {
            int done = g_poolDone;
            if (done >= 6) { exchRan = true; break; }
            Thread.Sleep(10);
        }
        int last = g_last;
        Console.WriteLine("pool_exch_ran=" + exchRan);
        Console.WriteLine("pool_exch_in_range=" + (last >= 100 && last <= 105));

        // ---- 11. Interlocked single-thread value-semantics deep pins ----
        int w = int.MaxValue;
        Interlocked.Increment(ref w);            // wrap: INT32_MAX + 1 -> INT32_MIN
        Console.WriteLine("il_wrap=" + (w == int.MinValue));
        int v = 42;
        int old = Interlocked.Exchange(ref v, 7);
        Console.WriteLine("il_exchange_old=" + (old == 42 && v == 7));
        int ce = 100;
        int r1 = Interlocked.CompareExchange(ref ce, 200, 100);  // match -> writes
        int r2 = Interlocked.CompareExchange(ref ce, 300, 100);  // no match
        Console.WriteLine("il_ce_match=" + (r1 == 100 && ce == 200));
        Console.WriteLine("il_ce_nomatch=" + (r2 == 200 && ce == 200));
        long lw = long.MaxValue;
        Interlocked.Add(ref lw, 1);              // long wrap
        Console.WriteLine("il_long_wrap=" + (lw == long.MinValue));
        Interlocked.SpeculationBarrier();        // wired (ICALL-C); must not crash
        Console.WriteLine("il_barrier_ok=" + true);
    }
}
