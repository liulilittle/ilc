// p-structarg.cs -- GN-0 probe: USER STRUCT generic argument (GENERICS-FULL-
// ARG-GAPS.md 1.1/5, P-structarg). Locks the status quo; no implementation
// changes in this batch (GN-1+ repair).
//
// Shapes probed:
//   * per-instance user generic Box<Pt> box/unbox round-trip -- the P25b
//     ref-free path (locked as covered in the gap doc);
//   * a SHARED byref-MVAR generic method (SwapRef<T>) called with a user
//     struct -- the 1.1 cell: MethodSpecGenArgFullName falls to the
//     `_ =>` int32 bridge (StackFold.cs:5499-5500), silently box/unboxing the
//     struct as int32 instead of LOUD-rejecting.
//
// net48 oracle: both lines True (the swap is a plain 8-byte struct move).
// If the transpiled side renders the shared bridge as int32, the swapped
// fields differ and the case FAILs at diff -- that FAIL is the locked red.
//------------------------------------------------------------------------------
using System;

public class Swap {
    public static void SwapRef<T>(ref T a, ref T b) { T t = a; a = b; b = t; }
}

namespace Cases {

struct Pt {
    public int X;
    public int Y;
    public Pt(int x, int y) { X = x; Y = y; }
}

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PStructArg {
    public static void Main() {
        // ---- 1. per-instance Box<Pt> box/unbox round-trip (P25b path) ----
        Box<Pt> b = new Box<Pt>();
        b.Set(new Pt(3, 4));
        Pt p = b.Get();
        Console.WriteLine(p.X == 3 && p.Y == 4);   // True

        // ---- 2. shared byref-MVAR SwapRef<T> with user-struct arg (1.1) ----
        Pt a = new Pt(1, 2);
        Pt c = new Pt(3, 4);
        Swap.SwapRef<Pt>(ref a, ref c);
        Console.WriteLine(a.X == 3 && a.Y == 4);   // True (net48 struct swap)
        Console.WriteLine(c.X == 1 && c.Y == 2);   // True (net48 struct swap)
    }
}}
