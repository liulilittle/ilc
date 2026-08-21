//------------------------------------------------------------------------------
// threadext2.cs -- System.Threading DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C39. EXTENDS the thread surface already
// proven by threadops/threadops2/threadcase2/conccases (MethodMap.Thread.cs +
// MethodMap.ThreadPool.cs + MethodMap.Monitor.cs -- threadobj.cpp / threadpool
// .cpp / monitor.cpp) with the CONCURRENCY-FACE matrix:
//
//   1) MONITOR RACE -- the 1-arg TryEnter(obj) form under real contention
//      (conccases only probed a depth-1 holder): a HolderWorker owns the gate
//      at RECURSION DEPTH 2 (nested lock); the main thread's TryEnter(gate)
//      must fail, and succeed only after the holder releases. Deterministic
//      via the holderHeld/release flag handshake (bounded poll). The net48
//      2-arg TryEnter(obj, ref bool) form is wired since D-THR3-2 (its probe
//      is section 8: the transpiler now emits the flag with the same
//      reinterpret_cast<bool*> the Enter path gets).
//   2) JOIN SEMANTICS -- Join() on a live worker returns only after the
//      worker's bounded sleep work is complete (workDone==1 after Join) and
//      IsAlive reads false post-Join. The Join(int timeout) overload is
//      wired since D-THR3-1 (section 7: Join(10000) -> true, Join(1) on a
//      long worker -> false).
//   3) THREAD POOL INTERLEAVING -- the MAIN thread and 8 pool workers share a
//      lock-protected counter (main does 500 lock-protected increments while
//      the workers run their kPoolIters each) -> the final total is EXACT
//      (8*200+500), read after a bounded poll on the monotonic done counter.
//   4) POOL REENTRANCY -- pool workers doing nested lock(gate){lock(gate){ }}
//      (recursion depth 2 from pool threads) -> exact 6*100 count.
//   5) POOL RACING TryEnter -- 8 pool workers each TryEnter the FREE gate and
//      release immediately (single-writer claim counters); the assertions are
//      convergence-style: every worker finished, at least one claimed, and
//      after all workers the gate is FREE again (a deterministically verifiable
//      post-race invariant -- net48 truth: >=1 claim, gate free after).
//   6) PulseAll WAKE-ALL -- TWO waiters both blocked in Monitor.Wait(gate,
//      5000); main PulseAll(gate) wakes BOTH (the runtime's simplified pulse
//      wakes all waiters, matching net48 PulseAll). The wReady handshake makes
//      the wake deterministic: main cannot acquire the gate (both waiters
//      hold it until Wait) until both are inside Wait, so the PulseAll fires
//      strictly after both Wait calls.
//
// Determinism contract (the threadcase2/conccases model): workers NEVER print
// and NEVER allocate (the M-stage managed allocator is SINGLE-THREADED) --
// worker callbacks only lock / Monitor / Interlocked / Thread.Sleep. Every
// observable value is read AFTER Thread.Join or after a bounded poll
// (Sleep(10) x 2000) gated on a monotonic completion counter, so the final
// state is scheduling-independent.
//
// Every line prints "label=True/False". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Threading;

class ThreadExt2
{
    // ---- shared gate + Monitor-race state ----
    static readonly object gate = new object();
    static int g_holderHeld;   // HolderWorker owns the gate at depth 2
    static int g_release;      // main asks the holder to leave

    // ---- Join state ----
    static int g_workDone;     // TimedWorker's completed work marker
    static int g_longDone;     // LongWorker's completion marker (reap gate)

    // ---- pool interleave state (single-writer counters) ----
    static int g_poolCnt;      // lock-protected shared counter (main + workers)
    static int g_poolReent;    // nested-lock pool counter
    static int g_poolClaim;    // successful racing TryEnter claims (workers)
    static int g_poolDone;     // monotonic pool completion counter

    // ---- PulseAll waiter state ----
    static int g_w1, g_w2;     // per-waiter wake result (1=pulsed)
    static int g_wReady;       // waiters inside Monitor.Wait

    const int kPoolWorkers = 8;
    const int kPoolIters = 200;
    const int kReentIters = 100;

    // ============ workers (allocation-free, never print) ============
    static void TimedWorker()
    {
        for (int i = 0; i < 10; i++) Thread.Sleep(2);
        g_workDone = 1;
    }

    static void LongWorker()
    {
        Thread.Sleep(300);   // long enough that a 1ms Join must time out
        g_longDone = 1;
    }

    static void HolderWorker()
    {
        lock (gate)
        {
            lock (gate)          // recursion depth 2
            {
                g_holderHeld = 1;
                while (g_release == 0) Thread.Sleep(1);
            }
        }
    }

    static void PoolLockWorker(object state)
    {
        for (int i = 0; i < kPoolIters; i++)
        {
            lock (gate) { g_poolCnt++; }
        }
        Interlocked.Increment(ref g_poolDone);
    }

    static void PoolReentWorker(object state)
    {
        for (int i = 0; i < kReentIters; i++)
        {
            lock (gate)
            {
                lock (gate) { g_poolReent++; }
            }
        }
        Interlocked.Increment(ref g_poolDone);
    }

    static void PoolTryWorker(object state)
    {
        bool taken = Monitor.TryEnter(gate);
        if (taken)
        {
            Interlocked.Increment(ref g_poolClaim);
            Monitor.Exit(gate);
        }
        Interlocked.Increment(ref g_poolDone);
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

    static void Waiter2()
    {
        lock (gate)
        {
            Interlocked.Increment(ref g_wReady);
            bool w = Monitor.Wait(gate, 5000);
            if (w) g_w2 = 1;
        }
    }

    // ============ main ============
    static void Main()
    {
        // ---- 1. Monitor race: 1-arg TryEnter under a depth-2 holder ----
        // (The 2-arg TryEnter(obj, ref bool lockTaken) form was BROKEN at
        // HEAD -- the transpiler emitted the flag WITHOUT the
        // reinterpret_cast<bool*> the Enter path gets, so the generated call
        // passed int32_t* and failed to compile. Fixed by D-THR3-2; its probe
        // is section 8.)
        g_holderHeld = 0; g_release = 0;
        Thread holder = new Thread(HolderWorker);
        holder.Start();
        for (int i = 0; i < 5000 && g_holderHeld == 0; i++) Thread.Sleep(1);
        bool takenA = Monitor.TryEnter(gate);   // contended: must fail
        Console.WriteLine("race_contended=" + (takenA == false));
        g_release = 1;
        holder.Join();
        bool takenB = Monitor.TryEnter(gate);   // free now: must acquire
        Console.WriteLine("race_free=" + takenB);
        if (takenB) Monitor.Exit(gate);

        // ---- 2. Join: returns only after the worker's work is done ----
        g_workDone = 0;
        Thread t = new Thread(TimedWorker);
        t.Start();
        t.Join();
        Console.WriteLine("join_after_work=" + (g_workDone == 1));
        Console.WriteLine("join_notalive=" + (!t.IsAlive));

        // ---- 3. pool interleave: main + 8 workers share the counter ----
        g_poolCnt = 0; g_poolDone = 0;
        for (int i = 0; i < kPoolWorkers; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PoolLockWorker));
        for (int i = 0; i < 500; i++) { lock (gate) { g_poolCnt++; } }
        bool interleaveRan = false;
        for (int i = 0; i < 2000; i++)
        {
            if (g_poolDone >= kPoolWorkers) { interleaveRan = true; break; }
            Thread.Sleep(10);
        }
        Console.WriteLine("interleave_all_ran=" + interleaveRan);
        int poolCnt = g_poolCnt;
        Console.WriteLine("interleave_count=" + (poolCnt == kPoolWorkers * kPoolIters + 500));

        // ---- 4. pool reentrancy (nested lock from pool threads) ----
        g_poolReent = 0; g_poolDone = 0;
        for (int i = 0; i < 6; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PoolReentWorker));
        bool reentRan = false;
        for (int i = 0; i < 2000; i++)
        {
            if (g_poolDone >= 6) { reentRan = true; break; }
            Thread.Sleep(10);
        }
        int reent = g_poolReent;
        Console.WriteLine("reent_ran=" + reentRan);
        Console.WriteLine("reent_count=" + (reent == 6 * kReentIters));

        // ---- 5. pool racing TryEnter: convergence assertions ----
        g_poolClaim = 0; g_poolDone = 0;
        for (int i = 0; i < 8; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PoolTryWorker));
        bool claimRan = false;
        for (int i = 0; i < 2000; i++)
        {
            if (g_poolDone >= 8) { claimRan = true; break; }
            Thread.Sleep(10);
        }
        Console.WriteLine("claim_ran=" + claimRan);
        Console.WriteLine("claim_done=" + (g_poolDone == 8));
        int claim = g_poolClaim;
        Console.WriteLine("claim_one_or_more=" + (claim >= 1));
        bool takenC = Monitor.TryEnter(gate);   // post-race: gate is free again
        Console.WriteLine("claim_gate_free=" + takenC);
        if (takenC) Monitor.Exit(gate);

        // ---- 6. PulseAll wakes BOTH waiters ----
        g_w1 = 0; g_w2 = 0; g_wReady = 0;
        Thread w1 = new Thread(Waiter1); w1.Start();
        Thread w2 = new Thread(Waiter2); w2.Start();
        for (int i = 0; i < 5000 && g_wReady < 2; i++) Thread.Sleep(1);
        lock (gate) { Monitor.PulseAll(gate); }
        w1.Join(); w2.Join();
        Console.WriteLine("pulseall_w1=" + (g_w1 == 1));
        Console.WriteLine("pulseall_w2=" + (g_w2 == 1));

        // ---- 7. Join(int) timeout (D-THR3-1, GAPS.md -- un-parked) ----
        // net48 semantics: Join(10000) returns true when the thread ends
        // within the timeout; Join(1) returns false when the timeout expires
        // while the worker still runs (LongWorker sleeps 300ms). The reaped
        // LongWorker marker poll keeps the process deterministic (no dangling
        // foreground thread at exit).
        g_workDone = 0;
        Thread pt = new Thread(TimedWorker);
        pt.Start();
        bool jok = pt.Join(10000);       // worker finishes well inside
        Console.WriteLine("join_timeout_ok=" + (jok == true));
        Console.WriteLine("join_timeout_after=" + (g_workDone == 1));
        g_longDone = 0;
        Thread lt = new Thread(LongWorker);
        lt.Start();
        bool jtimeout = lt.Join(1); // 1ms: must time out (LongWorker sleeps 300ms)
        Console.WriteLine("join_timeout=" + (jtimeout == false));
        bool longDone = false;
        for (int i = 0; i < 2000 && g_longDone == 0; i++) Thread.Sleep(10);
        if (g_longDone == 1) longDone = true;
        Console.WriteLine("join_long_done=" + longDone);

        // ---- 8. TryEnter(object, ref bool) 2-arg form (D-THR3-2) ----
        // At HEAD the transpiler emitted the flag as a BARE &loc (int32_t*),
        // which matches NO try_enter overload -> C++ build failure. The 2-arg
        // runtime overload il2cpp_monitor_try_enter(obj, bool*) writes
        // *lockTaken = acquired (the ref-bool flag), so the flag must read
        // false under contention and true on the free gate.
        g_holderHeld = 0; g_release = 0;
        Thread holder2 = new Thread(HolderWorker);
        holder2.Start();
        for (int i = 0; i < 5000 && g_holderHeld == 0; i++) Thread.Sleep(1);
        bool takenD = false;
        Monitor.TryEnter(gate, ref takenD);   // contended: flag stays false
        Console.WriteLine("tryenter2_contended=" + (takenD == false));
        g_release = 1;
        holder2.Join();
        bool takenE = false;
        Monitor.TryEnter(gate, ref takenE);   // free: flag set true
        Console.WriteLine("tryenter2_free=" + (takenE == true));
        if (takenE) Monitor.Exit(gate);
    }
}
