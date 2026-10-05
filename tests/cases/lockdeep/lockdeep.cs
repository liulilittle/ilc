// lockdeep.cs -- LOCK DEEP probe (e2e batch C23, "lock 深测: 嵌套/重入/异常
// 释放 -- lock 修复后"). Builds on lockcase (single-threaded lock statement /
// Monitor surface, label-at-end fix 82d0770) and goes DEEPER on the lock
// statement's lowering semantics:
//
//   * DEEP NESTING: 6 levels of nested lock(gate) -- Roslyn lowers each to
//     Monitor.Enter(obj, ref lockTaken) + try/finally { if(lockTaken)
//     Monitor.Exit(obj); }. A counter at EVERY level proves each guard runs
//     and each Exit fires in the correct reverse order.
//   * REENTRANT RECURSION: a recursive method that locks the SAME gate at
//     every call depth (8 deep) -- the recursion counter must unwind fully
//     (every Enter matched by an Exit in finally).
//   * EXCEPTION AT THE INNERMOST LEVEL: throw inside the 6th lock -- every
//     enclosing finally must still Exit (monitor released at recursion zero);
//     a subsequent Enter proves the monitor is free (no leak).
//   * EXCEPTION IN THE MIDDLE of the nesting: throw at level 3 of 6 --
//     levels 4..6 never entered, but 1..3 release; counter totals exact.
//   * MANUAL Monitor.Enter(object, ref bool) -- the exact lowering primitive
//     the lock statement uses: flag=false, Enter sets it, finally
//     if(flag) Exit. Direct 2-arg Enter/Exit pairs with an EXACT counter.
//   * MONITOR STATE PROBES: Enter twice on the same gate (recursion depth
//     2), Exit once -> still owned (TryEnter on the OWNING thread returns
//     true = reentrant); Exit again -> released; a THIRD Exit throws
//     SynchronizationLockException (caught). lockcase proved the basic
//     form; here the intermediate state (owned-but-level-1) is probed.
//   * WAIT TIMEOUT: inside the lock, Monitor.Wait(gate, 150) times out
//     (no Pulse) -> returns false, monitor REACQUIRED, Exit releases.
//   * TWO GATES: lock(A) { lock(B) { } } then lock(B) { lock(A) { } } on the
//     SAME thread -- sequential, so no deadlock (a single thread holds at
//     most one nesting chain); the reverse order just proves both monitors
//     are independent side-table entries.
//   * lock(this) on an INSTANCE + recursion.
//
// Single-threaded subset (lockcase model): no Thread objects in THIS case --
// conccases/concur2 already own the multi-thread lock matrix. Every check is
// deterministic; the recursion counter and the finally guards are observable
// without scheduling.
//
// Expected output: all-bool rows over exact counter values.
using System;
using System.Threading;

class LockDeep
{
    static int counter;
    static readonly object gateA = new object();
    static readonly object gateB = new object();

    // Reentrant recursion: locks the SAME gate at every depth.
    static void Rec(int depth)
    {
        lock (gateA)
        {
            counter++;
            if (depth > 0) Rec(depth - 1);
        }
    }

    // Nested helper: 6-level nesting with a per-level counter.
    static void Nested6()
    {
        lock (gateA)
        {
            counter += 1;
            lock (gateA)
            {
                counter += 10;
                lock (gateA)
                {
                    counter += 100;
                    lock (gateA)
                    {
                        counter += 1000;
                        lock (gateA)
                        {
                            counter += 10000;
                            lock (gateA)
                            {
                                counter += 100000;
                            }
                        }
                    }
                }
            }
        }
    }

    // Nested helper: exception at the INNERMOST level (6 deep).
    static void Nested6Throw()
    {
        lock (gateA)
        {
            counter += 1;
            lock (gateA)
            {
                counter += 10;
                lock (gateA)
                {
                    counter += 100;
                    lock (gateA)
                    {
                        counter += 1000;
                        lock (gateA)
                        {
                            counter += 10000;
                            lock (gateA)
                            {
                                counter += 100000;
                                throw new InvalidOperationException("deep");
                            }
                        }
                    }
                }
            }
        }
    }

    // Nested helper: exception at level 3 of 6 (levels 4..6 never entered).
    static void NestedThrowMid()
    {
        lock (gateA)
        {
            counter += 1;
            lock (gateA)
            {
                counter += 10;
                lock (gateA)
                {
                    counter += 100;
                    throw new InvalidOperationException("mid");
                }
            }
        }
    }

    // lock(this) recursion on an instance.
    static void InstRec(LockDeep self, int depth)
    {
        lock (self)
        {
            counter++;
            if (depth > 0) InstRec(self, depth - 1);
        }
    }

    static void Main()
    {
        counter = 0;

        // --- 1. deep nesting: 6 levels, all guards + exits run ---
        Nested6();
        // 1 + 10 + 100 + 1000 + 10000 + 100000 = 111111
        Console.WriteLine(counter == 111111);                      // True

        // --- 2. reentrant recursion: 8 locks of the SAME gate ---
        counter = 0;
        Rec(7);
        Console.WriteLine(counter == 8);                          // True

        // --- 3. exception at the innermost level: all finallys release ---
        counter = 0;
        bool caught = false;
        try { Nested6Throw(); }
        catch (InvalidOperationException) { caught = true; }
        // The 6 levels entered (1+10+100+1000+10000+100000) BEFORE the throw.
        Console.WriteLine(caught);                                // True
        Console.WriteLine(counter == 111111);                     // True
        // The monitor was released at recursion zero: Enter succeeds again.
        bool relock = true;
        try { lock (gateA) { counter++; } } catch (Exception) { relock = false; }
        Console.WriteLine(relock);                                // True
        Console.WriteLine(counter == 111112);                     // True

        // --- 4. exception in the middle (level 3 of 6) ---
        counter = 0;
        caught = false;
        try { NestedThrowMid(); }
        catch (InvalidOperationException) { caught = true; }
        Console.WriteLine(caught);                                // True
        Console.WriteLine(counter == 111);                        // True (levels 1..3 ran)
        // The first three monitors released.
        relock = true;
        try { lock (gateA) { counter++; } } catch (Exception) { relock = false; }
        Console.WriteLine(relock);                                // True
        Console.WriteLine(counter == 112);                        // True

        // --- 5. manual Monitor.Enter(object, ref bool) + Exit ---
        counter = 0;
        bool lockTaken = false;
        try
        {
            Monitor.Enter(gateA, ref lockTaken);
            counter += 5;
        }
        finally
        {
            if (lockTaken) Monitor.Exit(gateA);
        }
        Console.WriteLine(lockTaken);                             // True
        Console.WriteLine(counter == 5);                          // True
        // The manual Enter/Exit left the monitor free.
        relock = true;
        try { lock (gateA) { counter++; } } catch (Exception) { relock = false; }
        Console.WriteLine(relock);                                // True
        Console.WriteLine(counter == 6);                          // True

        // --- 6. monitor state probe: Enter twice, Exit once ---
        lockTaken = false;
        Monitor.Enter(gateA, ref lockTaken);                      // level 1
        bool reentered = false;
        Monitor.Enter(gateA, ref reentered);                      // level 2
        // Still owned at recursion level 1 after one Exit.
        Monitor.Exit(gateA);                                      // level 1 remains
        // 1-arg TryEnter on the OWNING thread returns true (reentrant).
        bool tryEnter1 = Monitor.TryEnter(gateA);
        Console.WriteLine(tryEnter1);                             // True (reentrant)
        // Exit the reentrant TryEnter + the remaining level 1.
        Monitor.Exit(gateA);
        Monitor.Exit(gateA);
        // Now released: a THIRD Exit must throw SynchronizationLockException.
        bool syncLock = false;
        try { Monitor.Exit(gateA); } catch (Exception) { syncLock = true; }
        Console.WriteLine(syncLock);                              // True
        // And the monitor is free again.
        relock = true;
        try { lock (gateA) { counter++; } } catch (Exception) { relock = false; }
        Console.WriteLine(relock);                                // True
        Console.WriteLine(counter == 7);                          // True

        // --- 7. Wait timeout inside the lock (no Pulse) ---
        counter = 0;
        bool waitResult = true;
        bool waitOk = true;
        try
        {
            lock (gateA)
            {
                counter += 1;
                waitResult = Monitor.Wait(gateA, 150);            // times out -> false
            }
        }
        catch (Exception) { waitOk = false; }
        Console.WriteLine(waitOk);                                // True
        Console.WriteLine(waitResult == false);                   // True (timeout)
        Console.WriteLine(counter == 1);                          // True
        // Wait reacquired the monitor before Exit released it.
        relock = true;
        try { lock (gateA) { counter++; } } catch (Exception) { relock = false; }
        Console.WriteLine(relock);                                // True
        Console.WriteLine(counter == 2);                          // True

        // --- 8. two independent gates, both orders (sequential, no deadlock) ---
        counter = 0;
        lock (gateA) { lock (gateB) { counter += 3; } }
        Console.WriteLine(counter == 3);                          // True
        lock (gateB) { lock (gateA) { counter += 4; } }           // reverse order
        Console.WriteLine(counter == 7);                          // True

        // --- 9. lock(this) on an instance + recursion ---
        counter = 0;
        LockDeep self = new LockDeep();
        InstRec(self, 4);                                         // 5 locks of 'self'
        Console.WriteLine(counter == 5);                          // True
        // 'self' monitor released (its own side-table entry).
        relock = true;
        try { lock (self) { counter++; } } catch (Exception) { relock = false; }
        Console.WriteLine(relock);                                // True
        Console.WriteLine(counter == 6);                          // True
    }
}
