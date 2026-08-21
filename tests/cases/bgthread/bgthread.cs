//------------------------------------------------------------------------------
// bgthread.cs -- System.Threading.Thread BACKGROUND / STOPPED-guard DEEP e2e
// reference case (net48 oracle, byte-for-byte). Created in e2e batch C19
// (2026-08-10).
//
// EXTENDS threadops / threadops2 / threadcase2 on the POST-GUARD surface: the
// thread background guard landed in M6 batch 49 (threadobj.cpp
// icall_threadobj_set_is_background:342-355) -- setting IsBackground on a
// thread whose callback has FINISHED now throws ThreadStateException
// ("Thread is dead; state cannot be accessed."), matching net48. threadops2
// had this exact probe PARKED (D-THR2-3); bgthread ACTIVATES it:
//
//   * FG/BG SWITCH (前台后台): a NEW thread defaults foreground; set
//     true/false roundtrips on an UNSTARTED thread, on the CURRENT thread,
//     and on a RUNNING thread (the guard accepts every non-stopped state,
//     net48-legal). The running-set is made deterministic with a child-set
//     static flag + bounded poll (single-writer static, D1) and a 200 ms
//     child sleep.
//   * STOPPED THROW (STOPPED 抛): after Join the thread is STOPPED -- setting
//     IsBackground throws ThreadStateException (caught, exact type asserted
//     via ex.GetType().Name), the FAILED set leaves the stored flag
//     unchanged (net48 + the icall both mutate only on success), and a base
//     `catch (Exception)` also catches it (ThreadStateException ->
//     SystemException -> Exception chain on both runtimes).
//   * SIBLING GUARDS: Join on an UNSTARTED thread throws ThreadStateException
//     ("Thread has not been started."); a second Start() throws
//     ThreadStateException ("Thread has already been started.").
//
// Byte-stability rules:
//   * Only catch FLAGS + ex.GetType().Name are probed on the failure paths
//     (net48 LOCALIZES ThreadStateException's message; the ILC carrier is
//     hardcoded English -- raw text would diverge, the exops2 D-EX2-3
//     family). No raw message is ever printed.
//   * All thread callbacks are allocation-free (static-field writes + sleep
//     only); delegate construction, boxing and Console output all happen on
//     the MAIN thread.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases { public static class BgThread {
    // single-writer static set by the child at entry (D1) -- the parent polls
    // it (bounded) so the RUNNING-state set is deterministic.
    static bool g_bgStarted;
    static void BgWorker() { g_bgStarted = true; Thread.Sleep(200); }

    public static void Main()
    {
        // ================= 1. FG/BG SWITCH on an UNSTARTED thread =======
        Thread a = new Thread(BgWorker);
        Console.WriteLine("bg_default_fg=" + (a.IsBackground == false));
        a.IsBackground = true;
        Console.WriteLine("bg_set_unstarted=" + a.IsBackground);
        a.IsBackground = false;
        Console.WriteLine("bg_reset_unstarted=" + (a.IsBackground == false));

        // ================= 2. FG/BG SWITCH on the CURRENT thread ========
        Thread cur = Thread.CurrentThread;
        Console.WriteLine("cur_default_fg=" + (cur.IsBackground == false));
        cur.IsBackground = true;
        Console.WriteLine("cur_set_true=" + cur.IsBackground);
        cur.IsBackground = false;
        Console.WriteLine("cur_reset_false=" + (cur.IsBackground == false));

        // ================= 3. FG/BG SWITCH on a RUNNING thread ==========
        Thread t = new Thread(BgWorker);
        t.Start();
        for (int i = 0; i < 1000 && !g_bgStarted; i++) Thread.Sleep(10);
        t.IsBackground = true;                       // RUNNING: accepted
        Console.WriteLine("running_bg_set=" + t.IsBackground);
        t.Join();
        Console.WriteLine("joined_alive=" + (t.IsAlive == false));

        // ============ 4. STOPPED guard: set after Join throws ===========
        // NOTE (D-BG-1): the IsBackground GETTER on a STOPPED thread ALSO
        // throws ThreadStateException on net48 (the flag can no longer be
        // read after the callback finishes), so "flag unchanged after the
        // failed set" is NOT probeable -- the flag-preservation oracle is
        // pinned in GAPS.md. Only the setter-throw + exact type are probed.
        // NOTE (D-BG-2): the ex.GetType().Name read MUST flow into a call
        // (WriteLine), not a local store -- a handler that STORES an
        // ex-member read to a local alongside another statement fails the
        // transpiler's catch-entry fold ("stack empty when popping this").
        bool stoppedCaught = false;
        try { t.IsBackground = true; }
        catch (ThreadStateException ex)
        {
            stoppedCaught = true;
            Console.WriteLine("stopped_bg_type=" + ex.GetType().Name);
        }
        Console.WriteLine("stopped_bg_throws=" + stoppedCaught);

        // ============ 5. STOPPED guard caught via the Exception base =====
        // NOTE (D-BG-2): the catch MUST name a variable -- a VARIABLELESS
        // `catch (Exception)` handler leaves the stack unbalanced in the
        // transpiler's catch-entry fold (a following callvirt pops the entry
        // as its receiver -> "stack empty" / wrong-receiver failure at
        // transpile time). Recorded defect; every catch here binds `ex`.
        bool stoppedBase = false;
        try { t.IsBackground = false; } catch (Exception ex) { stoppedBase = true; }
        Console.WriteLine("stopped_bg_base_catch=" + stoppedBase);

        // ============ 6. Sibling guard: Join on an UNSTARTED thread ======
        Thread nt = new Thread(BgWorker);
        bool joinUnstarted = false;
        try { nt.Join(); } catch (ThreadStateException ex) { joinUnstarted = true; }
        Console.WriteLine("join_unstarted_throws=" + joinUnstarted);

        // ============ 7. Sibling guard: Start TWICE throws ===============
        Thread t2 = new Thread(BgWorker);
        t2.Start();
        bool startTwice = false;
        try { t2.Start(); } catch (ThreadStateException ex) { startTwice = true; }
        t2.Join();
        Console.WriteLine("start_twice_throws=" + startTwice);
    }
}}
