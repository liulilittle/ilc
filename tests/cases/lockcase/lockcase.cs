using System;
using System.Threading;

// lockcase.cs -- net48 reference oracle for the C# lock statement
// (System.Threading.Monitor domain; see MethodMap.Monitor.cs and
// src/runtime/{include/internal/monitor.h,src/monitor.cpp}).
//
// Roslyn lowers lock(obj) { BODY } to ordinary icall calls:
//   Monitor.Enter(obj, ref lockTaken);
//   try { BODY } finally { if (lockTaken) Monitor.Exit(obj); }
// so lock support == Monitor.Enter/Exit wiring; the transpiler needs no
// lock special-casing. MethodMap.Monitor maps the 2-arg Enter to
// il2cpp_monitor_enter(obj, &lockTaken), whose bool* overload monitor.h/cpp
// must declare for the generated C++ to compile.
//
// Single-threaded subset only: no Thread objects exist (MethodMap.Thread.cs),
// so every check is deterministic -- the recursion counter and the finally
// guard are observable without scheduling.
//
// The "exception inside lock" probe relies on the net48 rule that
// Monitor.Exit on a monitor the caller does NOT own throws
// SynchronizationLockException: after the finally has released the monitor,
// an explicit Exit must throw. catch (Exception) keeps the probe independent
// of the exact exception name (the runtime's SynchronizationLockException
// carrier).
//
// Expected output (deterministic, all-bool): 9x True
class LockCase
{
    static int counter;
    static readonly object gate = new object();

    static void Main()
    {
        // --- 1. lock protecting a counter (single-threaded sequential) ---
        counter = 0;
        for (int i = 0; i < 100; i++)
        {
            lock (gate) { counter++; }
        }
        Console.WriteLine(counter == 100);

        // 1b. direct 1-arg Monitor.Enter/Exit pair (the classic lock core).
        Monitor.Enter(gate);
        counter++;
        Monitor.Exit(gate);
        Console.WriteLine(counter == 101);

        // --- 2. nested lock (reentrant Monitor.Enter recursion) ---
        lock (gate)
        {
            lock (gate)
            {
                lock (gate) { counter += 5; }
            }
            counter += 10;
        }
        Console.WriteLine(counter == 116);

        // --- 3. exception inside lock: the finally must still run Exit ---
        bool caught = false;
        try
        {
            lock (gate)
            {
                counter += 1;
                throw new InvalidOperationException("boom");
            }
        }
        catch (InvalidOperationException) { caught = true; }
        Console.WriteLine(caught);

        // The finally already released the monitor, so an explicit Exit now
        // must throw (SynchronizationLockException) instead of re-entering.
        bool released = false;
        try { Monitor.Exit(gate); }
        catch (Exception) { released = true; }
        Console.WriteLine(released);
        Console.WriteLine(counter == 117);

        // --- 4. lock on a string object (interning makes "shared" one object)
        string s = "shared";
        lock (s)
        {
            lock (s) { counter += 100; }
        }
        Console.WriteLine(counter == 217);

        // --- 5. lock(this) inside an instance method ---
        LockCase self = new LockCase();
        self.Run();
        Console.WriteLine(counter == 267);

        // --- 6. the monitor table is still functional after all the churn ---
        lock (gate) { counter += 3; }
        Console.WriteLine(counter == 270);
    }

    void Run()
    {
        for (int i = 0; i < 50; i++)
        {
            lock (this) { counter++; }
        }
    }
}
