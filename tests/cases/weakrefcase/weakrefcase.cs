//------------------------------------------------------------------------------
// weakrefcase.cs -- end-to-end reference case for the WEAK REFERENCE chain:
// System.WeakReference + GC.Collect + IsAlive/Target semantics, in the gcext
// domain. Every output line is a labeled bool, so the reference output is
// fully deterministic and hand-verifiable.
//
// WIRING STATUS (verified against the transpiler MethodMap, 2026-08-05):
//   * GC.Collect() parameterless   -- WIRED: il2cpp::runtime::icall_gc_collect
//                                      (MethodMap.GC.cs, count == 0)
//   * GC.Collect(int)              -- WIRED: same parameterless icall; the
//                                      int32 generation argument is DROPPED
//                                      (MethodMap.GC.cs, count == 1) -- the
//                                      runtime collector is full-collection
//                                      only (no per-generation selectable
//                                      collect), so Collect(n) == full collect.
//   * GC.KeepAlive                 -- WIRED: il2cpp::runtime::icall_gc_keep_alive
//   * System.WeakReference ctor    -- NOT wired: MethodMap.WeakReference.cs
//                                      LOUDLY throws (no WeakReference object
//                                      model in the M-stage transpiler; the
//                                      runtime icalls take client-owned void**
//                                      slots). TRANSPILE IS EXPECTED TO FAIL
//                                      until the object model lands; the
//                                      expected.txt is the net48 oracle
//                                      documenting the intended semantics.
//
// DETERMINISM / GC TIMING:
//   * net48 reference: GC.Collect() is a blocking full collection; after it
//     returns, dead targets are reclaimed, so an IsAlive check immediately
//     after Collect is deterministic.
//   * ILC runtime (when wired): icall_gc_collect is a SYNCHRONOUS collection
//     (no background GC), so the same immediate-check pattern stays
//     deterministic on the transpiled side.
//   * Dead-target probes NEVER hold the target in a Main local: the classic
//     net48 JIT liveness pitfall keeps locals alive until end of method
//     (observed empirically: a nulled Main local + a read-back temp kept the
//     target alive across a full collect), so every target that must die is
//     created inside a helper whose frame is gone before the collect. The
//     null-write itself (o = null) stays in the helper to document intent.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Weakrefcase
    {
        // A target that dies as soon as this method returns: no reachable
        // reference escapes, so the next full collection reclaims it
        // deterministically.
        static WeakReference DeadTarget()
        {
            return new WeakReference(new object());
        }

        // Same idea with an explicit null-write: the only strong reference is
        // cleared by hand, then only the weak reference escapes the method.
        static WeakReference CreateAndNullTarget()
        {
            object o = new object();
            WeakReference w = new WeakReference(o);
            o = null;            // clear the only strong reference
            return w;            // only the weak reference escapes
        }

        public static void Main()
        {
            // ===== 1. strong-reference target: alive across a full collect ====
            object o = new object();
            WeakReference wr = new WeakReference(o);
            GC.Collect();
            Console.WriteLine("alive1=" + wr.IsAlive);            // alive1=True

            // ===== 2. weak-ref value read-back: Target returns the object ====
            Console.WriteLine("target1=" + (wr.Target == o));     // target1=True
            Console.WriteLine("readback1=" + (wr.Target == o));   // readback1=True

            // ===== 3. target nulled: dies at the next full collect =====
            WeakReference wn = CreateAndNullTarget();
            GC.Collect();
            Console.WriteLine("alive2=" + wn.IsAlive);            // alive2=False
            Console.WriteLine("target2=" + (wn.Target == null));  // target2=True

            // ===== 4. dead target via helper: no reachable reference =====
            WeakReference wd = DeadTarget();
            GC.Collect();
            Console.WriteLine("alive3=" + wd.IsAlive);            // alive3=False
            Console.WriteLine("target3=" + (wd.Target == null));  // target3=True

            // ===== 5. live target round trip (no collect in between) =====
            object o2 = new object();
            WeakReference wr2 = new WeakReference(o2);
            Console.WriteLine("alive4=" + wr2.IsAlive);           // alive4=True
            Console.WriteLine("readback2=" + (wr2.Target == o2)); // readback2=True
            GC.KeepAlive(o2);
        }
    }
}
