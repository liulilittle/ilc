//------------------------------------------------------------------------------
// monitordeep.cs -- System.Threading.Monitor DEEP 2 e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C64, AFTER the monitor domain
// (monitor.cpp / monitor.h -- the side-table monitor: enter/exit/try_enter/
// pulse/pulseall/wait). EXTENDS lockdeep (C23: 6-level nesting, recursion 8,
// exceptions, manual Enter/Exit, state probes, wait timeout, two gates) and
// threadsync2 (C44: nesting 3, wait/pulse single waiter, timeout waiter) with
// the DEEPER nesting / reentrancy / wait faces:
//
//   1) NESTING: 8-level lock nesting on ONE gate (depth counter 1..10^7 =
//      11111111 -- every guard entered, every Exit in reverse order); TWO
//      gates interleaved (lock A { lock B { lock A { } } }) -- reentrant
//      return to A while B is held; and the same nesting across a HELPER
//      CALL BOUNDARY (the guard spans a method call returning from inside).
//   2) REENTRANCY: recursion depth 12 (Rec: same gate every depth), full
//      unwind with an exact counter; reentrant state probe -- Enter twice
//      (depth 2), TryEnter from the OWNING thread returns true (reentrant,
//      depth 3), Exit once, TryEnter again (still depth >= 2), Exit THREE
//      times back to depth 0, a FOURTH Exit throws SynchronizationLock-
//      Exception (caught); a final TryEnter proves the monitor is free.
//   3) WAIT / PULSE family (net48 byte-exact for SINGLE waiters -- the
//      runtime's simplified Pulse wakes ALL waiters, net48 wakes the FIFO
//      one, so multi-waiter Pulse probes are recorded, not probed):
//      a) WAIT TIMEOUT returns false and REACQUIRES: Wait(gate, 150) with
//         no Pulse -> false, the monitor is still owned afterwards, Exit
//         releases; a subsequent TryEnter proves no leak.
//      b) PULSE round-trip: ONE waiter in Wait(gate, 5000) woken by a
//         single Pulse(gate) from the main thread -> returns true; the
//         waiter then re-holds the lock (reacquire), flag set.
//      c) PulseAll round-trip: ONE waiter woken by PulseAll(gate) (the
//         runtime maps PulseAll to the same simplified pulse -- with one
//         waiter byte-exact).
//      d) RECURSION PRESERVATION across Wait: lock twice (depth 2), Wait,
//         Pulse wakes it -> Wait returns true AND the recursion depth is
//         RESTORED (still owned at depth 2 -- an Exit leaves depth 1, a
//         second Exit releases).
//   4) EXCEPTION FACE (wired Monitor overloads):
//      a) Monitor.Exit on an UNOWNED monitor -> SynchronizationLockException.
//      b) Monitor.Pulse on an unowned monitor -> SynchronizationLockException.
//      c) Monitor.Wait on an unowned monitor -> SynchronizationLockException.
//      d) Monitor.Enter(null) / Exit(null) -> ArgumentNullException.
//      e) Monitor.Enter(gate, ref true) -> ArgumentException (lockTaken must
//         be false on entry -- the monitor.cpp CheckLockTaken contract).
//   5) MANUAL Enter(object, ref bool) -- the exact Roslyn lock-lowering
//      primitive: flag=false, Enter sets it, finally if(flag) Exit; a second
//      pair on the OTHER gate so the counter stays exact.
//
// WIRED-SURFACE note (MethodMap.Monitor.cs, recorded in GAPS.md): only the
// int-form overloads are wired -- Enter(object)/Enter(object,ref bool)/Exit/
// TryEnter(object)/TryEnter(object,ref bool)/Pulse/PulseAll/Wait(object,int).
// TryEnter(object, timeout) / Wait(object) / Wait(object, TimeSpan) /
// Wait(object, int, bool) are LOUD NotSupportedException at transpile and are
// therefore NOT probed here.
//
// LEAK DISCIPLINE: every TryEnter result is captured into a bool, printed,
// and released via a matching Exit before the next section (a leaked monitor
// would corrupt the recursion-depth probes downstream).
//
// Wait/Pulse probes use ONE worker thread each; workers are allocation-free
// (static-field writes only, the conccases/threadsync2 model). Every line
// prints "label=True/False"; net48 is the byte-exact baseline.
//------------------------------------------------------------------------------
using System;
using System.Threading;

class MonitorDeep
{
    static int counter;
    static readonly object gateA = new object();
    static readonly object gateB = new object();

    // ---- wait/pulse worker state (allocation-free workers) ----
    static int g_w1;       // single-Pulse waiter: 1 = woken (Wait returned true)
    static int g_wReady;   // waiter is inside Monitor.Wait
    static int g_pa;       // PulseAll waiter: 1 = woken
    static int g_paReady;
    static int g_rec;      // recursion-preservation waiter: 1 = woke at depth 2
    static int g_recReady;

    // Reentrant recursion: locks the SAME gate at every depth.
    static void Rec(int depth)
    {
        lock (gateA)
        {
            counter++;
            if (depth > 0) Rec(depth - 1);
        }
    }

    // 8-level nesting on the SAME gate (depth counter 1,10,100,...,10^7).
    static void Nested8()
    {
        lock (gateA) { counter += 1;
            lock (gateA) { counter += 10;
                lock (gateA) { counter += 100;
                    lock (gateA) { counter += 1000;
                        lock (gateA) { counter += 10000;
                            lock (gateA) { counter += 100000;
                                lock (gateA) { counter += 1000000;
                                    lock (gateA) { counter += 10000000; }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // Nested helper that returns from inside the guards (spans a call).
    static int CallNested(int x)
    {
        lock (gateA)
        {
            lock (gateB) { return x + 1; }
        }
    }

    // ---- allocation-free workers (never print; static writes only) ----
    static void Waiter1()
    {
        lock (gateA)
        {
            Interlocked.Increment(ref g_wReady);
            bool w = Monitor.Wait(gateA, 5000);
            if (w) g_w1 = 1;
        }
    }
    static void PulseAllWaiter()
    {
        lock (gateA)
        {
            Interlocked.Increment(ref g_paReady);
            bool w = Monitor.Wait(gateA, 5000);
            if (w) g_pa = 1;
        }
    }
    static void RecWaiter()
    {
        lock (gateA)
        {
            lock (gateA)          // recursion depth 2
            {
                Interlocked.Increment(ref g_recReady);
                bool w = Monitor.Wait(gateA, 5000);
                if (w) g_rec = 1; // depth must be RESTORED on return
            }                     // first Exit -> depth 1
        }                         // second Exit -> released
    }

    static void Main()
    {
        // ==================== 1. NESTING ====================
        counter = 0;
        Nested8();
        // 1+10+100+1000+10000+100000+1000000+10000000 = 11111111
        Console.WriteLine("nest8_count=" + (counter == 11111111));
        bool n8free = Monitor.TryEnter(gateA);
        Console.WriteLine("nest8_free=" + n8free);
        if (n8free) Monitor.Exit(gateA);

        // Two gates interleaved: reentrant return to A while B is held.
        counter = 0;
        lock (gateA) { counter += 1;
            lock (gateB) { counter += 10;
                lock (gateA) { counter += 100; } } }
        Console.WriteLine("nest_aba=" + (counter == 111));
        lock (gateB) { counter += 1000;
            lock (gateA) { counter += 10000;
                lock (gateB) { counter += 100000; } } }
        Console.WriteLine("nest_bab=" + (counter == 111111));
        bool abFree = Monitor.TryEnter(gateA);
        bool baFree = Monitor.TryEnter(gateB);
        Console.WriteLine("nest_aba_free=" + (abFree && baFree));
        if (abFree) Monitor.Exit(gateA);
        if (baFree) Monitor.Exit(gateB);

        // Nesting across a HELPER CALL boundary (returns from inside guards).
        counter = 0;
        lock (gateA) { counter = CallNested(counter); }
        Console.WriteLine("nest_call=" + (counter == 1));

        // ==================== 2. REENTRANCY ====================
        counter = 0;
        Rec(11);                                            // 12 locks
        Console.WriteLine("rec12_count=" + (counter == 12));
        bool r12free = Monitor.TryEnter(gateA);
        Console.WriteLine("rec12_free=" + r12free);
        if (r12free) Monitor.Exit(gateA);

        // State probe: Enter twice -> depth 2; TryEnter (reentrant) -> depth 3;
        // Exit once -> depth 2; TryEnter again -> depth 3; Exit THREE times
        // back to depth 0; a FOURTH Exit throws SynchronizationLockException.
        bool lockTaken1 = false;
        Monitor.Enter(gateA, ref lockTaken1);               // depth 1
        bool lockTaken2 = false;
        Monitor.Enter(gateA, ref lockTaken2);               // depth 2
        bool re1 = Monitor.TryEnter(gateA);                 // reentrant -> depth 3
        Monitor.Exit(gateA);                                // depth 2
        bool re2 = Monitor.TryEnter(gateA);                 // still owned -> depth 3
        Console.WriteLine("re_enter2=" + (lockTaken1 && lockTaken2));
        Console.WriteLine("re_try_reentrant=" + (re1 && re2));
        Monitor.Exit(gateA);                                // depth 2
        Monitor.Exit(gateA);                                // depth 1
        Monitor.Exit(gateA);                                // depth 0
        bool syncThrow = false;
        try { Monitor.Exit(gateA); } catch (SynchronizationLockException) { syncThrow = true; }
        Console.WriteLine("re_fourth_exit_throws=" + syncThrow);
        bool reFree = Monitor.TryEnter(gateA);
        Console.WriteLine("re_released=" + reFree);
        if (reFree) Monitor.Exit(gateA);

        // ==================== 3. WAIT / PULSE family ====================
        // 3a. Wait timeout returns false and REACQUIRES (no Pulse).
        bool waitOk = true;
        bool waitTimedOut = false;
        try
        {
            lock (gateA)
            {
                waitTimedOut = !Monitor.Wait(gateA, 150);
            }
        }
        catch (Exception) { waitOk = false; }
        Console.WriteLine("wait_timeout_false=" + waitTimedOut);
        Console.WriteLine("wait_no_throw=" + waitOk);
        bool wr = Monitor.TryEnter(gateA);
        Console.WriteLine("wait_reacquired=" + wr);
        if (wr) Monitor.Exit(gateA);

        // 3b. Pulse round-trip: one waiter, one Pulse.
        g_w1 = 0; g_wReady = 0;
        Thread tw = new Thread(Waiter1);
        tw.Start();
        for (int i = 0; i < 5000 && g_wReady == 0; i++) Thread.Sleep(1);
        lock (gateA) { Monitor.Pulse(gateA); }
        tw.Join();
        Console.WriteLine("pulse_one_woken=" + (g_w1 == 1));

        // 3c. PulseAll round-trip: one waiter, one PulseAll.
        g_pa = 0; g_paReady = 0;
        Thread tpa = new Thread(PulseAllWaiter);
        tpa.Start();
        for (int i = 0; i < 5000 && g_paReady == 0; i++) Thread.Sleep(1);
        lock (gateA) { Monitor.PulseAll(gateA); }
        tpa.Join();
        Console.WriteLine("pulseall_one_woken=" + (g_pa == 1));

        // 3d. Recursion preservation across Wait (depth 2 held, restored).
        g_rec = 0; g_recReady = 0;
        Thread tr = new Thread(RecWaiter);
        tr.Start();
        for (int i = 0; i < 5000 && g_recReady == 0; i++) Thread.Sleep(1);
        lock (gateA) { Monitor.Pulse(gateA); }
        tr.Join();
        Console.WriteLine("wait_rec_restored=" + (g_rec == 1));

        // ==================== 4. EXCEPTION FACE ====================
        // 4a. Exit on an unowned monitor. TYPED catch: the runtime chain now
        // starts with the real full name "System.Threading.Synchronization-
        // LockException" (monitor.cpp ThrowSynchronizationLockException,
        // D-MON-3 fixed) so catch (SynchronizationLockException) matches --
        // same on net48, whose SLE derives from SystemException.
        bool ex1 = false;
        try { Monitor.Exit(gateB); } catch (SynchronizationLockException) { ex1 = true; }
        Console.WriteLine("exit_unowned_throws=" + ex1);
        // 4b. Pulse on an unowned monitor.
        bool ex2 = false;
        try { Monitor.Pulse(gateB); } catch (SynchronizationLockException) { ex2 = true; }
        Console.WriteLine("pulse_unowned_throws=" + ex2);
        // 4c. Wait on an unowned monitor.
        bool ex3 = false;
        try { Monitor.Wait(gateB, 10); } catch (SynchronizationLockException) { ex3 = true; }
        Console.WriteLine("wait_unowned_throws=" + ex3);
        // 4d. Null arguments.
        bool ex4 = false;
        try { Monitor.Enter(null); } catch (ArgumentNullException) { ex4 = true; }
        Console.WriteLine("enter_null_throws=" + ex4);
        bool ex5 = false;
        try { Monitor.Exit(null); } catch (ArgumentNullException) { ex5 = true; }
        Console.WriteLine("exit_null_throws=" + ex5);
        // 4e. lockTaken already true on entry.
        bool ex6 = false;
        bool already = true;
        try { Monitor.Enter(gateA, ref already); } catch (ArgumentException) { ex6 = true; }
        Console.WriteLine("enter_lockTaken_true_throws=" + ex6);

        // ==================== 5. MANUAL Enter/Exit (lock lowering) ==========
        counter = 0;
        bool lt1 = false;
        try
        {
            Monitor.Enter(gateA, ref lt1);
            counter += 5;
        }
        finally
        {
            if (lt1) Monitor.Exit(gateA);
        }
        bool lt2 = false;
        try
        {
            Monitor.Enter(gateB, ref lt2);
            counter += 7;
        }
        finally
        {
            if (lt2) Monitor.Exit(gateB);
        }
        Console.WriteLine("manual_pairs=" + (counter == 12 && lt1 && lt2));
        bool mfA = Monitor.TryEnter(gateA);
        bool mfB = Monitor.TryEnter(gateB);
        Console.WriteLine("manual_free=" + (mfA && mfB));
        if (mfA) Monitor.Exit(gateA);
        if (mfB) Monitor.Exit(gateB);
    }
}
