namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Type -- System.Type icall mappings (il2cpp_typeext.h).
// Owners: System.Type
//
// Simplified Type model (il2cpp_typeext.h): a Type VALUE is the full type
// NAME as a managed string -- the same shape icall_object_get_type produces
// for GetType().ToString(). The typeext primitives CONSUME runtime
// descriptors (Il2CppClass* -- the address of the static {Type}_class
// descriptor the transpiler emits per translated type), the transpiler
// passes the descriptor where the IL holds a RuntimeTypeHandle/typeof
// value. GetTypeFromHandle converts a descriptor to the name string; the
// result is recorded as "System.Type" (ReturnTypeName -> the fold's
// objTypes) so later get_Name/GetTypeCode/Equals/IsAssignableFrom calls on
// it resolve -- the GetType()-linkage the fold already uses.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapType(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // Type.GetTypeFromHandle(RuntimeTypeHandle) -- static. The handle
            // IS the runtime descriptor address; the icall returns the type
            // NAME string (managed ref). ReturnTypeName records the result as
            // "System.Type" (same tracked shape as icall_object_get_type), so
            // .Name/.GetTypeCode/.Equals/.IsAssignableFrom on the result
            // resolve through this domain (the fold records it in objTypes).
            case "GetTypeFromHandle":
                return I("il2cpp::runtime::il2cpp_type_get_type_from_handle", 1, true, true, false,
                    new[] { "" }, returnTypeName: "System.Type");
            // Type.Name -- TWO paths (the Type-value model split):
            //   * RUNTIME receivers (a GetType() result -- the FULL-name
            //     string the runtime icall produces from klass->name, e.g.
            //     "System.Int32"): the runtime truncation primitive
            //     (il2cpp_type_get_short_name: "System.Int32" -> "Int32" --
            //     net48 Type.Name semantics). The old identity was wrong
            //     here: the runtime value is the FULL name and .NET Name is
            //     the SHORT name (the boxing2/boxdeep GetType mismatch).
            //   * FOLDED receivers (typeof(X) -- the full-name string the
            //     ldtoken fold produces): StackFold intercepts get_Name
            //     BEFORE this mapping and truncates to the SHORT name
            //     (net48: typeof(int).Name == "Int32").
            case "get_Name":
                return I("il2cpp::runtime::il2cpp_type_get_short_name", 0, true, true, true, returnTypeName: "System.String");
            // Type.GetTypeCode(Type) -- STATIC overload: int32 TypeCode from
            // the descriptor's name (net48 values, identical table to
            // icall_object_get_type_code). TWO paths:
            //   * FOLDED argument (typeof(X)): StackFold intercepts and
            //     folds the full-name string to the typecode CONSTANT
            //     (no call emitted -- the runtime primitive takes a
            //     DESCRIPTOR, not the name string).
            //   * RUNTIME argument: the descriptor-ABI icall is emitted
            //     (the name-string receiver does not compile -- the Type
            //     domain's documented runtime-path limitation).
            case "GetTypeCode":
                if (ps.Length == 1)
                    return I("il2cpp::runtime::il2cpp_type_get_typecode", 1, true, false, false, new[] { "" });
                // INSTANCE GetTypeCode() (IConvertible impl): the receiver is
                // the name-string Type value -- the runtime has no
                // string->TypeCode path (the static primitive takes a
                // descriptor); unwireable, must be LOUD.
                throw new NotSupportedException("Type.GetTypeCode() (instance) not wired in M5 -- the simplified Type model has no name-string to TypeCode path (static Type.GetTypeCode(Type) is wired)");
            // Type.IsAssignableFrom(Type c) -- instance: target = this,
            // source = c (runtime conformance model: identity, array prefix,
            // baseClass chain, interface scan).
            case "IsAssignableFrom":
                return I("il2cpp::runtime::il2cpp_type_is_assignable_from", 1, true, false, true,
                    new[] { "", "" });
            // Type.Equals(object o) -- instance type identity (icall_type_equals:
            // descriptor identity fast path, else name equality). Wired only
            // for a TRACKED Type argument -- a non-Type argument cannot be
            // passed to the descriptor-taking icall (no .NET "false for
            // non-Type" support in the runtime).
            case "Equals":
                if (ps.Length == 1 && arg0Type == "System.Type")
                    return I("il2cpp::runtime::icall_type_equals", 1, true, false, true, new[] { "", "" });
                throw new NotSupportedException($"Type.Equals(object) with {(arg0Type == null ? "untracked" : $"non-Type argument {arg0Type}")} not wired in M5 -- only a tracked System.Type argument maps to icall_type_equals");
            // Type.GetProperty/GetMethod/GetField(string name) -- M5.5
            // typeinfo queries (see typeinfo.h managed-string ABI). The
            // receiver is the Type VALUE (the full-name string, same shape
            // as GetTypeFromHandle produces); the runtime resolves the type
            // through the typeinfo registry, scans the member table (own
            // table first, baseType chain second) and returns the MEMBER-NAME
            // string on a hit -- the simplified member model (the member
            // VALUE is its name string, like the Type model) -- or null
            // when the type is not reflected / the member is absent (.NET
            // "not found -> null" contract, no throw). ReturnTypeName
            // "System.String" keeps WriteLine(object)/Concat linkage working
            // on the result. Only the (string) overload is wired; the
            // BindingFlags overloads have no runtime support in M5.5.
            case "GetProperty":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_property", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetProperty with {ps.Length} parameter(s) not wired in M5.5 -- only the (string) overload maps to il2cpp_typeinfo_get_property");
            case "GetMethod":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_method", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetMethod with {ps.Length} parameter(s) not wired in M5.5 -- only the (string) overload maps to il2cpp_typeinfo_get_method");
            case "GetField":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_field", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetField with {ps.Length} parameter(s) not wired in M5.5 -- only the (string) overload maps to il2cpp_typeinfo_get_field");
            // R2 (B2): member ENUMERATION -- GetMethods()/GetFields()/
            // GetProperties()/GetConstructors()/GetInterfaces(). Each maps to
            // a runtime icall that materialises an Il2CppArray* of member-NAME
            // strings (the simplified member model -- a member VALUE is its
            // name string, typeinfo.h). ReturnTypeName "...[]" keeps the
            // Array-domain folds (.Length / element read / foreach) resolved.
            // Only the 0-param overloads are wired; the BindingFlags overloads
            // have no runtime support and fail LOUD.
            case "GetMethods":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_methods", 0, true, true, true,
                        returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetMethods with {ps.Length} parameter(s) not wired in R2 -- only the () overload maps to il2cpp_typeinfo_get_methods");
            case "GetFields":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_fields", 0, true, true, true,
                        returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetFields with {ps.Length} parameter(s) not wired in R2 -- only the () overload maps to il2cpp_typeinfo_get_fields");
            case "GetProperties":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_properties", 0, true, true, true,
                        returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetProperties with {ps.Length} parameter(s) not wired in R2 -- only the () overload maps to il2cpp_typeinfo_get_properties");
            case "GetConstructors":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_constructors", 0, true, true, true,
                        returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetConstructors with {ps.Length} parameter(s) not wired in R2 -- only the () overload maps to il2cpp_typeinfo_get_constructors");
            case "GetInterfaces":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_interfaces", 0, true, true, true,
                        returnTypeName: "System.Type[]");
                throw new NotSupportedException($"Type.GetInterfaces with {ps.Length} parameter(s) not wired in R2 -- only the () overload maps to il2cpp_typeinfo_get_interfaces");
            // C3-1 (COM-C3) / RF-N1: GetEvents() -- the event-table member
            // enumeration (own + base chain, ALL events -- Il2CppEventInfo has
            // no flags field, the documented simplification). The events
            // sub-table is the shared piece with the COM-C3 event metadata
            // pipeline (event name + add/remove accessors + the folded event
            // IID). ReturnTypeName "System.String[]" keeps the Array-domain
            // folds (.Length / element read / foreach) resolved.
            case "GetEvents":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_events", 0, true, true, true,
                        returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetEvents with {ps.Length} parameter(s) not wired in C3-1 -- only the () overload maps to il2cpp_typeinfo_get_events");
            // C3-1 (COM-C3): Type.GetEvent(string) -- the single-event member
            // query (own + base chain), the M5.5 get_property/get_method/get_field
            // shape: the receiver is the Type VALUE (full-name string), the
            // runtime returns the EVENT-NAME string on a hit or null (the
            // simplified member model -- a member VALUE is its name string).
            case "GetEvent":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_event", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetEvent with {ps.Length} parameter(s) not wired in C3-1 -- only the (string) overload maps to il2cpp_typeinfo_get_event");
            // Type.op_Equality(Type, Type) -- the C# == operator (csc emits
            // `call`, never callvirt; the resolver's default op_* heuristic
            // classifies it INSTANCE and the call path would throw "pops this
            // on empty stack" -- delechain's op_Equality precedent). The
            // simplified Type model (il2cpp_typeext.h) makes a Type VALUE the
            // full-name string, so equality is STRING content equality -- the
            // same icall_string_equals_static the String domain uses. net48
            // reference semantics reduce to name equality (one RuntimeType
            // instance per type): typeof(int) == typeof(int) -> "System.Int32"
            // vs "System.Int32" -> True, typeof(int) == typeof(string) ->
            // "System.Int32" vs "System.String" -> False.
            case "op_Equality":
                if (ps.Length == 2)
                    return I("il2cpp::runtime::icall_string_equals_static", 2, true, false, false);
                throw new NotSupportedException($"Type.op_Equality with {ps.Length} arg(s) not wired in M5 -- only the 2-arg (Type, Type) overload maps to string equality");
            // P6: Type.GetTypeFromHandleUnsafe (stub: returns nullptr).
            case "GetTypeFromHandleUnsafe": return I("il2cpp::runtime::icall_type_get_type_from_handle_unsafe", 1, true, true, false, new[] { "i" });
            // P6: Type.op_Inequality (stub: returns 0).
            case "op_Inequality": return I("il2cpp::runtime::icall_type_op_inequality", 2, true, false, false);
            // Type.GetElementType() -- instance: the element type of an array type
            // (e.g., int[] -> int). The receiver is the Type value (name string);
            // the runtime resolves the element type through the class descriptor.
            case "GetElementType":
                return I("il2cpp::runtime::il2cpp_type_get_element_type", 0, true, true, true, returnTypeName: "System.Type");
            // Type.FullName -- instance: the full ECMA name of the type (e.g.,
            // "System.Int32"). Contrast with get_Name (short name). The receiver
            // is the Type value (name string); the runtime returns the full name.
            case "get_FullName":
                // raudit3 (2026-09-13): the simplified Type VALUE is its
                // FULL-name string (typeof folds and il2cpp_type_get_type both
                // carry the canonical name), so FullName is the receiver
                // itself. The descriptor-taking il2cpp_type_get_full_name
                // (Il2CppClass*) cannot consume the string-model receiver --
                // raudit3 cmake: "cannot convert 'Il2CppObject*' to
                // 'Il2CppClass*'". Identity via the shared marker (the
                // CultureInfo.get_Name precedent, MethodMap.CultureInfo.cs).
                return new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true);
            // Type.IsInstanceOfType(object o) -- instance: whether the OBJECT
            // argument o is an instance of this type (the .NET isinst
            // conformance test). Descriptor channel mirror of IsAssignableFrom
            // (above): the runtime primitive icall_type_is_instance(obj, klass)
            // (typecheck.cpp) puts the object FIRST and the class descriptor
            // SECOND, while IL pushes the receiver (this Type) before the
            // object -- the per-resolution ArgOrder {1,0} swaps the IL
            // argument order into the runtime slot order (the CG7 table keys
            // icalls globally and this shape is per-resolution).
            case "IsInstanceOfType":
                return I("il2cpp::runtime::icall_type_is_instance", 1, true, false, true,
                    new[] { "", "" }, argOrder: new[] { 1, 0 });
            // Type.IsSubclassOf(Type c) -- instance: whether this type derives
            // from c. The receiver is this, the argument is c (both Type values).
            case "IsSubclassOf":
                if (ps.Length == 1 && arg0Type == "System.Type")
                    return I("il2cpp::runtime::icall_type_is_subclass_of", 1, true, false, true, new[] { "", "" });
                throw new NotSupportedException($"Type.IsSubclassOf with {(arg0Type == null ? "untracked" : $"non-Type argument {arg0Type}")} not wired -- only a tracked System.Type argument maps to icall_type_is_subclass_of");
            // A-class predicates (bool Type properties backed by existing
            // descriptor fields -- name / baseClass chain / instanceSize).
            // Each is a 0-arg instance getter returning int32 1/0.
            case "get_IsArray":
                return I("il2cpp::runtime::il2cpp_type_is_array", 0, true, false, true);
            case "get_IsEnum":
                return I("il2cpp::runtime::il2cpp_type_is_enum", 0, true, false, true);
            case "get_IsValueType":
                return I("il2cpp::runtime::il2cpp_type_is_value_type", 0, true, false, true);
            case "get_IsInterface":
                return I("il2cpp::runtime::il2cpp_type_is_interface", 0, true, false, true);
            case "get_IsClass":
                return I("il2cpp::runtime::il2cpp_type_is_class", 0, true, false, true);
            case "get_IsPrimitive":
                return I("il2cpp::runtime::il2cpp_type_is_primitive", 0, true, false, true);
            case "get_HasElementType":
                return I("il2cpp::runtime::il2cpp_type_has_element_type", 0, true, false, true);
            case "get_BaseType":
                return I("il2cpp::il2cpp_typeinfo_get_base_type", 0, true, true, true, returnTypeName: "System.Type");
            // RF-N4 -- generic-type identification (NEXT2-DECISION section ②
            // RF-N4, 裁决①三分落转译端):
            //   * get_IsGenericType -- the RUNTIME predicate, judged by the
            //     typeinfo's emitted genericArity (il2cpp_typeinfo_is_generic_type).
            //     Both the generic DEFINITION and its closed CLONES register
            //     under the SAME name key (ReflectionFullName), and the folded
            //     name of a closed instantiation IS the definition's name, so
            //     the arity check answers True for typeof(Box<>) and
            //     typeof(Box<int>) alike -- the runtime cannot (and need not)
            //     tell them apart (net48 IsGenericType is True for both).
            //   * get_IsGenericTypeDefinition -- NOT wired here: StackFold
            //     intercepts the folded typeof chain (definition -> 1, closed
            //     instance -> 0). A RUNTIME receiver (obj.GetType()) reaches
            //     this mapping -- the registry cannot distinguish a definition
            //     from its clones (same name key), so it must fail LOUD below.
            case "get_IsGenericType":
                return I("il2cpp::il2cpp_typeinfo_is_generic_type", 0, true, false, true);
            case "get_IsGenericTypeDefinition":
                throw new NotSupportedException(
                    "Type.get_IsGenericTypeDefinition on a runtime (GetType-derived) Type is not decidable: the typeinfo registry cannot distinguish a generic definition from its closed clones (both register the definition's name -- RF-N4), so StackFold must fold this on a typeof chain and this runtime path cannot be wired");
            //   * GetGenericArguments() -- the THREE-WAY runtime split
            //     (il2cpp_typeinfo_get_generic_arguments): non-generic ->
            //     EMPTY array; genericArity > 0 -> LOUD (禁伪造 -- the
            //     genericArgNames B方案 is deferred). ReturnTypeName
            //     "System.Type[]" keeps the Array-domain folds (.Length /
            //     element read) resolved.
            //   * GetGenericTypeDefinition() -- returns the DEFINITION's own
            //     name (identity -- the simplified Type model); a non-generic
            //     receiver throws InvalidOperationException (net48).
            case "GetGenericArguments":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_generic_arguments", 0, true, true, true,
                        returnTypeName: "System.Type[]");
                throw new NotSupportedException($"Type.GetGenericArguments with {ps.Length} parameter(s) not wired in RF-N4 -- only the () overload maps to il2cpp_typeinfo_get_generic_arguments");
            case "GetGenericTypeDefinition":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_generic_type_definition", 0, true, true, true,
                        returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetGenericTypeDefinition with {ps.Length} parameter(s) not wired in RF-N4 -- only the () overload maps to il2cpp_typeinfo_get_generic_type_definition");
            // C1 (A1): Type-COM creation. Type.GetTypeFromProgID(string) /
            // Type.GetTypeFromCLSID(Guid) produce the COM Type VALUE string
            // "COMCLSID:<32hex>" (il2cpp_com.h -- the simplified Type model's
            // sentinel for a COM type). ReturnTypeName "System.Type" keeps the
            // Type-domain linkage (get_Name / IsComObject / op_Equality) on
            // the result.
            case "GetTypeFromProgID":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_com_type_from_progid", 1, true, true, false,
                        new[] { "s" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetTypeFromProgID with {ps.Length} parameter(s) not wired in C1 -- only the (string) overload maps to il2cpp_com_type_from_progid");
            case "GetTypeFromCLSID":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::il2cpp_com_type_from_clsid", 1, true, true, false,
                        new[] { "g" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetTypeFromCLSID with {ps.Length} parameter(s) not wired in C1 -- only the (Guid) overload maps to il2cpp_com_type_from_clsid");
            // C2 (A2): Type.InvokeMember(string, BindingFlags, Binder, object,
            // object[]) on a COM Type -- the ONE-CALL late-bound IDispatch
            // invocation. ParamCount IS THE IL ARITY (5, the fold's stack pop
            // count); the argOrder maps the icall slots to the IR arg list
            // [receiver, name, invokeAttr, binder, target, args]: slot0 (rcw)
            // = index 4 (target), slot1 (name) = index 1, slot2 (args) = index
            // 5; the receiver and the invokeAttr/binder slots are not
            // referenced (dropped). property-put is LOUD by design (only
            // DISPATCH_METHOD + DISPATCH_PROPERTYGET). ReturnTypeName
            // "System.Object" keeps the null/IsComObject/Concat linkage.
            case "InvokeMember":
                if (ps.Length == 5 && ps[0] == 0x0E && ps[1] == 0x11 && ps[4] == 0x1D)
                    return I("il2cpp::runtime::icall_com_invoke_member", 5, true, true, true,
                        new[] { "", "s", "a" },
                        returnTypeName: "System.Object", argOrder: new[] { 4, 1, 5 });
                throw new NotSupportedException($"Type.InvokeMember with {ps.Length} parameter(s) not wired in C2 -- only the (string, BindingFlags, Binder, object, object[]) overload maps to icall_com_invoke_member");
            default: return null;
        }
    }
}
