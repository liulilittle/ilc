//------------------------------------------------------------------------------
// p-gn10mdim.cs -- GN-10 (ii) probe: multidim-of-multidim / mixed nested ARRAY
// generic arguments. Three shapes are covered, each double-locked on a shared
// Box<T> (only Set/Get -> object-share mask all-true) and a non-shared Box3<T>
// (a by-value MVAR generic method Echo<U> forces the per-instance path so no
// object collapse can mask a name fork):
//
//   Box<int[,][,]>  -- TypeSpec `15 12 <box> 01 14 14 08 02 00 02 00 00 ...`
//   Box<int[][,]>   -- TypeSpec `15 12 <box> 01 1D 14 08 02 00 02 00 00`
//   Box<int[,][]>   -- TypeSpec `15 12 <box> 01 14 1D 08 02 00 02 00 00`
//
// At HEAD the resolver's multidim element table (ConsumeComplexM3ArgPayload)
// and the three producers' ReadM3MultidimArg complex branch threw
// "multidim array generic-argument element byte 0x14/0x1D in M3" (LOUD) or
// produced a non-recursive token that forked the definition-side clone name.
// The fix decodes array-typed elements RECURSIVELY so every producer emits the
// MethodBodyReader token ("[[int2]2]" / "[[int2]]" / "[[int]2]").
//
// net48 is the reference baseline; every line must print True byte-exact.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

class Box3<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
    // By-value MVAR generic method: keeps the whole definition on the
    // per-instance path (no object collapse can mask a name fork).
    public static void Echo<U>(U u) { }
}

public static class PGn10Mdim {
    public static void Main() {
        // ---- 1. int[,][,] (multidim of multidim) ----
        int[,][,] mm = new int[2, 2][,];
        Box<int[,][,]> b1 = new Box<int[,][,]>();
        b1.Set(mm);
        Console.WriteLine(b1.Get() == mm);                 // True
        Box3<int[,][,]> b1n = new Box3<int[,][,]>();
        b1n.Set(mm);
        Console.WriteLine(b1n.Get() == mm);                // True

        // ---- 2. int[][,] (jagged of multidim) ----
        int[][,] jm = new int[2][,];
        Box<int[][,]> b2 = new Box<int[][,]>();
        b2.Set(jm);
        Console.WriteLine(b2.Get() == jm);                 // True
        Box3<int[][,]> b2n = new Box3<int[][,]>();
        b2n.Set(jm);
        Console.WriteLine(b2n.Get() == jm);                // True

        // ---- 3. int[,][] (multidim of jagged) ----
        int[,][] mj = new int[2, 2][];
        Box<int[,][]> b3 = new Box<int[,][]>();
        b3.Set(mj);
        Console.WriteLine(b3.Get() == mj);                 // True
        Box3<int[,][]> b3n = new Box3<int[,][]>();
        b3n.Set(mj);
        Console.WriteLine(b3n.Get() == mj);                // True
    }
}}
