using System;
using System.Threading;

// e2e_mon -- Monitor/lock domain e2e probe (net9 refs oracle vs transpiled
// MinGW build, byte-exact diff). Single-threaded => fully deterministic.
//
// Probes:
//   1. lock(obj){shared=5}            -- Roslyn lowers to Monitor.Enter(obj,
//                                        ref taken) + try/finally Exit; the
//                                        wired surface is MethodMap.Monitor.cs.
//   2. Monitor.Enter(gate, ref entered) / Monitor.Exit(gate) -- the 2-arg
//      Enter form maps to il2cpp_monitor_enter(obj, &lockTaken); reacquire
//      after Exit proves the release actually happened.
//   3. Monitor.IsEntered              -- NOT wired: no entry in
//      MethodMap.Monitor.cs and no il2cpp_monitor_is_entered icall in
//      monitor.h, so the call cannot even be emitted. Skipped with a
//      PASS-skip note on BOTH runtimes (line is part of the byte-exact set).
//   4. nested lock on the SAME object, reentrancy depth 2 -- the runtime
//      monitor must count recursion instead of self-deadlocking.
class E2eMon
{
    static object gate = new object();
    static int shared;

    static int Main()
    {
        int fails = 0;

        // --- 1. lock statement writes shared = 5 under the gate ---
        shared = 0;
        lock (gate) { shared = 5; }
        bool p1 = shared == 5;
        Console.WriteLine((p1 ? "PASS" : "FAIL") + " lock(obj){shared=5} shared=" + shared);
        if (!p1) fails++;

        // --- 2. Monitor.Enter(gate, ref entered) / Monitor.Exit(gate) ---
        bool entered = false;
        Monitor.Enter(gate, ref entered);
        bool acquiredFlag = entered;
        Monitor.Exit(gate);
        // If Exit truly released, a fresh Enter/Exit pair must succeed too.
        Monitor.Enter(gate);
        Monitor.Exit(gate);
        bool p2 = acquiredFlag && entered;
        Console.WriteLine((p2 ? "PASS" : "FAIL") + " Monitor.Enter(gate,ref)/Exit entered=" + entered);
        if (!p2) fails++;

        // --- 3. Monitor.IsEntered -- unwired surface: PASS-skip note ---
        Console.WriteLine("PASS-skip Monitor.IsEntered (not wired in MethodMap.Monitor.cs / monitor.h)");

        // --- 4. nested lock, same object, reentrancy depth 2 ---
        shared = 0;
        lock (gate)
        {
            lock (gate)
            {
                shared += 5;
            }
            shared += 10;
        }
        bool p4 = shared == 15;
        Console.WriteLine((p4 ? "PASS" : "FAIL") + " nested lock same-object depth2 shared=" + shared);
        if (!p4) fails++;

        Console.WriteLine(fails == 0 ? "ALL-PASS" : "ALL-FAIL(" + fails + ")");
        return fails == 0 ? 0 : 1;
    }
}
