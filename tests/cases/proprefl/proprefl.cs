//------------------------------------------------------------------------------
// proprefl.cs -- REFLECTION PROPERTY DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C13 (2026-08-09).
//
// Goes DEEPER than reflops on the TypeInfo PROPERTY SUBTABLE that batch 40
// landed (PropertyDef model, MetadataLoader.PropertyDefinition + TypeEmitter
// Il2CppPropertyInfo emission, typeinfo.h managed-string query). reflops
// proves the d1_prop trio (getmethod/gettype/getprop on a property-owner
// type) all resolve True; proprefl proves the property subtable holds under:
//   * GetProperty on a type with MULTIPLE properties (a real table scan, not
//     a single-entry special case).
//   * GetProperty on a GETTER-ONLY property and a SETTER-ONLY property (the
//     accessor-name slots are independent -- Il2CppPropertyInfo.getterName /
//     setterName are separately nullable).
//   * GetProperty INHERITED through the baseType chain (FindMemberThrough-
//     BaseChain walks own table first, base chain second -- the derived
//     table must NOT shadow a base property, the base table must be found).
//   * GetProperty NOT-FOUND on a type WITH a property table (must return
//     null, NOT throw; .NET contract) and NOT-FOUND on a type with NO
//     properties (null properties table, propertyCount 0).
//   * GetMethod/GetField still resolve on the same property-owner types
//     (the property subtable must COEXIST with the methods/fields tables --
//     no regression to the old foldedAccessorOwners whole-type exclusion).
//   * Type.GetType(string) resolves a property-owner type (registry holds
//     it, not just GetProperty).
//
// The simplified member model (typeinfo.h): a GetProperty/GetMethod/
// GetField RESULT VALUE is the member-NAME STRING on a hit, nullptr on a
// miss -- so every probe is `!ReferenceEquals(..., null)` / ReferenceEquals
// (byte-stable bool), never the member object's identity or value.
//
// RECORDED GAPS (see GAPS.md): (1) GetProperty on a type WITHOUT a property
// table (a field/method-only type) -- the null-properties-table shape is
// probed here and passes, but a type that is EXCLUDED from TypeInfo emission
// entirely (un-reflected in mode B) would return null while net48 resolves
// it; this build has no manifest so every type emits (no live divergence).
// (2) Type.op_Equality (`==` on Types) is avoided per reflectcase precedent.
//------------------------------------------------------------------------------
using System;
namespace Cases {

public class PropMulti {
    public int Alpha { get; set; }
    public int Beta { get; set; }
    public int Gamma { get; set; }
}

public class PropGetterOnly {
    public int Value { get { return 42; } }
}

public class PropSetterOnly {
    private int _v;
    public int Value { set { _v = value; } }
}

public class PropBase {
    public int BaseProp { get; set; }
    public int BaseMethod(int x) { return x + 1; }
}

public class PropDerived : PropBase {
    public int DerivedProp { get; set; }
    public int DerivedMethod(int x) { return x * 2; }
}

public class PropNoProperties {
    public int FieldOnly;
    public int MethodOnly(int x) { return x; }
}

public static class PropRefl {
    public static void Main() {
        // ========= A. multiple properties on one type =========
        Console.WriteLine("multi_a=" + !ReferenceEquals(typeof(PropMulti).GetProperty("Alpha"), null));
        Console.WriteLine("multi_b=" + !ReferenceEquals(typeof(PropMulti).GetProperty("Beta"), null));
        Console.WriteLine("multi_g=" + !ReferenceEquals(typeof(PropMulti).GetProperty("Gamma"), null));

        // ========= B. getter-only and setter-only properties =========
        Console.WriteLine("getonly=" + !ReferenceEquals(typeof(PropGetterOnly).GetProperty("Value"), null));
        Console.WriteLine("setonly=" + !ReferenceEquals(typeof(PropSetterOnly).GetProperty("Value"), null));

        // ========= C. inherited property through the base chain =========
        Console.WriteLine("base_own=" + !ReferenceEquals(typeof(PropBase).GetProperty("BaseProp"), null));
        Console.WriteLine("derived_inherit=" + !ReferenceEquals(typeof(PropDerived).GetProperty("BaseProp"), null));
        Console.WriteLine("derived_own=" + !ReferenceEquals(typeof(PropDerived).GetProperty("DerivedProp"), null));

        // ========= D. not-found on a type WITH a property table =========
        Console.WriteLine("nf_multi=" + ReferenceEquals(typeof(PropMulti).GetProperty("NoSuch"), null));
        Console.WriteLine("nf_derived=" + ReferenceEquals(typeof(PropDerived).GetProperty("NoSuch"), null));

        // ========= E. not-found on a type with NO properties =========
        Console.WriteLine("nf_noprops=" + ReferenceEquals(typeof(PropNoProperties).GetProperty("FieldOnly"), null));

        // ========= F. methods/fields COEXIST on property-owner types =========
        Console.WriteLine("m_multi=" + ReferenceEquals(typeof(PropMulti).GetMethod("Alpha"), null)); // property name is NOT a method
        Console.WriteLine("gm_derived=" + !ReferenceEquals(typeof(PropDerived).GetMethod("DerivedMethod"), null));
        Console.WriteLine("gm_base_inherit=" + !ReferenceEquals(typeof(PropDerived).GetMethod("BaseMethod"), null));
        Console.WriteLine("gf_noprops=" + !ReferenceEquals(typeof(PropNoProperties).GetField("FieldOnly"), null));
        Console.WriteLine("gf_base=" + !ReferenceEquals(typeof(PropBase).GetField("BaseProp"), null)); // backing field name differs -> null

        // ========= G. Type.GetType(string) on a property-owner type =========
        Console.WriteLine("gt_multi=" + !ReferenceEquals(Type.GetType("Cases.PropMulti"), null));
        Console.WriteLine("gt_getonly=" + !ReferenceEquals(Type.GetType("Cases.PropGetterOnly"), null));
        Console.WriteLine("gt_derived=" + !ReferenceEquals(Type.GetType("Cases.PropDerived"), null));
    }
}}
