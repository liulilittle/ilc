// p-gn14o1.cs -- GN-14 (c) probe: an arity>=2 closed clone whose per-argument
// VAR slots MIX a reference argument (T1 = int[,] -> a GC reference) and a
// value argument (T2 = int -> a 32-bit value).
//
// HEAD defect (O-1): the declaration/call faces classified ANY non-"int" whole
// InstantiationSuffix as a reference, so BOTH pair fields rendered
// Il2CppObject* and the ctor took (Il2CppObject*, Il2CppObject*, Il2CppObject*).
// The value argument (5) then could not convert -> g++ "invalid conversion".
// Fix: a per-argument CloneArgSlotIsRef(varIndex) helper (arity-1 / invalid
// index keeps the legacy whole-suffix classifier).
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Pair<A, B> {
    A a; B b;
    public Pair(A x, B y) { a = x; b = y; }
    public A First() { return a; }
    public B Second() { return b; }
}

public static class PGn14O1 {
    public static void Main() {
        Pair<int[,], int> p = new Pair<int[,], int>(null, 5);
        Console.WriteLine(p.First() == null);
        Console.WriteLine(p.Second());
    }
}}