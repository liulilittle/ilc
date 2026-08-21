using System;
namespace Cases { public static class Gcext {
    // Deterministic reference output (net48).
    //
    // This case documents the REAL .NET Framework semantics for
    // WeakReference and finalization; the ILC runtime's M-phase GC extension
    // (il2cpp_gc.h) implements the same two-collection finalization shape
    // (dead object retained for one collection, callback runs before the
    // memory is reclaimed) but with a synchronous callback at the start of
    // the next collection instead of a finalizer thread -- so the transpiled
    // run of THIS case is not wired yet (WeakReference/GC icalls are the
    // transpiler's wiring task). Only the net48 reference output matters
    // here.
    class Tracked {
        public static int Finalized = 0;
        public readonly int Id;
        public Tracked(int id) { Id = id; }
        ~Tracked() {
            Console.WriteLine("finalized " + Id);
            Finalized++;
        }
    }

    // A target that dies as soon as this method returns: no reference to the
    // object escapes, so the next full collection reclaims it deterministically.
    static WeakReference DeadRef() {
        return new WeakReference(new object());
    }

    // Same idea for finalizer objects: the instance is unreachable once this
    // method returns (a local in Main would be kept alive by the JIT until
    // the end of the method -- the classic net48 liveness pitfall -- and the
    // finalizer would only run at process exit).
    static void MakeDeadTracked(int id) {
        new Tracked(id);
    }

    public static void Main() {
        // --- WeakReference: live target (kept alive until KeepAlive) ---
        object o = new object();
        WeakReference wr = new WeakReference(o);
        GC.Collect();                       // full collection: o must survive
        Console.WriteLine(wr.IsAlive);      // True
        Console.WriteLine(wr.Target == o);  // True (identity)
        GC.KeepAlive(o);                    // o stays reachable at least until here

        // --- WeakReference: dead target (no reachable reference) ---
        WeakReference wd = DeadRef();
        GC.Collect();                       // target was unreachable -> collected
        Console.WriteLine(wd.IsAlive);      // False

        // --- finalizer: runs once, after death; reclamation is deferred ---
        MakeDeadTracked(1);
        GC.Collect();                       // queues the finalizer (object retained)
        GC.WaitForPendingFinalizers();      // runs it (finalizer thread)
        Console.WriteLine(Tracked.Finalized); // 1
        MakeDeadTracked(2);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(Tracked.Finalized); // 2
    }
}}
