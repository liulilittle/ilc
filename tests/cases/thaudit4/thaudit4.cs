// thaudit4.cs -- G6 thread-audit probe #4: one-shot Timer re-arm after the
// fire. Timer(cb, null, 50, Timeout.Infinite) fires once; after the first
// fire a Change(10, Timeout.Infinite) re-arms the schedule. net48 fires
// again (g_fires == 2); an implementation whose per-timer OS thread exits
// after a one-shot fire ignores the Change (g_fires == 1).
//
// Fixed sleeps only (no wait loops, no infinite waits): 400 ms guarantees
// the 50 ms first fire completed before the Change, 300 ms is the window
// for the re-armed 10 ms fire. Dispose() joins/waits before the read.
using System;
using System.Threading;

namespace Cases { public static class ThreadAudit4 {
    static int g_fires;
    static void Cb(object state) { g_fires++; }
    public static void Main() {
        Timer t = new Timer(Cb, null, 50, Timeout.Infinite);
        Thread.Sleep(400);
        t.Change(10, Timeout.Infinite);
        Thread.Sleep(300);
        t.Dispose();
        Console.WriteLine("fires=" + g_fires);
    }
}}
