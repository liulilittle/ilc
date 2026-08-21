//------------------------------------------------------------------------------
// gchandledeep.cs -- System.Runtime.InteropServices.GCHandle DEEP e2e reference
// case (net48 oracle, byte-for-byte). Created in e2e batch C53 (2026-08-12),
// AFTER the gchandle domain wiring (MethodMap.GCHandle.cs -> gchandle.cpp, the
// static handle-table icall surface in il2cpp_gchandle.h).
//
// SURFACE COVERED (wired, all verified against the transpiler MethodMap):
//   * GCHandle.Alloc(object)             -- icall_gchandle_alloc_normal (Normal)
//   * GCHandle.Alloc(object, type)       -- icall_gchandle_alloc (type "i")
//   * Free() / get_Target / set_Target   -- icall_gchandle_free/get/set_target
//   * get_IsAllocated                    -- icall_gchandle_is_allocated
//   * AddrOfPinnedObject()               -- icall_gchandle_addr_of_pinned ("ptr")
//   * op_Equality / op_Inequality        -- il2cpp_intptr_op_equality/inequality
//
// DEEP SEMANTICS exercised:
//   1. Strong (Normal) handle keeps the target alive across a full collect.
//   2. Weak handle: alive while a strong reference exists; dies at a full
//      collect issued in the CALLER after the target's only strong reference
//      was cleared inside a helper frame. (Both runtimes root reference
//      locals across allocations in the SAME frame, so the collect must not
//      share a frame with the dying local -- the weakrefcase precedent. The
//      handle escapes the helper through a static GCHandle field, because
//      M3 user-method VALUE-TYPE returns are not wired.)
//   3. set_Target re-targets WITHOUT changing the handle type (a weak handle
//      re-targeted to a dead object still dies at the next caller collect);
//      set_Target(null) is legal and reads back as null.
//   4. Alloc(null) is legal (Normal handle with a null target, IsAllocated true).
//   5. Pinned: AddrOfPinnedObject returns a stable payload address for the
//      same object and distinct addresses for distinct objects (no collect in
//      between -- the documented pin limitation); a NON-pinned handle throws
//      InvalidOperationException. net48 requires a BLITTABLE target for a
//      Pinned handle (a plain object throws ArgumentException), so the pinned
//      probes use string literals.
//   6. Garbage/default handles: IsAllocated is false (never throws); Free /
//      get_Target on a default handle throw InvalidOperationException; a
//      same-variable double Free throws InvalidOperationException.
//   7. GCHandleType enum values on net48: Weak=0, WeakTrackResurrection=1,
//      Normal=2, Pinned=3 (probed; matches the runtime's constants).
//
// NOT PROBED (net48/ILC divergence, recorded in GAPS): value-COPY aliasing --
// net48 GCHandle.IsAllocated reflects the STRUCT FIELD (a copy keeps a freed
// handle "allocated", and Free() through a copy of a freed handle is a silent
// no-op), while the ILC runtime tracks SLOT liveness (a copy's IsAllocated is
// false once the shared slot is freed, and freeing through a copy of a freed
// handle throws). Both behaviors are internally consistent; the e2e stays on
// the subset both sides agree on (same-variable Free/IsAllocated sequences).
//
// Every line is a labeled bool, so a diff failure shows exactly which
// assertion flipped. Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class GcHandleDeep {

    // Handles whose referent must die: allocated in a helper frame, escaped
    // via a static GCHandle field (the M3 user-method return model cannot
    // return a VALUE-TYPE GCHandle).
    static GCHandle s_wk;
    static GCHandle s_wt;
    static int s_guard;

    // The target's ONLY strong reference lives inside this frame; after the
    // null-write + return the frame (and its GC roots) are gone, so a full
    // collect issued in the CALLER reclaims it deterministically.
    static void AllocWeakToStatic() {
        object o = new object();
        s_wk = GCHandle.Alloc(o, GCHandleType.Weak);
        o = null;
    }

    // Re-target the weak handle to a helper-dead object: the handle type
    // stays Weak, so the new target dies at the next caller collect.
    static void RetargetWeakToDead() {
        object o = new object();
        s_wt.Target = o;
        o = null;
        s_guard = s_guard + 1;   // static write -- purity gate keeps this out of the inline pass
    }

    public static void Main() {
        // ============ 1. Alloc(object) = Normal strong handle ============
        object o1 = new object();
        GCHandle h1 = GCHandle.Alloc(o1);
        Console.WriteLine(h1.IsAllocated);                       // True
        Console.WriteLine(h1.Target == o1);                      // True (target round trip)
        GC.Collect();
        Console.WriteLine(h1.Target == o1);                      // True (Normal keeps alive)
        h1.Free();
        Console.WriteLine(!h1.IsAllocated);                      // True (freed)

        // ============ 2. handle value equality / same-variable Free ============
        GCHandle ha = GCHandle.Alloc(new object());
        GCHandle hb = GCHandle.Alloc("other");
        Console.WriteLine(ha != hb);                             // True (distinct handles)
        Console.WriteLine(ha == ha);                             // True (self equality)
        Console.WriteLine(hb != ha);                             // True (op_Inequality symmetric)
        Console.WriteLine(hb.Target != null);                    // True (independent handles)
        ha.Free();
        Console.WriteLine(!ha.IsAllocated);                      // True (freed)
        bool dblThrown = false;
        try { ha.Free(); } catch (InvalidOperationException) { dblThrown = true; }
        Console.WriteLine(dblThrown);                            // True (same-variable double Free throws)
        hb.Free();
        Console.WriteLine(!hb.IsAllocated);                      // True

        // ============ 3. Alloc(object, GCHandleType.Weak) -- weak semantics ============
        object ow = new object();
        GCHandle hw = GCHandle.Alloc(ow, GCHandleType.Weak);
        Console.WriteLine(hw.Target == ow);                      // True (alive while strongly referenced)
        Console.WriteLine(hw.IsAllocated);                       // True
        GC.Collect();
        Console.WriteLine(hw.Target == ow);                      // True (ow still rooted in Main)
        hw.Target = null;                                        // re-target to null
        Console.WriteLine(hw.Target == null);                    // True (null target reads null)
        Console.WriteLine(hw.IsAllocated);                       // True (still allocated)
        hw.Free();

        // weak death: target dies when the helper frame + a caller collect
        AllocWeakToStatic();
        GC.Collect();
        Console.WriteLine(s_wk.Target == null);                  // True (weak referent collected)
        Console.WriteLine(s_wk.IsAllocated);                     // True (handle still allocated)
        s_wk.Free();

        // weak handle re-targeted to a dead object still dies (type fixed)
        s_wt = GCHandle.Alloc("seed", GCHandleType.Weak);
        Console.WriteLine((string)s_wt.Target == "seed");        // True
        RetargetWeakToDead();
        GC.Collect();
        Console.WriteLine(s_wt.Target == null);                  // True (new target died)
        Console.WriteLine(s_wt.IsAllocated);                     // True
        s_wt.Free();

        // ============ 4. Alloc(null) is legal ============
        GCHandle hn = GCHandle.Alloc(null);
        Console.WriteLine(hn.IsAllocated);                       // True
        Console.WriteLine(hn.Target == null);                    // True (null target)
        hn.Target = "n";
        Console.WriteLine((string)hn.Target == "n");             // True (re-target from null)
        hn.Free();
        Console.WriteLine(!hn.IsAllocated);                      // True

        // ============ 5. Pinned + AddrOfPinnedObject ============
        // net48 requires a BLITTABLE target for a Pinned handle (a plain
        // object throws ArgumentException); strings are pinnable on both
        // runtimes, so the probes below pin string literals.
        string sp = "first-pinned";
        GCHandle hp = GCHandle.Alloc(sp, GCHandleType.Pinned);
        Console.WriteLine(hp.IsAllocated);                       // True
        Console.WriteLine((string)hp.Target == sp);              // True
        IntPtr a1 = hp.AddrOfPinnedObject();
        IntPtr a2 = hp.AddrOfPinnedObject();
        Console.WriteLine(a1 == a2);                             // True (stable address, no collect)
        GCHandle hq = GCHandle.Alloc("second-pinned", GCHandleType.Pinned);
        IntPtr b1 = hq.AddrOfPinnedObject();
        Console.WriteLine(a1 != b1);                             // True (distinct objects)
        hp.Free();
        hq.Free();
        bool notPinnedThrown = false;
        GCHandle hnp = GCHandle.Alloc(new object());             // Normal (not pinned)
        try { IntPtr unused = hnp.AddrOfPinnedObject(); } catch (InvalidOperationException) { notPinnedThrown = true; }
        Console.WriteLine(notPinnedThrown);                      // True (non-pinned throws)
        hnp.Free();

        // ============ 6. default / garbage handle semantics ============
        GCHandle gd = default(GCHandle);
        Console.WriteLine(!gd.IsAllocated);                      // True (never throws)
        bool freeDefThrown = false;
        try { gd.Free(); } catch (InvalidOperationException) { freeDefThrown = true; }
        Console.WriteLine(freeDefThrown);                        // True (Free on default throws)
        bool targetDefThrown = false;
        try { object t = gd.Target; } catch (InvalidOperationException) { targetDefThrown = true; }
        Console.WriteLine(targetDefThrown);                      // True (get_Target on default throws)

        // ============ 7. GCHandleType enum values (net48 constants) ============
        Console.WriteLine((int)GCHandleType.Weak == 0);          // True
        Console.WriteLine((int)GCHandleType.WeakTrackResurrection == 1); // True
        Console.WriteLine((int)GCHandleType.Normal == 2);        // True
        Console.WriteLine((int)GCHandleType.Pinned == 3);        // True
    }
}}
