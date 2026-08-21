//------------------------------------------------------------------------------
// virtsealed.cs -- DEVIRTUALIZATION BEHAVIOR DEEP 2 e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C17 (2026-08-10).
//
// DEEPENS virtops (C13) on the devirtualization gates (devirt S1 effectively-
// sealed receiver -> direct call; devirt S2 final override -> direct call;
// devirt S3 single-implementer interface -> direct call). virtops proved the
// gates produce byte-identical RESULTS for sealed receivers, effectively-
// sealed closed-world classes, final overrides, single/multi-implementer
// interfaces, multi-level override chains and base.Method(). virtsealed
// drives the SEALED-CLASS + FINAL-SLOT shapes further:
//
//   * SEALED RECEIVER DIRECT CALL on an INHERITED FINAL override: a sealed
//     class that does NOT re-override an inherited `sealed override` (final
//     slot) calls it directly; the result must equal the vtable dispatch of
//     the SAME call through a base-typed reference (polymorphism invariance).
//   * FINAL METHOD BLOCKS OVERRIDE: a `sealed override` at the MIDDLE of a
//     3-level chain freezes the slot -- the leaf (non-sealed) and every
//     reference type must run the final override's implementation.
//   * SEALED CLASS + OVERRIDE CHAIN: a sealed class that overrides later
//     virtuals (G/H) and inherits the final slot (F) -- reached through its
//     sealed static type (direct) and through base references (vtable); all
//     results identical.
//   * SEALED NO-OVERRIDE: a sealed class that overrides NOTHING inherits the
//     base implementation directly; the sealed-receiver direct call must equal
//     the base-typed vtable dispatch.
//   * SEALED CLASS + INTERFACE: a sealed class implementing an interface is
//     callable through its sealed static type (devirt candidate) and through
//     the interface reference (runtime dispatch); the results must agree
//     (devirt must not change observable semantics). A MULTI-implementer
//     interface whose sealed implementation coexists with a plain one must
//     still dispatch each implementation correctly (NOT devirtualized).
//   * base.Method() explicit call from a SEALED override.
//
// DETERMINISM: every probe folds to a deterministic int/bool computed from
// method results -- no timing, no identity of the resolved MethodInfo (devirt
// is internal; only the call RESULT is observable), exactly like virtops.
// The D-VIRT-1 `new`-hiding shape from virtops is NOT reproduced (no `new`
// method hiding a virtual anywhere below).
//------------------------------------------------------------------------------
using System;

public interface ISealedOp { int Apply(int x); }
public interface IMultiOp { int M(int x); }

public class VBase {
    public virtual int F(int x) { return x + 1; }
    public virtual int G(int x) { return x + 10; }
    public virtual int H(int x) { return x + 100; }
}
public class VMid : VBase {
    public sealed override int F(int x) { return x + 2; }   // FINAL slot
    public override int G(int x) { return x + 20; }
}
public class VLeaf : VMid {
    // F is `sealed override` in VMid -> cannot be overridden here.
    public override int G(int x) { return x + 30; }
}
public sealed class VSealed : VLeaf {
    public override int G(int x) { return x + 40; }
    public override int H(int x) { return x + 200; }
}
// sealed, overrides nothing -> inherits VBase's implementation directly.
public sealed class VNoOverride : VBase {
    public int Tag;
}
public sealed class VSingle : ISealedOp {
    public int Apply(int x) { return x * 7; }
}
public sealed class SealedImplA : IMultiOp {
    public int M(int x) { return x + 1; }
}
public class PlainImplB : IMultiOp {
    public int M(int x) { return x + 2; }
}
public class VSBase { public virtual int V() { return 10; } }
public sealed class VSDerived : VSBase {
    public override int V() { return base.V() + 5; }
}

namespace Cases { public static class VirtSealed {
    public static void Main() {
        // ---- A. sealed receiver direct call on an INHERITED final override
        // (F is `sealed override` in VMid; VSealed inherits the final slot)
        VSealed vs = new VSealed();
        Console.WriteLine(vs.F(1));        // 3  (sealed static type: direct)
        VBase vsAsBase = vs;
        Console.WriteLine(vsAsBase.F(1));  // 3  (base ref: vtable) SAME result
        VMid vsAsMid = vs;
        Console.WriteLine(vsAsMid.F(1));   // 3  (mid ref: vtable) SAME result

        // ---- B. FINAL method blocks override: leaf + every ref type runs
        // the final override's implementation
        VLeaf vl = new VLeaf();
        Console.WriteLine(vl.F(2));        // 4  (final: VMid's F)
        VBase vlAsBase = vl;
        Console.WriteLine(vlAsBase.F(2));  // 4
        VMid vlAsMid = vl;
        Console.WriteLine(vlAsMid.F(2));   // 4

        // ---- C. sealed class override chain reached through every ref type
        Console.WriteLine(vs.G(1));        // 41 (VSealed override)
        Console.WriteLine(vsAsBase.G(1));  // 41 (vtable -> VSealed)
        Console.WriteLine(vsAsMid.G(1));   // 41 (vtable -> VSealed)
        Console.WriteLine(vs.H(1));        // 201 (VSealed override)
        Console.WriteLine(vsAsBase.H(1));  // 201

        // ---- D. sealed no-override: inherited base impl, direct == vtable
        VNoOverride vno = new VNoOverride();
        Console.WriteLine(vno.F(3));       // 4  (VBase::F, direct call)
        VBase vnoAsBase = vno;
        Console.WriteLine(vnoAsBase.F(3)); // 4  (vtable -> VBase::F)
        Console.WriteLine(vno.G(3));       // 13 (VBase::G)
        Console.WriteLine(vnoAsBase.G(3)); // 13
        Console.WriteLine(vno.H(3));       // 103
        Console.WriteLine(vnoAsBase.H(3)); // 103
        vno.Tag = 1;
        Console.WriteLine(vno.Tag);        // 1 (own field intact)

        // ---- E. sealed class + interface: sealed-type direct call vs
        // interface dispatch must agree
        VSingle s = new VSingle();
        Console.WriteLine(s.Apply(3));     // 21 (direct on sealed static type)
        ISealedOp op = s;
        Console.WriteLine(op.Apply(3));    // 21 (interface dispatch) SAME result
        ISealedOp op2 = new VSingle();
        Console.WriteLine(op2.Apply(4));   // 28 (fresh instance via interface)

        // ---- F. multi-implementer interface with a SEALED implementation:
        // NOT devirtualized -- each impl must dispatch correctly
        IMultiOp ma = new SealedImplA();
        IMultiOp mb = new PlainImplB();
        Console.WriteLine(ma.M(1));        // 2  (SealedImplA)
        Console.WriteLine(mb.M(1));        // 3  (PlainImplB)
        Console.WriteLine(ma.M(2));        // 3
        Console.WriteLine(mb.M(2));        // 4

        // ---- G. base.Method() explicit from a SEALED override
        VSDerived sd = new VSDerived();
        Console.WriteLine(sd.V());         // 15 (base.V() + 5)
        VSBase sdAsBase = sd;
        Console.WriteLine(sdAsBase.V());   // 15
    }
}}
