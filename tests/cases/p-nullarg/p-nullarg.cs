// p-nullarg.cs -- GN-0 probe: Nullable<T> generic argument (GENERICS-FULL-
// ARG-GAPS.md 1.3/5, P-nullarg). Locks the status quo; no implementation
// changes in this batch (GN-2 repair, which is gated on this probe).
//
// The gap: `int?` decodes to the nested name "System.Nullable`1_int";
// TryClassGenericInstanceKind returns null for the BCL TypeRef def
// (MethodBodyReader.cs:526-527) so the per-instance clone is never
// synthesized, and box has no Nullable arm (CodeGenerator.cs:8182, default
// throw). net48 runs all four lines byte-exact.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PNullArg {
    public static void Main() {
        // ---- 1. Nullable<int> value round-trip ----
        Box<int?> bn = new Box<int?>();
        bn.Set(42);
        int? n = bn.Get();
        Console.WriteLine(n.HasValue);   // True
        Console.WriteLine(n.Value);      // 42

        // ---- 2. Nullable<int> null round-trip ----
        Box<int?> bn2 = new Box<int?>();
        bn2.Set(null);
        int? n2 = bn2.Get();
        Console.WriteLine(n2.HasValue);  // False
        Console.WriteLine(n2 == null);   // True
    }
}}
