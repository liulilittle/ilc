// thaudit1.cs -- G6 thread-audit probe #1: 65 simultaneous distinct-object
// monitors. net48 has no fixed monitor limit; the runtime monitor side table
// caps at 64 entries (monitor.cpp kMaxMonitors, the 65th Enter throws
// InvalidOperationException("monitor side table exhausted")).
//
// Deterministic: single thread, int/bool output only, no timing, no path
// echo. Every acquired monitor is released by the finally unwind.
using System;
using System.Threading;

namespace Cases { public static class ThreadAudit1 {
    public static void Main() {
        object[] gates = new object[65];
        for (int i = 0; i < 65; i++) gates[i] = new object();

        int acquired = 0;
        bool threw = false;
        try {
            for (int i = 0; i < 65; i++) {
                Monitor.Enter(gates[i]);
                acquired++;
            }
        } catch (Exception) {
            threw = true;
        } finally {
            for (int i = acquired - 1; i >= 0; i--) Monitor.Exit(gates[i]);
        }
        Console.WriteLine("acquired=" + acquired);
        Console.WriteLine("threw=" + threw);
    }
}}
