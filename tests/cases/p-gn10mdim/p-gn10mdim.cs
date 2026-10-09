//------------------------------------------------------------------------------
// p-gn10mdim.cs -- GN-10 (ii) probe: multidim-of-multidim / mixed nested ARRAY
// generic-argument NAME locking. Three shapes are covered, each double-locked
// on a shared Box<T> (only Set/Get -> object-share mask all-true) and a
// non-shared Box3<T> (a by-value MVAR generic method Echo<U> forces the
// per-instance path so no object collapse can mask a name fork):
//
//   Box<int[,][,]>  -- TypeSpec `15 12 <box> 01 14 14 08 02 00 02 00 00 ...`
//   Box<int[][,]>   -- TypeSpec `15 12 <box> 01 1D 14 08 02 00 02 00 00`
//   Box<int[,][]>   -- TypeSpec `15 12 <box> 01 14 1D 08 02 00 02 00 00`
//
// The values are intentionally null: the probe locks the generic-argument
// NAME resolution (clone class + ctor + Set/Get signature), not the multidim
// storage. At HEAD the resolver's multidim element table
// (ConsumeComplexM3ArgPayload) and the three producers' ReadM3MultidimArg
// complex branch threw "multidim array generic-argument element byte
// 0x14/0x1D in M3" (LOUD) or produced a non-recursive token that forked the
// definition-side clone name, so the fold was skipped and the whole Main
// reported "not transpiled". The fix decodes array-typed elements RECURSIVELY
// so every producer emits the MethodBodyReader token ("[[int2]2]" /
// "[[int2]]" / "[[int]2]").
//
// A simple-multidim green control (Box<int[,]>) rides alongside so the plain
// multidim name path stays covered.
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
        // ---- 0. control: plain int[,] multidim generic argument ----
        Box<int[,]> b0 = new Box<int[,]>();
        b0.Set(null);
        Console.WriteLine(b0.Get() == null);               // True

        // ---- 1. int[,][,] (multidim of multidim) ----
        Box<int[,][,]> b1 = new Box<int[,][,]>();
        b1.Set(null);
        Console.WriteLine(b1.Get() == null);               // True
        Box3<int[,][,]> b1n = new Box3<int[,][,]>();
        b1n.Set(null);
        Console.WriteLine(b1n.Get() == null);              // True

        // ---- 2. int[][,] (jagged of multidim) ----
        Box<int[][,]> b2 = new Box<int[][,]>();
        b2.Set(null);
        Console.WriteLine(b2.Get() == null);               // True
        Box3<int[][,]> b2n = new Box3<int[][,]>();
        b2n.Set(null);
        Console.WriteLine(b2n.Get() == null);              // True

        // ---- 3. int[,][] (multidim of jagged) ----
        Box<int[,][]> b3 = new Box<int[,][]>();
        b3.Set(null);
        Console.WriteLine(b3.Get() == null);               // True
        Box3<int[,][]> b3n = new Box3<int[,][]>();
        b3n.Set(null);
        Console.WriteLine(b3n.Get() == null);              // True
    }
}}
