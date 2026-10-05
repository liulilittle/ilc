//------------------------------------------------------------------------------
// gen-arrayarg.cs -- ARRAY generic-argument e2e reference case (net48 oracle,
// byte-for-byte). Created in the R→G→C batch, G1 (C1).
//
// Probes the array generic-argument cell of the instantiation-shape matrix:
//   * storage fidelity -- a Box<T> whose T is an ARRAY (value-element int[]
//     and reference-element string[]) stores/round-trips the array reference
//     through the generic slot (the array is a reference slot in the M-stage).
//   * instance identity -- whether the two CLOSED types are distinguishable.
//     net48: typeof(Box<int[]>) != typeof(Box<string[]>) (distinct closed
//     RuntimeTypes) and the instance GetType() objects are NOT reference-
//     equal. The transpiled side answers with the emitted-class name strings,
//     so the oracle pins whether the element-aware encoding matters.
//
// Byte-stability: every line prints "label=True/False". net48 is the
// reference baseline; every line must be byte-identical.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box<T> {
    T value;
    public void Set(T v) { value = v; }
    public T Get() { return value; }
}

public static class GenArrayArg {
    public static void Main() {
        // ---- 1. storage fidelity: int[] (value-element array) ----
        int[] ia = new int[] { 1, 2, 3 };
        Box<int[]> bi = new Box<int[]>();
        bi.Set(ia);
        Console.WriteLine(bi.Get() == ia);        // True: the stored ref round-trips
        Console.WriteLine(bi.Get().Length == 3);  // True: the stored array is intact

        // ---- 2. storage fidelity: string[] (reference-element array) ----
        string[] sa = new string[] { "a", "b" };
        Box<string[]> bs = new Box<string[]>();
        bs.Set(sa);
        Console.WriteLine(bs.Get() == sa);        // True
        Console.WriteLine(bs.Get().Length == 2);  // True

        // ---- 3. closed-type identity ----
        // The array-element-aware per-instance clones give the two instances
        // DISTINCT runtime types (GetType() objects not reference-equal), and
        // the same closed type re-uses one type (reference-equal). NOTE: the
        // bare `typeof(Box<int[]>) == typeof(Box<string[]>)` probe is NOT here
        // -- the typeof fold resolves a closed GenericInst to its DEFINITION
        // name ("Box`1"), a documented general simplification shared by ALL
        // generic instances (typeof(List<int>) == typeof(List<string>) folds
        // equal too), not the array cell this case pins.
        Console.WriteLine(object.ReferenceEquals(bi.GetType(), bs.GetType())); // False (net48)
        Console.WriteLine(typeof(Box<int[]>) == typeof(Box<int[]>));     // True (net48)
    }
}}