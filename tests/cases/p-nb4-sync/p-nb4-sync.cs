//------------------------------------------------------------------------------
// p-nb4-sync.cs -- nb4 SYNC-LITE difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the four lightweight synchronization objects that had NO object model:
//
//   1) SemaphoreSlim   -- counting via CurrentCount/Release()/Wait(0).
//   2) CountdownEvent  -- Signal()/CurrentCount/IsSet.
//   3) SpinLock        -- Enter(ref bool)/Exit()/IsHeld (struct carrier).
//   4) Barrier(1)      -- CurrentPhaseNumber/SignalAndWait() phase advance.
//
// Determinism: EVERY transaction is single-threaded on the main thread --
// Enter(ref bool) followed by Exit(), Release() before Wait(0), Signal() before
// the count probes -- a pure count protocol with NO wall-clock magnitudes, so
// the diff is scheduling-free (per the acceptance's "确定性计数协议").
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class PNb4Sync
    {
        public static void Main()
        {
            // 1) SemaphoreSlim: exhausted probe reads "0", the release that
            //    bumps 0->1 reads "1" (previous count), the acquisition reads
            //    True, the second probe of an exhausted one reads False.
            var sEx = new SemaphoreSlim(0);
            var sOne = new SemaphoreSlim(1);
            Console.WriteLine("[semslim]=" + sEx.CurrentCount + "|"
                              + sOne.Release() + "|"
                              + sOne.Wait(0) + "|"
                              + sEx.Wait(0));

            // 2) CountdownEvent: IsSet False at 2, CurrentCount 1 after one
            //    Signal, IsSet True + CurrentCount 0 after the second.
            var ce = new CountdownEvent(2);
            Console.WriteLine("[countdown]=" + ce.IsSet + "|");
            ce.Signal();
            Console.WriteLine(ce.CurrentCount + "|");
            ce.Signal();
            Console.WriteLine(ce.IsSet + "|");
            Console.WriteLine(ce.CurrentCount);

            // 3) SpinLock: IsHeld after Enter True, the taken flag True, IsHeld
            //    back to False after Exit.
            var sl = new SpinLock(false);
            bool taken = false;
            sl.Enter(ref taken);
            Console.WriteLine("[spinlock]=" + sl.IsHeld + "|" + taken);
            sl.Exit();
            Console.WriteLine("|" + sl.IsHeld);

            // 4) Barrier(1): phase 0 before SignalAndWait, phase 1 after.
            var b = new Barrier(1);
            Console.WriteLine("[barrier]=" + b.CurrentPhaseNumber + "|");
            b.SignalAndWait();
            Console.WriteLine(b.CurrentPhaseNumber);
        }
    }
}