//------------------------------------------------------------------------------
// threadpooldeep2.cs -- System.Threading.ThreadPool DEEP e2e reference
// program (net48 oracle, byte-for-byte). Created in e2e batch C57
// (2026-08-12), AFTER the basic pool surface (threadpoolcase: QueueUserWorkItem
// 1/2-arg fan-out; conccases: Interlocked/lock mix-in) and the pool wiring
// (MethodMap.ThreadPool.cs -> threadpool.cpp: ONE fixed-size pool, FIFO queue,
// N = min(CPU, 4) workers). This case probes the DEEP axes:
//
//   1. QUEUE (depth/exactness): 64 queued items ALL run and run EXACTLY once
//      (a monotonic Interlocked completion counter gates every read).
//   2. QUEUE (state passing): boxed-int state reaches the right slot -- each
//      worker writes a DISTINCT pre-allocated static array element, so the
//      state-to-slot mapping is race-free and scheduling-independent.
//   3. CONCURRENCY CAP (peak): 16 workers each busy-loop (allocation-free) and
//      record the peak concurrent count via Interlocked.CompareExchange. The
//      FIXED pool guarantees peak >= 2 on any real multi-core host (net48 pool
//      and the runtime's min(CPU,4) pool both overlap the busy workers). Only
//      the BOOLEAN peak >= 2 is asserted -- the exact peak VALUE is pool-
//      configuration-dependent (net48 grows on demand, the runtime caps at 4),
//      so a numeric equality would be a false divergence.
//   4. ROUND-ROBIN (worker distribution): 16 busy workers record the worker
//      ManagedThreadId + IsThreadPoolThread per slot. All must report pool-
//      worker true, and >= 2 DISTINCT workers must have served the queue
//      (the FIFO queue fans out over the fixed pool; a 2ms busy loop per item
//      keeps several workers busy so one worker cannot drain the whole queue).
//      NOTE: the worker records via Thread.CurrentThread -- a fresh Thread
//      object allocation per call (threadobj.cpp:465). This is bounded and
//      small (< 16 objects) vs the 64KB Gen0 segment, so no collection can
//      trigger mid-test; the object is additionally rooted in a static array.
//   5. RE-ENTRANT QUEUE: a worker enqueues another item (a delegate
//      pre-constructed on the MAIN thread and held in a static field -- the
//      allocation-free-worker contract; the static field roots it across the
//      pool's unrooted queue window).
//   6. SetMaxThreads: bare statement (the runtime mapping is ReturnsValue=false
//      -- a no-op fixed pool); the pool must still serve afterwards.
//
// Determinism contract (threadpoolcase/conccases model): workers NEVER print,
// never allocate, and all observable values are read after a bounded
// Interlocked-gated poll. Output is ASCII bool / int.
//------------------------------------------------------------------------------
using System;
using System.Threading;
namespace Cases { public static class ThreadPoolDeep2 {
    static int g_done;
    static int g_active;
    static int g_peak;
    static readonly int[] g_slots = new int[8];
    static readonly int[] g_workerIds = new int[16];
    static readonly int[] g_isPool = new int[16];
    static readonly object[] g_thr = new object[16];
    static WaitCallback g_innerCb;
    static int g_outer;
    static int g_inner;
    // E2E oracle fix (2026-09-11): the busy loops below were `for (...) { }`
    // -- an EMPTY body that Release -O2 legally elides (observed: the workers
    // finished instantly, the queue drained on one worker, workersGE2 flaked).
    // Each iteration does a real Interlocked.Increment -- an icall the C++
    // optimizer cannot remove -- so the intended busy interval survives. The
    // value itself is never asserted (each worker writes its own run counter).
    static int g_spin;

    static void IncWorker(object state)
    {
        Interlocked.Increment(ref g_done);
    }

    static void SlotWorker(object state)
    {
        int idx = (int)state;
        g_slots[idx] = idx * 100;
        Interlocked.Increment(ref g_done);
    }

    static void PeakWorker(object state)
    {
        int cur = Interlocked.Increment(ref g_active);
        for (;;)
        {
            int p = Interlocked.CompareExchange(ref g_peak, 0, 0);
            if (cur <= p) break;
            if (Interlocked.CompareExchange(ref g_peak, cur, p) == p) break;
        }
        for (int i = 0; i < 500000; i++) { Interlocked.Increment(ref g_spin); }
        Interlocked.Decrement(ref g_active);
        Interlocked.Increment(ref g_done);
    }

    static void RoundRobinWorker(object state)
    {
        int idx = (int)state;
        for (int i = 0; i < 500000; i++) { Interlocked.Increment(ref g_spin); }
        Thread cur = Thread.CurrentThread;
        g_thr[idx] = cur;
        g_workerIds[idx] = cur.ManagedThreadId;
        g_isPool[idx] = cur.IsThreadPoolThread ? 1 : 0;
        Interlocked.Increment(ref g_done);
    }

    static void OuterWorker(object state)
    {
        Interlocked.Increment(ref g_outer);
        ThreadPool.QueueUserWorkItem(g_innerCb);
    }

    static void InnerWorker(object state)
    {
        Interlocked.Increment(ref g_inner);
    }

    static bool WaitDone(int target)
    {
        for (int i = 0; i < 5000; i++)
        {
            if (Interlocked.CompareExchange(ref g_done, 0, 0) >= target) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    public static void Main()
    {
        // ---- 1. basic enqueue (1-arg and 2-arg) ----
        bool q1 = ThreadPool.QueueUserWorkItem(new WaitCallback(IncWorker));
        bool q2 = ThreadPool.QueueUserWorkItem(new WaitCallback(IncWorker), "s");
        Console.WriteLine("queued: " + (q1 && q2));
        Console.WriteLine("ranBasic: " + WaitDone(2));
        Console.WriteLine("basicCount: " + (Interlocked.CompareExchange(ref g_done, 0, 0) == 2));

        // ---- 2. queue depth -- 64 items, exactly once ----
        Interlocked.Exchange(ref g_done, 0);
        const int kDepth = 64;
        for (int i = 0; i < kDepth; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(IncWorker));
        bool depthRan = WaitDone(kDepth);
        int depthCount = Interlocked.Exchange(ref g_done, 0);
        Console.WriteLine("depthRan: " + depthRan);
        Console.WriteLine("depthExactly: " + (depthCount == kDepth));

        // ---- 3. state passing -- boxed int state to distinct slots ----
        for (int i = 0; i < 8; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(SlotWorker), i);
        bool slotsRan = WaitDone(8);
        bool slotsOk = true;
        for (int i = 0; i < 8; i++)
            if (g_slots[i] != i * 100) { slotsOk = false; break; }
        Console.WriteLine("slotsRan: " + slotsRan);
        Console.WriteLine("slotsOk: " + slotsOk);

        // ---- 4. concurrency peak -- 16 busy workers, peak >= 2 ----
        Interlocked.Exchange(ref g_done, 0);
        Interlocked.Exchange(ref g_peak, 0);
        Interlocked.Exchange(ref g_active, 0);
        const int kPar = 16;
        for (int i = 0; i < kPar; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(PeakWorker));
        bool peakRan = WaitDone(kPar);
        Console.WriteLine("peakRan: " + peakRan);
        Console.WriteLine("peakGE2: " + (Interlocked.CompareExchange(ref g_peak, 0, 0) >= 2));

        // ---- 5. round-robin -- >= 2 distinct workers, all pool threads ----
        Interlocked.Exchange(ref g_done, 0);
        for (int i = 0; i < 16; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(RoundRobinWorker), i);
        bool distRan = WaitDone(16);
        bool allPool = true;
        for (int i = 0; i < 16; i++)
            if (g_isPool[i] != 1) { allPool = false; break; }
        int distinct = 0;
        int[] seen = new int[16];
        for (int i = 0; i < 16; i++)
        {
            bool dup = false;
            for (int j = 0; j < distinct; j++)
                if (seen[j] == g_workerIds[i]) { dup = true; break; }
            if (!dup) seen[distinct++] = g_workerIds[i];
        }
        Console.WriteLine("distRan: " + distRan);
        Console.WriteLine("allPoolThreads: " + allPool);
        Console.WriteLine("workersGE2: " + (distinct >= 2));

        // ---- 6. re-entrant queue -- worker queues another item ----
        Interlocked.Exchange(ref g_outer, 0);
        Interlocked.Exchange(ref g_inner, 0);
        g_innerCb = new WaitCallback(InnerWorker);
        const int kNest = 4;
        for (int i = 0; i < kNest; i++)
            ThreadPool.QueueUserWorkItem(new WaitCallback(OuterWorker));
        bool nested = false;
        for (int i = 0; i < 5000; i++)
        {
            if (Interlocked.CompareExchange(ref g_inner, 0, 0) >= kNest) { nested = true; break; }
            Thread.Sleep(10);
        }
        Console.WriteLine("nestedRan: " + nested);
        Console.WriteLine("outerCount: " + Interlocked.CompareExchange(ref g_outer, 0, 0));
        Console.WriteLine("innerCount: " + Interlocked.CompareExchange(ref g_inner, 0, 0));

        // ---- 7. SetMaxThreads wired no-op; pool still serves ----
        ThreadPool.SetMaxThreads(2, 2);
        Interlocked.Exchange(ref g_done, 0);
        ThreadPool.QueueUserWorkItem(new WaitCallback(IncWorker));
        Console.WriteLine("aliveAfterSetMax: " + WaitDone(1));
    }
}}
