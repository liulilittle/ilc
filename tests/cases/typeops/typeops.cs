// typeops.cs -- deep System.Type e2e reference case (net48 baseline).
//
// DEEP probes of the wired Type domain (MethodMap.Type.cs simplified model:
// a Type VALUE is the full type name as a managed string; typeof folds to a
// constant, GetType() produces the name through the runtime descriptor).
//
//   1) typeof FOLDING: .Name short names for primitives, a user class, an
//      enum and a user struct (no boxing -- ldtoken folds to a constant).
//   2) GetType() SHORT names: boxed primitives + user class instances.
//   3) FullName / IsValueType / IsEnum: NOT probeable -- MethodMap.Type.cs
//      has no get_FullName / get_IsValueType / get_IsEnum mappings, so the
//      resolver falls to the generated-name path and emits UNDEFINED symbols
//      (System_Type_get_FullName_<hash> / get_IsValueType_<hash> /
//      get_IsEnum_<hash>), failing the link. Recorded as DEFECT-TYPE-2/3/4
//      in GAPS.md -- these probes are NOT called.
//   5) GetHashCode consistency: the same Type must hash equal across calls.
//   6) Equals: NOT probeable -- System.Type.Equals(object) requires a
//      TRACKED System.Type argument (MethodMap.Type.cs icall_type_equals
//      gate) and no user-code shape produces one today (typeof folds to an
//      UNTRACKED name string; locals are not tracked either). Recorded as
//      DEFECT-TYPE-1 in GAPS.md -- the EQUALS probes are NOT called.
//   7) Reference equality: object.ReferenceEquals / op_Equality (==) on the
//      folded Type values (net48 caches one RuntimeType per type).
//
// All probes print bools. GetHashCode / ReferenceEquals / == are the batch-C2
// expansion surface that IS wired; the unwired members are recorded in
// GAPS.md and NOT called.

using System;

namespace Cases {
    enum Color { Red, Green, Blue }
    struct Point { public int X; public int Y; }
    class Animal { }

    public static class TypeOps {
        public static void Main() {
            // ---- 1) typeof folding -> .Name short names ----
            Console.WriteLine(typeof(int).Name == "Int32");           // True
            Console.WriteLine(typeof(string).Name == "String");       // True
            Console.WriteLine(typeof(bool).Name == "Boolean");        // True
            Console.WriteLine(typeof(double).Name == "Double");       // True
            Console.WriteLine(typeof(Animal).Name == "Animal");       // True
            Console.WriteLine(typeof(Color).Name == "Color");         // True
            Console.WriteLine(typeof(Point).Name == "Point");         // True
            Console.WriteLine(typeof(TypeOps).Name == "TypeOps");     // True

            // ---- 2) GetType() short names (boxed prims + class instances) ----
            Console.WriteLine("hi".GetType().Name == "String");       // True
            Console.WriteLine(42.GetType().Name == "Int32");          // True
            Console.WriteLine((3.14).GetType().Name == "Double");     // True
            Console.WriteLine(new Animal().GetType().Name == "Animal"); // True

            // ---- 3) FullName / IsValueType / IsEnum: gap DEFECT-TYPE-2/3/4
            // (see GAPS.md) -- unwired getters emit undefined generated
            // symbols; NOT called here.

            // ---- 5) GetHashCode consistency (same type -> same hash) ----
            Console.WriteLine(typeof(int).GetHashCode() == typeof(int).GetHashCode());       // True
            Console.WriteLine(typeof(string).GetHashCode() == typeof(string).GetHashCode()); // True
            Console.WriteLine(typeof(int).GetHashCode() != typeof(string).GetHashCode());    // True
            Console.WriteLine(typeof(Color).GetHashCode() == typeof(Color).GetHashCode());   // True

            // ---- reference equality ----
            Console.WriteLine(object.ReferenceEquals(typeof(int), typeof(int)));  // True (cached RuntimeType)
            Console.WriteLine(object.ReferenceEquals(typeof(string), typeof(string))); // True
            Console.WriteLine(object.ReferenceEquals(typeof(int), typeof(string)));    // False
            Console.WriteLine(typeof(string) == typeof(string));         // True (op_Equality)
            Console.WriteLine(typeof(string) == typeof(int));            // False
            Console.WriteLine(typeof(int) == typeof(int));           // True
            Console.WriteLine(typeof(Animal) == typeof(Color));      // False
        }
    }
}
