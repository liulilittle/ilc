// p-namediv.cs -- GN-0 probe: generic-instantiation NAME divergence / clash
// (GENERICS-FULL-ARG-GAPS.md 2a/5, P-namediv). Locks the status quo; no
// implementation changes in this batch.
//
// The gap: Sanitize maps both '.' and '_' to '_', so the arg full names
// "Cases.A_B" and "Cases.A.B" collide onto the same C++ identifier. Both the
// arg classes themselves and the per-instance Box<T> clones
// (Box_1_Cases_A_B) end up with one name for two types -- duplicate type at
// build time or silent overwrite. net48 runs all three lines byte-exact
// (distinct runtime types, distinct Value()).
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
    public bool IsNull() { return (object)item == null; }  // directed -> per-instance
}

class A_B {
    public int Value() { return 1; }
}

class A {
    public class B {
        public int Value() { return 2; }
    }
}

public static class PNameDiv {
    public static void Main() {
        Box<A_B> b1 = new Box<A_B>();
        b1.Set(new A_B());
        Console.WriteLine(b1.Get().Value());   // 1
        Console.WriteLine(b1.IsNull());        // False

        Box<A.B> b2 = new Box<A.B>();
        b2.Set(new A.B());
        Console.WriteLine(b2.Get().Value());   // 2
        Console.WriteLine(b2.IsNull());        // False
    }
}}