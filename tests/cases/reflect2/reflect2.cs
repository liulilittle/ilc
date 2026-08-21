//------------------------------------------------------------------------------
// reflect2.cs -- REFLECTION MODE B/C DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C11 (2026-08-09).
//
// Goes DEEPER than reflectcase/reflops on the wired reflection surface,
// focused on the mode B/C acceptance surface landed at HEAD (abe0999 --
// "reflection mode C NormalizeTypeName verified (gettype_bcl True)"):
//   * typeof folding -> the full-name string + short name (StackFold fold).
//   * Type.GetType(string) through the typeinfo registry + NormalizeTypeName
//     (reflect_base.cpp:135): PLAIN name, ASSEMBLY-QUALIFIED name
//     ("Ns.Type, Assembly" -> stripped), LEADING-SPACE skip, EMPTY-assembly
//     reject (",") -> null, BCL full-qualified name ("System.Int32,
//     mscorlib, Version=...") -> "System.Int32" -> the class_by_name BCL
//     fallback, no-such -> null.
//   * Type.GetTypeCode fold (StackFold IsTypeGetTypeCode: typeof(X) ->
//     typecode constant).
//   * TypeInfo table queries on a REGISTERED property-free type (mode B/C:
//     GetMethod/GetField hit + miss -> null; GetProperty on a type with no
//     property -> null).
//   * GetHashCode CONSISTENCY probes (same type -> same int; never the raw
//     hash value -- the runtime string-hash differs from net48's per-RuntimeType).
//
// RECORDED GAPS (see the batch C11 report):
//   D2 (BCL registry miss, residual): Type.GetType("System.Int32") -> the
//     BUILTIN class registry (il2cpp_class_by_name) now resolves BCL types
//     (gettype_bcl True at HEAD); only the "Cases.*" user types resolve
//     through the typeinfo registry -- the assembly-qualified USER names
//     exercise the same NormalizeTypeName strip.
//   Nested '+' types ("Cases.Outer+Inner") are probed but MAY miss in the
//     typeinfo registry (entry.cpp registration covers reflected types; the
//     nested FullName "+" form is documented in NormalizeTypeName rule 3).
//
// Byte-stability: every active line prints "label=True/False". Type
// op_Equality (`==` on Types) is avoided (reflectcase precedent: op_*
// INSTANCE misclassification) -- ReferenceEquals / != null forms only.
//------------------------------------------------------------------------------
using System;

namespace Cases {
public class Refl2A {
    public int FieldA;
    public int FieldB;
    public int Add(int a, int b) { return a + b; }
    public int Mul(int a, int b) { return a * b; }
    public string Greet() { return "hi"; }
}
public class Refl2B {
    public int FieldC;
    public int Sub(int a, int b) { return a - b; }
}
public class Outer {
    public class Inner {
        public int FieldI;
        public int Get() { return FieldI; }
    }
    public int FieldO;
}

public static class Reflect2 {
    public static void Main() {
        // ================= A. typeof folding + short name =================
        Console.WriteLine("tf_a=" + (typeof(Refl2A).Name == "Refl2A"));
        Console.WriteLine("tf_b=" + (typeof(Refl2B).Name == "Refl2B"));
        Console.WriteLine("tf_int=" + (typeof(int).Name == "Int32"));
        Console.WriteLine("tf_string=" + (typeof(string).Name == "String"));
        Console.WriteLine("tf_bool=" + (typeof(bool).Name == "Boolean"));
        Console.WriteLine("tf_not=" + (typeof(Refl2A).Name != "Refl2B"));

        // ============ B. Type.GetTypeCode fold (typeof -> constant) ============
        Console.WriteLine("tc_int=" + (Type.GetTypeCode(typeof(int)) == TypeCode.Int32));
        Console.WriteLine("tc_string=" + (Type.GetTypeCode(typeof(string)) == TypeCode.String));
        Console.WriteLine("tc_bool=" + (Type.GetTypeCode(typeof(bool)) == TypeCode.Boolean));

        // ============ C. Type.GetType(string): mode C NormalizeTypeName ============
        // Plain user name (calling-assembly + registry hit).
        Console.WriteLine("gt_user=" + !ReferenceEquals(Type.GetType("Cases.Refl2A"), null));
        Console.WriteLine("gt_user2=" + !ReferenceEquals(Type.GetType("Cases.Refl2B"), null));
        // Assembly-qualified USER name: ", reflect2" stripped -> "Cases.Refl2A".
        Console.WriteLine("gt_user_asm=" + !ReferenceEquals(Type.GetType("Cases.Refl2A, reflect2"), null));
        // BCL names: plain, assembly-qualified, full Version/Culture/Token.
        Console.WriteLine("gt_bcl=" + !ReferenceEquals(Type.GetType("System.Int32"), null));
        Console.WriteLine("gt_bcl_asm=" + !ReferenceEquals(Type.GetType("System.Int32, mscorlib"), null));
        Console.WriteLine("gt_bcl_full=" + !ReferenceEquals(
            Type.GetType("System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"), null));
        Console.WriteLine("gt_bcl_str=" + !ReferenceEquals(Type.GetType("System.String"), null));
        // Leading-space skip (net48 + NormalizeTypeName both resolve).
        Console.WriteLine("gt_lead=" + !ReferenceEquals(Type.GetType(" System.Int32"), null));
        // Empty assembly part (",") -> null on both.
        Console.WriteLine("gt_emptyasm=" + ReferenceEquals(Type.GetType("System.Int32,"), null));
        // No-such type -> null (.NET contract, no throw).
        Console.WriteLine("gt_none=" + ReferenceEquals(Type.GetType("No.Such.Type"), null));
        // Nested '+' form (NormalizeTypeName rule 3; registry MAY miss).
        Console.WriteLine("gt_nested=" + !ReferenceEquals(Type.GetType("Cases.Outer+Inner"), null));

        // ============ D. TypeInfo table: GetMethod/GetField on registered types ============
        Console.WriteLine("tm_add=" + !ReferenceEquals(typeof(Refl2A).GetMethod("Add"), null));
        Console.WriteLine("tm_mul=" + !ReferenceEquals(typeof(Refl2A).GetMethod("Mul"), null));
        Console.WriteLine("tm_greet=" + !ReferenceEquals(typeof(Refl2A).GetMethod("Greet"), null));
        Console.WriteLine("tm_sub=" + !ReferenceEquals(typeof(Refl2B).GetMethod("Sub"), null));
        Console.WriteLine("tm_none=" + ReferenceEquals(typeof(Refl2A).GetMethod("NoSuch"), null));
        Console.WriteLine("tf_fielda=" + !ReferenceEquals(typeof(Refl2A).GetField("FieldA"), null));
        Console.WriteLine("tf_fieldb=" + !ReferenceEquals(typeof(Refl2A).GetField("FieldB"), null));
        Console.WriteLine("tf_fieldc=" + !ReferenceEquals(typeof(Refl2B).GetField("FieldC"), null));
        Console.WriteLine("tf_none=" + ReferenceEquals(typeof(Refl2A).GetField("NoField"), null));
        // GetProperty on a property-free registered type -> null (D3).
        Console.WriteLine("tp_none=" + ReferenceEquals(typeof(Refl2A).GetProperty("NoProp"), null));

        // ============ E. GetHashCode consistency (never raw values) ============
        Console.WriteLine("hc_a=" + (typeof(Refl2A).GetHashCode() == typeof(Refl2A).GetHashCode()));
        Console.WriteLine("hc_b=" + (typeof(Refl2B).GetHashCode() == typeof(Refl2B).GetHashCode()));
        Console.WriteLine("hc_diff=" + (typeof(Refl2A).GetHashCode() != typeof(Refl2B).GetHashCode()));
        Console.WriteLine("hc_int=" + (typeof(int).GetHashCode() == typeof(int).GetHashCode()));
    }
}}
