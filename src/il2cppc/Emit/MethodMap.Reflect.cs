namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Reflect -- reflection base pair icall mappings
// (reflect_base.h; reflection-status-v2 R-REFL-07/08).
// Owners: System.Activator, System.Type (GetType)
//
//   * Activator.CreateInstance(Type type) -- STATIC: the runtime resolves
//     the type's name through the typeinfo registry and instantiates the
//     registered class descriptor (il2cpp_object_new). The single argument
//     is a Type VALUE (the type's full-name string -- the simplified Type
//     model), the exact shape the 1-param icall consumes. The RESULT is the
//     created object itself (the standard allocation path), so no
//     ReturnTypeName tracking is needed -- later member calls on it route
//     by their declaring owner like any other instance.
//     (GAP-REFL-3: the previous M5.5 mapping wired CreateInstance(string) --
//     a single-arg string overload that does NOT exist in .NET Framework
//     4.8 (mscorlib v4.0.30319 reflection dump: the net48 string overloads
//     are (string assemblyName, string typeName), (string, string, params
//     object[]) and (string, string, bool, BindingFlags, Binder, object[],
//     CultureInfo, object[])), so it was e2e-unreachable from any net48
//     input assembly. Replaced by the reachable (Type) form; the
//     net48-nonexistent (string) arity and every other arity now fail LOUD
//     naming the actual net48 signatures.)
//   * Type.GetType(string typeName) -- STATIC: registry lookup, returns the
//     type's full-name string (the simplified Type model -- a Type VALUE is
//     its name, see il2cpp_typeext.h) or null when the type is not
//     reflected (Type.GetType "not found -> null", no throw). The result is
//     recorded as "System.Type" (ReturnTypeName -- the same tracked shape
//     as GetTypeFromHandle) so later .Name/.GetTypeCode/.Equals/
//     .IsAssignableFrom/.GetProperty calls on it resolve through this
//     domain and the Type domain.
//
// Only CreateInstance(Type) is wired; every other arity has no runtime
// support and MUST fail LOUD (NotSupportedException with the "{owner}::
// {name}" prefix -- see BclResolve's wrap), naming the actual net48
// signatures.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapReflect(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // Activator.CreateInstance(Type type) -- static. The single
            // argument is a Type VALUE (the type's full-name string in the
            // simplified Type model -- the same shape il2cpp_type_get_type
            // consumes), which the icall resolves through the typeinfo
            // registry and instantiates the registered class descriptor; an
            // unregistered type throws MissingMethodException (a generated
            // new cannot be emitted without a descriptor). ps[0] == 0x12
            // (CLASS) identifies this overload; the (string) arity (0x0E)
            // does not exist in net48 (GAP-REFL-3) and fails LOUD below.
            case "CreateInstance":
                if (ps.Length == 1 && ps[0] == 0x12)
                    return I("il2cpp::il2cpp_activator_create_instance", 1, true, true, false,
                        new[] { "" });
                if (ps.Length == 1 && ps[0] == 0x0E)
                    throw new NotSupportedException(
                        "CreateInstance(string) does not exist in .NET Framework 4.8 (mscorlib v4.0.30319 reflection dump) -- the net48 string overloads are CreateInstance(string assemblyName, string typeName), CreateInstance(string, string, params object[]) and CreateInstance(string, string, bool, BindingFlags, Binder, object[], CultureInfo, object[]); the single-arg string form is .NET Core-only (removed in Core 2.0) and cannot be expressed by any net48 assembly -- only CreateInstance(Type) maps to il2cpp_activator_create_instance");
                throw new NotSupportedException(
                    $"CreateInstance with {ps.Length} parameter(s) not wired in M5.5 -- net48 mscorlib actual overloads: (Type), (Type, params object[]), (Type, object[], object[]), (Type, bool), (Type, BindingFlags, Binder, object[], CultureInfo), (Type, BindingFlags, Binder, object[], CultureInfo, object[]), (string assemblyName, string typeName), (string, string, params object[]), (string, string, bool, BindingFlags, Binder, object[], CultureInfo, object[]), () -- only (Type) maps to il2cpp_activator_create_instance");
            // Type.GetType(string typeName) -- static. The runtime resolves
            // the typeinfo registry and returns the full-name string (the
            // Type VALUE -- the input shape the Type domain consumes); null
            // when the type is not reflected (.NET GetType contract, no
            // throw). ReturnTypeName "System.Type" keeps the fold linkage:
            // .Name/.GetTypeCode/.Equals/.IsAssignableFrom/.GetProperty on
            // the result resolve (same tracked shape as GetTypeFromHandle).
            case "GetType":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_type_get_type", 1, true, true, false,
                        new[] { "" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetType with {ps.Length} parameter(s) not wired in M5.5 -- only the (string typeName) overload maps to il2cpp_type_get_type");
            default: return null;
        }
    }
}
