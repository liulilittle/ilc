//------------------------------------------------------------------------------
// threadops.cs -- end-to-end reference case for the System.Threading.Thread
// DEEP object-model surface (net48 oracle). Exercises the wired
// MethodMap.Thread.cs + threadobj domain (il2cpp_thread.h statics +
// threadobj.h OBJECT model, which runs REAL OS threads):
//
// Coverage (active, all wired):
//   * Thread.Sleep on the MAIN thread: Sleep(50) paired with
//     Environment.TickCount (delta >= 45), Sleep(0) yields, a negative value
//     (-2) raises ArgumentOutOfRangeException.
//   * Thread.CurrentThread: IsAlive true, ManagedThreadId nonzero (the OS
//     thread id), Priority pinned to Normal (the DEFAULT getter value).
//   * Full lifecycle: new Thread(static ThreadStart) -> Start -> Join ->
//     IsAlive transitions (false before Start, true while the child sleeps,
//     false after Join), child side-effect visible to the main thread through
//     the join (happens-before), child ManagedThreadId nonzero and distinct
//     from the main id.
//   * ThreadStateException paths: Join on a never-started thread, Start twice
//     (a self-Join probe is deliberately absent -- net48 BLOCKS forever on a
//     self-join, the runtime model throws instead; see GAPS.md).
//   * new Thread(null) -> ArgumentNullException (the delegate null check).
//   * IsBackground foreground/background flag roundtrip (default foreground).
//
// The child ThreadStart callback must NOT allocate -- the M-stage managed
// allocator is single-threaded, so ChildWorker only writes a static int and
// calls Thread.Sleep (allocation-free, the threadcase contract).
//
// ACCEPTANCE SURFACE (NOT wired -- do not uncomment until fixed):
//   * Thread.Name get/set -- no get_Name/set_Name case in MethodMap.Thread.cs
//     and no icall_threadobj_{get,set}_name in threadobj.h/cpp, so a Name
//     probe transpiles to an undefined generated extern
//     System_Threading_Thread_{get,set}_Name_<hash> (C++ link failure). See
//     GAPS.md D-THR-1.
//   * ManagedThreadId BEFORE Start -- the runtime reports 0 until Start
//     (threadobj.h:67) while net48 assigns a sequential id at construction;
//     a before-Start probe would diverge, so it is NOT asserted.
//   * Thread.Priority SETTER roundtrip -- the runtime pins Priority to Normal
//     (setter accepted-ignored, getter always 2, threadobj.h:72-76) while
//     net48 reflects the set value; only the getter DEFAULT is asserted.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases { public static class ThreadOps {
    // Written by the child thread (allocation-free callback); read by the
    // main thread after Join (happens-before through the join).
    static int g_childRan;

    // The ThreadStart callback: runs on a REAL OS thread. No allocations --
    // the M-stage allocator is single-threaded (the child may not touch the
    // managed heap). The 50 ms sleep keeps IsAlive-after-Start deterministic
    // (the child is still running when the main thread probes it).
    static void ChildWorker()
    {
        g_childRan = 1;
        Thread.Sleep(50);
        g_childRan = 2;
    }

    public static void Main()
    {
        // ---- statics: Sleep + TickCount timing ----
        int t0 = Environment.TickCount;
        Thread.Sleep(50);
        int t1 = Environment.TickCount;
        Console.WriteLine("sleep50_delta=" + (t1 - t0 >= 45));

        Thread.Sleep(0);
        Console.WriteLine("sleep0_ok=True");

        bool threw = false;
        try { Thread.Sleep(-2); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("sleep_neg_throws=" + threw);

        // ---- CurrentThread: IsAlive / ManagedThreadId / default Priority ----
        Thread cur = Thread.CurrentThread;
        Console.WriteLine("cur_isalive=" + cur.IsAlive);
        Console.WriteLine("cur_tid_nonzero=" + (cur.ManagedThreadId != 0));
        Console.WriteLine("cur_priority_normal=" + (cur.Priority == ThreadPriority.Normal));

        // ---- Thread OBJECT lifecycle: new / Start / Join / IsAlive ----
        Thread t = new Thread(ChildWorker);
        Console.WriteLine("new_isalive=" + (t.IsAlive == false));

        int tt0 = Environment.TickCount;
        t.Start();
        Console.WriteLine("started_isalive=" + t.IsAlive);
        t.Join();
        int tt1 = Environment.TickCount;
        Console.WriteLine("join_waited=" + (tt1 - tt0 >= 45));
        Console.WriteLine("child_ran=" + (g_childRan == 2));
        Console.WriteLine("child_tid_nonzero=" + (t.ManagedThreadId != 0));
        Console.WriteLine("child_tid_diff=" + (t.ManagedThreadId != cur.ManagedThreadId));
        Console.WriteLine("joined_isalive=" + (t.IsAlive == false));

        // ---- ThreadStateException paths ----
        Thread t2 = new Thread(ChildWorker);
        threw = false;
        try { t2.Join(); } catch (ThreadStateException) { threw = true; }   // never started
        Console.WriteLine("join_unstarted_throws=" + threw);
        t2.Start();
        threw = false;
        try { t2.Start(); } catch (ThreadStateException) { threw = true; }  // double start
        Console.WriteLine("start_twice_throws=" + threw);
        t2.Join();

        // NOTE: a SELF-JOIN probe (cur.Join()) is deliberately ABSENT: net48
        // blocks forever on a self-join, while the runtime model throws
        // ThreadStateException (threadobj.h:76-77 documented deviation) -- the
        // probe would hang the net48 oracle. Recorded in GAPS.md.

        threw = false;
        try { new Thread((ThreadStart)null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("null_delegate_throws=" + threw);

        // ---- IsBackground: foreground default -> set true -> set false ----
        Thread t3 = new Thread(ChildWorker);
        Console.WriteLine("bg_default_foreground=" + (t3.IsBackground == false));
        t3.IsBackground = true;
        Console.WriteLine("bg_set_true=" + t3.IsBackground);
        t3.IsBackground = false;
        Console.WriteLine("bg_set_false=" + (t3.IsBackground == false));
        t3.Start();
        t3.Join();
    }
}}
