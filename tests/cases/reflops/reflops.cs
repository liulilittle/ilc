//------------------------------------------------------------------------------
// reflops.cs -- REFLECTION DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C10 (2026-08-09).
//
// Goes DEEPER than reflectcase/refinvoke on the wired reflection surface:
//   * typeof folding -> the full-name string (the simplified Type model);
//     typeof(X).Name -> the SHORT name (StackFold fold, net48 semantics).
//   * GetType() on an INSTANCE -> icall_object_get_type (full name), then
//     MemberInfo::get_Name -> il2cpp_type_get_short_name.
//   * Type.GetType(string) -> il2cpp_type_get_type (typeinfo registry by
//     name): USER types resolve (entry.cpp registers every reflected type
//     via il2cpp_typeinfo_register), BCL types miss (GAP-REFL-1 residual,
//     recorded), no-such -> null (.NET contract, no throw).
//   * typeof(X).GetHashCode() -> Object.GetHashCode -> icall_object_get_
//     hash_code on the folded full-name STRING (ordinal content hash):
//     CONSISTENCY probes only (same type -> same value), never the hash
//     VALUE (the runtime's string hash differs from net48's per-RuntimeType
//     hash -- byte-stability demands True/False, not raw ints).
//   * TypeInfo table queries (MethodMap.Type.cs -> il2cpp_typeinfo_*):
//     GetMethod/GetField on a property-free user type resolve through the
//     registered typeinfo table (member-name string or null).
//
// RECORDED GAPS (see the DEFECTS at the bottom):
//   D1 (typeinfo exclusion for property owners): a type with a property
//     (auto-property accessors) is excluded from TypeInfo emission
//     (CodeGenerator.cs:395 foldedAccessorOwners -> noTypeInfoOwners), so
//     ReflProp.GetMethod/GetField/Type.GetType("Cases.ReflProp") ALL miss
//     (null) in this build while net48 resolves them. The property-free
//     ReflA/ReflB are registered and their queries pass.
//   D2 (BCL registry miss, GAP-REFL-1 residual): Type.GetType("System.Int32")
//     -> null (the typeinfo registry holds only user-assembly types).
//   D3 (property sub-table empty): GetProperty returns null even for a real
//     property (PropertyDef model not landed, TypeEmitter.cs); only the
//     not-found -> null shape is byte-stable and probed on a registered
//     type. (ReflProp itself is excluded by D1 anyway -- probe a no-prop
//     type's GetProperty("NoProp") -> null.)
//
// Every active line prints "label=True/False" (byte-stable across console
// encodings). Type.op_Equality (`==` on Types) is avoided (reflectcase
// precedent: the op_* INSTANCE misclassification), ReferenceEquals forms
// are used for identity probes.
//------------------------------------------------------------------------------
using System;

namespace Cases {
public class ReflA {
    public int FieldA;
    public int Add(int a, int b) { return a + b; }
    public int Mul(int a, int b) { return a * b; }
}
public class ReflB {
    public int FieldB;
    public int Sub(int a, int b) { return a - b; }
}
public class ReflProp {
    public int PropA { get; set; }
    public int Double(int a) { return a * 2; }
}

public static class ReflOps {
    public static void Main() {
        // ================= A. typeof folding + short name =================
        Console.WriteLine("tf_a=" + (typeof(ReflA).Name == "ReflA"));
        Console.WriteLine("tf_b=" + (typeof(ReflB).Name == "ReflB"));
        Console.WriteLine("tf_string=" + (typeof(string).Name == "String"));
        Console.WriteLine("tf_int=" + (typeof(int).Name == "Int32"));
        Console.WriteLine("tf_not=" + (typeof(ReflA).Name != "ReflB"));

        // ============ B. GetType() instance -> short name ============
        Console.WriteLine("gt_a=" + (new ReflA().GetType().Name == "ReflA"));
        Console.WriteLine("gt_b=" + (new ReflB().GetType().Name == "ReflB"));

        // ================= C. Type.GetType(string) =================
        // USER type in the same assembly: net48 calling-assembly lookup
        // resolves it; ours resolves through the typeinfo registry (both
        // non-null for the registered property-free types).
        Console.WriteLine("gettype_user=" + !ReferenceEquals(Type.GetType("Cases.ReflA"), null));
        Console.WriteLine("gettype_user2=" + !ReferenceEquals(Type.GetType("Cases.ReflB"), null));
        // BCL type: net48 finds System.Int32 (mscorlib); our registry holds
        // only user-assembly types -> null (RECORDED GAP D2).
        Console.WriteLine("gettype_bcl=" + !ReferenceEquals(Type.GetType("System.Int32"), null));
        // no-such type -> null on both (.NET contract, no throw).
        Console.WriteLine("gettype_none=" + ReferenceEquals(Type.GetType("No.Such.Type"), null));

        // ============ D. GetHashCode consistency ============
        // Consistency only: the same type value hashes to the same int
        // (both runtimes; never compare the raw hash values byte-for-byte).
        Console.WriteLine("hc_same_a=" + (typeof(ReflA).GetHashCode() == typeof(ReflA).GetHashCode()));
        Console.WriteLine("hc_same_b=" + (typeof(ReflB).GetHashCode() == typeof(ReflB).GetHashCode()));
        Console.WriteLine("hc_diff=" + (typeof(ReflA).GetHashCode() != typeof(ReflB).GetHashCode()));
        Console.WriteLine("hc_notint=" + (typeof(ReflA).GetHashCode() != typeof(string).GetHashCode()));

        // ============ E. TypeInfo table queries (registered types) ============
        Console.WriteLine("tm_method=" + !ReferenceEquals(typeof(ReflA).GetMethod("Add"), null));
        Console.WriteLine("tm_method2=" + !ReferenceEquals(typeof(ReflB).GetMethod("Sub"), null));
        Console.WriteLine("tm_method_none=" + ReferenceEquals(typeof(ReflA).GetMethod("NoSuch"), null));
        Console.WriteLine("tm_field=" + !ReferenceEquals(typeof(ReflA).GetField("FieldA"), null));
        Console.WriteLine("tm_field2=" + !ReferenceEquals(typeof(ReflB).GetField("FieldB"), null));
        Console.WriteLine("tm_field_none=" + ReferenceEquals(typeof(ReflA).GetField("NoField"), null));
        Console.WriteLine("tm_prop_none=" + ReferenceEquals(typeof(ReflA).GetProperty("NoProp"), null));

        // ============ F. RECORDED GAP D1: property-owner exclusion ============
        // ReflProp has an auto-property -> excluded from TypeInfo emission
        // (foldedAccessorOwners). net48 resolves these; ours returns null.
        Console.WriteLine("d1_prop_getmethod=" + !ReferenceEquals(typeof(ReflProp).GetMethod("Double"), null));
        Console.WriteLine("d1_prop_gettype=" + !ReferenceEquals(Type.GetType("Cases.ReflProp"), null));
        Console.WriteLine("d1_prop_getprop=" + !ReferenceEquals(typeof(ReflProp).GetProperty("PropA"), null));
    }
}}
