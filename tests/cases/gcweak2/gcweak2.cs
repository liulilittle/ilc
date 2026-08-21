//------------------------------------------------------------------------------
// gcweak2.cs -- WEAK REFERENCE DEEP e2e reference case #2 (net48 oracle,
// byte-for-byte). Created in e2e batch C84, after the GC domain closure
// (MethodMap.WeakReference.cs + il2cpp_gc_ext.h / gcext.cpp weak-target
// handle table; the weakrefcase single-case surface already PASSes).
//
// This case drives the DEEP dimensions weakrefcase did not consolidate:
//
//   1. TARGET COLLECTION: one-shot and many-target weak refs whose targets
//      die (helper-returned so the creating frame is gone before Collect),
//      a BOXED target, and a strong-ref-pinned target that must SURVIVE
//      a full collection.
//   2. REDIRECTION: set_Target between two live objects, redirect-to-null
//      (IsAlive flips False immediately -- no collect required), redirect
//      back to a live object, dead->live REVIVAL, and repeated redirection
//      churn.
//   3. NESTING: WeakReference whose TARGET is another WeakReference object
//      (1- and 2-level chains), leaf-target death inside a chain, and the
//      outer slot clearing once the inner WeakReference OBJECT is
//      collected (helper-returned so the inner WR object's frame is gone).
//   4. Null-target WeakReference (IsAlive False, Target null).
//
// WIRED SURFACE (MethodMap.WeakReference.cs, verified 2026-08-13):
//   .ctor(object) / get_IsAlive / get_Target / set_Target -- WIRED.
//   .ctor(object, bool) and get_TrackResurrection -- LOUD NotSupported
//   (the handle-table object model does not track resurrection), so the
//   case NEVER probes them.
//
// DEAD-TARGET RULE (net48 JIT liveness pitfall, weakrefcase precedent):
// every target that MUST die is created inside a helper whose frame is
// gone before the collect; Main never holds a doomed target in a local.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Gcweak2
    {
        // One-shot dead target: only the weak reference escapes the frame.
        static WeakReference DeadTarget()
        {
            return new WeakReference(new object());
        }

        // A boxed target: the int is boxed, then only the weak ref escapes.
        static WeakReference DeadBoxedTarget()
        {
            return new WeakReference((object)12345);
        }

        // 1-level nesting, all live: the outer weak ref's target is the
        // inner WeakReference OBJECT. The leaf object stays referenced, so
        // after a full collect every level reports alive.
        static void NestOneLiveProbe()
        {
            object inner = new object();
            WeakReference innerWr = new WeakReference(inner);
            WeakReference outerWr = new WeakReference(innerWr);
            GC.Collect();
            Console.WriteLine("nest1_inner_alive=" + innerWr.IsAlive);            // True
            Console.WriteLine("nest1_outer_alive=" + outerWr.IsAlive);            // True
            Console.WriteLine("nest1_outer_same=" + (outerWr.Target == innerWr)); // True
            GC.KeepAlive(inner);
            GC.KeepAlive(innerWr);
            GC.KeepAlive(outerWr);
        }

        // Leaf death inside the chain: the leaf object dies, but the inner
        // WeakReference OBJECT is still referenced, so it survives and the
        // outer slot keeps pointing at it.
        static void NestLeafDeath()
        {
            object inner = new object();
            WeakReference innerWr = new WeakReference(inner);
            WeakReference outerWr = new WeakReference(innerWr);
            inner = null;                      // the only strong leaf ref dies
            GC.Collect();
            Console.WriteLine("nest_leaf_inner_dead=" + innerWr.IsAlive);          // False
            Console.WriteLine("nest_leaf_outer_alive=" + outerWr.IsAlive);         // True
            Console.WriteLine("nest_leaf_outer_target=" + (outerWr.Target == innerWr)); // True
            GC.KeepAlive(innerWr);
            GC.KeepAlive(outerWr);
        }

        // 2-level nesting: outer -> midWr (WR object) -> innerWr (WR object)
        // -> leaf object. All referenced, all alive after a collect.
        static void NestTwoLive()
        {
            object leaf = new object();
            WeakReference innerWr = new WeakReference(leaf);
            WeakReference midWr = new WeakReference(innerWr);
            WeakReference outerWr = new WeakReference(midWr);
            GC.Collect();
            Console.WriteLine("nest2_inner=" + innerWr.IsAlive);                   // True
            Console.WriteLine("nest2_mid=" + midWr.IsAlive);                       // True
            Console.WriteLine("nest2_outer=" + outerWr.IsAlive);                   // True
            Console.WriteLine("nest2_chain=" + (outerWr.Target == midWr && midWr.Target == innerWr)); // True
            GC.KeepAlive(leaf);
            GC.KeepAlive(innerWr);
            GC.KeepAlive(midWr);
            GC.KeepAlive(outerWr);
        }

        // The outer weak ref of a chain whose inner WeakReference OBJECT
        // dies: only the outer weak ref escapes the frame, so after a full
        // collect the inner WR object is reclaimed and the outer slot clears.
        static WeakReference NestOuterOfDeadInner()
        {
            object inner = new object();
            WeakReference innerWr = new WeakReference(inner);
            WeakReference outerWr = new WeakReference(innerWr);
            return outerWr; // inner and innerWr die with this frame
        }

        public static void Main()
        {
            // ================= 1. target collection =================
            WeakReference wa = DeadTarget();
            GC.Collect();
            Console.WriteLine("collect_dead1=" + wa.IsAlive);               // False
            Console.WriteLine("collect_dead1_target=" + (wa.Target == null)); // True

            WeakReference wb = DeadBoxedTarget();
            GC.Collect();
            Console.WriteLine("collect_box_dead=" + wb.IsAlive);           // False
            Console.WriteLine("collect_box_target=" + (wb.Target == null)); // True

            // a batch of independent dead targets (collection sweep) -- each
            // helper returns ONE weak ref, because a user method returning a
            // WeakReference[] is not supported (M3 return-type SZArray gap).
            WeakReference s0 = DeadTarget();
            WeakReference s1 = DeadTarget();
            WeakReference s2 = DeadTarget();
            GC.Collect();
            Console.WriteLine("sweep0=" + s0.IsAlive);               // False
            Console.WriteLine("sweep1=" + s1.IsAlive);               // False
            Console.WriteLine("sweep2=" + s2.IsAlive);               // False

            object pinned = new object();
            WeakReference wp = new WeakReference(pinned);
            GC.Collect();
            Console.WriteLine("collect_pinned_alive=" + wp.IsAlive);       // True
            Console.WriteLine("collect_pinned_target=" + (wp.Target == pinned)); // True

            // ================= 2. redirection =================
            object a = new object();
            object b = new object();
            WeakReference wr = new WeakReference(a);
            wr.Target = b;
            Console.WriteLine("redirect_to_b=" + (wr.Target == b));        // True
            Console.WriteLine("redirect_to_a=" + (wr.Target == a));        // False
            wr.Target = a;
            Console.WriteLine("redirect_back_a=" + (wr.Target == a));      // True
            wr.Target = null;
            Console.WriteLine("redirect_null_alive=" + wr.IsAlive);        // False
            Console.WriteLine("redirect_null_target=" + (wr.Target == null)); // True
            wr.Target = b;
            Console.WriteLine("redirect_relive_alive=" + wr.IsAlive);      // True
            Console.WriteLine("redirect_relive_target=" + (wr.Target == b)); // True
            wr.Target = a;
            wr.Target = b;
            wr.Target = a;
            Console.WriteLine("redirect_churn=" + (wr.Target == a));       // True
            GC.KeepAlive(a);
            GC.KeepAlive(b);

            // dead -> live revival
            WeakReference wd = DeadTarget();
            GC.Collect();
            object live = new object();
            wd.Target = live;
            Console.WriteLine("revive_alive=" + wd.IsAlive);               // True
            Console.WriteLine("revive_target=" + (wd.Target == live));     // True
            GC.KeepAlive(live);

            // ================= 3. nesting =================
            NestOneLiveProbe();
            NestLeafDeath();
            NestTwoLive();
            WeakReference nestOuter = NestOuterOfDeadInner();
            GC.Collect();
            Console.WriteLine("nest_outer_dead=" + nestOuter.IsAlive);     // False
            Console.WriteLine("nest_outer_dead_target=" + (nestOuter.Target == null)); // True

            // ================= 4. null target =================
            WeakReference wn = new WeakReference(null);
            Console.WriteLine("nullwr_alive=" + wn.IsAlive);               // False
            Console.WriteLine("nullwr_target=" + (wn.Target == null));     // True
        }
    }
}
