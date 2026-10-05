// thaudit2.cs -- G6 thread-audit probe #2: Mutex reentrancy. A same-thread
// WaitOne(0) twice: net48 Mutex is reentrant (second call returns true); a
// non-reentrant implementation returns false. WaitOne(0) is the required
// NON-BLOCKING form -- a blocking WaitOne() would deadlock on a
// non-reentrant implementation and is never used here.
using System;
using System.Threading;

namespace Cases { public static class ThreadAudit2 {
    public static void Main() {
        Mutex m = new Mutex();
        bool first = m.WaitOne(0);
        bool second = m.WaitOne(0);
        Console.WriteLine("first=" + first);
        Console.WriteLine("second=" + second);
        if (second) m.ReleaseMutex();
        if (first) m.ReleaseMutex();
    }
}}
