//------------------------------------------------------------------------------
// pinvoke-com-outref.cs -- `out` / `ref` [ComImport]-interface P/Invoke
// parameters: the RCW WRITE-BACK and the reference-ownership contract.
//
// THE #1 HAZARD THIS CASE GUARDS: a `ref` COM parameter must NEVER Release the
// old RCW from managed code. The reference the callee receives is one the
// marshaller hands over, so the callee may legitimately consume it; the RCW
// keeps its OWN separate reference and its finalizer releases that one.
// Releasing the RCW's pointer in the write-back is a DOUBLE RELEASE.
//
// MEASURED net48 contract (phase-0 oracle, .opencode/tmp/com-outref-oracle,
// records R1-R5 / O1-O12):
//   * `out`: the callee's pointer becomes a live RCW whose teardown returns
//     the object to its pre-wrapper reference count (O2 == the shipped F5:
//     refs 2 -> ReleaseComObject -> 0 for an object that was fully handed
//     over). Our adopt is icall_com_get_object_for_iunknown (WrapUnknown, no
//     AddRef), so the wrapper owns exactly the handed reference.
//   * `ref`, no replacement: the SAME managed wrapper survives (O4 / O10
//     same_wrapper = True) and the argument side is a +1/-1 pair (R1:
//     callee entry = caller + 1, post-call = caller).
//   * `ref`, replaced and the callee honours the COM contract (releases the
//     old): the old object lands back on its own baseline (O6 / R3).
//   * `ref`, replaced and the callee does NOT release: the old stays at
//     baseline + 1 (O5 / shipped F16) -- net48 leaves the caller's callee bug
//     in place and so must we.
//   * a NULL written by the callee (`out` or `ref`) becomes a null managed
//     reference and never throws (O3 / O7); a null IN value reaches the
//     callee as NULL (O8).
//
// EVERY RECORD IS A DELTA OR A BOOLEAN on purpose: coreclr's own QI probes and
// its GetObjectForIUnknown policy make ABSOLUTE reference counts differ from
// this runtime (measured divergence, com-verify NOTES section 6), while the
// END STATE and every delta agree. Absolute values here would make the oracle
// and the runtime disagree for a reason that is not a defect.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("00000000-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOutRefCom
{
    void RemoteRead();
}

public static class PInvokeComOutRef
{
    [DllImport("comoutrefprobe.dll")]
    private static extern IntPtr or_new();

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_out_new(out IOutRefCom pp);

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_out_null(out IOutRefCom pp);

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_ref_noop(ref IOutRefCom pp);

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_ref_replace(ref IOutRefCom pp);

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_ref_replace_owner(ref IOutRefCom pp);

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_ref_null(ref IOutRefCom pp);

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_refs(IntPtr p);

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_id(IntPtr p);

    [DllImport("comoutrefprobe.dll")]
    private static extern IntPtr or_handed();

    [DllImport("comoutrefprobe.dll")]
    private static extern IntPtr or_seen();

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_entry_refs();

    [DllImport("comoutrefprobe.dll")]
    private static extern int or_release_ptr(IntPtr p);

    private static void Rec(string id, int value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static int Bool(bool b)
    {
        return b ? 1 : 0;
    }

    public static void Main()
    {
        //==================================================================
        // 1. `out`: the callee creates the object and hands over the ONLY
        //    reference. The wrapper must ADOPT it -- so one
        //    ReleaseComObject returns the object to zero. A wrap that AddRef'd
        //    instead would leave 1 (the mutation this record catches).
        //==================================================================
        IOutRefCom o1;
        Rec("out.new.hr", or_out_new(out o1));
        IntPtr p1 = or_handed();
        Rec("out.new.is_com", Bool(o1 != null && Marshal.IsComObject(o1)));
        Marshal.ReleaseComObject(o1);
        Rec("out.new.refs_after_rc", or_refs(p1));

        //==================================================================
        // 2. `out` with a callee-written NULL: a null managed reference, no
        //    exception (measured O3).
        //==================================================================
        IOutRefCom o2 = null;
        Rec("out.null.hr", or_out_null(out o2));
        Rec("out.null.is_null", Bool(o2 == null));

        //==================================================================
        // 3. Two consecutive `out`s must produce INDEPENDENT wrappers, each
        //    owning its own reference (releasing one must not disturb the
        //    other).
        //==================================================================
        IOutRefCom a3, b3;
        or_out_new(out a3);
        IntPtr pa3 = or_handed();
        or_out_new(out b3);
        IntPtr pb3 = or_handed();
        Rec("out.two.distinct", Bool(!ReferenceEquals(a3, b3)));
        Marshal.ReleaseComObject(a3);
        Rec("out.two.first_rc_refs", or_refs(pa3));
        Rec("out.two.second_untouched", or_refs(pb3) > 0 ? 1 : 0);
        Marshal.ReleaseComObject(b3);
        Rec("out.two.second_rc_refs", or_refs(pb3));

        //==================================================================
        // 4. `ref`, the callee does NOT replace: the argument side is a
        //    +1/-1 pair, the managed variable keeps the SAME wrapper, and the
        //    post-call count returns to the pre-call count.
        //==================================================================
        IntPtr p4 = or_new();
        object w4 = Marshal.GetObjectForIUnknown(p4);
        IOutRefCom r4 = (IOutRefCom)w4;
        int base4 = or_refs(p4);
        or_ref_noop(ref r4);
        Rec("ref.noop.entry_delta", or_entry_refs() - base4);
        Rec("ref.noop.after_delta", or_refs(p4) - base4);
        Rec("ref.noop.same_wrapper", Bool(ReferenceEquals(r4, w4)));

        //==================================================================
        // 5. `ref`, the callee REPLACES the pointer and does NOT release the
        //    old (a contract violation net48 also leaves in place). The old
        //    object keeps exactly the ONE reference the marshaller handed the
        //    callee -- managed code must NOT release it on top of that.
        //    A double release here is the mutation this record catches.
        //==================================================================
        IntPtr p5 = or_new();
        object w5 = Marshal.GetObjectForIUnknown(p5);
        IOutRefCom r5 = (IOutRefCom)w5;
        int base5 = or_refs(p5);
        int oldId5 = or_id(p5);
        or_ref_replace(ref r5);
        Rec("ref.replace.old_delta", or_refs(p5) - base5);
        Rec("ref.replace.callee_saw_old", Bool(or_seen() == p5));
        Rec("ref.replace.wrapper_replaced", Bool(!ReferenceEquals(r5, w5)));
        Rec("ref.replace.new_id_differs", Bool(or_id(or_handed()) != oldId5));
        Rec("ref.replace.new_is_com", Bool(r5 != null && Marshal.IsComObject(r5)));
        Marshal.ReleaseComObject(r5);
        Rec("ref.replace.new_refs_after_rc", or_refs(or_handed()));

        //==================================================================
        // 6. `ref`, the callee REPLACES and RELEASES the old (the correct COM
        //    contract). The old must land back on its OWN baseline: if managed
        //    code released it too the count would drop below it.
        //==================================================================
        IntPtr p6 = or_new();
        object w6 = Marshal.GetObjectForIUnknown(p6);
        IOutRefCom r6 = (IOutRefCom)w6;
        int base6 = or_refs(p6);
        or_ref_replace_owner(ref r6);
        Rec("ref.replace_owner.old_delta", or_refs(p6) - base6);
        Rec("ref.replace_owner.wrapper_replaced", Bool(!ReferenceEquals(r6, w6)));
        Marshal.ReleaseComObject(r6);
        Rec("ref.replace_owner.new_refs_after_rc", or_refs(or_handed()));

        //==================================================================
        // 7. `ref` with a callee-written NULL: the managed variable becomes
        //    null (measured O7) and the old object is NOT released by managed
        //    code (delta stays at the +1 the callee left).
        //==================================================================
        IntPtr p7 = or_new();
        object w7 = Marshal.GetObjectForIUnknown(p7);
        IOutRefCom r7 = (IOutRefCom)w7;
        int base7 = or_refs(p7);
        or_ref_null(ref r7);
        Rec("ref.null.is_null", Bool(r7 == null));
        Rec("ref.null.old_delta", or_refs(p7) - base7);

        //==================================================================
        // 8. `ref` with a NULL IN value: the callee sees NULL and the managed
        //    variable stays null (measured O8) -- no throw on either side.
        //==================================================================
        IOutRefCom r8 = null;
        or_ref_noop(ref r8);
        Rec("ref.null_in.callee_saw_null", Bool(or_seen() == IntPtr.Zero));
        Rec("ref.null_in.stays_null", Bool(r8 == null));
    }
}
