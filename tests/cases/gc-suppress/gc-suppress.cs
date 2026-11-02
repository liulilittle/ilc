//------------------------------------------------------------------------------
// gc-suppress.cs -- GC.SuppressFinalize / GC.ReRegisterForFinalize e2e case
// (net48 oracle, byte-for-byte). Closes GAP-FIN-3 from tests/cases/finalops.
//
// WHY THIS CASE EXISTS: both icalls used to be placeholder no-ops, so a user
// finalizer whose body called GC.SuppressFinalize(this) still ran -- a silent
// divergence from net48. The collector now implements the measured net48
// semantics (oracle probes: .opencode/tmp/impl-gc-suppress/oracle/NOTES.md):
//
//     ReRegister: owed += 1, latch = false
//     Suppress  : if (!latch) { owed = max(owed-1, 0); latch = true }
//     the finalizer body runs whenever owed > executed
//
// ISOLATION (GAP-FIN-2, still real): this runtime roots a frame's reference
// temps for the WHOLE method, so an object created in Main stays reachable
// until Main returns and its finalizer never runs -- on net48 the same object
// dies. EVERY must-die object is therefore created, acted on and dropped inside
// a helper whose frame pops before the next collection.
//
// THE HELPERS DELIBERATELY CONTAIN A BRANCH (`if (id != 0)`). That is not
// decoration: the transpiler INLINES small straight-line bodies into Main
// (Opt/InlinePass.cs: BodyWithinThreshold allows <=5 statements, <=10 when
// branch-free), and an inlined allocation lands in MAIN's __frameRefs -- which
// are never cleared, so the object never dies and the case would print 0 for
// BOTH arms (both runtimes coincidentally agreeing, proving nothing). A branch
// with >5 statements keeps the body out of line, so the frame really pops.
// A.control=1 below is the self-check: if inlining ever changes, the control
// arm goes to 0 and this case goes RED instead of silently passing.
//
// GAP-FIN-1 (also real): a discarded `new T()` whose ctor is empty is DCE'd.
// Tracked's ctor stores Id (an observable side effect), so the allocation
// survives; with the branch above it also survives the inline pass.
//
// DETERMINISM: finalizers are SILENT (counters only; no Console output from
// the finalizer thread), and every aggregate is read in Main after a fixed
// Drain() of six collect/wait rounds. net48's finalizer queue order is
// implementation-specific, so only AGGREGATES are printed.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class GcSuppress {

    // Finalizable type. ctor stores Id (GAP-FIN-1: keeps the allocation alive).
    class Tracked {
        public static int N;
        public readonly int Id;
        public Tracked(int id) { Id = id; }
        ~Tracked() { N++; }
    }

    // A type with NO finalizer, for the "suppress/re-register is a no-op" arm.
    class NoFin { public int V; public NoFin(int v) { V = v; } }

    // ---- helpers: the object dies the moment the helper's frame pops ----
    // The `if (id != 0)` branches keep these bodies out of line (see the header):
    // an inlined allocation would be rooted in Main's frame instead.
    static void MakeUnsuppressed(int id) {
        Tracked t = new Tracked(id);
        if (id != 0) { GC.KeepAlive(t); }
    }

    static void MakeSuppressed(int id) {
        Tracked t = new Tracked(id);
        if (id != 0) { GC.SuppressFinalize(t); }
    }

    static void MakeSuppressedTwice(int id) {
        Tracked t = new Tracked(id);
        if (id != 0) {
            GC.SuppressFinalize(t);
            GC.SuppressFinalize(t);   // latched: must NOT cancel a second run
        }
    }

    static void MakeSuppressThenReregister(int id) {
        Tracked t = new Tracked(id);
        if (id != 0) {
            GC.SuppressFinalize(t);
            GC.ReRegisterForFinalize(t);
        }
    }

    static void MakeReregisterTwice(int id) {
        Tracked t = new Tracked(id);
        if (id != 0) {
            GC.ReRegisterForFinalize(t);
            GC.ReRegisterForFinalize(t);
        }
    }

    static void MakeReregisterThenSuppress(int id) {
        Tracked t = new Tracked(id);
        if (id != 0) {
            GC.ReRegisterForFinalize(t);
            GC.SuppressFinalize(t);
        }
    }

    static void MakeSuppressReregisterSuppress(int id) {
        Tracked t = new Tracked(id);
        if (id != 0) {
            GC.SuppressFinalize(t);
            GC.ReRegisterForFinalize(t);
            GC.SuppressFinalize(t);   // R cleared the latch: suppresses again
        }
    }

    static void MakeSuppressedNoFin(int id) {
        NoFin n = new NoFin(id);
        if (id != 0) {
            GC.SuppressFinalize(n);
            GC.ReRegisterForFinalize(n);
        }
    }

    // WeakReference to a SUPPRESSED object: proves the object really died
    // (memory reclaimed) rather than merely skipping its finalizer.
    static WeakReference SuppressedWeakRef(int id) {
        Tracked t = new Tracked(id);
        GC.SuppressFinalize(t);
        return new WeakReference(t);
    }

    static WeakReference UnsuppressedWeakRef(int id) {
        Tracked t = new Tracked(id);
        return new WeakReference(t);
    }

    // Six collect/wait rounds: enough for net48 to drain its finalizer queue and
    // for this runtime's two-collection cycle to settle every owed run.
    static void Drain() {
        for (int i = 0; i < 6; i++) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    public static void Main() {
        // ---- A. control: an unsuppressed object's finalizer RUNS (N=1) ----
        Tracked.N = 0;
        MakeUnsuppressed(1);
        Drain();
        Console.WriteLine("A.control=" + Tracked.N);          // 1

        // ---- B. suppressed object's finalizer does NOT run (the GAP-FIN-3
        //         discriminator: a no-op implementation prints 1 here) ----
        Tracked.N = 0;
        MakeSuppressed(2);
        Drain();
        Console.WriteLine("B.suppressed=" + Tracked.N);       // 0

        // ---- C. suppress is LATCHED: S;S == S (still 0) ----
        Tracked.N = 0;
        MakeSuppressedTwice(3);
        Drain();
        Console.WriteLine("C.suppressed_twice=" + Tracked.N); // 0

        // ---- D. suppress then re-register: exactly one run comes back ----
        Tracked.N = 0;
        MakeSuppressThenReregister(4);
        Drain();
        Console.WriteLine("D.suppress_reregister=" + Tracked.N); // 1

        // ---- E. R;R queues TWO extra runs (3 invocations in total) ----
        Tracked.N = 0;
        MakeReregisterTwice(5);
        Drain();
        Console.WriteLine("E.reregister_twice=" + Tracked.N);  // 3

        // ---- F. R then S cancels the run R added (one invocation) ----
        Tracked.N = 0;
        MakeReregisterThenSuppress(6);
        Drain();
        Console.WriteLine("F.reregister_suppress=" + Tracked.N); // 1

        // ---- G. S;R;S suppresses again (R cleared the latch) ----
        Tracked.N = 0;
        MakeSuppressReregisterSuppress(7);
        Drain();
        Console.WriteLine("G.srs=" + Tracked.N);               // 0

        // ---- H. suppress/re-register on a type with NO finalizer: silent ----
        Tracked.N = 0;
        MakeSuppressedNoFin(8);
        Drain();
        Console.WriteLine("H.nofin=" + Tracked.N);             // 0

        // ---- I. a suppressed object is still COLLECTED (weak target dies) ----
        WeakReference ws = SuppressedWeakRef(9);
        Drain();
        Console.WriteLine("I.suppressed_dead=" + (ws.IsAlive ? 1 : 0));   // 0

        // ---- J. an unsuppressed object also dies, its finalizer runs ----
        Tracked.N = 0;
        WeakReference wu = UnsuppressedWeakRef(10);
        Drain();
        Console.WriteLine("J.unsuppressed_dead=" + (wu.IsAlive ? 1 : 0)); // 0
        Console.WriteLine("J.unsuppressed_fin=" + Tracked.N);             // 1

        // ---- K. null argument contract (net48: ArgumentNullException).
        // Caught by SPECIFIC type: the collector's ArgumentNullException
        // carries the thrown type as its first chain segment, so an exact
        // catch is the portable assertion (see the report for the separate
        // latent note about catch(Exception) and base-chain strings). ----
        try { GC.SuppressFinalize(null); Console.WriteLine("K.suppress=no-throw"); }
        catch (ArgumentNullException) { Console.WriteLine("K.suppress=ANE"); }
        try { GC.ReRegisterForFinalize(null); Console.WriteLine("K.reregister=no-throw"); }
        catch (ArgumentNullException) { Console.WriteLine("K.reregister=ANE"); }
    }
}}