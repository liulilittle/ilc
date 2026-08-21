//------------------------------------------------------------------------------
// threadsync3.cs -- System.Threading DEEP 3 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C75, AFTER the monitor domain full
// chain (monitor.cpp / monitor.h -- the side-table monitor: enter/exit/
// try_enter/pulse/pulseall/wait). EXTENDS monitordeep (C64: nesting 8,
// recursion 12, single-waiter wait/pulse faces) and threadsync2 (C44) with the
// TWO faces this batch names:
//
//   1) WAIT TIMEOUT FACE (net48-exact on the wired Wait(object,int)):
//      a) Wait(gate, 0) -- a SINGLE non-blocking poll with no signal ->
//         returns false (the lock is released + immediately reacquired).
//      b) Wait(gate, 100) -- a positive bounded wait with no Pulse -> false.
//      c) A Pulse / PulseAll fired with NO waiter is a NO-OP (never queued):
//         the pulse is lost, and a SUBSEQUENT Wait(gate, 0) still returns
//         false (no spurious wake).
//      d) Wait(gate, -1) -- Timeout.Infinite, woken by a single Pulse ->
//         returns true (a worker thread + the airtight-ready handshake).
//      e) Wait(gate, -2) -- a timeout < -1 throws ArgumentOutOfRangeException
//         (and the monitor is left owned + released normally afterwards).
//      f) Wait(unowned, -2) -> ArgumentOutOfRangeException BEFORE the
//         ownership check (net48 validation order -- the runtime matches:
//         Wait(unowned, -2) -> AORE, Wait(unowned, 0) -> SyncLock).
//      g) Wait(unowned, 0) / Wait(unowned, 100) -> SynchronizationLockException.
//   2) PULSEALL MULTI-WAITER (byte-exact -- net48 PulseAll wakes ALL waiters,
//      the runtime's SIMPLIFIED pulse also wakes ALL): THREE workers blocked
//      in Wait(gate, 5000) are all woken by ONE PulseAll(gate); every worker
//      reacquires the monitor and reports its wake; the monitor ends FREE
//      (a later TryEnter succeeds). The multi-waiter handshake is AIRTIGHT:
//      the main thread spins on a ready counter, then acquires the gate --
//      the very acquisition PROVES every worker has released the lock (i.e.
//      is registered inside Wait), so the PulseAll cannot miss a waiter on
//      either runtime.
//   3) RECURSION RESTORATION across an INFINITE Wait: a waiter that holds the
//      gate at depth 2, calls Wait(gate, -1), is woken by a Pulse, and finds
//      the depth RESTORED (two Exits release it fully) -- same contract as
//      monitordeep 3d but on the infinite form.
//
// DELIBERATELY NOT PROBED (recorded defects -- see GAPS.md):
//   * D-MON-1: a SINGLE Pulse with TWO+ waiters DIVERGES (net48 wakes one
//     FIFO waiter, the runtime broadcast wakes N). ONLY PulseAll (wake-all
//     on both runtimes) is probed with multiple waiters.
//   * D-MON-2: TryEnter(object, int) / Wait(object) / Wait(object, TimeSpan)
//     / Wait(object, int, bool) stay LOUD at transpile (MethodMap.Monitor.cs)
//     and are not used here.
//
// Determinism contract (the threadsync2/monitordeep model): workers NEVER
// print and NEVER allocate -- only lock / Interlocked.Increment / static-field
// writes / Monitor.Wait; every observable value is read AFTER Thread.Join (or
// after the bounded ready poll); final state is scheduling-independent.
// Every line prints "label=True/False".
//------------------------------------------------------------------------------
using System;
using System.Threading;

class ThreadSync3
{
    static readonly object gate = new object();
    static readonly object gateB = new object();

    // ---- worker state (allocation-free, single-writer per flag) ----
    static int g_ready;          // waiters that have entered Monitor.Wait
    static int g_woke;           // multi-waiter PulseAll: total wakes (>=1 writers)
    static int g_timeout0;       // Wait(gate,0) no signal -> 1 = returned false
    static int g_inf;            // infinite waiter woken by Pulse -> 1 = true
    static int g_infReady;       // infinite waiter is inside Wait(gate,-1)
    static int g_recRestored;    // depth-2 infinite waiter: depth restored -> 1
    static int g_timeout100;     // Wait(gate,100) no signal -> 1 = returned false

    static void Wait0Worker()
    {
        lock (gate)
        {
            Interlocked.Increment(ref g_ready);
            bool w = Monitor.Wait(gate, 0);   // single poll, no pulse -> false
            if (!w) g_timeout0 = 1;
        }
    }

    static void Timeout100Worker()
    {
        lock (gate)
        {
            bool w = Monitor.Wait(gate, 100); // bounded wait, no pulse -> false
            if (!w) g_timeout100 = 1;
        }
    }

    static void MultiWaiter()
    {
        lock (gate)
        {
            Interlocked.Increment(ref g_ready);
            bool w = Monitor.Wait(gate, 5000);
            if (w) Interlocked.Increment(ref g_woke);
        }
    }

    static void InfWaiter()
    {
        lock (gate)
        {
            Interlocked.Increment(ref g_infReady);
            bool w = Monitor.Wait(gate, -1);  // Timeout.Infinite, pulsed -> true
            if (w) g_inf = 1;
        }
    }

    static void RecRestoreWaiter()
    {
        lock (gate)
        {
            lock (gate)                        // recursion depth 2
            {
                Interlocked.Increment(ref g_infReady);
                bool w = Monitor.Wait(gate, -1);
                if (w) g_recRestored = 1;      // depth must be RESTORED on return
            }                                  // first Exit -> depth 1
        }                                      // second Exit -> released
    }

    static void Main()
    {
        // ==================== 1. WAIT TIMEOUT FACE ====================
        // 1a. Wait(gate, 0): single non-blocking poll -> false with no signal.
        g_ready = 0; g_timeout0 = 0;
        Thread t0 = new Thread(Wait0Worker);
        t0.Start();
        t0.Join();
        Console.WriteLine("wait0_false=" + (g_timeout0 == 1));

        // 1b. Wait(gate, 100): bounded wait with no Pulse -> false.
        g_timeout100 = 0;
        Thread t2 = new Thread(Timeout100Worker);
        t2.Start();
        t2.Join();
        Console.WriteLine("wait100_false=" + (g_timeout100 == 1));

        // 1c. Pulse / PulseAll with NO waiter: a no-op, never queued.
        bool pulseNoop = true;
        try { lock (gate) { Monitor.Pulse(gate); } } catch (Exception) { pulseNoop = false; }
        Console.WriteLine("pulse_nowaiter_noop=" + pulseNoop);
        bool pulseAllNoop = true;
        try { lock (gate) { Monitor.PulseAll(gate); } } catch (Exception) { pulseAllNoop = false; }
        Console.WriteLine("pulseall_nowaiter_noop=" + pulseAllNoop);
        // The pulses above were LOST: a subsequent Wait(gate, 0) still returns
        // false (no spurious wake from a signal that found no waiter).
        g_ready = 0; g_timeout0 = 0;
        Thread t3 = new Thread(Wait0Worker);
        t3.Start();
        t3.Join();
        Console.WriteLine("lost_pulse_wait0_false=" + (g_timeout0 == 1));

        // 1d. Wait(gate, -1) infinite, woken by a single Pulse -> true.
        g_infReady = 0; g_inf = 0;
        Thread ti = new Thread(InfWaiter);
        ti.Start();
        for (int i = 0; i < 5000 && g_infReady == 0; i++) Thread.Sleep(1);
        lock (gate) { Monitor.Pulse(gate); }   // acquiring gate proves the waiter is inside Wait
        ti.Join();
        Console.WriteLine("inf_wait_pulsed=" + (g_inf == 1));

        // 1e. Wait(gate, -2) -> ArgumentOutOfRangeException; monitor stays owned
        //     and is released normally by the lock exit afterwards.
        bool aore1 = false;
        lock (gate)
        {
            try { Monitor.Wait(gate, -2); } catch (ArgumentOutOfRangeException) { aore1 = true; }
        }
        Console.WriteLine("wait_neg2_thrown=" + aore1);
        bool stillHeld = Monitor.TryEnter(gate);
        Console.WriteLine("wait_neg2_monitor_ok=" + stillHeld);
        if (stillHeld) Monitor.Exit(gate);

        // 1f/1g. Unowned-monitor ordering (net48-verified, D-MON-3 typed catch).
        bool aore2 = false;
        try { Monitor.Wait(gateB, -2); } catch (ArgumentOutOfRangeException) { aore2 = true; }
        Console.WriteLine("wait_unowned_neg2_aore=" + aore2);
        bool sl1 = false;
        try { Monitor.Wait(gateB, 0); } catch (SynchronizationLockException) { sl1 = true; }
        Console.WriteLine("wait_unowned0_synclock=" + sl1);
        bool sl2 = false;
        try { Monitor.Wait(gateB, 100); } catch (SynchronizationLockException) { sl2 = true; }
        Console.WriteLine("wait_unowned100_synclock=" + sl2);

        // ==================== 2. PULSEALL MULTI-WAITER ====================
        // Three workers in Wait(gate, 5000); ONE PulseAll wakes ALL of them.
        // The main thread only locks AFTER the ready counter hits 3, so the
        // acquisition PROVES every worker has released the gate (registered
        // inside Wait) -- the PulseAll cannot be missed on either runtime.
        g_ready = 0; g_woke = 0;
        Thread wa = new Thread(MultiWaiter);
        Thread wb = new Thread(MultiWaiter);
        Thread wc = new Thread(MultiWaiter);
        wa.Start(); wb.Start(); wc.Start();
        for (int i = 0; i < 5000 && g_ready < 3; i++) Thread.Sleep(1);
        lock (gate) { Monitor.PulseAll(gate); }
        wa.Join(); wb.Join(); wc.Join();
        Console.WriteLine("pulseall3_woke=" + (g_woke == 3));
        bool paFree = Monitor.TryEnter(gate);
        Console.WriteLine("pulseall3_free=" + paFree);
        if (paFree) Monitor.Exit(gate);

        // ==================== 3. RECURSION RESTORATION (infinite Wait) ======
        g_infReady = 0; g_recRestored = 0;
        Thread tr = new Thread(RecRestoreWaiter);
        tr.Start();
        for (int i = 0; i < 5000 && g_infReady == 0; i++) Thread.Sleep(1);
        lock (gate) { Monitor.Pulse(gate); }
        tr.Join();
        Console.WriteLine("rec_restored=" + (g_recRestored == 1));
        bool recFree = Monitor.TryEnter(gate);
        Console.WriteLine("rec_restored_free=" + recFree);
        if (recFree) Monitor.Exit(gate);
    }
}
