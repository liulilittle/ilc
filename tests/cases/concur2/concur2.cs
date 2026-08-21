// concur2.cs -- CONCURRENCY deep probe 2 (e2e batch C22, "并发深测 2: 多线程
// 锁/帧完整性/ThreadPool+锁 -- framestack 修复后"). conccases (C21) pinned
// the pre-fix baseline with allocation-free workers whose ANY ref-typed local/
// param (a `lock(gate)` gate local, an object pool state) corrupted the then-
// SHARED frame chain. Commit 9fa9ba5 made g_frameHead thread_local, so THIS
// case deliberately pushes ref-typed frames in workers -- gate objects
// captured into locals, object state params, deep recursion -- and verifies
// PER-THREAD frame integrity by exact totals and per-thread local sums read
// only AFTER Thread.Join (happens-before) or a bounded poll on a monotonic
// completion counter.
//
// Determinism contract (unchanged from conccases): workers NEVER print and
// NEVER allocate (the M-stage managed allocator is single-threaded by
// contract -- il2cpp_allocator.h:60-61 -- so workers only Interlocked / lock
// / Thread.Sleep / static-field reads, all allocation-free). Every observable
// is read after Join or a bounded poll, so the final state is
// scheduling-independent.
//
// Coverage (what conccases could NOT run before the framestack fix):
//   * THREE DISTINCT GATES (A/B/C): 8 threads x 500 per gate -> each gate's
//     counter exact (gate isolation; lock side-table concurrency).
//   * NESTED locks with a CONSISTENT global order (A then B on every worker)
//     -- the lock-order graph has no cycle (D11), so no deadlock; 8x500 ->
//     4000.
//   * MULTI-LOCAL worker: three gate objects captured into three locals, two
//     SEQUENTIAL locks per iteration -- but each lock owns its OWN counter
//     (g_mlA only under gateA, g_mlB only under gateB): a counter incremented
//     under two DIFFERENT gates would be a genuine data race (overlapping
//     critical sections on different locks -- the net48 oracle itself loses
//     updates), so the per-lock-counter invariant keeps every total exact.
//     8 threads x 500 -> both counters 4000 -- concurrent frames hold three
//     live refs on the thread-local chain.
//   * PER-THREAD LOCAL STATE: 8 threads each accumulate an int local across
//     1000 lock-protected increments; each writes its OWN fixed slot (8 named
//     wrapper methods -- no Thread[]/int[]; newarr of a BCL type is
//     LOUD-rejected). All 8 must equal the same expected value; a shared/
//     corrupted frame context would clobber another thread's accumulator.
//   * DEEP RECURSION in workers: Rec(depth, tag) locks the tag at EVERY level
//     (a frame per level); 12-deep and 2-deep workers run CONCURRENTLY (4
//     each) -- exact step totals (4*13 + 4*3 = 64) prove every level unwinds
//     on the right thread with no cross-thread frame leakage.
//   * ThreadPool + lock + object state: 6 WaitCallbacks with boxed ids lock
//     one gate 200x each -> the weighted sum is exact (the (int)state unbox
//     is a ref-typed param -- a frame -- on pool threads, previously the
//     exact crash class).
//   * Monitor.Wait / Pulse from a ThreadPool worker: queue -> poll ready ->
//     Pulse from main -> result read after a Join-style poll on a completion
//     counter.
//
// Expected output: all-bool rows over deterministic final values.
using System;
using System.Threading;

class Concur2
{
    // ---- three distinct lock gates (allocated once on the main thread) ----
    static readonly object gateA = new object();
    static readonly object gateB = new object();
    static readonly object gateC = new object();

    // ---- gate counters (read after Join) ----
    static int g_cntA;
    static int g_cntB;
    static int g_cntC;
    static int g_nested;
    static int g_mlA;       // incremented ONLY under gateA
    static int g_mlB;       // incremented ONLY under gateB
    static int g_mlDone;

    // ---- per-thread local-state slots (fixed locals, see header) ----
    static int s0, s1, s2, s3, s4, s5, s6, s7;

    // ---- deep-recursion counters ----
    static int g_recSteps;
    static int g_deepDone;
    static int g_shallowDone;

    // ---- thread pool ----
    static int g_poolSum;
    static int g_poolDone;
    static int g_pwReady;
    static int g_pwResult;
    static int g_pwDone;

    const int kIt = 500;        // gate / nested / multi-local iterations
    const int kSumIt = 1000;    // local-sum iterations
    const int kLocalExpect = kSumIt * (kSumIt - 1) / 2 + kSumIt; // 500500
    const int kPoolIt = 200;    // pool worker lock iterations

    // Run 8 instances of ONE worker concurrently and join all (happens-before
    // makes final shared-state reads exact).
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

    // Each worker captures its gate into a LOCAL (a ref-typed local pushes a
    // managed frame) and locks through the local -- the pre-fix crash class.
    static void LockWorkerA() { object g = gateA; for (int i = 0; i < kIt; i++) lock (g) { g_cntA++; } }
    static void LockWorkerB() { object g = gateB; for (int i = 0; i < kIt; i++) lock (g) { g_cntB++; } }
    static void LockWorkerC() { object g = gateC; for (int i = 0; i < kIt; i++) lock (g) { g_cntC++; } }

    // Nested locks, globally consistent order A -> B (deadlock-free by D11).
    static void NestedWorker()
    {
        object a = gateA; object b = gateB;
        for (int i = 0; i < kIt; i++) { lock (a) { lock (b) { g_nested++; } } }
    }

    // Three live ref locals + two sequential locks per iteration; each lock
    // owns its own counter (per-lock-counter invariant -- see the header).
    static void MultiLocalWorker()
    {
        object a = gateA; object b = gateB; object c = gateC;
        for (int i = 0; i < kIt; i++)
        {
            lock (a) { g_mlA++; }
            lock (b) { g_mlB++; }
        }
        Interlocked.Increment(ref g_mlDone);
    }

    // Per-thread local accumulation; each thread returns its own value which
    // the named wrapper stores to its own slot.
    static int LocalSumBody()
    {
        int acc = 0;
        object g = gateA;
        for (int i = 0; i < kSumIt; i++) { acc += i; lock (g) { acc++; } }
        return acc;
    }
    static void SumW0() { s0 = LocalSumBody(); }
    static void SumW1() { s1 = LocalSumBody(); }
    static void SumW2() { s2 = LocalSumBody(); }
    static void SumW3() { s3 = LocalSumBody(); }
    static void SumW4() { s4 = LocalSumBody(); }
    static void SumW5() { s5 = LocalSumBody(); }
    static void SumW6() { s6 = LocalSumBody(); }
    static void SumW7() { s7 = LocalSumBody(); }

    // Recursion with an object param (a frame per level); locks at EVERY
    // level, unwinds on the same thread (thread-local chain).
    static void Rec(int d, object tag)
    {
        object g = tag;
        lock (g) { g_recSteps++; }
        if (d > 0) Rec(d - 1, g);
    }
    static void DeepWorker() { Rec(12, gateC); Interlocked.Increment(ref g_deepDone); }
    static void ShallowWorker() { Rec(2, gateC); Interlocked.Increment(ref g_shallowDone); }

    // 4 deep + 4 shallow workers interleaved.
    static void RunRecMix()
    {
        Thread t0 = new Thread(DeepWorker); t0.Start();
        Thread t1 = new Thread(ShallowWorker); t1.Start();
        Thread t2 = new Thread(DeepWorker); t2.Start();
        Thread t3 = new Thread(ShallowWorker); t3.Start();
        Thread t4 = new Thread(DeepWorker); t4.Start();
        Thread t5 = new Thread(ShallowWorker); t5.Start();
        Thread t6 = new Thread(DeepWorker); t6.Start();
        Thread t7 = new Thread(ShallowWorker); t7.Start();
        t0.Join(); t1.Join(); t2.Join(); t3.Join();
        t4.Join(); t5.Join(); t6.Join(); t7.Join();
    }

    // ThreadPool + lock + object state (boxed id; unbox on a pool thread).
    static void PoolLockWorker(object state)
    {
        int id = (int)state;
        object g = gateB;
        for (int i = 0; i < kPoolIt; i++) lock (g) { g_poolSum += id; }
        Interlocked.Increment(ref g_poolDone);
    }

    // Monitor.Wait / Pulse from a ThreadPool worker.
    static void PoolWaiter(object state)
    {
        lock (gateA)
        {
            g_pwReady = 1;
            bool ok = Monitor.Wait(gateA, 5000);
            g_pwResult = ok ? 1 : 0;
        }
        Interlocked.Increment(ref g_pwDone);
    }

    // ============ main ============
    static void Main()
    {
        // ---- 1. three distinct gates, 8 threads x 500 each ----
        RunN(new ThreadStart(LockWorkerA));
        RunN(new ThreadStart(LockWorkerB));
        RunN(new ThreadStart(LockWorkerC));
        Console.WriteLine("lock_a=" + (g_cntA == 8 * kIt));
        Console.WriteLine("lock_b=" + (g_cntB == 8 * kIt));
        Console.WriteLine("lock_c=" + (g_cntC == 8 * kIt));

        // ---- 2. nested locks, consistent A->B order ----
        RunN(new ThreadStart(NestedWorker));
        Console.WriteLine("nested_lock=" + (g_nested == 8 * kIt));

        // ---- 3. multi-local worker (3 live ref locals, 2 locks/iter) ----
        RunN(new ThreadStart(MultiLocalWorker));
        Console.WriteLine("ml_sum=" + (g_mlA == 8 * kIt && g_mlB == 8 * kIt));
        Console.WriteLine("ml_done=" + (g_mlDone == 8));

        // ---- 4. per-thread local state isolation (frame integrity) ----
        Thread ta = new Thread(SumW0); ta.Start();
        Thread tb = new Thread(SumW1); tb.Start();
        Thread tc = new Thread(SumW2); tc.Start();
        Thread td = new Thread(SumW3); td.Start();
        Thread te = new Thread(SumW4); te.Start();
        Thread tf = new Thread(SumW5); tf.Start();
        Thread tg = new Thread(SumW6); tg.Start();
        Thread th = new Thread(SumW7); th.Start();
        ta.Join(); tb.Join(); tc.Join(); td.Join();
        te.Join(); tf.Join(); tg.Join(); th.Join();
        Console.WriteLine("local_sum_all=" + (s0 == kLocalExpect && s1 == kLocalExpect
            && s2 == kLocalExpect && s3 == kLocalExpect && s4 == kLocalExpect
            && s5 == kLocalExpect && s6 == kLocalExpect && s7 == kLocalExpect));

        // ---- 5. deep recursion (12-deep vs 2-deep, concurrent) ----
        RunRecMix();
        Console.WriteLine("rec_steps=" + (g_recSteps == 4 * 13 + 4 * 3));
        Console.WriteLine("rec_deep_done=" + (g_deepDone == 4));
        Console.WriteLine("rec_shallow_done=" + (g_shallowDone == 4));

        // ---- 6. ThreadPool + lock + object state (weighted sum) ----
        g_poolSum = 0; g_poolDone = 0;
        for (int i = 0; i < 6; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PoolLockWorker), (object)(100 + i));
        bool poolRan = false;
        for (int i = 0; i < 2000; i++)
        {
            int done = g_poolDone;
            if (done >= 6) { poolRan = true; break; }
            Thread.Sleep(10);
        }
        // ids 100..105 x 200 = (100+101+102+103+104+105)*200 = 615*200 = 123000
        Console.WriteLine("pool_all_ran=" + poolRan);
        Console.WriteLine("pool_frame_sum=" + (g_poolSum == 123000));

        // ---- 7. Monitor.Wait / Pulse from a ThreadPool worker ----
        g_pwReady = 0; g_pwResult = -1; g_pwDone = 0;
        ThreadPool.QueueUserWorkItem(new WaitCallback(PoolWaiter));
        bool pwReady = false;
        for (int i = 0; i < 5000 && g_pwReady == 0; i++) Thread.Sleep(1);
        pwReady = g_pwReady == 1;
        if (pwReady) { lock (gateA) { Monitor.Pulse(gateA); } }
        bool pwDone = false;
        for (int i = 0; i < 5000 && g_pwDone == 0; i++) Thread.Sleep(1);
        pwDone = g_pwDone == 1;
        Console.WriteLine("pool_wait_pulsed=" + (pwDone && g_pwResult == 1));
    }
}
