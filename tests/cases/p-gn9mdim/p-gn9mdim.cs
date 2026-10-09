//------------------------------------------------------------------------------
// p-gn9mdim.cs -- GN-9 (b) probe: nested generic MULTIDIM-ARRAY (int[,])
// argument name divergence on Box<Outer<Inner<int[,]>>> (double-locked:
// shared + non-shared).
//
// The divergence at HEAD: MethodRefResolver.ReadM3GenericArgNested has no 0x14
// (ELEMENT_TYPE_ARRAY) arm in its nested generic-argument byte table -- it
// throws "nested generic argument byte 0x14 in M3". StackFold / CodeGenerator
// / MethodBodyReader all already carry the 0x14 multidim arm; only the
// resolver-side nested table lags, so a nested multidim generic argument (the
// Inner<int[,]> leaf inside Outer<Inner<int[,]>> inside Box<...>) kills the
// transpile with a LOUD NotSupportedException before codegen.
//
// Same double-lock arms as p-gn8deep:
//   1. shared Box<T> (only Set/Get -> object-share mask all-true).
//   2. non-shared Box3<T> (Echo<U> by-value MVAR forces the per-instance path).
//
// net48 is the reference baseline; every line must print True byte-exact.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Inner<T> {
    public T V;
}

class Outer<T> {
    public T V;
}

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

public static class PGn9Mdim {
    public static void Main() {
        Outer<Inner<int[,]>> o = new Outer<Inner<int[,]>>();
        o.V = new Inner<int[,]>();
        o.V.V = new int[2, 2] { { 1, 2 }, { 3, 7 } };

        // ---- 1. shared Box<T> arm ----
        Box<Outer<Inner<int[,]>>> b = new Box<Outer<Inner<int[,]>>>();
        b.Set(o);
        Console.WriteLine(b.Get() == o);          // True: reference round-trips
        Console.WriteLine(b.Get().V.V[1, 1] == 7); // True: nested multidim intact

        // ---- 2. non-shared Box3<T> arm ----
        Box3<Outer<Inner<int[,]>>> b3 = new Box3<Outer<Inner<int[,]>>>();
        b3.Set(o);
        Console.WriteLine(b3.Get() == o);          // True: reference round-trips
        Console.WriteLine(b3.Get().V.V[1, 1] == 7); // True: nested multidim intact
    }
}}