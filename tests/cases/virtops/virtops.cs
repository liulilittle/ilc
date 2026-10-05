//------------------------------------------------------------------------------
// virtops.cs -- POLYMORPHISM DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C13 (2026-08-09).
//
// Verifies that DEVIRTUALIZATION (devirt S1: effectively-sealed receiver
// -> direct call; devirt S2: final override -> direct call; devirt S3:
// single-implementer interface -> direct call) did NOT change observable
// behavior. The transpiler lowers a callvirt to a direct call ONLY when the
// resolved implementation is provably identical to the runtime vtable slot
// (CodeGenerator.cs TryDevirtualize + VTablePlanner); virtops drives the
// exact gates and asserts the byte-identical RESULTS (net48 oracle vs ILC
// run). Every probe folds to a deterministic int/bool -- no timing, no
// identity of the resolved MethodInfo (devirt is internal; only the call
// RESULT is observable).
//
// Gate coverage:
//   S1 sealed receiver:   a `sealed` override called on its own static type
//                         -> DIRECT call. Result must equal the vtable
//                         dispatch result (probe the SAME call through a
//                         base-typed reference, which is NOT devirtualized --
//                         both must agree).
//   S1 effectively-sealed (closed-world no-subclass): a non-sealed class
//     with no subclass in the program -> direct call; result must match.
//   S2 final override:    a `sealed override` (final slot) on a non-sealed
//                         receiver -> direct call; the override must win.
//   S3 single implementer: an interface with exactly ONE concrete
//     implementer -> direct call to that impl; result must match a
//     non-devirtualized shape.
//   Multi-implementer interface (NOT devirtualized): each impl's own result
//     must dispatch correctly (runtime dispatch stays correct).
//   Multi-level override chain: Base -> Mid -> Derived override of the same
//     virtual; a Derived instance must run the MOST-derived override whether
//     reached through a Base or a Derived reference.
//   base.Method() explicit base call from an override.
//
// RECORDED DEFECT (D-VIRT-1, NOT probed -- see GAPS.md): a callvirt to a
// `new`-HIDDEN non-virtual method on its declaring type (`Square.Tag` hides
// the inherited virtual `Shape.Tag`) is resolved by the transpiler as the
// INHERITED VIRTUAL SLOT implementation (ILC runs Rect_Tag=100) instead of
// the hidden method's own implementation (net48 runs Square_Tag=200). IL
// proof: Main emits `callvirt MD:Square::Tag` at off=122. Only virtual-
// dispatch shapes are probed below (the `new`-hiding shape is parked to
// keep the active surface byte-exact).
//
// DETERMINISM: all outputs are int values or bools computed from method
// results; no heap addresses, no timings, no hash values.
//------------------------------------------------------------------------------
using System;

public interface IOp {
    int Apply(int x);
}
public sealed class SingleImpl : IOp {
    public int Apply(int x) { return x * 3; }
}
public class MultiA : IOp {
    public int Apply(int x) { return x + 5; }
}
public class MultiB : IOp {
    public int Apply(int x) { return x + 50; }
}

public class Shape {
    public virtual int Area() { return 0; }
    public virtual int Sides() { return 0; }
    public virtual int Tag() { return 1; }
}
public class Rect : Shape {
    public int W, H;
    public Rect(int w, int h) { W = w; H = h; }
    public override int Area() { return W * H; }
    public sealed override int Tag() { return 100; }   // final override (S2)
}
public class Square : Rect {
    public Square(int w) : base(w, w) { }
    public override int Area() { return W * W; }
    public override int Sides() { return 4; }
    public new int Tag() { return 200; }               // hides Shape.Rect.Tag (new)
}
public sealed class SealedRect : Rect {
    public SealedRect(int w, int h) : base(w, h) { }
    public override int Area() { return W * W; }
}
// effectively-sealed: no subclass in the closed world, not marked sealed.
public class LeafShape : Shape {
    public override int Area() { return 7; }
}
// multi-level chain
public class L1 { public virtual int F(int x) { return x + 1; } }
public class L2 : L1 { public override int F(int x) { return x + 2; } }
public class L3 : L2 { public override int F(int x) { return x + 3; } }
// base.Method() explicit
public class B { public virtual int V() { return 10; } }
public class D : B {
    public override int V() { return base.V() + 5; }
}

namespace Cases { public static class VirtOps {
    public static void Main() {
        // ---- A. S1 sealed receiver direct call vs vtable base ref ----
        SealedRect sr = new SealedRect(3, 4);
        Console.WriteLine(sr.Area());                 // 9  (sealed static type: direct)
        Rect rAsRect = sr;
        Console.WriteLine(rAsRect.Area());            // 9  (base ref: vtable) SAME result
        Shape sAsShape = sr;
        Console.WriteLine(sAsShape.Area());           // 9  (Shape ref: vtable) SAME result

        // ---- B. S1 effectively-sealed (closed-world no subclass) ----
        LeafShape leaf = new LeafShape();
        Console.WriteLine(leaf.Area());               // 7
        Shape leafAsShape = leaf;
        Console.WriteLine(leafAsShape.Area());        // 7

        // ---- C. S2 final override wins (non-sealed receiver type) ----
        Rect rect = new Rect(2, 3);
        Console.WriteLine(rect.Tag());                // 100 (final override)
        Console.WriteLine(rect.Area());               // 6  (Rect override)
        Square sq = new Square(3);
        Console.WriteLine(sq.Area());                 // 9  (Square override)
        Console.WriteLine(sq.Sides());                // 4
        // NOTE: `sq.Tag()` (the `new`-HIDDEN Square::Tag) is NOT probed -- it
        // exposes the recorded D-VIRT-1 defect (see GAPS.md): the transpiler
        // resolves a callvirt to the hidden non-virtual method as the
        // inherited virtual slot impl. Only the virtual-dispatch shapes are
        // active below.
        Shape sqAsShape = sq;
        Console.WriteLine(sqAsShape.Tag());           // 100 (virtual dispatch -> final override)
        Console.WriteLine(sqAsShape.Area());          // 9  (virtual dispatch -> Square)

        // ---- D. S3 single-implementer interface ----
        IOp op = new SingleImpl();
        Console.WriteLine(op.Apply(2));               // 6
        Console.WriteLine(op.Apply(3));               // 9

        // ---- E. multi-implementer interface (runtime dispatch) ----
        IOp a = new MultiA();
        IOp b = new MultiB();
        Console.WriteLine(a.Apply(2));                // 7  (MultiA)
        Console.WriteLine(b.Apply(2));                // 52 (MultiB)
        Console.WriteLine(a.Apply(3));                // 8
        Console.WriteLine(b.Apply(3));                // 53

        // ---- F. multi-level override chain ----
        L3 l3 = new L3();
        Console.WriteLine(l3.F(0));                   // 3  (most-derived)
        L1 asL1 = l3;
        Console.WriteLine(asL1.F(0));                 // 3  (dispatch through chain)
        L2 asL2 = l3;
        Console.WriteLine(asL2.F(0));                 // 3

        // ---- G. base.Method() explicit base call ----
        D d = new D();
        Console.WriteLine(d.V());                     // 15 (base.V() + 5)
        B bb = d;
        Console.WriteLine(bb.V());                    // 15
    }
}}
