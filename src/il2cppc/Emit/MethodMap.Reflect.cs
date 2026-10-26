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
                // RF-N2: PropertyInfo.GetValue -- the receiver is the
                // PROPERTY-NAME string (the simplified member model); the
                // runtime joins the declaring type from the TARGET's klass,
                // resolves the property table entry and dispatches the
                // GETTER accessor (getterName) through the same invoker as
                // MethodInfo.Invoke (il2cpp_methodinfo_invoke -- RF-1
                // instance-'this' injection). net48 csc emits GetValue(object)
                // for the common non-indexed shape (ildasm-verified) and
                // GetValue(object, object[]) for the indexed form; both map to
                // the same 3-slot icall (the 1-param form appends a null index
                // -- the non-indexed property's zero-arg getter). FieldInfo
                // keeps its R2 narrow-set mapping.
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::il2cpp_typeinfo_property_get_value", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.Object",
                        appendLiterals: new[] { "nullptr" });
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x1D)
                    return I("il2cpp::il2cpp_typeinfo_property_get_value", 2, true, true, true,
                        new[] { "", "", "a" }, returnTypeName: "System.Object");
                if (owner == "System.Reflection.FieldInfo" && ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::il2cpp_typeinfo_field_get_value", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.Object");
                throw new NotSupportedException(
                    $"{owner}::GetValue with {ps.Length} parameter(s) not wired in RF-N2 -- FieldInfo.GetValue(object) maps to il2cpp_typeinfo_field_get_value; PropertyInfo.GetValue(object) / GetValue(object, object[]) map to il2cpp_typeinfo_property_get_value");
            case "SetValue":
                // RF-N2: PropertyInfo.SetValue -- the receiver is the
                // PROPERTY-NAME string; the runtime resolves the property
                // table entry (own + base chain, from the TARGET's klass) and
                // dispatches the SETTER accessor (setterName) with
                // [index..., value] through il2cpp_methodinfo_invoke (RF-1
                // instance-'this'). net48 csc emits SetValue(object, object)
                // for the common non-indexed shape (ildasm-verified) and
                // SetValue(object, object, object[]) for the indexed form;
                // both map to the same 4-slot icall (the 2-param form appends
                // a null index). FieldInfo keeps its R2 narrow-set mapping.
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x1C)
                    return I("il2cpp::il2cpp_typeinfo_property_set_value", 2, true, false, true,
                        new[] { "", "", "" }, appendLiterals: new[] { "nullptr" });
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 3 && ps[0] == 0x1C && ps[1] == 0x1C && ps[2] == 0x1D)
                    return I("il2cpp::il2cpp_typeinfo_property_set_value", 3, true, false, true,
                        new[] { "", "", "", "a" });
                if (owner == "System.Reflection.FieldInfo" && ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x1C)
                    return I("il2cpp::il2cpp_typeinfo_field_set_value", 2, true, false, true,
                        new[] { "", "", "" });
                // REFL-ARITY: PropertyInfo.SetValue(object, object,
                // BindingFlags, Binder, object[], CultureInfo) / FieldInfo.
                // SetValue(object, object, BindingFlags, Binder, CultureInfo) --
                // net48's 6/5-param forms (reflection_propertyinfo.cs:84 /
                // reflection_fieldinfo.cs:134). The BindingFlags/Binder/
                // CultureInfo are dropped (this AOT build models no binder --
                // the same doctrine the Type.GetMethod binder-arity arms use);
                // the target/value/index slots keep their positions.
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 6
                    && ps[0] == 0x1C && ps[1] == 0x1C && ps[2] == 0x11 && ps[3] == 0x12 && ps[4] == 0x1D && ps[5] == 0x12)
                    return I("il2cpp::il2cpp_typeinfo_property_set_value", 6, true, false, true,
                        new[] { "", "", "", "a" }, argOrder: new[] { 0, 1, 2, 4 });
                if (owner == "System.Reflection.FieldInfo" && ps.Length == 5
                    && ps[0] == 0x1C && ps[1] == 0x1C && ps[2] == 0x11 && ps[3] == 0x12 && ps[4] == 0x12)
                    return I("il2cpp::il2cpp_typeinfo_field_set_value", 5, true, false, true,
                        new[] { "", "", "" }, argOrder: new[] { 0, 1, 2 });
                throw new NotSupportedException(
                    $"{owner}::SetValue with {ps.Length} parameter(s) not wired in RF-N2/refl-arity -- FieldInfo.SetValue(object, object) / (object, object, BindingFlags, Binder, CultureInfo) map to il2cpp_typeinfo_field_set_value; PropertyInfo.SetValue(object, object) / SetValue(object, object, object[]) / (object, object, BindingFlags, Binder, object[], CultureInfo) map to il2cpp_typeinfo_property_set_value");
            // GAP-3 (P1): PropertyInfo.GetGetMethod()/GetSetMethod() -- the
            // accessor-surface unfreeze (reflprop2 D-PRP2-1's parked group).
            // The receiver is the PROPERTY-NAME string (the simplified member
            // model); the runtime resolves the declaring type by registry scan
            // (deepest type declaring the property) and returns the PUBLIC
            // accessor METHOD-NAME string (a MethodInfo VALUE) or null.
            // dir is a compile-time constant: 0 = GetGetMethod, 1 = GetSetMethod.
            case "GetGetMethod":
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_property_get_accessor", 0, true, true, true,
                        new[] { "" }, appendLiterals: new[] { "0" }, returnTypeName: "System.Reflection.MethodInfo");
                // REFL-ARITY: PropertyInfo.GetGetMethod(bool nonPublic) --
                // net48's arity-1 form. The bool rides the int32 slot ("i");
                // dir stays the appended literal (0 = getter). The runtime
                // admits a non-public accessor when nonPublic != 0.
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 1 && ps[0] == 0x02)
                    return I("il2cpp::il2cpp_typeinfo_property_get_accessor_ex", 1, true, true, true,
                        new[] { "", "i" }, appendLiterals: new[] { "0" }, returnTypeName: "System.Reflection.MethodInfo");
                throw new NotSupportedException(
                    $"{owner}::GetGetMethod with {ps.Length} parameter(s) not wired in P1/refl-arity -- the () overload maps to il2cpp_typeinfo_property_get_accessor(_, 0); the (bool nonPublic) overload to il2cpp_typeinfo_property_get_accessor_ex(_, 0, nonPublic)");
            case "GetSetMethod":
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_property_get_accessor", 0, true, true, true,
                        new[] { "" }, appendLiterals: new[] { "1" }, returnTypeName: "System.Reflection.MethodInfo");
                // REFL-ARITY: PropertyInfo.GetSetMethod(bool nonPublic) --
                // same as GetGetMethod(bool) with dir = 1 (setter).
                if (owner == "System.Reflection.PropertyInfo" && ps.Length == 1 && ps[0] == 0x02)
                    return I("il2cpp::il2cpp_typeinfo_property_get_accessor_ex", 1, true, true, true,
                        new[] { "", "i" }, appendLiterals: new[] { "1" }, returnTypeName: "System.Reflection.MethodInfo");
                throw new NotSupportedException(
                    $"{owner}::GetSetMethod with {ps.Length} parameter(s) not wired in P1/refl-arity -- the () overload maps to il2cpp_typeinfo_property_get_accessor(_, 1); the (bool nonPublic) overload to il2cpp_typeinfo_property_get_accessor_ex(_, 1, nonPublic)");
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
                // REFL-ARITY: MethodBase.Invoke(object, BindingFlags, Binder,
                // object[], CultureInfo) -- net48's 5-param form
                // (reflection_methodinfo.cs:710). The BindingFlags/Binder/
                // CultureInfo are dropped (no binder model); the parameters
                // array keeps its slot.
                if ((owner == "System.Reflection.MethodInfo" || owner == "System.Reflection.MethodBase")
                    && ps.Length == 5 && ps[0] == 0x1C && ps[1] == 0x11 && ps[2] == 0x12 && ps[3] == 0x1D && ps[4] == 0x12)
                    return I("il2cpp::il2cpp_methodinfo_invoke", 5, true, true, true,
                        new[] { "", "", "a" }, returnTypeName: "System.Object",
                        argOrder: new[] { 0, 1, 3 });
                // GAP-5 (P1): ConstructorInfo.Invoke(object[] parameters) --
                // the 1-param ctor-invocation overload (no obj target; the
                // ctor CREATES the instance). The receiver is the ctor MEMBER
                // VALUE "<FullName>::.ctor" (as il2cpp_typeinfo_get_constructor
                // returns); the runtime parses the declaring type, allocates a
                // fresh instance and runs the ctor on it. The result is the
                // constructed object.
                if (owner == "System.Reflection.ConstructorInfo" && ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::il2cpp_constructorinfo_invoke", 1, true, true, true,
                        new[] { "", "a" }, returnTypeName: "System.Object");
                if (owner == "System.Reflection.ConstructorInfo" && ps.Length == 3)
                    throw new NotSupportedException(
                        "ConstructorInfo::Invoke(BindingFlags, Binder, object[], CultureInfo) is not wired in GAP-5 -- binders are not modeled in this AOT build");
                throw new NotSupportedException(
                    $"{owner}::Invoke with {ps.Length} parameter(s) not wired in R3/GAP-5 -- MethodBase/MethodInfo.Invoke(object, object[]) maps to il2cpp_methodinfo_invoke; ConstructorInfo.Invoke(object[]) maps to il2cpp_constructorinfo_invoke");
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
            // RF-N7 (NEXT2-DECISION section ② RF-N7): MethodInfo.MakeGenericMethod
            // -- explicit LOUD. Runtime generic-method instantiation has no AOT
            // support, so the (Type[]) overload maps to a runtime stub that
            // ALWAYS throws NotSupportedException (il2cpp_makegenericmethod_loud)
            // -- the call never silently falls to a spelling generated-member
            // name (the pre-RF-N7 phantom-symbol trap). The receiver is the
            // METHOD-NAME string; the typeArgs array rides as a ref slot.
            case "MakeGenericMethod":
                if ((owner is "System.Reflection.MethodInfo" or "System.Reflection.MethodBase")
                    && ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::il2cpp_makegenericmethod_loud", 1, true, true, true,
                        new[] { "", "a" }, returnTypeName: "System.String");
                throw new NotSupportedException(
                    $"{owner}::MakeGenericMethod with {ps.Length} parameter(s) not wired in RF-N7 -- only the (Type[]) overload maps to the LOUD stub il2cpp_makegenericmethod_loud");
            // REFL3 (item 2): MethodBase.GetParameters() (the parameterless
            // overload the MethodInfo/ConstructorInfo receivers bind). The
            // receiver is the MEMBER VALUE string (a method-name string or the
            // disambiguated ctor value "<FullName>::.ctor"); the runtime scans
            // the typeinfo registry for the declaring type and returns the
            // parameter array. ReturnTypeName "System.Reflection.ParameterInfo[]"
            // types the result so a later ldelem + ParameterInfo property call
            // re-resolves on the "System.Reflection.ParameterInfo" element type.
            case "GetParameters":
                if (owner is "System.Reflection.MethodBase" or "System.Reflection.MethodInfo"
                    or "System.Reflection.ConstructorInfo" && ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_parameters", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Reflection.ParameterInfo[]");
                throw new NotSupportedException(
                    $"{owner}::GetParameters with {ps.Length} parameter(s) not wired in REFL3 -- only the net48 () overload maps to il2cpp_typeinfo_get_parameters");
            default:
                // refl5 (item 3 / C2): member-TYPE property family + the
                // flag-derived predicates. The receiver is the member-VALUE
                // string (the simplified member model -- a member VALUE is
                // its name string). Type-VALUE results (FieldType etc.) carry
                // ReturnTypeName "System.Type" so a downstream .FullName /
                // == typeof(...) / .Name fold resolves on the Type domain;
                // the bool predicates ride the int32 (0/1) carrier.
                // (The MemberInfo base owner routes to TryMapObject, where
                // get_Name/get_MemberType/get_DeclaringType live; the
                // MemberInfo-derived owners below map the C2 family.)
                if (owner == "System.Reflection.FieldInfo" && name == "get_FieldType" && ps.Length == 0)
                    return I("il2cpp::il2cpp_fieldinfo_get_field_type", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Type");
                if (owner == "System.Reflection.PropertyInfo" && name == "get_PropertyType" && ps.Length == 0)
                    return I("il2cpp::il2cpp_propertyinfo_get_property_type", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Type");
                if ((owner is "System.Reflection.MethodInfo" or "System.Reflection.MethodBase")
                    && name == "get_ReturnType" && ps.Length == 0)
                    return I("il2cpp::il2cpp_methodinfo_get_return_type", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Type");
                if (owner == "System.Reflection.EventInfo" && name == "get_EventHandlerType" && ps.Length == 0)
                    return I("il2cpp::il2cpp_eventinfo_get_event_handler_type", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Type");
                // MemberInfo.get_ReflectedType -- declared on the MemberInfo
                // base; a MemberRef with a FieldInfo/MethodInfo/... parent
                // routes here (MemberInfo itself routes to TryMapObject).
                if (owner is "System.Reflection.FieldInfo" or "System.Reflection.PropertyInfo"
                    or "System.Reflection.MethodInfo" or "System.Reflection.MethodBase"
                    or "System.Reflection.ConstructorInfo" or "System.Reflection.EventInfo")
                    if (name == "get_ReflectedType" && ps.Length == 0)
                        return I("il2cpp::il2cpp_memberinfo_get_reflected_type", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.Type");
                if (owner == "System.Reflection.FieldInfo" && name == "get_IsAssembly" && ps.Length == 0)
                    return I("il2cpp::il2cpp_fieldinfo_is_assembly", 0, true, false, true, new[] { "" });
                // refl6b (item A): IsFamilyOrAssembly on a FIELD receiver
                // (FieldInfo.IsFamilyOrAssembly) or a METHOD receiver
                // (MethodBase.IsFamilyOrAssembly) -- net48 decodes the
                // FamORAssem(0x5) state EXACTLY (not an "non-public && non-private"
                // catch-all); the runtime checks the dedicated flag bit 0x40.
                if ((owner == "System.Reflection.FieldInfo" || owner is "System.Reflection.MethodInfo" or "System.Reflection.MethodBase")
                    && name == "get_IsFamilyOrAssembly" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_is_family_or_assembly", 0, true, false, true, new[] { "" });
                if (name == "get_IsStatic" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_is_static", 0, true, false, true, new[] { "" });
                if (name == "get_IsPublic" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_is_public", 0, true, false, true, new[] { "" });
                if (name == "get_IsConstructor" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_is_constructor", 0, true, false, true, new[] { "" });
                // p-refl2-gapmatrix (B05): MemberInfo.get_MetadataToken on a
                // DERIVED member receiver (MethodInfo/FieldInfo/PropertyInfo/
                // ConstructorInfo/EventInfo -- the MemberRef parent is usually
                // the base System.Reflection.MemberInfo, which routes to
                // TryMapObject; this arm covers the derived-parent form). The
                // receiver is the member-VALUE string; the runtime resolves
                // the deepest declarer and answers the transpiler-baked ECMA
                // MethodDef/FieldDef token (typeinfo.h refl2 metadataToken).
                if (name == "get_MetadataToken" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_get_metadata_token", 0, true, false, true,
                        new[] { "" });
                // p-refl2-gapmatrix (B07/B08/B11): the MethodBase/MethodInfo
                // flag predicates. The receiver is the member-VALUE string; the
                // runtime resolves the method (own + base chain) and decodes the
                // dedicated flag bits (virtual=0x8, abstract=0x80, generic=0x100).
                if (name == "get_IsVirtual" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_is_virtual", 0, true, false, true, new[] { "" });
                if (name == "get_IsAbstract" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_is_abstract", 0, true, false, true, new[] { "" });
                if (name == "get_IsGenericMethod" && ps.Length == 0)
                    return I("il2cpp::il2cpp_memberinfo_is_generic_method", 0, true, false, true, new[] { "" });
                // p-refl2-gapmatrix (B17/B18): the FieldInfo flag predicates
                // (IsInitOnly = dedicated flag bit 0x80, IsLiteral = bit2).
                if (name == "get_IsInitOnly" && ps.Length == 0)
                    return I("il2cpp::il2cpp_fieldinfo_is_init_only", 0, true, false, true, new[] { "" });
                if (name == "get_IsLiteral" && ps.Length == 0)
                    return I("il2cpp::il2cpp_fieldinfo_is_literal", 0, true, false, true, new[] { "" });
                // p-refl2-gapmatrix (E03/E04): MethodBase.MethodHandle /
                // FieldInfo.FieldHandle -- the raw handle IntPtr (the resolved
                // table entry address, reflecthandle.h's handle-value model).
                // The result is a VALUE (intptr_t) -- "ptr" carrier, no ref.
                if (name == "get_MethodHandle" && ps.Length == 0)
                    return I("il2cpp::il2cpp_methodinfo_get_method_handle", 0, true, false, true,
                        new[] { "" }, "ptr");
                if (name == "get_FieldHandle" && ps.Length == 0)
                    return I("il2cpp::il2cpp_fieldinfo_get_field_handle", 0, true, false, true,
                        new[] { "" }, "ptr");
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
                // p-refl2-gapmatrix (C02/C03): the two remaining reachable
                // (Type, ...) overloads. (Type, params object[]) resolves the
                // ctor by the ARG COUNT (the args array rides the "a" ref
                // slot); (Type, bool nonPublic) ignores the flag (net48
                // default binding is public-only) and reuses the (Type) icall.
                if (ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x1D)
                    return I("il2cpp::il2cpp_activator_create_instance_args", 2, true, true, false,
                        new[] { "", "a" });
                // REFL-ARITY: CreateInstance(Type, object[], object[]
                // activationAttributes) -- net48's 3-param form. A null/empty
                // activationAttributes behaves like the 2-param form; a non-empty
                // one throws NotSupportedException (net48, non-MBR).
                if (ps.Length == 3 && ps[0] == 0x12 && ps[1] == 0x1D && ps[2] == 0x1D)
                    return I("il2cpp::il2cpp_activator_create_instance_args_attrs", 3, true, true, false,
                        new[] { "", "a", "a" });
                if (ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x02)
                    return I("il2cpp::il2cpp_activator_create_instance", 2, true, true, false,
                        new[] { "", "drop" });
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
                // RF-REFL: Type.GetType(string typeName, bool throwOnError).
                // Same registry query as the 1-arg overload; the runtime
                // wrapper appends ignoreCase = 0 (the 2-arg overload has no
                // case-insensitive form). throwOnError controls whether a
                // well-formed but unregistered name throws TypeLoadException
                // or returns null (net48). The bool rides the int32 carrier.
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x02)
                    return I("il2cpp::il2cpp_type_get_type_ex", 2, true, true, false,
                        new[] { "", "i" }, returnTypeName: "System.Type",
                        appendLiterals: new[] { "0" });
                // RF-REFL: Type.GetType(string typeName, bool throwOnError,
                // bool ignoreCase). Both bools ride int32 carriers; the
                // runtime folds ASCII case for the registry key comparison
                // when ignoreCase != 0.
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x02 && ps[2] == 0x02)
                    return I("il2cpp::il2cpp_type_get_type_ex", 3, true, true, false,
                        new[] { "", "i", "i" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetType with {ps.Length} parameter(s) not wired in RF-REFL -- the (string typeName) / (string, bool throwOnError) / (string, bool throwOnError, bool ignoreCase) overloads map to il2cpp_type_get_type(_ex)");
            default:
                return null;
        }
    }
    //--------------------------------------------------------------------------
    // REFL3 (item 2): ParameterInfo property surface. The receiver is the
    // ParameterInfo VALUE string ("name|typeFullName|position|flags") that
    // il2cpp_typeinfo_get_parameters returns; the property arms decode it.
    //   get_Name          -> the name part (a managed string)
    //   get_ParameterType -> the typeFullName part -- a TYPE VALUE (full-name
    //                        string); ReturnTypeName "System.Type" keeps the
    //                        downstream .Name/.FullName/.IsArray/.GetElementType
    //                        /equality folds resolving on the Type domain.
    //   get_Position      -> the position int (int32)
    //   get_IsOut / IsOut -> the out flag (bool carrier)
    // All are instance (inst=true), zero explicit args -- the VALUE rides as
    // the receiver. Any malformed/unknown member stays LOUD.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapParameterInfo(string owner, string name, byte[] ps, int count)
    {
        switch (name)
        {
            case "get_Name":
                if (count == 0)
                    return I("il2cpp::il2cpp_parameterinfo_get_name", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"{owner}::get_Name with {count} parameter(s) not wired in REFL3");
            case "get_ParameterType":
                if (count == 0)
                    return I("il2cpp::il2cpp_parameterinfo_get_parameter_type", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"{owner}::get_ParameterType with {count} parameter(s) not wired in REFL3");
            case "get_Position":
                if (count == 0)
                    return I("il2cpp::il2cpp_parameterinfo_get_position", 0, true, false, true,
                        new[] { "" });
                throw new NotSupportedException($"{owner}::get_Position with {count} parameter(s) not wired in REFL3");
            case "get_IsOut":
            case "IsOut":
                if (count == 0)
                    return I("il2cpp::il2cpp_parameterinfo_is_out", 0, true, false, true,
                        new[] { "" });
                throw new NotSupportedException($"{owner}::{name} with {count} parameter(s) not wired in REFL3");
            default:
                return null;
        }
    }
}
