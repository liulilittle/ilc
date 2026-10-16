//------------------------------------------------------------------------------
// p-refl4-2gti.cs -- refl4 item 2 net48 oracle probe: System.Reflection.
// IntrospectionExtensions.GetTypeInfo(Type) -- the fold-identity. net48
// returns a System.Reflection.TypeInfo (a Type SUBCLASS), so every Type
// member (.Name/.FullName/.IsClass/.BaseType/...) on the result resolves with
// normal Type semantics. The simplified Type model carries a Type VALUE as the
// full-name string; mapping GetTypeInfo to the RECEIVER (identity) makes the
// downstream Type domain work without any new value type.
//
// Probes (byte-stable, net48 Type semantics):
//   * GetTypeInfo().Name  -- the SHORT name (Type.get_Name truncation).
//   * GetTypeInfo().FullName -- the full name (identity -- Type VALUE).
//   * GetTypeInfo().IsClass / IsValueType -- the Type-domain booleans.
//   * GetTypeInfo().BaseType -- the base chain walk.
//   * GetTypeInfo() == typeof(X)  -- Type op_Equality (string equality).
//   * GetTypeInfo().GetProperty("...") -- a Type-domain member query on the
//     TypeInfo result (registry receiver = the name string).
//   * GetTypeInfo() on typeof(int) -- a BCL primitive (no typeinfo registry
//     entry; Name=Int32).
// None of the TypeInfo-only Declared*/GetDeclared* members is probed (they
// must fail LOUD at transpile -- net48 has them, the simplified model does
// not; the batch marks them explicit NotSupportedException).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class GtiBase { public int BaseField; }
    public class GtiDerived : GtiBase { public int PropG { get; set; } }

    public enum GtiEnum { A, B }

    public static class PRefl4Gti
    {
        public static void Main()
        {
            var ti = typeof(GtiDerived).GetTypeInfo();
            Console.WriteLine("[gti.name]=" + (ti.Name == "GtiDerived"));
            Console.WriteLine("[gti.fn]=" + (ti.FullName == "Cases.GtiDerived"));
            Console.WriteLine("[gti.isclass]=" + ti.IsClass);
            Console.WriteLine("[gti.isvaltype]=" + ti.IsValueType);
            Console.WriteLine("[gti.base]=" + (ti.BaseType == typeof(GtiBase)));
            Console.WriteLine("[gti.ident]=" + (ti == typeof(GtiDerived)));
            Console.WriteLine("[gti.ne]=" + (!(ti == typeof(int))));
            Console.WriteLine("[gti.prop]=" + (ti.GetProperty("PropG") != null));

            var tiEnum = typeof(GtiEnum).GetTypeInfo();
            Console.WriteLine("[gti.enum.name]=" + (tiEnum.Name == "GtiEnum"));
            Console.WriteLine("[gti.enum.isclass]=" + tiEnum.IsClass);
            Console.WriteLine("[gti.enum.isval]=" + tiEnum.IsValueType);

            var tiInt = typeof(int).GetTypeInfo();
            Console.WriteLine("[gti.int.name]=" + (tiInt.Name == "Int32"));
            Console.WriteLine("[gti.int.ident]=" + (tiInt == typeof(int)));
        }
    }
}