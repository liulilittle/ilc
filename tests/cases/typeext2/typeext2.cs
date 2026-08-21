//------------------------------------------------------------------------------
// typeext2.cs -- System.Type DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C44. Goes DEEPER than
// typeops/typechain/reflect2 on the wired Type domain (MethodMap.Type.cs /
// MethodMap.Reflect.cs / MethodMap.Object.cs + typeext.cpp / typecheck.cpp /
// reflect_base.cpp), focused on GetType / type comparison / typeinfo queries.
//
//   1) GetType() instance chain -- boxed primitives (int/long/double/bool/
//      char), string, user classes -> icall_object_get_type full name +
//      MemberInfo::get_Name short-name truncation (il2cpp_type_get_short_name).
//   2) typeof folding -> .Name short names (user class, primitive).
//   3) Type comparison -- op_Equality (`==`) on folded types (string content
//      equality, net48 RuntimeType reference semantics reduce to name
//      equality), ReferenceEquals identity, GetHashCode CONSISTENCY (same
//      type -> same hash, different types -> different hashes; never the raw
//      hash values -- the runtime string-hash differs from net48's
//      per-RuntimeType hash).
//   4) Type.GetType(string) -- USER types resolve (calling-assembly /
//      typeinfo registry), BCL "System.Int32" resolves (class_by_name BCL
//      fallback), no-such -> null (.NET contract, no throw).
//   5) GetTypeCode static fold (int -> Int32, user class -> Object).
//   6) TypeInfo table queries (MethodMap.Type.cs -> il2cpp_typeinfo_*):
//      GetMethod/GetField hit + miss -> null on a registered property-free
//      user type; GetProperty on a property-free type -> null (no-property
//      miss shape).
//
// RECORDED DEFECTS (D-TYPEX-1..5, NOT probed -- 缺陷记录不修):
//   * D-TYPEX-1 Type.IsInstanceOfType(Type) -- NO MethodMap case and no
//     runtime is-instance-of-type icall (only the isinst/`is` operator path
//     through il2cpp_is_instance / icall_type_is_instance). NOT CALLED.
//   * D-TYPEX-2 Type.IsAssignableFrom -- wired to il2cpp_type_is_assignable_
//     from but the folded typeof VALUE is a name STRING, and the runtime
//     primitive expects Il2CppClass* descriptors -- the emitted C++ fails to
//     compile ("cannot initialize a parameter of type 'Il2CppClass *' with
//     an lvalue of type 'Il2CppObject *'"). NOT CALLED.
//   * D-TYPEX-3 Type.op_Inequality (`!=`) -- no case in MethodMap.Type.cs
//     (only op_Equality); emits an undefined System_Type_op_Inequality_<hash>
//     symbol. NOT CALLED (negation `!(a == b)` is used instead).
//   * D-TYPEX-4 Type.GetElementType / array-typed GetType().Name -- the
//     array descriptor name is the "[<elem>" form (typeext.cpp) and the
//     GetType().Name short-name truncation for arrays is not pinned; net48
//     prints "Int32[]". NOT CALLED.
//   * D-TYPEX-5 Interface-typed GetType()/typeof -- a class implementing a
//     member-less interface emits an EMPTY interface-slot map
//     (Cases_Impl_iface_*_map[] = {}), which is a ZERO-SIZE array that MSVC
//     rejects (C2466) while MinGW accepts (boxdeep's DeepImpl has the same
//     shape and fails the msvc leg). NOT CALLED here.
//
// Byte-stability: every line prints "label=True/False" (byte-stable across
// console encodings). No raw hash values, no unwired members.
// net48 is the reference baseline; every line must be byte-identical.
//------------------------------------------------------------------------------
using System;

namespace Cases {
class Base2 { }
class Der2 : Base2 { }
class PropFree2 { public int FieldA; public int Add(int a, int b) { return a + b; } }

public static class TypeExt2 {
    public static void Main() {
        // ---- 1. GetType() instance chain -> short names ----
        Console.WriteLine("gettype_der=" + (new Der2().GetType().Name == "Der2"));
        Console.WriteLine("gettype_int=" + ((42).GetType().Name == "Int32"));
        Console.WriteLine("gettype_long=" + ((42L).GetType().Name == "Int64"));
        Console.WriteLine("gettype_double=" + ((3.14).GetType().Name == "Double"));
        Console.WriteLine("gettype_bool=" + ((true).GetType().Name == "Boolean"));
        Console.WriteLine("gettype_char=" + (('A').GetType().Name == "Char"));
        Console.WriteLine("gettype_str=" + ("s".GetType().Name == "String"));

        // ---- 2. typeof folding -> .Name short names ----
        Console.WriteLine("typeof_der_name=" + (typeof(Der2).Name == "Der2"));
        Console.WriteLine("typeof_int=" + (typeof(int).Name == "Int32"));

        // ---- 3. type comparison ----
        Console.WriteLine("typeof_eq_same=" + (typeof(Der2) == typeof(Der2)));
        Console.WriteLine("typeof_eq_diff=" + (!(typeof(Der2) == typeof(Base2))));
        Console.WriteLine("typeof_eq_int=" + (typeof(int) == typeof(int)));
        Console.WriteLine("refeq_same=" + object.ReferenceEquals(typeof(Der2), typeof(Der2)));
        Console.WriteLine("refeq_diff=" + (!object.ReferenceEquals(typeof(Der2), typeof(Base2))));
        Console.WriteLine("hc_consist=" + (typeof(Der2).GetHashCode() == typeof(Der2).GetHashCode()));
        Console.WriteLine("hc_diff=" + (!(typeof(Der2).GetHashCode() == typeof(Base2).GetHashCode())));

        // ---- 4. Type.GetType(string) ----
        Console.WriteLine("gettype_str_type=" + (!ReferenceEquals(Type.GetType("Cases.TypeExt2"), null)));
        Console.WriteLine("gettype_bcl=" + (!ReferenceEquals(Type.GetType("System.Int32"), null)));
        Console.WriteLine("gettype_none=" + (ReferenceEquals(Type.GetType("Cases.NoSuch"), null)));

        // ---- 5. GetTypeCode static fold ----
        Console.WriteLine("typecode_int=" + (Type.GetTypeCode(typeof(int)) == TypeCode.Int32));
        Console.WriteLine("typecode_obj=" + (Type.GetTypeCode(typeof(Der2)) == TypeCode.Object));

        // ---- 6. TypeInfo table queries ----
        Console.WriteLine("getmethod_hit=" + (!ReferenceEquals(typeof(PropFree2).GetMethod("Add"), null)));
        Console.WriteLine("getmethod_miss=" + (ReferenceEquals(typeof(PropFree2).GetMethod("NoSuch"), null)));
        Console.WriteLine("getfield_hit=" + (!ReferenceEquals(typeof(PropFree2).GetField("FieldA"), null)));
        Console.WriteLine("getfield_miss=" + (ReferenceEquals(typeof(PropFree2).GetField("NoField"), null)));
        Console.WriteLine("getprop_miss=" + (ReferenceEquals(typeof(PropFree2).GetProperty("NoProp"), null)));
    }
}}
