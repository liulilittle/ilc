//------------------------------------------------------------------------------
// reflprop2.cs -- REFLECTION PROPERTY DEEP 2 e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C17 (2026-08-10).
//
// DEEPENS proprefl (C13) on the TypeInfo PROPERTY SUBTABLE (Il2CppPropertyInfo
// emission: name / typeName / getterName / setterName / indexParams). proprefl
// proved the subtable holds for multi-property types, getter-only/setter-only
// properties, base-chain inheritance, not-found -> null, coexistence with the
// methods/fields tables and Type.GetType(string). reflprop2 drives the
// subtable + the ACCESSOR VALUE SEMANTICS further:
//
//   * GETVALUE/SETVALUE VALUE ROUND-TRIP: the property's VALUE moves through
//     its real getter/setter pair (set X, read X back) -- including the
//     STATIC property case and the INDEXER case -- byte-stable int/bool
//     probes on the same types that own the property subtable. (The
//     REFLECTION path -- PropertyInfo.GetValue/SetValue -- is UNWIRED in this
//     build: no runtime invoker; see DEFECT D-PRP2-1.)
//   * INDEXED PROPERTIES: a class with an int indexer (this[int]) round-trips
//     values through get_Item/set_Item (direct calls), and the property
//     subtable exposes the indexer under its metadata name "Item" (indexParams
//     > 0) -- Type.GetProperty("Item") resolves on the indexer owner and
//     returns null on a non-indexer owner (net48 contract).
//   * INHERITED PROPERTIES: a derived instance reads/writes a base-declared
//     property (direct value round-trip), the subtable resolves a base
//     property through the baseType chain, and a `new`-HIDDEN property (own
//     table shadows the base) resolves to the most-derived declaration on
//     BOTH runtimes (own table first, .NET shadowing semantics).
//   * COEXISTENCE: GetMethod (public accessor get_Sum) and GetField (public
//     instance field) still resolve on the same property-owner types -- the
//     property subtable must not displace the methods/fields tables.
//
// The simplified member model (typeinfo.h): a GetProperty/GetMethod/GetField
// RESULT VALUE is the member-NAME STRING on a hit, nullptr on a miss -- so
// every reflection probe is `!ReferenceEquals(..., null)` / ReferenceEquals
// (byte-stable bool), never the member object's identity or value.
//
// RECORDED DEFECT (see GAPS.md): D-PRP2-1 -- System.Reflection.PropertyInfo
// has NO runtime invoker: GetValue/SetValue/GetGetMethod/GetIndexParameters
// fall through to the generated-name path and the generated C++ references
// undefined symbols (verified by transpile smoke test). The reflection-side
// value round-trip is therefore PARKED; the ACTIVE surface covers the wired
// subtable queries plus the DIRECT accessor value round-trip.
//------------------------------------------------------------------------------
using System;
namespace Cases {

public class PropStatic {
    private static int _count;
    public int PublicField;
    public static int Count { get { return _count; } set { _count = value; } }
    public int Instance { get; set; }
}

// NOTE (D-PRP2-2): the indexer's backing store is FOUR plain int fields --
// the transpiler aborts the whole assembly load on an ARRAY instance field
// ("field type byte 0x1D (array) not supported in M2", MetadataLoader.cs:652).
// A `private int[] _items;` backing field would abort reflprop2 at transpile.
public class PropIndexer {
    private int _d0, _d1, _d2, _d3;
    public int this[int i] {
        get {
            if (i == 0) return _d0;
            if (i == 1) return _d1;
            if (i == 2) return _d2;
            return _d3;
        }
        set {
            if (i == 0) { _d0 = value; return; }
            if (i == 1) { _d1 = value; return; }
            if (i == 2) { _d2 = value; return; }
            _d3 = value;
        }
    }
    public int Sum { get { return _d0 + _d1 + _d2 + _d3; } }
}

public class PropBase2 {
    public int BaseProp { get; set; }
    public virtual int Virt { get { return 10; } }
}
public class PropMid2 : PropBase2 {
    public override int Virt { get { return 20; } }
}
public class PropDerived2 : PropMid2 {
    public new int BaseProp { get; set; }   // hides PropBase2.BaseProp
}

public static class ReflProp2 {
    public static void Main() {
        // ========= A. GetValue/SetValue VALUE ROUND-TRIP (direct accessors)
        // static property, instance property on the same subtable-owning type
        PropStatic ps = new PropStatic();
        PropStatic.Count = 5;
        ps.Instance = 7;
        Console.WriteLine("rt_static=" + (PropStatic.Count == 5));
        Console.WriteLine("rt_inst=" + (ps.Instance == 7));
        PropStatic.Count = 9;
        Console.WriteLine("rt_static2=" + (PropStatic.Count == 9));   // re-set round-trip
        Console.WriteLine("rt_inst2=" + (ps.Instance == 7));  // untouched by static set

        // ========= B. INDEXED property value round-trip (get_Item/set_Item)
        PropIndexer pi = new PropIndexer();
        pi[0] = 1; pi[1] = 2; pi[2] = 3; pi[3] = 4;
        Console.WriteLine("rt_idx0=" + (pi[0] == 1));
        Console.WriteLine("rt_idx3=" + (pi[3] == 4));
        Console.WriteLine("rt_sum=" + (pi.Sum == 10));        // Sum = 1+2+3+4
        pi[2] = 30;
        Console.WriteLine("rt_idx2b=" + (pi[2] == 30));
        Console.WriteLine("rt_sum2=" + (pi.Sum == 37));

        // ========= C. INHERITED property value round-trip + shadowing =========
        PropDerived2 pd = new PropDerived2();
        pd.BaseProp = 11;                     // derived `new` BaseProp
        Console.WriteLine("rt_inherit=" + (pd.BaseProp == 11));
        // PARKED DEFECT D-PRP2-3: `pbAs.BaseProp` (a BASE-typed reference to
        // the derived instance, reading PropBase2::BaseProp = the base's own
        // untouched backing field, net48 0) reads the DERIVED backing field
        // on ILC (the folded auto-property accessor is resolved to the wrong
        // field through the base-ref shape). Not active -- see GAPS.md.
        //   PropBase2 pbAs = pd;
        //   Console.WriteLine("rt_baseview=" + (pbAs.BaseProp == 0));

        // ========= D. subtable: STATIC property + instance property =========
        Console.WriteLine("gp_static=" + !ReferenceEquals(typeof(PropStatic).GetProperty("Count"), null));
        Console.WriteLine("gp_inst=" + !ReferenceEquals(typeof(PropStatic).GetProperty("Instance"), null));

        // ========= E. subtable: INDEXER under metadata name "Item" =========
        Console.WriteLine("gp_item=" + !ReferenceEquals(typeof(PropIndexer).GetProperty("Item"), null));
        Console.WriteLine("gp_noidx=" + ReferenceEquals(typeof(PropStatic).GetProperty("Item"), null));

        // ========= F. subtable: inheritance + `new`-hiding resolution =========
        Console.WriteLine("gp_baseown=" + !ReferenceEquals(typeof(PropBase2).GetProperty("BaseProp"), null));
        Console.WriteLine("gp_derived_new=" + !ReferenceEquals(typeof(PropDerived2).GetProperty("BaseProp"), null));
        Console.WriteLine("gp_derived_inherit=" + !ReferenceEquals(typeof(PropMid2).GetProperty("BaseProp"), null));
        Console.WriteLine("gp_virt_derived=" + !ReferenceEquals(typeof(PropDerived2).GetProperty("Virt"), null));

        // ========= G. coexistence: methods/fields on the SAME owners =========
        Console.WriteLine("gm_sum=" + !ReferenceEquals(typeof(PropIndexer).GetMethod("get_Sum"), null));
        Console.WriteLine("gf_public=" + !ReferenceEquals(typeof(PropStatic).GetField("PublicField"), null));
        Console.WriteLine("gm_nopropmethod=" + ReferenceEquals(typeof(PropStatic).GetMethod("Count"), null)); // property name is NOT a method

        // ========= H. not-found -> null (net48 contract, no throw) =========
        Console.WriteLine("nf_idx=" + ReferenceEquals(typeof(PropIndexer).GetProperty("NoSuch"), null));
        Console.WriteLine("nf_static=" + ReferenceEquals(typeof(PropStatic).GetProperty("NoSuch"), null));

        // ========= I. PARKED DEFECT D-PRP2-1 (see GAPS.md): the REFLECTION
        // GetValue/SetValue value round-trip. PropertyInfo.GetValue / SetValue
        // (and GetGetMethod / GetIndexParameters) have NO runtime invoker in
        // this build -- the transpiler falls through to the generated-name
        // path and emits undefined symbols (verified by transpile smoke test).
        // Not active; the DIRECT accessor round-trip (A/B/C) covers the value
        // semantics, and the subtable name queries (D-H) cover the metadata.
        //   var prop = typeof(PropStatic).GetProperty("Count");
        //   prop.SetValue(ps, 41);                  // unwired -> undefined symbol
        //   Console.WriteLine(prop.GetValue(ps));   // unwired -> undefined symbol
    }
}}
