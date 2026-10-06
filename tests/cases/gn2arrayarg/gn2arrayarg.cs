//------------------------------------------------------------------------------
// gn2arrayarg.cs -- GN-2 defect1 e2e probe: array-wrapped nested generic
// argument on a NON-SHARED (per-instance) generic definition.
//
// The shape `Box2<int?[]>` -- the array element is itself a generic
// instantiation (System.Nullable`1<int>, ELEMENT_TYPE_GENERICINST 0x15).
// Before this fix the DEFINITION side cloned "Box2_1__System_Nullable_1_int_"
// while the CALL side referenced "Box2_1_array", forking the clone name and
// failing the mingw build ("was not declared"). A shared definition masks the
// fork through the object collapse; a NON-shared definition (forced here by the
// by-value MVAR generic method Echo<U>) exposes it.
//
// Byte-stability: net48 is the reference baseline; every line must be
// byte-identical after the fix.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box2<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
    // GN-2 def1: a generic METHOD with a BY-VALUE MVAR parameter keeps the
    // whole definition on the per-instance (non-shared) path
    // (GenericUsageAnalyzer.MethodMvarShareable) -- sharing can no longer mask
    // a clone-name divergence.
    public static void Echo<U>(U u) { }
}

public static class Gn2ArrayArg {
    public static void Main() {
        int?[] arr = new int?[2];
        Box2<int?[]> b = new Box2<int?[]>();
        b.Set(arr);
        Console.WriteLine(b.Get() == arr);       // True: the stored ref round-trips
        Console.WriteLine(b.Get() != null);      // True: the generic slot holds the array ref
    }
}}