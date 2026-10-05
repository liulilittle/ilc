//------------------------------------------------------------------------------
// finalops2.cs -- FINALIZER DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C17 (2026-08-10).
//
// DEEPENS finalops (C13) on the type-level finalizer registry (gc.cpp
// g_finalizerTypes + AllocCore auto-arm, closed in batch 40). finalops
// proved: multiple finalizable types, N instances of ONE type dying in one
// collection (aggregate only), a 2-level derived->base chain, finalizer +
// WeakReference interplay, one GC.KeepAlive root. finalops2 drives the
// CHAIN further:
//
//   * MULTI-TYPE FINALIZE ORDER: three finalizable types (OrdA/OrdB/OrdC)
//     each dying in its OWN collection round -> per-object prints are
//     deterministic (one death per round), proving the registry dispatches
//     every registered type's finalizer to the right instance, in the right
//     round. The SAME type across DISTINCT instances (round 2 of OrdA) is
//     armed independently per allocation (counter moves per instance).
//     Simultaneous deaths of DIFFERENT types are aggregate-only (LIFO-vs-FIFO
//     queue order is implementation-specific -- the byte-stable probe is the
//     sum, exactly like finalops E).
//   * THREE-LEVEL DERIVED->BASE CHAIN: ~Leaf() -> base.Finalize() (~Mid()) ->
//     base.Finalize() (~GrandBase()): the generated Finalize body chains the
//     base calls, so the net48 destructor order (leaf, mid, base) is
//     reproduced and each level's counter moves exactly once per death.
//   * RESURRECTION CHAIN: a finalizer that makes its object reachable again
//     (storing `this` into a LIVE holder object's instance field) is honored
//     -- the object survives (holder.Held != null), and the finalizer does
//     NOT re-run on the next collection (.NET resurrection semantics; the
//     gc.cpp two-collection model documents the same). NOTE (probe-verified
//     on net48): a System.WeakReference does NOT track resurrection -- its
//     target is cleared at the FIRST death sighting, BEFORE the finalizer
//     runs, so wrs.IsAlive reads False even though the object is alive again
//     (the resurrection is observed through holder.Held and the exactly-once
//     counter, not through the weakref). The static-field resurrection shape
//     (store this into a STATIC reference field) is PARKED -- GAP-FIN-4
//     (static ref fields are not GC roots on ILC) makes net48 keep the object
//     alive (True) while ILC reclaims it (False): recorded as DEFECT D-FIN2-1.
//   * KEEPALIVE BOUNDARY: (a) a silent survivor pinned past THREE collections
//     with GC.KeepAlive never finalizes; (b) explicit release (`held = null`)
//     makes the object die and its silent finalizer run EXACTLY once (the
//     counter moves 1); (c) a WeakReference target's lifetime boundary -- a
//     live local target stays alive across a collect, then nulling the local
//     makes the weakref read False on the NEXT collect (net48 precise
//     liveness is pinned by a Target==keep read and the KeepAlive use itself).
//
// LIVERESS DESIGN RULE (gcext/finalops pattern): every object that must DIE
// is allocated INSIDE a helper whose frame pops before the next collection
// (transpiler roots only frame-scoped temps; the object is unreachable the
// moment the helper returns -- identical on net48). An object created in
// Main would land in a frame-rooted temp and never die (GAP-FIN-2), so the
// helpers take the id/context ints and allocate the dead object themselves.
// Objects that must SURVIVE keep a LOCAL read after every collect (net48
// precise-liveness parity) and are extended with GC.KeepAlive. ALL
// surviving-object types use SILENT finalizers: net48 runs a still-pinned
// finalizer at process exit while ILC never does, so a printing finalizer on
// a survivor would diverge.
//
// WIRED SURFACE (finalops): GC.Collect / GC.WaitForPendingFinalizers /
// GC.KeepAlive / System.WeakReference ctor / IsAlive / Target / user
// ~Finalize() destructors (type-level registry).
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class FinalOps2 {
    // ---- per-object-print finalizable types (single-death rounds only) ----
    class OrdA {
        public static int Finalized;
        public readonly int Id;
        public OrdA(int id) { Id = id; }
        ~OrdA() { Console.WriteLine("oa " + Id); Finalized++; }
    }
    class OrdB {
        public static int Finalized;
        public readonly int Id;
        public OrdB(int id) { Id = id; }
        ~OrdB() { Console.WriteLine("ob " + Id); Finalized++; }
    }
    class OrdC {
        public static int Finalized;
        public readonly int Id;
        public OrdC(int id) { Id = id; }
        ~OrdC() { Console.WriteLine("oc " + Id); Finalized++; }
    }
    // ---- SILENT finalizable types (multi-death rounds + survivors) ----
    class SilentA {
        public static int Finalized;
        public readonly int Id;
        public SilentA(int id) { Id = id; }
        ~SilentA() { Finalized++; }
    }
    class SilentB {
        public static int Finalized;
        public readonly int Id;
        public SilentB(int id) { Id = id; }
        ~SilentB() { Finalized++; }
    }
    class SilentC {
        public static int Finalized;
        public readonly int Id;
        public SilentC(int id) { Id = id; }
        ~SilentC() { Finalized++; }
    }
    // ---- three-level derived->base finalizer chain ----
    class GrandBase {
        public static int BaseFinalized;
        ~GrandBase() { Console.WriteLine("cb"); BaseFinalized++; }
    }
    class MidChain : GrandBase {
        public static int MidFinalized;
        ~MidChain() { Console.WriteLine("cm"); MidFinalized++; }
    }
    class LeafChain : MidChain {
        public static int LeafFinalized;
        ~LeafChain() { Console.WriteLine("cl"); LeafFinalized++; }
    }
    // ---- resurrection types ----
    // ResurrectA's finalizer stores `this` into a LIVE holder's instance
    // field (a real GC-scanned ref slot). The holder is a Main LOCAL (alive
    // for the whole probe -- never a static field, GAP-FIN-4). The
    // resurrecting object keeps its own link to the holder in the `Host`
    // instance field so the finalizer body (which runs against the retained,
    // not-yet-freed block) can reach it. Silent counter + ONE marker print
    // (deterministic: single death per round, fired at the
    // WaitForPendingFinalizers point).
    class ResHolder { public object Held; }
    class ResurrectA {
        public static int Finalized;
        public readonly object Host;   // link to the live holder (set in ctor)
        public ResurrectA(int id, object host) { Host = host; }
        ~ResurrectA() { Console.WriteLine("rr"); ResurrectA.Finalized++; ((ResHolder)Host).Held = this; }
    }
    // Static-field resurrection: the finalizer stores `this` into a STATIC
    // reference field (net48 static root; ILC static ref fields are NOT GC
    // roots -- GAP-FIN-4). Probe parked in Main (see D-FIN2-1).
    class ResurrectS {
        public static int Finalized;
        public static object Survivor;
        ~ResurrectS() { ResurrectS.Survivor = this; Finalized++; }
    }
    // ---- silent survivor type for KeepAlive-boundary probes ----
    class SilentKeep {
        public static int Finalized;
        public readonly int Id;
        public SilentKeep(int id) { Id = id; }
        ~SilentKeep() { Finalized++; }
    }

    // ---- helpers: the dead object is allocated INSIDE the helper (its
    // frame pops -> the object is unreachable before the next collect) ----
    static void MakeDeadA(int id) { new OrdA(id); }
    static void MakeDeadB(int id) { new OrdB(id); }
    static void MakeDeadC(int id) { new OrdC(id); }
    static void MakeDeadSA(int id) { new SilentA(id); }
    static void MakeDeadSB(int id) { new SilentB(id); }
    static void MakeDeadSC(int id) { new SilentC(id); }
    static void MakeDeadChain() { new LeafChain(); }
    static WeakReference ResurrectRef(int id, object host) { return new WeakReference(new ResurrectA(id, host)); }
    static void MakeDeadKeep(int id) { new SilentKeep(id); }

    public static void Main() {
        // ---- A. multi-type finalize order: one death per round, per-object
        // prints deterministic -> the registry dispatches each type's own
        // finalizer to the right instance in the right round ----
        MakeDeadA(1);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("a1=" + OrdA.Finalized);         // "oa 1" then 1
        MakeDeadB(2);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("b2=" + OrdB.Finalized);         // "ob 2" then 1
        MakeDeadC(3);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("c3=" + OrdC.Finalized);         // "oc 3" then 1

        // ---- B. same type, DISTINCT instances across rounds (per-instance
        // arming: the registry arms each allocation independently) ----
        MakeDeadA(4);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("a4=" + OrdA.Finalized);         // "oa 4" then 2
        MakeDeadA(5);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("a5=" + OrdA.Finalized);         // "oa 5" then 3

        // ---- C. three-level derived->base chain (net48 destructor order:
        // leaf, mid, base) ----
        MakeDeadChain();
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("cl_leaf=" + LeafChain.LeafFinalized);   // "cl" "cm" "cb"
        Console.WriteLine("cl_mid=" + MidChain.MidFinalized);      // 1 each
        Console.WriteLine("cl_base=" + GrandBase.BaseFinalized);
        GC.Collect(); GC.WaitForPendingFinalizers();               // exactly once
        Console.WriteLine("cl_leaf2=" + LeafChain.LeafFinalized);  // still 1
        Console.WriteLine("cl_mid2=" + MidChain.MidFinalized);
        Console.WriteLine("cl_base2=" + GrandBase.BaseFinalized);

        // ---- D. simultaneous deaths of DIFFERENT types (aggregate only) ----
        // 2 SilentA + 2 SilentB + 2 SilentC die in ONE round -> each counter 2.
        // No per-object prints (queue order LIFO-vs-FIFO is impl-specific).
        MakeDeadSA(10); MakeDeadSA(11);
        MakeDeadSB(12); MakeDeadSB(13);
        MakeDeadSC(14); MakeDeadSC(15);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("sa=" + SilentA.Finalized);      // 2
        Console.WriteLine("sb=" + SilentB.Finalized);      // 2
        Console.WriteLine("sc=" + SilentC.Finalized);      // 2
        // a SECOND full collect must NOT re-run any of them (two-collection
        // semantics, finalops C parity): counters stay.
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("sa2=" + SilentA.Finalized);     // 2
        Console.WriteLine("sb2=" + SilentB.Finalized);     // 2
        Console.WriteLine("sc2=" + SilentC.Finalized);     // 2

        // ---- E. resurrection chain via a LIVE holder's instance field ----
        // The target dies in the helper; the finalizer runs (prints "rr"),
        // stores this into holder.Held -> the object is reachable again.
        // net48 probe: a WeakReference is cleared at the FIRST death sighting
        // (it does NOT track resurrection), so IsAlive reads False even while
        // the resurrection itself is proven by rr_held=True and the
        // exactly-once counter (rr_n2 stays 1 -- no re-run on net48).
        ResHolder holder = new ResHolder();
        WeakReference wrs = ResurrectRef(7, holder);
        GC.Collect();
        GC.WaitForPendingFinalizers();                     // finalizer runs here
        Console.WriteLine("rr_alive=" + wrs.IsAlive);      // False (weakref untracked)
        Console.WriteLine("rr_n=" + ResurrectA.Finalized); // 1
        Console.WriteLine("rr_held=" + !ReferenceEquals(holder.Held, null));
        GC.Collect(); GC.WaitForPendingFinalizers();       // no re-run
        Console.WriteLine("rr_alive2=" + wrs.IsAlive);     // False (stays untracked)
        Console.WriteLine("rr_n2=" + ResurrectA.Finalized);// 1 (exactly once)
        GC.KeepAlive(holder);

        // ---- F. KEEPALIVE BOUNDARY: pin a silent survivor past 3 collects,
        // then release -> its finalizer runs exactly once ----
        MakeDeadKeep(20);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("kd_pre=" + SilentKeep.Finalized); // 1 (helper death)
        SilentKeep held = new SilentKeep(21);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("kd_1=" + (held.Id == 21));        // True (pinned)
        Console.WriteLine("kd_n1=" + SilentKeep.Finalized);  // 1
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("kd_2=" + (held.Id == 21));        // True (2nd collect)
        Console.WriteLine("kd_n2=" + SilentKeep.Finalized);  // 1
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("kd_3=" + (held.Id == 21));        // True (3rd collect)
        Console.WriteLine("kd_n3=" + SilentKeep.Finalized);  // 1
        GC.KeepAlive(held);
        held = null;                                         // explicit release
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("kd_released=" + SilentKeep.Finalized); // 2 (ran once)
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("kd_released2=" + SilentKeep.Finalized); // 2 (once only)

        // ---- G. WeakReference target lifetime boundary ----
        object keep = new object();
        WeakReference wkp = new WeakReference(keep);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("wk_live=" + (wkp.Target == keep)); // True (pinned by use)
        GC.KeepAlive(keep);
        keep = null;
        GC.Collect(); GC.WaitForPendingFinalizers();
        Console.WriteLine("wk_dead=" + !wkp.IsAlive);         // True (released)

        // ---- H. PARKED DEFECT D-FIN2-1 (see GAPS.md): static-field
        // resurrection. net48 keeps the object alive via the static root;
        // ILC static reference fields are not GC roots -> the object is
        // reclaimed and wss.IsAlive reads False (divergence). Not active.
        //   WeakReference wss = MakeDeadResurrectS();
        //   GC.Collect(); GC.WaitForPendingFinalizers();
        //   Console.WriteLine(wss.IsAlive);   // net48 True / ILC False
        //   GC.KeepAlive(ResurrectS.Survivor);
    }
}}
