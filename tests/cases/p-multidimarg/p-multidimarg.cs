// p-multidimarg.cs -- GN-0 probe: MULTIDIMENSIONAL array (T[,]) generic
// argument (GENERICS-FULL-ARG-GAPS.md 1.4/5, P-multidimarg). Locks the status
// quo; no implementation changes in this batch (GN-3 repair, gated on this
// probe).
//
// The gap: a multidim array generic arg is encoded 0x14 (ELEMENT_TYPE_ARRAY,
// rank + sizes), and ReadM3Arg has no 0x14 arm -- default throw "generic
// argument byte 0x14 in M3" (StackFold.cs:4646). net48 runs all lines
// byte-exact; the transpiled side is expected to LOUD-reject.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PMultiDimArg {
    public static void Main() {
        // ---- 1. int[,] 2-rank generic argument (storage fidelity) ----
        int[,] m = new int[2, 2] { { 1, 2 }, { 3, 4 } };
        Box<int[,]> bm = new Box<int[,]>();
        bm.Set(m);
        Console.WriteLine(bm.Get() == m);    // True (reference round-trip)
        Console.WriteLine(bm.Get()[0, 1]);   // 2

        // ---- 2. int[,,] 3-rank generic argument ----
        int[,,] cube = new int[2, 2, 2];
        cube[1, 1, 0] = 7;
        Box<int[,,]> bc = new Box<int[,,]>();
        bc.Set(cube);
        Console.WriteLine(bc.Get()[1, 1, 0]);  // 7
    }
}}
