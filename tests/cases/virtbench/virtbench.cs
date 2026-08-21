//------------------------------------------------------------------------------
// virtbench.cs -- de-virtualization (S1/S3) performance verification case.
// net48 oracle: the trailing "checksum" line must be byte-identical on net48
// and the ILC transpiled build (correctness oracle); the per-mode "<ms>" lines
// are the benchmark data (net48 vs ILC ratio, target > 1.5x on the
// devirtualized modes).
//
// MODES (virtual-call-dense, N=20,000,000 / imulti M=5,000,000 loops):
//   1. iface  -- interface-dispatch loop: IOp.Apply through an interface
//      reference whose ONLY concrete implementer in the closed world is OpA.
//      ILC lowers this to a DIRECT call (devirt S3: InterfaceImplementerCount
//      == 1 + InterfaceSlotMaps resolution). MEASURED: net48's JIT also
//      devirtualizes this (its tracked/opaque receivers cost the same), so
//      the net48 "dispatch" it pays is near-zero.
//   2. poly   -- virtual-dispatch loop: PolyBase::F on a PolyA receiver that
//      HAS a subclass (PolyB) -> NOT effectively sealed -> ILC KEEPS the
//      runtime vtable dispatch (->klass->vtable[slot]). This is the control:
//      what a NON-devirtualizable virtual call costs on both runtimes.
//   3. sealed -- virtual-dispatch loop on a SEALED class receiver (SealedImpl)
//      -> ILC lowers to a DIRECT call (devirt S1: effectively-sealed
//      receiver). net48 pays a vtable dispatch at most (its JIT also
//      devirtualizes known sealed receivers).
//   4. imulti -- DIAGNOSTIC CONTROL: multi-implementer IMulti on an opaque
//      factory receiver. NEITHER runtime can devirtualize (receiver type not
//      statically known), so both pay REAL interface dispatch -- ILC keeps
//      the runtime il2cpp_interface_dispatch LINEAR SEARCH (the pre-S3 path).
//      Quantifies what S3's direct call saves the single-implementer mode.
//
// METHODOLOGY (same as tests/bench/bench.cs): fixed-work loops (the time
// varies, the work does not -- that is what makes the ratio meaningful).
// Every mode folds into the observable static 'checksum' field (external
// linkage storage in the generated C++), so neither the .NET JIT nor -O3 can
// eliminate the loops. Every virtual target runs a MEDIUM body (64-iteration
// xorshift accumulation -- too big for the net48 JIT to inline, so the
// measured cost is DISPATCH vs DIRECT CALL on a real body, not an inlined
// fragment) and is NoInlining on BOTH sides (net48 JIT:
// MethodImplOptions.NoInlining; ILC: IL2CPP_NOINLINE). Masked accumulation
// (& 0x7F) keeps the operands small; the int32 wrap is deterministic and
// identical on both runtimes (bench.cs precedent). Timing through the
// P/Invoke steady-clock now_us() (mylib, microseconds as int32), printed as
// "name" + "<ms>" on separate lines (the transpiler cannot resolve string+int
// concat -- bench.cs limitation).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

// Top-level translated types (virtual-sealed hierarchy shape as in the
// virtualsealed case: Shape/Rect/Square/Tri are top-level).
public interface IOp {
    int Apply(int x);
}
public sealed class OpA : IOp {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Apply(int x) {
        int s = x;
        for (int k = 0; k < 64; k++) { s = s * 3 + 1; s ^= s >> 13; }
        return s;
    }
}
public class PolyBase {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual int F(int x) {
        int s = x;
        for (int k = 0; k < 64; k++) { s = s * 3 + 1; s ^= s >> 13; }
        return s;
    }
}
public class PolyA : PolyBase {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override int F(int x) {
        int s = x;
        for (int k = 0; k < 64; k++) { s = s * 3 + 1; s ^= s >> 13; }
        return s;
    }
}
public class PolyB : PolyA {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override int F(int x) {
        int s = x;
        for (int k = 0; k < 64; k++) { s = s * 3 + 1; s ^= s >> 13; }
        return s;
    }
}
public sealed class SealedImpl : PolyBase {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override int F(int x) {
        int s = x;
        for (int k = 0; k < 64; k++) { s = s * 3 + 1; s ^= s >> 13; }
        return s;
    }
}

// --- mode D (diagnostic control): MULTI-implementer interface on an OPAQUE
// receiver. Neither runtime can devirtualize (the factory return type is not
// statically known), so BOTH pay real interface dispatch: net48 pays its
// slot-table dispatch; ILC keeps the runtime il2cpp_interface_dispatch
// LINEAR SEARCH (the pre-S3 path). This quantifies what S3's direct call
// saves the single-implementer iface mode. ---
public interface IMulti {
    int M(int x);
}
public class ImplA : IMulti {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int M(int x) {
        int s = x;
        for (int k = 0; k < 64; k++) { s = s * 3 + 1; s ^= s >> 13; }
        return s;
    }
}
public class ImplB : IMulti {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int M(int x) {
        int s = x;
        for (int k = 0; k < 64; k++) { s = s * 3 + 1; s ^= s >> 13; }
        return s;
    }
}

namespace Cases { public static class VirtBench {
    // P/Invoke steady clock (mylib: steady_clock, microseconds, wrapped to [0,2^31)).
    [DllImport("mylib", EntryPoint = "now_us")]
    public static extern int now_us();

    // Sink: every mode folds its result into this static field.
    public static int checksum;

    // 1. interface-dispatch loop (S3 single-implementer).
    public static int IfaceLoop(int iters) {
        IOp op = new OpA();
        int sum = 0;
        for (int i = 0; i < iters; i++) sum += op.Apply(i) & 0x7F;
        return sum;
    }

    // 2. virtual-dispatch loop (non-devirtualizable: receiver has a subclass).
    public static int PolyLoop(int iters) {
        PolyA p = new PolyA();
        int sum = 0;
        for (int i = 0; i < iters; i++) sum += p.F(i) & 0x7F;
        return sum;
    }

    // 3. virtual-dispatch loop on a sealed receiver (S1 direct call).
    public static int SealedLoop(int iters) {
        SealedImpl s = new SealedImpl();
        int sum = 0;
        for (int i = 0; i < iters; i++) sum += s.F(i) & 0x7F;
        return sum;
    }

    // 4. multi-implementer interface on an opaque receiver (no devirtualization
    //    on either runtime -- the S3-less dispatch control).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IMulti MakeMulti(int n) {
        if ((n & 1) == 0) return new ImplA();
        return new ImplB();
    }

    public static int MultiLoop(int iters) {
        IMulti m = MakeMulti(1);
        int sum = 0;
        for (int i = 0; i < iters; i++) sum += m.M(i) & 0x7F;
        return sum;
    }

    public static void Main() {
        int N = 20000000;
        int M = 5000000;
        int t0, t1;

        t0 = now_us();
        checksum += IfaceLoop(N);
        t1 = now_us();
        Console.WriteLine("iface");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += PolyLoop(N);
        t1 = now_us();
        Console.WriteLine("poly");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += SealedLoop(N);
        t1 = now_us();
        Console.WriteLine("sealed");
        Console.WriteLine((t1 - t0) / 1000);

        t0 = now_us();
        checksum += MultiLoop(M);
        t1 = now_us();
        Console.WriteLine("imulti");
        Console.WriteLine((t1 - t0) / 1000);

        Console.WriteLine("checksum");
        Console.WriteLine(checksum);
    }
}}
