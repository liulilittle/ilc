//------------------------------------------------------------------------------
// p-nb3-sync.cs -- nb3 SYNC-GAP difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the three sync closures:
//
//   1) Monitor.Wait(object) -- the single-arg form (which is
//      Timeout.Infinite / -1) was a transpiler LOUD rejection; the runtime
//      already implements wait(obj, -1). The wired arm must make
//      `Monitor.Wait(gate)` (no timeout) usable. Probed with the threadsync3
//      AIRTIGHT worker handshake: the worker increments a ready counter
//      under the gate lock and then calls Monitor.Wait(gate); the main thread
//      spins on the counter and THEN acquires the gate -- the acquisition
//      PROVES the worker released the lock and registered inside Wait -- so
//      the single Pulse cannot be missed on either runtime. Output is read
//      only after Join: scheduling-independent.
//
//   2) WaitOne(TimeSpan) -- the runtime wait-handle icalls exposed only the
//      int-milliseconds form (WaitOne(int)), so the TimeSpan overload folded
//      to a phantom symbol; net48 pins 1ms semantics (ticks/10000, truncation
//      toward zero -- TimeSpan.FromMilliseconds(1) == 10000 ticks) exclusive
//      of Timeout.InfiniteTimeSpan. Probed single-threaded as pure protocol
//      transactions: a NOT-signaled handle times out (false), a Set()ed
//      auto-reset handle is consumed once (true then false), a manual-reset
//      handle stays signaled until Reset, a semaphore Release bumps the
//      count (true then exhausted).
//
//   3) AutoResetEvent / ManualResetEvent -- the two derived wait handles had
//      NO class descriptors and NO ctor arms (phantom generated symbols). The
//      probe constructs both and drives Set/Reset/WaitOne(TimeSpan) through
//      the inherited EventWaitHandle icalls, so the net48 oracle pins the
//      exact transition table the runtime must reproduce.
//
// All records are plain True/False/1/0 booleans or bounded fixed sequences --
// NO wall-clock magnitudes are printed -- so the diff is scheduling-free.
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class PNb3Sync
    {
        static readonly object gate = new object();
        static int c_ready;   // worker entered Monitor.Wait(gate) single-arg
        static int c_woke;    // single-arg Wait returned true

        static void Wait1Worker()
        {
            lock (gate)
            {
                Interlocked.Increment(ref c_ready);
                bool w = Monitor.Wait(gate);   // Wait(object) == infinite
                if (w) c_woke = 1;
            }
        }

        public static void Main()
        {
            // 1) Monitor.Wait(object) single-arg (infinite arm).
            c_ready = 0; c_woke = 0;
            Thread tw = new Thread(Wait1Worker);
            tw.Start();
            for (int i = 0; i < 5000 && c_ready == 0; i++) Thread.Sleep(1);
            lock (gate) { Monitor.Pulse(gate); }
            tw.Join();
            Console.WriteLine("[sync.mw.single]=" + (c_woke == 1));

            // 2) WaitOne(TimeSpan) -- timeout / set transactions.
            var are = new AutoResetEvent(false);
            Console.WriteLine("[sync.are.ts0]=" + are.WaitOne(TimeSpan.FromMilliseconds(1)));
            are.Set();
            Console.WriteLine("[sync.are.set.wo]=" + are.WaitOne());
            Console.WriteLine("[sync.are.set.ts]=" + are.WaitOne(TimeSpan.FromMilliseconds(1)));
            are.Set();
            are.Set();                       // net48 boolean signal: still ONE count
            Console.WriteLine("[sync.are.set2.wo0]=" + are.WaitOne(0));
            are.Reset();
            Console.WriteLine("[sync.are.reset.ts]=" + are.WaitOne(TimeSpan.FromMilliseconds(1)));

            var mre = new ManualResetEvent(false);
            Console.WriteLine("[sync.mre.ts0]=" + mre.WaitOne(TimeSpan.FromMilliseconds(1)));
            mre.Set();
            Console.WriteLine("[sync.mre.set.wo]=" + mre.WaitOne());
            Console.WriteLine("[sync.mre.set.ts]=" + mre.WaitOne(TimeSpan.FromMilliseconds(1)));
            mre.Reset();
            Console.WriteLine("[sync.mre.reset.ts]=" + mre.WaitOne(TimeSpan.FromMilliseconds(1)));

            var sem = new Semaphore(0, 2);
            Console.WriteLine("[sync.sem.ts0]=" + sem.WaitOne(TimeSpan.FromMilliseconds(1)));
            sem.Release(1);
            Console.WriteLine("[sync.sem.rel.ts]=" + sem.WaitOne(TimeSpan.FromMilliseconds(1)));
            Console.WriteLine("[sync.sem.exh.ts]=" + sem.WaitOne(TimeSpan.FromMilliseconds(1)));

            // 3) cross-thread signal delivery (worker Set -> WaitOne True).
            var ev = new AutoResetEvent(false);
            int done = 0;
            Thread twk = new Thread(delegate () { ev.Set(); Interlocked.Increment(ref done); });
            twk.Start();
            for (int i = 0; i < 5000 && done == 0; i++) Thread.Sleep(1);
            bool w2 = ev.WaitOne(TimeSpan.FromSeconds(2));
            twk.Join();
            Console.WriteLine("[sync.are.thread.set]=" + (w2 && done == 1));
        }
    }
}