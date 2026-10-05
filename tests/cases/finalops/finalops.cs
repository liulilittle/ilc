//------------------------------------------------------------------------------
// finalops.cs -- GC/FINALIZER DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C13 (2026-08-09).
//
// Goes DEEPER than gcext on the finalizer chain that batch 40 closed
// (type-level finalizer registry, il2cpp_gc_register_type_finalizer +
// AllocCore auto-arm): gcext proves ONE finalizable type dies and its
// finalizer runs once. finalops proves the CHAIN holds for:
//   * MULTIPLE finalizable types in one program (each registered type gets
//     its own Finalize callback, every allocation of that type is armed).
//   * MULTIPLE instances of the SAME type dying in ONE collection -- the
//     aggregate counter increments by N. Per-object prints are AVOIDED for
//     simultaneous deaths (net48's finalizer thread processes the queue in
//     LIFO order, and ILC's table runs FIFO -- order is implementation-
//     specific, so only the AGGREGATE is byte-stable); per-object prints
//     appear only in single-death-per-round probes.
//   * A derived finalizer type whose Finalize body runs BEFORE the base's
//     (net48 destructor chaining: ~Derived() body ends with base.Finalize()
//     -> BaseFin's own finalizer body runs after the derived one; the ILC
//     two-collection registry runs the derived callback whose generated body
//     then calls the base Finalize -- both counters move).
//   * Finalizer + WeakReference interplay: a finalizable object's weakref
//     reads False AFTER the finalizer has run (dead AND finalized).
//   * GC.KeepAlive root: a kept-alive finalizable object does NOT run its
//     finalizer -- a LOCAL whose liveness is extended by GC.KeepAlive and then
//     explicitly released (net48 JIT liveness to KeepAlive vs ILC frame root
//     until the slot is nulled -- releasing before any further collect makes
//     the death+finalize deterministic on BOTH runtimes; a plain Main local
//     whose liveness was NOT extended would be collected mid-Main on net48
//     but not on ILC, diverging).
//
// LIVERESS DESIGN RULE (the gcext pattern, extended): every object that must
// DIE is allocated inside a HELPER method whose frame pops before the next
// collection -- the transpiler roots only frame-scoped temps while the frame
// is on the stack, so a helper-created object is unreachable the moment the
// helper returns, exactly like net48. Objects that must SURVIVE keep a LOCAL
// reference with GC.KeepAlive extending their liveness (identical on both).
//
// WIRED SURFACE (MethodMap.GC.cs + il2cpp_gc_api.h / gc.cpp / gcext.cpp):
//   GC.Collect / GC.GetTotalMemory / GC.GetGeneration / GC.KeepAlive /
//   GC.WaitForPendingFinalizers (-> icall_gc_finalize_trigger) /
//   System.WeakReference ctor/IsAlive/Target / user ~Finalize() destructors
//   (type-level registry).
//
// RECORDED GAPS (see GAPS.md, each verified empirically): (1) a discarded
// `new T()` where T's ctor is EMPTY is DCE'd by the transpiler (the net48
// allocation + finalizer side effect are lost); (2) an object passed to a
// WeakReference ctor in Main lands in a frame-rooted temp on ILC and never
// dies (net48 collects it) -- the helper pattern sidesteps it; (3)
// GC.SuppressFinalize is a placeholder no-op on ILC (net48 genuinely
// suppresses); (4) STATIC reference fields are NOT GC roots on ILC (no
// add_static_root emitted anywhere) -- an object pinned only by a static
// field dies on ILC while net48 keeps it alive.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class FinalOps {
    // Per-object-print type: used ONLY in single-death-per-round probes.
    class TrackA {
        public static int Finalized;
        public readonly int Id;
        public TrackA(int id) { Id = id; }
        ~TrackA() {
            Console.WriteLine("fa " + Id);
            Finalized++;
        }
    }
    // Second registered finalizable type (registry must hold both).
    class TrackB {
        public static int Finalized;
        public readonly int Id;
        public TrackB(int id) { Id = id; }
        ~TrackB() {
            Console.WriteLine("fb " + Id);
            Finalized++;
        }
    }
    // Aggregate-only type for the multi-death probe (no per-object print ->
    // the LIFO-vs-FIFO order difference is invisible; only the count matters).
    // ctor stores Id (a side effect) so the transpiler does NOT DCE the
    // discarded allocation (see GAPS.md gap 1).
    class SilentTrack {
        public static int Finalized;
        public readonly int Id;
        public SilentTrack(int id) { Id = id; }
        ~SilentTrack() { Finalized++; }
    }
    // Derived finalizer chain: ~DerivedFin() body ends with base.Finalize().
    class BaseFin {
        public static int BaseFinalized;
        ~BaseFin() { Console.WriteLine("fbase"); BaseFinalized++; }
    }
    class DerivedFin : BaseFin {
        public static int DerivedFinalized;
        ~DerivedFin() { Console.WriteLine("fder"); DerivedFinalized++; }
    }
    // Control: no finalizer, no registry entry.
    class NoFin { public int V; }

    // ---- helper allocators: the object dies when the helper's frame pops ----
    static void MakeDeadA(int id) { new TrackA(id); }
    static void MakeDeadB(int id) { new TrackB(id); }
    static void MakeDeadSilentOne(int id) { new SilentTrack(id); }
    static void MakeDeadDerived() { new DerivedFin(); }
    static WeakReference DeadRef() { return new WeakReference(new object()); }
    static WeakReference DeadFinalizableRef(int id) { return new WeakReference(new TrackA(id)); }

    public static void Main() {
        // ---- A. WeakReference live target survives a collection ----
        object o = new object();
        WeakReference wr = new WeakReference(o);
        GC.Collect();
        Console.WriteLine(wr.IsAlive);        // True
        Console.WriteLine(wr.Target == o);    // True (identity)
        GC.KeepAlive(o);

        // ---- B. WeakReference dead target (created in a helper -> dies) ----
        WeakReference wd = DeadRef();
        GC.Collect();
        Console.WriteLine(wd.IsAlive);        // False

        // ---- C. single finalizable object dies -> runs once, exactly ----
        MakeDeadA(7);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(TrackA.Finalized);  // 1
        // second full collect must NOT re-run it (two-collection semantics)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(TrackA.Finalized);  // 1 (still exactly once)

        // ---- D. a SECOND finalizable type (registry holds both) ----
        MakeDeadB(9);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(TrackB.Finalized);  // 1

        // ---- E. N instances of the same type die in ONE collection ----
        // 3 SilentTrack objects die in the same helper round -> count 3,
        // regardless of the implementation's finalizer queue order.
        MakeDeadSilentOne(1);
        MakeDeadSilentOne(2);
        MakeDeadSilentOne(3);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(SilentTrack.Finalized); // 3

        // ---- F. derived finalizer chain: derived then base ----
        MakeDeadDerived();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(DerivedFin.DerivedFinalized); // 1
        Console.WriteLine(BaseFin.BaseFinalized);       // 1 (via base call)
        // chain runs exactly once per death
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(DerivedFin.DerivedFinalized); // 1
        Console.WriteLine(BaseFin.BaseFinalized);       // 1

        // ---- G. finalizer + WeakReference interplay ----
        WeakReference wf = DeadFinalizableRef(21);  // target dies in helper
        GC.Collect();                    // TrackA dies, finalizer queued+run
        GC.WaitForPendingFinalizers();
        Console.WriteLine(wf.IsAlive);   // False (dead AND finalized)
        Console.WriteLine(TrackA.Finalized); // 2 (7's round + 21)

        // ---- H. KeepAlive root: a LIVE object's finalizer does NOT run ----
        // Uses SilentTrack (silent finalizer): the object stays reachable on
        // BOTH runtimes. On net48, a local's liveness extends to its LAST USE
        // -- so `held.Id` is read AFTER every collect to pin it through that
        // collect (net48's precise liveness would otherwise drop it after its
        // previous use and reclaim it mid-probe). On ILC the local is a frame
        // root for the whole method regardless. The SILENT finalizer makes the
        // process-exit difference unobservable (net48 runs it at exit, ILC
        // never; neither prints), keeping the output byte-stable.
        SilentTrack held = new SilentTrack(42);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(SilentTrack.Finalized); // 3 (held alive; 3 from E)
        Console.WriteLine(held.Id == 42);         // True (alive, field readable)
        GC.Collect();                             // second collect
        GC.WaitForPendingFinalizers();
        Console.WriteLine(held.Id == 42);         // True (pinned past 2nd collect)
        Console.WriteLine(SilentTrack.Finalized); // 3 (still pinned)
        GC.KeepAlive(held);

        // ---- I. non-finalizable control type: no registry, no callback ----
        new NoFin();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(TrackA.Finalized); // 3 (NoFin has no finalizer)

        // ---- J. GC stats (stable bool/compare probes, gcops convention) ----
        Console.WriteLine(GC.GetTotalMemory(false) > 0); // True (primed)
        object live = new object();
        Console.WriteLine(GC.GetGeneration(live) == 0);  // True (fresh gen0)
        GC.Collect();
        Console.WriteLine(GC.GetGeneration(live) > 0);   // True (promoted)
        GC.KeepAlive(live);
    }
}}
