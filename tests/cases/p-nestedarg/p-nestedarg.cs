// p-nestedarg.cs -- GN-0 probe: NESTED generic argument Foo<List<int>>
// (GENERICS-FULL-ARG-GAPS.md 1.8/5, P-nestedarg). Locks the status quo; no
// implementation changes in this batch (GN-4 repair, gated on this probe).
//
// The gap: a complex argument (List<int>) makes TryClassGenericInstanceKind
// return null (MethodBodyReader.cs:536-538: "complex argument: keep legacy
// kind") so the definition-side clone Foo_...List_1_int is never synthesized,
// while the call side emits that name (or collapses to Foo_object with no
// object-clone scan source, gap 2c) -- an unresolved symbol at ILC link time.
// net48 runs all three lines byte-exact.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PNestedArg {
    public static void Main() {
        // ---- 1. List<int> nested generic argument ----
        Box<List<int>> bl = new Box<List<int>>();
        bl.Set(new List<int>());
        Console.WriteLine(bl.Get() != null);         // True

        // ---- 2. List<string> nested generic argument (default slot) ----
        Box<List<string>> bls = new Box<List<string>>();
        Console.WriteLine(bls.Get() == null);        // True
    }
}}