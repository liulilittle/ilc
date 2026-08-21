//------------------------------------------------------------------------------
// gcops.cs -- System.GC DEEP e2e reference case (net48 oracle).
//
// Deterministic STABLE comparisons only: GetTotalMemory raw bytes differ run
// to run and between net48 and the ILC heap, so every memory probe is a bool
// comparison (primed > 0, monotonic increase with a rooted allocation,
// generation promotion) -- the gccase convention. Priming matters: a net48
// process enters Main with JIT/loader allocations already live, while the
// ILC heap is empty at entry, so every comparison is made AFTER a managed
// allocation so both heaps are non-empty.
//
// WIRED surface (MethodMap.GC.cs + il2cpp_gc_api.h / gcext.cpp):
//   GC.Collect() / GC.Collect(int)  -- full collection (the gen arg drops)
//   GC.GetTotalMemory(bool)         -- live-block bytes (real value, snapshot)
//   GC.GetGeneration(obj)           -- 0/1/2/3; full collect promotes
//   GC.KeepAlive(obj)               -- no-op root
//   GC.SuppressFinalize / GC.ReRegisterForFinalize -- placeholder no-ops
//   System.WeakReference ctor/IsAlive/Target/set_Target -- object model wired
//     (MethodMap.WeakReference.cs; weakrefcase passes)
//
// RECORDED GAPS (parked here -- each would break the transpile/build or the
// diff):
//   * GC.MaxGeneration              -- get_MaxGeneration has NO MethodMap.GC.cs
//     case -> generated-name undefined symbol -> link failure.
//   * GC.WaitForPendingFinalizers + finalizer COUNT -- unwired on the GC
//     domain (MethodMap.WeakReference.cs:25). A ~Tracked() destructor with a
//     static counter needs WaitForPendingFinalizers to observe the count
//     deterministically on net48; the symbol is undefined -> link failure
//     (gcext gen/Cases_Gcext.cpp:105 proves the generated name).
//   * GC.GetAllocatedBytesForCurrentThread() -- returns 0 on the ILC runtime
//     (il2cpp_gc_api.h KNOWN GAP) while net48 reports real bytes -> the raw
//     value can never diff; also the MethodMap case name does not match the
//     C# member ("GetAllocatedBytes" vs "...ForCurrentThread").
//
// Every active line uses ONLY the wired surface; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class GcOps {
    // A target that dies as soon as this method returns: no reachable
    // reference escapes, so the next full collection reclaims it
    // deterministically (the weakrefcase DeadTarget pattern).
    static WeakReference DeadTarget()
    {
        return new WeakReference(new object());
    }

    public static void Main() {
        // ---- priming (both heaps non-empty before any comparison) ----
        object o = new object();
        Console.WriteLine(GC.GetTotalMemory(false) > 0);   // True (primed)

        // ---- generation: fresh object is gen 0 ----
        Console.WriteLine(GC.GetGeneration(o) == 0);       // True

        // ---- Collect() full collection promotes survivors ----
        GC.Collect();
        Console.WriteLine(GC.GetTotalMemory(false) > 0);   // True (heap not empty)
        Console.WriteLine(GC.GetGeneration(o) > 0);        // True (promoted)

        // ---- Collect(int) overloads (gen arg dropped -> full collect) ----
        GC.Collect(0);
        Console.WriteLine(GC.GetGeneration(o) > 0);        // True
        GC.Collect(1);
        GC.Collect(2);
        Console.WriteLine(GC.GetTotalMemory(false) > 0);   // True

        // ---- GetTotalMemory threshold: a rooted allocation raises the total ----
        long m0 = GC.GetTotalMemory(false);
        byte[] big = new byte[1024 * 1024];                // 1 MiB live allocation
        long m1 = GC.GetTotalMemory(false);
        Console.WriteLine(m1 > m0);                        // True (monotonic with live alloc)
        Console.WriteLine(GC.GetTotalMemory(false) > 0);   // True

        // ---- SuppressFinalize / ReRegisterForFinalize no-ops, object stays live ----
        GC.SuppressFinalize(o);
        Console.WriteLine(GC.GetGeneration(o) > 0);        // True (still promoted/live)
        GC.ReRegisterForFinalize(o);
        Console.WriteLine(GC.GetGeneration(o) >= 0);       // True (still reachable)
        GC.KeepAlive(big);
        GC.KeepAlive(o);

        // ---- WeakReference: live target survives a full collection ----
        object live = new object();
        WeakReference wl = new WeakReference(live);
        GC.Collect();
        Console.WriteLine(wl.IsAlive);                     // True
        Console.WriteLine(wl.Target == live);              // True
        GC.KeepAlive(live);

        // ---- WeakReference: dead target reclaimed at the next full collection ----
        WeakReference wd = DeadTarget();
        GC.Collect();
        Console.WriteLine(wd.IsAlive);                     // False
        Console.WriteLine(wd.Target == null);              // True

        // ---- WeakReference: set_Target re-arms a reclaimed reference ----
        object o2 = new object();
        wd.Target = o2;
        Console.WriteLine(wd.IsAlive);                     // True
        Console.WriteLine(wd.Target == o2);                // True
        wd.Target = null;
        GC.Collect();
        Console.WriteLine(wd.IsAlive);                     // False
        GC.KeepAlive(o2);
    }
}}
