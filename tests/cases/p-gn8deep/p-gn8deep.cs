//------------------------------------------------------------------------------
// p-gn8deep.cs -- GN-8 probe: nested generic INNER-ARRAY argument name
// divergence on Box<Outer<Inner<int[]>>> (double-locked: shared + non-shared).
//
// The divergence at HEAD: MethodRefResolver.ReadM3GenericArgNested encodes
// an SZARRAY (0x1D) nested argument as the constant token "array" WITHOUT
// consuming the element-type bytes (stream misalignment), while the clone
// side (MethodBodyReader / StackFold / CodeGenerator and the top-level M3
// arg table) is element-aware and encodes "Inner<...>[int]". The call side
// references "..._array" while the emitted definition-side clone is named
// "..._[int]" (or reverse under collapse) -- the two sides fork and g++
// fails the build ("X does not name a type" / "was not declared").
//
// Two independent arms:
//   1. shared Box<T> (only Set/Get -> object-share mask all-true). The
//      collapse predicate (CollapseInstantiation IsArrayArgToken) only
//      matches a WHOLE arg token that starts with '[', so a nested
//      composite arg "..._[int]" slips past and the call side collapses to
//      Box_1_object while EnsureClone's suffix.Contains('[') keeps the
//      element-aware clone -> the clone is never referenced.
//   2. non-shared Box3<T> (Echo<U> by-value MVAR forces the per-instance
//      path) -- exposes the 0x1D name fork directly (no collapse mask).
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

public static class PGn8Deep {
    public static void Main() {
        Outer<Inner<int[]>> o = new Outer<Inner<int[]>>();
        o.V = new Inner<int[]>();
        o.V.V = new int[] { 7 };

        // ---- 1. shared Box<T> arm ----
        Box<Outer<Inner<int[]>>> b = new Box<Outer<Inner<int[]>>>();
        b.Set(o);
        Console.WriteLine(b.Get() == o);          // True: reference round-trips
        Console.WriteLine(b.Get().V.V[0] == 7);   // True: nested array intact

        // ---- 2. non-shared Box3<T> arm ----
        Box3<Outer<Inner<int[]>>> b3 = new Box3<Outer<Inner<int[]>>>();
        b3.Set(o);
        Console.WriteLine(b3.Get() == o);         // True: reference round-trips
        Console.WriteLine(b3.Get().V.V[0] == 7);  // True: nested array intact
    }
}}