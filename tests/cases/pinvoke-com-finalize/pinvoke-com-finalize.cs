//------------------------------------------------------------------------------
// pinvoke-com-finalize.cs -- the RCW GC finalizer: a dropped RCW must release
// its native reference at collection time (net48 semantics), exactly ONCE.
//
// WHY THIS CASE EXISTS: `out`/`ref` COM P/Invoke parameters create a NEW RCW
// for the pointer the callee produced. net48 releases that native reference
// when the wrapper is collected; without a finalizer every such call leaks one
// native reference. The runtime now arms a type-level finalizer on the RCW
// class, so an RCW dropped without Marshal.ReleaseComObject is released by the
// GC. This case pins the observable ledger.
//
// MEASURED net48 contract (oracle: .opencode/tmp/com-finalize/oracle/c.cs):
//   * Marshal.GetTypedObjectForIUnknown(p, typeof(object)) adds ONE reference
//     (raw refs 1 -> 2: ours + the wrapper's), on net48 AND in this runtime;
//     Marshal.GetUniqueObjectForIUnknown(p) does the same but creates a wrapper
//     that is NOT in the identity cache;
//   * dropping the wrapper + GC.Collect(); GC.WaitForPendingFinalizers();
//     GC.Collect(); releases the wrapper's reference (refs 2 -> 1). The RELEASE
//     IS DONE BY THE GC, not by CleanupUnusedObjectsInCurrentContext (which
//     changes nothing after the fact, and nothing at all while the wrapper is
//     still alive);
//   * an explicit ReleaseComObject followed by a GC does NOT release again;
//   * GC.SuppressFinalize(rcw) does NOT prevent the native release on net48
//     (measured; the records are not part of this case because this runtime's
//     GC.SuppressFinalize is a documented no-op and the observable end state is
//     identical anyway);
//   * the null forms of the RCW-creation APIs throw ArgumentNullException.
//
// The fixture (comfinalizeprobe) counts AddRef/Release and returns the live
// reference count; its Release does NOT free the object, so an erroneous second
// Release is observable as refs 0 instead of 1 (never a crash).
//
// The wrapper is stored in a STATIC field so dropping it is a real root clear
// (`s_rcw = null;`): a local would be a temp-slot root.
//
// GC TIMING, pinned identically on both sides:
//     s_rcw = null; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
// The first collection moves the dead RCW to "pending finalization", the wait
// runs the finalizer, and the second collection reclaims the block.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("00000000-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFinalizeUnknown
{
    void RemoteRead();
}

public static class PInvokeComFinalize
{
    // The wrapper under test. A static field is a real GC root slot, so
    // `s_rcw = null` genuinely makes the wrapper unreachable.
    private static object s_rcw;

    [DllImport("comfinalizeprobe.dll")]
    private static extern IntPtr fin_new_raw(int slot);

    [DllImport("comfinalizeprobe.dll")]
    private static extern int fin_refs(IntPtr p);

    private static void CollectTwice()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Rec(string id, int value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static void Rec(string id, string value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    public static void Main()
    {
        // ---- P0: explicit ReleaseComObject, then GC / cleanup. Records: no
        //          second release, and an already-released RCW is safe to
        //          finalize ------------------------------------------------
        IntPtr p0 = fin_new_raw(0);
        s_rcw = Marshal.GetTypedObjectForIUnknown(p0, typeof(object));
        Rec("p0.wrap", fin_refs(p0));
        Rec("p0.iscom", Marshal.IsComObject(s_rcw) ? 1 : 0);
        Marshal.ReleaseComObject(s_rcw);
        Rec("p0.released", fin_refs(p0));
        s_rcw = null;
        CollectTwice();
        Rec("p0.gc", fin_refs(p0));
        Marshal.CleanupUnusedObjectsInCurrentContext();
        Rec("p0.cleanup", fin_refs(p0));
        Rec("p0.release_ours", Marshal.Release(p0));

        // ---- P1: a cached RCW dropped WITHOUT ReleaseComObject: the GC
        //          releases its reference (THE finalizer record), and a later
        //          CleanupUnusedObjectsInCurrentContext must not release a
        //          second time ---------------------------------------------
        IntPtr p1 = fin_new_raw(1);
        s_rcw = Marshal.GetTypedObjectForIUnknown(p1, typeof(object));
        Rec("p1.wrap", fin_refs(p1));
        s_rcw = null;
        CollectTwice();
        Rec("p1.gc", fin_refs(p1));
        Marshal.CleanupUnusedObjectsInCurrentContext();
        Rec("p1.cleanup", fin_refs(p1));
        Rec("p1.release_ours", Marshal.Release(p1));

        // ---- P2: an RCW that is NOT in the identity cache (GetUniqueObject)
        //          has the same lifetime: the finalizer must release it even
        //          though it has no cache entry to detach ------------------
        IntPtr p2 = fin_new_raw(2);
        s_rcw = Marshal.GetUniqueObjectForIUnknown(p2);
        Rec("p2.wrap", fin_refs(p2));
        Rec("p2.iscom", Marshal.IsComObject(s_rcw) ? 1 : 0);
        s_rcw = null;
        CollectTwice();
        Rec("p2.gc", fin_refs(p2));
        Marshal.CleanupUnusedObjectsInCurrentContext();
        Rec("p2.cleanup", fin_refs(p2));
        Rec("p2.release_ours", Marshal.Release(p2));

        // ---- P3: a LIVE RCW is rooted by a static field: two full
        //          collections plus a cleanup must NOT release it (the
        //          finalizer only fires for unreachable wrappers; net48 M8
        //          oracle: refs stay at 2) --------------------------------
        IntPtr p3 = fin_new_raw(3);
        s_rcw = Marshal.GetTypedObjectForIUnknown(p3, typeof(object));
        Rec("p3.wrap", fin_refs(p3));
        CollectTwice();
        Rec("p3.gc1", fin_refs(p3));
        CollectTwice();
        Rec("p3.gc2", fin_refs(p3));
        Marshal.CleanupUnusedObjectsInCurrentContext();
        Rec("p3.cleanup", fin_refs(p3));
        Rec("p3.iscom", Marshal.IsComObject(s_rcw) ? 1 : 0);
        Marshal.ReleaseComObject(s_rcw);
        Rec("p3.after_releasecom", fin_refs(p3));
        s_rcw = null;
        CollectTwice();
        Rec("p3.release_ours", Marshal.Release(p3));

        // ---- P4: idempotence -- a finalized RCW survives repeated cleanup
        //          and another collection without a second Release (net48 M9
        //          oracle: refs stay at 1) ---------------------------------
        IntPtr p4 = fin_new_raw(0);
        s_rcw = Marshal.GetTypedObjectForIUnknown(p4, typeof(object));
        s_rcw = null;
        CollectTwice();
        Rec("p4.gc", fin_refs(p4));
        Marshal.CleanupUnusedObjectsInCurrentContext();
        Rec("p4.cleanup1", fin_refs(p4));
        Marshal.CleanupUnusedObjectsInCurrentContext();
        Rec("p4.cleanup2", fin_refs(p4));
        CollectTwice();
        Rec("p4.gc2", fin_refs(p4));
        Rec("p4.release_ours", Marshal.Release(p4));

        // ---- P5: null safety on the RCW-creation surface (both sides throw
        //          ArgumentNullException) ------------------------------------
        try
        {
            Marshal.GetTypedObjectForIUnknown(IntPtr.Zero, typeof(object));
            Rec("p5.typed.null", "no-throw");
        }
        catch (Exception e)
        {
            Rec("p5.typed.null", e.GetType().Name);
        }
    }
}
