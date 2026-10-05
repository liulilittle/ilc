using System;
using System.Threading;

// threadcase2.cs -- net48 reference oracle for the Thread OBJECT model
// wiring (threadobj domain), complementing threadcase.cs: new Thread on a
// STATIC method, two child threads accumulating into SINGLE-WRITER static
// ints, Thread.Sleep short naps inside the workers, Thread.CurrentThread /
// ManagedThreadId probes, IsAlive-after-Join, and IsBackground.
//
// Determinism contract (NO timing asserts -- every value is read AFTER
// Join, so the happens-before edge makes each bool exact):
//   workerA ran: True            (g_workerA == 100 after a.Join())
//   workerB ran: True            (g_workerB == 100 after b.Join())
//   combined count: True         (both sums == 200)
//   worker ids nonzero: True     (ManagedThreadId read after Join -- the
//                                 runtime records the OS id at child entry)
//   worker ids differ: True      (two REAL OS threads -> distinct ids)
//   child id != main id: True
//   current thread id matches: True (CurrentThread().ManagedThreadId is
//                                 stable across two get_CurrentThread calls)
//   isalive after join: True
//   isbackground set: True       (IsBackground = true round-trips)
//
// M-stage constraint: the managed allocator is SINGLE-THREADED, so the
// child callbacks MUST NOT allocate -- WorkerA/WorkerB only write their
// OWN static int field (single writer, no lock) and call Thread.Sleep
// (allocation-free). Thread.CurrentThread is probed on the MAIN thread
// only (its runtime path allocates a fresh Thread object).
class ThreadCase2
{
    // Single-writer shared statics: WorkerA writes g_workerA, WorkerB
    // writes g_workerB. The main thread reads both AFTER Join (join =
    // happens-before, no lock needed).
    static int g_workerA;
    static int g_workerB;

    const int kIters = 100;

    static void WorkerA()
    {
        for (int i = 0; i < kIters; i++)
        {
            g_workerA++;
            Thread.Sleep(1);
        }
    }

    static void WorkerB()
    {
        for (int i = 0; i < kIters; i++)
        {
            g_workerB++;
            Thread.Sleep(1);
        }
    }

    static void Main()
    {
        // --- new Thread(static method) / Start / Join ---
        Thread a = new Thread(WorkerA);
        Thread b = new Thread(WorkerB);

        // IsBackground set/get round-trip (net48: default threads are
        // foreground; setting flips the flag). MUST happen before Start --
        // setting IsBackground on a STOPPED thread throws
        // ThreadStateException on net48 (join'd threads are stopped).
        // Wired: icall_threadobj_get/set_is_background (plain flag storage;
        // the model accepts the set on any state -- documented deviation).
        a.IsBackground = true;
        Console.WriteLine("isbackground set: " + (a.IsBackground == true));

        a.Start();
        b.Start();
        a.Join();
        b.Join();

        // Read the ids AFTER Join: the runtime contract records the child's
        // OS id at thread ENTRY and preserves it on the object through Join
        // (threadobj.cpp: icall_threadobj_join -> t->tid), so a post-Join
        // read is deterministic -- reading right after Start would race the
        // child's entry (id still 0). net48 semantics: ManagedThreadId stays
        // queryable after the thread ends.
        int idA = a.ManagedThreadId;
        int idB = b.ManagedThreadId;

        // Read the static sums into locals first: `"..." + g_workerA`
        // would compile to ldsflda + Int32::ToString(), and the fold has
        // no Ldsflda case (recorded gap -- StackFold.cs default LOUD).
        int wa = g_workerA;
        int wb = g_workerB;
        Console.WriteLine("workerA ran: " + (wa == kIters));
        Console.WriteLine("workerB ran: " + (wb == kIters));
        Console.WriteLine("combined count: " + (wa + wb == kIters * 2));
        Console.WriteLine("worker ids nonzero: " + (idA != 0 && idB != 0));
        Console.WriteLine("worker ids differ: " + (idA != idB));
        Console.WriteLine("child id != main id: " + (idA != Thread.CurrentThread.ManagedThreadId));

        // CurrentThread: two calls must report the same managed id.
        int selfA = Thread.CurrentThread.ManagedThreadId;
        int selfB = Thread.CurrentThread.ManagedThreadId;
        Console.WriteLine("current thread id matches: " + (selfA == selfB));

        // Deterministic post-Join state: both threads are finished.
        Console.WriteLine("isalive after join: " + (a.IsAlive == false && b.IsAlive == false));
    }
}
