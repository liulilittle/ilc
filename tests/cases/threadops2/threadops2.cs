//------------------------------------------------------------------------------
// threadops2.cs -- System.Threading.Thread DEEP 2 e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C18 (2026-08-10).
//
// EXTENDS threadops / threadcase2 / threadpoolcase on the wired surface
// (MethodMap.Thread.cs + MethodMap.ThreadPool.cs + thread.cpp / threadobj.cpp
// / threadpool.cpp):
//
//   1) THREAD POOL WORKER: QueueUserWorkItem(WaitCallback, object) 2-arg AND
//      QueueUserWorkItem(WaitCallback) 1-arg (both -> the delegate unwrap +
//      enqueue, il2cpp_threadpool_queue_work_item_delegate, threadpool.cpp).
//      Four DISTINCT single-writer static flags (one per queued item) prove
//      every item ran; the main thread polls them bounded (Sleep(10) x1000).
//      Thread.CurrentThread.IsThreadPoolThread on the MAIN thread is false
//      (il2cpp_threadpool_is_current_thread_worker -> thread_local flag).
//      NOTE: an IN-WORKER IsThreadPoolThread probe is deliberately absent --
//      the worker callback must stay allocation-free (single-threaded managed
//      allocator) AND net48 resolves the flag per-Thread-object (the ILC
//      icall is current-thread-scoped, a documented deviation), so a worker
//      probe would diverge; recorded in GAPS.md D-THR2-2.
//   2) FOREGROUND / BACKGROUND: new Thread defaults foreground; set true /
//      set false roundtrips (icall_threadobj_get/set_is_background);
//      CurrentThread defaults foreground and set true/false roundtrips.
//      (net48 throws ThreadStateException setting IsBackground on a STOPPED
//      thread -- the ILC model accepts on any state, documented deviation;
//      the after-Join set probe is therefore PARKED, D-THR2-3.)
//   3) MemoryBarrier: Thread.MemoryBarrier() full seq_cst fence
//      (icall_thread_memory_barrier -- std::atomic_thread_fence) between a
//      write and a read; the result stays deterministic (single-threaded).
//   4) Thread.Name ROUNDTRIP -- PARKED (D-THR2-1): the runtime icalls exist
//      (threadobj.cpp icall_threadobj_get_name:346 / set_name:364) but
//      MethodMap.Thread.cs has NO get_Name / set_Name case at HEAD 9ec11f8,
//      so a Name probe transpiles to an undefined generated extern
//      System_Threading_Thread_{get,set}_Name_<hash> (C++ link failure, the
//      threadops GAPS.md D-THR-1 prediction). The probes are written below
//      (BLOCKED) with the net48 semantics pinned by the threadobj.cpp setter
//      contract; uncomment once the mapping lands.
//
// M-stage constraints honoured: every thread/pool callback is allocation-free
// (static-field writes only); ALL delegate construction, boxing and
// Console output happen on the MAIN thread.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases { public static class ThreadOps2 {
    // ---- pool workers: four DISTINCT single-writer flags (D1) ----
    static bool g_poolW1, g_poolW2, g_poolW3, g_poolW4;
    static void PoolW1(object s) { g_poolW1 = true; }
    static void PoolW2(object s) { g_poolW2 = true; }
    static void PoolW3(object s) { g_poolW3 = true; }
    static void PoolW4(object s) { g_poolW4 = true; }

    // ---- fg/bg child worker (allocation-free; sleeps so IsAlive is
    // observable) ----
    static void BgWorker() { Thread.Sleep(30); }

    public static void Main()
    {
        // ===================== 1. THREAD POOL WORKER =====================
        // 2-arg form: QueueUserWorkItem(WaitCallback, object)
        bool q2 = ThreadPool.QueueUserWorkItem(new WaitCallback(PoolW1), "s");
        Console.WriteLine("q_2arg=" + q2);
        // 1-arg form: QueueUserWorkItem(WaitCallback)
        bool q1 = ThreadPool.QueueUserWorkItem(new WaitCallback(PoolW2));
        Console.WriteLine("q_1arg=" + q1);
        // a second 2-arg form with a boxed-int state
        bool q2b = ThreadPool.QueueUserWorkItem(new WaitCallback(PoolW3), 42);
        Console.WriteLine("q_2arg_boxed=" + q2b);
        ThreadPool.QueueUserWorkItem(new WaitCallback(PoolW4));
        bool allRan = false;
        for (int i = 0; i < 1000; i++)
        {
            if (g_poolW1 && g_poolW2 && g_poolW3 && g_poolW4) { allRan = true; break; }
            Thread.Sleep(10);
        }
        Console.WriteLine("pool_all_ran=" + allRan);
        bool w1 = g_poolW1; bool w2 = g_poolW2; bool w3 = g_poolW3; bool w4 = g_poolW4;
        Console.WriteLine("pool_each_ran=" + (w1 && w2 && w3 && w4));
        // main thread is NOT a pool worker (thread_local registry)
        Console.WriteLine("pool_main_ispool=" + Thread.CurrentThread.IsThreadPoolThread);

        // ===================== 2. FOREGROUND / BACKGROUND =================
        Thread t = new Thread(BgWorker);
        Console.WriteLine("fg_default=" + (t.IsBackground == false));
        t.IsBackground = true;
        Console.WriteLine("bg_set_true=" + t.IsBackground);
        t.IsBackground = false;
        Console.WriteLine("bg_reset_false=" + (t.IsBackground == false));
        t.Start();
        t.Join();
        Console.WriteLine("bg_thread_joined=" + (t.IsAlive == false));

        Thread cur = Thread.CurrentThread;
        Console.WriteLine("cur_bg_default=" + (cur.IsBackground == false));
        cur.IsBackground = true;
        Console.WriteLine("cur_bg_set_true=" + cur.IsBackground);
        cur.IsBackground = false;
        Console.WriteLine("cur_bg_reset_false=" + (cur.IsBackground == false));

        // ===================== 3. MemoryBarrier =================
        int mbx = 0;
        mbx = 1;
        Thread.MemoryBarrier();
        int mby = mbx + 1;
        Console.WriteLine("mb_order=" + (mby == 2));
        Thread.MemoryBarrier();
        Console.WriteLine("mb_noop_ok=True");

        // ===================== 4. Thread.Name (PARKED, D-THR2-1) =====
        // BLOCKED at HEAD: get_Name / set_Name unmapped in MethodMap.Thread.cs
        // (runtime icalls threadobj.cpp:346/364 exist). Uncomment when the
        // mapping lands; net48 oracle values per the threadobj.cpp setter
        // contract (csc-probe-verified):
        //
        //   Thread nt = new Thread(BgWorker);
        //   nt.Name = "pooled";                              // set once
        //   Console.WriteLine("name_set=" + (nt.Name == "pooled"));   // True
        //   nt.Start(); nt.Join();
        //   Console.WriteLine("name_after_join=" + (nt.Name == "pooled")); // True
        //   Thread cur2 = Thread.CurrentThread;
        //   cur2.Name = "main";
        //   Console.WriteLine("cur_name=" + (cur2.Name == "main"));     // True
        //   cur2.Name = null;                                 // clear, no throw
        //   Console.WriteLine("cur_name_cleared=" + (cur2.Name == null)); // True
        //   bool twice = false;
        //   try { nt.Name = "second"; }                       // already named
        //   catch (InvalidOperationException) { twice = true; }
        //   Console.WriteLine("name_set_twice_throws=" + twice);        // True
    }
}}
