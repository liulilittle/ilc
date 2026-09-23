//------------------------------------------------------------------------------
// ipaudit4.cs -- WeakReference target-death probe (G6 audit J).
//
// net48 x64 reference: both short and long (trackResurrection=true) weak
// references report IsAlive=False after the target is dropped, the local is
// nulled (short case) and two full GC.Collect + WaitForPendingFinalizers
// rounds run.
//
// The ILC runtime (gcext.cpp -- handle-table weak model, the collector clears
// the weak slot when the referent dies) documents a known divergence: an
// object passed to a WeakReference ctor from Main lands in a frame-rooted
// temp and never dies on ILC while net48 collects it (finalops GAPS gap 2),
// so the second probe's True would be the evidence of that divergence. The
// trackResurrection flag itself is stored but not modelled
// (icall_weakref_ctor_track, gcext.cpp:269-276).
//
// net48 x64 reference output:
//   short=False
//   long=False
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class IpAudit4 {
    public static void Main() {
        object o = new object();
        WeakReference w = new WeakReference(o);
        o = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Console.WriteLine("short=" + w.IsAlive);

        WeakReference w2 = new WeakReference(new object(), true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Console.WriteLine("long=" + w2.IsAlive);
    }
}}
