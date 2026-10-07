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
    // raudit3 (2026-09-13): reflection MEMBER object == / != (the base
    // MemberInfo operator -- csc emits it for every derived member type).
    // In the simplified reflection model a member VALUE is its name string,
    // so reference equality reduces to ordinal string equality; both helpers
    // are null-safe (null == null -> True, exactly-one-null -> False), the
    // net48 ReferenceEquals doctrine for the == null / != null probes.
    //
    // R2 (B2): FieldInfo.GetValue/SetValue (the narrow int32/int64/string/
    // object set -- slot width dispatched by the field's typeName in the
    // runtime). The receiver is the FIELD-NAME string (the member VALUE, as
    // il2cpp_typeinfo_get_field returns); the runtime joins the declaring
    // type from the TARGET object's klass (typeinfo_lookup_by_class) and
    // walks own + base chain. GetValue returns the boxed value as a ref;
    // SetValue writes through the box payload / reference slot.
    private static ResolvedMethod? TryMapReflectionMember(string owner, string name, byte[] ps)
    {
        switch (name)
        {
            case "op_Equality":
                return ps.Length == 2
                    ? I("il2cpp::runtime::icall_string_equals_static", 2, true, false, false)
                    : null;
            case "op_Inequality":
                return ps.Length == 2
                    ? I("il2cpp::runtime::icall_type_op_inequality", 2, true, false, false)
                    : null;
            case "GetValue":
                // FieldInfo.GetValue(object) -- instance. Only the FieldInfo
                // owner is wired (PropertyInfo.GetValue rides the getter --
                // not in the R2 narrow set; MethodInfo.GetValue is R3's
                // il2cpp_methodinfo_invoke).
                if (owner == "System.Reflection.FieldInfo" && ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::il2cpp_typeinfo_field_get_value", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.Object");
                throw new NotSupportedException(
                    $"{owner}::GetValue with {ps.Length} parameter(s) not wired in R2 -- only FieldInfo.GetValue(object) maps to il2cpp_typeinfo_field_get_value");
            case "SetValue":
                // FieldInfo.SetValue(object obj, object value) -- instance.
                if (owner == "System.Reflection.FieldInfo" && ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x1C)
                    return I("il2cpp::il2cpp_typeinfo_field_set_value", 2, true, false, true,
                        new[] { "", "", "" });
                throw new NotSupportedException(
                    $"{owner}::SetValue with {ps.Length} parameter(s) not wired in R2 -- only FieldInfo.SetValue(object, object) maps to il2cpp_typeinfo_field_set_value");
            case "Invoke":
                // MethodBase.Invoke(object obj, object[] parameters) / the
                // MethodInfo override -- R3. net48 csc emits the BASE
                // MethodBase::Invoke memberref (ILDASM-verified), so BOTH
                // owners map to the same icall. The receiver is the METHOD-NAME
                // string (the simplified member model); the runtime joins the
                // declaring type from the target (klass join) and dispatches
                // through il2cpp_invoker_invoke (name + paramCount
                // disambiguation, ambiguity LOUD, no object creation). The
                // other MemberInfo-derived Invoke arities stay LOUD.
                if ((owner == "System.Reflection.MethodInfo" || owner == "System.Reflection.MethodBase")
                    && ps.Length == 2 && ps[0] == 0x1C && ps[1] is 0x1D or 0x12)
                    return I("il2cpp::il2cpp_methodinfo_invoke", 2, true, true, true,
                        new[] { "", "", "a" }, returnTypeName: "System.Object",
                        argOrder: new[] { 0, 1, 2 });
                throw new NotSupportedException(
                    $"{owner}::Invoke with {ps.Length} parameter(s) not wired in R3 -- only MethodBase/MethodInfo.Invoke(object, object[]) maps to il2cpp_methodinfo_invoke");
            // C3-2 (COM-C3): EventInfo.AddEventHandler(object, Delegate) /
            // EventInfo.RemoveEventHandler(object, Delegate) -- instance. The
            // receiver is the EVENT-NAME string (the simplified member model,
            // as il2cpp_typeinfo_get_event returns); the runtime joins the
            // declaring type from the TARGET's klass and dispatches the event
            // table's add/remove ACCESSOR on the target with the handler (the
            // net48 managed-event delivery -- the C3 genevt fake-source
            // self-loop's green path). An RCW target doors the deferred
            // shape-A delegate-sink bridging with a clear runtime LOUD.
            // net48 IL signatures: (object target, System.Delegate handler) --
            // ps[0] == 0x1C (OBJECT), ps[1] == 0x12 (CLASS System.Delegate).
            case "AddEventHandler":
                if (owner == "System.Reflection.EventInfo" && ps.Length == 2
                    && ps[0] == 0x1C && ps[1] == 0x12)
                    return I("il2cpp::il2cpp_eventinfo_add", 2, true, false, true,
                        new[] { "", "", "" });
                throw new NotSupportedException(
                    $"{owner}::AddEventHandler with {ps.Length} parameter(s) not wired in C3-2 -- only EventInfo.AddEventHandler(object, Delegate) maps to il2cpp_eventinfo_add");
            case "RemoveEventHandler":
                if (owner == "System.Reflection.EventInfo" && ps.Length == 2
                    && ps[0] == 0x1C && ps[1] == 0x12)
                    return I("il2cpp::il2cpp_eventinfo_remove", 2, true, false, true,
                        new[] { "", "", "" });
                throw new NotSupportedException(
                    $"{owner}::RemoveEventHandler with {ps.Length} parameter(s) not wired in C3-2 -- only EventInfo.RemoveEventHandler(object, Delegate) maps to il2cpp_eventinfo_remove");
            default:
                return null;
        }
    }

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
