// p-structarg.cs -- GN-0 probe: USER STRUCT generic argument (GENERICS-FULL-
// ARG-GAPS.md 1.1/5, P-structarg). Locks the status quo; D5 target: the
// SwapRef<Pt> shared byref cell (part 2) is currently LOUD (GN-1 ruling (2)
// -- no shared box/unbox bridge for user structs). Red at HEAD; D5 adds the
// bridge to make the byte-exact swap pass.
//
// Shapes probed:
//   * per-instance user generic Box<Pt> box/unbox round-trip -- the P25b
//     ref-free path (locked as covered in the gap doc);
//   * a SHARED byref-MVAR generic method (SwapRef<T>) called with a user
//     struct -- the 1.1 cell: MethodSpecGenArgFullName re-read mis-decoded a
//     VALUETYPE genArg as a TypeRef coded index (the element byte 0x11 was
//     read as the handle), naming 'DebuggableAttribute+DebuggingModes'
//     instead of 'Cases.Pt'; D5 fixes the decode and adds the user-struct
//     shared box/unbox bridge.
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

// GN-N6 obs2: a user STRUCT whose FULL NAME contains '_' ("Cases.A_B").
// Sanitize maps both '.' and '_' to '_' for the C++ name, but the M3 arg
// name is the full name "Cases.A_B" -- EnsureClone must match it whole.
struct A_B {
    public int X;
    public int Y;
    public A_B(int x, int y) { X = x; Y = y; }
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

        // ---- 1b. GN-N6 obs2: SHARED Box + user struct whose FULL NAME
        // itself contains '_' ("Cases.A_B"). EnsureClone's naive Split('_')
        // would fragment "Cases.A_B" into "Cases"/"A"/"B" and miss the
        // user-value-type set (collapsing Box<A_B> to the object clone),
        // while CollapseInstantiation's per-arg FULL-NAME match keeps
        // per-instance -- a C++ name divergence. ----
        Box<A_B> b2 = new Box<A_B>();
        b2.Set(new A_B(5, 6));
        A_B q = b2.Get();
        Console.WriteLine(q.X == 5 && q.Y == 6);   // True

        // ---- 2. shared byref-MVAR SwapRef<T> with user-struct arg (1.1) ----
        Pt a = new Pt(1, 2);
        Pt c = new Pt(3, 4);
        Swap.SwapRef<Pt>(ref a, ref c);
        Console.WriteLine(a.X == 3 && a.Y == 4);   // True (net48 struct swap)
        Console.WriteLine(c.X == 1 && c.Y == 2);   // True (net48 struct swap)
    }
}}
