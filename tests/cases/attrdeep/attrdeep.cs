//------------------------------------------------------------------------------
// attrdeep.cs -- Attribute-domain DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C44. Probes the WIRED attribute-domain
// TYPE surface (typeof folds on System.Attribute classes + attribute-type
// identity/comparison semantics).
//
// D-ATTR-1 items c/d FIXED (this batch): the transpiler now reads the
// CustomAttribute table, constructs custom attribute INSTANCES for user
// attribute classes and registers them (il2cpp_attribute_register) at startup,
// and resolves BCL attribute class names through registered name descriptors
// (Type.GetType fallback) -- see probes 7/8 below.
//
// ACTIVE SURFACE (byte-exact, net48 == transpiled):
//   1) typeof FOLDS on System.Attribute classes (.Name short-name fold) --
//      ObsoleteAttribute, SerializableAttribute, FlagsAttribute,
//      AttributeUsageAttribute, the Attribute base class itself.
//   2) Attribute-TYPE identity: GetHashCode consistency (same type -> same
//      hash, different types -> different hashes) -- consistency only, never
//      the raw hash values (the runtime string-hash differs from net48's
//      per-RuntimeType hash).
//   3) ReferenceEquals identity (net48 caches one RuntimeType per type; the
//      transpiled fold interns one string per literal -- both stable).
//   4) GetTypeCode fold on an attribute class (net48: TypeCode.Object).
//   5) Custom attribute classes transpile (System.Attribute::.ctor no-op).
//   6) GetCustomAttributes managed-string ABI on a PLAIN class (empty
//      registry answer: empty array / null).
//   7) D-ATTR-1 item c: custom attribute REGISTRATION + query -- an annotated
//      user type's [Tagged] attribute is constructed and registered at
//      startup; GetCustomAttribute/GetCustomAttributes return the instance.
//   8) D-ATTR-1 item d: Type.GetType resolves BCL attribute class names
//      (net48 mscorlib; transpiled via registered name descriptors).
//
// Byte-stability: every line prints "label=True/False". No raw hash values,
// no attribute QUERY calls with numeric outputs, no custom attribute class
// declarations with non-constant ctor args.
// net48 is the reference baseline; every line must be byte-identical.
//------------------------------------------------------------------------------
using System;

namespace Cases {
public static class AttrDeep {
    public static void Main() {
        // ---- 1. typeof folds -> .Name short names on attribute classes ----
        Console.WriteLine(typeof(ObsoleteAttribute).Name == "ObsoleteAttribute");     // True
        Console.WriteLine(typeof(SerializableAttribute).Name == "SerializableAttribute"); // True
        Console.WriteLine(typeof(FlagsAttribute).Name == "FlagsAttribute");           // True
        Console.WriteLine(typeof(AttributeUsageAttribute).Name == "AttributeUsageAttribute"); // True
        Console.WriteLine(typeof(Attribute).Name == "Attribute");                     // True
        Console.WriteLine(typeof(ObsoleteAttribute).Name != typeof(SerializableAttribute).Name); // True

        // ---- 2. attribute-type GetHashCode CONSISTENCY (never raw values) ----
        Console.WriteLine(typeof(ObsoleteAttribute).GetHashCode() == typeof(ObsoleteAttribute).GetHashCode()); // True
        Console.WriteLine(typeof(ObsoleteAttribute).GetHashCode() != typeof(SerializableAttribute).GetHashCode()); // True
        Console.WriteLine(typeof(Attribute).GetHashCode() == typeof(Attribute).GetHashCode()); // True

        // ---- 3. ReferenceEquals identity (net48 RuntimeType cache == fold intern) ----
        // One operand of each probe is read back out of an array slot so the
        // emitted C++ compares a MATERIALIZED value against a fresh fold
        // (`reIds[0] == g_strPool[5]`) instead of a textual self-compare
        // (`g_strPool[5] == g_strPool[5]`), which g++ flags with
        // -Wtautological-compare. A plain local is not enough: the fold
        // propagates the constant straight into the comparison. Semantics are
        // unchanged -- and arguably strengthened: the probe still fails if the
        // two typeof folds ever diverge.
        Type[] reIds = { typeof(ObsoleteAttribute), typeof(Attribute) };
        Console.WriteLine(object.ReferenceEquals(reIds[0], typeof(ObsoleteAttribute))); // True
        Console.WriteLine(object.ReferenceEquals(reIds[1], typeof(Attribute))); // True

        // ---- 4. GetTypeCode fold on an attribute class ----
        Console.WriteLine(Type.GetTypeCode(typeof(ObsoleteAttribute)) == TypeCode.Object); // True

        // ---- 5. D-ATTR-1 fix: custom attribute classes transpile (System.Attribute::.ctor no-op) ----
        Console.WriteLine(typeof(Marker).Name == "Marker");       // True
        Console.WriteLine(typeof(Tagged).Name == "Tagged");       // True
        Console.WriteLine(new Marker() != null);                   // True
        Console.WriteLine(new Tagged() != null);                   // True
        Console.WriteLine(typeof(Marker) == typeof(Marker));       // True

        // ---- 6. D-ATTR-1 fix: GetCustomAttributes managed-string ABI ----
        // The transpiler emits il2cpp_attribute_get_custom_named(type-name
        // string) / get_single_named; the probe element is a PLAIN class
        // (base chain = System.Object) -- no registered attributes, so the
        // net48-exact answers are: empty array (never null) for the plural,
        // null for the singular.
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(Plain)).Length == 0); // True
        Console.WriteLine(Attribute.GetCustomAttribute(typeof(Plain), typeof(ObsoleteAttribute)) == null); // True

        // ---- 7. D-ATTR-1 item c FIX: custom attribute registration + query ----
        // Annotated carries [Tagged]; the transpiler constructs the Tagged
        // instance at startup, registers it under Annotated's name, and the
        // queries return it (singular: the instance; plural: length 1).
        Console.WriteLine(Attribute.GetCustomAttribute(typeof(Annotated), typeof(Tagged)) != null); // True
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(Annotated)).Length == 1); // True

        // ---- 8. D-ATTR-1 item d FIX: Type.GetType resolves BCL attribute classes ----
        // net48 resolves these from mscorlib; the transpiled side answers via
        // the registered BCL attribute class name descriptors (class_by_name
        // fallback) -- non-null, matching net48. The (object) cast lowers the
        // null test to a plain reference comparison (System.Type::op_Inequality
        // has no fold in the transpiler).
        Console.WriteLine((object)Type.GetType("System.ObsoleteAttribute") != null); // True
        Console.WriteLine((object)Type.GetType("System.FlagsAttribute") != null);    // True
    }

    // D-ATTR-1: custom attribute classes (System.Attribute::.ctor no-op wiring).
    class Marker : Attribute { }
    class Tagged : Attribute { }
    // D-ATTR-1 query probe element: plain class (no attribute chain).
    class Plain { }
    // D-ATTR-1 item c query probe element: annotated with the user attribute class.
    [Tagged]
    class Annotated { }
}}
