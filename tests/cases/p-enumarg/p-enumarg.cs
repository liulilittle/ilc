// p-enumarg.cs -- GN-0 probe: ENUM generic argument (GENERICS-FULL-ARG-GAPS.md
// 1.1/5, P-enumarg). Locks the status quo; D5 target: the SwapRef<Color> shared
// byref cell (part 3) is currently LOUD (GN-1 ruling (2) -- no shared
// box/unbox bridge for user enums). Red at HEAD; D5 adds the bridge to make
// the byte-exact swap pass.
//
// Shapes probed:
//   * per-instance Box<Color> (user enum) and Box<DayOfWeek> (BCL enum)
//     box/unbox round-trips -- the enum-box cell (icall_box_enum white-list);
//   * a SHARED byref-MVAR generic method (SwapRef<T>) called with an enum --
//     the 1.1 cell: the shared bridge's int32 fallback silently treats the
//     enum's underlying int as the payload (StackFold.cs:5499-5500). D5 adds
//     the user-enum shared box/unbox bridge.
//
// net48 oracle: True / 1 / True / True / True (enum swap is a 4-byte move).
// Divergence on the transpiled side = the locked red for the enum cell.
//------------------------------------------------------------------------------
using System;

public class Swap {
    public static void SwapRef<T>(ref T a, ref T b) { T t = a; a = b; b = t; }
}

namespace Cases {

enum Color { Red, Green, Blue }

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PEnumArg {
    public static void Main() {
        // ---- 1. user enum generic argument (box/unbox round-trip) ----
        Box<Color> bc = new Box<Color>();
        bc.Set(Color.Green);
        Color c = bc.Get();
        Console.WriteLine(c == Color.Green);   // True
        Console.WriteLine((int)c);             // 1

        // ---- 2. BCL enum generic argument (box/unbox round-trip) ----
        Box<DayOfWeek> bd = new Box<DayOfWeek>();
        bd.Set(DayOfWeek.Thursday);
        Console.WriteLine(bd.Get() == DayOfWeek.Thursday);  // True

        // ---- 3. shared byref-MVAR SwapRef<T> with enum arg (1.1) ----
        Color x = Color.Red;
        Color y = Color.Blue;
        Swap.SwapRef<Color>(ref x, ref y);
        Console.WriteLine(x == Color.Blue);    // True (net48)
        Console.WriteLine(y == Color.Red);     // True (net48)
    }
}}
